using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Sanitization;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed partial class PackageSetupTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
    private const string LifecycleSource = "os.lifecycle.catalog";

    [Fact]
    public void ReviewedProfileExpiresAndBindsTheSupportedPackage()
    {
        var option = Assert.IsType<PlanProfileOption>(WorkshopPackageProfile.Load(Now));
        var entry = Assert.Single(option.Catalog.Applications);
        Assert.False(option.Profile.SyntheticData);
        Assert.Equal("0.2.0", option.Profile.Version);
        Assert.Equal("0.2.0", option.Catalog.Version);
        Assert.Equal(PilotPackageService.Identity.InstallerSha256, entry.Package.InstallerSha256);
        Assert.Null(WorkshopPackageProfile.Load(Now.AddMonths(2)));
    }

    [Theory]
    [InlineData(PackageVerificationStatus.Passed, JournalResult.PackageLaunchVerified, "passed the launch check")]
    [InlineData(PackageVerificationStatus.ExecutableMissing, JournalResult.PackageProbeExecutableMissing, "reviewed executable was missing")]
    [InlineData(PackageVerificationStatus.ExecutableUntrusted, JournalResult.PackageProbeExecutableUntrusted, "failed its launch trust check")]
    [InlineData(PackageVerificationStatus.LaunchFailed, JournalResult.PackageProbeLaunchFailed, "could not be started")]
    [InlineData(PackageVerificationStatus.ExitedEarly, JournalResult.PackageProbeExitedEarly, "closed before the launch check completed")]
    [InlineData(PackageVerificationStatus.TimedOut, JournalResult.PackageProbeTimedOut, "launch check reached its time limit")]
    [InlineData(PackageVerificationStatus.Cancelled, JournalResult.PackageProbeCancelled, "verification was cancelled")]
    [InlineData(PackageVerificationStatus.CleanupUnconfirmed, JournalResult.PackageProbeCleanupUnconfirmed, "Cleanup of the launch check could not be confirmed")]
    public Task PackagePlanUsesDurableApprovalAndReportsTheReopenedJournal(
        PackageVerificationStatus probeStatus, JournalResult expectedResult, string expectedReport) =>
        CheckPackageJournalAsync(new FixtureInstaller(PackageInstallStatus.Applied, register: true) { ProbeStatus = probeStatus },
            probeStatus == PackageVerificationStatus.Passed ? JournalState.Verified : JournalState.RequiresReview,
            expectedResult, expectedReport, probeCalls: 1);

    [Theory]
    [InlineData(null, JournalResult.PackageDetectionAbsent, "registration was not found")]
    [InlineData("8.9.9.0", JournalResult.PackageDetectionMismatch, "registration did not match")]
    public Task BackendSuccessCannotVerifyMissingOrWrongPackage(string? registeredVersion, JournalResult result, string guidance) =>
        CheckPackageJournalAsync(new FixtureInstaller(PackageInstallStatus.Applied, register: registeredVersion is not null)
        { RegisteredVersion = registeredVersion },
            JournalState.RequiresReview, result, guidance, probeCalls: 0);

    [Fact]
    public Task BackendSuccessCannotVerifyUnavailableRegistration() =>
        CheckPackageJournalAsync(new FixtureInstaller(PackageInstallStatus.Applied, register: true) { RegistrationUnavailable = true },
            JournalState.RequiresReview, JournalResult.PackageDetectionUnavailable, "registration could not be read", probeCalls: 0);

    [Theory]
    [InlineData(PackageInstallStatus.AuthorizationDeclined, JournalState.Failed, JournalResult.AuthorizationDeclined, "consent was declined")]
    [InlineData(PackageInstallStatus.ElevationUnavailable, JournalState.Failed, JournalResult.PackageElevationUnavailable, "authorization was unavailable")]
    [InlineData(PackageInstallStatus.Failed, JournalState.Failed, JournalResult.OperationFailed, "installer reported failure")]
    [InlineData(PackageInstallStatus.RestartRequired, JournalState.Applied, JournalResult.BackendApplied, "a restart is required")]
    public Task PackageVerificationWaitsForCompletedApply(
        PackageInstallStatus installerResult, JournalState expectedState, JournalResult expectedResult, string expectedReport) =>
        CheckPackageJournalAsync(new FixtureInstaller(installerResult, register: true),
            expectedState, expectedResult, expectedReport, probeCalls: 0);

    private static async Task CheckPackageJournalAsync(
        FixtureInstaller runtime, JournalState expectedState, JournalResult expectedResult, string expectedReport, int probeCalls)
    {
        using var disk = new AuditTestStore();
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        string jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            await workspace.CreateJobAsync();
            jobId = workspace.JobId;
            var storedJobId = workspace.Job!.Job.JobId;
            runtime.Probing = async () =>
            {
                await using var check = await disk.OpenSqliteAsync();
                var durable = Assert.Single(await check.LoadJobJournalsAsync(storedJobId));
                Assert.Equal(new[] { JournalState.Approved, JournalState.Started, JournalState.Applied },
                    durable.Entries.Select(entry => entry.State));
                Assert.Equal(JournalCheckpoint.PackageLaunchPendingVerification, durable.Entries[^1].Checkpoint);
                var observingRuntime = new RecoveryObservationRuntime(
                    new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion));
                await using var observer = OpenWorkspace(disk, reports, new PilotPackageService(observingRuntime));
                await observer.OpenJobAsync(storedJobId.Value);
                Assert.Contains("cleanup of any temporary launch check files have not been confirmed", observer.PackageHistoryText);
                Assert.False(observer.CanInstallPackage);
                Assert.Equal(0, observingRuntime.Probes);
            };
            await workspace.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
                "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
            await workspace.StartAssessmentAsync();
            workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => !option.Profile.SyntheticData);
            await workspace.InstallPackageAsync();
            Assert.Equal(0, runtime.InstallCalls);
            await workspace.BuildPlanReviewAsync();
            Assert.Null(workspace.PlanReview?.Plan.BlockedReason);
            Assert.Contains("launch the application with no documents", workspace.PlanReviewDetails);
            Assert.False(workspace.CanApprovePlanReview);
            workspace.PackageOperatorCode = "fixture-operator";
            Assert.True(workspace.CanApprovePlanReview, workspace.PackageSourceStatus);
            await workspace.ApprovePlanReviewAsync();
            Assert.True(workspace.CanInstallPackage, workspace.PlanReviewStatus);
            await workspace.InstallPackageAsync();
            Assert.Equal(1, runtime.InstallCalls);
            Assert.False(workspace.CanInstallPackage);
            Assert.Contains(expectedReport, workspace.PackageHistoryText);
            if (expectedState == JournalState.RequiresReview)
            {
                Assert.True(workspace.CanCheckPackageRecovery);
                await workspace.CheckPackageRecoveryAsync();
                Assert.False(workspace.CanInstallPackage);
                Assert.Contains(expectedReport, workspace.PackageHistoryText);
            }
            workspace.CloseJob();
            Assert.Empty(workspace.PackageHistoryText);
        }

        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId);
        Assert.Contains(expectedReport, reopened.PackageHistoryText);
        Assert.Contains(expectedReport, reopened.LatestPackageOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(reopened.PackageJournal, entry => entry.Title == "Exact plan approval saved");
        Assert.Contains(reopened.PackageJournal, entry => entry.Title == "Dispatch intent saved");
        Assert.Equal(expectedState == JournalState.Verified,
            reopened.PackageJournal.Any(entry => entry.Title == "Independent verification saved"));
        Assert.False(reopened.CanInstallPackage);
        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        Assert.Contains("Notepad++.Notepad++ 8.9.9.1", reopened.ReportPreview);
        Assert.Contains("Requested scope machine-wide", reopened.ReportPreview);
        Assert.Contains(expectedReport, reopened.ReportPreview);
        Assert.DoesNotContain("fixture-operator", reopened.ReportPreview.Split("Machine-wide setup journal")[1]);
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Equal(probeCalls, runtime.ProbeCalls);
        await using var check = await OpenPackageStoreAsync(disk, new FixedClock());
        var history = Assert.Single(await check.LoadJobJournalsAsync(reopened.Job!.Job.JobId));
        var step = Assert.Single(history.Plan.Schedule!.Steps);
        Assert.Equal(history.Plan.Actions[0].ActionId, step.ActionId);
        Assert.Equal(1, step.Phase);
        Assert.Empty(step.Dependencies);
        Assert.Equal(Assert.Single(history.Approvals).Plan, history.Plan.Key);
        Assert.Equal(expectedState, history.StateOf(history.Plan.Actions[0].ActionId));
        Assert.Equal(JournalCheckpoint.PackageDispatchIntent, history.Entries[1].Checkpoint);
        Assert.Equal(JournalActor.PackageBackend, history.Entries[2].Actor);
        var last = history.Entries[^1];
        Assert.Equal(expectedResult, last.Result);
        if (last.Checkpoint == JournalCheckpoint.RestartRequired)
        {
            var checkpoint = await check.LoadPackageRebootCheckpointAsync(last);
            Assert.NotNull(checkpoint);
            Assert.False(checkpoint.CanVerify);
            Assert.False(checkpoint.IsExpired);
        }

        if (expectedState is JournalState.Verified || expectedState == JournalState.RequiresReview && history.Entries[2].State == JournalState.Applied)
        {
            Assert.Equal(JournalActor.Verifier, last.Actor);
            Assert.Equal(history.Entries[2].ApprovalId, last.ApprovalId);
            Assert.Equal(history.Entries[2].CorrelationId, last.CorrelationId);
        }
    }

    [Fact]
    public async Task PackageControlsExposeApprovalAndInstallationAccessibly()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, true);
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime));
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
            "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
        await workspace.StartAssessmentAsync();
        workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => !option.Profile.SyntheticData);
        await workspace.BuildPlanReviewAsync();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace);
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Prepare);
                ShellTestHost.Drain(window);
                var panel = Named<PlanReviewView>(Named<FrameworkElement>(window, "AuditView"), "PlanReviewPanel");
                Assert.Equal(8, Named<ItemsControl>(panel, "MaterialImpactItems").Items.Count);
                Assert.Single(workspace.VisiblePlanProfiles);
                var verification = Named<TextBlock>(panel, "PackageVerificationNotice");
                Assert.True(verification.IsVisible && verification.Focusable);
                Assert.Contains("with no documents and closes the instance it started", new TextBlockAutomationPeer(verification).GetName(),
                    StringComparison.Ordinal);
                var code = Named<TextBox>(panel, "PackageOperator");
                Assert.True(code.Focusable && code.IsTabStop);
                var apply = Named<Button>(panel, "InstallPackageButton");
                Assert.True(apply.Focusable && apply.IsTabStop);
                Assert.False(apply.IsEnabled);
                var cancel = Named<Button>(panel, "CancelPackageButton");
                Assert.True(cancel.Focusable && cancel.IsTabStop);
                Assert.False(cancel.IsEnabled);
                var plan = workspace.PlanReview;
                window.ShowStage(ShellStage.Verify);
                ShellTestHost.Drain(window);
                Assert.False(panel.IsVisible);
                Assert.True(workspace.ShowsVerifyEmptyState);
                window.ShowStage(ShellStage.Prepare);
                ShellTestHost.Drain(window);
                Assert.Same(plan, workspace.PlanReview);
                Assert.Equal(0, runtime.InstallCalls);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public async Task AnExpiredApprovalCannotDispatchAndTheUnstartedPlanCanBeReapproved()
    {
        using var disk = new AuditTestStore();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, true);
        var clock = new FixedClock();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime), clock);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
            "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
        await workspace.StartAssessmentAsync();
        workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => !option.Profile.SyntheticData);
        await workspace.BuildPlanReviewAsync();
        workspace.PackageOperatorCode = "fixture-operator";
        await workspace.ApprovePlanReviewAsync();
        Assert.True(workspace.CanInstallPackage);
        clock.Current = Now.AddMinutes(11);
        await workspace.InstallPackageAsync();
        Assert.Equal(0, runtime.InstallCalls);
        Assert.Contains("fresh plan", workspace.PlanReviewStatus);

        await workspace.BuildPlanReviewAsync();
        await workspace.ApprovePlanReviewAsync();
        Assert.True(workspace.CanInstallPackage);
        await workspace.InstallPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Contains("Independent verification confirmed", workspace.PackageHistoryText);
    }

    [Fact]
    public async Task ApprovalExpiringDuringAcquisitionPreventsNativeDispatch()
    {
        using var disk = new AuditTestStore();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, true);
        var clock = new FixedClock();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime), clock);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
            "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
        await workspace.StartAssessmentAsync();
        workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => !option.Profile.SyntheticData);
        await workspace.BuildPlanReviewAsync();
        workspace.PackageOperatorCode = "fixture-operator";
        await workspace.ApprovePlanReviewAsync();
        runtime.Acquiring = () => clock.Current = Now.AddMinutes(11);
        await workspace.InstallPackageAsync();
        Assert.Equal(0, runtime.InstallCalls);
        Assert.Contains("changed before dispatch", workspace.PackageHistoryText);
    }

    private static AuditWorkspace OpenWorkspace(
        AuditTestStore disk, WorkshopReportStore reports, PilotPackageService service, TimeProvider? clock = null,
        Func<StructuredDiagnosticEvent, CancellationToken, Task>? appendPackageDiagnostic = null)
    {
        var time = clock ?? new FixedClock();
        return new(async token => await OpenPackageStoreAsync(disk, time, token),
            [OsSample(), SystemSample()], time, reports,
            packageService: service, appendPackageDiagnostic: appendPackageDiagnostic);
    }

    private static Task<SqliteJobStore> OpenPackageStoreAsync(
        AuditTestStore disk, TimeProvider clock, CancellationToken cancellationToken = default) =>
        SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion,
            clock, faultInjector: null, cancellationToken);

    private static FixtureProvider OsSample() => new("windows-operating-system",
        Enum("os.family", "windows_11", "os.windows.identity"),
        Enum("os.architecture", "x64", "os.dotnet.os_architecture"),
        Enum("os.support_target", "supported", LifecycleSource),
        Text("os.lifecycle.source", "microsoft.windows-client.release-health"),
        Text("os.lifecycle.revision", "thirdlife.windows-lifecycle.2026-11-25.v1"),
        Text("os.lifecycle.as_of", "2026-11-25"),
        Text("os.lifecycle.fresh_through", "2026-12-25"),
        Text("os.lifecycle.evaluated_on", "2026-11-25"));

    private static FixtureProvider SystemSample() => new("windows-system-inventory",
        Enum("system.architecture", "x64", "system_inventory.dotnet.os_architecture"));

    private static NormalizedEvidence Enum(string key, string value, string source) =>
        NormalizedEvidence.Observed(new EvidenceKey(key), EvidenceValue.FromEnum(value), sourceReference: source);

    private static NormalizedEvidence Text(string key, string value) =>
        NormalizedEvidence.Observed(new EvidenceKey(key), EvidenceValue.FromString(value), sourceReference: LifecycleSource);

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    // The test store and service are isolated. These shaped inputs never enter a real job.
    private sealed class FixtureProvider(string id, params NormalizedEvidence[] evidence) : IInventoryProvider
    {
        public InventoryProviderDescriptor Descriptor { get; } = new(new(id), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2), ProviderNetworkUse.None, ProviderEvidenceOrigin.ActiveMachine,
            [ProviderOperatingSystem.Windows11], new(new("fixture." + id + ".failed"), "fixture.package_setup.failed"),
            evidence.Select(fact => new ProviderEvidenceDefinition(fact.EvidenceKey, fact.Value!.Kind, null, fact.SourceReference!)));

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderReadResult.Collected(evidence));
    }

    private sealed class FixtureInstaller(PackageInstallStatus result, bool register) : IPilotPackageRuntime
    {
        private bool _registered;
        public int InstallCalls { get; private set; }
        public int ProbeCalls { get; private set; }
        public Action? Acquiring { get; set; }
        public Func<Task>? Probing { get; set; }
        public PackageVerificationStatus ProbeStatus { get; init; } = PackageVerificationStatus.Passed;
        public string? RegisteredVersion { get; init; } = PilotPackageService.Identity.ExactVersion;
        public bool RegistrationUnavailable { get; init; }

        public PackageObservation Inspect() => _registered
            ? RegistrationUnavailable ? new(PackageObservationStatus.Unavailable) : new(PackageObservationStatus.Installed, RegisteredVersion)
            : new(PackageObservationStatus.Absent);

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            Acquiring?.Invoke();
            return Task.FromResult<IPackageArtifact>(new FixtureArtifact());
        }

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            InstallCalls++;
            _registered = register;
            return Task.FromResult(new PackageInstallResult(result));
        }

        public async Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            ProbeCalls++;
            if (Probing is { } probe)
            {
                await probe();
            }

            return ProbeStatus;
        }
    }

    private sealed class FixtureArtifact : IPackageArtifact
    {
        public void Dispose() { }
    }
}
