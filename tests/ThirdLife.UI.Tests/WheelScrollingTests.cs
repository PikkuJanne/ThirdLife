using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThirdLife.UI.Features.ManualTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class WheelScrollingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FractionalWheelPulsesMoveTheShellByACombinedDetent(bool drainBetweenPulses)
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 500, Height = 320 };
            try
            {
                ShellTestHost.Show(window);
                var scroller = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                var source = ShellTestHost.Element<TextBlock>(window, "StageHeading");

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
                window.Close();
            }
        });
    }

    [Fact]
    public void WheelOverAnUnfocusedIntakeChoiceScrollsTheShellProportionally()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 500, Height = 320 };
            try
            {
                var auditView = ShellTestHost.Element<AuditWorkspaceView>(window, "AuditView");
                auditView.Visibility = Visibility.Visible;
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
                window.Close();
            }
        });
    }

    [Fact]
    public void WheelOverEvidenceMovesOnlyThePixelScrolledList()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 500, Height = 320 };
            try
            {
                var auditView = ShellTestHost.Element<AuditWorkspaceView>(window, "AuditView");
                auditView.Visibility = Visibility.Visible;
                var list = Assert.IsType<ListBox>(auditView.FindName("AssessmentObservations"));
                list.ItemsSource = Enumerable.Range(0, 60).Select(index => new
                {
                    Name = $"Finding {index}",
                    Value = "A bounded example finding",
                    Evidence = "Synthetic fixture",
                    Provider = "Example provider",
                }).ToArray();
                Assert.IsType<Expander>(list.Parent).IsExpanded = true;
                ShellTestHost.Show(window);
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
                window.Close();
            }
        });
    }

    [Fact]
    public void WheelOverReportMovesItsReadingPaneWithinOnePulse()
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 500, Height = 320 };
            try
            {
                var auditView = ShellTestHost.Element<AuditWorkspaceView>(window, "AuditView");
                auditView.Visibility = Visibility.Visible;
                var preview = Assert.IsType<TextBox>(auditView.FindName("WorkshopReportPreview"));
                preview.Visibility = Visibility.Visible;
                preview.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Synthetic report line for scroll testing.", 80));
                ShellTestHost.Show(window);
                preview.BringIntoView();
                ShellTestHost.Drain(window);
                var outer = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
                var inner = FindVisualDescendant<ScrollViewer>(preview);
                var outerBefore = outer.VerticalOffset;

                SendWheel(inner, -20);
                ShellTestHost.Drain(window);
                double minimum = SystemParameters.WheelScrollLines == 0 ? 0 : 0.5;
                Assert.InRange(inner.VerticalOffset, minimum, 8.5d);
                Assert.Equal(outerBefore, outer.VerticalOffset);
            }
            finally
            {
                window.Close();
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
