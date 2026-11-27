using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using ThirdLife.Verification;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Fact]
    [Trait("Category", "Extended")]
    public async Task OfflineMaintenancePreservesTheApprovedJobAndResumesAfterReopen()
    {
        using var disk = new AuditTestStore();
        var clock = new FixedClock();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var service = new PilotPackageService(runtime);
        var agent = new MaintenanceScanFixture { Status = WindowsUpdateStatus.Offline };
        string jobId;
        MaintenanceCheckpoint approved;

        await using (var workspace = Open())
        {
            await PrepareInitialPackagePlanAsync(workspace);
            jobId = workspace.JobId;
            workspace.IncludeFullMaintenance = true;
            await workspace.BuildPlanReviewAsync();
            workspace.PackageOperatorCode = "fixture-operator";
            await workspace.ApprovePlanReviewAsync();
            approved = await ReadCheckpointAsync(jobId);
            Assert.True(workspace.CanContinueMaintenance, workspace.OperationError);
            await workspace.ContinueMaintenanceAsync();
            Assert.Equal(1, agent.Scans);
            Assert.Contains("network connection", workspace.MaintenancePhaseStatus);
            Assert.False(workspace.CanInstallPackage);
        }

        await using var reopened = Open();
        await reopened.OpenJobAsync(jobId);
        Assert.Contains("network connection", reopened.MaintenancePhaseStatus);
        Assert.False(reopened.CanInstallPackage);
        var deferred = await ReadCheckpointAsync(jobId);
        Assert.Equal(approved.Plan, deferred.Plan);
        Assert.Equal(approved.ApprovalId, deferred.ApprovalId);
        Assert.Equal(0, runtime.InstallCalls);
        Assert.Equal(1, agent.Scans);

        agent.Status = WindowsUpdateStatus.Succeeded;
        await reopened.ContinueMaintenanceAsync();
        Assert.Equal(2, agent.Scans);
        Assert.Contains("Package phase", reopened.MaintenancePhaseStatus);
        var resumed = await ReadCheckpointAsync(jobId);
        Assert.Equal(MaintenancePhase.Packages, resumed.Phase);
        Assert.Equal(approved.Plan, resumed.Plan);
        Assert.Equal(approved.ApprovalId, resumed.ApprovalId);
        Assert.Equal(0, runtime.InstallCalls);

        AuditWorkspace Open() => OpenMaintenanceWorkspace(disk, clock, service, agent, OpenStore);

        Task<SqliteJobStore> OpenStore(CancellationToken token) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000UL,
            readBootSession: () => 7, cancellationToken: token);

        async Task<MaintenanceCheckpoint> ReadCheckpointAsync(string id)
        {
            await using var store = await OpenStore(default);
            return Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(new JobId(id)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullMaintenanceReviewResumesThroughFinalRestartWithoutReinstalling(bool renewApproval)
    {
        using var disk = new AuditTestStore();
        var clock = new FixedClock();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var agent = new MaintenanceScanFixture();
        uint bootSession = 7;
        ColdBootObservation? coldBoot = null;
        var service = new PilotPackageService(runtime);
        var jobId = string.Empty;
        Guid approval;
        await using (var first = Open())
        {
            await first.CreateJobAsync();
            jobId = first.JobId;
            await first.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
                "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
            await first.StartAssessmentAsync();
            first.SelectedPlanProfile = Assert.Single(first.VisiblePlanProfiles);
            await first.BuildPlanReviewAsync();
            var standalone = first.PlanReview!.Plan.ContentSha256;
            Assert.False(first.IncludeFullMaintenance);
            first.IncludeFullMaintenance = true;
            Assert.Null(first.PlanReview);
            await first.BuildPlanReviewAsync();
            Assert.NotEqual(standalone, first.PlanReview!.Plan.ContentSha256);
            Assert.Contains("final check", first.PlanReviewDetails);
            Assert.False(first.HasMaintenancePhaseStatus);
            first.PackageOperatorCode = "fixture-operator";
            await first.ApprovePlanReviewAsync();
            Assert.True(first.CanContinueMaintenance, first.OperationError + first.MaintenancePhaseStatus);
            Assert.False(first.CanInstallPackage);
            await using var saved = await OpenStore(default);
            approval = (await saved.LoadMaintenanceAsync(first.Job!.Job.JobId))!.ApprovalId;
        }

        await using (var reopened = Open())
        {
            await reopened.OpenJobAsync(jobId);
            Assert.Equal(0, agent.Scans);
            Assert.True(reopened.CanContinueMaintenance, reopened.MaintenancePhaseStatus);
            if (renewApproval)
            {
                clock.Current = Now.AddMinutes(11);
                agent.Status = WindowsUpdateStatus.Offline;
                await reopened.ContinueMaintenanceAsync();
                Assert.Contains("network connection", reopened.MaintenancePhaseStatus);
                Assert.False(reopened.CanInstallPackage);
                agent.Status = WindowsUpdateStatus.Succeeded;
            }
            await reopened.ContinueMaintenanceAsync();
            Assert.Contains("Package phase", reopened.MaintenancePhaseStatus);
            reopened.SelectedPlanProfile = Assert.Single(reopened.VisiblePlanProfiles);
            Assert.True(reopened.IncludeFullMaintenance);
            await reopened.BuildPlanReviewAsync();
            if (renewApproval)
            {
                Assert.False(reopened.CanInstallPackage);
                reopened.PackageOperatorCode = "renewing-operator";
                Assert.True(reopened.CanApprovePlanReview);
                await reopened.ApprovePlanReviewAsync();
                await using var saved = await OpenStore(default);
                var renewed = await saved.LoadMaintenanceAsync(reopened.Job!.Job.JobId);
                Assert.Equal(MaintenancePhase.Packages, renewed!.Phase);
                Assert.NotEqual(approval, renewed.ApprovalId);
                approval = renewed.ApprovalId;
            }
            Assert.True(reopened.CanInstallPackage, reopened.PlanReviewStatus + reopened.PackageSourceStatus);
            await reopened.InstallPackageAsync();
            Assert.Equal(1, runtime.InstallCalls);
            await reopened.ContinueMaintenanceAsync();
            Assert.Contains("Post-package checks", reopened.MaintenancePhaseStatus);
            await CheckReportAsync(reopened, "Post-package checks are pending");
            if (renewApproval)
            {
                runtime.RegistrationUnavailable = true;
                await reopened.ContinueMaintenanceAsync();
                Assert.Contains("could not read the installed package version", reopened.MaintenancePhaseStatus);
                reopened.CloseJob();
                await reopened.OpenJobAsync(jobId);
                Assert.Contains("could not read the installed package version", reopened.MaintenancePhaseStatus);
                await CheckReportAsync(reopened, "last installed-package check could not read");
                runtime.RegistrationUnavailable = false;
            }
            await reopened.ContinueMaintenanceAsync();
            Assert.Contains("Final restart required", reopened.MaintenancePhaseStatus);
            await CheckReportAsync(reopened, "final restart checkpoint is saved");
            await reopened.ContinueMaintenanceAsync();
            Assert.Contains("Final restart required", reopened.MaintenancePhaseStatus);
        }

        bootSession++;
        await using var final = Open();
        await final.OpenJobAsync(jobId);
        Assert.Contains("Final restart required", final.MaintenancePhaseStatus);
        const string ExactVersions = "Resolved version 8.9.9.1. Installed version 8.9.9.1.";
        const string DowngradedVersions = "Resolved version 8.9.9.1. Installed version 8.9.9.0.";
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(final);
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Verify);
                ShellTestHost.Drain(window);
                var panel = Named<PackageVerificationView>(Named<FrameworkElement>(window, "AuditView"), "PackageVerificationPanel");
                var advance = Named<Button>(panel, "ContinueMaintenanceButton");
                Assert.True(advance.Focus());
                Invoke(window, advance);
                PumpUntil(window, () => !final.IsBusy);
                var message = Named<TextBlock>(panel, "MaintenancePauseMessage");
                Assert.True(message.IsVisible && message.Focusable);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(message));
                Assert.Contains("awaiting independent final verification", message.Text);
                Assert.False(advance.IsVisible);

                var run = Named<Button>(panel, "RunProfileVerificationButton");
                var status = Named<TextBlock>(panel, "ProfileVerificationMessage");
                var evidence = Named<TextBox>(panel, "ProfileVerificationEvidence");
                var beforeFinalCheck = agent.Scans;
                Assert.True(run.IsVisible && run.IsEnabled && run.Focus());
                Invoke(window, run);
                PumpUntil(window, () => !final.IsBusy && final.HasProfileVerificationDetails,
                    () => final.OperationError + final.ProfileVerificationStatus);
                Assert.Equal(beforeFinalCheck + 1, agent.Scans);
                Assert.True(status.IsVisible && status.Focusable);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
                Assert.Contains("an unmet or unresolved requirement", status.Text);
                Assert.True(evidence.IsVisible && evidence.IsReadOnly && evidence.Focusable);
                Assert.Contains(ExactVersions, evidence.Text);
            }
            finally { CloseWindow(window); }
        });

        await using var reporter = Open();
        await reporter.OpenJobAsync(jobId);
        Assert.Contains(ExactVersions, reporter.ProfileVerificationDetails);
        await CheckReportAsync(reporter, "Review the separate final profile verification results");
        Assert.Contains(ExactVersions, reporter.ReportPreview);
        await using var store = await OpenStore(default);
        var savedFinal = await store.LoadMaintenanceAsync(final.Job!.Job.JobId);
        Assert.Equal(MaintenancePhase.FinalVerification, savedFinal!.Phase);
        Assert.Equal(approval, savedFinal.ApprovalId);
        var beforeChange = await store.LoadProfileVerificationAsync(savedFinal.Plan.JobId);
        Assert.NotNull(beforeChange);
        Assert.True(beforeChange.IsCurrent);
        Assert.Equal(ProfileVerificationState.Passed, Assert.Single(beforeChange.Record.Result.Capabilities).State);

        runtime.RegisteredVersion = "8.9.9.0";
        await reporter.RunProfileVerificationAsync();
        Assert.Contains(DowngradedVersions, reporter.ProfileVerificationDetails);
        Assert.Null(reporter.ReportArtifact);
        await CheckReportAsync(reporter, "Review the separate final profile verification results");
        Assert.Contains(DowngradedVersions, reporter.ReportPreview);
        var changed = await store.LoadProfileVerificationAsync(savedFinal.Plan.JobId);
        Assert.NotNull(changed);
        Assert.True(changed.IsCurrent);
        Assert.Equal(savedFinal, changed.Record.Checkpoint);
        Assert.NotEqual(beforeChange.Record.ScanId, changed.Record.ScanId);
        var changedCapability = Assert.Single(changed.Record.Result.Capabilities);
        Assert.Equal(ProfileVerificationState.Failed, changedCapability.State);
        Assert.Equal("8.9.9.0", changedCapability.ObservedVersion);
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Equal(1, runtime.ProbeCalls);

        clock.Current = clock.Current.AddMinutes(1);
        WithVerifyPanel(reporter, (window, panel) =>
        {
            var save = Named<Button>(panel, "RecordColdBootCheckpointButton");
            var run = Named<Button>(panel, "RunPostBootVerificationButton");
            var status = Named<TextBlock>(panel, "ColdBootVerificationMessage");
            Assert.False(run.IsEnabled);
            Assert.True(save.IsVisible && save.IsEnabled && save.Focus());
            Invoke(window, save);
            PumpUntil(window, () => !reporter.IsBusy && reporter.HasColdBootVerificationDetails,
                () => reporter.OperationError + reporter.ColdBootVerificationStatus);
            Assert.True(status.IsVisible && status.Focusable);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Contains("Checkpoint saved", status.Text);
            Assert.False(run.IsEnabled);
        });

        await using (var sameBoot = Open())
        {
            await sameBoot.OpenJobAsync(jobId);
            Assert.Contains("Checkpoint saved", sameBoot.ColdBootVerificationStatus);
            Assert.False(sameBoot.CanRunPostBootVerification);
        }

        bootSession++;
        clock.Current = clock.Current.AddMinutes(1);
        coldBoot = new(bootSession, clock.Current, ColdBootKind.Restart);
        await using (var afterRestart = Open())
        {
            await afterRestart.OpenJobAsync(jobId);
            Assert.Contains("Windows recorded a restart", afterRestart.ColdBootVerificationStatus);
            Assert.False(afterRestart.CanRunPostBootVerification);
            Assert.Equal(1, runtime.ProbeCalls);
            await afterRestart.RecordColdBootCheckpointAsync();
            Assert.Contains("Checkpoint saved", afterRestart.ColdBootVerificationStatus);
        }

        bootSession++;
        clock.Current = clock.Current.AddMinutes(1);
        coldBoot = new(bootSession, clock.Current, ColdBootKind.OrderlyPowerOff);
        runtime.RegisteredVersion = "8.9.9.1";
        runtime.ProbeStatus = renewApproval ? PackageVerificationStatus.ExitedEarly : PackageVerificationStatus.Passed;
        var probeOutcome = renewApproval ? "Requirement unmet. Blocks post-boot verification." : "Verified.";
        await using var afterColdBoot = Open();
        await afterColdBoot.OpenJobAsync(jobId);
        WithVerifyPanel(afterColdBoot, (window, panel) =>
        {
            var run = Named<Button>(panel, "RunPostBootVerificationButton");
            var beforePostBoot = agent.Scans;
            Assert.True(run.IsVisible && run.IsEnabled && run.Focus());
            Invoke(window, run);
            PumpUntil(window, () => !afterColdBoot.IsBusy && afterColdBoot.ProfileVerificationDetails.Contains("post-boot pass", StringComparison.Ordinal),
                () => afterColdBoot.OperationError + afterColdBoot.ColdBootVerificationStatus);
            Assert.Equal(beforePostBoot + 1, agent.Scans);
            var evidence = Named<TextBox>(panel, "ColdBootVerificationEvidence");
            Assert.True(evidence.IsVisible && evidence.IsReadOnly && evidence.Focusable);
            Assert.Contains("orderly shutdown and full startup", evidence.Text);
            Assert.Contains(ExactVersions, afterColdBoot.ProfileVerificationDetails);
            Assert.Contains($"Post-boot launch probe. {probeOutcome}", afterColdBoot.ProfileVerificationDetails);
        });
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Equal(2, runtime.ProbeCalls);

        await using var afterPostBoot = Open();
        await afterPostBoot.OpenJobAsync(jobId);
        await CheckReportAsync(afterPostBoot, "Review the separate final profile verification results");
        Assert.Contains("Independent post-boot profile verification", afterPostBoot.ReportPreview);
        Assert.Contains($"Post-boot launch probe. {probeOutcome}", afterPostBoot.ReportPreview);
        Assert.Contains("an unmet or unresolved requirement", afterPostBoot.ColdBootVerificationStatus);

        static void WithVerifyPanel(AuditWorkspace workspace, Action<MainWindow, PackageVerificationView> check) => ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace);
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Verify);
                ShellTestHost.Drain(window);
                check(window, Named<PackageVerificationView>(Named<FrameworkElement>(window, "AuditView"), "PackageVerificationPanel"));
            }
            finally { CloseWindow(window); }
        });

        static async Task CheckReportAsync(AuditWorkspace workspace, string phase)
        {
            Assert.Contains(phase, workspace.PreparationDispositionStatus);
            await workspace.GenerateReportAsync();
            await workspace.OpenReportAsync();
            Assert.Contains(phase, workspace.ReportPreview);
            Assert.Contains("The package action requirements have been met", workspace.ReportPreview);
        }

        Task<SqliteJobStore> OpenStore(CancellationToken token) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000UL,
            readBootSession: () => bootSession, readColdBoot: (_, _) => coldBoot, cancellationToken: token);

        AuditWorkspace Open() => OpenMaintenanceWorkspace(disk, clock, service, agent, OpenStore);
    }

    [Fact]
    public async Task MaintenanceContinuationRereadsAssessmentChangedInAnotherWorkspaceDuringScan()
    {
        using var disk = new AuditTestStore();
        var clock = new FixedClock();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var agent = new MaintenanceScanFixture();
        var service = new PilotPackageService(runtime);
        Task<SqliteJobStore> OpenStore(CancellationToken token) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000UL,
            readBootSession: () => 7, cancellationToken: token);
        await using var workspace = OpenMaintenanceWorkspace(disk, clock, service, agent, OpenStore);
        var jobId = await PrepareInitialPackagePlanAsync(workspace);
        workspace.IncludeFullMaintenance = true;
        await workspace.BuildPlanReviewAsync();
        workspace.PackageOperatorCode = "fixture-operator";
        await workspace.ApprovePlanReviewAsync();
        Assert.True(workspace.CanContinueMaintenance, workspace.OperationError);
        await using var other = OpenMaintenanceWorkspace(disk, clock, service, agent, OpenStore);
        await other.OpenJobAsync(jobId.Value);
        clock.Current = Now.AddMinutes(1);
        agent.BeforeResult = other.StartAssessmentAsync;
        await workspace.ContinueMaintenanceAsync();
        Assert.Contains("no longer matches", workspace.MaintenancePhaseStatus);
        Assert.False(workspace.CanContinueMaintenance);
        Assert.False(workspace.CanInstallPackage);
        Assert.Equal(0, runtime.InstallCalls);
        await using var store = await OpenStore(default);
        Assert.Equal(MaintenancePhase.WindowsUpdates, (await store.LoadMaintenanceAsync(jobId))!.Phase);
    }

    private static AuditWorkspace OpenMaintenanceWorkspace(AuditTestStore disk, FixedClock clock, PilotPackageService service,
        MaintenanceScanFixture agent, Func<CancellationToken, Task<SqliteJobStore>> openStore)
    {
        var workspace = new AuditWorkspace(async token => await openStore(token), [OsSample(), SystemSample()],
            clock, new WorkshopReportStore(Path.Combine(disk.Root, "reports")), packageService: service,
            observePreparationResources: ReadyPreparationResources);
        // The production factory pairs active-machine package and update backends. Use only
        // the isolated scanner here, retaining the fixture/live constructor admission checks.
        typeof(AuditWorkspace).GetProperty("UpdateAgent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(workspace, agent);
        return workspace;
    }

    private sealed class MaintenanceScanFixture : IWindowsUpdateAgent
    {
        public int Scans { get; private set; }
        public WindowsUpdateStatus Status { get; set; } = WindowsUpdateStatus.Succeeded;
        public Func<Task>? BeforeResult { get; set; }
        public async Task<WindowsUpdateScan> ScanAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Scans++;
            if (BeforeResult is not null) { await BeforeResult(); }
            return new WindowsUpdateScan(Status, [], Status == WindowsUpdateStatus.Succeeded ? false : null);
        }

        public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken token) =>
            throw new InvalidOperationException();
        public Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken token) =>
            throw new InvalidOperationException();
    }
}
