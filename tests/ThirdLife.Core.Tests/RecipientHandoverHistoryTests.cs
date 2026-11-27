using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Tests;

public sealed class RecipientHandoverHistoryTests
{
    private static readonly JobId Job = new("job-handover-history");
    private static readonly OperatorId Operator = new("operator-handover-test");
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LaterModeSelectionsKeepHistoryAndDeferPreviouslyRecordedChoices()
    {
        var history = new[]
        {
            Select(1, HandoverMode.Assisted),
            new RecipientHandoverEvent(2, HandoverMode.Assisted, RecipientSetupArea.Backup,
                RecipientSetupChoice.GuidanceRequested, true, Operator, RecordedAt),
            Select(3, HandoverMode.Sealed),
            Select(4, HandoverMode.Assisted),
        };
        var state = SavedRecipientHandover.Evaluate(Job, false, history);

        Assert.Equal(HandoverMode.Assisted, state.Handover?.Mode);
        Assert.Equal(4, state.Sequence);
        Assert.Equal(4, state.ModeSequence);
        Assert.Equal(history.AsEnumerable(), state.History);
        Assert.All(state.Handover!.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
        Assert.True(state.History[1].RecipientPresent);

        var earlier = SavedRecipientHandover.Evaluate(Job, true, history[..2]);
        Assert.False(earlier.CanRecord);
        Assert.Equal(1, earlier.ModeSequence);
        Assert.Contains(new RecipientSetupDecision(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested),
            earlier.Handover!.Decisions);
    }

    [Theory]
    [InlineData(HandoverMode.Assisted, false)]
    [InlineData(HandoverMode.Sealed, true)]
    public void AChoiceEventRequiresPresenceAndAssistedMode(HandoverMode mode, bool recipientPresent)
    {
        Assert.Throws<ArgumentException>(() =>
            new RecipientHandoverEvent(1, mode, RecipientSetupArea.Backup, RecipientSetupChoice.Declined,
                recipientPresent, Operator, RecordedAt));
    }

    [Fact]
    public void InvalidHistoricalTransitionDoesNotProduceCurrentChoices()
    {
        var choice = new RecipientHandoverEvent(1, HandoverMode.Assisted, RecipientSetupArea.Backup,
            RecipientSetupChoice.Declined, true, Operator, RecordedAt);
        Assert.Throws<ArgumentException>(() => SavedRecipientHandover.Evaluate(Job, false, [choice]));
        Assert.Throws<ArgumentException>(() => SavedRecipientHandover.Evaluate(Job, false,
            [Select(1, HandoverMode.Sealed), Select(2, HandoverMode.Sealed)]));
    }

    private static RecipientHandoverEvent Select(long sequence, HandoverMode mode) =>
        new(sequence, mode, null, null, false, Operator, RecordedAt);
}
