using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using ThirdLife.UI.Shell;
using Xunit.Abstractions;

namespace ThirdLife.UI.Tests;

[Collection("Loaded shell windows")]
public sealed class ShellProcessSmokeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void UnelevatedApphostExposesShellAndDeliversNavigationAnnouncement()
    {
        Assert.Equal(StartupAccess.StandardUser, StartupSafety.CheckCurrentProcess());
        var apphost = Path.Combine(AppContext.BaseDirectory, "ThirdLife.UI.exe");
        var timer = Stopwatch.StartNew();
        using var process = Process.Start(new ProcessStartInfo(apphost)
        {
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        Assert.NotNull(process);
        AutomationElement? window = null;
        AutomationEventHandler? handler = null;
        using var announced = new ManualResetEventSlim();
        try
        {
            Assert.True(process.WaitForInputIdle(10_000), "The owned shell did not reach input idle.");
            Assert.True(SpinWait.SpinUntil(() =>
            {
                process.Refresh();
                return process.MainWindowHandle != IntPtr.Zero || process.HasExited;
            }, TimeSpan.FromSeconds(5)), "The owned shell did not create its window.");
            Assert.False(process.HasExited);
            window = AutomationElement.FromHandle(process.MainWindowHandle);
            var inspect = window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "StageInspect"));
            Assert.NotNull(inspect);
            Assert.True(inspect.Current.IsEnabled);
            handler = (sender, _) =>
            {
                try
                {
                    if (sender is AutomationElement element &&
                        element.Current.Name == "Inspect opened.")
                    { announced.Set(); }
                }
                catch (ElementNotAvailableException) { }
            };
            Automation.AddAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent,
                window, TreeScope.Descendants, handler);
            ((SelectionItemPattern)inspect.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Assert.True(announced.Wait(TimeSpan.FromSeconds(5)),
                "The external UI Automation listener did not receive the changed navigation status.");
            process.Refresh();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"shell_metrics: elapsed_ms={timer.ElapsedMilliseconds}; peak_working_set_bytes={process.PeakWorkingSet64}; cpu_ms={process.TotalProcessorTime.TotalMilliseconds:F3}; source=owned_apphost; gpu=default_wpf; network_requests=none_by_design"));
        }
        finally
        {
            if (handler is not null && window is not null)
            {
                Automation.RemoveAutomationEventHandler(AutomationElementIdentifiers.LiveRegionChangedEvent, window, handler);
            }
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(5_000))
                {
                    process.Kill();
                    Assert.True(process.WaitForExit(5_000), "The owned smoke-test process could not be cleaned up.");
                    Assert.Fail("The shell did not close normally after the smoke test.");
                }
            }
        }
        Assert.Equal(0, process.ExitCode);
    }
}
