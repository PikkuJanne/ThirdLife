using System.Collections.Immutable;
using ThirdLife.Actions.Journal;

namespace ThirdLife.Actions.Planning;

/// <summary>Validates and orders the actions of an exact plan snapshot for review.</summary>
/// <remarks>The schedule itself is outside the current approval contract and confers no authority to dispatch.</remarks>
public sealed class ActionSchedule
{
    private const int MaximumPhase = 4;

    private ActionSchedule(JournalPlanKey planKey, ImmutableArray<ScheduledAction> steps)
    {
        PlanKey = planKey;
        Steps = steps;
    }

    public JournalPlanKey PlanKey { get; }
    public ImmutableArray<ScheduledAction> Steps { get; }

    public static ActionSchedule Create(JournalPlanSnapshot plan, IEnumerable<ScheduledAction> steps)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(steps);
        var planOrder = plan.Actions.Select((action, index) => (action.ActionId, index))
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
        var remaining = plan.Actions.Select(action => byAction[action.ActionId]).OrderBy(step => step.Phase).ToList();
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

        return new ActionSchedule(plan.Key, ordered.MoveToImmutable());
    }

    /// <summary>Projects the latest committed state of each step in this exact saved plan.</summary>
    public ActionScheduleProjection Project(JournalHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Plan.Key != PlanKey)
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
        var progress = ImmutableArray.CreateBuilder<ScheduledActionProgress>(Steps.Length);
        Guid? nextForReview = null;
        foreach (var step in Steps)
        {
            var entry = latest.GetValueOrDefault(step.ActionId);
            var pending = step.Dependencies.Where(id => !IsVerified(id)).ToImmutableArray();
            var earlierPhasePending = Steps.Any(previous => previous.Phase < step.Phase && !IsVerified(previous.ActionId));
            progress.Add(new ScheduledActionProgress(step.ActionId, entry?.State ?? JournalState.Planned,
                entry?.Checkpoint ?? JournalCheckpoint.None, entry?.Result ?? JournalResult.None, pending, earlierPhasePending));
            if (nextForReview is null && history.Plan.IsCurrentPolicyPlan && !requiresRecovery &&
                pending.IsEmpty && !earlierPhasePending &&
                entry is { State: JournalState.Approved } && history.ActiveApproval is { } approval &&
                approval.ApprovalId == entry.ApprovalId)
            {
                nextForReview = step.ActionId;
            }
        }

        return new ActionScheduleProjection(progress.MoveToImmutable(), nextForReview, requiresRecovery);

        bool IsVerified(Guid id) => latest.GetValueOrDefault(id)?.State == JournalState.Verified;
    }
}

public sealed record ScheduledAction(Guid ActionId, int Phase, ImmutableArray<Guid> Dependencies);

public sealed record ScheduledActionProgress(Guid ActionId, JournalState State, JournalCheckpoint Checkpoint,
    JournalResult Result, ImmutableArray<Guid> PendingDependencies, bool EarlierPhasePending);

/// <param name="Actions">Durable progress in schedule order.</param>
/// <param name="NextForReview">The first dependency-ready action matching the saved active approval. Backend and job-wide admission still apply.</param>
/// <param name="RequiresRecovery">The journal contains a started, applied or review-required action.</param>
public sealed record ActionScheduleProjection(ImmutableArray<ScheduledActionProgress> Actions, Guid? NextForReview, bool RequiresRecovery);
