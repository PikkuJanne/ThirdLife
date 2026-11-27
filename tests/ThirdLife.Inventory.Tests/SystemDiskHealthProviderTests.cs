using System.Security.Principal;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class SystemDiskHealthProviderTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, "healthy")]
    [InlineData(1, "warning")]
    [InlineData(2, "unhealthy")]
    public async Task ReportsOnlyTheHealthOfTheMatchedWindowsDisk(ushort code, string status)
    {
        var result = WindowsStorageWmiSource.ResolveSystemDiskHealth(Collected([Disk(12, code)]), 12);
        var provider = new SystemDiskHealthProvider(_ => result, ProviderEvidenceOrigin.SyntheticFixture);

        var evidence = Assert.Single((await provider.ObserveAsync()).Evidence);

        Assert.Equal("storage.system.health_state", evidence.EvidenceKey.Value);
        Assert.Equal("storage.msft.boot_partition_disk", evidence.SourceReference);
        Assert.Equal(EvidenceClassification.Inferred, evidence.EvidenceClassification);
        Assert.Equal(status, evidence.Value?.StringValue);
    }

    [Fact]
    public void TheBootPartitionMustIdentifyExactlyOneWellFormedDiskNumber()
    {
        Assert.Equal(12u, WindowsStorageWmiSource.ResolveSystemPartitionDisk(Collected([Partition(12u)])).GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.InvalidData,
            WindowsStorageWmiSource.ResolveSystemPartitionDisk(Collected([Partition(12u), Partition(12u)])).Status);
        Assert.Equal(SystemInventorySourceStatus.InvalidData,
            WindowsStorageWmiSource.ResolveSystemPartitionDisk(Collected([Partition(12)])).Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable,
            WindowsStorageWmiSource.ResolveSystemPartitionDisk(Collected([])).Status);
    }

    [Fact]
    public void AnUnmatchedOrAmbiguousDiskCannotContributeHealth()
    {
        Assert.Equal(SystemInventorySourceStatus.InvalidData,
            WindowsStorageWmiSource.ResolveSystemDiskHealth(Collected([Disk(11, 0)]), 12).Status);
        Assert.Equal(SystemInventorySourceStatus.InvalidData,
            WindowsStorageWmiSource.ResolveSystemDiskHealth(Collected([Disk(12, 0), Disk(12, 0)]), 12).Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable,
            WindowsStorageWmiSource.ResolveSystemDiskHealth(Collected([Disk(12, 5)]), 12).Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable,
            WindowsStorageWmiSource.ResolveSystemDiskHealth(Collected([]), 12).Status);
        Assert.Equal(SystemInventorySourceStatus.AccessDenied,
            WindowsStorageWmiSource.ResolveSystemDiskHealth(new(new(StorageCollectionStatus.AccessDenied), []), 12).Status);
    }

    [Fact]
    public void QueriesUseOnlyTheBoundedLocalJoin()
    {
        Assert.Equal("SELECT DiskNumber FROM MSFT_Partition WHERE IsBoot = TRUE AND IsOffline = FALSE AND DriveLetter = 'D'",
            WindowsStorageWmiSource.QueryText(StorageWmiQuery.SystemPartition, driveLetter: 'D'));
        Assert.Equal("SELECT Number,HealthStatus FROM MSFT_Disk WHERE Number = 12 AND IsOffline = FALSE AND IsBoot = TRUE",
            WindowsStorageWmiSource.QueryText(StorageWmiQuery.SystemDisk, diskNumber: 12));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsStorageWmiSource.QueryText(StorageWmiQuery.SystemPartition, driveLetter: (char)39));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ObservesTheSystemDiskHealthThroughTheProductionProvider()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        var provider = new SystemDiskHealthProvider();
        var result = await new InventoryProviderRunner().RunAsync(provider);

        var evidence = Assert.Single(result.Observations);
        Assert.Equal("storage.system.health_state", evidence.EvidenceKey.Value);
        Assert.True(evidence.Value?.StringValue is "healthy" or "warning" or "unhealthy",
            $"Expected a classified system disk health observation. Outcome: {result.Outcome}. Limitation: {evidence.LimitationCode}.");
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(EvidenceClassification.Inferred, evidence.Metadata.EvidenceClassification);
        Assert.Equal("storage.msft.boot_partition_disk", evidence.Metadata.Provenance.SourceReference);
        Assert.Equal(PrivacyClassification.WorkshopRestricted, evidence.Metadata.PrivacyClassification);
        output.WriteLine($"Windows system disk health status: {evidence.Value!.StringValue}");
    }

    private static StorageWmiQueryResult Collected(IReadOnlyList<StorageWmiRow> rows) =>
        new(StorageSourceFailures.Collected, rows);

    private static StorageWmiRow Partition(object diskNumber) => new(new Dictionary<string, object?>
    {
        ["DiskNumber"] = diskNumber,
    });

    private static StorageWmiRow Disk(uint number, ushort health) => new(new Dictionary<string, object?>
    {
        ["Number"] = number,
        ["HealthStatus"] = health,
    });
}
