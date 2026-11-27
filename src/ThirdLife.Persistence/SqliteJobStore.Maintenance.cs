using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IMaintenancePhaseStore
{
    private sealed record SavedMaintenanceBoundary(MaintenanceCheckpoint Checkpoint,
        ImmutableArray<MaintenancePackageObservation> PackageObservations);

    public Task<MaintenanceCheckpoint?> LoadMaintenanceAsync(JobId jobId, CancellationToken token = default) =>
        ReadJournalTransactionAsync((transaction, ct) => ReadMaintenanceAsync(transaction, jobId, ct), token);

    private async Task<MaintenanceCheckpoint?> ReadMaintenanceAsync(SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        if (!_supportsMaintenance) { return null; }
        _layout.ValidateJobDirectory(jobId);
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision, plan_sha256, impact_sha256, approval_id, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM maintenance_checkpoints WHERE job_id = $job_id ORDER BY revision DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) { return null; }
        if (reader.GetInt64(4) is < 2 or > JournalRecordLimit) { throw new JobStoreCorruptionException("store_payload_mismatch"); }

        var saved = DeserializePayload<SavedMaintenanceBoundary>(reader.GetString(5), reader.GetString(6), JournalRecordLimit);
        var checkpoint = saved.Checkpoint;
        if (checkpoint?.Plan?.JobId != jobId || checkpoint.Revision != reader.GetInt64(0) ||
            checkpoint.Plan.PlanSha256 != reader.GetString(1) || checkpoint.Plan.ImpactSha256 != reader.GetString(2) ||
            checkpoint.ApprovalId.ToString("D") != reader.GetString(3) || !Enum.IsDefined(checkpoint.Phase) ||
            saved.PackageObservations.IsDefault || saved.PackageObservations.Length > 4)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
        return checkpoint;
    }

    private async Task StartMaintenanceAsync(SqliteTransaction transaction, JournalPlanSnapshot plan,
        JournalApproval approval, MaintenanceCheckpoint? previous, CancellationToken token)
    {
        if (!_supportsMaintenance || _readBootSession() is not { } boot || approval.OccurredAtUtc > _timeProvider.GetUtcNow())
        {
            throw new JobStoreConflictException();
        }

        await RequireMaintenanceEvidenceAsync(transaction, plan, token).ConfigureAwait(false);
        var revision = (previous?.Revision ?? 0) + 1;
        MaintenanceCheckpoint next;
        if (previous?.Plan == plan.Key && previous.Phase is MaintenancePhase.WindowsUpdates or MaintenancePhase.Packages)
        {
            next = previous with { Revision = revision, ApprovalId = approval.ApprovalId };
        }
        else
        {
            var histories = await ReadJobJournalHistoriesAsync(transaction, plan.Key.JobId, token).ConfigureAwait(false);
            if (MaintenancePhases.RequiresPackageRecovery(histories)) { throw new JobStoreConflictException(); }
            var updates = await ReadWindowsUpdatesAsync(transaction, plan.Key.JobId, token).ConfigureAwait(false);
            var latest = updates.LastOrDefault()?.Id;
            next = new(plan.Key, approval.ApprovalId, MaintenancePhase.WindowsUpdates, revision, boot, boot, latest);
        }
        await InsertMaintenanceAsync(transaction, next, [], token).ConfigureAwait(false);
    }

    public Task<MaintenanceCheckpoint> AdvanceMaintenanceAsync(MaintenanceCheckpoint expected, Guid scanId,
        ImmutableArray<MaintenancePackageObservation> packageObservations = default, CancellationToken token = default) =>
        CheckMaintenanceBoundaryAsync(expected, scanId, packageObservations, null, token);

    public Task<MaintenanceCheckpoint> RecordMaintenanceCheckFailureAsync(MaintenanceCheckpoint expected, Guid scanId,
        MaintenanceCheckFailure failure, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return CheckMaintenanceBoundaryAsync(expected, scanId, [], failure, token);
    }

    private async Task<MaintenanceCheckpoint> CheckMaintenanceBoundaryAsync(MaintenanceCheckpoint expected, Guid scanId,
        ImmutableArray<MaintenancePackageObservation> packageObservations, MaintenanceCheckFailure? failure, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (packageObservations.IsDefault) { packageObservations = []; }
        MaintenanceCheckpoint? advanced = null;
        await ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            var jobId = expected.Plan.JobId;
            _layout.ValidateJobDirectory(jobId);
            if (await ReadMaintenanceAsync(transaction, jobId, ct).ConfigureAwait(false) != expected ||
                expected.Phase == MaintenancePhase.FinalVerification || _readBootSession() is not { } boot ||
                boot < expected.BootSession)
            {
                throw new JobStoreConflictException();
            }
            var plan = await RequireJournalPlanAsync(connection, transaction, expected.Plan, ct).ConfigureAwait(false);
            await RequireCurrentJournalPlanAsync(connection, transaction, plan, ct).ConfigureAwait(false);
            await RequireMaintenanceEvidenceAsync(transaction, plan, ct).ConfigureAwait(false);
            var packages = await ReadJobJournalHistoriesAsync(transaction, jobId, ct).ConfigureAwait(false);
            var exact = packages.Single(history => history.Plan.Key == plan.Key);
            var updates = await ReadWindowsUpdatesAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (!plan.FullMaintenance || exact.ActiveApproval?.ApprovalId != expected.ApprovalId ||
                MaintenancePhases.RequiresPackageRecovery(packages) || updates.LastOrDefault()?.Id != scanId ||
                scanId == expected.ScanId || !IsCurrentMaintenanceScan(updates, plan, boot) || updates[^2].Id == expected.ScanId)
            {
                throw new JobStoreConflictException();
            }

            var disposition = WindowsUpdateHistory.Disposition(updates);
            if (failure is not null)
            {
                if (expected.Phase is not (MaintenancePhase.PostPackageChecks or MaintenancePhase.FinalRestart) ||
                    (expected.Phase == MaintenancePhase.FinalRestart && !MaintenancePhases.ConfirmsFinalRestart(expected, boot)) ||
                    disposition is not (WindowsUpdateDisposition.Current or WindowsUpdateDisposition.NoEligibleOffers) ||
                    !Enum.IsDefined(failure.Issue) || !plan.Actions.Any(action => action.ActionId == failure.ActionId) ||
                    exact.StateOf(failure.ActionId) != JournalState.Verified)
                {
                    throw new JobStoreConflictException();
                }
                // Retain the pre-restart boot when a check on a later boot fails. A new observation
                // on that same later boot can settle it without requiring an unnecessary restart.
                advanced = expected with { Revision = expected.Revision + 1, ScanId = scanId, LastCheckFailure = failure };
            }
            else
            {
                var next = MaintenancePhases.Next(expected, boot, disposition, exact, packageObservations) ?? throw new JobStoreConflictException();
                advanced = expected with
                {
                    Revision = expected.Revision + 1,
                    Phase = next,
                    ScanId = scanId,
                    BootSession = boot,
                    LastCheckFailure = null,
                };
            }
            await InsertMaintenanceAsync(transaction, advanced, packageObservations, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return advanced!;
    }

    private async Task RequireMaintenancePackageDispatchAsync(SqliteTransaction transaction, JournalPlanSnapshot plan,
        JournalApproval approval, ImmutableArray<WindowsUpdateEntry> updates, CancellationToken token)
    {
        var current = await ReadMaintenanceAsync(transaction, plan.Key.JobId, token).ConfigureAwait(false);
        if (current is null || current.Phase == MaintenancePhase.FinalVerification)
        {
            if (plan.FullMaintenance) { throw new JobStoreConflictException(); }
            return;
        }

        await RequireMaintenanceEvidenceAsync(transaction, plan, token).ConfigureAwait(false);
        if (current.Plan != plan.Key || current.ApprovalId != approval.ApprovalId || current.Phase != MaintenancePhase.Packages ||
            _readBootSession() is not { } boot || !MaintenancePhases.WithinRestartLimit(current, boot) ||
            !IsCurrentMaintenanceScan(updates, plan, boot) ||
            WindowsUpdateHistory.Disposition(updates) is not (WindowsUpdateDisposition.Current or WindowsUpdateDisposition.NoEligibleOffers))
        {
            throw new JobStoreConflictException();
        }
    }

    private async Task RequireMaintenanceEvidenceAsync(SqliteTransaction transaction, JournalPlanSnapshot plan, CancellationToken token)
    {
        if (plan.ReviewedEvidenceCount is not { } reviewed ||
            reviewed != await ReadEvidenceCountAsync(_connection, transaction, plan.Key.JobId, token).ConfigureAwait(false))
        {
            throw new JobStoreConflictException();
        }
    }

    private bool IsCurrentMaintenanceScan(ImmutableArray<WindowsUpdateEntry> updates, JournalPlanSnapshot plan, uint boot)
    {
        var now = _timeProvider.GetUtcNow();
        return updates is [.., { Stage: WindowsUpdateStage.ScanStarted } started, { Stage: WindowsUpdateStage.ScanCompleted } completed] &&
            started.BootSession == boot && completed.BootSession == boot && completed.GateId == plan.GateDecisionId &&
            now >= completed.OccurredAtUtc && now - completed.OccurredAtUtc <= TimeSpan.FromMinutes(10);
    }

    private async Task InsertMaintenanceAsync(SqliteTransaction transaction, MaintenanceCheckpoint checkpoint,
        ImmutableArray<MaintenancePackageObservation> observations, CancellationToken token)
    {
        if (checkpoint.Revision > JournalHistory.MaximumEntriesPerJob) { throw new JobStoreConflictException(); }
        await using var command = JournalCommand(_connection, transaction, checkpoint.Plan, """
            INSERT INTO maintenance_checkpoints(job_id, revision, plan_sha256, impact_sha256, approval_id, payload_json, payload_sha256)
            VALUES ($job_id, $revision, $plan, $impact, $id, $json, $sha);
            """);
        command.Parameters.AddWithValue("$revision", checkpoint.Revision);
        AddJournalPayload(command, checkpoint.ApprovalId, SerializePayload(new SavedMaintenanceBoundary(checkpoint, observations), JournalRecordLimit));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
