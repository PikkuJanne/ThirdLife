using System.Windows.Automation;
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
}
