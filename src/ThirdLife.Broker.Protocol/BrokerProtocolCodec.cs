using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Broker.Protocol;

/// <summary>Encodes a bounded, closed v1 message without granting session or dispatch authority.</summary>
public static class BrokerProtocolCodec
{
    private const string InvalidMessage = "The message does not satisfy the ThirdLife broker v1 contract.";
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static BrokerMessage Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is < 1 or > BrokerProtocolLimits.MaximumMessageBytes || payload.Span.StartsWith("\uFEFF"u8))
        {
            throw new JsonException(InvalidMessage);
        }

        try
        {
            var ownedBytes = payload.ToArray();
            _ = StrictUtf8.GetCharCount(ownedBytes);
            using var document = JsonDocument.Parse(ownedBytes, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = BrokerProtocolLimits.MaximumJsonDepth,
            });
            return ReadMessage(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            // Bounded inputs and fixed error text keep decoder or wire details out of logs and replies.
            throw new JsonException(InvalidMessage);
        }
    }

    public static byte[] Serialize(BrokerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            var envelope = message.Envelope;
            writer.WriteStartObject();
            writer.WriteNumber("protocol_version", envelope.ProtocolVersion);
            writer.WriteString("kind", KindName(message.Kind));
            writer.WriteString("correlation_id", envelope.CorrelationId);
            writer.WriteString("nonce", envelope.Nonce.Value);
            writer.WriteString("expires_at_utc", envelope.ExpiresAtUtc.ToString(UtcFormat, CultureInfo.InvariantCulture));
            WriteBody(writer, message);
            writer.WriteEndObject();
        }

        return buffer.WrittenCount <= BrokerProtocolLimits.MaximumMessageBytes
            ? buffer.WrittenSpan.ToArray()
            : throw new JsonException(InvalidMessage);
    }

    /// <summary>Hashes the exact canonical request, including its session context and ordered actions.</summary>
    public static string ComputeRequestSha256(BrokerRequest request) =>
        Convert.ToHexStringLower(SHA256.HashData(Serialize(request)));

    private static BrokerMessage ReadMessage(JsonElement root)
    {
        if (!root.GetProperty("protocol_version").TryGetInt32(out var version) || version != BrokerProtocolLimits.CurrentVersion)
        {
            throw new JsonException();
        }

        var envelope = new BrokerEnvelope(ReadGuid(root, "correlation_id"), new BrokerNonce(ReadString(root, "nonce")),
            ReadUtc(root));
        var kind = ReadString(root, "kind");
        switch (kind)
        {
            case "hello":
                RequireFields(root, []);
                return new BrokerHello(envelope);
            case "hello_ack":
                RequireFields(root, []);
                return new BrokerHelloAck(envelope);
            case "request":
                RequireFields(root, ["job_id", "approval_id", "plan_sha256", "impact_sha256", "actions"]);
                return new BrokerRequest(envelope, new JobId(ReadString(root, "job_id")), ReadGuid(root, "approval_id"),
                    ReadString(root, "plan_sha256"), ReadString(root, "impact_sha256"), ReadActions(root.GetProperty("actions")));
            case "progress":
                RequireFields(root, ["action_id", "sequence", "stage"]);
                var stage = ReadString(root, "stage") switch
                {
                    "accepted" => BrokerProgressStage.Accepted,
                    "running" => BrokerProgressStage.Running,
                    _ => throw new JsonException(),
                };
                return new BrokerProgress(envelope, ReadGuid(root, "action_id"), ReadInt32(root, "sequence"), stage);
            case "result":
                RequireFields(root, ["action_id", "sequence", "status"]);
                var status = ReadString(root, "status") switch
                {
                    "applied" => BrokerResultStatus.Applied,
                    "failed" => BrokerResultStatus.Failed,
                    "cancelled" => BrokerResultStatus.Cancelled,
                    "requires_review" => BrokerResultStatus.RequiresReview,
                    _ => throw new JsonException(),
                };
                return new BrokerResult(envelope, ReadGuid(root, "action_id"), ReadInt32(root, "sequence"), status);
            case "cancel":
                RequireFields(root, ["action_id"]);
                return new BrokerCancel(envelope, ReadGuid(root, "action_id"));
            case "error":
                RequireFields(root, ["code"]);
                var code = ReadString(root, "code") switch
                {
                    "invalid_message" => BrokerErrorCode.InvalidMessage,
                    "unauthorized" => BrokerErrorCode.Unauthorized,
                    "expired" => BrokerErrorCode.Expired,
                    "replay" => BrokerErrorCode.Replay,
                    "invalid_state" => BrokerErrorCode.InvalidState,
                    "unavailable" => BrokerErrorCode.Unavailable,
                    _ => throw new JsonException(),
                };
                return new BrokerError(envelope, code);
            default:
                throw new JsonException();
        }
    }

    private static void WriteBody(Utf8JsonWriter writer, BrokerMessage message)
    {
        switch (message)
        {
            case BrokerHello or BrokerHelloAck:
                break;
            case BrokerRequest request:
                writer.WriteString("job_id", request.JobId.Value);
                writer.WriteString("approval_id", request.ApprovalId);
                writer.WriteString("plan_sha256", request.PlanSha256);
                writer.WriteString("impact_sha256", request.ImpactSha256);
                writer.WriteStartArray("actions");
                foreach (var action in request.Actions)
                {
                    writer.WriteRawValue(ActionRequestCodec.Serialize(action));
                }

                writer.WriteEndArray();
                break;
            case BrokerProgress progress:
                writer.WriteString("action_id", progress.ActionId);
                writer.WriteNumber("sequence", progress.Sequence);
                writer.WriteString("stage", progress.Stage switch
                {
                    BrokerProgressStage.Accepted => "accepted",
                    BrokerProgressStage.Running => "running",
                    _ => throw new JsonException(InvalidMessage),
                });
                break;
            case BrokerResult result:
                writer.WriteString("action_id", result.ActionId);
                writer.WriteNumber("sequence", result.Sequence);
                writer.WriteString("status", result.Status switch
                {
                    BrokerResultStatus.Applied => "applied",
                    BrokerResultStatus.Failed => "failed",
                    BrokerResultStatus.Cancelled => "cancelled",
                    BrokerResultStatus.RequiresReview => "requires_review",
                    _ => throw new JsonException(InvalidMessage),
                });
                break;
            case BrokerCancel cancel:
                writer.WriteString("action_id", cancel.ActionId);
                break;
            case BrokerError error:
                writer.WriteString("code", error.Code switch
                {
                    BrokerErrorCode.InvalidMessage => "invalid_message",
                    BrokerErrorCode.Unauthorized => "unauthorized",
                    BrokerErrorCode.Expired => "expired",
                    BrokerErrorCode.Replay => "replay",
                    BrokerErrorCode.InvalidState => "invalid_state",
                    BrokerErrorCode.Unavailable => "unavailable",
                    _ => throw new JsonException(InvalidMessage),
                });
                break;
            default:
                throw new JsonException(InvalidMessage);
        }
    }

    private static ImmutableArray<ActionRequest> ReadActions(JsonElement list)
    {
        var count = list.GetArrayLength();
        if (count is < 1 or > BrokerProtocolLimits.MaximumActions)
        {
            throw new JsonException();
        }

        var actions = ImmutableArray.CreateBuilder<ActionRequest>(count);
        foreach (var action in list.EnumerateArray())
        {
            actions.Add(ActionRequestCodec.Parse(StrictUtf8.GetBytes(action.GetRawText())));
        }

        return actions.MoveToImmutable();
    }

    private static void RequireFields(JsonElement root, ReadOnlySpan<string> bodyFields)
    {
        var count = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("protocol_version" or "kind" or "correlation_id" or "nonce" or "expires_at_utc") &&
                !bodyFields.Contains(property.Name))
            {
                throw new JsonException();
            }

            count++;
        }

        if (count != 5 + bodyFields.Length)
        {
            throw new JsonException();
        }
    }

    private static Guid ReadGuid(JsonElement root, string name)
    {
        var value = ReadString(root, name);
        return value.Length == 36 && Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty &&
            value == id.ToString("D") ? id : throw new JsonException();
    }

    private static DateTimeOffset ReadUtc(JsonElement root)
    {
        var text = ReadString(root, "expires_at_utc");
        return text.Length == 28 && DateTimeOffset.TryParseExact(text, UtcFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) &&
            text == result.ToString(UtcFormat, CultureInfo.InvariantCulture) ? result : throw new JsonException();
    }

    private static int ReadInt32(JsonElement root, string name) =>
        root.GetProperty(name).TryGetInt32(out var value) ? value : throw new JsonException();

    private static string ReadString(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new JsonException();

    private static string KindName(BrokerMessageKind kind) => kind switch
    {
        BrokerMessageKind.Hello => "hello",
        BrokerMessageKind.HelloAck => "hello_ack",
        BrokerMessageKind.Request => "request",
        BrokerMessageKind.Progress => "progress",
        BrokerMessageKind.Result => "result",
        BrokerMessageKind.Cancel => "cancel",
        BrokerMessageKind.Error => "error",
        _ => throw new JsonException(InvalidMessage),
    };
}
