using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Journal;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IActionJournalStore
{
    private const int JournalRecordLimit = 4096;
    private const string JournalPlanWhere = "job_id = $job_id AND plan_sha256 = $plan AND impact_sha256 = $impact";
    private const string ApprovalProjection =
        "SELECT approval_id, plan_sha256, impact_sha256, payload_json, payload_sha256, length(CAST(payload_json AS BLOB)) FROM action_journal_approvals ";
    private const string EntryProjection =
        "SELECT entry_id, plan_sha256, impact_sha256, payload_json, payload_sha256, length(CAST(payload_json AS BLOB)), action_id, state FROM action_journal_entries ";

    public async Task<JournalWriteResult> SaveJournalPlanAsync(JournalPlanSnapshot plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfDisposed();
        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(plan.Key.JobId);
            var existing = await ReadJournalPlanAsync(connection, transaction, plan.Key, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!existing.CanonicalPlan.AsSpan().SequenceEqual(plan.CanonicalPlan.AsSpan()) ||
                    !existing.CanonicalImpact.AsSpan().SequenceEqual(plan.CanonicalImpact.AsSpan()))
                {
                    throw new JobStoreConflictException();
                }

                return;
            }

            if (!plan.HasPlanningProvenance)
            {
                throw new ArgumentException("A new journal plan requires a locally resolved proposal.", nameof(plan));
            }

            await RequireExpectedSanitizationGateAsync(connection, transaction, plan.Key.JobId, plan.GateDecisionId, token).ConfigureAwait(false);
            await RequireJournalCapacityAsync(connection, transaction, plan.Key,
                "SELECT count(*) FROM action_journal_plans WHERE job_id = $job_id;", JournalHistory.MaximumPlansPerJob, token).ConfigureAwait(false);
            await using var insert = JournalCommand(connection, transaction, plan.Key, """
                INSERT INTO action_journal_plans (job_id, plan_sha256, impact_sha256, plan_json, impact_json)
                VALUES ($job_id, $plan, $impact, $plan_json, $impact_json);
                """);
            insert.Parameters.AddWithValue("$plan_json", plan.CanonicalPlan.ToArray());
            insert.Parameters.AddWithValue("$impact_json", plan.CanonicalImpact.ToArray());
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<JournalWriteResult> AppendJournalApprovalAsync(JournalApproval approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ThrowIfDisposed();
        var payload = SerializePayload(approval, JournalRecordLimit);
        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(approval.Plan.JobId);
            var existing = await ReadApprovalAsync(connection, transaction, approval.Plan, approval.ApprovalId, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing != approval) { throw new JobStoreConflictException(); }
                return;
            }

            var plan = await RequireJournalPlanAsync(connection, transaction, approval.Plan, token).ConfigureAwait(false);
            await RequireExpectedSanitizationGateAsync(connection, transaction, plan.Key.JobId, plan.GateDecisionId, token).ConfigureAwait(false);
            var latest = await ReadApprovalAsync(connection, transaction, approval.Plan, null, token).ConfigureAwait(false);
            if (latest?.OccurredAtUtc > approval.OccurredAtUtc ||
                await HasUnreconciledJournalActionAsync(connection, transaction, approval.Plan, token).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }

            await RequireJournalCapacityAsync(connection, transaction, plan.Key,
                "SELECT count(*) FROM action_journal_approvals WHERE job_id = $job_id;", JournalHistory.MaximumApprovalsPerJob, token).ConfigureAwait(false);
            await using var insert = JournalCommand(connection, transaction, approval.Plan, """
                INSERT INTO action_journal_approvals (job_id, approval_id, plan_sha256, impact_sha256, payload_json, payload_sha256)
                VALUES ($job_id, $id, $plan, $impact, $json, $sha);
                """);
            AddJournalPayload(insert, approval.ApprovalId, payload);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<JournalWriteResult> AppendJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ThrowIfDisposed();
        var payload = SerializePayload(entry, JournalRecordLimit);
        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(entry.Plan.JobId);
            var existing = await ReadEntryAsync(connection, transaction, entry.Plan,
                EntryProjection + "WHERE job_id = $job_id AND entry_id = $id;", entry.EntryId, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing != entry) { throw new JobStoreConflictException(); }
                // Only the first committed dispatch intent grants permission to send the request.
                return;
            }

            var plan = await RequireJournalPlanAsync(connection, transaction, entry.Plan, token).ConfigureAwait(false);
            var previous = await ReadEntryAsync(connection, transaction, entry.Plan,
                EntryProjection + "WHERE " + JournalPlanWhere + " AND action_id = $id ORDER BY entry_sequence DESC LIMIT 1;",
                entry.ActionId, token).ConfigureAwait(false);
            var needsCurrentApproval = entry.State is JournalState.Approved or JournalState.Started;
            var approval = needsCurrentApproval
                ? await ReadApprovalAsync(connection, transaction, entry.Plan, null, token).ConfigureAwait(false)
                : entry.ApprovalId is { } id
                    ? await ReadApprovalAsync(connection, transaction, entry.Plan, id, token).ConfigureAwait(false)
                    : null;
            if (!JournalStateMachine.CanAppend(plan, approval, previous, entry))
            {
                throw new JobStoreConflictException();
            }

            if (needsCurrentApproval)
            {
                await RequireExpectedSanitizationGateAsync(connection, transaction, plan.Key.JobId, plan.GateDecisionId, token).ConfigureAwait(false);
            }
            else if (entry.State == JournalState.Skipped && await ReadArchiveStateAsync(connection, transaction, plan.Key.JobId, token).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }

            await RequireJournalCapacityAsync(connection, transaction, plan.Key,
                "SELECT count(*) FROM action_journal_entries WHERE job_id = $job_id;", JournalHistory.MaximumEntriesPerJob, token).ConfigureAwait(false);
            await using var insert = JournalCommand(connection, transaction, entry.Plan, """
                INSERT INTO action_journal_entries (job_id, entry_id, plan_sha256, impact_sha256, action_id, state, payload_json, payload_sha256)
                VALUES ($job_id, $id, $plan, $impact, $action, $state, $json, $sha);
                """);
            AddJournalPayload(insert, entry.EntryId, payload);
            insert.Parameters.AddWithValue("$action", entry.ActionId.ToString("D"));
            insert.Parameters.AddWithValue("$state", (int)entry.State);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<JournalHistory?> LoadJournalAsync(JournalPlanKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TranslateReadErrorsAsync(async () =>
            {
                _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                await using var transaction = _connection.BeginTransaction(deferred: true);
                var plan = await ReadJournalPlanAsync(_connection, transaction, key, cancellationToken).ConfigureAwait(false);
                var history = plan is null ? null : await ReadJournalHistoryAsync(transaction, plan, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                return history;
            }).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task<JournalHistory> ReadJournalHistoryAsync(SqliteTransaction transaction, JournalPlanSnapshot plan, CancellationToken token)
    {
        var key = plan.Key;
        _layout.ValidateJobDirectory(key.JobId);
        await using var approvalsCommand = JournalCommand(_connection, transaction, key,
            ApprovalProjection + "WHERE " + JournalPlanWhere + " ORDER BY approval_sequence;");
        var approvals = await ReadJournalRecordsAsync(approvalsCommand, key, JournalHistory.MaximumApprovalsPerJob,
            ReadApproval, token).ConfigureAwait(false);
        await using var entriesCommand = JournalCommand(_connection, transaction, key,
            EntryProjection + "WHERE " + JournalPlanWhere + " ORDER BY entry_sequence;");
        var entries = await ReadJournalRecordsAsync(entriesCommand, key, JournalHistory.MaximumEntriesPerJob,
            ReadEntry, token).ConfigureAwait(false);
        var active = await ReadApprovalAsync(_connection, transaction, key, null, token).ConfigureAwait(false);
        if (active?.Plan != key)
        {
            active = null;
        }
        else
        {
            try
            {
                await RequireExpectedSanitizationGateAsync(_connection, transaction, key.JobId, plan.GateDecisionId, token).ConfigureAwait(false);
            }
            catch (JobStoreConflictException)
            {
                active = null;
            }
        }

        try
        {
            return new JournalHistory(plan, approvals, entries, active);
        }
        catch (ArgumentException)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static async Task<JournalPlanSnapshot> RequireJournalPlanAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, CancellationToken token) =>
        await ReadJournalPlanAsync(connection, transaction, key, token).ConfigureAwait(false) ?? throw new JobStoreConflictException();

    private static async Task<JournalPlanSnapshot?> ReadJournalPlanAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, CancellationToken token)
    {
        await using var command = JournalCommand(connection, transaction, key,
            "SELECT plan_json, impact_json, length(plan_json), length(impact_json) FROM action_journal_plans WHERE " + JournalPlanWhere + ";");
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        if (reader.GetInt64(2) is < 1 or > JournalPlanSnapshot.MaximumPlanBytes ||
            reader.GetInt64(3) is < 1 or > JournalPlanSnapshot.MaximumImpactBytes)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        try
        {
            return JournalPlanSnapshot.Restore(key, reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private async Task<JournalApproval?> ReadApprovalAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, Guid? id, CancellationToken token)
    {
        var sql = id is null
            ? ApprovalProjection + "WHERE job_id = $job_id ORDER BY approval_sequence DESC LIMIT 1;"
            : ApprovalProjection + "WHERE job_id = $job_id AND approval_id = $id;";
        await using var command = JournalCommand(connection, transaction, key, sql);
        if (id is { } approvalId) { command.Parameters.AddWithValue("$id", approvalId.ToString("D")); }
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadApproval(reader, key) : null;
    }

    private async Task<JournalEntry?> ReadEntryAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, string sql, Guid id, CancellationToken token)
    {
        await using var command = JournalCommand(connection, transaction, key, sql);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadEntry(reader, key) : null;
    }

    private JournalApproval ReadApproval(SqliteDataReader reader, JournalPlanKey key)
    {
        var approval = ReadJournalPayload<JournalApproval>(reader);
        RequireJournalRow(reader, key, approval.Plan, approval.ApprovalId);
        return approval;
    }

    private JournalEntry ReadEntry(SqliteDataReader reader, JournalPlanKey key)
    {
        var entry = ReadJournalPayload<JournalEntry>(reader);
        RequireJournalRow(reader, key, entry.Plan, entry.EntryId);
        if (reader.GetString(6) != entry.ActionId.ToString("D") || reader.GetInt64(7) != (int)entry.State)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return entry;
    }

    private T ReadJournalPayload<T>(SqliteDataReader reader) where T : class
    {
        if (reader.GetInt64(5) is < 2 or > JournalRecordLimit)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return DeserializePayload<T>(reader.GetString(3), reader.GetString(4), JournalRecordLimit);
    }

    private static void RequireJournalRow(SqliteDataReader reader, JournalPlanKey requested, JournalPlanKey stored, Guid id)
    {
        if (stored.JobId != requested.JobId || reader.GetString(0) != id.ToString("D") ||
            reader.GetString(1) != stored.PlanSha256 || reader.GetString(2) != stored.ImpactSha256)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static async Task<ImmutableArray<T>> ReadJournalRecordsAsync<T>(
        SqliteCommand command, JournalPlanKey key, int maximum, Func<SqliteDataReader, JournalPlanKey, T> read, CancellationToken token)
    {
        var rows = ImmutableArray.CreateBuilder<T>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (rows.Count >= maximum) { throw new JobStoreCorruptionException("store_payload_mismatch"); }
            rows.Add(read(reader, key));
        }

        return rows.ToImmutable();
    }

    private static async Task RequireJournalCapacityAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, string sql, int maximum, CancellationToken token)
    {
        await using var command = JournalCommand(connection, transaction, key, sql);
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not long count || count >= maximum)
        {
            throw new JobStoreConflictException();
        }
    }

    private static async Task<bool> HasUnreconciledJournalActionAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, CancellationToken token)
    {
        await using var command = JournalCommand(connection, transaction, key, """
            SELECT 1 FROM action_journal_entries AS entry
            WHERE entry.job_id = $job_id AND entry.state IN ($started, $applied, $review)
              AND NOT EXISTS (
                SELECT 1 FROM action_journal_entries AS newer
                WHERE newer.job_id = entry.job_id AND newer.plan_sha256 = entry.plan_sha256
                  AND newer.impact_sha256 = entry.impact_sha256 AND newer.action_id = entry.action_id
                  AND newer.entry_sequence > entry.entry_sequence)
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$started", (int)JournalState.Started);
        command.Parameters.AddWithValue("$applied", (int)JournalState.Applied);
        command.Parameters.AddWithValue("$review", (int)JournalState.RequiresReview);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null;
    }

    private static SqliteCommand JournalCommand(SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$job_id", key.JobId.Value);
        command.Parameters.AddWithValue("$plan", key.PlanSha256);
        command.Parameters.AddWithValue("$impact", key.ImpactSha256);
        return command;
    }

    private static void AddJournalPayload(SqliteCommand command, Guid id, Payload payload)
    {
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$json", payload.Json);
        command.Parameters.AddWithValue("$sha", payload.Sha256);
    }
}
