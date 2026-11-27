using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Jobs;
using ThirdLife.Packages;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Theory]
    [InlineData(PreparationPlatform.Windows11X64, 0UL, "Free up space")]
    [InlineData(PreparationPlatform.UnsupportedArchitecture, 32UL << 30, "native x64 Windows")]
    public async Task PackagePreflightStopsSourceDownloadAndExposesTheBlocker(
        PreparationPlatform platform, ulong availableBytes, string expectedReason)
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime),
            observePreparationResources: now => ReadyPreparationResources(now) with
            {
                Platform = platform,
                SystemAvailableBytes = availableBytes,
                StagingAvailableBytes = availableBytes,
            });

        var jobId = await PrepareInitialPackagePlanAsync(workspace);
        workspace.PackageOperatorCode = "fixture-operator";
        Assert.True(workspace.HasPackagePreflightStatus);
        Assert.Contains(expectedReason, workspace.PackagePreflightStatus, StringComparison.Ordinal);
        Assert.Contains("Installed size is uncertain.", workspace.PackagePreflightStatus, StringComparison.Ordinal);
        Assert.False(workspace.CanApprovePlanReview);
        await workspace.ApprovePlanReviewAsync();
        await workspace.InstallPackageAsync();
        Assert.Equal(0, runtime.AcquireCalls);
        Assert.Equal(0, runtime.InstallCalls);
        await using (var store = await disk.OpenSqliteAsync())
        {
            Assert.Empty(await store.LoadJobJournalsAsync(jobId));
        }

        InPackagePreparationWindow(workspace, (_, panel) =>
        {
            var status = Named<TextBlock>(panel, "PackagePreflightMessage");
            Assert.True(status.IsVisible && status.Focusable);
            var peer = new TextBlockAutomationPeer(status);
            Assert.Equal(workspace.PackagePreflightStatus, peer.GetName());
            Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
        });
    }

    [Theory]
    [InlineData(0UL, PreparationPower.ExternalPower)]
    [InlineData(32UL << 30, PreparationPower.CriticallyLowBattery)]
    public async Task PackagePreflightDefersAnApprovedAttemptWithoutSpendingTheApprovalOrStartingAnAttempt(
        ulong availableBytes, PreparationPower power)
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var snapshot = ReadyPreparationResources(Now);
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime),
            observePreparationResources: now => snapshot with { ObservedAtUtc = now });
        var jobId = await ApproveInitialPackagePlanAsync(workspace);
        var approved = await ReadRecoveryHistoryAsync(disk, jobId);
        var initialDownloads = runtime.AcquireCalls;
        Assert.Equal(1, initialDownloads);

        snapshot = snapshot with { SystemAvailableBytes = availableBytes, StagingAvailableBytes = availableBytes, Power = power };
        await workspace.InstallPackageAsync();

        Assert.True(workspace.HasPackagePreflightStatus);
        Assert.Equal(initialDownloads, runtime.AcquireCalls);
        Assert.Equal(0, runtime.InstallCalls);
        var deferred = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(Assert.Single(approved.Approvals), Assert.Single(deferred.Approvals));
        Assert.Equal(Assert.Single(approved.Entries), Assert.Single(deferred.Entries));
        Assert.Equal(JournalState.Approved, deferred.Entries[^1].State);

        snapshot = ReadyPreparationResources(Now);
        Assert.True(workspace.CanInstallPackage, workspace.PlanReviewStatus);
        await workspace.InstallPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        var completed = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(Assert.Single(approved.Approvals), Assert.Single(completed.Approvals));
        Assert.Equal(JournalState.Verified, completed.Entries[^1].State);
    }

    [Fact]
    public async Task PackagePreflightChangeAfterAcquisitionPreventsApplyAndReopensWithTheSavedDeferral()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var service = new PilotPackageService(runtime);
        var snapshot = ReadyPreparationResources(Now);
        var clock = new FixedClock();
        JobId jobId;
        await using (var workspace = OpenWorkspace(disk, reports, service,
            observePreparationResources: now => snapshot with { ObservedAtUtc = now }))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            runtime.Acquiring = () => snapshot = snapshot with { Power = PreparationPower.CriticallyLowBattery };
            await workspace.InstallPackageAsync();

            Assert.Equal(2, runtime.AcquireCalls);
            Assert.Equal(0, runtime.InstallCalls);
            Assert.Equal(0, runtime.ProbeCalls);
            var history = await ReadRecoveryHistoryAsync(disk, jobId);
            Assert.Equal(new[] { JournalState.Approved, JournalState.Started, JournalState.Failed },
                history.Entries.Select(entry => entry.State));
            Assert.Equal(JournalCheckpoint.PackagePreDispatchFailure, history.Entries[^1].Checkpoint);
            Assert.Equal(JournalResult.PackageResourcePreflightDeferred, history.Entries[^1].Result);
            Assert.Equal(history.Entries[^2].CorrelationId, history.Entries[^1].CorrelationId);
        }

        clock.Current = Now.AddSeconds(6);
        await using var reopened = OpenWorkspace(disk, reports, service, clock,
            observePreparationResources: now => snapshot with { ObservedAtUtc = now });
        await reopened.OpenJobAsync(jobId.Value);
        Assert.False(reopened.CanCheckPackageRecovery);
        Assert.False(reopened.CanInstallPackage);
        Assert.Contains("Resource requirements changed before dispatch. Windows Installer did not start.",
            reopened.LatestPackageOutcome, StringComparison.Ordinal);
        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        Assert.Contains(reopened.LatestPackageOutcome, reopened.ReportPreview, StringComparison.Ordinal);
        Assert.Equal(0, runtime.InstallCalls);

        Assert.True(reopened.CanRetryPackage, reopened.PackageRetryStatusText);
        await reopened.RetryPackageAsync();
        var stillDeferred = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(JournalResult.PackageResourcePreflightDeferred, stillDeferred.Entries[^1].Result);
        Assert.Equal(3, stillDeferred.Entries.Length);
        Assert.Equal(2, runtime.AcquireCalls);

        runtime.Acquiring = null;
        snapshot = ReadyPreparationResources(clock.Current);
        await reopened.RetryPackageAsync();
        var retried = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(1, runtime.InstallCalls);
        Assert.Equal(Assert.Single(stillDeferred.Approvals), Assert.Single(retried.Approvals));
        var retryIntent = Assert.Single(retried.Entries, entry => entry.Checkpoint == JournalCheckpoint.PackageRetryDispatchIntent);
        Assert.Equal(stillDeferred.Entries[^1].EntryId, retryIntent.PriorAttemptEntryId);
        Assert.Equal(JournalState.Verified, retried.Entries[^1].State);
    }

    [Fact]
    public async Task PackagePreflightCancelledDuringPlanReviewLeavesDownloadAndJournalUntouched()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var resources = new HeldPreparationObservation();
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime),
            observePreparationResources: resources.Read);

        resources.HoldNext();
        var review = PrepareInitialPackagePlanAsync(workspace);
        await resources.CancelAndReleaseAsync(workspace);
        var jobId = await review;

        Assert.Empty(workspace.OperationError);
        Assert.Contains("cancel", workspace.PackageSourceStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(workspace.CanCancelPackage);
        Assert.Equal(0, runtime.AcquireCalls);
        Assert.Equal(0, runtime.InstallCalls);
        await using (var store = await disk.OpenSqliteAsync())
        {
            Assert.Empty(await store.LoadJobJournalsAsync(jobId));
        }

        await workspace.BuildPlanReviewAsync();
        workspace.PackageOperatorCode = "fixture-operator";
        Assert.True(workspace.CanApprovePlanReview, workspace.PackageSourceStatus);
        Assert.Equal(1, runtime.AcquireCalls);
    }

    [Fact]
    public async Task PackagePreflightCancelledAfterApprovalRetainsTheUnstartedExactApproval()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var resources = new HeldPreparationObservation();
        await using var workspace = OpenWorkspace(disk, reports, new PilotPackageService(runtime),
            observePreparationResources: resources.Read);
        var jobId = await ApproveInitialPackagePlanAsync(workspace);
        var approved = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(1, runtime.AcquireCalls);

        resources.HoldNext();
        var installation = workspace.InstallPackageAsync();
        await resources.CancelAndReleaseAsync(workspace);
        await installation;

        Assert.Empty(workspace.OperationError);
        Assert.Contains("cancel", workspace.PlanReviewStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(workspace.CanCancelPackage);
        Assert.Equal(1, runtime.AcquireCalls);
        Assert.Equal(0, runtime.InstallCalls);
        var cancelled = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(Assert.Single(approved.Approvals), Assert.Single(cancelled.Approvals));
        Assert.Equal(Assert.Single(approved.Entries), Assert.Single(cancelled.Entries));

        Assert.True(workspace.CanInstallPackage, workspace.PlanReviewStatus);
        await workspace.InstallPackageAsync();
        Assert.Equal(1, runtime.InstallCalls);
        var completed = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(Assert.Single(approved.Approvals), Assert.Single(completed.Approvals));
        Assert.Equal(JournalState.Verified, completed.Entries[^1].State);
    }

    private static PreparationResourceSnapshot ReadyPreparationResources(DateTimeOffset observedAt) =>
        new(observedAt, PreparationPlatform.Windows11X64, PreparationPower.ExternalPower, 32UL << 30, 32UL << 30, true);

    private sealed class HeldPreparationObservation
    {
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _holdNext;

        public void HoldNext() => Interlocked.Exchange(ref _holdNext, 1);

        public PreparationResourceSnapshot Read(DateTimeOffset now)
        {
            if (Interlocked.Exchange(ref _holdNext, 0) == 1)
            {
                _entered.TrySetResult();
                _released.Task.WaitAsync(WaitLimit).GetAwaiter().GetResult();
            }

            return ReadyPreparationResources(now);
        }

        public async Task CancelAndReleaseAsync(AuditWorkspace workspace)
        {
            try
            {
                await _entered.Task.WaitAsync(WaitLimit);
                Assert.True(workspace.CanCancelPackage);
                workspace.CancelPackageOperation();
            }
            finally
            {
                _released.TrySetResult();
            }
        }
    }
}
