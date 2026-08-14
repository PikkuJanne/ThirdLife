namespace ThirdLife.Packages.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Packages.AssemblyMarker);

        Assert.Equal("ThirdLife.Packages", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Packages", marker.Namespace);
    }
}
