using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IToggleKeysAuditStore
{
    private const int MaximumToggleKeysEvents = 128;
    private const int ToggleKeysAuditPayloadLimit = 1024;

    public async Task<ToggleKeysAuditEntry> StageToggleKeysAsync(ToggleKeysApproval approval, OperatorId actor,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(actor);
        var change = approval.Change;
        ToggleKeysAuditEntry? accepted = null;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            if (!_supportsToggleKeysAudit ||
                await ReadRecipientHandoverAsync(transaction, change.JobId, ct).ConfigureAwait(false) is not
                { IsArchived: false, Handover.Mode: HandoverMode.Assisted } handover || handover.Sequence != change.HandoverSequence)
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadToggleKeysJournalAsync(transaction, change.JobId, ct).ConfigureAwait(false);
            if (history.Count > MaximumToggleKeysEvents - 2 || history.Any(row => row.Audit.ChangeId == change.ChangeId) ||
                history.Count > 0 && history[^1].Audit.Outcome == ToggleKeysAuditOutcome.ApprovedPending)
            {
                throw new JobStoreConflictException();
            }

            accepted = new(change.ChangeId, change.HandoverSequence, change.Before, change.Requested, actor,
                _timeProvider.GetUtcNow(), ToggleKeysAuditOutcome.ApprovedPending, null);
            await AppendToggleKeysAuditAsync(transaction, change.JobId, history.Count + 1, accepted, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return accepted ?? throw new JobStoreConflictException();
    }

    public Task RecordToggleKeysOutcomeAsync(JobId jobId, ToggleKeysAuditEntry expected, ToggleKeysAuditOutcome result,
        bool? observed, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Outcome != ToggleKeysAuditOutcome.ApprovedPending || result == ToggleKeysAuditOutcome.ApprovedPending)
        {
            throw new ArgumentException("Recording a setting attempt requires an approved intent and a later outcome.", nameof(result));
        }

        return ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            if (!_supportsToggleKeysAudit) { throw new JobStoreConflictException(); }
            var history = await ReadToggleKeysJournalAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (history.Count == 0 || history.Count >= MaximumToggleKeysEvents || history[^1].Audit != expected)
            {
                throw new JobStoreConflictException();
            }
            if ((result is ToggleKeysAuditOutcome.Verified or ToggleKeysAuditOutcome.ObservedMismatch) &&
                (await ReadRecipientHandoverAsync(transaction, jobId, ct).ConfigureAwait(false) is not
                { IsArchived: false, Handover.Mode: HandoverMode.Assisted } handover || handover.Sequence != expected.HandoverSequence))
            {
                throw new JobStoreConflictException();
            }

            var terminal = new ToggleKeysAuditEntry(expected.ChangeId, expected.HandoverSequence, expected.Before, expected.Requested,
                expected.OperatorId, _timeProvider.GetUtcNow(), result, observed);
            await AppendToggleKeysAuditAsync(transaction, jobId, history.Count + 1, terminal, ct).ConfigureAwait(false);
        }, token);
    }

    public Task<IReadOnlyList<ToggleKeysAuditEntry>> LoadToggleKeysAttemptsAsync(JobId jobId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync<IReadOnlyList<ToggleKeysAuditEntry>>(async (transaction, ct) =>
        {
            if (!_supportsToggleKeysAudit) { return []; }
            var history = await ReadToggleKeysJournalAsync(transaction, jobId, ct).ConfigureAwait(false);
            return Array.AsReadOnly(history.Select(row => row.Audit).ToArray());
        }, token);
    }

    private sealed record StoredToggleKeysAudit(JobId JobId, long Sequence, ToggleKeysAuditEntry Audit);

    private async Task<IReadOnlyList<StoredToggleKeysAudit>> ReadToggleKeysJournalAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        var history = await ReadHandoverHistoryAsync<StoredToggleKeysAudit>(transaction, jobId,
            """
            SELECT sequence, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM recipient_toggle_keys_audit WHERE job_id = $job ORDER BY sequence LIMIT $limit;
            """, MaximumToggleKeysEvents, ToggleKeysAuditPayloadLimit,
            (row, sequence) => row.JobId == jobId && row.Sequence == sequence && row.Audit is not null, token).ConfigureAwait(false);
        if (history.Count == 0) { return history; }

        _layout.ValidateJobDirectory(jobId);
        var previousIds = new HashSet<Guid>();
        ToggleKeysAuditEntry? pending = null;
        foreach (var row in history)
        {
            var audit = row.Audit;
            if (pending is null && audit.Outcome == ToggleKeysAuditOutcome.ApprovedPending && previousIds.Add(audit.ChangeId))
            {
                pending = audit;
                continue;
            }
            if (pending is null || audit.Outcome == ToggleKeysAuditOutcome.ApprovedPending || audit.ChangeId != pending.ChangeId ||
                audit.HandoverSequence != pending.HandoverSequence || audit.Before != pending.Before ||
                audit.Requested != pending.Requested || audit.OperatorId != pending.OperatorId)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }
            pending = null;
        }
        return history;
    }

    private Task AppendToggleKeysAuditAsync(SqliteTransaction transaction, JobId jobId, long sequence,
        ToggleKeysAuditEntry audit, CancellationToken token) => InsertHandoverHistoryAsync(transaction, jobId, sequence,
        new StoredToggleKeysAudit(jobId, sequence, audit), ToggleKeysAuditPayloadLimit,
        """
        INSERT INTO recipient_toggle_keys_audit (job_id, sequence, payload_json, payload_sha256)
        VALUES ($job, $sequence, $payload, $sha);
        """, token);
}
