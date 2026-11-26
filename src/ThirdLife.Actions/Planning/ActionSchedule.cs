using System.Collections.Immutable;
using ThirdLife.Actions.Journal;
using ThirdLife.Catalog.Profiles;

namespace ThirdLife.Actions.Planning;

/// <summary>Orders the actions of an exact plan snapshot and projects their saved progress.</summary>
public sealed class ActionSchedule
{
    private const int MaximumPhase = 4;

    internal ActionSchedule(JournalPlanKey planKey, ImmutableArray<ScheduledAction> steps)
    {
        PlanKey = planKey;
        Steps = steps;
    }

    public JournalPlanKey PlanKey { get; }
    public ImmutableArray<ScheduledAction> Steps { get; }

    public static ActionSchedule Create(JournalPlanSnapshot plan, IEnumerable<ScheduledAction> steps)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ordered = Order(plan.Actions.Select(action => action.ActionId), steps);
        if (plan.Schedule is { } bound)
        {
            if (!SameSteps(ordered, bound.Steps))
            {
                throw new ArgumentException("The schedule differs from the exact saved plan.", nameof(steps));
            }

            return bound;
        }

        return new ActionSchedule(plan.Key, ordered);
    }

    internal static ImmutableArray<ScheduledAction> Order(IEnumerable<Guid> actionIds, IEnumerable<ScheduledAction> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var reviewedOrder = actionIds.ToImmutableArray();
        var planOrder = reviewedOrder.Select((actionId, index) => (ActionId: actionId, index))
            .ToDictionary(action => action.ActionId, action => action.index);
        var byAction = new Dictionary<Guid, ScheduledAction>();
        foreach (var step in steps)
        {
            if (byAction.Count == planOrder.Count || step is null ||
                !planOrder.ContainsKey(step.ActionId) || !byAction.TryAdd(step.ActionId, step))
            {
                throw new ArgumentException("Supply exactly one schedule step for every action in the plan.", nameof(steps));
            }

            if (step.Phase is < 1 or > MaximumPhase || step.Dependencies.IsDefault ||
                step.Dependencies.Length >= planOrder.Count)
            {
                throw new ArgumentException("The action phase or dependency count is outside its bounds.", nameof(steps));
            }
        }

        if (byAction.Count != planOrder.Count)
        {
            throw new ArgumentException("Supply exactly one schedule step for every action in the plan.", nameof(steps));
        }

        foreach (var step in byAction.Values)
        {
            if (step.Dependencies.Distinct().Count() != step.Dependencies.Length ||
                step.Dependencies.Any(id => id == step.ActionId || !byAction.TryGetValue(id, out var dependency) ||
                    dependency.Phase > step.Phase))
            {
                throw new ArgumentException("Each dependency must be a distinct action in the same or an earlier phase.", nameof(steps));
            }
        }

        // Preserve the reviewed plan order for actions whose dependencies allow either order.
        var remaining = reviewedOrder.Select(id => byAction[id]).OrderBy(step => step.Phase).ToList();
        var ordered = ImmutableArray.CreateBuilder<ScheduledAction>(remaining.Count);
        var placed = new HashSet<Guid>();
        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(step => step.Dependencies.All(placed.Contains));
            if (index < 0)
            {
                throw new ArgumentException("The action dependencies contain a cycle.", nameof(steps));
            }

            var step = remaining[index];
            ordered.Add(step with { Dependencies = step.Dependencies.OrderBy(id => planOrder[id]).ToImmutableArray() });
            placed.Add(step.ActionId);
            remaining.RemoveAt(index);
        }

        return ordered.MoveToImmutable();
    }

    internal static bool SameSteps(ImmutableArray<ScheduledAction> left, ImmutableArray<ScheduledAction> right) =>
        left.Length == right.Length && left.Zip(right).All(pair => pair.First.ActionId == pair.Second.ActionId &&
            pair.First.Phase == pair.Second.Phase && pair.First.Dependencies.SequenceEqual(pair.Second.Dependencies));

    /// <summary>Projects the latest committed state of each step in this exact saved plan.</summary>
    public ActionScheduleProjection Project(JournalHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Plan.Key != PlanKey ||
            (history.Plan.Schedule is { } bound && !SameSteps(Steps, bound.Steps)))
        {
            throw new ArgumentException("The journal belongs to a different plan or impact snapshot.", nameof(history));
        }

        var latest = new Dictionary<Guid, JournalEntry>();
        foreach (var entry in history.Entries)
        {
            latest[entry.ActionId] = entry;
        }

        var requiresRecovery = latest.Values.Any(entry =>
            entry.State is JournalState.Started or JournalState.Applied or JournalState.RequiresReview);
        var approval = history.ActiveApproval;
        var requiresFreshApproval = history.Plan.Schedule is not null && approval is not null && latest.Values.Any(entry =>
            entry.ApprovalId == approval.ApprovalId &&
            (entry.Checkpoint == JournalCheckpoint.BrokerPreDispatchFailure ||
                entry is { Checkpoint: JournalCheckpoint.PackagePreDispatchFailure, Result: JournalResult.PackageDispatchAuthorizationChanged } or
                { State: JournalState.Failed, Actor: JournalActor.PackageBackend, Result: JournalResult.AuthorizationDeclined }));
        var progress = ImmutableArray.CreateBuilder<ScheduledActionProgress>(Steps.Length);
        var optionalActions = history.Plan.Actions.Where(action => action.Intent == CapabilityIntent.Optional)
            .ToDictionary(action => action.ActionId);
        Guid? nextForReview = null;
        foreach (var step in Steps)
        {
            var entry = latest.GetValueOrDefault(step.ActionId);
            var pending = step.Dependencies.Where(id => !IsVerified(id)).ToImmutableArray();
            var earlierPhasePending = Steps.Any(previous => previous.Phase < step.Phase && !PhaseActionResolved(previous.ActionId));
            progress.Add(new ScheduledActionProgress(step.ActionId, entry?.State ?? JournalState.Planned,
                entry?.Checkpoint ?? JournalCheckpoint.None, entry?.Result ?? JournalResult.None, pending, earlierPhasePending));
            if (nextForReview is null && history.Plan.IsCurrentPolicyPlan && !requiresRecovery && !requiresFreshApproval &&
                pending.IsEmpty && !earlierPhasePending &&
                entry is { State: JournalState.Approved } && approval is not null &&
                approval.ApprovalId == entry.ApprovalId)
            {
                nextForReview = step.ActionId;
            }
        }

        return new ActionScheduleProjection(progress.MoveToImmutable(), nextForReview, requiresRecovery, requiresFreshApproval);

        bool IsVerified(Guid id) => latest.GetValueOrDefault(id)?.State == JournalState.Verified;

        bool PhaseActionResolved(Guid id) => IsVerified(id) ||
            (optionalActions.TryGetValue(id, out var action) && latest.TryGetValue(id, out var entry) &&
                PreparationDisposition.IsSettledOmission(action, entry));
    }

    /// <summary>Checks the graph prerequisites and active approval for a selected failed package step.</summary>
    /// <remarks>The store must also check job-wide recovery, retry eligibility and the prior attempt inside the write transaction.</remarks>
    public bool CanRetryPackageAction(JournalHistory history, Guid actionId)
    {
        var projection = Project(history);
        if (!JournalStateMachine.IsScheduledPackagePlan(history.Plan) || projection.RequiresRecovery ||
            projection.RequiresFreshApproval || history.ActiveApproval is not { } approval)
        {
            return false;
        }

        var latest = history.Entries.LastOrDefault(entry => entry.ActionId == actionId);
        return latest is not null && latest.ApprovalId == approval.ApprovalId && PackageRetryPolicy.IsRetryableFailure(latest) &&
            projection.Actions.Any(action => action.ActionId == actionId && !action.EarlierPhasePending && action.PendingDependencies.IsEmpty);
    }
}

public sealed record ScheduledAction(Guid ActionId, int Phase, ImmutableArray<Guid> Dependencies);

public sealed record ScheduledActionProgress(Guid ActionId, JournalState State, JournalCheckpoint Checkpoint,
    JournalResult Result, ImmutableArray<Guid> PendingDependencies, bool EarlierPhasePending);

/// <param name="Actions">Durable progress in schedule order.</param>
/// <param name="NextForReview">The first dependency-ready action matching the saved active approval. Backend and job-wide admission still apply.</param>
/// <param name="RequiresRecovery">The journal contains a started, applied or review-required action.</param>
/// <param name="RequiresFreshApproval">The active approval needs renewal before another action can be dispatched.</param>
public sealed record ActionScheduleProjection(ImmutableArray<ScheduledActionProgress> Actions, Guid? NextForReview, bool RequiresRecovery,
    bool RequiresFreshApproval);
