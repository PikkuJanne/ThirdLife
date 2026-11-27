using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class WindowsUpdateWorkspaceTests
{
    private static readonly WindowsUpdateCandidate SecurityUpdate = new(
        new WindowsUpdateIdentity(Guid.Parse("fca157ca-19df-4c66-a8fa-d4e51fca97bb"), 3), WindowsUpdateClass.Security, 4096);
    private static readonly WindowsUpdateCandidate DriverUpdate = new(
        new WindowsUpdateIdentity(Guid.Parse("3eb39a15-46bf-4b84-bda7-52da5463485d"), 1), WindowsUpdateClass.Driver, 256);

    [Fact]
    public async Task RestrictedScanRequiresIntakeAndPreservesLastSuccessfulInventoryAfterOfflineReopen()
    {
        using var disk = new AuditTestStore();
        var agent = new ScanFixture();
        string id;
        await using (var workspace = Open(disk, agent))
        {
            await workspace.CreateJobAsync();
            id = workspace.JobId;
            Assert.False(workspace.CanScanWindowsUpdates);
            await workspace.ScanWindowsUpdatesAsync();
            Assert.Equal(0, agent.Calls);
            await workspace.RecordSanitizationAsync(VerifiedSample());
            Assert.True(workspace.CanScanWindowsUpdates);
            await workspace.ScanWindowsUpdatesAsync();
            Assert.Contains(SecurityUpdate.Identity.UpdateId.ToString("D"), workspace.WindowsUpdateInventory);
            Assert.Contains("revision 3", workspace.WindowsUpdateInventory);
            await workspace.ScanWindowsUpdatesAsync();
            Assert.Contains("network connection", workspace.WindowsUpdateStatusText);
            Assert.Contains("Security", workspace.WindowsUpdateInventory);
            Assert.False(workspace.CanCheckUpdateOffer);
            workspace.CloseJob();
            Assert.False(workspace.HasWindowsUpdateHistory);
            Assert.Empty(workspace.WindowsUpdateInventory);
        }

        await using var reopened = Open(disk, agent);
        await reopened.OpenJobAsync(id);
        Assert.Contains("network connection", reopened.WindowsUpdateStatusText);
        Assert.Contains(SecurityUpdate.Identity.UpdateId.ToString("D"), reopened.WindowsUpdateInventory);
        Assert.False(reopened.CanCheckUpdateOffer);
        Assert.Equal(2, agent.Calls);
    }

    [Fact]
    public async Task KeyboardAccessibleScanCanBeCancelledAndResultIsVisibleInVerify()
    {
        using var disk = new AuditTestStore();
        var agent = new ScanFixture(wait: true);
        await using var workspace = Open(disk, agent);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 680, Height = 460 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Prepare);
                var view = Named<FrameworkElement>(window, "AuditView");
                var prepare = Named<PlanReviewView>(view, "PlanReviewPanel");
                var scan = Named<Button>(prepare, "ScanWindowsUpdatesButton");
                scan.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.Equal("Scan for Windows updates", ShellTestHost.ButtonPeer(scan).GetName());
                Assert.True(scan.Focus(), $"Visible={scan.IsVisible}, enabled={scan.IsEnabled}, scan available={workspace.CanScanWindowsUpdates}");
                Invoke(window, scan);
                PumpUntil(window, () => agent.Calls == 1 && workspace.CanCancelWindowsUpdate);
                var cancel = Named<Button>(prepare, "CancelWindowsUpdateButton");
                Assert.True(cancel.Focus());
                Invoke(window, cancel);
                PumpUntil(window, () => !workspace.IsBusy);
                var message = Named<TextBlock>(prepare, "WindowsUpdateMessage");
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(message));
                Assert.Contains("Scan cancelled", message.Text);
                window.ShowStage(ShellStage.Verify);
                ShellTestHost.Drain(window);
                var verify = Named<PackageVerificationView>(view, "PackageVerificationPanel");
                var result = Named<TextBlock>(verify, "SavedUpdateResult");
                Assert.True(result.IsVisible && result.Focusable);
                Assert.Equal(message.Text, result.Text);
            }
            finally { CloseWindow(window); }
        });
    }

    [Fact]
    public async Task OperatorCanCheckExactSavedOffersFromPrepareWithoutApprovalOrMutation()
    {
        using var disk = new AuditTestStore();
        var agent = new ScanFixture();
        await using var workspace = Open(disk, agent);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.ScanWindowsUpdatesAsync();
        var jobId = new JobId(workspace.JobId);
        workspace.CloseJob();
        await workspace.OpenJobAsync(jobId.Value);
        Assert.Empty(agent.Checked);
        Assert.Empty(workspace.WindowsUpdatePreview);
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 680, Height = 460 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Prepare);
                ShellTestHost.Drain(window);
                var prepare = Named<PlanReviewView>(Named<FrameworkElement>(window, "AuditView"), "PlanReviewPanel");
                var picker = Named<ComboBox>(prepare, "UpdateOfferPicker");
                picker.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.True(picker.Focus());
                Assert.Equal("Update to check", AutomationProperties.GetName(picker));
                Assert.Equal(2, picker.Items.Count);
                var check = Named<Button>(prepare, "CheckUpdateOfferButton");
                Assert.Equal("Check selected offer online", ShellTestHost.ButtonPeer(check).GetName());
                Assert.True(check.Focus());
                Invoke(window, check);
                PumpUntil(window, () => !workspace.IsBusy && workspace.HasWindowsUpdatePreview);
                var result = Named<TextBlock>(prepare, "UpdateOfferPreview");
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(result));
                Assert.True(result.IsVisible && result.Focusable);
                Assert.Contains("revision 3", result.Text);
                Assert.Contains("machine cache", result.Text);
                Assert.Contains("independent check", result.Text);
                Assert.Contains("Meets the Windows Update space recommendation", result.Text);

                picker.SelectedIndex = 1;
                ShellTestHost.Drain(window);
                Assert.False(workspace.HasWindowsUpdatePreview);
                Invoke(window, check);
                PumpUntil(window, () => !workspace.IsBusy && workspace.HasWindowsUpdatePreview);
                Assert.Contains("driver", result.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("machine cache", result.Text);
                Assert.Equal([SecurityUpdate, DriverUpdate], agent.Checked);
            }
            finally { CloseWindow(window); }
        });

        await using var store = await disk.OpenSqliteAsync();
        var saved = await store.LoadWindowsUpdatesAsync(jobId);
        Assert.Equal([WindowsUpdateStage.ScanStarted, WindowsUpdateStage.ScanCompleted], saved.Select(entry => entry.Stage));
        await using var reopened = Open(disk, agent);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Empty(reopened.WindowsUpdatePreview);
        Assert.Equal(2, agent.Checked.Count);
        await reopened.CheckUpdateOfferAsync();
        Assert.True(reopened.HasWindowsUpdatePreview);
        await reopened.RecordSanitizationAsync(VerifiedSample());
        Assert.False(reopened.HasWindowsUpdatePreview);
        Assert.Empty(reopened.WindowsUpdatePreview);
        Assert.False(reopened.CanCheckUpdateOffer);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("rescan")]
    [InlineData("cancel")]
    public async Task InflightCheckCannotReportResultsForAnObsoleteOrCancelledOffer(string scenario)
    {
        using var disk = new AuditTestStore();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new ScanFixture { HoldCheck = hold.Task, ChangedRevision = scenario == "changed" };
        await using var workspace = Open(disk, agent);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.ScanWindowsUpdatesAsync();
        var check = workspace.CheckUpdateOfferAsync();
        try
        {
            await agent.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (scenario == "cancel") { workspace.CancelWindowsUpdate(); }
            if (scenario == "rescan")
            {
                await using var other = Open(disk, new ScanFixture());
                await other.OpenJobAsync(workspace.JobId);
                await other.ScanWindowsUpdatesAsync();
            }
        }
        finally { hold.TrySetResult(); }
        await check.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = scenario switch { "changed" => "exact offer changed", "rescan" => "Reopen this job", _ => "cancelled" };
        Assert.Contains(expected, workspace.WindowsUpdatePreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine cache", workspace.WindowsUpdatePreview);
        if (scenario == "rescan") { Assert.False(workspace.CanCheckUpdateOffer); }
    }

    private static AuditWorkspace Open(AuditTestStore disk, ScanFixture agent) =>
        new(async token => await disk.OpenSqliteAsync(token), [AuditTestProvider.Available("updates")],
            TimeProvider.System, fixtureWindowsUpdates: agent);

    private sealed class ScanFixture(bool wait = false) : IWindowsUpdateAgent
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public List<WindowsUpdateCandidate> Checked { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task HoldCheck { get; init; } = Task.CompletedTask;
        public bool ChangedRevision { get; init; }

        public async Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken)
        {
            var calls = Interlocked.Increment(ref _calls);
            if (wait) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            return calls == 1 ? new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [SecurityUpdate, DriverUpdate], false)
                : new WindowsUpdateScan(WindowsUpdateStatus.Offline, [], null);
        }

        public async Task<WindowsUpdatePreparation> AssessPreparationAsync(WindowsUpdateCandidate exact, CancellationToken token)
        {
            Checked.Add(exact);
            Started.TrySetResult();
            await HoldCheck.WaitAsync(token);
            var observed = ChangedRevision ? new WindowsUpdateCandidate(new(exact.Identity.UpdateId, exact.Identity.Revision + 1),
                exact.Classification, exact.MaximumDownloadBytes) : exact;
            return new(observed, WindowsUpdateStatus.Succeeded, DateTimeOffset.UtcNow, false, true, true,
                65_536, 65_536, true, 8192, exact.Classification == WindowsUpdateClass.Driver
                    ? WindowsUpdateEligibilityReason.Driver : WindowsUpdateEligibilityReason.Eligible);
        }

        public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A scan cannot dispatch downloads.");
        public Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A scan cannot dispatch installations.");
    }
}
