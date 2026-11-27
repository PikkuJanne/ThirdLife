using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Packages.WindowsUpdates;
using ThirdLife.UI.Shell;
using Xunit.Abstractions;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed class WindowsUpdateInventoryTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineReopensConfiguredUpdateInventoryWithoutServicing()
    {
        var native = new WindowsUpdateAgent();
        var scan = await native.ScanAsync(CancellationToken.None);
        Assert.Equal(WindowsUpdateStatus.Succeeded, scan.Status);
        Assert.NotNull(scan.EligibilityReasons);
        var selectable = scan.Updates.Count(WindowsUpdatePolicy.IsSelectable);
        output.WriteLine($"Offered {scan.Updates.Count} revisions, selectable {selectable}.");
        var exact = scan.Updates.FirstOrDefault(WindowsUpdatePolicy.IsSelectable)
            ?? scan.Updates.FirstOrDefault(update => update.Classification != WindowsUpdateClass.Driver)
            ?? (scan.Updates.Count == 0 ? null : scan.Updates[0]);
        if (exact is not null)
        {
            var observation = await native.AssessPreparationAsync(exact, CancellationToken.None);
            Assert.Equal(exact, observation.Exact);
            output.WriteLine($"Exact {exact.Classification} lookup status={observation.Status}, reason={observation.EligibilityReason}, " +
                $"servicingSupported={observation.ServicingSupported}, powerReady={observation.PowerReady}, restart={observation.RebootRequired}, " +
                $"systemObserved={observation.SystemAvailableBytes.HasValue}, cacheObserved={observation.CacheAvailableBytes.HasValue}, " +
                $"sharedVolume={observation.CacheSharesSystemVolume}, recommendedBytes={observation.RecommendedInstallationBytes}, " +
                $"recommendedSpaceMet={observation.HasRecommendedSpace}, removalSupported={observation.RemovalSupported?.ToString() ?? "Unknown"},");
            if (observation.Status == WindowsUpdateStatus.Succeeded)
            {
                Assert.NotNull(observation.EligibilityReason);
                Assert.NotNull(observation.RebootRequired);
            }
            else
            {
                Assert.Null(observation.HasRecommendedSpace);
                Assert.Null(observation.RemovalSupported);
            }
        }
        await VerifySavedInventoryAsync(scan);
        output.WriteLine("Reopened the captured native inventory; scan records=2, approvals=0, dispatches=0.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAndUnsupportedOffersReopenWithTheirDistinctFindings(bool restricted)
    {
        var offer = new WindowsUpdateCandidate(new(Guid.NewGuid(), 4), WindowsUpdateClass.Restricted, 4096);
        await VerifySavedInventoryAsync(new(WindowsUpdateStatus.Succeeded, restricted ? [offer] : [], false,
            restricted ? [WindowsUpdateEligibilityReason.UnsupportedHandler] : []));
    }

    private static async Task VerifySavedInventoryAsync(WindowsUpdateScan scan)
    {
        // The input is captured before creating the synthetic intake. The test job only reads
        // that inventory, preserving the separation between fixture intake and live servicing.
        using var disk = new AuditTestStore();
        var captured = new CapturedInventory(scan);
        AuditWorkspace Open() => new(async token => await disk.OpenSqliteAsync(token),
            [AuditTestProvider.Available("updates")], TimeProvider.System, fixtureWindowsUpdates: captured);
        string id;
        string inventory;
        await using (var prepare = Open())
        {
            await prepare.CreateJobAsync();
            id = prepare.JobId;
            await prepare.RecordSanitizationAsync(VerifiedSample());
            await prepare.ScanWindowsUpdatesAsync();
            inventory = prepare.WindowsUpdateInventory;
            Assert.False(prepare.ShowsUpdateApproval);
            Assert.False(prepare.CanRunApprovedUpdate);
        }
        await using var reopened = Open();
        await reopened.OpenJobAsync(id);
        Assert.Equal(1, captured.Reads);
        Assert.NotEmpty(inventory);
        Assert.Equal(inventory, reopened.WindowsUpdateInventory);
        Assert.False(reopened.CanApproveWindowsUpdate);
        Assert.False(reopened.CanRunApprovedUpdate);
        if (scan.Updates.Count == 0 && scan.RebootRequired == false)
        {
            Assert.Contains("offered no applicable", reopened.WindowsUpdateStatusText);
        }
        if (scan.Updates.Count > 0 && !scan.Updates.Any(WindowsUpdatePolicy.IsSelectable))
        {
            Assert.Contains("None met the servicing requirements", inventory);
            Assert.DoesNotContain("offered no applicable", reopened.WindowsUpdateStatusText);
            if (scan.EligibilityReasons!.Contains(WindowsUpdateEligibilityReason.UnsupportedHandler))
            {
                Assert.Contains("required component-based servicing handler", inventory);
            }
        }
        await using var db = await disk.OpenSqliteAsync();
        var history = await db.LoadWindowsUpdatesAsync(new JobId(id));
        Assert.Equal([WindowsUpdateStage.ScanStarted, WindowsUpdateStage.ScanCompleted], history.Select(entry => entry.Stage));
        Assert.All(history, entry => Assert.Null(entry.Approval));
        var saved = Assert.IsType<WindowsUpdateScan>(history[^1].Scan);
        Assert.Equal(scan.Status, saved.Status);
        Assert.Equal(scan.RebootRequired, saved.RebootRequired);
        Assert.Equal(scan.Updates.ToArray(), saved.Updates.ToArray());
        Assert.Equal(scan.EligibilityReasons!.ToArray(), saved.EligibilityReasons!.ToArray());
    }

    private sealed class CapturedInventory(WindowsUpdateScan scan) : IWindowsUpdateAgent
    {
        public int Reads { get; private set; }
        public Task<WindowsUpdateScan> ScanAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult(scan);
        }
        public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken token) =>
            throw new InvalidOperationException("Read-only inventory cannot download.");
        public Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken token) =>
            throw new InvalidOperationException("Read-only inventory cannot install.");
    }
}
