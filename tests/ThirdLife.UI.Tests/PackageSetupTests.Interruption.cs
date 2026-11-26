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
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    private const string PackageInterruptionRootVariable = "THIRDLIFE_TL0405_INTERRUPTION_ROOT";
    private const string PackageInterruptionPrefix = "ThirdLife-TL0405-";
    private const string PackageInterruptionMarker = "installing.json";
    private const string PackageInterruptionConfirmed = "intent-confirmed.marker";

    [Fact]
    [Trait("Category", "Extended")]
    public async Task KilledPackageApplicationReopensTheSameJobForReviewWithoutDispatch()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = Directory.CreateTempSubdirectory(PackageInterruptionPrefix);
        try
        {
            var (job, pending) = await KillOwnedApplicationWhileInstallingAsync(root.FullName);
            var runtime = new HeldPackageInstaller();
            await using var reopened = OpenPackageInterruptionWorkspace(root.FullName, runtime);
            await reopened.OpenJobAsync(job.Value);
            Assert.Equal(job.Value, reopened.JobId);

            ShellTestHost.Run(() => InPackageInterruptionWindow(reopened, (window, panel) =>
            {
                var history = Named<TextBox>(panel, "SavedPackageHistory");
                Assert.True(history.IsVisible && history.IsReadOnly);
                Assert.Contains("No final result was saved", history.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("installed state", history.Text, StringComparison.OrdinalIgnoreCase);
                Assert.False(Named<Button>(panel, "InstallPackageButton").IsEnabled);

                var checkRecovery = Named<Button>(panel, "CheckPackageRecoveryButton");
                Assert.True(checkRecovery.IsVisible && checkRecovery.IsEnabled);
                Invoke(window, checkRecovery);
                var recovery = Named<TextBlock>(panel, "PackageRecoveryMessage");
                PumpUntil(window, () => !reopened.IsBusy && recovery.Text.Contains(
                    "Windows Installer may still be running", StringComparison.Ordinal), () => recovery.Text);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(recovery));
                Assert.Contains("Keep this job in review", new TextBlockAutomationPeer(recovery).GetName(),
                    StringComparison.Ordinal);
                Assert.False(Named<Button>(panel, "InstallPackageButton").IsEnabled);
            }));

            var afterReview = await LoadPackageInterruptionJournalAsync(root.FullName, job);
            Assert.Equal(pending.Plan.Key, afterReview.Plan.Key);
            Assert.Equal(pending.Approvals, afterReview.Approvals.AsEnumerable());
            Assert.Equal(pending.Entries, afterReview.Entries.AsEnumerable());
            Assert.Equal(1, runtime.InspectionCalls);
            Assert.Equal(0, runtime.AcquisitionCalls);
            Assert.False(runtime.ApplyEntered);
        }
        finally { root.Delete(recursive: true); }
        Assert.False(Directory.Exists(root.FullName));
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task OwnedPackageApplicationStopsWhileTheFixtureIsApplying()
    {
        var runRoot = Environment.GetEnvironmentVariable(PackageInterruptionRootVariable);
        if (string.IsNullOrEmpty(runRoot)) { return; }
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = new DirectoryInfo(runRoot);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), root.Parent!.FullName, ignoreCase: true);
        Assert.StartsWith(PackageInterruptionPrefix, root.Name, StringComparison.Ordinal);
        Assert.False(root.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Empty(root.EnumerateFileSystemInfos());

        var runtime = new HeldPackageInstaller();
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
            PumpUntil(window, () => runtime.ApplyEntered && progress.Text.Contains("applying", StringComparison.OrdinalIgnoreCase),
                () => progress.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(progress));
            Assert.Contains("Windows Installer", new TextBlockAutomationPeer(progress).GetName(), StringComparison.Ordinal);
            Assert.False(Named<Button>(panel, "CancelPackageButton").IsEnabled);

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

    private static async Task<(JobId Job, JournalHistory Journal)> KillOwnedApplicationWhileInstallingAsync(string root)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } dotnet ? dotnet : "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(PackageSetupTests).Assembly.Location);
        startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(PackageSetupTests).FullName}.{nameof(OwnedPackageApplicationStopsWhileTheFixtureIsApplying)}");
        startInfo.ArgumentList.Add("--logger:console;verbosity=quiet");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("RunConfiguration.TreatNoTestsAsError=true");
        startInfo.Environment[PackageInterruptionRootVariable] = root;
        using var launcher = Assert.IsType<Process>(Process.Start(startInfo));
        try
        {
            var marker = new FileInfo(Path.Combine(root, PackageInterruptionMarker));
            var wait = Stopwatch.StartNew();
            while (!marker.Exists && !launcher.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                marker.Refresh();
            }

            Assert.True(marker.Exists && !launcher.HasExited, "The owned WPF fixture did not reach package apply.");
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
            Assert.Equal(new[] { JournalState.Approved, JournalState.Started }, pending.Entries.Select(entry => entry.State));
            var started = pending.Entries[1];
            Assert.Equal(action.ActionId, started.ActionId);
            Assert.Equal(approval.ApprovalId, started.ApprovalId);
            Assert.NotEqual(approval.CorrelationId, started.CorrelationId);
            Assert.Equal(JournalCheckpoint.PackageDispatchIntent, started.Checkpoint);
            Assert.False(client.HasExited);
            using (new FileStream(Path.Combine(root, PackageInterruptionConfirmed), FileMode.CreateNew)) { }
            await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(0, launcher.ExitCode);
            return (job, pending);
        }
        finally
        {
            if (!launcher.HasExited)
            {
                launcher.Kill(entireProcessTree: true);
                await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private static void InPackageInterruptionWindow(AuditWorkspace workspace, Action<MainWindow, PlanReviewView> check)
    {
        var window = new MainWindow(new ShellViewModel(), workspace);
        try
        {
            ShellTestHost.Show(window);
            ShellTestHost.Invoke(window, "PrepareButton");
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

    private static AuditWorkspace OpenPackageInterruptionWorkspace(string root, IPilotPackageRuntime runtime) =>
        new(async token => await SqliteJobStore.OpenAsync(Path.Combine(root, "store"), token).ConfigureAwait(false),
            [OsSample(), SystemSample()], new FixedClock(), packageService: new PilotPackageService(runtime));

    private static async Task<JournalHistory> LoadPackageInterruptionJournalAsync(string root, JobId job)
    {
        await using var store = await SqliteJobStore.OpenAsync(Path.Combine(root, "store"));
        return Assert.Single(await store.LoadJobJournalsAsync(job));
    }

    private sealed record PackageInterruptionNotice(string JobId, int ProcessId);

    private sealed class HeldPackageInstaller : IPilotPackageRuntime
    {
        private readonly TaskCompletionSource<PackageInstallResult> _heldApply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _applyEntered;

        public int InspectionCalls { get; private set; }
        public int AcquisitionCalls { get; private set; }
        public bool ApplyEntered => Volatile.Read(ref _applyEntered) != 0;

        public PackageObservation Inspect()
        {
            InspectionCalls++;
            return new PackageObservation(PackageObservationStatus.Absent);
        }

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            AcquisitionCalls++;
            return Task.FromResult<IPackageArtifact>(new FixtureArtifact());
        }

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            Volatile.Write(ref _applyEntered, 1);
            return _heldApply.Task;
        }

        public Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Interrupted installation cannot reach the probe.");
    }
}
