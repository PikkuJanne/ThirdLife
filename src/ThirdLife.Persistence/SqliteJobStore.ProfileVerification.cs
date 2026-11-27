using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Verification;

namespace ThirdLife.Persistence;

public interface IProfileVerificationStore
{
    Task SaveProfileVerificationAsync(ProfileVerificationRecord record, CancellationToken token = default);
    Task<SavedProfileVerification?> LoadProfileVerificationAsync(JobId jobId, CancellationToken token = default);
    Task<ColdBootCheckpoint> RecordColdBootCheckpointAsync(MaintenanceCheckpoint expected, CancellationToken token = default);
    Task<SavedColdBootVerification?> LoadColdBootVerificationAsync(JobId jobId, CancellationToken token = default);
}

public sealed record ProfileVerificationRecord(MaintenanceCheckpoint Checkpoint, Guid ScanId,
    Guid PackageJournalEntryId, int JournalEntryCount, ProfileVerificationResult Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ColdBootCheckpointId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ColdBootObservation? ColdBootObservation = null);

public sealed record SavedProfileVerification(ProfileVerificationRecord Record, bool IsCurrent)
{
    public bool HasPassedPostBootVerification => IsCurrent && Record.ColdBootObservation is not null &&
        Record.Result.ProfileChecksPassed;
}

public sealed partial class SqliteJobStore : IProfileVerificationStore
{
    private const int MaximumProfileVerifications = 64;

    public Task SaveProfileVerificationAsync(ProfileVerificationRecord record, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            if (record.ColdBootObservation is not null) { throw new JobStoreConflictException(); }
            if (record.ColdBootCheckpointId is { } id)
            {
                if (!_supportsColdBoot || _readBootSession() is not { } boot ||
                    await ReadColdBootRequestAsync(transaction, record.Checkpoint.Plan.JobId, ct).ConfigureAwait(false) is not
                    { Authenticated: true } row || row.Request.Checkpoint.Id != id)
                {
                    throw new JobStoreConflictException();
                }

                var now = _timeProvider.GetUtcNow();
                var checkpoint = row.Request.Checkpoint;
                var observation = checkpoint.CanObserve(boot, now) ? ReadColdBootObservation(checkpoint, boot, now) : null;
                if (!checkpoint.Matches(observation, boot, now)) { throw new JobStoreConflictException(); }
                record = record with { ColdBootObservation = observation };
            }
            if (!_supportsProfileVerification ||
                !await IsCurrentProfileVerificationAsync(transaction, record, ct).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }

            var previous = await ReadProfileVerificationAsync(transaction, record.Checkpoint.Plan.JobId, ct).ConfigureAwait(false);
            var sequence = (previous?.Sequence ?? 0) + 1;
            if (sequence > MaximumProfileVerifications || previous?.Record.ScanId == record.ScanId)
            {
                throw new JobStoreConflictException();
            }

            var payload = SerializePayload(record);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO profile_verifications(job_id, sequence, maintenance_revision, scan_id, payload_json, payload_sha256)
                VALUES ($job, $sequence, $revision, $scan, $payload, $sha);
                """;
            command.Parameters.AddWithValue("$job", record.Checkpoint.Plan.JobId.Value);
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$revision", record.Checkpoint.Revision);
            command.Parameters.AddWithValue("$scan", record.ScanId.ToString("D"));
            command.Parameters.AddWithValue("$payload", payload.Json);
            command.Parameters.AddWithValue("$sha", payload.Sha256);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, token);
    }

    public Task<SavedProfileVerification?> LoadProfileVerificationAsync(JobId jobId, CancellationToken token = default) =>
        ReadJournalTransactionAsync<SavedProfileVerification?>(async (transaction, ct) =>
        {
            if (!_supportsProfileVerification) { return null; }
            var saved = await ReadProfileVerificationAsync(transaction, jobId, ct).ConfigureAwait(false);
            return saved is { } row
                ? new(row.Record, await IsCurrentProfileVerificationAsync(transaction, row.Record, ct).ConfigureAwait(false))
                : null;
        }, token);

    private async Task<(int Sequence, ProfileVerificationRecord Record)?> ReadProfileVerificationAsync(
        SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        _layout.ValidateJobDirectory(jobId);
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sequence, maintenance_revision, scan_id, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM profile_verifications WHERE job_id = $job ORDER BY sequence DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) { return null; }
        if (reader.GetInt64(3) is < 2 or > MaximumPayloadBytes)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        var record = DeserializePayload<ProfileVerificationRecord>(reader.GetString(4), reader.GetString(5));
        if (record.Checkpoint?.Plan?.JobId != jobId || record.Checkpoint.Phase != MaintenancePhase.FinalVerification ||
            record.Checkpoint.Revision != reader.GetInt64(1) || record.ScanId.ToString("D") != reader.GetString(2) ||
            record.ScanId == record.Checkpoint.ScanId || record.PackageJournalEntryId == Guid.Empty ||
            record.JournalEntryCount is < 1 or > JournalHistory.MaximumEntriesPerJob || record.Result is null ||
            record.ColdBootCheckpointId == Guid.Empty ||
            record.ColdBootCheckpointId.HasValue != (record.ColdBootObservation is not null) ||
            record.ColdBootObservation is { Kind: not ColdBootKind.OrderlyPowerOff })
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
        return (reader.GetInt32(0), record);
    }

    private async Task<bool> IsCurrentProfileVerificationAsync(SqliteTransaction transaction, ProfileVerificationRecord record,
        CancellationToken token)
    {
        if (record.Checkpoint is not { Plan: { } key, Phase: MaintenancePhase.FinalVerification, ScanId: { } boundaryScan } expected ||
            record.ScanId == boundaryScan || record.Result is null || _readBootSession() is not { } boot || boot < expected.BootSession ||
            record.Result.FinishedAtUtc > _timeProvider.GetUtcNow() ||
            await ReadMaintenanceAsync(transaction, key.JobId, token).ConfigureAwait(false) != expected)
        {
            return false;
        }

        var plan = await RequireJournalPlanAsync(_connection, transaction, key, token).ConfigureAwait(false);
        try
        {
            await RequireCurrentJournalPlanAsync(_connection, transaction, plan, token).ConfigureAwait(false);
            await RequireMaintenanceEvidenceAsync(transaction, plan, token).ConfigureAwait(false);
        }
        catch (JobStoreConflictException)
        {
            return false;
        }

        var histories = await ReadJobJournalHistoriesAsync(transaction, key.JobId, token).ConfigureAwait(false);
        var history = histories.Single(item => item.Plan.Key == key);
        var updates = await ReadWindowsUpdatesAsync(transaction, key.JobId, token).ConfigureAwait(false);
        var boundary = updates.SingleOrDefault(entry => entry.Id == boundaryScan);
        return plan is { FullMaintenance: true, SyntheticData: false } && history.ActiveApproval?.ApprovalId == expected.ApprovalId &&
            history.Entries.LastOrDefault()?.EntryId == record.PackageJournalEntryId &&
            histories.Sum(item => item.Entries.Length) == record.JournalEntryCount &&
            !MaintenancePhases.RequiresPackageRecovery(histories) && !record.Result.SyntheticData &&
            record.Result.ProfileSha256 == plan.ProfileSha256 && MatchesPackageVerification(record, history) &&
            updates is [.., { Stage: WindowsUpdateStage.ScanStarted } started, { Stage: WindowsUpdateStage.ScanCompleted } completed] &&
            started.BootSession == boot && completed.BootSession == boot && completed.GateId == plan.GateDecisionId &&
            completed.Id == record.ScanId && boundary?.OccurredAtUtc <= record.Result.StartedAtUtc &&
            started.OccurredAtUtc >= record.Result.StartedAtUtc && completed.OccurredAtUtc <= record.Result.FinishedAtUtc &&
            (!record.Result.ProfileChecksPassed || WindowsUpdateHistory.Disposition(updates) == WindowsUpdateDisposition.Current) &&
            await MatchesSavedColdBootAsync(transaction, record, boot, token).ConfigureAwait(false);
    }

    private async Task<bool> MatchesSavedColdBootAsync(SqliteTransaction transaction, ProfileVerificationRecord record,
        uint boot, CancellationToken token)
    {
        if (record.ColdBootCheckpointId is not { } id)
        {
            return record.ColdBootObservation is null && record.Result.Capabilities.All(capability => capability.LaunchProbe is null);
        }

        if (!_supportsColdBoot ||
            await ReadColdBootRequestAsync(transaction, record.Checkpoint.Plan.JobId, token).ConfigureAwait(false) is not
            { Authenticated: true } row || row.Request.Checkpoint.Id != id ||
            row.Request.Binding != await ReadColdBootBindingAsync(transaction, record.Checkpoint, token).ConfigureAwait(false))
        {
            return false;
        }

        return row.Request.Checkpoint.Matches(record.ColdBootObservation, boot, record.Result.StartedAtUtc) &&
            row.Request.Checkpoint.CanObserve(boot, record.Result.FinishedAtUtc) &&
            record.Result.Capabilities.All(capability => capability.ActionId.HasValue == capability.LaunchProbe.HasValue);
    }

    private static bool MatchesPackageVerification(ProfileVerificationRecord record, JournalHistory history)
    {
        foreach (var action in history.Plan.Actions)
        {
            if (action.Request is not InstallPackageActionRequest request) { return false; }
            var row = record.Result.Capabilities.SingleOrDefault(capability => capability.CapabilityId == action.CapabilityId);
            var latest = history.Entries.LastOrDefault(entry => entry.ActionId == action.ActionId);
            if (row is null || row.ActionId != action.ActionId || row.ExpectedVersion != request.ExactVersion ||
                row.Essential != (action.Intent == CapabilityIntent.Essential) ||
                (row.State == ProfileVerificationState.Passed &&
                    (latest is not { State: JournalState.Verified } || latest.ApprovalId != record.Checkpoint.ApprovalId)))
            {
                return false;
            }
        }
        return record.Result.Capabilities.All(row => row.ActionId is not { } id || history.Plan.Actions.Any(action => action.ActionId == id));
    }
}
