using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    [Fact]
    public async Task PackageRetryRequiresDurableBackoffAndCannotStartTwiceFromOneFailure()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime.AddMinutes(-1));
        JournalEntry failed;
        JournalApproval approval;
        await using (var store = await OpenAsync(workspace, clock))
        {
            var started = await SaveStartedAsync(store, "package-retry", packageBackend: true);
            approval = Assert.Single((await store.LoadJournalAsync(started.Plan))!.Approvals);
            failed = PackagePreDispatchFailure(started, JournalResult.NetworkUnavailable, clock.GetUtcNow());
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(failed));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(
                PackageRetry(approval, failed, failed.ActionId, JournalState.Failed, clock.GetUtcNow())));
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        await using var reopened = await OpenAsync(workspace, clock);
        await using var contender = await OpenAsync(workspace, clock);
        var first = PackageRetry(approval, failed, failed.ActionId, JournalState.Failed, clock.GetUtcNow());
        var second = PackageRetry(approval, failed, failed.ActionId, JournalState.Failed, clock.GetUtcNow());
        var winners = await Task.WhenAll(Task.Run(() => TryClaimPackageStartAsync(reopened, first)),
            Task.Run(() => TryClaimPackageStartAsync(contender, second)));
        var winner = Assert.Single(winners.OfType<JournalEntry>());
        Assert.Equal(failed.EntryId, winner.PriorAttemptEntryId);
        Assert.NotEqual(failed.CorrelationId, winner.CorrelationId);
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await reopened.AppendJournalEntryAsync(winner));

        var secondFailure = PackagePreDispatchFailure(winner, JournalResult.Cancelled, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(secondFailure));
        clock.Advance(TimeSpan.FromSeconds(14));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(
            PackageRetry(approval, secondFailure, winner.ActionId, JournalState.Failed, clock.GetUtcNow())));
        clock.Advance(TimeSpan.FromSeconds(1));
        var third = PackageRetry(approval, secondFailure, winner.ActionId, JournalState.Failed, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(third));
        var thirdFailure = PackagePreDispatchFailure(third, JournalResult.NetworkUnavailable, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(thirdFailure));
        clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(
            PackageRetry(approval, thirdFailure, winner.ActionId, JournalState.Failed, clock.GetUtcNow())));

        var history = Assert.Single(await reopened.LoadJobJournalsAsync(failed.Plan.JobId));
        var target = Assert.IsType<InstallPackageActionRequest>(Assert.Single(history.Plan.Actions).Request);
        var result = PackageRetryPolicy.Evaluate([history], target, clock.GetUtcNow());
        Assert.Equal(PackageRetryStatus.Exhausted, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(thirdFailure, result.LatestAttemptEntry);
        Assert.Equal(winner, history.Entries[3]);
        Assert.Equal(third, history.Entries[5]);
        Assert.Single(history.Approvals);
    }

    [Fact]
    public async Task ChangedAuthorizationRequiresAReviewedPlanAndLinksAcrossCatalogVersions()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime.AddMinutes(-1));
        await using var store = await OpenAsync(workspace, clock);
        var started = await SaveStartedAsync(store, "package-catalog-retry", packageBackend: true);
        var failed = PackagePreDispatchFailure(started, JournalResult.PackageDispatchAuthorizationChanged, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(failed));
        clock.Advance(TimeSpan.FromSeconds(5));
        var original = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(started.Plan));
        var oldApproval = Assert.Single(original.Approvals);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(
            PackageRetry(oldApproval, failed, started.ActionId, JournalState.Failed, clock.GetUtcNow())));

        var savedJob = await store.LoadJobAsync(started.Plan.JobId);
        Assert.NotNull(savedJob);
        var changed = BuildReviewedPlan(savedJob, Assert.Single(savedJob.SanitizationEvidence), CreateReviewedCatalog("1.0.1"));
        Assert.NotEqual(started.Plan, changed.Key);
        Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(changed));
        var approval = new JournalApproval(Guid.NewGuid(), changed.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalApprovalAsync(approval));
        var action = Assert.Single(changed.Actions);
        var approved = new JournalEntry(Guid.NewGuid(), changed.Key, action.ActionId, approval.ApprovalId,
            JournalState.Planned, JournalState.Approved, JournalActor.Operator, Approver, approval.CorrelationId,
            clock.GetUtcNow(), JournalResult.None, JournalCheckpoint.PackagePlanApproval);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(approved));
        var ordinary = new JournalEntry(Guid.NewGuid(), changed.Key, action.ActionId, approval.ApprovalId,
            JournalState.Approved, JournalState.Started, JournalActor.Coordinator, new OperatorId("retry-coordinator"),
            Guid.NewGuid(), clock.GetUtcNow(), JournalResult.None, JournalCheckpoint.PackageDispatchIntent);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(ordinary));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(
            PackageRetry(approval, started, action.ActionId, JournalState.Approved, clock.GetUtcNow())));

        var retry = PackageRetry(approval, failed, action.ActionId, JournalState.Approved, clock.GetUtcNow());
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(retry));
        var applied = new JournalEntry(Guid.NewGuid(), retry.Plan, retry.ActionId, retry.ApprovalId,
            JournalState.Started, JournalState.Applied, JournalActor.PackageBackend, new OperatorId("package-backend"),
            retry.CorrelationId, clock.GetUtcNow(), JournalResult.BackendApplied, JournalCheckpoint.PackageLaunchPendingVerification);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied));
        var verified = new JournalEntry(Guid.NewGuid(), applied.Plan, applied.ActionId, applied.ApprovalId,
            JournalState.Applied, JournalState.Verified, JournalActor.Verifier, new OperatorId("package-verifier"),
            applied.CorrelationId, clock.GetUtcNow(), JournalResult.PackageLaunchVerified,
            verification: ActionVerification.ExactPackageIdentity);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(verified));

        var histories = await store.LoadJobJournalsAsync(started.Plan.JobId);
        Assert.Equal(original.Entries.AsEnumerable(), histories[0].Entries.AsEnumerable());
        Assert.Equal(failed.EntryId, histories[1].Entries[1].PriorAttemptEntryId);
        var target = Assert.IsType<InstallPackageActionRequest>(action.Request);
        var result = PackageRetryPolicy.Evaluate(histories, target, clock.GetUtcNow());
        Assert.Equal(PackageRetryStatus.Verified, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(verified, result.LatestAttemptEntry);
    }

    private static JournalEntry PackageRetry(JournalApproval approval, JournalEntry prior, Guid actionId,
        JournalState state, DateTimeOffset now) =>
        new(Guid.NewGuid(), approval.Plan, actionId, approval.ApprovalId, state, JournalState.Started,
            JournalActor.Coordinator, new OperatorId("retry-coordinator"), Guid.NewGuid(), now, JournalResult.None,
            JournalCheckpoint.PackageRetryDispatchIntent, priorAttemptEntryId: prior.EntryId);

    private static JournalEntry PackagePreDispatchFailure(JournalEntry started, JournalResult result, DateTimeOffset now) =>
        new(Guid.NewGuid(), started.Plan, started.ActionId, started.ApprovalId, JournalState.Started, JournalState.Failed,
            JournalActor.PackageBackend, new OperatorId("package-backend"), started.CorrelationId, now,
            result, JournalCheckpoint.PackagePreDispatchFailure);

    private static async Task<JournalEntry?> TryClaimPackageStartAsync(SqliteJobStore store, JournalEntry entry)
    {
        try
        {
            return await store.AppendJournalEntryAsync(entry) == JournalWriteResult.Recorded ? entry : null;
        }
        catch (JobStoreConflictException)
        {
            return null;
        }
    }
}
