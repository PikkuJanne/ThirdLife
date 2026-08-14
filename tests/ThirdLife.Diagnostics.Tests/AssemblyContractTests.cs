namespace ThirdLife.Diagnostics.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Diagnostics.AssemblyMarker);

        Assert.Equal("ThirdLife.Diagnostics", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Diagnostics", marker.Namespace);
    }
}
