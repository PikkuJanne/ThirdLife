using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Tests;

public sealed class RecipientToggleKeysTests
{
    private static readonly JobId Job = new("job-recipient-toggle-fixture");
    private static readonly OperatorId Actor = new("operator-recipient-fixture");
    private static readonly DateTimeOffset Recorded = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = new("04c9cd7c-a790-4718-bd1b-600b6e84adf0");

    [Theory]
    [InlineData(HandoverMode.Sealed, false, RecipientSettingAvailability.Deferred)]
    [InlineData(HandoverMode.Assisted, true, RecipientSettingAvailability.Unavailable)]
    public void UnavailableHandoverContextsCannotCreateAChange(HandoverMode mode, bool archived,
        RecipientSettingAvailability availability)
    {
        var preview = ToggleKeysChange.Preview(Handover(mode, archived), new(User, false), requested: true);

        Assert.Equal(availability, preview.Availability);
        Assert.Null(preview.Change);
    }

    [Fact]
    public void DispatchRequiresRecipientApprovalAndTheExactAccountRevisionAndObservedState()
    {
        var assisted = Handover(HandoverMode.Assisted);
        var preview = ToggleKeysChange.Preview(assisted, new(User, false), requested: true);
        var change = Assert.IsType<ToggleKeysChange>(preview.Change);
        Assert.Equal(RecipientSettingAvailability.ReviewRequired, preview.Availability);
        Assert.Throws<InvalidOperationException>(() => change.Approve(change.ChangeId, recipientConfirmed: false));
        Assert.Throws<InvalidOperationException>(() => change.Approve(Guid.NewGuid(), recipientConfirmed: true));

        var approved = change.Approve(change.ChangeId, recipientConfirmed: true);

        Assert.True(approved.CanDispatch(assisted, new(User, false)));
        Assert.False(approved.CanDispatch(assisted, new(User, true)));
        Assert.False(approved.CanDispatch(assisted, new(Guid.NewGuid(), false)));
        Assert.False(approved.CanDispatch(Handover(HandoverMode.Sealed), new(User, false)));
        Assert.False(approved.CanDispatch(Handover(HandoverMode.Assisted, archived: true), new(User, false)));
        var laterChoice = SavedRecipientHandover.Evaluate(Job, false, [.. assisted.History,
            new(2, HandoverMode.Assisted, RecipientSetupArea.Accessibility, RecipientSetupChoice.Declined, true, Actor,
                Recorded.AddMinutes(1))]);
        Assert.False(approved.CanDispatch(laterChoice, new(User, false)));
    }

    [Fact]
    public void AnIndependentReadControlsVerificationAndRestorationRequiresNewApproval()
    {
        var assisted = Handover(HandoverMode.Assisted);
        var change = Assert.IsType<ToggleKeysChange>(ToggleKeysChange.Preview(assisted, new(User, false), true).Change);
        var approved = change.Approve(change.ChangeId, recipientConfirmed: true);

        Assert.Equal(RecipientSettingVerification.Unavailable, approved.Verify(assisted, new(User, null)));
        Assert.Equal(RecipientSettingVerification.Mismatch, approved.Verify(assisted, new(User, false)));
        Assert.Equal(RecipientSettingVerification.Verified, approved.Verify(assisted, new(User, true)));
        Assert.Null(approved.PreviewRestoration(assisted, new(User, false)).Change);

        var restore = Assert.IsType<ToggleKeysChange>(approved.PreviewRestoration(assisted, new(User, true)).Change);
        Assert.False(restore.Requested);
        Assert.Throws<InvalidOperationException>(() => restore.Approve(change.ChangeId, recipientConfirmed: true));
    }

    private static SavedRecipientHandover Handover(HandoverMode mode, bool archived = false) =>
        SavedRecipientHandover.Evaluate(Job, archived, [new(1, mode, null, null, false, Actor, Recorded)]);
}
