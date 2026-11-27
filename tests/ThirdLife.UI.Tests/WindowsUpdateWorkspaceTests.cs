using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
            workspace.CloseJob();
            Assert.False(workspace.HasWindowsUpdateHistory);
            Assert.Empty(workspace.WindowsUpdateInventory);
        }

        await using var reopened = Open(disk, agent);
        await reopened.OpenJobAsync(id);
        Assert.Contains("network connection", reopened.WindowsUpdateStatusText);
        Assert.Contains(SecurityUpdate.Identity.UpdateId.ToString("D"), reopened.WindowsUpdateInventory);
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
                PumpUntil(window, () => agent.Calls == 1 && workspace.CanCancelWindowsUpdateScan);
                var cancel = Named<Button>(prepare, "CancelWindowsUpdateScanButton");
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

    private static AuditWorkspace Open(AuditTestStore disk, ScanFixture agent) =>
        new(async token => await disk.OpenSqliteAsync(token), [AuditTestProvider.Available("updates")],
            TimeProvider.System, fixtureWindowsUpdates: agent);

    private sealed class ScanFixture(bool wait = false) : IWindowsUpdateAgent
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public async Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken)
        {
            var calls = Interlocked.Increment(ref _calls);
            if (wait) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            return calls == 1 ? new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [SecurityUpdate], false)
                : new WindowsUpdateScan(WindowsUpdateStatus.Offline, [], null);
        }

        public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A scan cannot dispatch downloads.");
        public Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A scan cannot dispatch installations.");
    }
}
