using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Actions.Execution;

public enum NoOpExecutionStatus
{
    ApprovalRequired = 1,
    AlreadyStarted,
    Unavailable,
    Declined,
    Cancelled,
    Completed,
    Failed,
    RequiresReview,
    CompletedWithOmissions,
}

/// <summary>Coordinates reviewed no-op actions through the durable unelevated journal.</summary>
public sealed class ExecutionCoordinator(IBrokerExecutionJournal journal, INoOpBrokerBackend? backend = null,
    TimeProvider? timeProvider = null, INoOpPostconditionVerifier? verifier = null)
{
    public static readonly TimeSpan MaximumDispatchLifetime = TimeSpan.FromMinutes(2);
    private static readonly OperatorId Coordinator = new("thirdlife-no-op-coordinator");
    private static readonly OperatorId Broker = new("thirdlife-no-op-broker");
    private static readonly OperatorId Verifier = new("thirdlife-no-op-verifier");
    private readonly IBrokerExecutionJournal _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public bool CanDispatch => backend is not null;

    public bool HasScheduledVerification => verifier is not null;

    public bool CanDispatchPlan(JournalPlanSnapshot plan) => CanDispatch && IsBrokerPlan(plan) &&
        (plan.Schedule is null || HasScheduledVerification);

    public static bool IsBrokerPlan(JournalPlanSnapshot plan) => plan.IsCurrentPolicyPlan && plan.SyntheticData &&
        plan.Actions.Length is > 0 and <= IBrokerExecutionJournal.MaximumBatchActions && plan.Actions.All(action => action is
        {
            Request: NoOpActionRequest,
            Definition: { Availability: ActionAvailability.BrokerNoOp, Verification: ActionVerification.NoMachineChange },
        });

    public async Task<JournalApproval> ApproveAsync(JournalPlanSnapshot plan, OperatorId operatorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operatorId);
        if (!IsBrokerPlan(plan))
        {
            throw new ArgumentException("This approval requires a bounded no-op broker plan.", nameof(plan));
        }

        await _journal.SaveJournalPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        var history = await _journal.LoadJournalAsync(plan.Key, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The saved no-op plan is unavailable.");
        var approval = new JournalApproval(Guid.NewGuid(), plan.Key, operatorId, Guid.NewGuid(), _time.GetUtcNow());
        var actionIds = history.Plan.Schedule is { } schedule
            ? schedule.Steps.Select(step => step.ActionId)
                .Where(id => JournalStateMachine.CanApproveBrokerAction(LatestEntry(history, id)))
            : plan.Actions.Select(action => action.ActionId);
        var entries = actionIds.Select(id => new JournalEntry(Guid.NewGuid(), plan.Key, id, approval.ApprovalId,
            history.StateOf(id), JournalState.Approved, JournalActor.Operator, operatorId, approval.CorrelationId,
            approval.OccurredAtUtc, JournalResult.None, JournalCheckpoint.BrokerApproval)).ToImmutableArray();
        if (entries.IsEmpty) { throw new InvalidOperationException("The saved plan has no actions eligible for approval."); }

        await _journal.RecordBrokerApprovalAsync(approval, entries, cancellationToken).ConfigureAwait(false);
        return approval;
    }

    public Task<NoOpExecutionStatus> ExecuteAsync(JournalPlanKey plan, Guid approvalId,
        IProgress<NoOpExecutionProgress>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(plan, approvalId, progress, validateCurrentPlan: null, cancellationToken);

    public async Task<NoOpExecutionStatus> ExecuteAsync(JournalPlanKey plan, Guid approvalId,
        IProgress<NoOpExecutionProgress>? progress, Func<CancellationToken, Task<bool>>? validateCurrentPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (backend is null) { return NoOpExecutionStatus.Unavailable; }
        if (cancellationToken.IsCancellationRequested) { return NoOpExecutionStatus.Cancelled; }

        var history = await _journal.LoadJournalAsync(plan, cancellationToken).ConfigureAwait(false);
        if (history is null || !IsBrokerPlan(history.Plan) || history.ActiveApproval is not { } approval ||
            approval.ApprovalId != approvalId)
        {
            return NoOpExecutionStatus.ApprovalRequired;
        }

        if (history.Plan.Schedule is { } schedule)
        {
            if (verifier is null) { return NoOpExecutionStatus.Unavailable; }
            if (validateCurrentPlan is null) { return NoOpExecutionStatus.ApprovalRequired; }

            return await ExecuteScheduleAsync(history, schedule, approvalId, verifier, progress, validateCurrentPlan, cancellationToken)
                .ConfigureAwait(false);
        }

        var lastEntries = LatestEntries(history);
        if (lastEntries.Any(entry => entry?.State != JournalState.Approved)) { return NoOpExecutionStatus.AlreadyStarted; }
        if (lastEntries.Any(entry => entry is not { Checkpoint: JournalCheckpoint.BrokerApproval } || entry.ApprovalId != approvalId))
        {
            return NoOpExecutionStatus.ApprovalRequired;
        }

        var actions = history.Plan.Actions.Select(action => (NoOpActionRequest)action.Request).ToImmutableArray();
        return await ExecuteBatchAsync(plan, approvalId, actions, progress, validateCurrentPlan, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NoOpExecutionStatus> ExecuteScheduleAsync(JournalHistory initial, ActionSchedule schedule,
        Guid approvalId, INoOpPostconditionVerifier postconditionVerifier, IProgress<NoOpExecutionProgress>? progress,
        Func<CancellationToken, Task<bool>> validateCurrentPlan, CancellationToken cancellationToken)
    {
        JournalHistory? history = initial;
        try
        {
            // Each action can add at most one dispatch and one independent observation in this run.
            for (var transition = 0; transition <= schedule.Steps.Length * 2; transition++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (history?.ActiveApproval?.ApprovalId != approvalId)
                {
                    return NoOpExecutionStatus.ApprovalRequired;
                }

                var projection = schedule.Project(history);
                if (projection.RequiresFreshApproval)
                {
                    return NoOpExecutionStatus.ApprovalRequired;
                }

                if (projection.RequiresRecovery)
                {
                    var unresolved = projection.Actions.Where(action =>
                        action.State is JournalState.Started or JournalState.Applied or JournalState.RequiresReview).ToArray();
                    if (unresolved is not [{ State: JournalState.Applied } pending] ||
                        !await VerifyAppliedAsync(history, pending.ActionId, approvalId, postconditionVerifier, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        return NoOpExecutionStatus.RequiresReview;
                    }
                }
                else
                {
                    if (projection.NextForReview is not { } actionId)
                    {
                        if (projection.Actions.All(action => action.State == JournalState.Verified))
                        {
                            return NoOpExecutionStatus.Completed;
                        }

                        if (PreparationDisposition.Evaluate(history).Disposition == Disposition.ReadyToPrepare)
                        {
                            return NoOpExecutionStatus.CompletedWithOmissions;
                        }

                        return projection.Actions.Any(action => action.State == JournalState.Failed) ? NoOpExecutionStatus.Failed :
                            projection.Actions.Any(action => action.State == JournalState.Skipped) ? NoOpExecutionStatus.RequiresReview :
                                NoOpExecutionStatus.ApprovalRequired;
                    }

                    if (LatestEntry(history, actionId) is not { Checkpoint: JournalCheckpoint.BrokerApproval } ||
                        !await validateCurrentPlan(cancellationToken).ConfigureAwait(false))
                    {
                        return NoOpExecutionStatus.ApprovalRequired;
                    }

                    var request = (NoOpActionRequest)history.Plan.Actions.Single(action => action.ActionId == actionId).Request;
                    var status = await ExecuteBatchAsync(history.Plan.Key, approvalId, [request], progress, validateCurrentPlan,
                        cancellationToken).ConfigureAwait(false);
                    if (status is not (NoOpExecutionStatus.Completed or NoOpExecutionStatus.Failed))
                    {
                        return status;
                    }
                }

                history = await _journal.LoadJournalAsync(schedule.PlanKey, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NoOpExecutionStatus.Cancelled;
        }

        return NoOpExecutionStatus.RequiresReview;
    }

    private async Task<bool> VerifyAppliedAsync(JournalHistory history, Guid actionId, Guid approvalId,
        INoOpPostconditionVerifier postconditionVerifier, CancellationToken cancellationToken)
    {
        if (LatestEntry(history, actionId) is not
            {
                State: JournalState.Applied, Actor: JournalActor.Broker,
                Result: JournalResult.BackendApplied, Checkpoint: JournalCheckpoint.None
            } applied || applied.ApprovalId != approvalId)
        {
            return false;
        }

        var action = history.Plan.Actions.Single(item => item.ActionId == actionId);
        var target = new NoOpVerificationTarget(history.Plan.Key, approvalId, applied.CorrelationId, (NoOpActionRequest)action.Request);
        if (!await postconditionVerifier.ObserveNoMachineChangeAsync(target, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = _time.GetUtcNow();
        if (observedAt < applied.OccurredAtUtc) { return false; }

        var entry = new JournalEntry(Guid.NewGuid(), applied.Plan, applied.ActionId, approvalId, JournalState.Applied,
            JournalState.Verified, JournalActor.Verifier, Verifier, applied.CorrelationId, observedAt, JournalResult.VerificationPassed,
            verification: ActionVerification.NoMachineChange);
        await _journal.AppendBrokerBatchAsync([entry], CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private async Task<NoOpExecutionStatus> ExecuteBatchAsync(JournalPlanKey plan, Guid approvalId,
        ImmutableArray<NoOpActionRequest> actions, IProgress<NoOpExecutionProgress>? progress,
        Func<CancellationToken, Task<bool>>? validateCurrentPlan, CancellationToken cancellationToken)
    {
        if (backend is null) { return NoOpExecutionStatus.Unavailable; }

        var correlation = Guid.NewGuid();
        var startedAt = _time.GetUtcNow();
        var batch = new NoOpExecutionBatch(plan, approvalId, correlation, startedAt + MaximumDispatchLifetime, actions);
        var starts = batch.Actions.Select(action => new JournalEntry(Guid.NewGuid(), plan, action.ActionId, approvalId,
            JournalState.Approved, JournalState.Started, JournalActor.Coordinator, Coordinator, correlation, startedAt,
            JournalResult.None, JournalCheckpoint.BrokerDispatchIntent)).ToImmutableArray();
        if (await _journal.AppendBrokerBatchAsync(starts, cancellationToken).ConfigureAwait(false) != JournalWriteResult.Recorded)
        {
            return NoOpExecutionStatus.AlreadyStarted;
        }

        var dispatchGranted = 0;
        var boundedProgress = progress is null ? null : new CorrelatedProgress(batch, progress, () => Volatile.Read(ref dispatchGranted) == 1);
        async Task<bool> CanSendAsync(CancellationToken token)
        {
            if (validateCurrentPlan is not null && !await validateCurrentPlan(token).ConfigureAwait(false)) { return false; }

            var latest = await _journal.LoadJournalAsync(plan, token).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            return latest?.ActiveApproval?.ApprovalId == approvalId &&
                batch.Actions.Select(action => LatestEntry(latest, action.ActionId)).SequenceEqual(starts) &&
                !cancellationToken.IsCancellationRequested && !token.IsCancellationRequested &&
                now >= startedAt && now < batch.ExpiresAtUtc && Interlocked.CompareExchange(ref dispatchGranted, 1, 0) == 0;
        }

        NoOpBrokerBatchResult result;
        try
        {
            result = await backend.RunAsync(batch, CanSendAsync, boundedProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new(Volatile.Read(ref dispatchGranted) == 1 ? NoOpBrokerBatchStatus.RequiresReview : NoOpBrokerBatchStatus.CancelledBeforeDispatch, []);
        }
        finally
        {
            boundedProgress?.Close();
        }

        var permitted = Volatile.Read(ref dispatchGranted) == 1;
        var (status, outcomes) = DescribeOutcome(batch, result, permitted);
        var finishedAt = _time.GetUtcNow();
        var terminal = starts.Select((start, index) => OutcomeEntry(start, outcomes[index],
            finishedAt >= startedAt ? finishedAt : startedAt, beforeDispatch: !permitted))
            .ToImmutableArray();
        await _journal.AppendBrokerBatchAsync(terminal, CancellationToken.None).ConfigureAwait(false);
        return status;
    }

    /// <summary>Closes expired dispatch intents without invoking a broker or asserting an unobserved result.</summary>
    public async Task ReconcileAsync(JournalPlanKey plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var history = await _journal.LoadJournalAsync(plan, cancellationToken).ConfigureAwait(false);
        if (history is null || !IsBrokerPlan(history.Plan)) { return; }

        var now = _time.GetUtcNow();
        var expired = LatestEntries(history).OfType<JournalEntry>().Where(entry =>
            entry is { State: JournalState.Started, Checkpoint: JournalCheckpoint.BrokerDispatchIntent } &&
            now - entry.OccurredAtUtc >= MaximumDispatchLifetime);
        foreach (var batch in expired.GroupBy(entry => entry.CorrelationId))
        {
            var outcomes = batch.Select(entry => OutcomeEntry(entry, JournalResult.Interrupted, now)).ToImmutableArray();
            await _journal.AppendBrokerBatchAsync(outcomes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<JournalEntry?> LatestEntries(JournalHistory history) => history.Plan.Actions.Select(action =>
        LatestEntry(history, action.ActionId));

    private static JournalEntry? LatestEntry(JournalHistory history, Guid actionId) =>
        history.Entries.LastOrDefault(entry => entry.ActionId == actionId);

    private static (NoOpExecutionStatus Status, JournalResult[] Outcomes) DescribeOutcome(NoOpExecutionBatch batch,
        NoOpBrokerBatchResult result, bool permitted)
    {
        var outcomes = new JournalResult[batch.Actions.Length];
        Array.Fill(outcomes, JournalResult.Interrupted);
        var noDispatch = !permitted && !result.Results.IsDefault && result.Results.IsEmpty;
        var (status, failure) = result.Status switch
        {
            NoOpBrokerBatchStatus.Declined when noDispatch => (NoOpExecutionStatus.Declined, JournalResult.AuthorizationDeclined),
            NoOpBrokerBatchStatus.CancelledBeforeDispatch when noDispatch => (NoOpExecutionStatus.Cancelled, JournalResult.Cancelled),
            NoOpBrokerBatchStatus.Unavailable when noDispatch => (NoOpExecutionStatus.Unavailable, JournalResult.BackendUnavailable),
            _ => (NoOpExecutionStatus.RequiresReview, JournalResult.Interrupted),
        };
        if (failure != JournalResult.Interrupted)
        {
            Array.Fill(outcomes, failure);
            return (status, outcomes);
        }

        if (!permitted || result.Status is not (NoOpBrokerBatchStatus.Finished or NoOpBrokerBatchStatus.RequiresReview) ||
            result.Results.IsDefault || result.Results.Length > batch.Actions.Length)
        {
            return (status, outcomes);
        }

        for (var index = 0; index < result.Results.Length; index++)
        {
            var action = result.Results[index];
            if (action.CorrelationId != batch.CorrelationId || action.ActionId != batch.Actions[index].ActionId || !Enum.IsDefined(action.Status))
            {
                Array.Fill(outcomes, JournalResult.Interrupted);
                return (status, outcomes);
            }

            outcomes[index] = action.Status switch
            {
                NoOpBrokerActionStatus.Applied => JournalResult.BackendApplied,
                NoOpBrokerActionStatus.Failed => JournalResult.OperationFailed,
                NoOpBrokerActionStatus.Cancelled => JournalResult.Cancelled,
                _ => JournalResult.Interrupted,
            };
        }

        if (result.Status == NoOpBrokerBatchStatus.Finished)
        {
            status = outcomes.Contains(JournalResult.Interrupted) ? NoOpExecutionStatus.RequiresReview :
                outcomes.Contains(JournalResult.OperationFailed) ? NoOpExecutionStatus.Failed :
                outcomes.Contains(JournalResult.Cancelled) ? NoOpExecutionStatus.Cancelled : NoOpExecutionStatus.Completed;
        }

        return (status, outcomes);
    }

    private static JournalEntry OutcomeEntry(JournalEntry start, JournalResult outcome, DateTimeOffset at, bool beforeDispatch = false)
    {
        var (state, actor, checkpoint, result) = outcome switch
        {
            JournalResult.BackendApplied => (JournalState.Applied, JournalActor.Broker, JournalCheckpoint.None, outcome),
            JournalResult.OperationFailed => (JournalState.Failed, JournalActor.Broker, JournalCheckpoint.None, outcome),
            JournalResult.AuthorizationDeclined or JournalResult.BackendUnavailable or JournalResult.Cancelled when beforeDispatch =>
                (JournalState.Failed, JournalActor.Coordinator, JournalCheckpoint.BrokerPreDispatchFailure, outcome),
            JournalResult.Cancelled => (JournalState.RequiresReview, JournalActor.Coordinator, JournalCheckpoint.Reconcile, outcome),
            _ => (JournalState.RequiresReview, JournalActor.Coordinator, JournalCheckpoint.Reconcile, JournalResult.Interrupted),
        };
        return new JournalEntry(Guid.NewGuid(), start.Plan, start.ActionId, start.ApprovalId, JournalState.Started, state,
            actor, actor == JournalActor.Broker ? Broker : Coordinator, start.CorrelationId, at, result, checkpoint);
    }

    private sealed class CorrelatedProgress(NoOpExecutionBatch batch, IProgress<NoOpExecutionProgress> target, Func<bool> isPermitted)
        : IProgress<NoOpExecutionProgress>
    {
        private readonly Lock _gate = new();
        private int _index = -1;
        private NoOpProgressStage _stage;
        private bool _closed;

        public void Report(NoOpExecutionProgress value)
        {
            lock (_gate)
            {
                if (_closed || !isPermitted() || value.CorrelationId != batch.CorrelationId || !Enum.IsDefined(value.Stage)) { return; }

                var index = 0;
                while (index < batch.Actions.Length && batch.Actions[index].ActionId != value.ActionId) { index++; }
                if (index == batch.Actions.Length || index < _index || (index == _index && value.Stage <= _stage)) { return; }

                _index = index;
                _stage = value.Stage;
                target.Report(value);
            }
        }

        public void Close()
        {
            lock (_gate) { _closed = true; }
        }
    }
}
