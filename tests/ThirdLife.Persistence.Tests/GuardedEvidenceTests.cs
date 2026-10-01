using System.Runtime.Versioning;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class GuardedEvidenceTests
{
    [Theory]
    [InlineData(SanitizationState.Verified)]
    [InlineData(SanitizationState.ReplacementStorage)]
    [InlineData(SanitizationState.NoDonorStorage)]
    public async Task CurrentAllowingDecisionCommitsWholeObservationBatchAndCheckpoint(SanitizationState state)
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision decision;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            (job, decision) = await PrepareAsync(store, "allowed", state);
            await store.RequireSanitizationGateAsync(job.JobId, decision.DecisionId);
            await store.AppendEvidenceAsync(Batch(job, decision.DecisionId));
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = (await reopened.LoadJobAsync(job.JobId))!;
        Assert.Equal(2, actual.Observations.Count);
        Assert.Equal(3, actual.Checkpoints.Count);
        Assert.Equal(JobCheckpointKind.EvidenceCommitted, actual.Checkpoints[^1].Kind);
        Assert.Equal(decision, Assert.Single(actual.SanitizationGateDecisions));
        Assert.Equal(3, SqliteJobStore.CurrentSchemaVersion);
    }

    [Theory]
    [InlineData("missing_evidence")]
    [InlineData("missing_decision")]
    [InlineData("wrong_decision")]
    [InlineData("wrong_job")]
    [InlineData("stale_verified")]
    [InlineData("stale_failed")]
    [InlineData("archived")]
    [InlineData("blocked_unknown")]
    [InlineData("blocked_failed")]
    public async Task RejectedGuardCannotAppendObservationsOrCheckpoint(string condition)
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var job = PersistenceTestData.CreateJob("rejected");
        await store.CreateJobAsync(job);
        var expected = new SanitizationGateDecisionId("gate-missing");
        if (condition != "missing_evidence")
        {
            var state = condition switch
            {
                "blocked_unknown" => SanitizationState.Unknown,
                "blocked_failed" => SanitizationState.Failed,
                _ => SanitizationState.Verified,
            };
            var evidence = PersistenceTestData.CreateSanitization(state, "rejected", PersistenceTestData.Timestamp);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));
            if (condition != "missing_decision")
            {
                expected = (await store.RecordSanitizationGateDecisionAsync(
                    SanitizationGate.Evaluate(job.JobId, evidence, PersistenceTestData.Timestamp))).DecisionId;
            }
        }
        switch (condition)
        {
            case "wrong_decision": expected = new("gate-wrong"); break;
            case "wrong_job":
                (_, var otherDecision) = await PrepareAsync(store, "other");
                expected = otherDecision.DecisionId;
                break;
            case "stale_verified":
            case "stale_failed":
                // Append order owns freshness even when the new source timestamp is older.
                var newer = PersistenceTestData.CreateSanitization(
                    condition == "stale_verified" ? SanitizationState.Verified : SanitizationState.Failed,
                    "newer", PersistenceTestData.Timestamp.AddDays(-1));
                await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [newer]));
                await store.RecordSanitizationGateDecisionAsync(
                    SanitizationGate.Evaluate(job.JobId, newer, PersistenceTestData.Timestamp));
                break;
            case "archived":
                await store.SetArchiveStateAsync(job.JobId, true, PersistenceTestData.Timestamp);
                break;
        }
        var before = (await store.LoadJobAsync(job.JobId))!;

        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.RequireSanitizationGateAsync(job.JobId, expected));

        var error = await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.AppendEvidenceAsync(Batch(job, expected)));

        Assert.Equal("store_record_conflict", error.ResultCode);
        var after = (await store.LoadJobAsync(job.JobId))!;
        Assert.Empty(after.Observations);
        Assert.Equal(before.Checkpoints, after.Checkpoints);
        Assert.Equal(before.SanitizationEvidence, after.SanitizationEvidence);
        Assert.Equal(before.SanitizationGateDecisions, after.SanitizationGateDecisions);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ConcurrentGateChangeAndGuardedWriteHaveOneTransactionOrder(bool guardedWriteFirst, bool archive)
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision decision;
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        { (job, decision) = await PrepareAsync(initializer, "concurrent"); }
        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeWriteCommit);
        await using var blockedStore = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, TimeProvider.System, blocker);
        await using var competingStore = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task guardedWrite;
        Task change;
        try
        {
            if (guardedWriteFirst)
            {
                guardedWrite = blockedStore.AppendEvidenceAsync(Batch(job, decision.DecisionId));
                await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
                change = Task.Run(async () => { started.SetResult(); await ChangeAsync(competingStore); });
            }
            else
            {
                change = ChangeAsync(blockedStore);
                await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
                guardedWrite = Task.Run(async () =>
                {
                    started.SetResult();
                    await competingStore.AppendEvidenceAsync(Batch(job, decision.DecisionId));
                });
            }
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { blocker.Release(); }

        await change.WaitAsync(TimeSpan.FromSeconds(10));
        if (guardedWriteFirst) { await guardedWrite.WaitAsync(TimeSpan.FromSeconds(10)); }
        else
        {
            await Assert.ThrowsAsync<JobStoreConflictException>(async () =>
                await guardedWrite.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        var actual = (await competingStore.LoadJobAsync(job.JobId))!;
        Assert.Equal(guardedWriteFirst ? 2 : 0, actual.Observations.Count);
        Assert.Equal(guardedWriteFirst ? 4 : 3, actual.Checkpoints.Count);
        Assert.False(SanitizationGate.Inspect(actual).AllowsAssessment);
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            competingStore.AppendEvidenceAsync(Batch(job, decision.DecisionId)));

        Task ChangeAsync(SqliteJobStore store) => archive
            ? store.SetArchiveStateAsync(job.JobId, true, PersistenceTestData.Timestamp)
            : store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence:
                [PersistenceTestData.CreateSanitization(SanitizationState.Failed, "concurrent-failed", PersistenceTestData.Timestamp)]));
    }

    [Theory]
    [InlineData((int)JobStoreFaultPoint.AfterFirstEvidenceInsert, false)]
    [InlineData((int)JobStoreFaultPoint.BeforeWriteCommit, false)]
    [InlineData((int)JobStoreFaultPoint.AfterWriteCommit, true)]
    public async Task InterruptedGuardedWriteReopensWithNoneOrTheCompleteBatch(int fault, bool committed)
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision decision;
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        { (job, decision) = await PrepareAsync(initializer, "interrupted"); }
        await using (var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, TimeProvider.System, new ThrowingFaultInjector((JobStoreFaultPoint)fault)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() =>
                store.AppendEvidenceAsync(Batch(job, decision.DecisionId)));
        }
        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = (await reopened.LoadJobAsync(job.JobId))!;
        Assert.Equal(committed ? 2 : 0, actual.Observations.Count);
        Assert.Equal(committed ? 3 : 2, actual.Checkpoints.Count);
        if (committed)
        {
            // A caller error after commit must not be treated as proof of rollback.
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                reopened.AppendEvidenceAsync(Batch(job, decision.DecisionId)));
        }
        else { await reopened.AppendEvidenceAsync(Batch(job, decision.DecisionId)); }
        Assert.Equal(2, (await reopened.LoadJobAsync(job.JobId))!.Observations.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAroundCommitLeavesTruthfulDurableState(bool cancelAfterCommit)
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision decision;
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        { (job, decision) = await PrepareAsync(initializer, "cancelled"); }
        var blocker = new BlockingFaultInjector(cancelAfterCommit
            ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit);
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, TimeProvider.System, blocker);
        using var cancellation = new CancellationTokenSource();
        var write = store.AppendEvidenceAsync(Batch(job, decision.DecisionId), cancellation.Token);
        try
        {
            await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        }
        finally { blocker.Release(); }
        var actual = (await store.LoadJobAsync(job.JobId))!;
        Assert.Equal(cancelAfterCommit ? 2 : 0, actual.Observations.Count);
        Assert.Equal(cancelAfterCommit ? 3 : 2, actual.Checkpoints.Count);
    }

    private static JobEvidenceBatch Batch(Job job, SanitizationGateDecisionId decision) => new(job.JobId,
        [PersistenceTestData.CreateObservation("guard-first"), PersistenceTestData.CreateObservation("guard-second")],
        expectedSanitizationGateDecisionId: decision);

    private static async Task<(Job Job, SanitizationGateDecision Decision)> PrepareAsync(SqliteJobStore store,
        string suffix, SanitizationState state = SanitizationState.Verified)
    {
        var job = PersistenceTestData.CreateJob(suffix);
        var evidence = PersistenceTestData.CreateSanitization(state, suffix, PersistenceTestData.Timestamp);
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));
        var decision = await store.RecordSanitizationGateDecisionAsync(
            SanitizationGate.Evaluate(job.JobId, evidence, PersistenceTestData.Timestamp));
        return (job, decision);
    }
}
