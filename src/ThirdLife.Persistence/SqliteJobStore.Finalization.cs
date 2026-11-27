using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Verification;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IFinalizationStore
{
    private const int FinalizationPayloadLimit = 1024;

    public Task<FinalizationChecklist?> LoadFinalizationAsync(JobId jobId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync<FinalizationChecklist?>(async (transaction, ct) =>
        {
            if (!_supportsFinalization ||
                await ReadFinalizationStateAsync(transaction, jobId, ct).ConfigureAwait(false) is not { } state)
            {
                return null;
            }

            var history = await ReadFinalizationHistoryAsync(transaction, jobId, ct).ConfigureAwait(false);
            var restart = state.EvidenceVersion is null ? FinalizationOutcome.Unknown :
                await ReadFinalizationRestartAsync(transaction, jobId, ct).ConfigureAwait(false);
            return FinalizationChecklist.Evaluate(state.EvidenceVersion, history, restart);
        }, token);
    }

    public Task SaveFinalizationConfirmationAsync(JobId jobId, FinalizationChecklist expected, FinalizationRuleId ruleId,
        FinalizationOutcome outcome, FinalizationReason? reason, OperatorId operatorId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(operatorId);
        ThrowIfDisposed();
        var expectedVersion = expected.EvidenceVersion;
        var expectedSequence = expected.History.Count == 0 ? 0 : expected.History[^1].Sequence;
        return ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            if (!_supportsFinalization || expectedVersion is null ||
                await ReadFinalizationStateAsync(transaction, jobId, ct).ConfigureAwait(false) is not { } state ||
                !string.Equals(state.EvidenceVersion, expectedVersion, StringComparison.Ordinal))
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadFinalizationHistoryAsync(transaction, jobId, ct).ConfigureAwait(false);
            var currentSequence = history.Count == 0 ? 0 : history[^1].Sequence;
            if (history.Count >= FinalizationChecklist.MaximumHistoryPerJob || expectedSequence != currentSequence)
            {
                throw new JobStoreConflictException();
            }

            var record = new FinalizationRecord(expectedSequence + 1, ruleId, outcome, reason, operatorId,
                _timeProvider.GetUtcNow(), expectedVersion);
            var payload = SerializePayload(new StoredFinalizationConfirmation(jobId, record), FinalizationPayloadLimit);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO finalization_confirmations (job_id, sequence, payload_json, payload_sha256)
                VALUES ($job, $sequence, $payload, $sha);
                """;
            command.Parameters.AddWithValue("$job", jobId.Value);
            command.Parameters.AddWithValue("$sequence", record.Sequence);
            command.Parameters.AddWithValue("$payload", payload.Json);
            command.Parameters.AddWithValue("$sha", payload.Sha256);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, token);
    }

    private sealed record FinalizationState(string? EvidenceVersion);

    private sealed record StoredFinalizationConfirmation(JobId JobId, FinalizationRecord Record);

    private async Task<FinalizationState?> ReadFinalizationStateAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        // The per-job tables are append-oriented. Their revisions bind confirmations to
        // inspected context without reloading evidence payloads or counting checklist writes.
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT is_archived,
                (SELECT max(checkpoint_sequence) FROM store_checkpoints WHERE job_id = $job),
                (SELECT max(decision_sequence) FROM sanitization_gate_decisions WHERE job_id = $job),
                (SELECT max(decision_sequence) FROM policy_decisions WHERE job_id = $job),
                (SELECT max(snapshot_sequence) FROM policy_snapshots
                 WHERE policy_id IN (SELECT policy_id FROM policy_decisions WHERE job_id = $job)),
                (SELECT count(*) FROM action_journal_plans WHERE job_id = $job),
                (SELECT max(approval_sequence) FROM action_journal_approvals WHERE job_id = $job),
                (SELECT max(entry_sequence) FROM action_journal_entries WHERE job_id = $job),
                (SELECT count(*) FROM reboot_checkpoints WHERE job_id = $job),
                (SELECT count(*) FROM reboot_checkpoint_dispositions WHERE job_id = $job),
                (SELECT max(sequence) FROM windows_update_journal WHERE job_id = $job),
                (SELECT max(revision) FROM maintenance_checkpoints WHERE job_id = $job),
                (SELECT max(sequence) FROM profile_verifications WHERE job_id = $job),
                (SELECT max(sequence) FROM cold_boot_checkpoints WHERE job_id = $job)
            FROM jobs WHERE job_id = $job;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        _layout.ValidateJobDirectory(jobId);
        var archiveState = reader.GetInt64(0);
        if (archiveState is not 0 and not 1)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        if (archiveState == 1 || _readBootSession() is not { } bootSession)
        {
            return new(EvidenceVersion: null);
        }

        var revisions = new long[reader.FieldCount - 1];
        for (var index = 0; index < revisions.Length; index++)
        {
            revisions[index] = reader.IsDBNull(index + 1) ? 0 : reader.GetInt64(index + 1);
        }

        return new(SerializePayload(new
        {
            Revision = FinalizationChecklist.RuleRevision,
            Job = jobId.Value,
            BootSession = bootSession,
            Revisions = revisions,
        }).Sha256);
    }

    private async Task<IReadOnlyList<FinalizationRecord>> ReadFinalizationHistoryAsync(SqliteTransaction transaction,
        JobId jobId, CancellationToken token)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sequence, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM finalization_confirmations WHERE job_id = $job ORDER BY sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$limit", FinalizationChecklist.MaximumHistoryPerJob + 1);
        var history = new List<FinalizationRecord>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (history.Count >= FinalizationChecklist.MaximumHistoryPerJob || reader.GetInt64(0) != history.Count + 1 ||
                reader.GetInt64(1) is < 2 or > FinalizationPayloadLimit)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var confirmation = DeserializePayload<StoredFinalizationConfirmation>(reader.GetString(2), reader.GetString(3),
                FinalizationPayloadLimit);
            if (confirmation.JobId != jobId || confirmation.Record?.Sequence != reader.GetInt64(0))
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            history.Add(confirmation.Record);
        }

        return history;
    }

    private async Task<FinalizationOutcome> ReadFinalizationRestartAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        if (await ReadProfileVerificationAsync(transaction, jobId, token).ConfigureAwait(false) is not { } verification ||
            !await IsCurrentProfileVerificationAsync(transaction, verification.Record, token).ConfigureAwait(false))
        {
            return FinalizationOutcome.Unknown;
        }

        return verification.Record.Result.Checks.Single(check => check.Kind == ProfileCheckKind.PendingRestart).State switch
        {
            ProfileVerificationState.Passed when !verification.Record.Result.UpdateRecoveryPending => FinalizationOutcome.Passed,
            ProfileVerificationState.Failed => FinalizationOutcome.Failed,
            _ => FinalizationOutcome.Unknown,
        };
    }
}
