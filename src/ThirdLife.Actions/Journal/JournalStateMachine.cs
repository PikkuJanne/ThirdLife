using ThirdLife.Actions.Contracts;

namespace ThirdLife.Actions.Journal;

public static class JournalStateMachine
{
    public static bool CanAppend(JournalPlanSnapshot plan, JournalApproval? approval, JournalEntry? previous, JournalEntry candidate)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(candidate);
        var action = plan.Actions.FirstOrDefault(item => item.Request.ActionId == candidate.ActionId);
        if (action is null || candidate.Plan != plan.Key ||
            candidate.PriorState != (previous?.State ?? JournalState.Planned) ||
            (previous is not null && (previous.Plan != plan.Key || previous.ActionId != candidate.ActionId ||
                candidate.OccurredAtUtc < previous.OccurredAtUtc)))
        {
            return false;
        }

        if (candidate.State == JournalState.Skipped && candidate.PriorState == JournalState.Planned)
        {
            return approval is null && candidate.ApprovalId is null && IsDecline(candidate);
        }

        if (approval is null || approval.Plan != plan.Key || candidate.ApprovalId != approval.ApprovalId ||
            candidate.OccurredAtUtc < approval.OccurredAtUtc)
        {
            return false;
        }

        if (candidate.State != JournalState.Approved &&
            candidate.PriorState is JournalState.Started or JournalState.Applied or JournalState.Failed or JournalState.RequiresReview &&
            (candidate.CorrelationId != previous?.CorrelationId || candidate.ApprovalId != previous?.ApprovalId))
        {
            return false;
        }

        if (candidate.State != JournalState.Verified && candidate.Result != JournalResult.NoChangeConfirmed &&
            candidate.Verification is not null)
        {
            return false;
        }

        if (candidate.Checkpoint is JournalCheckpoint.PackageRecoveryVerification or JournalCheckpoint.PackageRebootVerification)
        {
            var expected = candidate.Checkpoint == JournalCheckpoint.PackageRebootVerification
                ? JournalCheckpoint.RestartRequired : JournalCheckpoint.PackageLaunchPendingVerification;
            return IsMachinePackage(action) && previous is
            { State: JournalState.Applied, Actor: JournalActor.PackageBackend, Result: JournalResult.BackendApplied } &&
                previous.Checkpoint == expected && candidate.Actor == JournalActor.Verifier &&
                ((candidate.State == JournalState.Verified && candidate.Result == JournalResult.PackageLaunchVerified &&
                    candidate.Verification == action.Definition.Verification) ||
                    (candidate.State == JournalState.RequiresReview && IsPackageVerificationFailure(candidate.Result)));
        }

        return (candidate.PriorState, candidate.State) switch
        {
            (JournalState.Planned, JournalState.Approved) =>
                IsApproval(candidate, approval) &&
                (candidate.Checkpoint == JournalCheckpoint.None ||
                    (candidate.Checkpoint == JournalCheckpoint.PackagePlanApproval && IsMachinePackage(action)) ||
                    (candidate.Checkpoint == JournalCheckpoint.BrokerApproval && IsBrokerNoOp(action))),
            (JournalState.Approved, JournalState.Approved) =>
                IsApproval(candidate, approval) && candidate.ApprovalId != previous?.ApprovalId &&
                candidate.Checkpoint == previous?.Checkpoint &&
                ((candidate.Checkpoint == JournalCheckpoint.PackagePlanApproval && IsMachinePackage(action)) ||
                    (candidate.Checkpoint == JournalCheckpoint.BrokerApproval && IsBrokerNoOp(action))),
            (JournalState.Failed, JournalState.Approved) =>
                IsApproval(candidate, approval) && candidate.ApprovalId != previous?.ApprovalId &&
                ((candidate.Checkpoint == JournalCheckpoint.BrokerApproval &&
                    previous?.Checkpoint == JournalCheckpoint.BrokerPreDispatchFailure && IsBrokerNoOp(action)) ||
                    (candidate.Checkpoint == JournalCheckpoint.PackagePlanApproval &&
                        previous is
                        {
                            Checkpoint: JournalCheckpoint.PackagePreDispatchFailure, Actor: JournalActor.PackageBackend,
                            Result: JournalResult.NetworkUnavailable or JournalResult.Cancelled
                        } && IsMachinePackage(action))),
            (JournalState.Approved, JournalState.Started) =>
                candidate.Actor == JournalActor.Coordinator && candidate.Result == JournalResult.None &&
                ((candidate.Checkpoint == JournalCheckpoint.DispatchIntent && previous?.Checkpoint is not
                    (JournalCheckpoint.PackagePlanApproval or JournalCheckpoint.BrokerApproval)) ||
                    (candidate.Checkpoint == JournalCheckpoint.PackageDispatchIntent && IsMachinePackage(action) &&
                        previous?.Checkpoint == JournalCheckpoint.PackagePlanApproval &&
                        previous?.ApprovalId == candidate.ApprovalId) ||
                    (candidate.Checkpoint == JournalCheckpoint.BrokerDispatchIntent && IsBrokerNoOp(action) &&
                        previous?.Checkpoint == JournalCheckpoint.BrokerApproval &&
                        previous?.ApprovalId == candidate.ApprovalId)),
            (JournalState.Approved, JournalState.Skipped) => IsDecline(candidate),
            (JournalState.Started, JournalState.Applied) =>
                IsExecutionActor(candidate, previous) && candidate.Result == JournalResult.BackendApplied &&
                (candidate.Checkpoint == JournalCheckpoint.None ||
                    (candidate.Checkpoint == JournalCheckpoint.RestartRequired && action.Definition.Reboot == ActionReboot.MayRequire) ||
                    (candidate.Checkpoint == JournalCheckpoint.PackageLaunchPendingVerification &&
                        IsMachinePackage(action) && previous?.Checkpoint == JournalCheckpoint.PackageDispatchIntent)),
            (JournalState.Started, JournalState.Failed) =>
                (IsExecutionActor(candidate, previous) && candidate.Checkpoint == JournalCheckpoint.None &&
                    (candidate.Result == JournalResult.OperationFailed ||
                        (candidate.Actor == JournalActor.PackageBackend && candidate.Result is JournalResult.BackendUnavailable or
                            JournalResult.AuthorizationDeclined or JournalResult.TrustMismatch or JournalResult.PreconditionChanged))) ||
                (IsMachinePackage(action) && previous?.Checkpoint == JournalCheckpoint.PackageDispatchIntent &&
                    candidate.Actor == JournalActor.PackageBackend && candidate.Checkpoint == JournalCheckpoint.None &&
                    candidate.Result is JournalResult.PackageDownloadUnavailable or JournalResult.PackageElevationUnavailable) ||
                (previous?.Checkpoint == JournalCheckpoint.PackageDispatchIntent && IsMachinePackage(action) &&
                    candidate.Actor == JournalActor.PackageBackend && candidate.Checkpoint == JournalCheckpoint.PackagePreDispatchFailure &&
                    candidate.Result is JournalResult.NetworkUnavailable or JournalResult.Cancelled) ||
                (previous?.Checkpoint == JournalCheckpoint.BrokerDispatchIntent && IsBrokerNoOp(action) &&
                    candidate.Actor == JournalActor.Coordinator && candidate.Checkpoint == JournalCheckpoint.BrokerPreDispatchFailure &&
                    candidate.Result is JournalResult.AuthorizationDeclined or JournalResult.BackendUnavailable or JournalResult.Cancelled),
            (JournalState.Started, JournalState.RequiresReview) =>
                ((candidate.Actor is JournalActor.Coordinator or JournalActor.Broker && IsInterruption(candidate)) ||
                    (previous?.Checkpoint == JournalCheckpoint.PackageDispatchIntent && IsMachinePackage(action) && candidate.Actor == JournalActor.PackageBackend &&
                        candidate.Result is JournalResult.Interrupted or JournalResult.Cancelled or JournalResult.TimedOut or
                            JournalResult.PackageDownloadTimedOut &&
                        candidate.Checkpoint == JournalCheckpoint.Reconcile) ||
                    (previous?.Checkpoint == JournalCheckpoint.BrokerDispatchIntent && IsBrokerNoOp(action) &&
                        candidate.Actor == JournalActor.Coordinator && candidate.Result == JournalResult.Cancelled &&
                        candidate.Checkpoint == JournalCheckpoint.Reconcile)),
            (JournalState.Applied, JournalState.Verified) =>
                candidate.Actor == JournalActor.Verifier &&
                candidate.Result == (previous?.Checkpoint == JournalCheckpoint.PackageLaunchPendingVerification && IsMachinePackage(action)
                    ? JournalResult.PackageLaunchVerified : JournalResult.VerificationPassed) &&
                candidate.Verification == action.Definition.Verification && candidate.Checkpoint == JournalCheckpoint.None &&
                previous?.Checkpoint != JournalCheckpoint.RestartRequired,
            (JournalState.Applied, JournalState.RequiresReview) =>
                (candidate.Actor is JournalActor.Coordinator or JournalActor.Verifier && IsInterruption(candidate)) ||
                (candidate.Actor == JournalActor.Verifier && action.Request is InstallPackageActionRequest &&
                    candidate.Checkpoint == JournalCheckpoint.Reconcile &&
                    (candidate.Result == JournalResult.VerificationUnconfirmed ||
                        (IsMachinePackage(action) && previous?.Checkpoint == JournalCheckpoint.PackageLaunchPendingVerification &&
                            IsPackageVerificationFailure(candidate.Result)))),
            (JournalState.RequiresReview, JournalState.Failed) =>
                candidate.Actor == JournalActor.Verifier && candidate.Result == JournalResult.NoChangeConfirmed &&
                candidate.Checkpoint == JournalCheckpoint.None && candidate.Verification == ActionVerification.NoMachineChange &&
                action.Definition.Availability == ActionAvailability.BrokerNoOp,
            (JournalState.Applied or JournalState.Failed or JournalState.RequiresReview, JournalState.RolledBack) =>
                candidate.Actor == JournalActor.Verifier && candidate.Result == JournalResult.RollbackVerified &&
                candidate.Checkpoint == JournalCheckpoint.None && action.Definition.Rollback == ActionRollback.ManualReview,
            _ => false,
        };
    }

    private static bool IsApproval(JournalEntry entry, JournalApproval approval) =>
        entry.Actor == JournalActor.Operator && entry.ActorId == approval.ActorId &&
        entry.CorrelationId == approval.CorrelationId && entry.Result == JournalResult.None;

    private static bool IsDecline(JournalEntry entry) =>
        entry.Actor == JournalActor.Operator && entry.Result == JournalResult.Declined &&
        entry.Checkpoint == JournalCheckpoint.None && entry.Verification is null;

    private static bool IsInterruption(JournalEntry entry) =>
        entry.Result == JournalResult.Interrupted && entry.Checkpoint == JournalCheckpoint.Reconcile;

    private static bool IsPackageVerificationFailure(JournalResult result) => result is
        JournalResult.PackageProbeUnavailable or JournalResult.PackageProbeExitedEarly or JournalResult.PackageProbeTimedOut or
        JournalResult.PackageProbeCancelled or JournalResult.PackageProbeCleanupUnconfirmed or JournalResult.PackageDetectionAbsent or
        JournalResult.PackageDetectionMismatch or JournalResult.PackageDetectionUnavailable or JournalResult.PackageProbeExecutableMissing or
        JournalResult.PackageProbeExecutableUntrusted or JournalResult.PackageProbeLaunchFailed;

    private static bool IsExecutionActor(JournalEntry entry, JournalEntry? previous) =>
        entry.Actor == (previous?.Checkpoint == JournalCheckpoint.PackageDispatchIntent ? JournalActor.PackageBackend : JournalActor.Broker);

    private static bool IsMachinePackage(JournalAction action) =>
        action.Request is InstallPackageActionRequest { Scope: ActionScope.Machine };

    private static bool IsBrokerNoOp(JournalAction action) =>
        action.Request is NoOpActionRequest && action.Definition.Availability == ActionAvailability.BrokerNoOp;
}
