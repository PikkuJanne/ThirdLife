using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class BatteryInspectionTests
{
    [Fact]
    public void CompiledNoticeExposesItsSafetyMeaningToKeyboardAndAutomation()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow();
            try
            {
                var scroll = Assert.IsType<ScrollViewer>(window.Content);
                var region = Assert.IsType<GroupBox>(window.FindName("BatterySafetyRegion"));
                var message = Assert.IsType<TextBlock>(window.FindName("BatteryInspectionMessage"));
                var heading = Assert.IsType<TextBlock>(window.FindName("BatteryInspectionHeading"));
                ShowAndWaitForLoadedBindings(window);
                Assert.True(scroll.IsLoaded);
                Assert.Equal("Battery reports cannot detect swelling. Swelling requires human inspection.", message.Text);
                Assert.True(region.Focusable);
                Assert.True(KeyboardNavigation.GetIsTabStop(region));
                Assert.True(region.Focus());
                region.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.Same(region, Keyboard.FocusedElement);
                Assert.Equal(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(heading));
                var peer = Assert.IsType<GroupBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(region));
                Assert.Equal("Battery safety information", peer.GetName());
                Assert.Equal(message.Text, peer.GetHelpText());
                Assert.True(peer.IsKeyboardFocusable());
                Assert.True(peer.HasKeyboardFocus());
                var textPeer = new TextBlockAutomationPeer(message);
                Assert.Equal(message.Text, textPeer.GetName());
                Assert.Equal(AutomationControlType.Text, textPeer.GetAutomationControlType());
                Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
                Assert.Equal(TextTrimming.None, message.TextTrimming);
                Assert.True(DependencyPropertyHelper.GetValueSource(window, Control.BackgroundProperty).IsExpression);
                Assert.True(DependencyPropertyHelper.GetValueSource(window, Control.ForegroundProperty).IsExpression);
                Assert.Equal(SystemColors.WindowBrush, window.Background);
                Assert.Equal(SystemColors.WindowTextBrush, window.Foreground);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EnlargedLongNoticeWrapsAndRemainsReachableInSmallViewport()
    {
        RunOnSta(() =>
        {
            var window = new MainWindow { Width = 480, Height = 320 };
            try
            {
                var scroll = Assert.IsType<ScrollViewer>(window.Content);
                var content = Assert.IsType<StackPanel>(scroll.Content);
                var message = Assert.IsType<TextBlock>(window.FindName("BatteryInspectionMessage"));
                message.Text = string.Join(' ', Enumerable.Repeat(message.Text, 8));
                message.FontSize = 24;
                content.LayoutTransform = new ScaleTransform(2, 2);
                ShellTestHost.Show(window);

                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
                Assert.True(message.ActualHeight > message.FontSize);
                Assert.InRange(scroll.ExtentWidth, 0, scroll.ViewportWidth + 1);
                scroll.ScrollToBottom();
                scroll.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Background);
                scroll.UpdateLayout();
                Assert.True(scroll.VerticalOffset > 0);
                Assert.InRange(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight), 0, 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void ShowAndWaitForLoadedBindings(Window window)
    {
        var frame = new DispatcherFrame();
        var loadedBindingsReady = false;
        var timeout = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        DispatcherOperation? loadedOperation = null;
        RoutedEventHandler loaded = (_, _) => loadedOperation = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                loadedBindingsReady = true;
                frame.Continue = false;
            }));
        EventHandler timedOut = (_, _) => frame.Continue = false;
        window.Loaded += loaded;
        timeout.Tick += timedOut;
        try
        {
            timeout.Start();
            window.Show();
            Dispatcher.PushFrame(frame);
            Assert.True(loadedBindingsReady && window.IsLoaded,
                "The window did not finish loading its bindings within the bounded dispatcher wait.");
        }
        finally
        {
            timeout.Stop();
            timeout.Tick -= timedOut;
            window.Loaded -= loaded;
            loadedOperation?.Abort();
        }
    }

    private static void RunOnSta(Action check)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                check();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The bounded STA component check did not finish.");
        failure?.Throw();
    }
}
