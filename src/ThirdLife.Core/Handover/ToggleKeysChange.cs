using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

public enum RecipientSettingAvailability
{
    Deferred = 1,
    Unavailable,
    NoChange,
    ReviewRequired,
}

public enum RecipientSettingVerification
{
    Verified = 1,
    Mismatch,
    Unavailable,
}

/// <summary>The runtime supplies an opaque binding for the current Windows user context.</summary>
public sealed record ToggleKeysObservation(Guid UserBinding, bool? Enabled);

public sealed record ToggleKeysPreview(RecipientSettingAvailability Availability, ToggleKeysChange? Change);

/// <summary>Proposes the Toggle Keys setting for the current Windows user.</summary>
public sealed class ToggleKeysChange
{
    private ToggleKeysChange(JobId jobId, long handoverSequence, Guid userBinding, bool before, bool requested)
    {
        ChangeId = Guid.NewGuid();
        JobId = jobId;
        HandoverSequence = handoverSequence;
        UserBinding = userBinding;
        Before = before;
        Requested = requested;
    }

    public Guid ChangeId { get; }
    public JobId JobId { get; }
    public long HandoverSequence { get; }
    public Guid UserBinding { get; }
    public bool Before { get; }
    public bool Requested { get; }

    public static ToggleKeysPreview Preview(SavedRecipientHandover handover, ToggleKeysObservation current, bool requested)
    {
        ArgumentNullException.ThrowIfNull(handover);
        ArgumentNullException.ThrowIfNull(current);
        if (handover.Handover?.Mode != HandoverMode.Assisted)
        {
            return new(RecipientSettingAvailability.Deferred, null);
        }

        if (handover.IsArchived || current.UserBinding == Guid.Empty || current.Enabled is not { } before)
        {
            return new(RecipientSettingAvailability.Unavailable, null);
        }

        return before == requested
            ? new(RecipientSettingAvailability.NoChange, null)
            : new(RecipientSettingAvailability.ReviewRequired,
                new(handover.JobId, handover.Sequence, current.UserBinding, before, requested));
    }

    public ToggleKeysApproval Approve(Guid reviewedChangeId, bool recipientConfirmed)
    {
        if (reviewedChangeId != ChangeId || !recipientConfirmed)
        {
            throw new InvalidOperationException("The recipient must approve this exact current-user setting change.");
        }

        return new(this);
    }

    internal bool MatchesContext(SavedRecipientHandover handover, ToggleKeysObservation observation) =>
        handover.JobId == JobId && handover is { IsArchived: false, Handover.Mode: HandoverMode.Assisted } &&
        handover.Sequence == HandoverSequence && observation.UserBinding == UserBinding;
}

public sealed class ToggleKeysApproval
{
    internal ToggleKeysApproval(ToggleKeysChange change) => Change = change;

    public ToggleKeysChange Change { get; }

    public bool CanDispatch(SavedRecipientHandover handover, ToggleKeysObservation current) =>
        Change.MatchesContext(handover, current) && current.Enabled == Change.Before;

    public RecipientSettingVerification Verify(SavedRecipientHandover handover, ToggleKeysObservation observed) =>
        !Change.MatchesContext(handover, observed) || observed.Enabled is null ? RecipientSettingVerification.Unavailable
            : observed.Enabled == Change.Requested ? RecipientSettingVerification.Verified : RecipientSettingVerification.Mismatch;

    public ToggleKeysPreview PreviewRestoration(SavedRecipientHandover handover, ToggleKeysObservation observed) =>
        Verify(handover, observed) == RecipientSettingVerification.Verified
            ? ToggleKeysChange.Preview(handover, observed, Change.Before)
            : new(RecipientSettingAvailability.Unavailable, null);
}
