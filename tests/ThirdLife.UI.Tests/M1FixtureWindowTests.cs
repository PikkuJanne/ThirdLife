using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class M1FixtureWindowTests
{
    private static readonly TimeSpan WindowLimit = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ExternalAuditJourneyPersistsItsReport()
    {
        var scenarioId = Environment.GetEnvironmentVariable("THIRDLIFE_M1_JOURNEY_ID");
        Assert.True(Guid.TryParseExact(scenarioId, "N", out _), "Use eng/run-m1-audit-journey.ps1 to drive the fixture.");
        Assert.True(Environment.UserInteractive, "The fixture requires an interactive Windows desktop.");
        Assert.Equal(StartupAccess.StandardUser, StartupSafety.CheckCurrentProcess());

        using var fixture = new M1AuditFixture();
        M1AuditJourneyTests.JourneyResult? result = null;
        ShellTestHost.Run(() =>
        {
            var workspace = fixture.CreateWorkspace();
            var window = fixture.CreateWindow(workspace, prefillInputs: true);
            window.Width = 920;
            window.Height = 550;
            AutomationProperties.SetAutomationId(window, scenarioId!);
            if (Environment.GetEnvironmentVariable("THIRDLIFE_M1_WHEEL_TRACE") == "1")
            {
                ObserveWheelDelivery(window);
            }
            var expired = false;
            var timer = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher) { Interval = WindowLimit };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                expired = true;
                window.Close();
            };
            window.ContentRendered += ActivateOnFirstRender;

            timer.Start();
            try
            {
                window.ShowDialog();
            }
            finally
            {
                timer.Stop();
            }

            Assert.False(expired, "The fixture driver did not finish before the window time limit.");
            var job = Assert.IsType<StoredJob>(workspace.Job);
            var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
            M1AuditJourneyTests.AssertFixtureAssessment(assessment, workspace.Findings);
            var report = Assert.IsType<WorkshopReportArtifact>(workspace.ReportArtifact);
            Assert.Equal(job.Job.JobId, report.JobId);
            Assert.NotEmpty(workspace.ReportPreview);
            result = new M1AuditJourneyTests.JourneyResult(
                job.Job.JobId, assessment.AssessmentId, workspace.ReportPreview, report.FileName);

            void ActivateOnFirstRender(object? sender, EventArgs args)
            {
                window.ContentRendered -= ActivateOnFirstRender;
                window.Activate();
            }
        }, WindowLimit + TimeSpan.FromSeconds(10));

        await M1AuditJourneyTests.AssertDurableFixtureResultAsync(fixture,
            Assert.IsType<M1AuditJourneyTests.JourneyResult>(result));
    }

    private static void ObserveWheelDelivery(Window window)
    {
        const int mouseWheelMessage = 0x020A;
        var nativeMessages = 0;
        var routedEvents = 0;
        var hasRoutedMove = false;
        PublishDelivery();
        window.SourceInitialized += (_, _) =>
        {
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(window));
            source.AddHook((nint handle, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == mouseWheelMessage)
                {
                    nativeMessages++;
                    PublishDelivery();
                }

                return 0;
            });
        };
        window.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) =>
        {
            routedEvents++;
            PublishDelivery();
        }), handledEventsToo: true);

        window.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, _) =>
        {
            if (!hasRoutedMove)
            {
                hasRoutedMove = true;
                PublishDelivery();
            }
        }), handledEventsToo: true);

        void PublishDelivery() =>
            AutomationProperties.SetItemStatus(window, $"native={nativeMessages} routed={routedEvents} pointer_ready={hasRoutedMove}");
    }
}
