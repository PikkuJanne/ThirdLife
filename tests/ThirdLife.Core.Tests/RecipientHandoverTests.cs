using ThirdLife.Core.Handover;

namespace ThirdLife.Core.Tests;

public class RecipientHandoverTests
{
    [Theory]
    [InlineData(HandoverMode.Assisted)]
    [InlineData(HandoverMode.Sealed)]
    public void StartingHandoverLeavesAllRecipientSetupDeferred(HandoverMode mode)
    {
        var handover = RecipientHandover.Start(mode);

        Assert.Equal(mode, handover.Mode);
        Assert.Equal(Enum.GetValues<RecipientSetupArea>(), handover.Decisions.Select(decision => decision.Area));
        Assert.All(handover.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
    }

    [Theory]
    [InlineData(HandoverMode.Assisted, false)]
    [InlineData(HandoverMode.Sealed, true)]
    [InlineData(HandoverMode.Sealed, false)]
    public void RecordingAChoiceRequiresAssistedHandoverAndRecipientPresence(HandoverMode mode, bool recipientPresent)
    {
        var handover = RecipientHandover.Start(mode);

        Assert.Throws<InvalidOperationException>(() =>
            handover.RecordChoice(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested, recipientPresent));
    }

    [Fact]
    public void ModeChangeResetsChoicesAndOriginalCanStillBeReviewed()
    {
        var assisted = RecipientHandover.Start(HandoverMode.Assisted)
            .RecordChoice(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested, recipientPresent: true)
            .RecordChoice(RecipientSetupArea.PersonalAccount, RecipientSetupChoice.Declined, recipientPresent: true);

        Assert.Same(assisted, assisted.ChangeMode(HandoverMode.Assisted));
        var sealedMode = assisted.ChangeMode(HandoverMode.Sealed);
        var reopenedAssisted = sealedMode.ChangeMode(HandoverMode.Assisted);

        Assert.Contains(new RecipientSetupDecision(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested), assisted.Decisions);
        Assert.Contains(new RecipientSetupDecision(RecipientSetupArea.PersonalAccount, RecipientSetupChoice.Declined), assisted.Decisions);
        Assert.All(sealedMode.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
        Assert.All(reopenedAssisted.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
    }
}
