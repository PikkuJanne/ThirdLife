using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Packages;
using ThirdLife.Reports.WorkshopReport;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Fact]
    public async Task EssentialSkipSurvivesReopenAndAppearsInVerifyAndWorkshopReport()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: false);
        var service = new PilotPackageService(runtime);
        string jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            jobId = (await PrepareInitialPackagePlanAsync(workspace)).Value;
            workspace.PackageOperatorCode = "fixture-skip-operator";
            await workspace.ApprovePlanReviewAsync();
            Assert.True(workspace.CanInstallPackage);
            var skip = Assert.Single(workspace.ActionSkipChoices);
            await workspace.SkipApprovedActionAsync(skip.ActionId);
            Assert.False(workspace.CanInstallPackage);
            Assert.Equal(0, runtime.InstallCalls);
        }

        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId);
        Assert.Contains("Human review required", reopened.PreparationDispositionStatus, StringComparison.Ordinal);
        Assert.Contains("Document editing (essential)", reopened.PreparationOutcomes, StringComparison.Ordinal);
        Assert.Contains("Skipped by the workshop operator", reopened.PreparationOutcomes, StringComparison.Ordinal);
        Assert.False(reopened.CanInstallPackage);
        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        Assert.Contains("The operator skipped this package action", reopened.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("document_editing (essential)", reopened.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("Workshop operator code fixture-skip-operator", reopened.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("Human review required", reopened.ReportPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("Independent verification confirmed", reopened.ReportPreview, StringComparison.Ordinal);

        InPackageVerificationWindow(reopened, (_, panel) =>
        {
            var status = Named<TextBlock>(panel, "PreparationDispositionMessage");
            Assert.True(status.IsVisible && status.Focusable);
            var statusPeer = new TextBlockAutomationPeer(status);
            Assert.Contains("Human review required", statusPeer.GetName(), StringComparison.Ordinal);
            Assert.Equal(AutomationLiveSetting.Polite, statusPeer.GetLiveSetting());
            var rationale = Named<TextBox>(panel, "PreparationOutcomeDetails");
            Assert.True(rationale.IsVisible && rationale.IsReadOnly && rationale.IsTabStop);
            Assert.Equal("Preparation action outcomes", new TextBoxAutomationPeer(rationale).GetName());
        });

        await using var reassessed = OpenWorkspace(disk, reports, service, new FixedClock { Current = Now.AddMinutes(1) });
        await reassessed.OpenJobAsync(jobId);
        await reassessed.StartAssessmentAsync();
        Assert.Contains("No current preparation disposition", reassessed.PreparationDispositionStatus, StringComparison.Ordinal);
        await reassessed.GenerateReportAsync();
        await reassessed.OpenReportAsync();
        Assert.Contains("Earlier or no longer applicable plan", reassessed.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("document_editing (essential)", reassessed.ReportPreview, StringComparison.Ordinal);
        Assert.Equal(0, runtime.InstallCalls);
    }
}
