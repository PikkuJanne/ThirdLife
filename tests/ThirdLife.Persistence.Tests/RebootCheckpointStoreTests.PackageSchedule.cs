using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.Recovery;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    private static readonly OperatorId PackageScheduleActor = new("actor-package-schedule-fixture");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MaintenancePausesApprovedPackageAtEachUpdateRestartAcrossReopen(int requiredRestarts)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        ulong uptime = 90_000;
        JournalPlanSnapshot plan;
        JournalApproval approval;
        await using (var prepared = await OpenPackageAsync(workspace, clock, () => uptime))
        {
            plan = await OpenPackageScheduleAsync(prepared, "maintenance", ids => [new(ids[0], 1, [])], 1);
            approval = new(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            await prepared.RecordPackageScheduleApprovalAsync(approval, ScheduledApprovalEntries(plan, approval));
        }

        var actionId = plan.Schedule!.Steps[0].ActionId;
        var provider = new WindowsUpdateWorkflowTests.FixtureAgent();
        for (var pass = 0; pass < requiredRestarts; pass++)
        {
            await using (var before = await OpenPackageAsync(workspace, clock, () => uptime))
            {
                var workflow = new WindowsUpdateWorkflow(before, provider, clock);
                provider.Scan = new(WindowsUpdateStatus.Succeeded, [], true);
                await workflow.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
            }
            await using var reopened = await OpenPackageAsync(workspace, clock, () => uptime);
            var waiting = new WindowsUpdateWorkflow(reopened, provider, clock);
            // A changed pending flag on the same boot cannot clear the earlier restart.
            provider.Scan = new(WindowsUpdateStatus.Succeeded, [], false);
            await waiting.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(
                ScheduledPackageStart(approval, actionId, clock.GetUtcNow())));
            var saved = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
            Assert.Equal(approval, saved.ActiveApproval);
            Assert.Equal(JournalState.Approved, saved.StateOf(actionId));
            uptime /= 2;
            await waiting.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        }

        await using var resumed = await OpenPackageAsync(workspace, clock, () => uptime);
        var last = new WindowsUpdateWorkflow(resumed, provider, clock);
        // A known ineligible offer alone is independent of the reviewed package route.
        provider.Scan = new(WindowsUpdateStatus.Succeeded,
            [new(new(Guid.NewGuid(), 1), WindowsUpdateClass.Restricted, 128)], false);
        await last.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        Assert.Equal(JournalWriteResult.Recorded, await resumed.AppendJournalEntryAsync(
            ScheduledPackageStart(approval, actionId, clock.GetUtcNow())));
        Assert.Empty(provider.Mutations);
        Assert.True(MaintenancePhases.RequiresPackageRecovery(await resumed.LoadJobJournalsAsync(plan.Key.JobId)));
    }

    [Fact]
    public async Task UnresolvedPackageFromEarlierIntakeBlocksUpdateChannelButAllowsReadOnlyScan()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        JournalEntry package;
        await using (var store = await OpenAsync(workspace, clock))
        {
            package = await SaveAppliedAsync(store, "phase-crossing", needsRestart: true, packageBackend: true);
        }
        await using var reopened = await OpenAsync(workspace, clock);
        var prior = Assert.IsType<StoredJob>(await reopened.LoadJobAsync(package.Plan.JobId));
        var (job, gate, policy) = await WindowsUpdateWorkflowTests.SeedAsync(reopened, prior.Job);
        var provider = new WindowsUpdateWorkflowTests.FixtureAgent();
        var workflow = new WindowsUpdateWorkflow(reopened, provider, provider, (_, _) => Task.FromResult(true), clock);
        var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
        var exact = Assert.Single(scan.Scan!.Updates);
        var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, exact, Approver, policy);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => workflow.ExecuteAsync(job.JobId, approval.Id));
        Assert.Empty(provider.Prepared);
        Assert.Empty(provider.Mutations);
        Assert.Equal(approval, (await reopened.LoadWindowsUpdatesAsync(job.JobId))[^1]);
        Assert.Equal(package, (await reopened.LoadJournalAsync(package.Plan))?.Entries[^1]);
    }

    [Fact]
    public async Task PackageScheduleApprovalAndIndependentVerificationSurviveReopen()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        JournalPlanSnapshot plan;
        JournalApproval approval;
        JournalEntry applied;
        Guid first;
        Guid dependent;
        Guid laterPhase;
        await using (var store = await OpenAsync(workspace, clock))
        {
            plan = await OpenPackageScheduleAsync(store, "schedule-reopen",
                ids => [new(ids[0], 1, []), new(ids[1], 1, [ids[0]]), new(ids[2], 2, [])], actionCount: 3);
            var ids = plan.Schedule!.Steps.Select(step => step.ActionId).ToArray();
            (first, dependent, laterPhase) = (ids[0], ids[1], ids[2]);
            approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            var entries = ScheduledApprovalEntries(plan, approval);

            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalApprovalAsync(approval));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.RecordPackageScheduleApprovalAsync(approval, [entries[0]]));
            var unapproved = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(plan.Key));
            Assert.Empty(unapproved.Approvals);
            Assert.Empty(unapproved.Entries);

            Assert.Equal(JournalWriteResult.Recorded, await store.RecordPackageScheduleApprovalAsync(approval, entries));
            Assert.Equal(JournalWriteResult.AlreadyRecorded, await store.RecordPackageScheduleApprovalAsync(approval, entries));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(
                ScheduledPackageStart(approval, dependent, clock.GetUtcNow())));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(
                ScheduledPackageStart(approval, laterPhase, clock.GetUtcNow())));

            await using var contender = await OpenAsync(workspace, clock);
            var intents = await Task.WhenAll(
                Task.Run(() => TryClaimPackageStartAsync(store, ScheduledPackageStart(approval, first, clock.GetUtcNow()))),
                Task.Run(() => TryClaimPackageStartAsync(contender, ScheduledPackageStart(approval, first, clock.GetUtcNow()))));
            var started = Assert.Single(intents.OfType<JournalEntry>());
            Assert.Equal(JournalWriteResult.AlreadyRecorded, await store.AppendJournalEntryAsync(started));
            applied = ScheduledPackageApplied(started, clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied));
        }

        await using var reopened = await OpenAsync(workspace, clock);
        var saved = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
        Assert.Equal(approval, saved.ActiveApproval);
        Assert.Equal(plan.CanonicalPlan.AsEnumerable(), saved.Plan.CanonicalPlan.AsEnumerable());
        Assert.Equal(plan.CanonicalImpact.AsEnumerable(), saved.Plan.CanonicalImpact.AsEnumerable());
        var graph = Assert.IsType<ActionSchedule>(saved.Plan.Schedule);
        var projection = graph.Project(saved);
        Assert.True(projection.RequiresRecovery);
        Assert.Null(projection.NextForReview);
        Assert.Equal(JournalState.Applied, projection.Actions[0].State);
        Assert.Equal(first, Assert.Single(projection.Actions[1].PendingDependencies));
        Assert.True(projection.Actions[2].EarlierPhasePending);
        Assert.Equal(applied, saved.Entries[^1]);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(
            ScheduledPackageStart(approval, dependent, clock.GetUtcNow())));

        var verification = ScheduledPackageVerified(applied, clock.GetUtcNow(), JournalCheckpoint.PackageRecoveryVerification);
        Assert.True(await reopened.RecordPackageRecoveryVerificationAsync(applied, verification));
        var afterVerification = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
        Assert.Equal(dependent, graph.Project(afterVerification).NextForReview);
        Assert.Equal(JournalState.Verified, afterVerification.StateOf(first));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(
            ScheduledPackageStart(approval, laterPhase, clock.GetUtcNow())));

        var startedDependent = ScheduledPackageStart(approval, dependent, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(startedDependent));
        var appliedDependent = ScheduledPackageApplied(startedDependent, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(appliedDependent));
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(
            ScheduledPackageVerified(appliedDependent, clock.GetUtcNow())));
        var phaseReady = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
        Assert.Equal(laterPhase, graph.Project(phaseReady).NextForReview);
        Assert.Equal(2, phaseReady.Entries.Count(entry => entry.State == JournalState.Started));
    }

    [Fact]
    public async Task PackageScheduleExecutorUsesTheSavedGraphAndRetainsIndependentVerification()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        JournalPlanSnapshot plan;
        JournalApproval approval;
        Guid failed;
        Guid blocked;
        Guid independent;
        await using (var store = await OpenAsync(workspace, clock))
        {
            plan = await OpenPackageScheduleAsync(store, "schedule-executor",
                ids => [new(ids[0], 1, []), new(ids[1], 1, [ids[0]]), new(ids[2], 1, [])], actionCount: 3);
            var ids = plan.Schedule!.Steps.Select(step => step.ActionId).ToArray();
            (failed, blocked, independent) = (ids[0], ids[1], ids[2]);
            approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.RecordPackageScheduleApprovalAsync(approval, ScheduledApprovalEntries(plan, approval)));

            var executor = new ScheduledPackageExecutor(store);
            await executor.ExecuteAsync(approval, request => request.ActionId != failed, UnexpectedAttempt);
            var before = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(plan.Key));
            Assert.All(before.Entries, entry => Assert.Equal(JournalState.Approved, entry.State));

            var attempts = new List<Guid>();
            await executor.ExecuteAsync(approval, _ => true, async (history, request, token) =>
            {
                Assert.Equal(request.ActionId, history.Plan.Schedule!.Project(history).NextForReview);
                attempts.Add(request.ActionId);
                var started = ScheduledPackageStart(approval, request.ActionId, clock.GetUtcNow());
                Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(started, token));
                if (request.ActionId == failed)
                {
                    var failure = new JournalEntry(Guid.NewGuid(), started.Plan, started.ActionId, started.ApprovalId,
                        JournalState.Started, JournalState.Failed, JournalActor.PackageBackend, PackageScheduleActor,
                        started.CorrelationId, clock.GetUtcNow(), JournalResult.OperationFailed);
                    Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(failure, token));
                    return;
                }

                var applied = ScheduledPackageApplied(started, clock.GetUtcNow());
                Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied, token));
                Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(
                    ScheduledPackageVerified(applied, clock.GetUtcNow()), token));
            });
            Assert.Equal([failed, independent], attempts);
        }

        await using var reopened = await OpenAsync(workspace, clock);
        await new ScheduledPackageExecutor(reopened).ExecuteAsync(approval, _ => true, UnexpectedAttempt);
        var durable = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
        var progress = durable.Plan.Schedule!.Project(durable);
        Assert.Null(progress.NextForReview);
        Assert.Equal(JournalState.Failed, durable.StateOf(failed));
        Assert.Equal(JournalState.Approved, durable.StateOf(blocked));
        Assert.Equal(failed, Assert.Single(Assert.Single(progress.Actions, action => action.ActionId == blocked).PendingDependencies));
        Assert.Equal(JournalState.Verified, durable.StateOf(independent));
        Assert.Equal(2, durable.Entries.Count(entry => entry.State == JournalState.Started));
        Assert.Single(durable.Approvals);

        static Task UnexpectedAttempt(JournalHistory history, InstallPackageActionRequest request, CancellationToken token) =>
            throw new InvalidOperationException("This schedule has no dispatchable package.");
    }

    [Fact]
    public async Task PackageScheduleIsolatesRetryAndHoldsTheNextPhaseUntilAuthenticatedRebootVerification()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        ulong? uptime = 9_000;
        JournalPlanSnapshot plan;
        JournalApproval approval;
        JournalEntry applied;
        Guid first;
        Guid next;
        await using (var store = await OpenPackageAsync(workspace, clock, () => uptime))
        {
            plan = await OpenPackageScheduleAsync(store, "schedule-package-reboot",
                ids => [new(ids[0], 1, []), new(ids[1], 2, [ids[0]])], actionCount: 2);
            first = plan.Schedule!.Steps[0].ActionId;
            next = plan.Schedule.Steps[1].ActionId;
            approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.RecordPackageScheduleApprovalAsync(approval, ScheduledApprovalEntries(plan, approval)));
            var started = ScheduledPackageStart(approval, first, clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(started));
            var failure = PackagePreDispatchFailure(started, JournalResult.NetworkUnavailable, clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(failure));
            var histories = await store.LoadJobJournalsAsync(plan.Key.JobId);
            var firstTarget = Assert.IsType<InstallPackageActionRequest>(Assert.Single(plan.Actions, action => action.ActionId == first).Request);
            var nextTarget = Assert.IsType<InstallPackageActionRequest>(Assert.Single(plan.Actions, action => action.ActionId == next).Request);
            Assert.Equal(PackageRetryStatus.CoolingDown, PackageRetryPolicy.Evaluate(histories, firstTarget, clock.GetUtcNow()).Status);
            Assert.Equal(PackageRetryStatus.Initial, PackageRetryPolicy.Evaluate(histories, nextTarget, clock.GetUtcNow()).Status);
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                store.AppendJournalEntryAsync(PackageRetry(approval, failure, first, JournalState.Failed, clock.GetUtcNow())));

            clock.Advance(TimeSpan.FromSeconds(5));
            var retry = PackageRetry(approval, failure, first, JournalState.Failed, clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(retry));
            applied = ScheduledPackageApplied(retry, clock.GetUtcNow(), JournalCheckpoint.RestartRequired);
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied));
        }

        await using var reopened = await OpenPackageAsync(workspace, clock, () => uptime);
        var waiting = Assert.IsType<PackageRebootCandidate>(
            await reopened.LoadPackageRebootCheckpointAsync(applied));
        Assert.False(waiting.CanVerify);
        Assert.Equal(first, waiting.Candidate.Checkpoint.ActionId);
        Assert.Equal(approval.ApprovalId, waiting.Candidate.Checkpoint.ApprovalId);
        Assert.Equal(applied.CorrelationId, waiting.Candidate.Checkpoint.CorrelationId);
        var verification = ScheduledPackageVerified(applied, clock.GetUtcNow(), JournalCheckpoint.PackageRebootVerification);
        Assert.False(await reopened.RecordPackageRebootVerificationAsync(verification, waiting.Candidate.Token, waiting.Candidate.Checkpoint.Id));
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            reopened.AppendJournalEntryAsync(ScheduledPackageStart(approval, next, clock.GetUtcNow())));

        uptime = 1_000;
        Assert.True(await reopened.RecordPackageRebootVerificationAsync(verification, waiting.Candidate.Token, waiting.Candidate.Checkpoint.Id));
        var resumed = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
        Assert.Equal(JournalState.Verified, resumed.StateOf(first));
        Assert.Equal(next, resumed.Plan.Schedule!.Project(resumed).NextForReview);
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(
            ScheduledPackageStart(approval, next, clock.GetUtcNow())));
        Assert.Null(await reopened.LoadPackageRebootCheckpointAsync(applied));
    }

    private static async Task<JournalPlanSnapshot> OpenPackageScheduleAsync(SqliteJobStore store, string suffix,
        Func<Guid[], ImmutableArray<ScheduledAction>> steps, int actionCount)
    {
        var (job, intake) = await CreateReviewedJobAsync(store, suffix);
        var plan = BuildReviewedPlan(job, intake, CreateReviewedCatalog(actionCount: actionCount), proposal =>
            proposal.WithSchedule(steps(proposal.Actions.Select(action => action.Request.ActionId).ToArray())));
        Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
        return plan;
    }

    private static ImmutableArray<JournalEntry> ScheduledApprovalEntries(JournalPlanSnapshot plan, JournalApproval approval) =>
        plan.Schedule!.Steps.Select(step => new JournalEntry(Guid.NewGuid(), plan.Key, step.ActionId, approval.ApprovalId,
            JournalState.Planned, JournalState.Approved, JournalActor.Operator, approval.ActorId, approval.CorrelationId,
            approval.OccurredAtUtc, JournalResult.None, JournalCheckpoint.PackagePlanApproval)).ToImmutableArray();

    private static JournalEntry ScheduledPackageStart(JournalApproval approval, Guid actionId, DateTimeOffset now) =>
        new(Guid.NewGuid(), approval.Plan, actionId, approval.ApprovalId, JournalState.Approved, JournalState.Started,
            JournalActor.Coordinator, PackageScheduleActor, Guid.NewGuid(), now, JournalResult.None, JournalCheckpoint.PackageDispatchIntent);

    private static JournalEntry ScheduledPackageApplied(JournalEntry started, DateTimeOffset now,
        JournalCheckpoint checkpoint = JournalCheckpoint.PackageLaunchPendingVerification) =>
        new(Guid.NewGuid(), started.Plan, started.ActionId, started.ApprovalId, JournalState.Started, JournalState.Applied,
            JournalActor.PackageBackend, PackageScheduleActor, started.CorrelationId, now, JournalResult.BackendApplied, checkpoint);

    private static JournalEntry ScheduledPackageVerified(JournalEntry applied, DateTimeOffset now,
        JournalCheckpoint checkpoint = JournalCheckpoint.None) =>
        new(Guid.NewGuid(), applied.Plan, applied.ActionId, applied.ApprovalId, JournalState.Applied, JournalState.Verified,
            JournalActor.Verifier, PackageScheduleActor, applied.CorrelationId, now, JournalResult.PackageLaunchVerified, checkpoint,
            verification: ActionVerification.ExactPackageIdentity);
}
