using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
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
    [Theory]
    [InlineData(PackageObservationStatus.Installed, "8.9.9.1", JournalState.Verified, JournalResult.PackageLaunchVerified)]
    [InlineData(PackageObservationStatus.Absent, null, JournalState.RequiresReview, JournalResult.PackageDetectionAbsent)]
    [InlineData(PackageObservationStatus.Installed, "8.9.9.0", JournalState.RequiresReview, JournalResult.PackageDetectionMismatch)]
    [InlineData(PackageObservationStatus.Conflict, null, JournalState.RequiresReview, JournalResult.PackageDetectionMismatch)]
    [InlineData(PackageObservationStatus.Unavailable, null, JournalState.RequiresReview, JournalResult.PackageDetectionUnavailable)]
    public async Task ReopeningAnAppliedPackageReobservesAndPersistsTheVerificationOutcome(
        PackageObservationStatus status, string? version, JournalState expectedState, JournalResult expectedResult)
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var (jobId, prior) = await SaveRecoveryBoundaryAsync(disk, reports, JournalCheckpoint.PackageLaunchPendingVerification);
        var runtime = new RecoveryObservationRuntime(new PackageObservation(status, version));

        await using var reopened = OpenWorkspace(disk, reports, new PilotPackageService(runtime));
        await reopened.OpenJobAsync(jobId.Value);

        var history = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(prior.Entries.AsEnumerable(), history.Entries.Take(prior.Entries.Length));
        Assert.Equal(prior.Approvals.AsEnumerable(), history.Approvals.AsEnumerable());
        var verified = Assert.Single(history.Entries.Skip(prior.Entries.Length));
        Assert.Equal(JournalState.Applied, verified.PriorState);
        Assert.Equal(JournalActor.Verifier, verified.Actor);
        Assert.Equal(history.Plan.Key, verified.Plan);
        Assert.Equal(prior.Entries[^1].ActionId, verified.ActionId);
        Assert.Equal(prior.Entries[^1].ApprovalId, verified.ApprovalId);
        Assert.Equal(prior.Entries[^1].CorrelationId, verified.CorrelationId);
        Assert.Equal(expectedState, verified.State);
        Assert.Equal(expectedResult, verified.Result);
        Assert.Equal(expectedState == JournalState.Verified ? ActionVerification.ExactPackageIdentity : null, verified.Verification);
        Assert.Equal(1, runtime.Inspections);
        Assert.Equal(expectedState == JournalState.Verified ? 1 : 0, runtime.Probes);
        Assert.False(reopened.CanInstallPackage);

        await reopened.OpenJobAsync(jobId.Value);
        Assert.Equal(history.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
        if (expectedState == JournalState.Verified)
        {
            await reopened.GenerateReportAsync();
            await reopened.OpenReportAsync();
            Assert.Contains("passed the launch check", reopened.ReportPreview, StringComparison.Ordinal);
            Assert.Equal(1, runtime.Inspections);
            Assert.Equal(1, runtime.Probes);
        }
        else
        {
            Assert.True(reopened.CanCheckPackageRecovery);
            Assert.Contains("review", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExactRegistrationAfterLostInstallerOutcomeKeepsTheAttemptInReviewOnEachOpen()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var (jobId, prior) = await SaveRecoveryBoundaryAsync(disk, reports, JournalCheckpoint.PackageDispatchIntent);
        var runtime = new RecoveryObservationRuntime(
            new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion));
        await using var reopened = OpenWorkspace(disk, reports, new PilotPackageService(runtime));

        await reopened.OpenJobAsync(jobId.Value);
        Assert.Equal(1, runtime.Inspections);
        Assert.Contains("review", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(reopened.CanInstallPackage);
        Assert.Equal(0, runtime.Probes);
        Assert.Equal(prior.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());

        runtime.Observation = new PackageObservation(PackageObservationStatus.Absent);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Equal(2, runtime.Inspections);
        Assert.Contains("not found", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(reopened.CanInstallPackage);
        Assert.Equal(0, runtime.Probes);
        Assert.Equal(prior.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
    }

    [Fact]
    public async Task ArchivedAppliedPackageHistoryOpensWithoutAnyVerification()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var (jobId, prior) = await SaveRecoveryBoundaryAsync(disk, reports, JournalCheckpoint.PackageLaunchPendingVerification);
        await using (var store = await disk.OpenSqliteAsync())
        {
            await store.SetArchiveStateAsync(jobId, true, Now.AddMinutes(1));
        }
        var runtime = new RecoveryObservationRuntime(
            new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion));
        await using var reopened = OpenWorkspace(disk, reports, new PilotPackageService(runtime));

        await reopened.OpenJobAsync(jobId.Value);
        Assert.True(reopened.Job!.IsArchived);
        Assert.Contains("installer reported success", reopened.PackageHistoryText, StringComparison.OrdinalIgnoreCase);
        Assert.False(reopened.CanCheckPackageRecovery);
        Assert.False(reopened.CanInstallPackage);
        Assert.Equal(0, runtime.Inspections);
        Assert.Equal(0, runtime.Probes);
        Assert.Equal(prior.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
    }

    [Fact]
    public async Task CancellingResumedVerificationKeepsAnAccessibleReviewResult()
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var (jobId, prior) = await SaveRecoveryBoundaryAsync(disk, reports, JournalCheckpoint.PackageLaunchPendingVerification);
        var runtime = new RecoveryObservationRuntime(
            new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion))
        {
            ObserveLaunch = async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                return PackageVerificationStatus.Passed;
            },
        };
        await using var reopened = OpenWorkspace(disk, reports, new PilotPackageService(runtime));
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(reopened);
            try
            {
                ShellTestHost.Show(window);
                var audit = Named<FrameworkElement>(window, "AuditView");
                var panel = Named<PackageVerificationView>(audit, "PackageVerificationPanel");
                var status = Named<TextBlock>(panel, "PackageRecoveryMessage");
                var cancel = Named<Button>(panel, "CancelPackageRecoveryButton");
                Named<TextBox>(audit, "JobIdInput").Text = jobId.Value;
                Invoke(window, Named<Button>(audit, "OpenJobButton"));
                PumpUntil(window, () => reopened.Job is not null && runtime.Probes == 1);
                window.ShowStage(ShellStage.Verify);
                PumpUntil(window, () => runtime.Probes == 1 && cancel.IsVisible && cancel.IsEnabled);
                var buttonPeer = ShellTestHost.ButtonPeer(cancel);
                Assert.Equal("Cancel package verification", buttonPeer.GetName());
                Assert.True(buttonPeer.IsKeyboardFocusable());
                cancel.BringIntoView();
                Assert.True(cancel.Focus());
                Invoke(window, cancel);
                PumpUntil(window, () => !reopened.IsBusy && status.Text.Contains("verification was cancelled", StringComparison.OrdinalIgnoreCase));
                var statusPeer = new TextBlockAutomationPeer(status);
                Assert.Equal(AutomationLiveSetting.Polite, statusPeer.GetLiveSetting());
                Assert.Contains("review", statusPeer.GetName(), StringComparison.OrdinalIgnoreCase);
                Assert.Same(status, Keyboard.FocusedElement);
                Assert.False(cancel.IsVisible);
                Assert.False(reopened.CanInstallPackage);
            }
            finally { CloseWindow(window); }
        });

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(prior.Entries.AsEnumerable(), saved.Entries.Take(prior.Entries.Length));
        var cancelled = Assert.Single(saved.Entries.Skip(prior.Entries.Length));
        Assert.Equal(JournalState.RequiresReview, cancelled.State);
        Assert.Equal(JournalResult.PackageProbeCancelled, cancelled.Result);
        Assert.Equal(prior.Entries[^1].ApprovalId, cancelled.ApprovalId);
        Assert.Equal(prior.Entries[^1].CorrelationId, cancelled.CorrelationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartRequiredWaitsForWindowsBootEvidenceThenVerifiesTheSavedAttempt(bool expireCheckpoint)
    {
        using var disk = new AuditTestStore();
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var clock = new FixedClock();
        ulong? uptime = (ulong)TimeSpan.FromHours(2).Ticks;
        Task<SqliteJobStore> OpenStore(CancellationToken token = default) =>
            SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion,
                clock, faultInjector: null, readBootUptime: () => uptime, cancellationToken: token);

        var installer = new FixtureInstaller(PackageInstallStatus.RestartRequired, register: true);
        JobId jobId;
        await using (var workspace = OpenRestartWorkspace(installer))
        {
            jobId = await ApproveInitialPackagePlanAsync(workspace);
            await workspace.InstallPackageAsync();
            Assert.Equal(1, installer.InstallCalls);
            Assert.Equal(0, installer.ProbeCalls);
        }

        var prior = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(JournalCheckpoint.RestartRequired, prior.Entries[^1].Checkpoint);
        var observer = new RecoveryObservationRuntime(
            new PackageObservation(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion));
        await using var reopened = OpenRestartWorkspace(observer);
        uptime = null;
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Contains("restart", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, observer.Probes);
        Assert.Empty(reopened.RebootRecoveryChoices);
        Assert.False(reopened.CanInstallPackage);
        Assert.False(reopened.CanRetryPackage);
        Assert.Equal(prior.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
        await using (var accessible = OpenRestartWorkspace(observer))
        {
            await accessible.OpenJobAsync(jobId.Value);
            InPackageVerificationWindow(accessible, (_, panel) =>
            {
                var message = Named<TextBlock>(panel, "PackageRecoveryMessage");
                var peer = new TextBlockAutomationPeer(message);
                Assert.True(message.IsVisible && message.Focusable && KeyboardNavigation.GetIsTabStop(message));
                Assert.Contains("restart", peer.GetName(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            });
        }

        uptime = (ulong)TimeSpan.FromHours(3).Ticks;
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Equal(0, observer.Probes);
        Assert.Equal(prior.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());

        clock.Current = expireCheckpoint ? Now.AddHours(25) : Now.AddMinutes(10);
        uptime = (ulong)TimeSpan.FromMinutes(1).Ticks;
        await reopened.OpenJobAsync(jobId.Value);

        var history = await ReadRecoveryHistoryAsync(disk, jobId);
        Assert.Equal(prior.Entries.AsEnumerable(), history.Entries.Take(prior.Entries.Length));
        Assert.False(reopened.CanInstallPackage);
        Assert.False(reopened.CanRetryPackage);
        if (expireCheckpoint)
        {
            Assert.Contains("expir", reopened.PackageRecoveryStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(prior.Entries.Length, history.Entries.Length);
            Assert.Equal(0, observer.Probes);
        }
        else
        {
            var verification = Assert.Single(history.Entries.Skip(prior.Entries.Length));
            Assert.Equal(JournalState.Verified, verification.State);
            Assert.Equal(JournalResult.PackageLaunchVerified, verification.Result);
            Assert.Equal(prior.Entries[^1].ApprovalId, verification.ApprovalId);
            Assert.Equal(prior.Entries[^1].CorrelationId, verification.CorrelationId);
            Assert.Equal(1, observer.Probes);
            await reopened.OpenJobAsync(jobId.Value);
            Assert.Equal(1, observer.Probes);
            Assert.Equal(history.Entries.AsEnumerable(), (await ReadRecoveryHistoryAsync(disk, jobId)).Entries.AsEnumerable());
        }

        AuditWorkspace OpenRestartWorkspace(IPilotPackageRuntime runtime) =>
            new(async token => await OpenStore(token), [OsSample(), SystemSample()], clock, reports,
                packageService: new PilotPackageService(runtime));
    }

    private static async Task<(JobId JobId, JournalHistory History)> SaveRecoveryBoundaryAsync(
        AuditTestStore disk, WorkshopReportStore reports, JournalCheckpoint checkpoint)
    {
        JobId jobId;
        await using (var original = OpenWorkspace(disk, reports,
            new PilotPackageService(new FixtureInstaller(PackageInstallStatus.Applied, true))))
        {
            jobId = await ApproveInitialPackagePlanAsync(original);
        }

        await using var store = await disk.OpenSqliteAsync();
        var approved = Assert.Single(await store.LoadJobJournalsAsync(jobId));
        var approval = Assert.Single(approved.Approvals);
        var action = Assert.Single(approved.Plan.Actions);
        var started = new JournalEntry(Guid.NewGuid(), approval.Plan, action.ActionId, approval.ApprovalId,
            JournalState.Approved, JournalState.Started, JournalActor.Coordinator, new OperatorId("fixture-coordinator"),
            Guid.NewGuid(), Now, JournalResult.None, JournalCheckpoint.PackageDispatchIntent);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(started));
        if (checkpoint != JournalCheckpoint.PackageDispatchIntent)
        {
            var applied = new JournalEntry(Guid.NewGuid(), approval.Plan, action.ActionId, approval.ApprovalId,
                JournalState.Started, JournalState.Applied, JournalActor.PackageBackend, new OperatorId("fixture-backend"),
                started.CorrelationId, Now, JournalResult.BackendApplied, checkpoint);
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied));
        }

        return (jobId, Assert.Single(await store.LoadJobJournalsAsync(jobId)));
    }

    private sealed class RecoveryObservationRuntime(PackageObservation observation) : IPilotPackageRuntime
    {
        public PackageObservation Observation { get; set; } = observation;
        public Func<CancellationToken, Task<PackageVerificationStatus>>? ObserveLaunch { get; init; }
        public int Inspections { get; private set; }
        public int Probes { get; private set; }

        public PackageObservation Inspect()
        {
            Inspections++;
            return Observation;
        }

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery attempted to acquire an installer.");

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery attempted to apply an installer.");

        public Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Probes++;
            return ObserveLaunch?.Invoke(cancellationToken) ?? Task.FromResult(PackageVerificationStatus.Passed);
        }
    }
}
