using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

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
}
