using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Packages;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Fact]
    public async Task CancellingTheDownloadThroughWpfPreservesTheAttemptAndAllowsFreshApproval()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var clock = new FixedClock();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service, clock))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            JournalHistory? pending = null;
            var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.NextAcquisition = async token =>
            {
                pending = await ReadRecoveryHistoryAsync(disk, jobId);
                downloadStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new FixtureArtifact();
            };

            InPackagePreparationWindow(workspace, (window, panel) =>
            {
                var status = Named<TextBlock>(panel, "PackageSourceMessage");
                var peer = new TextBlockAutomationPeer(status);
                var updates = new List<string>();
                Binding.AddTargetUpdatedHandler(status, (_, args) =>
                {
                    if (args.Property == TextBlock.TextProperty)
                    {
                        updates.Add(peer.GetName());
                    }
                });
                Invoke(window, Named<Button>(panel, "InstallPackageButton"));
                var cancel = Named<Button>(panel, "CancelPackageButton");
                PumpUntil(window, () => downloadStarted.Task.IsCompletedSuccessfully && cancel.IsEnabled &&
                    peer.GetName().Contains("Downloading and checking", StringComparison.Ordinal), () => workspace.PackageSourceStatus);

                var journal = Assert.IsType<JournalHistory>(pending);
                var intent = journal.Entries.Last();
                Assert.Equal(JournalState.Started, intent.State);
                Assert.Equal(JournalCheckpoint.PackageDispatchIntent, intent.Checkpoint);
                Assert.Equal(Assert.Single(journal.Plan.Actions).ActionId, intent.ActionId);
                Assert.Equal(Assert.Single(journal.Approvals).ApprovalId, intent.ApprovalId);
                Assert.NotEqual(Guid.Empty, intent.CorrelationId);
                Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
                Assert.True(status.Focusable && KeyboardNavigation.GetIsTabStop(status));
                Assert.Contains(peer.GetName(), updates);
                Assert.True(ShellTestHost.ButtonPeer(cancel).IsKeyboardFocusable());

                Invoke(window, cancel);
                PumpUntil(window, () => !workspace.IsBusy, () => workspace.PackageSourceStatus);
                Assert.False(cancel.IsEnabled);
                Assert.False(workspace.CanInstallPackage);
                Assert.Contains("cancelled", workspace.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
            });
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        AssertInterruptedAttempt(saved, JournalState.Failed, JournalResult.Cancelled, JournalCheckpoint.PackagePreDispatchFailure);
        Assert.Equal(0, runtime.InstallCalls);
        clock.Current = Now.AddMinutes(11);
        await VerifyFreshPackageApprovalAsync(disk, service, runtime, reports, jobId, saved, clock);
    }

    [Fact]
    public async Task NetworkLossKeepsResolvedPlanAndApprovalWithoutStoringBackendDetail()
    {
        const string backendDetail = "https://downloads.example.invalid/fixture?token=fixture-secret";
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime);
        var clock = new FixedClock();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service, clock))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            runtime.NextAcquisition = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, backendDetail);
            await workspace.InstallPackageAsync();
            Assert.Contains("lost its connection", workspace.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(backendDetail, workspace.PackageHistoryText, StringComparison.Ordinal);
            Assert.DoesNotContain(backendDetail, workspace.PackageSourceStatus, StringComparison.Ordinal);
            Assert.DoesNotContain(backendDetail, workspace.PlanReviewStatus, StringComparison.Ordinal);
            Assert.False(workspace.CanInstallPackage);
            Assert.Equal(0, runtime.InstallCalls);
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        AssertInterruptedAttempt(saved, JournalState.Failed, JournalResult.NetworkUnavailable, JournalCheckpoint.PackagePreDispatchFailure);
        Assert.DoesNotContain(backendDetail, JsonSerializer.Serialize(saved.Entries), StringComparison.Ordinal);

        await using var reopened = OpenWorkspace(disk, reports, service, clock);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.False(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        Assert.Equal(saved.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
        Assert.Equal(0, runtime.InstallCalls);

        clock.Current = Now.AddSeconds(5);
        Assert.True(reopened.CanRetryPackage, reopened.PlanReviewStatus);
        InPackageVerificationWindow(reopened, (window, panel) =>
        {
            var retry = Named<Button>(panel, "RetryPackageButton");
            var peer = ShellTestHost.ButtonPeer(retry);
            Assert.True(retry.IsVisible && retry.IsEnabled && retry.IsTabStop);
            Assert.True(peer.IsKeyboardFocusable());
            Assert.Contains("retry", peer.GetName(), StringComparison.OrdinalIgnoreCase);
            var message = Named<TextBlock>(panel, "PackageRetryMessage");
            var messagePeer = new TextBlockAutomationPeer(message);
            Assert.True(message.IsVisible && message.Focusable && KeyboardNavigation.GetIsTabStop(message));
            Assert.Equal(AutomationLiveSetting.Polite, messagePeer.GetLiveSetting());
            Assert.Contains("plan and approval", messagePeer.GetName(), StringComparison.OrdinalIgnoreCase);
            retry.BringIntoView();
            Assert.True(retry.Focus());
            Invoke(window, retry);
            PumpUntil(window, () => !reopened.IsBusy &&
                reopened.PackageHistoryText.Contains("Independent verification confirmed", StringComparison.Ordinal));
            Assert.Same(message, Keyboard.FocusedElement);
            Assert.Contains("lost its connection", reopened.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
            Assert.False(reopened.CanRetryPackage);
        });

        var retried = await AssertPackageRetryAsync(disk, jobId, saved, Assert.Single(saved.Approvals).ApprovalId);
        Assert.Equal(saved.Approvals.AsEnumerable(), retried.Approvals.AsEnumerable());
        Assert.Equal(1, runtime.InstallCalls);
        await using var reportWorkspace = OpenWorkspace(disk, reports, service, clock);
        await reportWorkspace.OpenJobAsync(jobId.Value);
        await reportWorkspace.GenerateReportAsync();
        await reportWorkspace.OpenReportAsync();
        Assert.Contains("lost its connection", reportWorkspace.ReportPreview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Independent verification confirmed", reportWorkspace.ReportPreview, StringComparison.Ordinal);
        Assert.DoesNotContain(backendDetail, reportWorkspace.ReportPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallerTimeoutLeavesTheAttemptInReviewAndIgnoresALateBackendResult()
    {
        using var disk = new AuditTestStore();
        var enteringInstaller = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInstaller = new TaskCompletionSource<PackageInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken installerToken = default;
        var runtime = new InterruptedPackageRuntime
        {
            Install = token =>
            {
                installerToken = token;
                enteringInstaller.SetResult();
                return finishInstaller.Task;
            },
        };
        var service = new PilotPackageService(runtime, installTimeout: TimeSpan.FromSeconds(3));
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            InPackagePreparationWindow(workspace, (window, panel) =>
            {
                var status = Named<TextBlock>(panel, "PackageSourceMessage");
                var peer = new TextBlockAutomationPeer(status);
                var cancel = Named<Button>(panel, "CancelPackageButton");
                Invoke(window, Named<Button>(panel, "InstallPackageButton"));
                try
                {
                    PumpUntil(window, () => enteringInstaller.Task.IsCompletedSuccessfully &&
                        peer.GetName().Contains("Windows Installer is applying", StringComparison.Ordinal), () => workspace.PackageSourceStatus);
                    Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
                    Assert.False(cancel.IsEnabled);
                    workspace.CancelPackageOperation();
                    Assert.False(installerToken.IsCancellationRequested);
                    PumpUntil(window, () => !workspace.IsBusy, () => workspace.PackageSourceStatus);
                    Assert.Contains("reached its time limit", workspace.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
                    Assert.False(workspace.CanInstallPackage);
                    Assert.False(installerToken.IsCancellationRequested);
                    Assert.False(runtime.LastArtifact!.Disposal.Task.IsCompleted);
                }
                finally
                {
                    finishInstaller.TrySetResult(new PackageInstallResult(PackageInstallStatus.Failed));
                }
            });
        }

        await runtime.LastArtifact!.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        AssertInterruptedAttempt(saved, JournalState.RequiresReview, JournalResult.TimedOut, JournalCheckpoint.Reconcile);
        Assert.DoesNotContain(saved.Entries, entry => entry.State is JournalState.Verified or JournalState.RolledBack);
        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Contains("reached its time limit", reopened.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
        Assert.False(reopened.CanInstallPackage);
        Assert.True(reopened.CanCheckPackageRecovery);
        var inspections = runtime.Inspections;
        InPackageVerificationWindow(reopened, (window, panel) =>
        {
            var check = Named<Button>(panel, "CheckPackageRecoveryButton");
            Assert.True(ShellTestHost.ButtonPeer(check).IsKeyboardFocusable());
            Invoke(window, check);
            PumpUntil(window, () => !reopened.IsBusy);
            var message = new TextBlockAutomationPeer(Named<TextBlock>(panel, "PackageRecoveryMessage"));
            Assert.Equal(AutomationLiveSetting.Polite, message.GetLiveSetting());
            Assert.Contains("approved version was not found", message.GetName(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("keep this job in review", message.GetName(), StringComparison.OrdinalIgnoreCase);
            Assert.False(reopened.CanInstallPackage);
        });
        Assert.Equal(inspections + 1, runtime.Inspections);
        Assert.Equal(1, runtime.InstallCalls);
        var reviewed = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(saved.Entries.AsEnumerable(), reviewed.Entries.AsEnumerable());
        Assert.Equal(saved.Approvals.AsEnumerable(), reviewed.Approvals.AsEnumerable());
    }

    [Fact]
    public async Task InstallerReportedCancellationRemainsInReviewWhenJobReopens()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime
        {
            Install = _ => Task.FromResult(new PackageInstallResult(PackageInstallStatus.Cancelled)),
        };
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            await workspace.InstallPackageAsync();
        }

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        AssertInterruptedAttempt(saved, JournalState.RequiresReview, JournalResult.Cancelled, JournalCheckpoint.Reconcile);
        await using var reopened = OpenWorkspace(disk, reports, service);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Contains("Cancellation was recorded", reopened.PackageHistoryText, StringComparison.Ordinal);
        Assert.Contains("Review actual installed state", reopened.PackageHistoryText, StringComparison.Ordinal);
        Assert.True(reopened.CanCheckPackageRecovery);
        reopened.SelectedPlanProfile = Assert.Single(reopened.PlanProfiles, option => !option.Profile.SyntheticData);
        await reopened.BuildPlanReviewAsync();
        reopened.PackageOperatorCode = "fixture-retry-operator";
        Assert.False(reopened.CanApprovePlanReview);
        await reopened.InstallPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Equal(saved.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
    }

    private static async Task<JobId> ApproveInitialPackagePlanAsync(AuditWorkspace workspace)
    {
        var jobId = await PrepareInitialPackagePlanAsync(workspace);
        workspace.PackageOperatorCode = "fixture-operator";
        await workspace.ApprovePlanReviewAsync();
        Assert.True(workspace.CanInstallPackage, workspace.PlanReviewStatus);
        return jobId;
    }

    private static async Task<JobId> PrepareInitialPackagePlanAsync(AuditWorkspace workspace)
    {
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(new SanitizationInput(SanitizationState.NoDonorStorage,
            "fixture-operator", DateOnly.FromDateTime(Now.UtcDateTime), "", ConfirmedExternalRecord: true));
        await workspace.StartAssessmentAsync();
        workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => !option.Profile.SyntheticData);
        await workspace.BuildPlanReviewAsync();
        return workspace.Job!.Job.JobId;
    }

    private static void InPackagePreparationWindow(AuditWorkspace workspace, Action<MainWindow, PlanReviewView> check) =>
        InPackageStage(workspace, ShellStage.Prepare, "PlanReviewPanel", check);

    private static void InPackageVerificationWindow(AuditWorkspace workspace, Action<MainWindow, PackageVerificationView> check) =>
        InPackageStage(workspace, ShellStage.Verify, "PackageVerificationPanel", check);

    private static void InPackageStage<T>(AuditWorkspace workspace, ShellStage stage, string viewName,
        Action<MainWindow, T> check) where T : FrameworkElement => ShellTestHost.Run(() =>
    {
        var window = new MainWindow(workspace);
        try
        {
            ShellTestHost.Show(window);
            window.ShowStage(stage);
            ShellTestHost.Drain(window);
            var view = Named<T>(Named<FrameworkElement>(window, "AuditView"), viewName);
            Assert.True(view.IsVisible);
            check(window, view);
        }
        finally { CloseWindow(window); }
    });

    private static async Task<JournalHistory> ReadRecoveryHistoryAsync(AuditTestStore disk, JobId jobId)
    {
        await using var store = await disk.OpenSqliteAsync();
        return Assert.Single(await store.LoadJobJournalsAsync(jobId));
    }

    private static void AssertInterruptedAttempt(JournalHistory journal, JournalState state, JournalResult reason, JournalCheckpoint checkpoint)
    {
        var approval = Assert.Single(journal.Approvals);
        var start = Assert.Single(journal.Entries, entry => entry.Checkpoint == JournalCheckpoint.PackageDispatchIntent);
        var outcome = journal.Entries.Last();
        Assert.Equal(approval.Plan, outcome.Plan);
        Assert.Equal(approval.ApprovalId, start.ApprovalId);
        Assert.Equal(start.ApprovalId, outcome.ApprovalId);
        Assert.Equal(start.CorrelationId, outcome.CorrelationId);
        Assert.Equal(start.ActionId, outcome.ActionId);
        Assert.Equal(JournalState.Started, outcome.PriorState);
        Assert.Equal(state, outcome.State);
        Assert.Equal(reason, outcome.Result);
        Assert.Equal(checkpoint, outcome.Checkpoint);
        Assert.Null(outcome.Verification);
    }

    private static async Task VerifyFreshPackageApprovalAsync(AuditTestStore disk, PilotPackageService service,
        InterruptedPackageRuntime runtime, WorkshopReportStore reports, JobId jobId, JournalHistory saved, FixedClock clock)
    {
        await using var reopened = OpenWorkspace(disk, reports, service, clock);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Contains("cancelled", reopened.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
        Assert.False(reopened.CanInstallPackage);
        Assert.True(reopened.CanRetryPackage);
        await reopened.RetryPackageAsync();
        Assert.Null(reopened.PlanReviewDecision);
        Assert.False(reopened.CanInstallPackage);
        Assert.Equal(saved.Approvals.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Approvals.AsEnumerable());
        await reopened.InstallPackageAsync();
        Assert.Equal(0, runtime.InstallCalls);
        var acquisitions = runtime.Acquisitions;
        var inspections = runtime.Inspections;
        reopened.SelectedPlanProfile = Assert.Single(reopened.PlanProfiles, option => !option.Profile.SyntheticData);
        await reopened.BuildPlanReviewAsync();
        Assert.Equal(acquisitions + 1, runtime.Acquisitions);
        Assert.True(runtime.Inspections > inspections);
        Assert.False(reopened.CanInstallPackage);
        reopened.PackageOperatorCode = "fixture-retry-operator";
        Assert.True(reopened.CanApprovePlanReview, reopened.PlanReviewStatus);
        await reopened.ApprovePlanReviewAsync();
        Assert.True(reopened.CanInstallPackage, reopened.PlanReviewStatus);
        Assert.Equal(0, runtime.InstallCalls);
        await reopened.InstallPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Contains("Independent verification confirmed", reopened.PackageHistoryText, StringComparison.Ordinal);

        var retried = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(2, retried.Approvals.Length);
        Assert.Equal(Assert.Single(saved.Approvals), retried.Approvals[0]);
        Assert.NotEqual(retried.Approvals[0].ApprovalId, retried.Approvals[1].ApprovalId);
        await AssertPackageRetryAsync(disk, jobId, saved, retried.Approvals[1].ApprovalId);
    }

    private static async Task<JournalHistory> AssertPackageRetryAsync(
        AuditTestStore disk, JobId jobId, JournalHistory saved, Guid approvalId)
    {
        var retried = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(saved.Plan.Key, retried.Plan.Key);
        Assert.Equal(saved.Entries.AsEnumerable(), retried.Entries.Take(saved.Entries.Length));
        var started = Assert.Single(retried.Entries.Skip(saved.Entries.Length), entry => entry.State == JournalState.Started);
        Assert.NotEqual(saved.Entries.Last().CorrelationId, started.CorrelationId);
        Assert.Equal(approvalId, started.ApprovalId);
        Assert.Equal(JournalCheckpoint.PackageRetryDispatchIntent, started.Checkpoint);
        Assert.Equal(saved.Entries[^1].EntryId, started.PriorAttemptEntryId);
        Assert.Equal(JournalState.Verified, retried.StateOf(started.ActionId));
        return retried;
    }

    private sealed class InterruptedPackageRuntime : IPilotPackageRuntime
    {
        private volatile bool _installed;
        private int _acquisitions;
        private int _inspections;
        private int _installCalls;
        private int _probeCalls;
        private PackageObservation? _nextObservation;
        private Func<CancellationToken, Task<IPackageArtifact>>? _nextAcquisition;

        public int Acquisitions => Volatile.Read(ref _acquisitions);
        public int Inspections => Volatile.Read(ref _inspections);
        public int InstallCalls => Volatile.Read(ref _installCalls);
        public int ProbeCalls => Volatile.Read(ref _probeCalls);
        public TrackedRecoveryArtifact? LastArtifact { get; private set; }
        public bool Installed { set => _installed = value; }
        public PackageObservation? NextObservation { set => _nextObservation = value; }
        public Func<CancellationToken, Task<IPackageArtifact>>? NextAcquisition { set => _nextAcquisition = value; }
        public Func<CancellationToken, Task<PackageInstallResult>>? Install { get; init; }

        public PackageObservation Inspect()
        {
            Interlocked.Increment(ref _inspections);
            return Interlocked.Exchange(ref _nextObservation, null) ?? (_installed
                ? new(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion)
                : new(PackageObservationStatus.Absent));
        }

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquisitions);
            return Interlocked.Exchange(ref _nextAcquisition, null)?.Invoke(cancellationToken)
                ?? Task.FromResult<IPackageArtifact>(LastArtifact = new TrackedRecoveryArtifact());
        }

        public async Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _installCalls);
            var result = Install is { } apply ? await apply(cancellationToken) : new PackageInstallResult(PackageInstallStatus.Applied);
            _installed = result.Status == PackageInstallStatus.Applied;
            return result;
        }

        public Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _probeCalls);
            return Task.FromResult(PackageVerificationStatus.Passed);
        }
    }

    private sealed class TrackedRecoveryArtifact : IPackageArtifact
    {
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Disposal.TrySetResult();
    }
}
