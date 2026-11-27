using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Tests;

public sealed class FileHistoryReviewTests
{
    private static readonly FileHistoryDestinationObservation Destination = new(Guid.NewGuid(), true, 8_000_000);
    private static readonly OperatorId Actor = new("operator-backup-fixture");
    private static readonly DateTimeOffset Recorded = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SealedAndUnsupportedHandoverRemainDeferredOrUnavailable()
    {
        Assert.Equal(new FileHistoryPreview(FileHistoryReadiness.Deferred, null),
            FileHistoryReview.Inspect(Handover(HandoverMode.Sealed), true, Destination, 1, true));
        Assert.Equal(new FileHistoryPreview(FileHistoryReadiness.RouteUnavailable, null),
            FileHistoryReview.Inspect(Handover(HandoverMode.Assisted), false, Destination, 1, true));
    }

    [Fact]
    public void MissingCapacityAndRecoveryOwnershipPreventReview()
    {
        var handover = Handover(HandoverMode.Assisted);
        Assert.Equal(FileHistoryReadiness.DestinationUnavailable,
            FileHistoryReview.Inspect(handover, true, new(Destination.VolumeBinding, true, null), 1, true).Readiness);
        Assert.Equal(FileHistoryReadiness.ScopeSizeUnavailable,
            FileHistoryReview.Inspect(handover, true, Destination, null, true).Readiness);
        Assert.Equal(FileHistoryReadiness.InsufficientCapacity,
            FileHistoryReview.Inspect(handover, true, Destination, 10_000_000, true).Readiness);
        Assert.Equal(FileHistoryReadiness.RecoveryOwnershipUnconfirmed,
            FileHistoryReview.Inspect(handover, true, Destination, 1, false).Readiness);
    }

    [Fact]
    public void ExactApprovalExpiresWhenDestinationCapacityOrRecipientContextChanges()
    {
        var assisted = Handover(HandoverMode.Assisted);
        var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(assisted, true, Destination, 1_000, true).Review);
        Assert.Throws<InvalidOperationException>(() => review.Approve(Guid.NewGuid(), true));
        Assert.Throws<InvalidOperationException>(() => review.Approve(review.ReviewId, false));
        var approval = review.Approve(review.ReviewId, true);
        Assert.True(approval.IsCurrent(assisted, true, Destination));
        Assert.False(approval.IsCurrent(assisted, true, new(Guid.NewGuid(), true, 8_000_000)));
        Assert.False(approval.IsCurrent(assisted, true, new(Destination.VolumeBinding, true, 999)));
        Assert.False(approval.IsCurrent(Handover(HandoverMode.Sealed), true, Destination));
    }

    [Fact]
    public void AVerifiedHarmlessRestoreRequiresTheExactSampleScopeAndBytes()
    {
        var handover = Handover(HandoverMode.Assisted);
        var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(handover, true, Destination, 1_000, true).Review);
        var approval = review.Approve(review.ReviewId, true);
        Assert.Throws<InvalidOperationException>(() => approval.CreateRestoreSample(Handover(HandoverMode.Sealed), true, Destination));
        var sample = approval.CreateRestoreSample(handover, true, Destination);
        var digest = new FileHistorySampleDigest(sample.ByteLength, sample.Sha256);
        var observed = new FileHistorySampleObservation(sample.SampleId, digest, digest);

        Assert.Equal(FileHistoryRestoreOutcome.Pending, approval.EvaluateRestore(handover, true, Destination, sample, null, true));
        Assert.Equal(FileHistoryRestoreOutcome.Pending, approval.EvaluateRestore(handover, true, Destination, sample, observed, false));
        Assert.Equal(FileHistoryRestoreOutcome.UnboundObservation,
            approval.EvaluateRestore(handover, true, Destination, sample, observed with { SampleId = Guid.NewGuid() }, true));
        Assert.Equal(FileHistoryRestoreOutcome.Mismatch,
            approval.EvaluateRestore(handover, true, Destination, sample, observed with { Recovered = digest with { ByteLength = 1 } }, true));
        Assert.Equal(FileHistoryRestoreOutcome.UnboundObservation,
            approval.EvaluateRestore(Handover(HandoverMode.Sealed), true, Destination, sample, observed, true));
        Assert.Equal(FileHistoryRestoreOutcome.Verified, approval.EvaluateRestore(handover, true, Destination, sample, observed, true));
    }

    [Fact]
    public void FreshApprovedReviewReopensOnlyTheExactPreparedSample()
    {
        var handover = Handover(HandoverMode.Assisted);
        var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(handover, true, Destination, 1, true).Review);
        var original = review.Approve(review.ReviewId, true).CreateRestoreSample(handover, true, Destination);
        var fresh = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(handover, true, Destination, 1, true).Review);
        var approval = fresh.Approve(fresh.ReviewId, true);
        var prepared = new FileHistorySampleAudit(original.SampleId, handover.Sequence, original.Sha256,
            original.ByteLength, Actor, Recorded, FileHistorySampleAuditState.Prepared);
        var sample = Assert.IsType<FileHistoryRestoreSample>(approval.ReopenRestoreSample(handover, true, Destination, prepared));
        Assert.True(sample.IsFor(approval));
        Assert.Equal(original.CopyContent(), sample.CopyContent());
        Assert.Null(approval.ReopenRestoreSample(Handover(HandoverMode.Sealed), true, Destination, prepared));
        var badDigest = new FileHistorySampleAudit(original.SampleId, handover.Sequence, new string('a', 64),
            original.ByteLength, Actor, Recorded, FileHistorySampleAuditState.Prepared);
        Assert.Null(approval.ReopenRestoreSample(handover, true, Destination, badDigest));
        var incomplete = new FileHistorySampleAudit(original.SampleId, handover.Sequence, original.Sha256,
            original.ByteLength, Actor, Recorded, FileHistorySampleAuditState.Preparing);
        Assert.Null(approval.ReopenRestoreSample(handover, true, Destination, incomplete));
    }

    private static SavedRecipientHandover Handover(HandoverMode mode) => SavedRecipientHandover.Evaluate(new JobId("job-file-history"),
        false, [new(1, mode, null, null, false, Actor, Recorded)]);
}
