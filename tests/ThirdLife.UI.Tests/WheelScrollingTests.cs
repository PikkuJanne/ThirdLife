using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThirdLife.UI.Features.ManualTests;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class WheelScrollingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FractionalWheelPulsesMoveTheShellByACombinedDetent(bool drainBetweenPulses)
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace()) { Width = 500, Height = 320 };
            try
            {
                ShellTestHost.Show(window);
                var scroller = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                var source = ShellTestHost.Element<ListBoxItem>(window, "IntakeStage");

                Reset(window, scroller);
                SendWheel(source, -120);
                ShellTestHost.Drain(window);
                var fullStep = scroller.VerticalOffset;
                double minimum = SystemParameters.WheelScrollLines == 0 ? 0 : 1;
                Assert.InRange(fullStep, minimum, 48d);

                Reset(window, scroller);
                for (int pulse = 0; pulse < 6; pulse++)
                {
                    SendWheel(source, -20);
                    if (drainBetweenPulses)
                    {
                        ShellTestHost.Drain(window);
                    }
                }

                ShellTestHost.Drain(window);
                Assert.InRange(scroller.VerticalOffset, fullStep - 0.5, fullStep + 0.5);

                Reset(window, scroller);
                SendWheel(source, int.MinValue);
                ShellTestHost.Drain(window);
                Assert.InRange(scroller.VerticalOffset, minimum, 48d);
                SendWheel(source, int.MaxValue);
                ShellTestHost.Drain(window);
                Assert.Equal(0d, scroller.VerticalOffset);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task WheelOverAnUnfocusedIntakeChoiceScrollsTheShellProportionally()
    {
        using var fixture = new M1AuditFixture();
        var workspace = fixture.CreateWorkspace();
        await workspace.CreateJobAsync();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 500, Height = 320 };
            try
            {
                var auditView = ShellTestHost.Element<AuditWorkspaceView>(window, "AuditView");
                var choice = Assert.IsType<ComboBox>(auditView.FindName("SanitizationResult"));
                choice.SelectedIndex = 1;
                ShellTestHost.Show(window);
                choice.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.False(choice.IsKeyboardFocusWithin);
                var outer = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                var before = outer.VerticalOffset;

                SendWheel(choice, -20);
                ShellTestHost.Drain(window);

                double minimum = SystemParameters.WheelScrollLines == 0 ? 0 : 0.5;
                Assert.InRange(outer.VerticalOffset - before, minimum, 8.5d);
                Assert.Equal(1, choice.SelectedIndex);

                Assert.True(choice.Focus());
                ShellTestHost.Drain(window);
                before = outer.VerticalOffset;

                SendWheel(choice, -20);
                ShellTestHost.Drain(window);

                Assert.Equal(before, outer.VerticalOffset);
                Assert.Equal(2, choice.SelectedIndex);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public void WheelOverEvidenceMovesOnlyThePixelScrolledList()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace()) { Width = 500, Height = 320 };
            try
            {
                var auditView = ShellTestHost.Element<AuditWorkspaceView>(window, "AuditView");
                var list = Assert.IsType<ListBox>(auditView.FindName("AssessmentObservations"));
                list.ItemsSource = Enumerable.Range(0, 60).Select(index => new AssessmentFinding(
                    $"finding-{index}", "A bounded example finding", "Synthetic fixture", "Example provider",
                    $"Finding {index}", "Fixture time", "Fixture evidence")).ToArray();
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Inspect);
                list.BringIntoView();
                ShellTestHost.Drain(window);
                var outer = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                var inner = FindVisualDescendant<ScrollViewer>(list);
                var source = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                var outerBefore = outer.VerticalOffset;

                SendWheel(source, -20);
                ShellTestHost.Drain(window);
                double minimum = SystemParameters.WheelScrollLines == 0 ? 0 : 0.5;
                Assert.InRange(inner.VerticalOffset, minimum, 8.5d);
                Assert.Equal(outerBefore, outer.VerticalOffset);
                Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));

                inner.ScrollToBottom();
                ShellTestHost.Drain(window);
                var innerAtBottom = inner.VerticalOffset;
                outerBefore = outer.VerticalOffset;
                Assert.True(outer.ScrollableHeight - outerBefore >= 8);

                SendWheel(inner, -20);
                ShellTestHost.Drain(window);
                Assert.Equal(innerAtBottom, inner.VerticalOffset);
                Assert.InRange(outer.VerticalOffset - outerBefore, minimum, 8.5d);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task WheelOverReportMovesItsReadingPaneWithinOnePulse()
    {
        using var fixture = new M1AuditFixture();
        await using var workspace = fixture.CreateWorkspace();
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.StartAssessmentAsync();
        await workspace.GenerateReportAsync();
        await workspace.OpenReportAsync();
        Assert.NotEmpty(workspace.ReportPreview);
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 500, Height = 320 };
            try
            {
                var auditView = ShellTestHost.Element<AuditWorkspaceView>(window, "AuditView");
                var preview = Assert.IsType<TextBox>(auditView.FindName("WorkshopReportPreview"));
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Handover);
                ShellTestHost.Drain(window);
                preview.BringIntoView();
                ShellTestHost.Drain(window);
                var outer = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                var inner = FindVisualDescendant<ScrollViewer>(preview);
                Assert.Equal(workspace.ReportPreview, preview.Text);
                Assert.True(inner.ScrollableHeight >= 8.5d);
                var outerBefore = outer.VerticalOffset;

                SendWheel(inner, -20);
                ShellTestHost.Drain(window);
                double minimum = SystemParameters.WheelScrollLines == 0 ? 0 : 0.5;
                Assert.InRange(inner.VerticalOffset, minimum, 8.5d);
                Assert.Equal(outerBefore, outer.VerticalOffset);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    private static void Reset(Window window, ScrollViewer scroller)
    {
        scroller.ScrollToTop();
        ShellTestHost.Drain(window);
        Assert.Equal(0d, scroller.VerticalOffset);
    }

    private static void SendWheel(UIElement source, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(preview);
        if (!preview.Handled)
        {
            source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.MouseWheelEvent });
        }
    }

    private static T FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject =>
        FindVisualDescendantOrNull<T>(root) ?? throw new InvalidOperationException("Expected visual descendant.");

    private static T? FindVisualDescendantOrNull<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var found = child as T ?? FindVisualDescendantOrNull<T>(child);
            if (found is not null) { return found; }
        }

        return null;
    }
}
