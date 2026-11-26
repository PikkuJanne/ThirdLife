using System.Collections.Immutable;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IRebootCheckpointStore
{
    private const int MaximumRebootCheckpointsPerJob = 128;
    private const int RebootCheckpointRecordLimit = 4096;
    private const string RebootCheckpointProjection = """
        SELECT checkpoint.checkpoint_id, checkpoint.plan_sha256, checkpoint.impact_sha256,
               checkpoint.journal_entry_id, checkpoint.payload_json, checkpoint.payload_sha256,
               length(CAST(checkpoint.payload_json AS BLOB)), checkpoint.authenticity_blob,
               length(checkpoint.authenticity_blob), disposition.checkpoint_id IS NOT NULL,
               disposition.disposition, disposition.decided_at_utc, disposition.payload_json, disposition.payload_sha256,
               length(CAST(disposition.payload_json AS BLOB))
        FROM reboot_checkpoints AS checkpoint
        LEFT JOIN reboot_checkpoint_dispositions AS disposition
            ON disposition.job_id = checkpoint.job_id AND disposition.checkpoint_id = checkpoint.checkpoint_id
        WHERE checkpoint.job_id = $job_id
        """;

    public async Task<RebootResumeCandidate?> IssueRebootCheckpointAsync(
        JournalEntry restartEntry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restartEntry);
        ThrowIfDisposed();
        if (RebootCheckpoint.IsRestartResult(restartEntry, JournalActor.PackageBackend))
        {
            // Package proof is captured in the transaction that recorded the restart result.
            return (await LoadPackageRebootCheckpointAsync(restartEntry, cancellationToken).ConfigureAwait(false))?.Candidate;
        }

        if (!RebootCheckpoint.IsRestartResult(restartEntry, JournalActor.Broker) || restartEntry.ApprovalId is not { } approvalId)
        {
            return null;
        }

        RebootResumeCandidate? candidate = null;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            var key = restartEntry.Plan;
            _layout.ValidateJobDirectory(key.JobId);
            var latest = await ReadLatestRebootEntryAsync(connection, transaction, key, restartEntry.ActionId, token).ConfigureAwait(false);
            if (latest != restartEntry ||
                !await HasCurrentRebootAuthorizationAsync(
                    connection, transaction, key, restartEntry.ActionId, approvalId, token).ConfigureAwait(false))
            {
                return;
            }

            var existing = await ReadRebootCheckpointAsync(
                connection, transaction, key.JobId, restartEntry.EntryId, byJournalEntry: true, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!existing.Candidate.Checkpoint.MatchesRestart(restartEntry))
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }

                if (!existing.IsDecided && IsAuthenticRebootCandidate(existing.Candidate))
                {
                    candidate = new RebootResumeCandidate(existing.Candidate.Checkpoint, existing.Candidate.Token, isCurrentPolicyPlan: true);
                }

                return;
            }

            var now = _timeProvider.GetUtcNow();
            if (now < restartEntry.OccurredAtUtc || now > DateTimeOffset.MaxValue - RebootCheckpoint.MaximumLifetime)
            {
                return;
            }

            var checkpoint = new RebootCheckpoint(Guid.NewGuid(), key, restartEntry.ActionId, restartEntry.EntryId,
                approvalId, restartEntry.CorrelationId, now, now + RebootCheckpoint.MaximumLifetime);
            var protectedToken = WindowsRebootAuthenticator.Seal(checkpoint, _databaseGuard);
            candidate = await InsertRebootCheckpointAsync(connection, transaction, checkpoint, protectedToken, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return candidate;
    }

    private async Task<RebootResumeCandidate> InsertRebootCheckpointAsync(
        SqliteConnection connection, SqliteTransaction transaction, RebootCheckpoint checkpoint,
        RebootResumeToken protectedToken, CancellationToken token)
    {
        await RequireJournalCapacityAsync(connection, transaction, checkpoint.Plan,
            "SELECT count(*) FROM reboot_checkpoints WHERE job_id = $job_id;",
            MaximumRebootCheckpointsPerJob, token).ConfigureAwait(false);
        var payload = SerializePayload(checkpoint, RebootCheckpointRecordLimit);
        await using var insert = JournalCommand(connection, transaction, checkpoint.Plan, """
            INSERT INTO reboot_checkpoints
                (job_id, checkpoint_id, plan_sha256, impact_sha256, journal_entry_id, payload_json, payload_sha256, authenticity_blob)
            VALUES ($job_id, $id, $plan, $impact, $entry, $json, $sha, $authenticity);
            """);
        AddJournalPayload(insert, checkpoint.Id, payload);
        insert.Parameters.AddWithValue("$entry", checkpoint.JournalEntryId.ToString("D"));
        insert.Parameters.AddWithValue("$authenticity", protectedToken.AuthenticityBlob.ToArray());
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        return new RebootResumeCandidate(checkpoint, protectedToken, isCurrentPolicyPlan: true);
    }

    public Task<RebootResumeCandidate?> LoadRebootCheckpointAsync(
        JobId jobId, Guid checkpointId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentOutOfRangeException.ThrowIfEqual(checkpointId, Guid.Empty);
        return ReadRebootCheckpointsAsync(async (connection, transaction, token) =>
        {
            var stored = await ReadRebootCheckpointAsync(
                connection, transaction, jobId, checkpointId, byJournalEntry: false, token).ConfigureAwait(false);
            if (stored is null || stored.IsDecided || stored.Candidate.Checkpoint.PackageBootUptime is not null)
            {
                return null;
            }

            _layout.ValidateJobDirectory(jobId);
            if (!IsAuthenticRebootCandidate(stored.Candidate))
            {
                return null;
            }

            var plan = await RequireJournalPlanAsync(connection, transaction, stored.Candidate.Checkpoint.Plan, token).ConfigureAwait(false);
            return new RebootResumeCandidate(stored.Candidate.Checkpoint, stored.Candidate.Token, plan.IsCurrentPolicyPlan);
        }, cancellationToken);
    }

    public Task<RebootRecoveryDecision?> LoadLatestRebootDecisionAsync(
        JobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadRebootCheckpointsAsync(async (connection, transaction, token) =>
        {
            await using var command = RebootCheckpointCommand(connection, transaction, jobId,
                RebootCheckpointProjection + " AND disposition.checkpoint_id IS NOT NULL ORDER BY disposition.rowid DESC LIMIT $limit;");
            command.Parameters.AddWithValue("$limit", MaximumRebootCheckpointsPerJob + 1);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var read = 0;
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (++read > MaximumRebootCheckpointsPerJob)
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }

                _layout.ValidateJobDirectory(jobId);
                var stored = ReadRebootCheckpoint(reader, jobId);
                if (!IsAuthenticRebootCandidate(stored.Candidate))
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }

                if (stored.Candidate.Checkpoint.PackageBootUptime is not null)
                {
                    continue;
                }

                if (reader.GetInt64(14) is < 2 or > RebootCheckpointRecordLimit)
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }

                var decision = DeserializePayload<RebootRecoveryDecision>(
                    reader.GetString(12), reader.GetString(13), RebootCheckpointRecordLimit);
                if (decision.Checkpoint != stored.Candidate.Checkpoint ||
                    (int)decision.Disposition != reader.GetInt32(10) || decision.DecidedAtUtc != ParseTimestamp(reader.GetString(11)))
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }

                return decision;
            }

            return null;
        }, cancellationToken);
    }

    public Task<ImmutableArray<RebootResumeCandidate>> ListPendingRebootCheckpointsAsync(
        JobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadRebootCheckpointsAsync(async (connection, transaction, token) =>
        {
            await using var command = RebootCheckpointCommand(connection, transaction, jobId,
                RebootCheckpointProjection + " AND disposition.checkpoint_id IS NULL ORDER BY checkpoint.rowid DESC LIMIT $limit;");
            command.Parameters.AddWithValue("$limit", MaximumRebootCheckpointsPerJob + 1);
            var candidates = ImmutableArray.CreateBuilder<RebootResumeCandidate>();
            await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                var read = 0;
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var stored = ReadRebootCheckpoint(reader, jobId);
                    if (++read > MaximumRebootCheckpointsPerJob || !IsAuthenticRebootCandidate(stored.Candidate))
                    {
                        throw new JobStoreCorruptionException("store_payload_mismatch");
                    }

                    if (stored.Candidate.Checkpoint.PackageBootUptime is null)
                    {
                        candidates.Add(stored.Candidate);
                    }
                }
            }

            if (candidates.Count != 0)
            {
                _layout.ValidateJobDirectory(jobId);
            }

            var planContracts = new Dictionary<JournalPlanKey, bool>();
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var key = candidate.Checkpoint.Plan;
                if (!planContracts.TryGetValue(key, out var current))
                {
                    var plan = await RequireJournalPlanAsync(connection, transaction, key, token).ConfigureAwait(false);
                    current = plan.IsCurrentPolicyPlan;
                    planContracts.Add(key, current);
                }

                candidates[index] = new RebootResumeCandidate(candidate.Checkpoint, candidate.Token, current);
            }

            return candidates.ToImmutable();
        }, cancellationToken);
    }

    public async Task<RebootCheckpoint?> DecideRebootCheckpointAsync(
        JobId jobId, Guid checkpointId, RebootResumeToken token, RebootDisposition disposition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentOutOfRangeException.ThrowIfEqual(checkpointId, Guid.Empty);
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        ThrowIfDisposed();
        RebootCheckpoint? decided = null;
        await ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            var stored = await ReadRebootCheckpointAsync(
                connection, transaction, jobId, checkpointId, byJournalEntry: false, ct).ConfigureAwait(false);
            if (stored is null || stored.IsDecided || stored.Candidate.Checkpoint.PackageBootUptime is not null ||
                !CryptographicOperations.FixedTimeEquals(stored.Candidate.Token.AuthenticityBlob.AsSpan(), token.AuthenticityBlob.AsSpan()))
            {
                return;
            }

            _layout.ValidateJobDirectory(jobId);
            if (!IsAuthenticRebootCandidate(stored.Candidate))
            {
                return;
            }

            var checkpoint = stored.Candidate.Checkpoint;
            if (disposition == RebootDisposition.Resume)
            {
                var latest = await ReadLatestRebootEntryAsync(
                    connection, transaction, checkpoint.Plan, checkpoint.ActionId, ct).ConfigureAwait(false);
                if (!checkpoint.MatchesRestart(latest) ||
                    !await HasCurrentRebootAuthorizationAsync(
                        connection, transaction, checkpoint.Plan, checkpoint.ActionId, checkpoint.ApprovalId, ct).ConfigureAwait(false))
                {
                    return;
                }
            }

            var now = _timeProvider.GetUtcNow();
            if (now < checkpoint.IssuedAtUtc || (disposition == RebootDisposition.Resume && !checkpoint.IsCurrent(now)))
            {
                return;
            }

            await InsertRebootDecisionAsync(connection, transaction, checkpoint, disposition, now, ct).ConfigureAwait(false);
            decided = checkpoint;
        }, cancellationToken).ConfigureAwait(false);
        return decided;
    }

    private async Task InsertRebootDecisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, RebootCheckpoint checkpoint, RebootDisposition disposition,
        DateTimeOffset now, CancellationToken token)
    {
        var decision = new RebootRecoveryDecision(checkpoint, disposition, now);
        var payload = SerializePayload(decision, RebootCheckpointRecordLimit);
        await using var insert = RebootCheckpointCommand(connection, transaction, checkpoint.Plan.JobId, """
            INSERT INTO reboot_checkpoint_dispositions
                (job_id, checkpoint_id, disposition, decided_at_utc, payload_json, payload_sha256)
            VALUES ($job_id, $id, $disposition, $decided_at_utc, $json, $sha);
            """);
        AddJournalPayload(insert, checkpoint.Id, payload);
        insert.Parameters.AddWithValue("$disposition", (int)disposition);
        insert.Parameters.AddWithValue("$decided_at_utc", FormatTimestamp(now));
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private Task<JournalEntry?> ReadLatestRebootEntryAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, Guid actionId, CancellationToken token) =>
        ReadEntryAsync(connection, transaction, key,
            EntryProjection + "WHERE " + JournalPlanWhere + " AND action_id = $id ORDER BY entry_sequence DESC LIMIT 1;",
            actionId, token);

    private async Task<bool> HasCurrentRebootAuthorizationAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalPlanKey key, Guid actionId, Guid approvalId,
        CancellationToken token)
    {
        var plan = await ReadJournalPlanAsync(connection, transaction, key, token).ConfigureAwait(false);
        var approval = await ReadApprovalAsync(connection, transaction, key, null, token).ConfigureAwait(false);
        if (plan is not { IsCurrentPolicyPlan: true } || approval?.Plan != key || approval.ApprovalId != approvalId ||
            !plan.Actions.Any(action => action.ActionId == actionId && action.Definition.Reboot == ActionReboot.MayRequire))
        {
            return false;
        }

        try
        {
            await RequireExpectedSanitizationGateAsync(connection, transaction, key.JobId, plan.GateDecisionId, token)
                .ConfigureAwait(false);
            return true;
        }
        catch (JobStoreConflictException)
        {
            return false;
        }
    }

    private async Task<StoredRebootCheckpoint?> ReadRebootCheckpointAsync(
        SqliteConnection connection, SqliteTransaction transaction, JobId jobId, Guid id, bool byJournalEntry,
        CancellationToken token)
    {
        var predicate = byJournalEntry ? " AND checkpoint.journal_entry_id = $id;" : " AND checkpoint.checkpoint_id = $id;";
        await using var command = RebootCheckpointCommand(connection, transaction, jobId, RebootCheckpointProjection + predicate);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadRebootCheckpoint(reader, jobId) : null;
    }

    private StoredRebootCheckpoint ReadRebootCheckpoint(SqliteDataReader reader, JobId jobId)
    {
        if (reader.GetInt64(6) is < 2 or > RebootCheckpointRecordLimit ||
            reader.GetInt64(8) is < 1 or > RebootResumeToken.MaximumBytes)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        var checkpoint = DeserializePayload<RebootCheckpoint>(reader.GetString(4), reader.GetString(5), RebootCheckpointRecordLimit);
        if (checkpoint.Plan.JobId != jobId || reader.GetString(0) != checkpoint.Id.ToString("D") ||
            reader.GetString(1) != checkpoint.Plan.PlanSha256 || reader.GetString(2) != checkpoint.Plan.ImpactSha256 ||
            reader.GetString(3) != checkpoint.JournalEntryId.ToString("D"))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        var token = new RebootResumeToken(reader.GetFieldValue<byte[]>(7));
        return new StoredRebootCheckpoint(new RebootResumeCandidate(checkpoint, token), reader.GetBoolean(9));
    }

    private bool IsAuthenticRebootCandidate(RebootResumeCandidate candidate) =>
        WindowsRebootAuthenticator.Matches(candidate.Checkpoint, candidate.Token, _databaseGuard);

    private async Task<T> ReadRebootCheckpointsAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TranslateReadErrorsAsync(async () =>
            {
                _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                await using var transaction = _connection.BeginTransaction(deferred: true);
                var result = await read(_connection, transaction, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                return result;
            }).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static SqliteCommand RebootCheckpointCommand(
        SqliteConnection connection, SqliteTransaction transaction, JobId jobId, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        return command;
    }

    private sealed record StoredRebootCheckpoint(RebootResumeCandidate Candidate, bool IsDecided);
}
