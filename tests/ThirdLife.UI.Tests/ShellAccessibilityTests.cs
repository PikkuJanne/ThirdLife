using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class ShellAccessibilityTests
{
    private static readonly string[] AvailableStageButtons = ["IntakeButton", "InspectButton"];
    private static readonly string[] LaterStageCriteria = ["DecideCriteria", "PrepareCriteria", "VerifyCriteria", "HandoverCriteria"];
    private static readonly string[] LongTextTargets = ["RequirementsText", "EvidenceText", "PlanText", "PrepareCriteria"];

    [Fact]
    public void AutomationNavigationChangesReviewAndExposesCurrentStageWithoutLosingFocus()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                ShellTestHost.Show(window);
                var intake = ShellTestHost.Element<Button>(window, "IntakeButton");
                var inspect = ShellTestHost.Element<Button>(window, "InspectButton");
                Assert.Same(intake, FocusManager.GetFocusedElement(window));
                Assert.True(inspect.Focus());
                ShellTestHost.Invoke(window, "InspectButton");
                Assert.Same(inspect, Keyboard.FocusedElement);
                Assert.Equal("Inspect", ShellTestHost.Element<TextBlock>(window, "StageHeading").Text);
                Assert.Contains("no assessment has been run", AutomationProperties.GetHelpText(ShellTestHost.Element<GroupBox>(window, "StageReview")), StringComparison.Ordinal);
                Assert.Equal("Current review stage", ShellTestHost.ButtonPeer(inspect).GetItemStatus());
                Assert.Equal("Available for review", ShellTestHost.ButtonPeer(intake).GetItemStatus());
                var status = ShellTestHost.Element<TextBlock>(window, "StatusAnnouncement");
                var peer = new TextBlockAutomationPeer(status);
                Assert.Equal(status.Text, peer.GetName());
                Assert.Contains("Inspect selected", peer.GetName(), StringComparison.Ordinal);
                Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
                ShellTestHost.Invoke(window, "IntakeButton");
                Assert.Equal("Intake", ShellTestHost.Element<TextBlock>(window, "StageHeading").Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void KeyboardTraversalReachesReviewRegionsAndSkipsUnavailableStages()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                ShellTestHost.Show(window);
                Assert.True(ShellTestHost.Element<Button>(window, "SkipToReviewButton").Focus());
                NextIs(window, "WorkflowStages");
                NextIs(window, "IntakeButton");
                NextIs(window, "InspectButton");
                NextIs(window, "StageReview");
                NextIs(window, "RequirementsRegion");
                NextIs(window, "EvidenceRegion");
                NextIs(window, "PlanRegion");
                MoveNext(window);
                Assert.True(ShellTestHost.Element<Expander>(window, "EvidenceDetails").IsKeyboardFocusWithin);
                NextIs(window, "ActivityRegion");
                NextIs(window, "BatterySafetyRegion");
                NextIs(window, "ReturnToStagesButton");
                NextIs(window, "SkipToReviewButton");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void SkipAndReturnRestoreContextForBothReviewStages()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 480, Height = 320 };
            try
            {
                ShellTestHost.Show(window);
                foreach (var stageButton in AvailableStageButtons)
                {
                    ShellTestHost.Invoke(window, stageButton);
                    ShellTestHost.Invoke(window, "SkipToReviewButton");
                    Assert.Same(window.FindName("StageReview"), Keyboard.FocusedElement);
                    ShellTestHost.Invoke(window, "ReturnToStagesButton");
                    Assert.Same(window.FindName(stageButton), Keyboard.FocusedElement);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void KeyboardPagingCanExposeUnavailableStageCriteriaInSmallViewport()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 480, Height = 320 };
            try
            {
                ShellTestHost.Show(window);
                var region = ShellTestHost.Element<GroupBox>(window, "WorkflowStages");
                var scroll = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                Assert.True(region.Focus());
                scroll.ScrollToTop();
                ShellTestHost.Drain(window);
                var source = PresentationSource.FromVisual(region);
                Assert.NotNull(source);
                var visibleCriteria = new HashSet<string>(StringComparer.Ordinal);
                var coveredText = new Dictionary<string, double>(StringComparer.Ordinal);
                for (var page = 0; page < 12 && visibleCriteria.Count < 4; page++)
                {
                    foreach (var name in LaterStageCriteria)
                    {
                        var criteria = ShellTestHost.Element<TextBlock>(window, name);
                        var bounds = criteria.TransformToAncestor(scroll).TransformBounds(new Rect(criteria.RenderSize));
                        var start = Math.Max(0, scroll.Padding.Top - bounds.Top);
                        var end = Math.Min(bounds.Height, scroll.ActualHeight - scroll.Padding.Bottom - bounds.Top);
                        if (end > start)
                        {
                            coveredText.TryGetValue(name, out var previousEnd);
                            Assert.True(start <= previousEnd + 1, "Keyboard paging skipped part of the criteria text.");
                            coveredText[name] = Math.Max(previousEnd, end);
                            if (end >= bounds.Height - 1) { visibleCriteria.Add(name); }
                        }
                    }
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.PageDown)
                    { RoutedEvent = Keyboard.KeyDownEvent };
                    region.RaiseEvent(args);
                    ShellTestHost.Drain(window);
                }
                Assert.Equal(4, visibleCriteria.Count);
                Assert.True(scroll.VerticalOffset > 0);
                var previousOffset = scroll.VerticalOffset;
                region.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.PageUp)
                { RoutedEvent = Keyboard.KeyDownEvent });
                ShellTestHost.Drain(window);
                Assert.True(scroll.VerticalOffset < previousOffset);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Decide")]
    [InlineData("Prepare")]
    [InlineData("Verify")]
    [InlineData("Handover")]
    public void UnavailableStagesRejectAutomationAndKeepRequirementsExposed(string stage)
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                ShellTestHost.Show(window);
                var button = ShellTestHost.Element<Button>(window, stage + "Button");
                var peer = ShellTestHost.ButtonPeer(button);
                var criteria = ShellTestHost.Element<TextBlock>(window, stage + "Criteria");
                Assert.Contains("unavailable", peer.GetName(), StringComparison.Ordinal);
                Assert.False(peer.IsEnabled());
                Assert.False(peer.IsKeyboardFocusable());
                var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
                Assert.Throws<ElementNotEnabledException>(invoke.Invoke);
                Assert.True(criteria.IsVisible);
                Assert.Contains("Required:", new TextBlockAutomationPeer(criteria).GetName(), StringComparison.Ordinal);
                Assert.True(criteria.ActualHeight > 0);
                Assert.Equal("Intake", ShellTestHost.Element<TextBlock>(window, "StageHeading").Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ActivityFixturesUpdateLiveTextProgressAndRecoveryWithoutClaimingReadiness()
    {
        ShellTestHost.Run(() =>
        {
            var model = new ShellViewModel();
            var window = new MainWindow(model);
            try
            {
                ShellTestHost.Show(window);
                var text = ShellTestHost.Element<TextBlock>(window, "ActivityAnnouncement");
                var progress = ShellTestHost.Element<ProgressBar>(window, "ActivityProgress");
                var peer = new TextBlockAutomationPeer(text);
                Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
                Assert.Equal("No operation is running.", peer.GetName());
                Assert.Equal(Visibility.Collapsed, progress.Visibility);
                model.SetActivity(ShellActivity.Running);
                ShellTestHost.Drain(window);
                Assert.True(progress.IsIndeterminate);
                Assert.True(progress.IsVisible);
                Assert.Contains("not yet available", peer.GetName(), StringComparison.Ordinal);
                model.SetActivity(ShellActivity.Running, 2, 4);
                ShellTestHost.Drain(window);
                Assert.False(progress.IsIndeterminate);
                var progressPeer = Assert.IsType<ProgressBarAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(progress));
                var range = Assert.IsAssignableFrom<IRangeValueProvider>(progressPeer.GetPattern(PatternInterface.RangeValue));
                Assert.Equal(2, range.Value);
                Assert.Equal(4, range.Maximum);
                Assert.True(range.IsReadOnly);
                Assert.Contains("2 of 4", peer.GetName(), StringComparison.Ordinal);
                foreach (var fixture in new[]
                {
                    (ShellActivity.Cancelling, "Waiting", Visibility.Visible),
                    (ShellActivity.Cancelled, "partial evidence", Visibility.Collapsed),
                    (ShellActivity.Completed, "readiness has not been verified", Visibility.Collapsed),
                    (ShellActivity.Failed, "before retrying", Visibility.Collapsed),
                    (ShellActivity.RequiresReview, "Human review required", Visibility.Collapsed),
                })
                {
                    model.SetActivity(fixture.Item1, 4, 4);
                    ShellTestHost.Drain(window);
                    Assert.Equal(fixture.Item3, progress.Visibility);
                    Assert.Contains(fixture.Item2, peer.GetName(), StringComparison.Ordinal);
                }
                Assert.Contains("unknown", ShellTestHost.Element<TextBlock>(window, "StatusAnnouncement").Text, StringComparison.Ordinal);
            }
            finally { window.Close(); }
            model.SetActivity(ShellActivity.Idle);
            ShellTestHost.Drain(window);
        });
    }

    [Fact]
    public void LargeTextAtDoubleScaleReflowsColumnsAndKeepsFinalControlReachable()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                ShellTestHost.Show(window);
                var review = ShellTestHost.Element<StackPanel>(window, "ReviewContent");
                Assert.Equal(1, Grid.GetColumn(review));
                Assert.Equal(0, Grid.GetRow(review));
                window.Width = 480;
                window.Height = 320;
                ShellTestHost.Element<StackPanel>(window, "ShellContent").LayoutTransform = new ScaleTransform(2, 2);
                foreach (var name in LongTextTargets)
                {
                    var text = ShellTestHost.Element<TextBlock>(window, name);
                    text.Text = string.Join(' ', Enumerable.Repeat(text.Text, 4));
                    text.FontSize = 22;
                }
                ShellTestHost.Drain(window);
                var scroll = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                Assert.Equal(0, Grid.GetColumn(review));
                Assert.Equal(1, Grid.GetRow(review));
                Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
                Assert.InRange(scroll.ExtentWidth, 0, scroll.ViewportWidth + 1);
                Assert.Equal(0, scroll.ScrollableWidth);
                scroll.ScrollToBottom();
                ShellTestHost.Drain(window);
                Assert.InRange(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight), 0, 1);
                var returnButton = ShellTestHost.Element<Button>(window, "ReturnToStagesButton");
                var bounds = returnButton.TransformToAncestor(scroll).TransformBounds(new Rect(returnButton.RenderSize));
                Assert.InRange(bounds.Left, 0, scroll.ActualWidth);
                Assert.InRange(bounds.Right, 0, scroll.ActualWidth);
                Assert.InRange(bounds.Bottom, 0, scroll.ActualHeight);
                Assert.True(returnButton.Focus());
                ShellTestHost.Invoke(window, "ReturnToStagesButton");
                Assert.Same(window.FindName("IntakeButton"), Keyboard.FocusedElement);
            }
            finally { window.Close(); }
        });
    }

    private static void NextIs(Window window, string elementName)
    {
        MoveNext(window);
        Assert.Same(window.FindName(elementName), Keyboard.FocusedElement);
    }

    private static void MoveNext(Window window)
    {
        var focused = Assert.IsAssignableFrom<FrameworkElement>(Keyboard.FocusedElement);
        Assert.True(focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
        ShellTestHost.Drain(window);
    }
}
