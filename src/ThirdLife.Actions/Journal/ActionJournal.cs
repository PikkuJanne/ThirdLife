using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ThirdLife.Actions.Contracts;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Actions.Journal;

public interface IActionJournalStore
{
    Task<JournalWriteResult> SaveJournalPlanAsync(JournalPlanSnapshot plan, CancellationToken cancellationToken = default);
    Task<JournalWriteResult> AppendJournalApprovalAsync(JournalApproval approval, CancellationToken cancellationToken = default);
    Task<JournalWriteResult> AppendJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default);
    Task<JournalHistory?> LoadJournalAsync(JournalPlanKey key, CancellationToken cancellationToken = default);
}

public enum JournalWriteResult
{
    Recorded = 1,
    AlreadyRecorded,
}

public enum JournalState
{
    Planned = 1,
    Approved,
    Started,
    Applied,
    Verified,
    Failed,
    Skipped,
    RolledBack,
    RequiresReview,
}

public enum JournalActor
{
    Operator = 1,
    Coordinator,
    Broker,
    Verifier,
}

public enum JournalResult
{
    None,
    BackendApplied,
    OperationFailed,
    VerificationPassed,
    RollbackVerified,
    Interrupted,
    Declined,
    NoChangeConfirmed,
}

public enum JournalCheckpoint
{
    None,
    DispatchIntent,
    RestartRequired,
    Reconcile,
}

public sealed record JournalApproval
{
    [JsonConstructor]
    public JournalApproval(Guid approvalId, JournalPlanKey plan, OperatorId actorId, Guid correlationId, DateTimeOffset occurredAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(approvalId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(correlationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(occurredAtUtc, default);
        ApprovalId = approvalId;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public Guid ApprovalId { get; }
    public JournalPlanKey Plan { get; }
    public OperatorId ActorId { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
}

public sealed record JournalEntry
{
    [JsonConstructor]
    public JournalEntry(
        Guid entryId,
        JournalPlanKey plan,
        Guid actionId,
        Guid? approvalId,
        JournalState priorState,
        JournalState state,
        JournalActor actor,
        OperatorId actorId,
        Guid correlationId,
        DateTimeOffset occurredAtUtc,
        JournalResult result,
        JournalCheckpoint checkpoint = JournalCheckpoint.None,
        ActionVerification? verification = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(entryId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(correlationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(occurredAtUtc, default);
        if (approvalId == Guid.Empty || !Enum.IsDefined(priorState) || !Enum.IsDefined(state) || !Enum.IsDefined(actor) ||
            !Enum.IsDefined(result) || !Enum.IsDefined(checkpoint) ||
            (verification is { } method && !Enum.IsDefined(method)))
        {
            throw new ArgumentException("The journal entry contains an invalid typed value.");
        }

        EntryId = entryId;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ActionId = actionId;
        ApprovalId = approvalId;
        PriorState = priorState;
        State = state;
        Actor = actor;
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Result = result;
        Checkpoint = checkpoint;
        Verification = verification;
    }

    public Guid EntryId { get; }
    public JournalPlanKey Plan { get; }
    public Guid ActionId { get; }
    public Guid? ApprovalId { get; }
    public JournalState PriorState { get; }
    public JournalState State { get; }
    public JournalActor Actor { get; }
    public OperatorId ActorId { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public JournalResult Result { get; }
    public JournalCheckpoint Checkpoint { get; }
    public ActionVerification? Verification { get; }
}

public sealed class JournalHistory
{
    public const int MaximumPlansPerJob = 128;
    public const int MaximumApprovalsPerJob = 128;
    public const int MaximumEntriesPerJob = 1_024;

    public JournalHistory(
        JournalPlanSnapshot plan,
        ImmutableArray<JournalApproval> approvals,
        ImmutableArray<JournalEntry> entries,
        JournalApproval? activeApproval)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (approvals.IsDefault || entries.IsDefault || approvals.Length > MaximumApprovalsPerJob || entries.Length > MaximumEntriesPerJob ||
            (activeApproval is not null && !approvals.Contains(activeApproval)))
        {
            throw new ArgumentException("The journal history is outside its bounds.");
        }

        var approvalsById = new Dictionary<Guid, JournalApproval>();
        foreach (var approval in approvals)
        {
            if (approval.Plan != plan.Key || !approvalsById.TryAdd(approval.ApprovalId, approval))
            {
                throw new ArgumentException("The journal approval history is inconsistent.");
            }
        }

        var ids = new HashSet<Guid>();
        var latest = new Dictionary<Guid, JournalEntry>();
        foreach (var entry in entries)
        {
            var approval = entry.ApprovalId is { } id ? approvalsById.GetValueOrDefault(id) : null;
            if (!ids.Add(entry.EntryId) || !JournalStateMachine.CanAppend(plan, approval, latest.GetValueOrDefault(entry.ActionId), entry))
            {
                throw new ArgumentException("The journal transition history is inconsistent.");
            }

            latest[entry.ActionId] = entry;
        }

        Plan = plan;
        Approvals = approvals;
        Entries = entries;
        ActiveApproval = activeApproval;
    }

    public JournalPlanSnapshot Plan { get; }
    public ImmutableArray<JournalApproval> Approvals { get; }
    public ImmutableArray<JournalEntry> Entries { get; }
    public JournalApproval? ActiveApproval { get; }

    public JournalState StateOf(Guid actionId)
    {
        if (!Plan.Actions.Any(action => action.Request.ActionId == actionId))
        {
            throw new ArgumentException("The action does not belong to this plan.", nameof(actionId));
        }

        return Entries.LastOrDefault(entry => entry.ActionId == actionId)?.State ?? JournalState.Planned;
    }
}
