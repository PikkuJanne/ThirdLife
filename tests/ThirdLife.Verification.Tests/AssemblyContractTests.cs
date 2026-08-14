namespace ThirdLife.Verification.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Verification.AssemblyMarker);

        Assert.Equal("ThirdLife.Verification", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Verification", marker.Namespace);
    }
}
