namespace ThirdLife.Reports.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Reports.AssemblyMarker);

        Assert.Equal("ThirdLife.Reports", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Reports", marker.Namespace);
    }
}
