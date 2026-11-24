using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ThirdLife.UI.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShellWindowChecks
{
    public const string Name = "Loaded shell windows";
}

internal static class ShellTestHost
{
    public static void Run(Action check, TimeSpan? timeout = null)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var previousMode = RenderOptions.ProcessRenderMode;
            try
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                check();
            }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            finally
            {
                RenderOptions.ProcessRenderMode = previousMode;
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(timeout ?? TimeSpan.FromSeconds(20)), "The bounded STA shell check did not finish.");
        failure?.Throw();
    }

    public static void Show(Window window)
    {
        var frame = new DispatcherFrame();
        var loaded = false;
        var timeout = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher)
        { Interval = TimeSpan.FromSeconds(5) };
        DispatcherOperation? ready = null;
        RoutedEventHandler onLoaded = (_, _) => ready = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle, new Action(() => { loaded = true; frame.Continue = false; }));
        EventHandler onTimeout = (_, _) => frame.Continue = false;
        window.Loaded += onLoaded;
        timeout.Tick += onTimeout;
        try
        {
            timeout.Start();
            window.Show();
            Dispatcher.PushFrame(frame);
            Assert.True(loaded && window.IsLoaded, "Shell bindings did not load within five seconds.");
        }
        finally
        {
            timeout.Stop();
            timeout.Tick -= onTimeout;
            window.Loaded -= onLoaded;
            ready?.Abort();
        }
    }

    public static void Drain(Window window)
    {
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    public static T Element<T>(Window window, string name) where T : FrameworkElement =>
        Assert.IsType<T>(window.FindName(name));

    public static ButtonAutomationPeer ButtonPeer(Button button) =>
        Assert.IsType<ButtonAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(button));

    public static void Invoke(Window window, string name)
    {
        var peer = ButtonPeer(Element<Button>(window, name));
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        invoke.Invoke();
        Drain(window);
    }
}
