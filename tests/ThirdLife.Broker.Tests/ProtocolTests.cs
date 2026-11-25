using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Broker.Tests;

public sealed class ProtocolTests
{
    private const string InvalidMessage = "The message does not satisfy the ThirdLife broker v1 contract.";
    private const string WireExpiry = "2030-06-01T09:00:00.0000000Z";
    private static readonly DateTimeOffset Expires = new(2030, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Correlation = new("6ffea09a-d55c-453b-b21e-d2ee8c2a7d2e");
    private static readonly Guid Approval = new("abc08d16-0409-4583-a9ab-64c87fea7c2a");
    private static readonly Guid FirstAction = new("1310aec5-420b-4d3d-adcf-2bca560f4a79");
    private static readonly Guid SecondAction = new("370103be-1f30-4cd8-b290-f65fc2ec81f7");
    private static readonly Guid OtherId = new("d95c98d9-d2a7-42b8-abe5-aa2486573211");
    private static readonly BrokerNonce Nonce = new(new string('a', 64));
    private static readonly BrokerEnvelope Envelope = new(Correlation, Nonce, Expires);
    private static readonly JobId Job = new("job-protocol-fixture");
    private static readonly string PlanDigest = new('b', 64);
    private static readonly string ImpactDigest = new('c', 64);

    [Fact]
    public void EveryMessageRoundTripsWithTheExactCommonEnvelope()
    {
        (BrokerMessage Message, string Kind)[] messages =
        [
            (new BrokerHello(Envelope), "hello"),
            (new BrokerHelloAck(Envelope), "hello_ack"),
            (MakeRequest(), "request"),
            (new BrokerProgress(Envelope, FirstAction, 1, BrokerProgressStage.Accepted), "progress"),
            (new BrokerResult(Envelope, FirstAction, 2, BrokerResultStatus.Applied), "result"),
            (new BrokerCancel(Envelope, FirstAction), "cancel"),
            (new BrokerError(Envelope, BrokerErrorCode.InvalidMessage), "error"),
        ];

        foreach (var (message, kind) in messages)
        {
            var bytes = BrokerProtocolCodec.Serialize(message);
            Assert.InRange(bytes.Length, 1, BrokerProtocolLimits.MaximumMessageBytes);
            var root = JsonNode.Parse(bytes)!.AsObject();
            Assert.Equal(BrokerProtocolLimits.CurrentVersion, root["protocol_version"]!.GetValue<int>());
            Assert.Equal(kind, root["kind"]!.GetValue<string>());
            Assert.Equal(Correlation.ToString("D"), root["correlation_id"]!.GetValue<string>());
            Assert.Equal(Nonce.Value, root["nonce"]!.GetValue<string>());
            Assert.Equal(WireExpiry, root["expires_at_utc"]!.GetValue<string>());

            var decoded = BrokerProtocolCodec.Parse(bytes);
            Assert.IsType(message.GetType(), decoded);
            Assert.Equal(bytes, BrokerProtocolCodec.Serialize(decoded));
        }
    }

    [Theory]
    [InlineData(BrokerProgressStage.Accepted, "accepted")]
    [InlineData(BrokerProgressStage.Running, "running")]
    public void ProgressCarriesOnlyTheActionSequenceAndStructuredStage(BrokerProgressStage stage, string expected)
    {
        var progress = new BrokerProgress(Envelope, FirstAction, BrokerProtocolLimits.MaximumSequence, stage);
        var wire = ToObject(progress);
        Assert.Equal(FirstAction.ToString("D"), wire["action_id"]!.GetValue<string>());
        Assert.Equal(BrokerProtocolLimits.MaximumSequence, wire["sequence"]!.GetValue<int>());
        Assert.Equal(expected, wire["stage"]!.GetValue<string>());
        Assert.Equal(8, wire.Count);
        Assert.IsType<BrokerProgress>(Parse(wire));
    }

    [Theory]
    [InlineData(BrokerResultStatus.Applied, "applied")]
    [InlineData(BrokerResultStatus.Failed, "failed")]
    [InlineData(BrokerResultStatus.Cancelled, "cancelled")]
    [InlineData(BrokerResultStatus.RequiresReview, "requires_review")]
    public void ResultsCarryAClosedStatusVocabulary(BrokerResultStatus status, string expected)
    {
        var result = new BrokerResult(Envelope, FirstAction, 2, status);
        var wire = ToObject(result);
        Assert.Equal(expected, wire["status"]!.GetValue<string>());
        Assert.Equal(8, wire.Count);
        Assert.IsType<BrokerResult>(Parse(wire));
    }

    [Theory]
    [InlineData(BrokerErrorCode.InvalidMessage, "invalid_message")]
    [InlineData(BrokerErrorCode.Unauthorized, "unauthorized")]
    [InlineData(BrokerErrorCode.Expired, "expired")]
    [InlineData(BrokerErrorCode.Replay, "replay")]
    [InlineData(BrokerErrorCode.InvalidState, "invalid_state")]
    [InlineData(BrokerErrorCode.Unavailable, "unavailable")]
    public void ErrorsCarryOnlyClosedCodes(BrokerErrorCode code, string expected)
    {
        var wire = ToObject(new BrokerError(Envelope, code));
        Assert.Equal(expected, wire["code"]!.GetValue<string>());
        Assert.Equal(6, wire.Count);
        Assert.IsType<BrokerError>(Parse(wire));

        wire["message"] = "private-fixture-marker";
        Reject(wire);
    }

    [Theory]
    [InlineData("protocol_version", "0")]
    [InlineData("protocol_version", "2")]
    [InlineData("protocol_version", "\"1\"")]
    [InlineData("protocol_version", "1.0")]
    [InlineData("kind", "\"unrecognized\"")]
    [InlineData("kind", "1")]
    [InlineData("correlation_id", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("correlation_id", "\"6FFEA09A-D55C-453B-B21E-D2EE8C2A7D2E\"")]
    [InlineData("correlation_id", "\"6ffea09ad55c453bb21ed2ee8c2a7d2e\"")]
    [InlineData("nonce", "\"private-fixture-marker\"")]
    [InlineData("expires_at_utc", "\"2030-06-01T09:00:00Z\"")]
    [InlineData("expires_at_utc", "\"2030-06-01T09:00:00.0000000+00:00\"")]
    [InlineData("expires_at_utc", "\"2030-02-31T09:00:00.0000000Z\"")]
    public void RejectsUnsupportedAndNonCanonicalEnvelopeValues(string field, string json)
    {
        var wire = ToObject(new BrokerHello(Envelope));
        wire[field] = JsonNode.Parse(json);

        Reject(wire);
    }

    [Fact]
    public void RequiresExactFieldsForEachMessageAndNestedTypedAction()
    {
        BrokerMessage[] messages =
        [
            new BrokerHello(Envelope),
            new BrokerHelloAck(Envelope),
            MakeRequest(),
            new BrokerProgress(Envelope, FirstAction, 1, BrokerProgressStage.Accepted),
            new BrokerResult(Envelope, FirstAction, 2, BrokerResultStatus.Applied),
            new BrokerCancel(Envelope, FirstAction),
            new BrokerError(Envelope, BrokerErrorCode.InvalidMessage),
        ];
        foreach (var message in messages)
        {
            var wire = ToObject(message);
            RejectMissingFields(wire, wire);
            wire["unrecognized"] = true;
            Reject(wire);
        }

        var request = ToObject(MakeRequest());
        var action = request["actions"]![0]!.AsObject();
        RejectMissingFields(request, action);
        action["parameters"]!["unrecognized"] = true;
        Reject(request);
    }

    [Theory]
    [InlineData("job_id", "\"../fixture\"")]
    [InlineData("approval_id", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("plan_sha256", "\"short\"")]
    [InlineData("impact_sha256", "null")]
    [InlineData("actions", "[]")]
    [InlineData("actions", "{}")]
    public void RejectsInvalidRequestBindings(string field, string json)
    {
        var wire = ToObject(MakeRequest());
        wire[field] = JsonNode.Parse(json);

        Reject(wire);
    }

    [Fact]
    public void RequestUsesOnlyDistinctBoundedNoOpActions()
    {
        var request = MakeRequest();
        var requestWire = ToObject(request);
        var actions = requestWire["actions"]!.AsArray();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Equal("no_op", action!["type"]!.GetValue<string>()));
        var parsed = Assert.IsType<BrokerRequest>(Parse(requestWire));
        Assert.Equal(
            [FirstAction, SecondAction],
            parsed.Actions.Select(action => Assert.IsType<NoOpActionRequest>(action).ActionId));

        actions[0]!["type"] = "unknown_action";
        Reject(requestWire);
        actions[0]!["type"] = "no_op";
        actions.Add(actions[0]!.DeepClone());
        Reject(requestWire);
        Assert.ThrowsAny<ArgumentException>(() => new BrokerRequest(Envelope, Job, Approval, PlanDigest, ImpactDigest,
            [ActionRegistry.CreateNoOp(FirstAction), ActionRegistry.CreateNoOp(FirstAction)]));

        var install = ActionRegistry.CreateInstallPackage(FirstAction, "fixture-repository", "Fictional.Tests.Package",
            "1.0.0", "Fictional Publisher", ActionScope.Machine, ActionArchitecture.X64);
        Assert.ThrowsAny<ArgumentException>(() => new BrokerRequest(Envelope, Job, Approval, PlanDigest, ImpactDigest, [install]));
        requestWire["actions"] = new JsonArray(JsonNode.Parse(ActionRequestCodec.Serialize(install)));
        Reject(requestWire);
    }

    [Fact]
    public void FramesCarryAtMostFourActionsAndRejectExcessLengthBeforeDecode()
    {
        var actionIds = new[] { FirstAction, SecondAction, Approval, OtherId };
        var largestBatch = new BrokerRequest(Envelope, Job, Approval, PlanDigest, ImpactDigest,
            [.. actionIds.Select(ActionRegistry.CreateNoOp)]);
        var serialized = BrokerProtocolCodec.Serialize(largestBatch);
        Assert.Equal(BrokerProtocolLimits.MaximumActions,
            Assert.IsType<BrokerRequest>(BrokerProtocolCodec.Parse(serialized)).Actions.Length);
        var oversizedBatch = ToObject(largestBatch);
        oversizedBatch["actions"]!.AsArray().Add(JsonNode.Parse(ActionRequestCodec.Serialize(ActionRegistry.CreateNoOp(Correlation))));
        Reject(oversizedBatch);

        var atLimit = Encoding.UTF8.GetString(serialized)
            + new string(' ', BrokerProtocolLimits.MaximumMessageBytes - serialized.Length);
        Assert.IsType<BrokerRequest>(BrokerProtocolCodec.Parse(Encoding.UTF8.GetBytes(atLimit)));
        Reject(Encoding.UTF8.GetBytes(atLimit + " "));
    }

    [Theory]
    [InlineData("sequence", "0")]
    [InlineData("sequence", "33")]
    [InlineData("sequence", "\"1\"")]
    [InlineData("sequence", "1.5")]
    [InlineData("stage", "\"complete\"")]
    [InlineData("action_id", "\"00000000-0000-0000-0000-000000000000\"")]
    public void ProgressRejectsOutOfRangeAndUntypedValues(string field, string json)
    {
        var wire = ToObject(new BrokerProgress(Envelope, FirstAction, 1, BrokerProgressStage.Accepted));
        wire[field] = JsonNode.Parse(json);

        Reject(wire);
    }

    [Fact]
    public void RejectsInvalidResultAndErrorCodes()
    {
        var result = ToObject(new BrokerResult(Envelope, FirstAction, 1, BrokerResultStatus.Applied));
        result["status"] = "verified";
        Reject(result);

        var error = ToObject(new BrokerError(Envelope, BrokerErrorCode.Expired));
        error["code"] = 1;
        Reject(error);
    }

    [Fact]
    public void ValidatesTheNonceAndShortExpiryWindow()
    {
        Assert.Matches("^[0-9a-f]{64}$", BrokerNonce.Create().Value);
        Assert.DoesNotContain(Nonce.Value, Nonce.ToString(), StringComparison.Ordinal);
        Assert.ThrowsAny<ArgumentException>(() => new BrokerNonce(new string('A', 64)));
        Assert.ThrowsAny<ArgumentException>(() => new BrokerNonce(new string('a', 63)));
        Assert.ThrowsAny<ArgumentException>(() => new BrokerNonce(new string('a', 65)));

        var now = Expires.AddMinutes(-1);
        Assert.False(BrokerProtocolLimits.IsWithinWindow(now, now));
        Assert.False(BrokerProtocolLimits.IsWithinWindow(now.AddTicks(-1), now));
        Assert.True(BrokerProtocolLimits.IsWithinWindow(Expires, now));
        Assert.True(BrokerProtocolLimits.IsWithinWindow(now + BrokerProtocolLimits.MaximumLifetime, now));
        Assert.False(BrokerProtocolLimits.IsWithinWindow(now + BrokerProtocolLimits.MaximumLifetime + TimeSpan.FromTicks(1), now));
    }

    [Fact]
    public void CanonicalRequestSha256CoversAllBindingsAndActionOrder()
    {
        var request = MakeRequest();
        var bytes = BrokerProtocolCodec.Serialize(request);
        var digest = BrokerProtocolCodec.ComputeRequestSha256(request);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), digest);
        var pretty = ToObject(request).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var decoded = Assert.IsType<BrokerRequest>(BrokerProtocolCodec.Parse(Encoding.UTF8.GetBytes(pretty)));
        Assert.Equal(digest, BrokerProtocolCodec.ComputeRequestSha256(decoded));

        BrokerRequest[] changes =
        [
            MakeRequest(new BrokerEnvelope(OtherId, Nonce, Expires)),
            MakeRequest(new BrokerEnvelope(Correlation, new BrokerNonce(new string('d', 64)), Expires)),
            MakeRequest(new BrokerEnvelope(Correlation, Nonce, Expires.AddTicks(1))),
            MakeRequest(job: new JobId("job-other-fixture")),
            MakeRequest(approval: OtherId),
            MakeRequest(plan: new string('e', 64)),
            MakeRequest(impact: new string('f', 64)),
            new(Envelope, Job, Approval, PlanDigest, ImpactDigest, [ActionRegistry.CreateNoOp(FirstAction)]),
            new(Envelope, Job, Approval, PlanDigest, ImpactDigest, [ActionRegistry.CreateNoOp(SecondAction), ActionRegistry.CreateNoOp(FirstAction)]),
        ];
        Assert.All(changes, changed => Assert.NotEqual(digest, BrokerProtocolCodec.ComputeRequestSha256(changed)));
    }

    [Fact]
    public void RejectsDuplicatePropertiesDeepInputsAndMalformedUtf8WithOneSanitizedError()
    {
        var bytes = BrokerProtocolCodec.Serialize(MakeRequest());
        var request = Encoding.UTF8.GetString(bytes);
        const string duplicate = """
            "\u0070rotocol_version":1,
            """;
        Reject(Encoding.UTF8.GetBytes(request.Insert(1, duplicate)));
        Reject(Encoding.UTF8.GetBytes(request.Replace("\"parameters\":{}", """
            "parameters":{"fixture":[[[[[]]]]]}
            """, StringComparison.Ordinal)));
        Reject(Encoding.UTF8.GetBytes(request.Replace("\"parameters\":{}", """
            "\u0070arameters":{},"parameters":{}
            """, StringComparison.Ordinal)));

        var invalidUtf8 = Encoding.UTF8.GetBytes(request.Replace(Job.Value, "~", StringComparison.Ordinal));
        invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'~')] = 0xFF;
        Reject(invalidUtf8);
        Reject(Array.Empty<byte>());
    }

    [Fact]
    public void BoundedSeededInvalidInputsAlwaysUseTheSanitizedDecoderError()
    {
        var random = new Random(3_110);
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var input = new byte[random.Next(1, 257)];
            random.NextBytes(input);
            // An array prefix guarantees this fixture cannot become a valid broker object.
            input[0] = (byte)'[';
            Reject(input);
        }
    }

    private static BrokerRequest MakeRequest(
        BrokerEnvelope? envelope = null, JobId? job = null, Guid? approval = null, string? plan = null, string? impact = null) =>
        new(envelope ?? Envelope, job ?? Job, approval ?? Approval, plan ?? PlanDigest, impact ?? ImpactDigest,
            [ActionRegistry.CreateNoOp(FirstAction), ActionRegistry.CreateNoOp(SecondAction)]);

    private static JsonObject ToObject(BrokerMessage message) => JsonNode.Parse(BrokerProtocolCodec.Serialize(message))!.AsObject();

    private static BrokerMessage Parse(JsonObject wire) => BrokerProtocolCodec.Parse(Encoding.UTF8.GetBytes(wire.ToJsonString()));

    private static void Reject(JsonObject wire) => Reject(Encoding.UTF8.GetBytes(wire.ToJsonString()));

    private static void Reject(byte[] bytes)
    {
        var error = Assert.Throws<JsonException>(() => BrokerProtocolCodec.Parse(bytes));
        Assert.Equal(InvalidMessage, error.Message);
    }

    private static void RejectMissingFields(JsonObject root, JsonObject selected)
    {
        foreach (var field in selected.Select(property => property.Key).ToArray())
        {
            var value = selected[field];
            selected.Remove(field);
            Reject(root);
            selected[field] = value;
        }
    }
}
