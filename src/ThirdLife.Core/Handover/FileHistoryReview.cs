using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

public enum FileHistoryReadiness
{
    Deferred = 1,
    RouteUnavailable,
    DestinationUnavailable,
    ScopeSizeUnavailable,
    InsufficientCapacity,
    RecoveryOwnershipUnconfirmed,
    ReviewRequired,
}

public sealed record FileHistoryDestinationObservation
{
    public FileHistoryDestinationObservation(Guid volumeBinding, bool eligible, long? availableBytes)
    {
        if (availableBytes < 0 || eligible && volumeBinding == Guid.Empty)
        {
            throw new ArgumentException("The destination observation is invalid.");
        }

        VolumeBinding = volumeBinding;
        Eligible = eligible;
        AvailableBytes = availableBytes;
    }

    // This opaque binding is scoped to the local review. It is not a volume name or path.
    public Guid VolumeBinding { get; }
    public bool Eligible { get; }
    public long? AvailableBytes { get; }
}

public sealed record FileHistoryPreview(FileHistoryReadiness Readiness, FileHistoryReview? Review);

public sealed class FileHistoryReview
{
    private FileHistoryReview(SavedRecipientHandover handover, FileHistoryDestinationObservation destination, long requiredBytes)
    {
        ReviewId = Guid.NewGuid();
        JobId = handover.JobId;
        HandoverSequence = handover.Sequence;
        VolumeBinding = destination.VolumeBinding;
        RequiredBytes = requiredBytes;
    }

    public Guid ReviewId { get; }
    public JobId JobId { get; }
    public long HandoverSequence { get; }
    public Guid VolumeBinding { get; }
    public long RequiredBytes { get; }

    public static FileHistoryPreview Inspect(SavedRecipientHandover handover, bool routeAvailable,
        FileHistoryDestinationObservation? destination, long? requiredBytes, bool recoveryOwnershipConfirmed)
    {
        ArgumentNullException.ThrowIfNull(handover);
        if (handover.IsArchived || handover.Handover?.Mode != HandoverMode.Assisted)
        {
            return new(FileHistoryReadiness.Deferred, null);
        }
        if (!routeAvailable) { return new(FileHistoryReadiness.RouteUnavailable, null); }
        if (destination is not { Eligible: true, AvailableBytes: { } available })
        {
            return new(FileHistoryReadiness.DestinationUnavailable, null);
        }
        if (requiredBytes is not > 0) { return new(FileHistoryReadiness.ScopeSizeUnavailable, null); }
        if (available < requiredBytes) { return new(FileHistoryReadiness.InsufficientCapacity, null); }
        if (!recoveryOwnershipConfirmed) { return new(FileHistoryReadiness.RecoveryOwnershipUnconfirmed, null); }

        return new(FileHistoryReadiness.ReviewRequired, new(handover, destination, requiredBytes.Value));
    }

    public FileHistoryApproval Approve(Guid reviewedId, bool recipientConfirmed)
    {
        if (reviewedId != ReviewId || !recipientConfirmed)
        {
            throw new InvalidOperationException("The recipient must confirm the exact reviewed backup destination and scope.");
        }

        return new FileHistoryApproval(this);
    }
}

public sealed class FileHistoryApproval
{
    internal FileHistoryApproval(FileHistoryReview review) => Review = review;
    public FileHistoryReview Review { get; }

    public bool IsCurrent(SavedRecipientHandover handover, bool routeAvailable, FileHistoryDestinationObservation? destination)
    {
        ArgumentNullException.ThrowIfNull(handover);
        return routeAvailable && handover is { IsArchived: false, Handover.Mode: HandoverMode.Assisted } &&
            handover.JobId == Review.JobId && handover.Sequence == Review.HandoverSequence &&
            destination is { Eligible: true } && destination.VolumeBinding == Review.VolumeBinding &&
            destination.AvailableBytes >= Review.RequiredBytes;
    }

    public FileHistoryRestoreOutcome EvaluateRestore(SavedRecipientHandover handover, bool routeAvailable,
        FileHistoryDestinationObservation? destination, Guid sampleId, string expectedSha256, int expectedLength,
        FileHistoryRestoredSample? restored)
    {
        if (sampleId == Guid.Empty || expectedSha256 is not { Length: 64 } ||
            expectedSha256.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            expectedLength is < 1 or > 4096)
        {
            throw new ArgumentException("A bounded harmless sample with exact identity is required.");
        }
        if (restored is null) { return FileHistoryRestoreOutcome.Pending; }
        if (!IsCurrent(handover, routeAvailable, destination) || restored.ReviewId != Review.ReviewId || restored.SampleId != sampleId || restored.VolumeBinding != Review.VolumeBinding ||
            !restored.ObservedFromSeparateRestore)
        {
            return FileHistoryRestoreOutcome.UnboundObservation;
        }
        return restored.ByteLength == expectedLength && restored.Sha256 == expectedSha256
            ? FileHistoryRestoreOutcome.Verified : FileHistoryRestoreOutcome.Mismatch;
    }
}

public enum FileHistoryRestoreOutcome
{
    Pending = 1,
    UnboundObservation,
    Mismatch,
    Verified,
}

public sealed record FileHistoryRestoredSample(Guid ReviewId, Guid SampleId, Guid VolumeBinding,
    bool ObservedFromSeparateRestore, int ByteLength, string Sha256);
