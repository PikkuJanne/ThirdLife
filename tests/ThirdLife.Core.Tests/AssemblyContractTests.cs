namespace ThirdLife.Core.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Core.AssemblyMarker);

        Assert.Equal("ThirdLife.Core", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Core", marker.Namespace);
    }
}
