namespace ThirdLife.Inventory.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Inventory.AssemblyMarker);

        Assert.Equal("ThirdLife.Inventory", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Inventory", marker.Namespace);
    }
}
