namespace ThirdLife.Persistence.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Persistence.AssemblyMarker);

        Assert.Equal("ThirdLife.Persistence", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Persistence", marker.Namespace);
    }
}
