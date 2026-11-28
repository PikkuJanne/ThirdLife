using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class JobDataLifecycleStoreTests
{
    private static readonly OperatorId Actor = new("operator-deletion-test");
    private static readonly DateTimeOffset ReviewedAt = PersistenceTestData.Timestamp.AddMinutes(2);

    [Fact]
    public async Task MigratedLegacyAndReservedFilesUseOneBoundManifestAndKeepOtherJobs()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("deletion-subject");
        var neighbor = PersistenceTestData.CreateJob("deletion-neighbor");
        var observation = PersistenceTestData.CreateObservation("deletion_subject");
        var legacy = LocalFile(JobLocalFileKind.WorkshopReport, 'a');
        FileHistorySampleAudit original;
        await using (var prior = await OpenAsync(workspace, version: 21))
        {
            await prior.CreateJobAsync(job);
            await prior.CreateJobAsync(neighbor);
            await prior.AppendEvidenceAsync(new(job.JobId, [observation]));
            var current = Assert.IsType<HandoverEligibility>(await prior.LoadHandoverReviewAsync(job.JobId));
            var receipt = new HandoverFileReceipt(new(HandoverOutputKind.WorkshopRecord, current.Scope, "report-1", legacy.Sha256),
                legacy.FileName, legacy.ByteLength);
            await prior.SaveHandoverFileAsync(receipt, new ExactReceipt(receipt));
            original = await StageFileHistorySampleAsync(prior, job.JobId);
        }

        var reserved = LocalFile(JobLocalFileKind.RecipientGuide, 'b');
        var externalNeighbor = LocalFile(JobLocalFileKind.WorkshopReport, 'c');
        var present = new HashSet<JobLocalFile> { legacy, externalNeighbor };
        var sample = new JobLocalFile(JobLocalFileKind.FileHistoryOriginal, original.FileName, original.ByteLength, original.Sha256);
        CheckpointId checkpoint;
        string databasePath;
        string removedDirectory;
        await using (var migrated = await OpenAsync(workspace))
        {
            databasePath = migrated.DatabasePath;
            using (var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot))
            {
                removedDirectory = layout.GetJobDirectoryPath(job.JobId);
            }
            await migrated.PublishJobLocalFileAsync(job.JobId, reserved, _ => Assert.True(present.Add(reserved)));
            await migrated.PublishFileHistoryOriginalAsync(job.JobId, original, _ => present.Add(sample));
            await migrated.PublishJobLocalFileAsync(neighbor.JobId, externalNeighbor, _ => { });
            checkpoint = await ArchiveAsync(migrated, job.JobId);
            var review = Assert.IsType<ArchivedJobDeletionReview>(await migrated.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
            Assert.Equal([legacy, reserved, sample], review.Files);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.ExecuteArchivedJobDeletionAsync(job.JobId,
                checkpoint, (_, _) => throw new InvalidOperationException("An unapproved delete dispatched.")));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.PublishFileHistoryOriginalAsync<bool>(job.JobId,
                original, _ => throw new InvalidOperationException("An archived publisher dispatched.")));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.ApproveArchivedJobDeletionAsync(review with { Files = [legacy] }, Actor));
            var approved = await migrated.ApproveArchivedJobDeletionAsync(review, Actor);
            Assert.Equal(Actor, approved.ApprovedBy);
            Assert.Equal(ReviewedAt, approved.ApprovedAtUtc);
        }

        await using (var removal = await OpenAsync(workspace))
        {
            var pending = Assert.IsType<ArchivedJobDeletionReview>(await removal.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
            Assert.Equal(Actor, pending.ApprovedBy);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => removal.SetArchiveStateAsync(job.JobId, false, ReviewedAt));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => removal.AppendEvidenceAsync(new(job.JobId,
                [PersistenceTestData.CreateObservation("approved_delete")])));
            var refusal = await removal.ExecuteArchivedJobDeletionAsync(job.JobId, checkpoint, (file, _) => file == reserved
                ? JobLocalFileRemoval.Retained : present.Remove(file) ? JobLocalFileRemoval.Removed : JobLocalFileRemoval.Missing);
            Assert.False(refusal.Completed);
            Assert.Equal(reserved, Assert.Single(refusal.RetainedFiles));
            Assert.Equal(observation, Assert.Single((await removal.LoadJobAsync(job.JobId))!.Observations));
        }

        await using (var reopened = await OpenAsync(workspace))
        {
            var result = await reopened.ExecuteArchivedJobDeletionAsync(job.JobId, checkpoint, (file, _) =>
                present.Remove(file) ? JobLocalFileRemoval.Removed : JobLocalFileRemoval.Missing);
            Assert.True(result.Completed);
            Assert.False(result.EmptyDirectoryRetained);
            Assert.Empty(result.RetainedFiles);
            Assert.Equal(externalNeighbor, Assert.Single(present));
            Assert.Null(await reopened.LoadJobAsync(job.JobId));
            Assert.Equal(neighbor, (await reopened.LoadJobAsync(neighbor.JobId))?.Job);
        }

        Assert.False(Directory.Exists(removedDirectory));
        Assert.Equal(0, new FileInfo(databasePath + "-journal").Length);
        Assert.True((await File.ReadAllBytesAsync(databasePath)).AsSpan().IndexOf(Encoding.UTF8.GetBytes(job.JobId.Value)) < 0);
        await using var verified = await OpenAsync(workspace);
        Assert.Null(await verified.LoadJobAsync(job.JobId));
        Assert.NotNull(await verified.LoadJobAsync(neighbor.JobId));
    }

    [Fact]
    public async Task InterruptedDatabaseCommitReopensTheApprovalAndRechecksAlreadyMissingFiles()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var report = LocalFile(JobLocalFileKind.WorkshopReport, 'a');
        CheckpointId checkpoint;
        await using (var initial = await OpenAsync(workspace))
        {
            await initial.CreateJobAsync(job);
            await initial.PublishJobLocalFileAsync(job.JobId, report, _ => { });
            checkpoint = await ArchiveAsync(initial, job.JobId);
            var review = Assert.IsType<ArchivedJobDeletionReview>(await initial.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
            await initial.ApproveArchivedJobDeletionAsync(review, Actor);
        }

        var removed = false;
        await using (var interrupted = await OpenAsync(workspace, fault: new ThrowingFaultInjector(JobStoreFaultPoint.BeforeWriteCommit)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.ExecuteArchivedJobDeletionAsync(job.JobId,
                checkpoint, (file, _) =>
                {
                    Assert.Equal(report, file);
                    removed = true;
                    return JobLocalFileRemoval.Removed;
                }));
        }

        await using var reopened = await OpenAsync(workspace);
        Assert.True(removed);
        Assert.NotNull(await reopened.LoadJobAsync(job.JobId));
        var pending = Assert.IsType<ArchivedJobDeletionReview>(await reopened.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
        Assert.Equal(Actor, pending.ApprovedBy);
        var result = await reopened.ExecuteArchivedJobDeletionAsync(job.JobId, checkpoint, (file, _) =>
        {
            Assert.Equal(report, file);
            return JobLocalFileRemoval.Missing;
        });
        Assert.True(result.Completed);
        Assert.Null(await reopened.LoadJobAsync(job.JobId));
    }

    [Fact]
    public async Task CompetingDeletionApprovalStopsTheReservedPublisher()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var report = LocalFile(JobLocalFileKind.WorkshopReport, 'a');
        await using var archivist = await OpenAsync(workspace);
        await archivist.CreateJobAsync(job);
        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.AfterWriteCommit);
        await using var publisher = await OpenAsync(workspace, fault: blocker);
        var writing = publisher.PublishJobLocalFileAsync(job.JobId, report,
            _ => throw new InvalidOperationException("The competing deletion approval must stop this write."));
        CheckpointId archived;
        try
        {
            await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
            archived = await ArchiveAsync(archivist, job.JobId);
            var review = Assert.IsType<ArchivedJobDeletionReview>(await archivist.ReviewArchivedJobDeletionAsync(job.JobId, archived));
            Assert.Equal(report, Assert.Single(review.Files));
            await archivist.ApproveArchivedJobDeletionAsync(review, Actor);
        }
        finally
        {
            blocker.Release();
        }

        await Assert.ThrowsAsync<JobStoreConflictException>(() => writing);
        var pending = Assert.IsType<ArchivedJobDeletionReview>(await archivist.ReviewArchivedJobDeletionAsync(job.JobId, archived));
        Assert.Equal(Actor, pending.ApprovedBy);
    }

    [Fact]
    public async Task NonpendingArchivedPublicationPreservesViewingAndInvalidatesTheEarlierPreview()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        await using var store = await OpenAsync(workspace);
        await store.CreateJobAsync(job);
        var archived = await ArchiveAsync(store, job.JobId);
        var stale = Assert.IsType<ArchivedJobDeletionReview>(await store.ReviewArchivedJobDeletionAsync(job.JobId, archived));
        var report = LocalFile(JobLocalFileKind.WorkshopReport, 'a');
        var published = false;
        await store.PublishJobLocalFileAsync(job.JobId, report, _ => published = true);
        Assert.True(published);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.ApproveArchivedJobDeletionAsync(stale, Actor));
        var current = Assert.IsType<ArchivedJobDeletionReview>(await store.ReviewArchivedJobDeletionAsync(job.JobId, archived));
        Assert.Equal(report, Assert.Single(current.Files));
        await store.SetArchiveStateAsync(job.JobId, false, ReviewedAt);
        Assert.NotEqual(archived, await ArchiveAsync(store, job.JobId));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.ApproveArchivedJobDeletionAsync(current, Actor));
        Assert.Null(await store.ReviewArchivedJobDeletionAsync(job.JobId, archived));
    }

    [Fact]
    public async Task FailedFilePublisherPreservesItsTypedFailureAndTheExactReservation()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        await using var store = await OpenAsync(workspace);
        await store.CreateJobAsync(job);
        var report = LocalFile(JobLocalFileKind.WorkshopReport, 'a');
        var failure = new FixturePublicationException();
        Assert.Same(failure, await Assert.ThrowsAsync<FixturePublicationException>(() =>
            store.PublishJobLocalFileAsync(job.JobId, report, _ => throw failure)));
        var archived = await ArchiveAsync(store, job.JobId);
        var review = Assert.IsType<ArchivedJobDeletionReview>(await store.ReviewArchivedJobDeletionAsync(job.JobId, archived));
        Assert.Equal(report, Assert.Single(review.Files));
    }

    [Fact]
    public async Task CrossJobLegacyReceiptAndUnknownJobDirectoryContentsBlockDataRemoval()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("deletion-subject");
        var neighbor = PersistenceTestData.CreateJob("deletion-neighbor");
        var targetFile = LocalFile(JobLocalFileKind.WorkshopReport, 'a');
        await using var store = await OpenAsync(workspace);
        await store.CreateJobAsync(job);
        await store.CreateJobAsync(neighbor);
        var neighborReview = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(neighbor.JobId));
        var otherClaim = new HandoverFileReceipt(new(HandoverOutputKind.WorkshopRecord, neighborReview.Scope, "report-1", targetFile.Sha256),
            targetFile.FileName, targetFile.ByteLength);
        await store.SaveHandoverFileAsync(otherClaim, new ExactReceipt(otherClaim));
        await store.PublishJobLocalFileAsync(job.JobId, targetFile, _ => { });
        var checkpoint = await ArchiveAsync(store, job.JobId);
        await Assert.ThrowsAsync<JobStoreCorruptionException>(() => store.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
        Assert.NotNull(await store.LoadJobAsync(job.JobId));
        Assert.NotNull(await store.LoadJobAsync(neighbor.JobId));

        var unknown = PersistenceTestData.CreateJob("deletion-unknown");
        await store.CreateJobAsync(unknown);
        var unknownCheckpoint = await ArchiveAsync(store, unknown.JobId);
        using var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot);
        var unknownPath = Path.Combine(layout.GetJobDirectoryPath(unknown.JobId), "unattributed-decoy.txt");
        await File.WriteAllTextAsync(unknownPath, "synthetic unrelated data");
        var unknownReview = Assert.IsType<ArchivedJobDeletionReview>(
            await store.ReviewArchivedJobDeletionAsync(unknown.JobId, unknownCheckpoint));
        await Assert.ThrowsAsync<JobStorePathException>(() => store.ApproveArchivedJobDeletionAsync(unknownReview, Actor));
        Assert.Equal("synthetic unrelated data", await File.ReadAllTextAsync(unknownPath));
    }

    [Fact]
    public async Task CrossJobOriginalSampleClaimIsRefusedUsingThePersistedSampleId()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("deletion-subject");
        var neighbor = PersistenceTestData.CreateJob("deletion-neighbor");
        string databasePath;
        FileHistorySampleAudit original;
        CheckpointId checkpoint;
        await using (var store = await OpenAsync(workspace))
        {
            databasePath = store.DatabasePath;
            await store.CreateJobAsync(job);
            await store.CreateJobAsync(neighbor);
            original = await StageFileHistorySampleAsync(store, job.JobId);
            checkpoint = await ArchiveAsync(store, job.JobId);
            Assert.NotNull(await store.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
        }

        // This valid fixture reproduces an otherwise impractical cross-job UUID collision.
        var payload = JsonSerializer.Serialize(new { JobId = neighbor.JobId, Sequence = 1, Audit = original },
            DomainJson.CreateStrictOptions());
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        await SqliteTestControl.ExecuteAsync(databasePath, $"""
            INSERT INTO file_history_sample_audit (job_id, sequence, payload_json, payload_sha256)
            VALUES ('{neighbor.JobId.Value}', 1, '{payload}', '{digest}');
            """);
        await using var reopened = await OpenAsync(workspace);
        Assert.Equal(original, Assert.Single(await reopened.LoadFileHistorySampleHistoryAsync(neighbor.JobId)));
        await Assert.ThrowsAsync<JobStoreCorruptionException>(() => reopened.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
    }

    private static async Task<FileHistorySampleAudit> StageFileHistorySampleAsync(SqliteJobStore store, JobId jobId)
    {
        var handover = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(jobId));
        await store.SaveHandoverModeAsync(jobId, handover, HandoverMode.Assisted, Actor);
        var assisted = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(jobId));
        var destination = new FileHistoryDestinationObservation(Guid.NewGuid(), true, 50_000);
        var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(assisted, true, destination, 1, true).Review);
        var approval = review.Approve(review.ReviewId, true);
        return await store.StageFileHistorySampleAsync(approval, approval.CreateRestoreSample(assisted, true, destination), Actor);
    }

    private static async Task<CheckpointId> ArchiveAsync(SqliteJobStore store, JobId jobId)
    {
        await store.SetArchiveStateAsync(jobId, true, ReviewedAt);
        var archived = Assert.IsType<StoredJob>(await store.LoadJobAsync(jobId));
        return archived.Checkpoints[^1].CheckpointId;
    }

    private static JobLocalFile LocalFile(JobLocalFileKind kind, char token) => new(kind,
        kind == JobLocalFileKind.WorkshopReport ? $"workshop-{new string(token, 32)}.txt" : $"recipient-guide-{new string(token, 32)}.html",
        20, new string(token, 64));

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, int? version = null,
        IJobStoreFaultInjector? fault = null) => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
        version ?? SqliteJobStore.CurrentSchemaVersion, new FixedTimeProvider(ReviewedAt), fault, () => 10_000, () => 10);

    private sealed class ExactReceipt(HandoverFileReceipt expected) : IHandoverFileVerifier
    {
        public bool MatchesPublishedFile(HandoverFileReceipt receipt, CancellationToken token) => receipt == expected;
    }

    private sealed class FixturePublicationException : IOException
    {
    }
}
