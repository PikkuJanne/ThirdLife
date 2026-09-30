using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class SqliteJobStoreIntegrationTests
{
    [Fact]
    public async Task CreateCloseReopenArchiveAndRestorePreserveCommittedState()
    {
        using var workspace = new PersistenceTestWorkspace();
        var time = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var job = PersistenceTestData.CreateJob();
        var observation = PersistenceTestData.CreateObservation();
        var sanitization = PersistenceTestData.CreateSanitization();
        var humanTest = PersistenceTestData.CreateHumanTest(job);
        string jobDirectory;
        string engineVersion;

        await using (var store = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         SqliteJobStore.CurrentSchemaVersion,
                         time,
                         faultInjector: null))
        {
            engineVersion = store.DatabaseEngineVersion;
            await store.CreateJobAsync(job);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(
                job.JobId,
                [observation],
                [sanitization],
                [humanTest]));

            time.Advance(TimeSpan.FromMinutes(1));
            await store.SetArchiveStateAsync(job.JobId, isArchived: true, time.GetUtcNow());
            jobDirectory = store.GetJobDirectoryPath(job.JobId);
        }

        Assert.Matches("^[0-9]+\\.[0-9]+", engineVersion);
        Assert.True(Directory.Exists(jobDirectory));
        Assert.DoesNotContain(job.JobId.Value, Path.GetFileName(jobDirectory), StringComparison.OrdinalIgnoreCase);

        await using (var reopened = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         SqliteJobStore.CurrentSchemaVersion,
                         time,
                         faultInjector: null))
        {
            var actual = await reopened.LoadJobAsync(job.JobId);

            Assert.NotNull(actual);
            Assert.Equal(job, actual.Job);
            Assert.True(actual.IsArchived);
            Assert.Equal(time.GetUtcNow(), actual.ArchivedAtUtc);
            Assert.Equal(observation, Assert.Single(actual.Observations));
            Assert.Equal(sanitization, Assert.Single(actual.SanitizationEvidence));
            Assert.Equal(humanTest, Assert.Single(actual.HumanTests));
            Assert.Equal(
                [JobCheckpointKind.JobCreated, JobCheckpointKind.EvidenceCommitted, JobCheckpointKind.Archived],
                actual.Checkpoints.Select(checkpoint => checkpoint.Kind));

            time.Advance(TimeSpan.FromMinutes(1));
            await reopened.SetArchiveStateAsync(job.JobId, isArchived: false, time.GetUtcNow());
        }

        await using var restoredStore = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var restored = await restoredStore.LoadJobAsync(job.JobId);

        Assert.NotNull(restored);
        Assert.False(restored.IsArchived);
        Assert.Null(restored.ArchivedAtUtc);
        Assert.Single(restored.Observations);
        Assert.Single(restored.SanitizationEvidence);
        Assert.Single(restored.HumanTests);
        Assert.Equal(JobCheckpointKind.Restored, restored.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task DuplicateEvidenceRollsBackTheWholeBatchAndCheckpoint()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var existing = PersistenceTestData.CreateObservation("existing");
        var proposed = PersistenceTestData.CreateObservation("proposed");

        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, [existing]));

        var duplicate = new Observation(
            existing.Metadata,
            new EvidenceKey("system.synthetic_duplicate"),
            EvidenceValue.FromBoolean(true),
            unit: null,
            limitationCode: null);

        var exception = await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, [proposed, duplicate])));
        var actual = await store.LoadJobAsync(job.JobId);

        Assert.Equal("store_record_conflict", exception.ResultCode);
        Assert.NotNull(actual);
        Assert.Equal(existing, Assert.Single(actual.Observations));
        Assert.Equal(2, actual.Checkpoints.Count);
    }

    [Fact]
    public async Task HumanTestForAnotherJobIsRejectedWithoutAWrite()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("owner");
        var otherJob = PersistenceTestData.CreateJob("other");
        var mismatched = PersistenceTestData.CreateHumanTest(otherJob, "mismatched");

        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, humanTests: [mismatched])));
        var actual = await store.LoadJobAsync(job.JobId);

        Assert.NotNull(actual);
        Assert.Empty(actual.HumanTests);
        Assert.Single(actual.Checkpoints);
    }

    [Fact]
    public async Task ConcurrentWriterTimesOutWithoutPartialStateAndCanBeRetriedExplicitly()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();

        await using var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await initializer.CreateJobAsync(job);
        await initializer.DisposeAsync();

        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeWriteCommit);
        await using var first = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker);
        await using var second = await SqliteJobStore.OpenAsync(workspace.StoreRoot);

        var firstWrite = first.AppendEvidenceAsync(new JobEvidenceBatch(
            job.JobId,
            [PersistenceTestData.CreateObservation("first")]));
        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));

        var busy = await Assert.ThrowsAsync<JobStoreBusyException>(() =>
            second.AppendEvidenceAsync(new JobEvidenceBatch(
                job.JobId,
                [PersistenceTestData.CreateObservation("second")])));
        Assert.Equal("store_busy", busy.ResultCode);

        blocker.Release();
        await firstWrite;
        await second.AppendEvidenceAsync(new JobEvidenceBatch(
            job.JobId,
            [PersistenceTestData.CreateObservation("second")]));

        var actual = await second.LoadJobAsync(job.JobId);
        Assert.NotNull(actual);
        Assert.Equal(2, actual.Observations.Count);
        Assert.Equal(3, actual.Checkpoints.Count);
    }

    [Fact]
    public async Task CancellationBeforeCommitRollsBackTheBatch()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeWriteCommit);

        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await initializer.CreateJobAsync(job);
        }

        await using var store = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker);
        using var cancellation = new CancellationTokenSource();
        var write = store.AppendEvidenceAsync(
            new JobEvidenceBatch(job.JobId, [PersistenceTestData.CreateObservation("cancelled")]),
            cancellation.Token);
        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        var actual = await store.LoadJobAsync(job.JobId);

        Assert.NotNull(actual);
        Assert.Empty(actual.Observations);
        Assert.Single(actual.Checkpoints);
    }

    [Fact]
    public async Task LoadUsesOneSnapshotWhileAConcurrentArchiveWaitsToCommit()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("snapshot");
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await initializer.CreateJobAsync(job);
        }

        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.DuringSnapshotRead);
        await using var readerStore = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker);
        await using var writerStore = await SqliteJobStore.OpenAsync(workspace.StoreRoot);

        var read = readerStore.LoadJobAsync(job.JobId);
        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
        var archive = Task.Run(() => writerStore.SetArchiveStateAsync(
            job.JobId,
            isArchived: true,
            PersistenceTestData.Timestamp.AddMinutes(10)));

        bool completedBeforeRelease;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            completedBeforeRelease = archive.IsCompleted;
        }
        finally
        {
            blocker.Release();
        }

        var snapshot = await read;
        await archive;
        var afterCommit = await writerStore.LoadJobAsync(job.JobId);

        Assert.False(completedBeforeRelease);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsArchived);
        Assert.Single(snapshot.Checkpoints);
        Assert.NotNull(afterCommit);
        Assert.True(afterCommit.IsArchived);
        Assert.Equal(JobCheckpointKind.Archived, afterCommit.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task CheckpointLimitRejectsTheWholeArchiveTransitionBeforeStateBecomesUnreadable()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("checkpoint-limit");
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);
        await SqliteTestControl.SeedCheckpointsAsync(
            store.DatabasePath,
            job.JobId,
            SqliteJobStore.MaximumCheckpointsPerJob - 2);

        var archivedAt = PersistenceTestData.Timestamp.AddMinutes(20);
        await store.SetArchiveStateAsync(job.JobId, isArchived: true, archivedAt);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.SetArchiveStateAsync(
                job.JobId,
                isArchived: false,
                archivedAt.AddMinutes(1)));

        var actual = await store.LoadJobAsync(job.JobId);
        Assert.NotNull(actual);
        Assert.True(actual.IsArchived);
        Assert.Equal(archivedAt, actual.ArchivedAtUtc);
        Assert.Equal(SqliteJobStore.MaximumCheckpointsPerJob, actual.Checkpoints.Count);
        Assert.Equal(JobCheckpointKind.Archived, actual.Checkpoints[^1].Kind);
    }

    [Fact]
    public async Task JobLimitRejectsCreationBeforeTheStoreCanBecomeUnreadable()
    {
        using var workspace = new PersistenceTestWorkspace();
        var proposed = PersistenceTestData.CreateJob("job-limit");
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await SqliteTestControl.SeedJobsAsync(
            store.DatabasePath,
            SqliteJobStore.MaximumJobs);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.CreateJobAsync(proposed));

        Assert.Equal(
            SqliteJobStore.MaximumJobs,
            await SqliteTestControl.ReadJobCountAsync(store.DatabasePath));
    }

    [Fact]
    public async Task GateDecisionRoundTripsAndSameEvidenceRetryIsIdempotent()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("gate-roundtrip");
        var evidence = PersistenceTestData.CreateSanitization("gate-roundtrip");
        var candidate = SanitizationGate.Evaluate(
            job.JobId,
            evidence,
            PersistenceTestData.Timestamp.AddMinutes(2),
            new SanitizationGateDecisionId("gate-roundtrip"));

        SanitizationGateDecision accepted;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));
            accepted = await store.RecordSanitizationGateDecisionAsync(candidate);

            var retryCandidate = SanitizationGate.Evaluate(
                job.JobId,
                evidence,
                PersistenceTestData.Timestamp.AddMinutes(3),
                new SanitizationGateDecisionId("gate-roundtrip-retry"));
            Assert.Equal(accepted, await store.RecordSanitizationGateDecisionAsync(retryCandidate));
            await store.SetArchiveStateAsync(
                job.JobId,
                isArchived: true,
                PersistenceTestData.Timestamp.AddMinutes(4));
            await store.SetArchiveStateAsync(
                job.JobId,
                isArchived: false,
                PersistenceTestData.Timestamp.AddMinutes(5));
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = await reopened.LoadJobAsync(job.JobId);

        Assert.NotNull(actual);
        Assert.False(actual.IsArchived);
        Assert.Equal(evidence, Assert.Single(actual.SanitizationEvidence));
        Assert.Equal(accepted, Assert.Single(actual.SanitizationGateDecisions));
        Assert.True(SanitizationGate.Inspect(actual).AllowsAssessment);
    }

    [Fact]
    public async Task LaterFailedEvidenceWithOlderTimestampInvalidatesPriorAllowDecision()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("gate-stale");
        var verified = PersistenceTestData.CreateSanitization(
            SanitizationState.Verified,
            "gate-verified",
            PersistenceTestData.Timestamp.AddHours(2));
        var failed = PersistenceTestData.CreateSanitization(
            SanitizationState.Failed,
            "gate-failed",
            PersistenceTestData.Timestamp.AddHours(-2));
        var verifiedDecision = SanitizationGate.Evaluate(
            job.JobId,
            verified,
            PersistenceTestData.Timestamp.AddMinutes(2));

        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [verified]));
            await store.RecordSanitizationGateDecisionAsync(verifiedDecision);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [failed]));

            var stale = await store.LoadJobAsync(job.JobId);
            Assert.NotNull(stale);
            Assert.Equal(SanitizationGateReason.GateDecisionStale, SanitizationGate.Inspect(stale).Reason);
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                store.RecordSanitizationGateDecisionAsync(verifiedDecision));

            var failedDecision = SanitizationGate.Evaluate(
                job.JobId,
                failed,
                PersistenceTestData.Timestamp.AddMinutes(3));
            await store.RecordSanitizationGateDecisionAsync(failedDecision);
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = await reopened.LoadJobAsync(job.JobId);

        Assert.NotNull(actual);
        Assert.Equal(2, actual.SanitizationEvidence.Count);
        Assert.Equal(2, actual.SanitizationGateDecisions.Count);
        var status = SanitizationGate.Inspect(actual);
        Assert.False(status.AllowsAssessment);
        Assert.Equal(SanitizationGateReason.SanitizationFailed, status.Reason);
    }

    [Fact]
    public async Task GateRecordingRejectsArchivedAndMismatchedCandidatesWithoutWrite()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("gate-reject");
        var evidence = PersistenceTestData.CreateSanitization("gate-reject");

        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));

        var wrongPolicy = new SanitizationGateDecision(
            new SanitizationGateDecisionId("gate-wrong-policy"),
            job.JobId,
            evidence.Metadata.EvidenceId,
            "different-policy@1.0.0",
            SanitizationGateOutcome.AllowAssessment,
            SanitizationGateReason.SanitizationVerified,
            PersistenceTestData.Timestamp.AddMinutes(2));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordSanitizationGateDecisionAsync(wrongPolicy));

        await store.SetArchiveStateAsync(
            job.JobId,
            isArchived: true,
            PersistenceTestData.Timestamp.AddMinutes(3));
        var candidate = SanitizationGate.Evaluate(
            job.JobId,
            evidence,
            PersistenceTestData.Timestamp.AddMinutes(4));
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.RecordSanitizationGateDecisionAsync(candidate));

        var actual = await store.LoadJobAsync(job.JobId);
        Assert.NotNull(actual);
        Assert.Empty(actual.SanitizationGateDecisions);
        Assert.Equal(evidence, Assert.Single(actual.SanitizationEvidence));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrderedCrossInstanceEvidenceAndGateWritesAlwaysFinishFailClosed(
        bool gateWriteStartsFirst)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("gate-race");
        var verified = PersistenceTestData.CreateSanitization(
            SanitizationState.Verified,
            "gate-race-verified",
            PersistenceTestData.Timestamp.AddMinutes(1));
        var failed = PersistenceTestData.CreateSanitization(
            SanitizationState.Failed,
            "gate-race-failed",
            PersistenceTestData.Timestamp.AddMinutes(2));
        var candidate = SanitizationGate.Evaluate(
            job.JobId,
            verified,
            PersistenceTestData.Timestamp.AddMinutes(3));

        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await initializer.CreateJobAsync(job);
            await initializer.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [verified]));
        }

        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeWriteCommit);
        await using var blockedStore = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker);
        await using var competingStore = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var competingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SanitizationGateDecision> gateWrite;
        Task evidenceWrite;

        try
        {
            if (gateWriteStartsFirst)
            {
                gateWrite = blockedStore.RecordSanitizationGateDecisionAsync(candidate);
                await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
                evidenceWrite = Task.Run(async () =>
                {
                    competingStarted.SetResult();
                    await competingStore.AppendEvidenceAsync(
                        new JobEvidenceBatch(job.JobId, sanitizationEvidence: [failed]));
                });
                await competingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else
            {
                evidenceWrite = blockedStore.AppendEvidenceAsync(
                    new JobEvidenceBatch(job.JobId, sanitizationEvidence: [failed]));
                await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
                gateWrite = Task.Run(async () =>
                {
                    competingStarted.SetResult();
                    return await competingStore.RecordSanitizationGateDecisionAsync(candidate);
                });
                await competingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            blocker.Release();
        }

        if (gateWriteStartsFirst)
        {
            await gateWrite.WaitAsync(TimeSpan.FromSeconds(10));
            await evidenceWrite.WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            await evidenceWrite.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<JobStoreConflictException>(async () =>
                await gateWrite.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        var actual = await competingStore.LoadJobAsync(job.JobId);

        Assert.NotNull(actual);
        Assert.Equal(2, actual.SanitizationEvidence.Count);
        var status = SanitizationGate.Inspect(actual);
        Assert.False(status.AllowsAssessment);
        Assert.Equal(
            gateWriteStartsFirst
                ? SanitizationGateReason.GateDecisionStale
                : SanitizationGateReason.GateDecisionMissing,
            status.Reason);
        Assert.Equal(gateWriteStartsFirst ? 1 : 0, actual.SanitizationGateDecisions.Count);
    }

    [Fact]
    public async Task CancellationBeforeCommitRollsBackGateDecision()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("gate-cancel");
        var evidence = PersistenceTestData.CreateSanitization("gate-cancel");
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await initializer.CreateJobAsync(job);
            await initializer.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));
        }

        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeWriteCommit);
        await using var store = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker);
        using var cancellation = new CancellationTokenSource();
        var write = store.RecordSanitizationGateDecisionAsync(
            SanitizationGate.Evaluate(
                job.JobId,
                evidence,
                PersistenceTestData.Timestamp.AddMinutes(2)),
            cancellation.Token);
        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        blocker.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        var actual = await store.LoadJobAsync(job.JobId);

        Assert.NotNull(actual);
        Assert.Empty(actual.SanitizationGateDecisions);
        Assert.Equal(SanitizationGateReason.GateDecisionMissing, SanitizationGate.Inspect(actual).Reason);
    }
}
