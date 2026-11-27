using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.Planning;

public static class MaintenancePhases
{
    public const int PackagePhase = 3;
    public const int MaximumPreFinalRestarts = 3;
    internal static readonly ImmutableArray<string> ReviewedSequence =
        ["windows_updates", "required_update_restarts", "packages", "post_package_checks", "final_restart", "final_verification"];

    // An approved package has not started. Only an outstanding package attempt holds
    // Windows servicing. Keep old plans in scope when their result is unresolved.
    public static bool RequiresPackageRecovery(ImmutableArray<JournalHistory> histories) =>
        histories.Where(history => !history.Plan.SyntheticData).Any(history => history.Plan.Actions.Any(action =>
            action.Request is InstallPackageActionRequest &&
            history.StateOf(action.ActionId) is JournalState.Started or JournalState.Applied or JournalState.RequiresReview));

    public static bool WithinRestartLimit(MaintenanceCheckpoint current, uint? boot) =>
        boot >= current.BootSession && boot - current.InitialBootSession <= MaximumPreFinalRestarts;

    public static bool ConfirmsFinalRestart(MaintenanceCheckpoint current, uint boot) =>
        boot > current.BootSession && boot - current.InitialBootSession <= MaximumPreFinalRestarts + 1;

    // The store supplies the latest current-boot scan and reads the exact journal under its
    // transaction. Only an explicit operator continuation can record the next boundary.
    public static MaintenancePhase? Next(MaintenanceCheckpoint current, uint boot, WindowsUpdateDisposition updates,
        JournalHistory packages, ImmutableArray<MaintenancePackageObservation> observations)
    {
        if (observations.IsDefault || packages.Plan.Key != current.Plan || !packages.Plan.FullMaintenance ||
            packages.ActiveApproval?.ApprovalId != current.ApprovalId || boot < current.BootSession ||
            current.Phase == MaintenancePhase.FinalVerification)
        {
            return null;
        }

        if (updates == WindowsUpdateDisposition.ApprovalRequired && current.Phase != MaintenancePhase.WindowsUpdates)
        {
            // A later eligible offer goes back through normal exact-revision review.
            return WithinRestartLimit(current, boot) && observations.IsEmpty ? MaintenancePhase.WindowsUpdates : null;
        }

        if (updates is not (WindowsUpdateDisposition.Current or WindowsUpdateDisposition.NoEligibleOffers) ||
            (current.Phase == MaintenancePhase.FinalRestart ? !ConfirmsFinalRestart(current, boot) : !WithinRestartLimit(current, boot)) ||
            (current.Phase != MaintenancePhase.WindowsUpdates && PreparationDisposition.Evaluate(packages).Disposition != Disposition.ReadyToPrepare))
        {
            return null;
        }

        // Both sides of the final restart require current observations for the verified
        // targets. The action journal retains any settled optional omissions separately.
        var expected = current.Phase is MaintenancePhase.PostPackageChecks or MaintenancePhase.FinalRestart
            ? packages.Plan.Actions.Where(action => packages.StateOf(action.ActionId) == JournalState.Verified)
                .Select(action => (InstallPackageActionRequest)action.Request)
                .Select(action => new MaintenancePackageObservation(action.ActionId, action.ExactVersion))
            : [];
        if (observations.Length > packages.Plan.Actions.Length || observations.Any(row => row is null) ||
            !expected.OrderBy(row => row.ActionId).SequenceEqual(observations.OrderBy(row => row.ActionId)))
        {
            return null;
        }

        return current.Phase switch
        {
            MaintenancePhase.WindowsUpdates => MaintenancePhase.Packages,
            MaintenancePhase.Packages => MaintenancePhase.PostPackageChecks,
            MaintenancePhase.PostPackageChecks => MaintenancePhase.FinalRestart,
            MaintenancePhase.FinalRestart => MaintenancePhase.FinalVerification,
            _ => null,
        };
    }
}

public enum MaintenancePhase
{
    WindowsUpdates = 1,
    Packages,
    PostPackageChecks,
    FinalRestart,
    FinalVerification,
}

public sealed record MaintenanceCheckpoint(JournalPlanKey Plan, Guid ApprovalId, MaintenancePhase Phase,
    int Revision, uint BootSession, uint InitialBootSession, Guid? ScanId, MaintenanceCheckFailure? LastCheckFailure = null);

public enum MaintenanceCheckIssue
{
    PackageMismatch = 1,
    PackageUnavailable,
}

public sealed record MaintenanceCheckFailure(Guid ActionId, MaintenanceCheckIssue Issue);

public sealed record MaintenancePackageObservation(Guid ActionId, string ExactVersion);

public interface IMaintenancePhaseStore
{
    Task<JournalWriteResult> RecordMaintenanceApprovalAsync(JournalApproval approval, ImmutableArray<JournalEntry> entries,
        CancellationToken token = default);

    Task<MaintenanceCheckpoint?> LoadMaintenanceAsync(JobId jobId, CancellationToken token = default);

    Task<MaintenanceCheckpoint> RecordMaintenanceCheckFailureAsync(MaintenanceCheckpoint expected, Guid scanId,
        MaintenanceCheckFailure failure, CancellationToken token = default);

    Task<MaintenanceCheckpoint> AdvanceMaintenanceAsync(MaintenanceCheckpoint expected, Guid scanId,
        ImmutableArray<MaintenancePackageObservation> packageObservations = default, CancellationToken token = default);
}
