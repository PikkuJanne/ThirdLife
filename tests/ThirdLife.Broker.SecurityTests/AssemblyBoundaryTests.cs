namespace ThirdLife.Broker.SecurityTests;

public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void BrokerAndProtocolAreSeparateAssemblies()
    {
        var brokerAssembly = typeof(global::ThirdLife.Broker.AssemblyMarker).Assembly;
        var protocolAssembly = typeof(global::ThirdLife.Broker.Protocol.AssemblyMarker).Assembly;

        Assert.NotEqual(brokerAssembly, protocolAssembly);
    }
}
