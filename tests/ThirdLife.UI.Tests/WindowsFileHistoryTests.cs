using System.IO;
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
}
