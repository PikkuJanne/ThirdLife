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
        var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(Handover(HandoverMode.Assisted), true,
            Destination, 1_000, true).Review);
        var approval = review.Approve(review.ReviewId, true);
        var id = Guid.NewGuid();
        var digest = new string('a', 64);
        var sample = new FileHistoryRestoredSample(review.ReviewId, id, Destination.VolumeBinding, true, 22, digest);
        Assert.Equal(FileHistoryRestoreOutcome.Pending, approval.EvaluateRestore(Handover(HandoverMode.Assisted), true, Destination, id, digest, 22, null));
        Assert.Equal(FileHistoryRestoreOutcome.UnboundObservation, approval.EvaluateRestore(Handover(HandoverMode.Assisted), true, Destination, id, digest, 22, sample with
        {
            ObservedFromSeparateRestore = false,
        }));
        Assert.Equal(FileHistoryRestoreOutcome.Mismatch, approval.EvaluateRestore(Handover(HandoverMode.Assisted), true, Destination, id, digest, 22, sample with { ByteLength = 21 }));
        Assert.Equal(FileHistoryRestoreOutcome.UnboundObservation,
            approval.EvaluateRestore(Handover(HandoverMode.Sealed), true, Destination, id, digest, 22, sample));
        Assert.Equal(FileHistoryRestoreOutcome.Verified, approval.EvaluateRestore(Handover(HandoverMode.Assisted), true, Destination, id, digest, 22, sample));
    }

    private static SavedRecipientHandover Handover(HandoverMode mode) => SavedRecipientHandover.Evaluate(new JobId("job-file-history"),
        false, [new(1, mode, null, null, false, Actor, Recorded)]);
}
