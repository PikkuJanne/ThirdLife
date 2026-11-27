using System.IO;
using ThirdLife.Core.Handover;
using ThirdLife.UI.Features.Finalization;

namespace ThirdLife.UI.Tests;

public sealed class WindowsFileHistoryTests
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ReadsNativeRouteAvailabilityAndRejectsTheSystemVolume()
    {
        Assert.True(WindowsFileHistoryInventory.RouteAvailable());
        var system = Assert.IsType<string>(Path.GetPathRoot(Environment.SystemDirectory));
        var inventory = new WindowsFileHistoryInventory();
        Assert.Null(inventory.InspectDestination(system));
        Assert.Null(inventory.InspectDestination(Path.Combine(system, "Windows")));
        Assert.Null(inventory.InspectDestination("network-share"));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public void ReadsOnlyTheExplicitlySelectedLocalBackupVolume()
    {
        var selected = Environment.GetEnvironmentVariable("THIRDLIFE_FILE_HISTORY_TEST_VOLUME");
        Assert.False(string.IsNullOrWhiteSpace(selected), "Specify the exact test volume root for this observation.");
        var inventory = new WindowsFileHistoryInventory();
        var first = Assert.IsType<FileHistoryDestinationObservation>(inventory.InspectDestination(selected));
        var second = Assert.IsType<FileHistoryDestinationObservation>(inventory.InspectDestination(selected));
        Assert.True(first.Eligible);
        Assert.NotEqual(Guid.Empty, first.VolumeBinding);
        Assert.Equal(first.VolumeBinding, second.VolumeBinding);
        Assert.True(first.AvailableBytes > 0);
        Assert.True(second.AvailableBytes > 0);
    }
}
