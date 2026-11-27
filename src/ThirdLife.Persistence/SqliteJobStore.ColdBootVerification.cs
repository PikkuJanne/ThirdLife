using System.Text;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.Recovery;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

public sealed record SavedColdBootVerification(ColdBootCheckpoint Checkpoint,
    ColdBootVerificationState State, ColdBootObservation? Observation = null);

public sealed partial class SqliteJobStore
{
    private const int MaximumColdBootCheckpoints = 64;
    private const int ColdBootPayloadLimit = 8192;
    private static ReadOnlySpan<byte> ColdBootPurpose => "ThirdLife.ColdBootCheckpoint.v1"u8;

    private sealed record ColdBootBinding(MaintenanceCheckpoint Maintenance, Guid PackageJournalEntryId,
        int JournalEntryCount, Guid? LastServicingEntryId);

    private sealed record ColdBootRequest(ColdBootCheckpoint Checkpoint, ColdBootBinding Binding);

    private sealed record ColdBootRow(int Sequence, ColdBootRequest Request, bool Authenticated);

    public async Task<ColdBootCheckpoint> RecordColdBootCheckpointAsync(MaintenanceCheckpoint expected,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ColdBootCheckpoint? checkpoint = null;
        await ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            if (!_supportsColdBoot || _readColdBoot is null || _readBootSession() is not { } boot ||
                boot == uint.MaxValue || boot < expected.BootSession ||
                await ReadColdBootBindingAsync(transaction, expected, ct).ConfigureAwait(false) is not { } binding)
            {
                throw new JobStoreConflictException();
            }

            var previous = await ReadColdBootRequestAsync(transaction, expected.Plan.JobId, ct).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();
            if (previous is { Authenticated: true } && previous.Request.Binding == binding &&
                IsAwaitingShutdown(previous.Request.Checkpoint, boot, now))
            {
                checkpoint = previous.Request.Checkpoint;
                return;
            }

            var sequence = (previous?.Sequence ?? 0) + 1;
            if (sequence > MaximumColdBootCheckpoints) { throw new JobStoreConflictException(); }
            checkpoint = new(Guid.NewGuid(), now, boot);
            var payload = SerializePayload(new ColdBootRequest(checkpoint, binding), ColdBootPayloadLimit);
            var authenticator = WindowsRebootAuthenticator.Seal(Encoding.UTF8.GetBytes(payload.Json), _databaseGuard,
                ColdBootPurpose) ?? throw new JobStoreUnavailableException();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cold_boot_checkpoints(job_id, sequence, maintenance_revision, checkpoint_id, payload_json, payload_sha256, authenticator)
                VALUES ($job, $sequence, $revision, $id, $payload, $sha, $authenticator);
                """;
            command.Parameters.AddWithValue("$job", expected.Plan.JobId.Value);
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$revision", expected.Revision);
            command.Parameters.AddWithValue("$id", checkpoint.Id.ToString("D"));
            command.Parameters.AddWithValue("$payload", payload.Json);
            command.Parameters.AddWithValue("$sha", payload.Sha256);
            command.Parameters.AddWithValue("$authenticator", authenticator);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return checkpoint!;
    }

    public Task<SavedColdBootVerification?> LoadColdBootVerificationAsync(JobId jobId, CancellationToken token = default) =>
        ReadJournalTransactionAsync<SavedColdBootVerification?>(async (transaction, ct) =>
        {
            if (!_supportsColdBoot ||
                await ReadColdBootRequestAsync(transaction, jobId, ct).ConfigureAwait(false) is not { } row)
            {
                return null;
            }

            var checkpoint = row.Request.Checkpoint;
            var now = _timeProvider.GetUtcNow();
            if (!row.Authenticated || _readBootSession() is not { } boot ||
                await ReadColdBootBindingAsync(transaction, row.Request.Binding.Maintenance, ct).ConfigureAwait(false) != row.Request.Binding)
            {
                return new(checkpoint, ColdBootVerificationState.Historical);
            }

            if (IsAwaitingShutdown(checkpoint, boot, now)) { return new(checkpoint, ColdBootVerificationState.AwaitingShutdown); }
            if (!checkpoint.CanObserve(boot, now)) { return new(checkpoint, ColdBootVerificationState.Historical); }

            var observation = ReadColdBootObservation(checkpoint, boot, now);
            return new(checkpoint, checkpoint.Matches(observation, boot, now)
                ? ColdBootVerificationState.ReadyToVerify : ColdBootVerificationState.AwaitingEvidence, observation);
        }, token);

    private ColdBootObservation? ReadColdBootObservation(ColdBootCheckpoint checkpoint, uint boot, DateTimeOffset now) =>
        _readColdBoot?.Invoke(boot, checkpoint.RequestedAtUtc) is { } observation &&
        observation.BootSession == boot && observation.StartedAtUtc > checkpoint.RequestedAtUtc &&
        observation.StartedAtUtc <= now && Enum.IsDefined(observation.Kind) ? observation : null;

    private static bool IsAwaitingShutdown(ColdBootCheckpoint checkpoint, uint boot, DateTimeOffset now) =>
        boot == checkpoint.RequestedBootSession && now >= checkpoint.RequestedAtUtc &&
        now - checkpoint.RequestedAtUtc < TimeSpan.FromHours(24);

    private async Task<ColdBootRow?> ReadColdBootRequestAsync(SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        _layout.ValidateJobDirectory(jobId);
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sequence, maintenance_revision, checkpoint_id, length(CAST(payload_json AS BLOB)),
                payload_json, payload_sha256, length(authenticator), authenticator
            FROM cold_boot_checkpoints WHERE job_id = $job ORDER BY sequence DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) { return null; }
        if (reader.GetInt64(3) is < 2 or > ColdBootPayloadLimit ||
            reader.GetInt64(6) is < 1 or > RebootResumeToken.MaximumBytes)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
        var json = reader.GetString(4);
        var request = DeserializePayload<ColdBootRequest>(json, reader.GetString(5), ColdBootPayloadLimit);
        if (request.Binding?.Maintenance?.Plan?.JobId != jobId ||
            request.Binding.Maintenance.Revision != reader.GetInt32(1) ||
            request.Checkpoint is not { } checkpoint || checkpoint.Id == Guid.Empty ||
            checkpoint.Id.ToString("D") != reader.GetString(2) || checkpoint.RequestedAtUtc == default ||
            checkpoint.RequestedBootSession < request.Binding.Maintenance.BootSession)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
        var authenticator = reader.GetFieldValue<byte[]>(7);
        var authenticated = WindowsRebootAuthenticator.Matches(Encoding.UTF8.GetBytes(json), authenticator, _databaseGuard, ColdBootPurpose);
        return new(reader.GetInt32(0), request, authenticated);
    }

    private async Task<ColdBootBinding?> ReadColdBootBindingAsync(SqliteTransaction transaction,
        MaintenanceCheckpoint expected, CancellationToken token)
    {
        var jobId = expected.Plan.JobId;
        if (expected is not { Phase: MaintenancePhase.FinalVerification, ScanId: { } boundaryScan } ||
            await ReadMaintenanceAsync(transaction, jobId, token).ConfigureAwait(false) != expected)
        {
            return null;
        }

        var plan = await RequireJournalPlanAsync(_connection, transaction, expected.Plan, token).ConfigureAwait(false);
        try
        {
            await RequireCurrentJournalPlanAsync(_connection, transaction, plan, token).ConfigureAwait(false);
            await RequireMaintenanceEvidenceAsync(transaction, plan, token).ConfigureAwait(false);
        }
        catch (JobStoreConflictException)
        {
            return null;
        }

        var histories = await ReadJobJournalHistoriesAsync(transaction, jobId, token).ConfigureAwait(false);
        var history = histories.Single(item => item.Plan.Key == expected.Plan);
        var updates = await ReadWindowsUpdatesAsync(transaction, jobId, token).ConfigureAwait(false);
        if (plan is not { FullMaintenance: true, SyntheticData: false } ||
            history.ActiveApproval?.ApprovalId != expected.ApprovalId || history.Entries.LastOrDefault() is not { } last ||
            MaintenancePhases.RequiresPackageRecovery(histories) || WindowsUpdateHistory.RecoveryBeforePackages(updates) != UpdateRecovery.None)
        {
            return null;
        }
        var afterBoundary = updates.SkipWhile(entry => entry.Id != boundaryScan).Skip(1);
        var lastServicing = afterBoundary.LastOrDefault(entry => entry.Stage is not
            (WindowsUpdateStage.ScanStarted or WindowsUpdateStage.ScanCompleted));
        return new(expected, last.EntryId, histories.Sum(item => item.Entries.Length), lastServicing?.Id);
    }
}
