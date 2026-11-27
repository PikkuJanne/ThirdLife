using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IFileHistorySampleAuditStore
{
    private const int MaximumFileHistorySampleEvents = 128;
    private const int FileHistorySamplePayloadLimit = 1024;

    public async Task<FileHistorySampleAudit> StageFileHistorySampleAsync(FileHistoryApproval approval,
        FileHistoryRestoreSample sample, OperatorId actor, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(actor);
        if (!sample.IsFor(approval)) { throw new ArgumentException("The sample belongs to a different backup review.", nameof(sample)); }

        var review = approval.Review;
        FileHistorySampleAudit? entry = null;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            await RequireCurrentFileHistoryHandoverAsync(transaction, review.JobId, review.HandoverSequence, ct).ConfigureAwait(false);
            var history = await ReadFileHistorySampleHistoryAsync(transaction, review.JobId, ct).ConfigureAwait(false);
            if (history.Count > MaximumFileHistorySampleEvents - 2 || history.Any(row => row.Audit.SampleId == sample.SampleId))
            {
                throw new JobStoreConflictException();
            }
            entry = new(sample.SampleId, review.HandoverSequence, sample.Sha256, sample.ByteLength, actor,
                _timeProvider.GetUtcNow(), FileHistorySampleAuditState.Preparing);
            await AppendFileHistorySampleAsync(transaction, review.JobId, history.Count + 1, entry, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return entry ?? throw new JobStoreConflictException();
    }

    public async Task<FileHistorySampleAudit> RecordFileHistorySampleAsync(JobId jobId, FileHistorySampleAudit expected,
        FileHistorySampleAuditState outcome, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(expected);
        if (!expected.CanRecord(outcome)) { throw new ArgumentException("The current sample cannot record that outcome.", nameof(outcome)); }

        FileHistorySampleAudit? recorded = null;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            await RequireCurrentFileHistoryHandoverAsync(transaction, jobId, expected.HandoverSequence, ct).ConfigureAwait(false);
            var history = await ReadFileHistorySampleHistoryAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (history.Count == 0 || history[^1].Audit != expected)
            {
                throw new JobStoreConflictException();
            }
            if (outcome == expected.State)
            {
                recorded = expected;
                return;
            }
            if (history.Count >= MaximumFileHistorySampleEvents) { throw new JobStoreConflictException(); }
            recorded = new(expected.SampleId, expected.HandoverSequence, expected.Sha256, expected.ByteLength, expected.OperatorId,
                _timeProvider.GetUtcNow(), outcome);
            await AppendFileHistorySampleAsync(transaction, jobId, history.Count + 1, recorded, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return recorded ?? throw new JobStoreConflictException();
    }

    public Task<IReadOnlyList<FileHistorySampleAudit>> LoadFileHistorySampleHistoryAsync(JobId jobId,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync<IReadOnlyList<FileHistorySampleAudit>>(async (transaction, ct) =>
        {
            if (!_supportsFileHistorySampleAudit) { return []; }
            var history = await ReadFileHistorySampleHistoryAsync(transaction, jobId, ct).ConfigureAwait(false);
            return Array.AsReadOnly(history.Select(row => row.Audit).ToArray());
        }, token);
    }

    private async Task RequireCurrentFileHistoryHandoverAsync(SqliteTransaction transaction, JobId jobId,
        long expectedSequence, CancellationToken token)
    {
        if (!_supportsFileHistorySampleAudit || await ReadRecipientHandoverAsync(transaction, jobId, token).ConfigureAwait(false) is not
            { IsArchived: false, Handover.Mode: HandoverMode.Assisted } handover || handover.Sequence != expectedSequence)
        {
            throw new JobStoreConflictException();
        }
    }

    private sealed record StoredFileHistorySample(JobId JobId, long Sequence, FileHistorySampleAudit Audit);

    private async Task<IReadOnlyList<StoredFileHistorySample>> ReadFileHistorySampleHistoryAsync(SqliteTransaction transaction,
        JobId jobId, CancellationToken token)
    {
        var history = await ReadHandoverHistoryAsync<StoredFileHistorySample>(transaction, jobId,
            """
            SELECT sequence, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM file_history_sample_audit WHERE job_id = $job ORDER BY sequence LIMIT $limit;
            """, MaximumFileHistorySampleEvents, FileHistorySamplePayloadLimit,
            (row, sequence) => row.JobId == jobId && row.Sequence == sequence && row.Audit is not null, token).ConfigureAwait(false);
        if (history.Count == 0) { return history; }

        _layout.ValidateJobDirectory(jobId);
        var sampleIds = new HashSet<Guid>();
        FileHistorySampleAudit? previous = null;
        foreach (var row in history)
        {
            var audit = row.Audit;
            var valid = audit.State == FileHistorySampleAuditState.Preparing
                ? sampleIds.Add(audit.SampleId)
                : previous is not null && previous.CanRecord(audit.State) && previous.SampleId == audit.SampleId &&
                    previous.HandoverSequence == audit.HandoverSequence && previous.Sha256 == audit.Sha256 &&
                    previous.ByteLength == audit.ByteLength && previous.OperatorId == audit.OperatorId;
            if (!valid) { throw new JobStoreCorruptionException("store_payload_mismatch"); }
            previous = audit;
        }
        return history;
    }

    private Task AppendFileHistorySampleAsync(SqliteTransaction transaction, JobId jobId, long sequence,
        FileHistorySampleAudit audit, CancellationToken token) => InsertHandoverHistoryAsync(transaction, jobId, sequence,
        new StoredFileHistorySample(jobId, sequence, audit), FileHistorySamplePayloadLimit,
        """
        INSERT INTO file_history_sample_audit (job_id, sequence, payload_json, payload_sha256)
        VALUES ($job, $sequence, $payload, $sha);
        """, token);
}
