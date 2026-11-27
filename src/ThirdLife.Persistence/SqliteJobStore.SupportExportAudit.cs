using Microsoft.Data.Sqlite;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Support;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : ISupportExportAuditStore
{
    private const int MaximumSupportExports = 64;
    private const int MaximumSupportAuditBytes = 1024;

    public Task StageSupportExportAsync(JobId jobId, SupportExportAudit intent, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.State != SupportExportState.Pending)
        {
            throw new ArgumentException("Staging requires a pending export intent.", nameof(intent));
        }

        return ExecuteWriteAsync(async (connection, transaction, cancellationToken) =>
        {
            if (!_supportsSupportExportAudit ||
                await ReadArchiveStateAsync(connection, transaction, jobId, cancellationToken).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }

            if (intent.ReviewVersion is { } revision && (!_supportsHandoverCompletion ||
                await ReadHandoverReviewAsync(transaction, jobId, cancellationToken).ConfigureAwait(false) is not
                { Scope.Origin: HandoverEvidenceOrigin.ActiveDevice } current || current.Scope.Version != revision))
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadSupportHistoryAsync(transaction, jobId, cancellationToken).ConfigureAwait(false);
            if (history.Count >= MaximumSupportExports || history.ContainsKey(intent.InternalSupportId))
            {
                throw new JobStoreConflictException();
            }

            await AppendSupportAuditAsync(connection, transaction, jobId, intent, 1, cancellationToken).ConfigureAwait(false);
        }, token);
    }

    public Task RecordSupportExportAsync(JobId jobId, SupportExportAudit completed, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.State == SupportExportState.Pending)
        {
            throw new ArgumentException("Recording an export requires a terminal result.", nameof(completed));
        }

        return ExecuteWriteAsync(async (connection, transaction, cancellationToken) =>
        {
            if (!_supportsSupportExportAudit)
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadSupportHistoryAsync(transaction, jobId, cancellationToken).ConfigureAwait(false);
            if (!history.TryGetValue(completed.InternalSupportId, out var export) || !SameSupportArchive(export, completed))
            {
                throw new JobStoreConflictException();
            }

            if (export.State == SupportExportState.Pending)
            {
                await AppendSupportAuditAsync(connection, transaction, jobId, completed, 2, cancellationToken).ConfigureAwait(false);
            }
            else if (export != completed)
            {
                throw new JobStoreConflictException();
            }
        }, token);
    }

    public Task<IReadOnlyList<SupportExportAudit>> LoadSupportExportsAsync(JobId jobId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync<IReadOnlyList<SupportExportAudit>>(async (transaction, cancellationToken) =>
        {
            if (!_supportsSupportExportAudit)
            {
                return [];
            }

            var history = await ReadSupportHistoryAsync(transaction, jobId, cancellationToken).ConfigureAwait(false);
            return Array.AsReadOnly(history.Values.OrderBy(entry => entry.RecordedAtUtc)
                .ThenBy(entry => entry.InternalSupportId, StringComparer.Ordinal).ToArray());
        }, token);
    }

    private async Task<Dictionary<string, SupportExportAudit>> ReadSupportHistoryAsync(
        SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        _layout.ValidateJobDirectory(jobId);
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT support_id, phase, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM support_export_audit WHERE job_id = $job ORDER BY support_id, phase LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$limit", MaximumSupportExports * 2 + 1);
        var current = new Dictionary<string, SupportExportAudit>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (reader.GetInt64(2) is < 2 or > MaximumSupportAuditBytes)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var payload = DeserializePayload<StoredSupportAudit>(reader.GetString(3), reader.GetString(4), MaximumSupportAuditBytes);
            if (payload.JobId != jobId || payload.Export is not { } export || export.InternalSupportId != reader.GetString(0))
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var phase = reader.GetInt64(1);
            if (phase == 1 && export.State == SupportExportState.Pending &&
                current.Count < MaximumSupportExports && current.TryAdd(export.InternalSupportId, export))
            {
                continue;
            }

            if (phase != 2 || export.State == SupportExportState.Pending ||
                !current.TryGetValue(export.InternalSupportId, out var prior) || !SameSupportArchive(prior, export))
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            current[export.InternalSupportId] = export;
        }

        return current;
    }

    private async Task AppendSupportAuditAsync(SqliteConnection connection, SqliteTransaction transaction,
        JobId jobId, SupportExportAudit audit, int phase, CancellationToken token)
    {
        var payload = SerializePayload(new StoredSupportAudit(jobId, audit), MaximumSupportAuditBytes);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO support_export_audit (job_id, support_id, phase, payload_json, payload_sha256)
            VALUES ($job, $support, $phase, $payload, $sha);
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$support", audit.InternalSupportId);
        command.Parameters.AddWithValue("$phase", phase);
        command.Parameters.AddWithValue("$payload", payload.Json);
        command.Parameters.AddWithValue("$sha", payload.Sha256);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static bool SameSupportArchive(SupportExportAudit left, SupportExportAudit right) =>
        left.InternalSupportId == right.InternalSupportId && left.SchemaVersion == right.SchemaVersion &&
        left.ManifestVersion == right.ManifestVersion && left.ContentSha256 == right.ContentSha256 &&
        left.OperatorId == right.OperatorId && left.ReviewVersion == right.ReviewVersion;

    private sealed record StoredSupportAudit(JobId JobId, SupportExportAudit Export);
}
