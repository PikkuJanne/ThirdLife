using System.Collections.Immutable;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Actions.Tests;

public sealed class ExecutionCoordinatorTests
{
    private static readonly OperatorId Operator = new("operator-synthetic-execution");

    [Fact]
    public async Task RequiresCurrentApprovalForTheExactSavedPlanAndImpact()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend((_, _, _, _) => throw new InvalidOperationException("Missing approval must prevent the broker call."));
        var coordinator = new ExecutionCoordinator(journal, backend);
        await journal.SaveJournalPlanAsync(plan);

        Assert.Equal(NoOpExecutionStatus.ApprovalRequired, await coordinator.ExecuteAsync(plan.Key, Guid.NewGuid()));
        var approval = await coordinator.ApproveAsync(plan, Operator);
        var differentImpact = new JournalPlanKey(plan.Key.JobId, plan.Key.PlanSha256, new string('a', 64));
        Assert.Equal(NoOpExecutionStatus.ApprovalRequired, await coordinator.ExecuteAsync(differentImpact, approval.ApprovalId));
        Assert.Equal(NoOpExecutionStatus.ApprovalRequired, await coordinator.ExecuteAsync(plan.Key, Guid.NewGuid()));

        journal.ApprovalInvalidated = true;
        Assert.Equal(NoOpExecutionStatus.ApprovalRequired, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        Assert.Equal(0, backend.Calls);
        Assert.All(journal.Read(plan.Key).Entries, entry => Assert.Equal(JournalState.Approved, entry.State));
    }

    [Fact]
    public async Task RechecksApprovalAfterConsentBeforeGivingBackendPermissionToDispatch()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend(async (_, canDispatch, _, cancellationToken) =>
        {
            journal.ApprovalInvalidated = true;
            Assert.False(await canDispatch(cancellationToken));
            return new(NoOpBrokerBatchStatus.Unavailable, []);
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.Unavailable, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        Assert.Equal(1, backend.Calls);
        var saved = journal.Read(plan.Key);
        Assert.All(plan.Actions, action =>
        {
            Assert.Equal(JournalState.Failed, saved.StateOf(action.ActionId));
            Assert.Contains(saved.Entries, entry => entry.ActionId == action.ActionId &&
                entry.Checkpoint == JournalCheckpoint.BrokerPreDispatchFailure && entry.Result == JournalResult.BackendUnavailable);
        });
    }

    [Fact]
    public async Task DeclineRemainsResumableAndAFreshExplicitAttemptRecordsAppliedOnce()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var decline = new Backend((_, _, _, _) => Task.FromResult(new NoOpBrokerBatchResult(NoOpBrokerBatchStatus.Declined, [])));
        var coordinator = new ExecutionCoordinator(journal, decline);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.Declined, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        var declined = journal.Read(plan.Key);
        Assert.Equal(approval, declined.ActiveApproval);
        Assert.All(plan.Actions, action =>
        {
            var terminal = Assert.Single(declined.Entries,
                entry => entry.ActionId == action.ActionId && entry.PriorState == JournalState.Started);
            Assert.Equal(JournalState.Failed, terminal.State);
            Assert.Equal(JournalResult.AuthorizationDeclined, terminal.Result);
            Assert.Equal(JournalCheckpoint.BrokerPreDispatchFailure, terminal.Checkpoint);
        });
        Assert.DoesNotContain(declined.Entries, entry => entry.State is JournalState.Applied or JournalState.Verified);

        JournalApproval? retryApproval = null;
        var accept = new Backend(async (batch, canDispatch, _, cancellationToken) =>
        {
            var activeApproval = Assert.IsType<JournalApproval>(retryApproval);
            Assert.Equal(plan.Key, batch.Plan);
            Assert.Equal(activeApproval.ApprovalId, batch.ApprovalId);
            Assert.Equal(plan.Actions.Select(action => action.ActionId), batch.Actions.Select(action => action.ActionId));
            Assert.True(await canDispatch(cancellationToken));
            var started = journal.Read(plan.Key);
            Assert.All(plan.Actions, action =>
            {
                var entry = Assert.Single(started.Entries,
                    entry => entry.ActionId == action.ActionId && entry.State == JournalState.Started && entry.ApprovalId == activeApproval.ApprovalId);
                Assert.Equal(batch.CorrelationId, entry.CorrelationId);
                Assert.Equal(JournalCheckpoint.BrokerDispatchIntent, entry.Checkpoint);
            });
            return Applied(batch);
        });
        var reopened = new ExecutionCoordinator(journal, accept);
        retryApproval = await reopened.ApproveAsync(plan, Operator);
        Assert.NotEqual(approval.ApprovalId, retryApproval.ApprovalId);
        Assert.Equal(NoOpExecutionStatus.ApprovalRequired, await reopened.ExecuteAsync(plan.Key, approval.ApprovalId));

        Assert.Equal(NoOpExecutionStatus.Completed, await reopened.ExecuteAsync(plan.Key, retryApproval.ApprovalId));
        Assert.Equal(NoOpExecutionStatus.AlreadyStarted, await reopened.ExecuteAsync(plan.Key, retryApproval.ApprovalId));
        Assert.Equal(1, accept.Calls);
        var completed = journal.Read(plan.Key);
        Assert.All(plan.Actions, action =>
        {
            Assert.Equal(JournalState.Applied, completed.StateOf(action.ActionId));
            var entries = completed.Entries.Where(entry => entry.ActionId == action.ActionId && entry.ApprovalId == retryApproval.ApprovalId).ToArray();
            var started = Assert.Single(entries, entry => entry.State == JournalState.Started);
            var applied = Assert.Single(entries, entry => entry.State == JournalState.Applied);
            Assert.Equal(started.CorrelationId, applied.CorrelationId);
            Assert.Equal(JournalActor.Broker, applied.Actor);
            Assert.DoesNotContain(entries, entry => entry.State == JournalState.Verified);
        });
    }

    [Fact]
    public async Task ProgressIgnoresUnknownWrongAttemptAndRepeatedUpdates()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var progress = new RecordedProgress();
        var expected = new List<NoOpExecutionProgress>();
        var backend = new Backend(async (batch, canDispatch, observer, cancellationToken) =>
        {
            Assert.NotNull(observer);
            Assert.True(await canDispatch(cancellationToken));
            var first = batch.Actions[0].ActionId;
            observer.Report(new(Guid.NewGuid(), first, NoOpProgressStage.Accepted));
            observer.Report(new(batch.CorrelationId, Guid.NewGuid(), NoOpProgressStage.Accepted));
            observer.Report(new(batch.CorrelationId, first, (NoOpProgressStage)int.MaxValue));
            foreach (var action in batch.Actions)
            {
                var accepted = new NoOpExecutionProgress(batch.CorrelationId, action.ActionId, NoOpProgressStage.Accepted);
                var running = accepted with { Stage = NoOpProgressStage.Running };
                expected.Add(accepted);
                expected.Add(running);
                observer.Report(accepted);
                observer.Report(accepted);
                observer.Report(running);
                observer.Report(accepted);
                observer.Report(running);
            }

            return Applied(batch);
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.Completed, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId, progress));
        Assert.Equal(expected, progress.Items);
    }

    [Fact]
    public async Task CancelBeforeDispatchLeavesAResumableJobWithoutAnAppliedResult()
    {
        using var cancel = new CancellationTokenSource();
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend((_, _, _, cancellationToken) =>
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            cancel.Cancel();
            Assert.True(cancellationToken.IsCancellationRequested);
            return Task.FromResult(new NoOpBrokerBatchResult(NoOpBrokerBatchStatus.CancelledBeforeDispatch, []));
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.Cancelled, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId, cancellationToken: cancel.Token));

        var saved = journal.Read(plan.Key);
        Assert.Equal(approval, saved.ActiveApproval);
        Assert.All(plan.Actions, action =>
        {
            var terminal = Assert.Single(saved.Entries,
                entry => entry.ActionId == action.ActionId && entry.PriorState == JournalState.Started);
            Assert.Equal(JournalResult.Cancelled, terminal.Result);
            Assert.Equal(JournalCheckpoint.BrokerPreDispatchFailure, terminal.Checkpoint);
        });
        Assert.DoesNotContain(saved.Entries, entry => entry.State is JournalState.Applied or JournalState.Verified);
    }

    [Fact]
    public async Task InFlightCancellationKeepsAnAppliedResultAndRecordsTheUnverifiedRemainder()
    {
        using var cancel = new CancellationTokenSource();
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend(async (batch, canDispatch, _, cancellationToken) =>
        {
            Assert.True(await canDispatch(cancellationToken));
            cancel.Cancel();
            Assert.True(cancellationToken.IsCancellationRequested);
            return new(NoOpBrokerBatchStatus.Finished, batch.Actions.Select((action, index) =>
                new NoOpBrokerActionResult(batch.CorrelationId, action.ActionId,
                    index == 0 ? NoOpBrokerActionStatus.Applied : NoOpBrokerActionStatus.Cancelled)).ToImmutableArray());
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.Cancelled, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId, cancellationToken: cancel.Token));

        var persisted = journal.Read(plan.Key);
        Assert.Equal(JournalState.Applied, persisted.StateOf(plan.Actions[0].ActionId));
        Assert.All(plan.Actions.Skip(1), action =>
        {
            var terminal = Assert.Single(persisted.Entries,
                entry => entry.ActionId == action.ActionId && entry.PriorState == JournalState.Started);
            Assert.Equal(JournalResult.Cancelled, terminal.Result);
            Assert.Equal(JournalState.RequiresReview, terminal.State);
        });
        Assert.DoesNotContain(persisted.Entries, entry => entry.State == JournalState.Verified);
    }

    [Fact]
    public async Task InterruptedBrokerPreservesAuthenticatedPrefixAndFlagsMissingResults()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend(async (batch, canDispatch, _, cancellationToken) =>
        {
            Assert.True(await canDispatch(cancellationToken));
            return new(NoOpBrokerBatchStatus.RequiresReview,
                [new(batch.CorrelationId, batch.Actions[0].ActionId, NoOpBrokerActionStatus.Applied)]);
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.RequiresReview, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        var reopened = journal.Read(plan.Key);

        Assert.Equal(JournalState.Applied, reopened.StateOf(plan.Actions[0].ActionId));
        Assert.All(plan.Actions.Skip(1), action => Assert.Equal(JournalState.RequiresReview, reopened.StateOf(action.ActionId)));
        Assert.Equal(NoOpExecutionStatus.AlreadyStarted, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task AnotherCoordinatorCannotDispatchOrReconcileAnActiveAttempt()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend(async (batch, canDispatch, _, cancellationToken) =>
        {
            Assert.True(await canDispatch(cancellationToken));
            entered.SetResult();
            await finish.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return Applied(batch);
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);
        var execution = coordinator.ExecuteAsync(plan.Key, approval.ApprovalId);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var reopened = new ExecutionCoordinator(journal, backend);
            Assert.Equal(NoOpExecutionStatus.AlreadyStarted, await reopened.ExecuteAsync(plan.Key, approval.ApprovalId));
            await reopened.ReconcileAsync(plan.Key);
            Assert.All(plan.Actions, action => Assert.Equal(JournalState.Started, journal.Read(plan.Key).StateOf(action.ActionId)));
        }
        finally
        {
            finish.SetResult();
        }

        Assert.Equal(NoOpExecutionStatus.Completed, await execution);
        Assert.Equal(1, backend.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidTerminalResultsCannotRecordApplied(bool omitResultArray)
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend(async (batch, canDispatch, _, cancellationToken) =>
        {
            Assert.True(await canDispatch(cancellationToken));
            return new(NoOpBrokerBatchStatus.Finished,
                omitResultArray ? default : [new(Guid.NewGuid(), batch.Actions[0].ActionId, NoOpBrokerActionStatus.Applied)]);
        });
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.RequiresReview, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));

        var reopened = journal.Read(plan.Key);
        Assert.All(plan.Actions, action => Assert.Equal(JournalState.RequiresReview, reopened.StateOf(action.ActionId)));
        Assert.DoesNotContain(reopened.Entries, entry => entry.State is JournalState.Applied or JournalState.Verified);
    }

    [Fact]
    public async Task ExactLookingResultsWithoutDispatchPermissionCannotRecordApplied()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var backend = new Backend((batch, _, _, _) => Task.FromResult(Applied(batch)));
        var coordinator = new ExecutionCoordinator(journal, backend);
        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.RequiresReview, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        Assert.All(plan.Actions, action => Assert.Equal(JournalState.RequiresReview, journal.Read(plan.Key).StateOf(action.ActionId)));
    }

    [Fact]
    public async Task ReopeningAnUnfinishedClientWaitsForExpiryBeforeExplicitReconciliation()
    {
        var time = new FixedTime(new DateTimeOffset(2030, 5, 1, 10, 0, 0, TimeSpan.Zero));
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var coordinator = new ExecutionCoordinator(journal, timeProvider: time);
        var approval = await coordinator.ApproveAsync(plan, Operator);
        var correlation = Guid.NewGuid();
        var started = plan.Actions.Select(action => new JournalEntry(Guid.NewGuid(), plan.Key, action.ActionId, approval.ApprovalId,
            JournalState.Approved, JournalState.Started, JournalActor.Coordinator, new OperatorId("coordinator-fixture"), correlation,
            time.GetUtcNow(), JournalResult.None, JournalCheckpoint.BrokerDispatchIntent)).ToImmutableArray();
        await journal.AppendBrokerBatchAsync(started);

        var reopened = new ExecutionCoordinator(journal, timeProvider: time);
        await reopened.ReconcileAsync(plan.Key);
        Assert.All(plan.Actions, action => Assert.Equal(JournalState.Started, journal.Read(plan.Key).StateOf(action.ActionId)));

        time.Advance(ExecutionCoordinator.MaximumDispatchLifetime + TimeSpan.FromSeconds(1));
        await reopened.ReconcileAsync(plan.Key);
        await reopened.ReconcileAsync(plan.Key);

        var saved = journal.Read(plan.Key);
        Assert.All(plan.Actions, action =>
        {
            var terminal = Assert.Single(saved.Entries, entry => entry.ActionId == action.ActionId && entry.PriorState == JournalState.Started);
            Assert.Equal(correlation, terminal.CorrelationId);
            Assert.Equal(JournalResult.Interrupted, terminal.Result);
            Assert.Equal(JournalState.RequiresReview, terminal.State);
        });
    }

    [Fact]
    public async Task UnavailableBackendPreservesApprovalAndCreatesNoDispatchIntent()
    {
        var plan = NoOpPlan();
        var journal = new ExecutionJournal();
        var coordinator = new ExecutionCoordinator(journal);
        Assert.False(coordinator.CanDispatch);

        var approval = await coordinator.ApproveAsync(plan, Operator);

        Assert.Equal(NoOpExecutionStatus.Unavailable, await coordinator.ExecuteAsync(plan.Key, approval.ApprovalId));
        var saved = journal.Read(plan.Key);
        Assert.Equal(approval, saved.ActiveApproval);
        Assert.Equal(approval, Assert.Single(saved.Approvals));
        Assert.Equal(plan.Actions.Length, saved.Entries.Length);
        Assert.All(saved.Entries, entry => Assert.Equal(JournalState.Approved, entry.State));
    }

    [Fact]
    public void PrivilegedPackageIsOutsideTheNoOpRoute()
    {
        var package = PlanResolverTests.CreateReviewedPlan();
        Assert.False(ExecutionCoordinator.IsBrokerPlan(JournalPlanSnapshot.Create(package, ImpactEstimator.Estimate(package))));
        Assert.True(ExecutionCoordinator.IsBrokerPlan(NoOpPlan()));
    }

    private static JournalPlanSnapshot NoOpPlan()
    {
        var plan = PlanResolverTests.CreateSyntheticPlan();
        return JournalPlanSnapshot.Create(plan, ImpactEstimator.Estimate(plan));
    }

    private static NoOpBrokerBatchResult Applied(NoOpExecutionBatch batch) => new(NoOpBrokerBatchStatus.Finished,
        batch.Actions.Select(action => new NoOpBrokerActionResult(batch.CorrelationId, action.ActionId,
            NoOpBrokerActionStatus.Applied)).ToImmutableArray());

    private sealed class Backend(
        Func<NoOpExecutionBatch, Func<CancellationToken, Task<bool>>, IProgress<NoOpExecutionProgress>?, CancellationToken,
            Task<NoOpBrokerBatchResult>> run) : INoOpBrokerBackend
    {
        public int Calls { get; private set; }

        public Task<NoOpBrokerBatchResult> RunAsync(NoOpExecutionBatch batch, Func<CancellationToken, Task<bool>> canDispatch,
            IProgress<NoOpExecutionProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return run(batch, canDispatch, progress, cancellationToken);
        }
    }

    private sealed class RecordedProgress : IProgress<NoOpExecutionProgress>
    {
        public List<NoOpExecutionProgress> Items { get; } = [];

        public void Report(NoOpExecutionProgress value) => Items.Add(value);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan interval) => now += interval;
    }

    private sealed class ExecutionJournal : IBrokerExecutionJournal
    {
        private readonly object _gate = new();
        private JournalPlanSnapshot? _plan;
        private ImmutableArray<JournalApproval> _approvals = [];
        private ImmutableArray<JournalEntry> _entries = [];

        public bool ApprovalInvalidated { get; set; }

        public JournalHistory Read(JournalPlanKey plan)
        {
            lock (_gate)
            {
                Assert.Equal(_plan?.Key, plan);
                return Snapshot();
            }
        }

        public Task<JournalWriteResult> SaveJournalPlanAsync(JournalPlanSnapshot plan, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_plan is not null)
                {
                    Assert.Equal(_plan.Key, plan.Key);
                    return Task.FromResult(JournalWriteResult.AlreadyRecorded);
                }

                _plan = JournalPlanSnapshot.Restore(plan.Key, plan.CanonicalPlan.ToArray(), plan.CanonicalImpact.ToArray());
                return Task.FromResult(JournalWriteResult.Recorded);
            }
        }

        public Task<JournalWriteResult> RecordBrokerApprovalAsync(JournalApproval approval,
            ImmutableArray<JournalEntry> entries, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Assert.Equal(approval.Plan, _plan!.Key);
                return Task.FromResult(Record(_approvals.Add(approval), entries));
            }
        }

        public Task<JournalWriteResult> AppendBrokerBatchAsync(ImmutableArray<JournalEntry> entries,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) { return Task.FromResult(Record(_approvals, entries)); }
        }

        public Task<JournalHistory?> LoadJournalAsync(JournalPlanKey key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) { return Task.FromResult(_plan?.Key == key ? Snapshot() : null); }
        }

        public Task<ImmutableArray<JournalHistory>> LoadJobJournalsAsync(JobId jobId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_plan?.Key.JobId == jobId ? ImmutableArray.Create(Snapshot()) : []);
            }
        }

        public Task<JournalWriteResult> AppendJournalApprovalAsync(JournalApproval approval, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Broker approvals require an atomic batch.");

        public Task<JournalWriteResult> AppendJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default) =>
            AppendBrokerBatchAsync([entry], cancellationToken);

        private JournalWriteResult Record(ImmutableArray<JournalApproval> approvals, ImmutableArray<JournalEntry> entries)
        {
            var latest = approvals.LastOrDefault();
            Assert.NotEmpty(entries);
            if (entries.All(_entries.Contains))
            {
                return JournalWriteResult.AlreadyRecorded;
            }

            var combined = _entries.AddRange(entries);
            var active = ApprovalInvalidated ? null : latest;
            Assert.All(entries.Where(entry => entry.State is JournalState.Started or JournalState.Approved),
                entry => Assert.Equal(active?.ApprovalId, entry.ApprovalId));
            _ = new JournalHistory(_plan!, approvals, combined, active);
            _approvals = approvals;
            _entries = combined;
            return JournalWriteResult.Recorded;
        }

        private JournalHistory Snapshot() => new(_plan!, _approvals, _entries,
            ApprovalInvalidated ? null : _approvals.LastOrDefault());
    }
}
