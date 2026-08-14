namespace ThirdLife.UI.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.UI.AssemblyMarker);

        Assert.Equal("ThirdLife.UI", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.UI", marker.Namespace);
    }
}
