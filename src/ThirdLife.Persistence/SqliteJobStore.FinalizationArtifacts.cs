using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore
{
    private const int ArtifactScanPayloadLimit = 2048;

    public Task SaveFinalizationArtifactScanAsync(JobId jobId, FinalizationChecklist expected,
        SanitizationGateDecisionId expectedGate, IReadOnlyList<WorkshopArtifactFinding> findings,
        ProvenanceKind origin, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(expectedGate);
        ThrowIfDisposed();
        if (expected.EvidenceVersion is not { } expectedVersion || !expected.CanScanArtifacts)
        {
            throw new JobStoreConflictException();
        }

        // Snapshot the caller's typed results before waiting for the transaction.
        var scan = new WorkshopArtifactScan(expected.ArtifactScans.Count + 1L, _timeProvider.GetUtcNow(),
            expectedVersion, WorkshopArtifactScan.CurrentDetectorRevision, origin, findings);
        return ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            if (!_supportsFinalizationArtifacts ||
                await ReadFinalizationStateAsync(transaction, jobId, ct).ConfigureAwait(false) is not { } state ||
                !string.Equals(state.EvidenceVersion, expectedVersion, StringComparison.Ordinal))
            {
                throw new JobStoreConflictException();
            }

            await RequireExpectedSanitizationGateAsync(connection, transaction, jobId, expectedGate, ct).ConfigureAwait(false);

            var currentScans = await ReadFinalizationArtifactScansAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (currentScans.Count != expected.ArtifactScans.Count ||
                currentScans.Count > 0 && currentScans[^1].Origin != scan.Origin)
            {
                throw new JobStoreConflictException();
            }

            var payload = SerializePayload(new StoredArtifactScan(jobId, scan), ArtifactScanPayloadLimit);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO finalization_artifact_scans (job_id, sequence, payload_json, payload_sha256)
                VALUES ($job, $sequence, $payload, $sha);
                """;
            command.Parameters.AddWithValue("$job", jobId.Value);
            command.Parameters.AddWithValue("$sequence", scan.Sequence);
            command.Parameters.AddWithValue("$payload", payload.Json);
            command.Parameters.AddWithValue("$sha", payload.Sha256);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, token);
    }

    private async Task<IReadOnlyList<WorkshopArtifactScan>> ReadFinalizationArtifactScansAsync(
        SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        if (!_supportsFinalizationArtifacts)
        {
            return [];
        }

        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sequence, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM finalization_artifact_scans WHERE job_id = $job ORDER BY sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$limit", WorkshopArtifactScan.MaximumScansPerJob + 1);
        var scans = new List<WorkshopArtifactScan>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (scans.Count >= WorkshopArtifactScan.MaximumScansPerJob || reader.GetInt64(0) != scans.Count + 1 ||
                reader.GetInt64(1) is < 2 or > ArtifactScanPayloadLimit)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var stored = DeserializePayload<StoredArtifactScan>(reader.GetString(2), reader.GetString(3), ArtifactScanPayloadLimit);
            if (stored.JobId != jobId || stored.Scan?.Sequence != reader.GetInt64(0) ||
                scans.Count > 0 && scans[0].Origin != stored.Scan.Origin)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            scans.Add(stored.Scan);
        }

        return scans;
    }

    private sealed record StoredArtifactScan(JobId JobId, WorkshopArtifactScan Scan);
}
