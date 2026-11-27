using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using ThirdLife.Actions.Journal;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    private const string PackageInterruptionRootVariable = "THIRDLIFE_TL0405_INTERRUPTION_ROOT";
    private const string PackageInterruptionVerificationVariable = "THIRDLIFE_TL0408_HOLD_VERIFICATION";
    private const string PackageInterruptionPrefix = "ThirdLife-TL0405-";
    private const string PackageInterruptionMarker = "installing.json";
    private const string PackageInterruptionConfirmed = "intent-confirmed.marker";
    private const string PackageBackendMarker = "backend-ready.marker";

    [Fact]
    [Trait("Category", "Extended")]
    public async Task KilledPackageApplicationReopensTheSameJobForReviewWithoutDispatch()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = Directory.CreateTempSubdirectory(PackageInterruptionPrefix);
        try
        {
            var (job, pending) = await KillOwnedPackageApplicationAsync(root.FullName, holdVerification: false);
            var runtime = new HeldPackageInstaller();
            await using var reopened = OpenPackageInterruptionWorkspace(root.FullName, runtime);
            await reopened.OpenJobAsync(job.Value);
            Assert.Equal(job.Value, reopened.JobId);
            Assert.Equal(1, runtime.InspectionCalls);
            Assert.Contains("Windows Installer may still be running", reopened.PackageRecoveryStatus, StringComparison.Ordinal);

            InPackageVerificationWindow(reopened, (window, panel) =>
            {
                Named<Expander>(panel, "PackageHistoryExpander").IsExpanded = true;
                ShellTestHost.Drain(window);
                var history = Named<TextBox>(panel, "SavedPackageHistory");
                Assert.True(history.IsVisible && history.IsReadOnly);
                Assert.Contains("No final result was saved", history.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("installed state", history.Text, StringComparison.OrdinalIgnoreCase);
                Assert.False(reopened.CanInstallPackage);

                var checkRecovery = Named<Button>(panel, "CheckPackageRecoveryButton");
                Assert.True(checkRecovery.IsVisible && checkRecovery.IsEnabled);
                Invoke(window, checkRecovery);
                var recovery = Named<TextBlock>(panel, "PackageRecoveryMessage");
                PumpUntil(window, () => !reopened.IsBusy && recovery.Text.Contains(
                    "Windows Installer may still be running", StringComparison.Ordinal), () => recovery.Text);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(recovery));
                Assert.Contains("Keep this job in review", new TextBlockAutomationPeer(recovery).GetName(),
                    StringComparison.Ordinal);
                Assert.False(reopened.CanInstallPackage);
            });

            var afterReview = await LoadPackageInterruptionJournalAsync(root.FullName, job);
            Assert.Equal(pending.Plan.Key, afterReview.Plan.Key);
            Assert.Equal(pending.Approvals, afterReview.Approvals.AsEnumerable());
            Assert.Equal(pending.Entries, afterReview.Entries.AsEnumerable());
            Assert.Equal(2, runtime.InspectionCalls);
            Assert.Equal(0, runtime.AcquisitionCalls);
            Assert.False(runtime.ApplyEntered);
        }
        finally { root.Delete(recursive: true); }
        Assert.False(Directory.Exists(root.FullName));
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task KilledPackageApplicationReopensTheAppliedAttemptAndVerifiesWithoutDispatch()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = Directory.CreateTempSubdirectory(PackageInterruptionPrefix);
        try
        {
            var (job, pending) = await KillOwnedPackageApplicationAsync(root.FullName, holdVerification: true);
            var runtime = new RecoveryObservationRuntime(
                new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion));
            await using var reopened = OpenPackageInterruptionWorkspace(root.FullName, runtime);
            await reopened.OpenJobAsync(job.Value);
            Assert.Equal(job.Value, reopened.JobId);
            Assert.Contains("passed the launch check", reopened.PackageHistoryText, StringComparison.Ordinal);
            Assert.False(reopened.CanInstallPackage);
            Assert.Equal(1, runtime.Inspections);
            Assert.Equal(1, runtime.Probes);

            var verifiedHistory = await LoadPackageInterruptionJournalAsync(root.FullName, job);
            Assert.Equal(pending.Plan.Key, verifiedHistory.Plan.Key);
            Assert.Equal(pending.Approvals.AsEnumerable(), verifiedHistory.Approvals.AsEnumerable());
            Assert.Equal(pending.Entries.AsEnumerable(), verifiedHistory.Entries.Take(pending.Entries.Length));
            var applied = pending.Entries[^1];
            var verified = Assert.Single(verifiedHistory.Entries.Skip(pending.Entries.Length));
            Assert.Equal(JournalState.Applied, verified.PriorState);
            Assert.Equal(JournalState.Verified, verified.State);
            Assert.Equal(JournalResult.PackageLaunchVerified, verified.Result);
            Assert.Equal(applied.ApprovalId, verified.ApprovalId);
            Assert.Equal(applied.CorrelationId, verified.CorrelationId);
        }
        finally { root.Delete(recursive: true); }
        Assert.False(Directory.Exists(root.FullName));
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task TerminatedOwnedPackageBackendLeavesTheReopenedAttemptInReview()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = Directory.CreateTempSubdirectory(PackageInterruptionPrefix);
        try
        {
            using var disk = new AuditTestStore();
            var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
            JournalHistory? pending = null;
            JobId? job = null;
            var backend = new InterruptedPackageRuntime
            {
                Install = async cancellationToken =>
                {
                    using var process = StartOwnedPackageFixture(root.FullName, nameof(OwnedPackageBackendWaitsForTermination));
                    try
                    {
                        var marker = new FileInfo(Path.Combine(root.FullName, PackageBackendMarker));
                        await WaitForOwnedPackageMarkerAsync(process, marker);
                        Assert.Equal(sizeof(int), marker.Length);
                        using var client = Process.GetProcessById(
                            BitConverter.ToInt32(await File.ReadAllBytesAsync(marker.FullName, cancellationToken)));
                        Assert.False(client.HasExited);
                        Assert.Equal(WindowsBrokerPeer.CurrentIdentity().SessionId, client.SessionId);
                        pending = await ReadRecoveryHistoryAsync(disk, job!);
                        using (new FileStream(Path.Combine(root.FullName, PackageInterruptionConfirmed), FileMode.CreateNew)) { }
                        await client.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                        Assert.NotEqual(0, process.ExitCode);
                        return new PackageInstallResult(PackageInstallStatus.ReviewRequired);
                    }
                    finally { await StopOwnedPackageFixtureAsync(process); }
                },
            };
            await using (var workspace = OpenWorkspace(disk, reports, new PilotPackageService(backend)))
            {
                job = await ApproveInitialPackagePlanAsync(workspace);
                await workspace.InstallPackageAsync();
            }

            var inFlight = Assert.IsType<JournalHistory>(pending);
            Assert.Equal(JournalCheckpoint.PackageDispatchIntent, inFlight.Entries[^1].Checkpoint);
            var saved = await ReadRecoveryHistoryAsync(disk, job);
            Assert.Equal(inFlight.Entries.AsEnumerable(), saved.Entries.Take(inFlight.Entries.Length));
            AssertInterruptedAttempt(saved, JournalState.RequiresReview, JournalResult.Interrupted, JournalCheckpoint.Reconcile);
            Assert.Equal(1, backend.InstallCalls);
            var runtime = new RecoveryObservationRuntime(
                new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion));
            await using var reopened = OpenWorkspace(disk, reports, new PilotPackageService(runtime));
            await reopened.OpenJobAsync(job.Value);
            Assert.False(reopened.CanInstallPackage);
            Assert.Contains("keep this job in review", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, runtime.Inspections);
            Assert.Equal(0, runtime.Probes);
            Assert.Equal(saved.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, job)).Entries.AsEnumerable());
        }
        finally { root.Delete(recursive: true); }
        Assert.False(Directory.Exists(root.FullName));
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task OwnedPackageBackendWaitsForTermination()
    {
        if (OpenFixtureRunDirectory() is not { } root) { return; }
        var marker = Path.Combine(root.FullName, PackageBackendMarker);
        var staged = marker + ".pending";
        await File.WriteAllBytesAsync(staged, BitConverter.GetBytes(Environment.ProcessId));
        File.Move(staged, marker);
        var confirmation = Path.Combine(root.FullName, PackageInterruptionConfirmed);
        var timeout = Stopwatch.StartNew();
        while (!File.Exists(confirmation) && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(File.Exists(confirmation), "The launcher did not confirm the durable package intent.");
        using var self = Process.GetCurrentProcess();
        self.Kill();
        throw new InvalidOperationException("The owned package backend fixture did not terminate.");
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task OwnedPackageApplicationStopsAtTheFixtureBoundary()
    {
        if (OpenFixtureRunDirectory() is not { } root) { return; }

        var holdVerification = Environment.GetEnvironmentVariable(PackageInterruptionVerificationVariable) == "1";
        var runtime = new HeldPackageInstaller(holdVerification);
        await using var workspace = OpenPackageInterruptionWorkspace(root.FullName, runtime);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
            "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
        await workspace.StartAssessmentAsync();

        ShellTestHost.Run(() => InPackageInterruptionWindow(workspace, (window, panel) =>
        {
            workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => !option.Profile.SyntheticData);
            ShellTestHost.Drain(window);
            Invoke(window, Named<Button>(panel, "BuildPlanReviewButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.HasPlanReview, () => workspace.PlanReviewStatus);
            Named<TextBox>(panel, "PackageOperator").Text = "fixture-operator";
            ShellTestHost.Drain(window);
            Invoke(window, Named<Button>(panel, "ApprovePlanReviewButton"));
            PumpUntil(window, () => workspace.CanInstallPackage, () => workspace.PlanReviewStatus);
            Invoke(window, Named<Button>(panel, "InstallPackageButton"));

            var progress = Named<TextBlock>(panel, "PackageSourceMessage");
            var stage = holdVerification ? "testing application launch" : "applying";
            PumpUntil(window, () => runtime.ApplyEntered && (!holdVerification || runtime.ProbeEntered) &&
                progress.Text.Contains(stage, StringComparison.OrdinalIgnoreCase), () => progress.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(progress));
            Assert.Contains(stage, new TextBlockAutomationPeer(progress).GetName(), StringComparison.OrdinalIgnoreCase);
            if (!holdVerification)
            {
                Assert.False(Named<Button>(panel, "CancelPackageButton").IsEnabled);
            }

            var marker = Path.Combine(root.FullName, PackageInterruptionMarker);
            var staged = marker + ".pending";
            File.WriteAllText(staged, JsonSerializer.Serialize(new PackageInterruptionNotice(workspace.JobId, Environment.ProcessId)));
            File.Move(staged, marker);
            WaitForPackageInterruptionConfirmation(window, Path.Combine(root.FullName, PackageInterruptionConfirmed));

            // The launcher has verified the durable intent. Terminate only this fixture process.
            using var self = Process.GetCurrentProcess();
            self.Kill();
            throw new InvalidOperationException("The owned WPF fixture process did not terminate.");
        }), TimeSpan.FromSeconds(50));
    }

    private static async Task<(JobId Job, JournalHistory Journal)> KillOwnedPackageApplicationAsync(string root, bool holdVerification)
    {
        using var launcher = StartOwnedPackageFixture(root, nameof(OwnedPackageApplicationStopsAtTheFixtureBoundary), holdVerification);
        try
        {
            var marker = new FileInfo(Path.Combine(root, PackageInterruptionMarker));
            await WaitForOwnedPackageMarkerAsync(launcher, marker);
            Assert.InRange(marker.Length, 1, 512);
            var notice = Assert.IsType<PackageInterruptionNotice>(
                JsonSerializer.Deserialize<PackageInterruptionNotice>(await File.ReadAllTextAsync(marker.FullName)));
            Assert.Matches(@"\Ajob-[0-9a-f]{32}\z", notice.JobId);
            var job = new JobId(notice.JobId);
            using var client = Process.GetProcessById(notice.ProcessId);
            Assert.False(client.HasExited);
            Assert.Equal(WindowsBrokerPeer.CurrentIdentity().SessionId, client.SessionId);
            var pending = await LoadPackageInterruptionJournalAsync(root, job);
            var approval = Assert.Single(pending.Approvals);
            var action = Assert.Single(pending.Plan.Actions);
            Assert.Equal(action.ActionId, Assert.Single(pending.Plan.Schedule!.Steps).ActionId);
            JournalState[] expectedStates = holdVerification
                ? [JournalState.Approved, JournalState.Started, JournalState.Applied]
                : [JournalState.Approved, JournalState.Started];
            Assert.Equal(expectedStates, pending.Entries.Select(entry => entry.State));
            var started = pending.Entries[1];
            Assert.Equal(action.ActionId, started.ActionId);
            Assert.Equal(approval.ApprovalId, started.ApprovalId);
            Assert.NotEqual(approval.CorrelationId, started.CorrelationId);
            Assert.Equal(JournalCheckpoint.PackageDispatchIntent, started.Checkpoint);
            if (holdVerification)
            {
                Assert.Equal(JournalCheckpoint.PackageLaunchPendingVerification, pending.Entries[^1].Checkpoint);
            }
            Assert.False(client.HasExited);
            using (new FileStream(Path.Combine(root, PackageInterruptionConfirmed), FileMode.CreateNew)) { }
            await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(0, launcher.ExitCode);
            return (job, pending);
        }
        finally { await StopOwnedPackageFixtureAsync(launcher); }
    }

    private static DirectoryInfo? OpenFixtureRunDirectory()
    {
        var runRoot = Environment.GetEnvironmentVariable(PackageInterruptionRootVariable);
        if (string.IsNullOrEmpty(runRoot)) { return null; }
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = new DirectoryInfo(runRoot);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), root.Parent!.FullName, ignoreCase: true);
        Assert.StartsWith(PackageInterruptionPrefix, root.Name, StringComparison.Ordinal);
        Assert.False(root.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Empty(root.EnumerateFileSystemInfos());
        return root;
    }

    private static Process StartOwnedPackageFixture(string root, string testName, bool holdVerification = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } dotnet ? dotnet : "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(PackageSetupTests).Assembly.Location);
        startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(PackageSetupTests).FullName}.{testName}");
        startInfo.ArgumentList.Add("--logger:console;verbosity=quiet");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("RunConfiguration.TreatNoTestsAsError=true");
        startInfo.Environment[PackageInterruptionRootVariable] = root;
        startInfo.Environment[PackageInterruptionVerificationVariable] = holdVerification ? "1" : "0";
        return Assert.IsType<Process>(Process.Start(startInfo));
    }

    private static async Task WaitForOwnedPackageMarkerAsync(Process process, FileInfo marker)
    {
        var wait = Stopwatch.StartNew();
        while (!marker.Exists && !process.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            marker.Refresh();
        }

        Assert.True(marker.Exists && !process.HasExited, "The owned fixture did not reach the package interruption boundary.");
    }

    private static async Task StopOwnedPackageFixtureAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static void InPackageInterruptionWindow(AuditWorkspace workspace, Action<MainWindow, PlanReviewView> check)
    {
        var window = new MainWindow(workspace);
        try
        {
            ShellTestHost.Show(window);
            window.ShowStage(ShellStage.Prepare);
            var panel = Named<PlanReviewView>(Named<FrameworkElement>(window, "AuditView"), "PlanReviewPanel");
            Assert.True(panel.IsVisible);
            check(window, panel);
        }
        finally { if (window.IsVisible) { CloseWindow(window); } }
    }

    private static void WaitForPackageInterruptionConfirmation(Window window, string path)
    {
        var frame = new DispatcherFrame();
        var wait = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        timer.Tick += (_, _) => frame.Continue = window.IsVisible && !File.Exists(path) && wait.Elapsed < TimeSpan.FromSeconds(20);
        try
        {
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        finally { timer.Stop(); }
        Assert.True(window.IsVisible && File.Exists(path), "The launcher did not confirm the durable package intent.");
    }

    private static AuditWorkspace OpenPackageInterruptionWorkspace(string root, IPilotPackageRuntime runtime)
    {
        var time = new FixedClock();
        return new(async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(root, "store"),
                SqliteJobStore.CurrentSchemaVersion, time, faultInjector: null, token).ConfigureAwait(false),
            [OsSample(), SystemSample()], time, packageService: new PilotPackageService(runtime),
            observePreparationResources: ReadyPreparationResources);
    }

    private static async Task<JournalHistory> LoadPackageInterruptionJournalAsync(string root, JobId job)
    {
        await using var store = await SqliteJobStore.OpenAsync(Path.Combine(root, "store"));
        return Assert.Single(await store.LoadJobJournalsAsync(job));
    }

    private sealed record PackageInterruptionNotice(string JobId, int ProcessId);

    private sealed class HeldPackageInstaller(bool holdVerification = false) : IPilotPackageRuntime
    {
        private readonly TaskCompletionSource<PackageInstallResult> _heldApply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<PackageVerificationStatus> _heldProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _applyEntered;
        private int _probeEntered;

        public int InspectionCalls { get; private set; }
        public int AcquisitionCalls { get; private set; }
        public bool ApplyEntered => Volatile.Read(ref _applyEntered) != 0;
        public bool ProbeEntered => Volatile.Read(ref _probeEntered) != 0;

        public PackageObservation Inspect()
        {
            InspectionCalls++;
            return holdVerification && ApplyEntered
                ? new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion)
                : new PackageObservation(PackageObservationStatus.Absent);
        }

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            AcquisitionCalls++;
            return Task.FromResult<IPackageArtifact>(new FixtureArtifact());
        }

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            Volatile.Write(ref _applyEntered, 1);
            return holdVerification ? Task.FromResult(new PackageInstallResult(PackageInstallStatus.Applied)) : _heldApply.Task;
        }

        public Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Assert.True(holdVerification, "Interrupted installation cannot reach the probe.");
            Volatile.Write(ref _probeEntered, 1);
            return _heldProbe.Task;
        }
    }
}
