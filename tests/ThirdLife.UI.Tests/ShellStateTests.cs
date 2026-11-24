using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

public sealed class ShellStateTests
{
    [Fact]
    public void StartupHasNoJobEvidenceOrRunningOperation()
    {
        var model = new ShellViewModel();

        Assert.Equal(ShellStage.Intake, model.SelectedStage);
        Assert.Equal(ShellActivity.Idle, model.Activity);
        Assert.Contains("No job is open", model.StageSummary, StringComparison.Ordinal);
        Assert.Contains("Not available", model.EvidenceText, StringComparison.Ordinal);
        Assert.Contains("No plan exists", model.PlanText, StringComparison.Ordinal);
        Assert.Contains("unknown", model.StatusText, StringComparison.Ordinal);
        Assert.Equal("No operation is running.", model.ActivityText);
        Assert.False(model.IsBusy);
        Assert.False(model.IsIndeterminate);
        Assert.Equal(0, model.ProgressValue);
        Assert.Equal(1, model.ProgressMaximum);
    }

    [Theory]
    [InlineData(ShellStage.Intake)]
    [InlineData(ShellStage.Inspect)]
    public void AvailableStagesKeepEvidenceUnknown(ShellStage stage)
    {
        var model = new ShellViewModel();

        Assert.True(ShellViewModel.CanNavigate(stage));
        Assert.True(model.Navigate(stage));

        Assert.Equal(stage, model.SelectedStage);
        Assert.Contains("No job is open", model.StageSummary, StringComparison.Ordinal);
        Assert.Contains("Not available", model.EvidenceText, StringComparison.Ordinal);
        Assert.Contains("No plan exists", model.PlanText, StringComparison.Ordinal);
        Assert.Contains("Device readiness is unknown", model.StatusText, StringComparison.Ordinal);
        Assert.Equal(ShellActivity.Idle, model.Activity);
    }

    [Theory]
    [InlineData(ShellStage.Decide)]
    [InlineData(ShellStage.Prepare)]
    [InlineData(ShellStage.Verify)]
    [InlineData(ShellStage.Handover)]
    [InlineData((ShellStage)(-1))]
    [InlineData((ShellStage)int.MaxValue)]
    public void UnavailableAndUnknownStagesCannotAdvanceOrAnnounceSuccess(ShellStage stage)
    {
        var model = new ShellViewModel();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);

        Assert.False(ShellViewModel.CanNavigate(stage));
        Assert.False(model.Navigate(stage));

        Assert.Equal(ShellStage.Intake, model.SelectedStage);
        Assert.Empty(notifications);
        Assert.Contains("unknown", model.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationAnnouncesChangedContextOnceAndKeepsOperationStateSeparate()
    {
        var model = new ShellViewModel();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);

        Assert.True(model.Navigate(ShellStage.Inspect));

        Assert.Equal("Inspect", model.StageHeading);
        Assert.Equal(new[]
        {
            nameof(model.SelectedStage), nameof(model.StageHeading), nameof(model.StageSummary),
            nameof(model.RequirementsText), nameof(model.EvidenceText), nameof(model.PlanText), nameof(model.StatusText),
        }, notifications);
        Assert.Equal(ShellActivity.Idle, model.Activity);
        notifications.Clear();
        Assert.True(model.Navigate(ShellStage.Inspect));
        Assert.Empty(notifications);
        Assert.True(model.Navigate(ShellStage.Intake));
        Assert.Equal("Intake", model.StageHeading);
    }

    [Fact]
    public void ProgressAndCancellationDoNotClaimReadinessOrStopBeforeConfirmation()
    {
        var model = new ShellViewModel();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);

        model.SetActivity(ShellActivity.Running);
        Assert.True(model.IsIndeterminate);
        Assert.True(model.IsBusy);
        Assert.Contains("Progress is not yet available", model.ActivityText, StringComparison.Ordinal);

        model.SetActivity(ShellActivity.Running, 2, 16);
        Assert.False(model.IsIndeterminate);
        Assert.Equal(2, model.ProgressValue);
        Assert.Equal(16, model.ProgressMaximum);
        Assert.Contains("2 of 16", model.ActivityText, StringComparison.Ordinal);
        Assert.Contains(nameof(model.ActivityText), notifications);

        model.SetActivity(ShellActivity.Cancelling, 2, 16);
        Assert.True(model.IsBusy);
        Assert.Contains("Waiting", model.ActivityText, StringComparison.Ordinal);

        model.SetActivity(ShellActivity.Cancelled, 2, 16);
        Assert.False(model.IsBusy);
        Assert.False(model.IsIndeterminate);
        Assert.Contains("partial evidence", model.ActivityText, StringComparison.Ordinal);
        Assert.Equal(2, model.ProgressValue);
        Assert.Contains("unknown", model.StatusText, StringComparison.Ordinal);
        Assert.Contains("Not available", model.EvidenceText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ShellActivity.Completed, "Device readiness has not been verified")]
    [InlineData(ShellActivity.Failed, "Review the evidence before retrying")]
    [InlineData(ShellActivity.RequiresReview, "Human review required")]
    public void TerminalActivityExplainsOutcomeWithoutPromotingEvidence(ShellActivity activity, string expected)
    {
        var model = new ShellViewModel();
        model.SetActivity(activity, 16, 16);

        Assert.False(model.IsBusy);
        Assert.False(model.IsIndeterminate);
        Assert.Contains(expected, model.ActivityText, StringComparison.Ordinal);
        Assert.Contains("unknown", model.StatusText, StringComparison.Ordinal);
        Assert.Equal(ShellStage.Intake, model.SelectedStage);
        Assert.False(model.Navigate(ShellStage.Prepare));
    }

    [Theory]
    [InlineData((ShellActivity)(-1), 0, 0)]
    [InlineData((ShellActivity)int.MaxValue, 0, 0)]
    [InlineData(ShellActivity.Running, -1, 1)]
    [InlineData(ShellActivity.Running, 0, -1)]
    [InlineData(ShellActivity.Running, 0, 17)]
    [InlineData(ShellActivity.Running, 2, 1)]
    [InlineData(ShellActivity.Running, 1, 0)]
    [InlineData(ShellActivity.Idle, 0, 1)]
    [InlineData(ShellActivity.Completed, 1, 2)]
    public void InvalidActivityCannotChangePresentation(ShellActivity activity, int completed, int total)
    {
        var model = new ShellViewModel();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);

        Assert.ThrowsAny<ArgumentException>(() => model.SetActivity(activity, completed, total));

        Assert.Equal(ShellActivity.Idle, model.Activity);
        Assert.Equal(0, model.ProgressValue);
        Assert.Equal(1, model.ProgressMaximum);
        Assert.Empty(notifications);
    }

    [Fact]
    public void RepeatedActivityDoesNotRepeatAnnouncementsAndRecoveryCanReturnToIdle()
    {
        var model = new ShellViewModel();
        model.SetActivity(ShellActivity.Failed, 1, 2);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);

        model.SetActivity(ShellActivity.Failed, 1, 2);
        Assert.Empty(notifications);

        model.SetActivity(ShellActivity.Idle);
        Assert.Equal("No operation is running.", model.ActivityText);
        Assert.Equal(0, model.ProgressValue);
        Assert.Equal(1, model.ProgressMaximum);
        Assert.Contains(nameof(model.ActivityText), notifications);
        Assert.Contains("unknown", model.StatusText, StringComparison.Ordinal);
    }
}
