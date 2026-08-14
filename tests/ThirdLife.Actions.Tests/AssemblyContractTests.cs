namespace ThirdLife.Actions.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Actions.AssemblyMarker);

        Assert.Equal("ThirdLife.Actions", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Actions", marker.Namespace);
    }
}
