namespace ThirdLife.Catalog.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Catalog.AssemblyMarker);

        Assert.Equal("ThirdLife.Catalog", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Catalog", marker.Namespace);
    }
}
