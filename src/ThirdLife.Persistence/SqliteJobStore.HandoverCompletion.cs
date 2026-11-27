using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Support;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore
{
    private const int MaximumHandoverPublications = 256;
    private const int PublicationPayloadLimit = 2048;
    private const int SignOffPayloadLimit = 16384;

    public Task SaveHandoverFileAsync(HandoverFileReceipt receipt, IHandoverFileVerifier verifier, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(verifier);
        return AppendHandoverPublicationAsync(receipt.Output.Scope, sequence =>
        {
            if (!verifier.MatchesPublishedFile(receipt, token)) { throw new JobStoreConflictException(); }
            return new(sequence, receipt, null);
        }, token);
    }

    public Task SaveHandoverSupportOmissionAsync(HandoverReviewScope expected, OperatorId actor, SupportBundleOmissionReason reason,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return AppendHandoverPublicationAsync(expected, sequence => new(sequence, null,
            new(expected, $"support-omission-{sequence}", actor, _timeProvider.GetUtcNow(), reason)), token);
    }

    public async Task<HandoverSignOff> CompleteHandoverAsync(HandoverEligibility reviewed, OperatorId actor,
        IHandoverFileVerifier verifier, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(verifier);
        HandoverSignOff? completed = null;
        var jobId = reviewed.Scope.JobId;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            if (!_supportsHandoverCompletion || reviewed.Scope.Origin != HandoverEvidenceOrigin.ActiveDevice ||
                await ReadHandoverReviewAsync(transaction, jobId, ct, verifier).ConfigureAwait(false) is not { } current ||
                !current.MatchesReview(reviewed))
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadSignOffsAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (history.Count > 0 && history[^1].IsCurrent(current))
            {
                completed = history[^1];
                return;
            }
            if (history.Count >= HandoverSignOff.MaximumHistoryPerJob) { throw new JobStoreConflictException(); }

            completed = HandoverSignOff.ForReviewedState(reviewed, current, history.Count + 1, actor, _timeProvider.GetUtcNow())
                ?? throw new JobStoreConflictException();
            await InsertHandoverHistoryAsync(transaction, jobId, completed.Sequence, completed, SignOffPayloadLimit,
                """
                INSERT INTO handover_signoffs (job_id, sequence, payload_json, payload_sha256)
                VALUES ($job, $sequence, $payload, $sha);
                """, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return completed ?? throw new JobStoreConflictException();
    }

    public Task<SavedHandoverCompletion?> ReadHandoverCompletionAsync(JobId jobId, IHandoverFileVerifier verifier,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(verifier);
        return ReadJournalTransactionAsync<SavedHandoverCompletion?>(async (transaction, ct) =>
        {
            if (!_supportsHandoverCompletion) { return null; }
            var history = await ReadSignOffsAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (history.Count == 0) { return null; }

            var latest = history[^1];
            _layout.ValidateJobDirectory(jobId);
            var current = await ReadHandoverReviewAsync(transaction, jobId, ct, verifier).ConfigureAwait(false);
            return new(latest, current is not null && latest.IsCurrent(current));
        }, token);
    }

    private sealed record HandoverPublicationEvent(long Sequence, HandoverFileReceipt? File, HandoverSupportBundleOmission? Omission)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public HandoverReviewScope? Scope => File?.Output.Scope ?? Omission?.Scope;
    }

    private Task AppendHandoverPublicationAsync(HandoverReviewScope expected, Func<long, HandoverPublicationEvent> create,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            if (!_supportsHandoverCompletion || expected.Origin != HandoverEvidenceOrigin.ActiveDevice ||
                await ReadHandoverReviewAsync(transaction, expected.JobId, ct).ConfigureAwait(false) is not { } current ||
                current.Scope != expected || current.Blockers.Any(blocker => blocker.Requirement == HandoverRequirement.OpenJob))
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadPublicationsAsync(transaction, expected.JobId, ct).ConfigureAwait(false);
            if (history.Count >= MaximumHandoverPublications) { throw new JobStoreConflictException(); }
            var entry = create(history.Count + 1);
            await InsertHandoverHistoryAsync(transaction, expected.JobId, entry.Sequence, entry, PublicationPayloadLimit,
                """
                INSERT INTO handover_publications (job_id, sequence, payload_json, payload_sha256)
                VALUES ($job, $sequence, $payload, $sha);
                """, ct).ConfigureAwait(false);
        }, token);
    }

    private async Task AddPublicationEvidenceAsync(SqliteTransaction transaction, HandoverReviewScope scope,
        List<HandoverRequirementEvidence> evidence, IHandoverFileVerifier verifier, CancellationToken token)
    {
        var history = await ReadPublicationsAsync(transaction, scope.JobId, token).ConfigureAwait(false);
        var workshop = history.LastOrDefault(item => item.File?.Output.Kind == HandoverOutputKind.WorkshopRecord)?.File;
        var guide = history.LastOrDefault(item => item.File?.Output.Kind == HandoverOutputKind.RecipientGuide)?.File;
        AddFileEvidence(HandoverRequirement.WorkshopRecord, workshop);
        AddFileEvidence(HandoverRequirement.RecipientGuide, guide);

        var omission = history.LastOrDefault(item => item.Omission is not null)?.Omission;
        var support = (await ReadSupportHistoryAsync(transaction, scope.JobId, token).ConfigureAwait(false)).Values
            .Where(entry => entry.ReviewVersion == scope.Version).ToArray();
        if (support.Any(entry => entry.State == SupportExportState.Pending))
        {
            evidence.Add(new(HandoverRequirement.SupportBundleDecision, HandoverEvidenceState.Unavailable, scope));
            return;
        }

        var published = support.Where(entry => entry.State == SupportExportState.Published)
            .OrderByDescending(entry => entry.RecordedAtUtc).ThenBy(entry => entry.InternalSupportId, StringComparer.Ordinal).FirstOrDefault();
        if (omission?.Scope == scope && (published is null || published.RecordedAtUtc <= omission.RecordedAtUtc))
        {
            evidence.Add(new(HandoverRequirement.SupportBundleDecision, HandoverEvidenceState.OmittedByRecordedChoice, scope,
                omission.Version, supportBundleOmission: omission));
        }
        else if (published is not null)
        {
            var version = $"{published.ManifestVersion}-{published.InternalSupportId}";
            evidence.Add(new(HandoverRequirement.SupportBundleDecision, HandoverEvidenceState.Verified, scope, version,
                new(HandoverOutputKind.SupportBundle, scope, version, published.ContentSha256)));
        }

        void AddFileEvidence(HandoverRequirement kind, HandoverFileReceipt? receipt)
        {
            if (receipt is null) { return; }
            if (receipt.Output.Scope != scope)
            {
                evidence.Add(new(kind, HandoverEvidenceState.Stale, scope));
                return;
            }
            evidence.Add(verifier.MatchesPublishedFile(receipt, token)
                ? new(kind, HandoverEvidenceState.Verified, scope, receipt.Output.Version, receipt.Output)
                : new(kind, HandoverEvidenceState.Unavailable, scope));
        }
    }

    private Task<IReadOnlyList<HandoverPublicationEvent>> ReadPublicationsAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token) => ReadHandoverHistoryAsync<HandoverPublicationEvent>(transaction, jobId,
        """
        SELECT sequence, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
        FROM handover_publications WHERE job_id = $job ORDER BY sequence LIMIT $limit;
        """, MaximumHandoverPublications, PublicationPayloadLimit,
        (row, sequence) => row.Sequence == sequence && row.Scope?.JobId == jobId &&
            row.Scope.Origin == HandoverEvidenceOrigin.ActiveDevice && (row.File is null) != (row.Omission is null), token);

    private Task<IReadOnlyList<HandoverSignOff>> ReadSignOffsAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token) => ReadHandoverHistoryAsync<HandoverSignOff>(transaction, jobId,
        """
        SELECT sequence, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
        FROM handover_signoffs WHERE job_id = $job ORDER BY sequence LIMIT $limit;
        """, HandoverSignOff.MaximumHistoryPerJob, SignOffPayloadLimit,
        (row, sequence) => row.Sequence == sequence && row.Scope.JobId == jobId &&
            row.Scope.Origin == HandoverEvidenceOrigin.ActiveDevice, token);

    private async Task<IReadOnlyList<T>> ReadHandoverHistoryAsync<T>(SqliteTransaction transaction, JobId jobId,
        string query, int limit, int payloadLimit, Func<T, long, bool> matches, CancellationToken token) where T : class
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$limit", limit + 1);
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var sequence = reader.GetInt64(0);
            if (rows.Count >= limit || sequence != rows.Count + 1 || reader.GetInt64(1) < 2 || reader.GetInt64(1) > payloadLimit)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }
            var row = DeserializePayload<T>(reader.GetString(2), reader.GetString(3), payloadLimit);
            if (!matches(row, sequence)) { throw new JobStoreCorruptionException("store_payload_mismatch"); }
            rows.Add(row);
        }
        return rows;
    }

    private async Task InsertHandoverHistoryAsync<T>(SqliteTransaction transaction, JobId jobId, long sequence,
        T record, int payloadLimit, string query, CancellationToken token)
    {
        var payload = SerializePayload(record, payloadLimit);
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$payload", payload.Json);
        command.Parameters.AddWithValue("$sha", payload.Sha256);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
