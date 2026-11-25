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

        if (candidate.PriorState is JournalState.Started or JournalState.Applied or JournalState.Failed or JournalState.RequiresReview &&
            (candidate.CorrelationId != previous?.CorrelationId || candidate.ApprovalId != previous?.ApprovalId))
        {
            return false;
        }

        if (candidate.State != JournalState.Verified && candidate.Verification is not null)
        {
            return false;
        }

        return (candidate.PriorState, candidate.State) switch
        {
            (JournalState.Planned, JournalState.Approved) =>
                candidate.Actor == JournalActor.Operator && candidate.ActorId == approval.ActorId &&
                candidate.CorrelationId == approval.CorrelationId &&
                candidate.Result == JournalResult.None && candidate.Checkpoint == JournalCheckpoint.None,
            (JournalState.Approved, JournalState.Started) =>
                candidate.Actor == JournalActor.Coordinator && candidate.Result == JournalResult.None &&
                candidate.Checkpoint == JournalCheckpoint.DispatchIntent,
            (JournalState.Approved, JournalState.Skipped) => IsDecline(candidate),
            (JournalState.Started, JournalState.Applied) =>
                candidate.Actor == JournalActor.Broker && candidate.Result == JournalResult.BackendApplied &&
                (candidate.Checkpoint == JournalCheckpoint.None ||
                    (candidate.Checkpoint == JournalCheckpoint.RestartRequired && action.Definition.Reboot == ActionReboot.MayRequire)),
            (JournalState.Started, JournalState.Failed) =>
                candidate.Actor == JournalActor.Broker && candidate.Result == JournalResult.OperationFailed &&
                candidate.Checkpoint == JournalCheckpoint.None,
            (JournalState.Started, JournalState.RequiresReview) =>
                candidate.Actor is JournalActor.Coordinator or JournalActor.Broker && IsInterruption(candidate),
            (JournalState.Applied, JournalState.Verified) =>
                candidate.Actor == JournalActor.Verifier && candidate.Result == JournalResult.VerificationPassed &&
                candidate.Verification == action.Definition.Verification && candidate.Checkpoint == JournalCheckpoint.None,
            (JournalState.Applied, JournalState.RequiresReview) =>
                candidate.Actor is JournalActor.Coordinator or JournalActor.Verifier && IsInterruption(candidate),
            (JournalState.Applied or JournalState.Failed or JournalState.RequiresReview, JournalState.RolledBack) =>
                candidate.Actor == JournalActor.Verifier && candidate.Result == JournalResult.RollbackVerified &&
                candidate.Checkpoint == JournalCheckpoint.None && action.Definition.Rollback == ActionRollback.ManualReview,
            _ => false,
        };
    }

    private static bool IsDecline(JournalEntry entry) =>
        entry.Actor == JournalActor.Operator && entry.Result == JournalResult.Declined &&
        entry.Checkpoint == JournalCheckpoint.None && entry.Verification is null;

    private static bool IsInterruption(JournalEntry entry) =>
        entry.Result == JournalResult.Interrupted && entry.Checkpoint == JournalCheckpoint.Reconcile;
}
