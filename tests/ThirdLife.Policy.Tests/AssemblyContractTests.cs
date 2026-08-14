namespace ThirdLife.Policy.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Policy.AssemblyMarker);

        Assert.Equal("ThirdLife.Policy", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Policy", marker.Namespace);
    }
}
