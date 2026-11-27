using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class FileHistorySampleAuditStoreTests
{
    [Fact]
    public async Task MigratedJobPreservesTheExactUnconfirmedSampleAndHistoricalScope()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var actor = new OperatorId("operator-sample-audit");
        var destination = new FileHistoryDestinationObservation(Guid.NewGuid(), true, 50_000);
        FileHistorySampleAudit pending;
        var schema = SqliteJobStore.CurrentSchemaVersion;
        await using (var original = await Open(schema - 1))
        {
            await original.CreateJobAsync(job);
            var initial = Assert.IsType<SavedRecipientHandover>(await original.LoadRecipientHandoverAsync(job.JobId));
            await original.SaveHandoverModeAsync(job.JobId, initial, HandoverMode.Assisted, actor);
        }

        await using (var current = await Open(schema))
        {
            var handover = Assert.IsType<SavedRecipientHandover>(await current.LoadRecipientHandoverAsync(job.JobId));
            var first = RequiredReview(handover);
            var second = RequiredReview(handover);
            var approval = first.Approve(first.ReviewId, true);
            var otherApproval = second.Approve(second.ReviewId, true);
            var sample = approval.CreateRestoreSample(handover, true, destination);
            await Assert.ThrowsAsync<ArgumentException>(() => current.StageFileHistorySampleAsync(otherApproval, sample, actor));
            pending = await current.StageFileHistorySampleAsync(approval, sample, actor);
            Assert.Equal(FileHistorySampleAuditState.Preparing, pending.State);
            Assert.Equal(sample.Sha256, pending.Sha256);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => current.StageFileHistorySampleAsync(approval, sample, actor));
        }

        await using var reopened = await Open(schema);
        Assert.Equal(pending, Assert.Single(await reopened.LoadFileHistorySampleHistoryAsync(job.JobId)));
        var resumedActor = new OperatorId("operator-resumed-sample");
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.RecordFileHistorySampleAsync(
            job.JobId, pending, FileHistorySampleAuditState.Prepared, resumedActor));
        var prepared = await reopened.RecordFileHistorySampleAsync(job.JobId, pending, FileHistorySampleAuditState.Prepared, actor);
        var matched = await reopened.RecordFileHistorySampleAsync(job.JobId, prepared, FileHistorySampleAuditState.CopyMatches, actor);
        Assert.Equal(matched, await reopened.RecordFileHistorySampleAsync(
            job.JobId, matched, FileHistorySampleAuditState.CopyMatches, actor));
        var confirmedBySecondOperator = await reopened.RecordFileHistorySampleAsync(
            job.JobId, matched, FileHistorySampleAuditState.CopyMatches, resumedActor);
        Assert.Equal(resumedActor, confirmedBySecondOperator.OperatorId);
        var assisted = Assert.IsType<SavedRecipientHandover>(await reopened.LoadRecipientHandoverAsync(job.JobId));
        await reopened.SaveHandoverModeAsync(job.JobId, assisted, HandoverMode.Sealed, actor);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.RecordFileHistorySampleAsync(
            job.JobId, confirmedBySecondOperator, FileHistorySampleAuditState.CopyMismatch, resumedActor));
        Assert.Equal(new[] { pending, prepared, matched, confirmedBySecondOperator }, await reopened.LoadFileHistorySampleHistoryAsync(job.JobId));

        FileHistoryReview RequiredReview(SavedRecipientHandover handover) =>
            Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(handover, true, destination, 1, true).Review);

        Task<SqliteJobStore> Open(int version) => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, version,
            clock, null, () => 10_000, () => 10);
    }
}
