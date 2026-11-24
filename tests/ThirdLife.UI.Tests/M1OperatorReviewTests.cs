using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using ThirdLife.UI.Shell;
using Xunit.Abstractions;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class M1OperatorReviewTests(ITestOutputHelper output)
{
    private static readonly TimeSpan WindowLimit = TimeSpan.FromMinutes(30);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void OpenPreparedFixtureForOperatorReview()
    {
        Assert.True(string.Equals(
            Environment.GetEnvironmentVariable("THIRDLIFE_M1_OPERATOR_REVIEW"),
            "1", StringComparison.Ordinal), "The interactive fixture requires an explicit operator-review invocation.");
        Assert.True(Environment.UserInteractive, "The fixture requires an interactive Windows desktop.");
        Assert.Equal(StartupAccess.StandardUser, StartupSafety.CheckCurrentProcess());

        var elapsed = TimeSpan.Zero;
        ShellTestHost.Run(() =>
        {
            using var fixture = new M1AuditFixture();
            var workspace = fixture.CreateWorkspace();
            var window = fixture.CreateWindow(workspace, prefillInputs: true);
            var expired = false;
            var timer = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher) { Interval = WindowLimit };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                expired = true;
                window.Close();
            };
            window.ContentRendered += ActivateOnFirstRender;

            var clock = Stopwatch.StartNew();
            timer.Start();
            try
            {
                window.ShowDialog();
            }
            finally
            {
                timer.Stop();
                elapsed = clock.Elapsed;
            }

            Assert.False(expired, "The interactive window reached its time limit before it was closed.");

            void ActivateOnFirstRender(object? sender, EventArgs args)
            {
                window.ContentRendered -= ActivateOnFirstRender;
                window.Activate();
            }
        }, WindowLimit + TimeSpan.FromMinutes(1));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "m1-operator-fixture-window",
            elapsedMilliseconds = (long)elapsed.TotalMilliseconds,
        }));
    }
}
