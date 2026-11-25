using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Broker.SecurityTests;

public sealed class BrokerRequestSecurityTests
{
    [Theory]
    [InlineData("executable")]
    [InlineData("command")]
    [InlineData("url")]
    [InlineData("registry_key")]
    public void RejectsExecutionFieldsWithoutDisclosingInput(string field)
    {
        const string fixtureValue = "synthetic-private-request-field";
        var nonce = new BrokerNonce(new string('a', 64));
        var envelope = new BrokerEnvelope(new Guid("93e29b64-d656-45b9-8587-3f0c6a3b67b7"), nonce,
            new DateTimeOffset(2030, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var request = new BrokerRequest(envelope, new JobId("job-broker-security-fixture"),
            new Guid("b9f4b66b-01d3-4562-979d-c432bdaa1d2c"), new string('b', 64), new string('c', 64),
            [ActionRegistry.CreateNoOp(new Guid("5feefc13-1a95-4d47-ac48-3ced26b89ff0"))]);

        var wire = JsonNode.Parse(BrokerProtocolCodec.Serialize(request))!.AsObject();
        wire["actions"]![0]!["parameters"]![field] = fixtureValue;

        var exception = Record.Exception(() =>
        {
            _ = BrokerProtocolCodec.Parse(Encoding.UTF8.GetBytes(wire.ToJsonString()));
        });

        Assert.NotNull(exception);
        var diagnostic = exception.ToString();
        Assert.False(diagnostic.Contains(nonce.Value, StringComparison.Ordinal), "The diagnostic disclosed the fixture nonce.");
        Assert.False(diagnostic.Contains(fixtureValue, StringComparison.Ordinal), "The diagnostic disclosed a fixture field value.");
        Assert.IsType<JsonException>(exception);
        Assert.True(exception.Message == "The message does not satisfy the ThirdLife broker v1 contract.",
            "The rejection must use the fixed broker diagnostic.");
        Assert.True(exception.InnerException is null, "The rejection must omit parser exception details.");
    }
}
