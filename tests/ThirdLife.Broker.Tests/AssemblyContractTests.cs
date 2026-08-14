namespace ThirdLife.Broker.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void BrokerAssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Broker.AssemblyMarker);

        Assert.Equal("ThirdLife.Broker", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Broker", marker.Namespace);
    }

    [Fact]
    public void ProtocolAssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Broker.Protocol.AssemblyMarker);

        Assert.Equal("ThirdLife.Broker.Protocol", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Broker.Protocol", marker.Namespace);
    }
}
