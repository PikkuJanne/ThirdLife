using System.Text.Json;

namespace ThirdLife.Diagnostics.Logging;

internal static class PersistedDiagnosticRecordValidator
{
    private static readonly HashSet<string> EnvelopeFields =
    [
        "schema_version",
        "event_id",
        "event_code",
        "component",
        "phase",
        "severity",
        "occurred_at_utc",
        "correlation_id",
        "build_version",
        "safe_message_key",
    ];

    public static bool IsCanonical(byte[] bytes, DateTimeOffset expectedTimestamp)
    {
        try
        {
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 3,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.GetProperty("schema_version").GetString() != StructuredDiagnosticEvent.CurrentSchemaVersion ||
                root.GetProperty("occurred_at_utc").GetDateTimeOffset() != expectedTimestamp)
            {
                return false;
            }

            var eventCode = Parse<DiagnosticEventCode>(
                root.GetProperty("event_code").GetString(),
                DiagnosticWireNames.EventCode);
            var component = Parse<DiagnosticComponent>(
                root.GetProperty("component").GetString(),
                DiagnosticWireNames.Component);
            var phase = Parse<DiagnosticPhase>(
                root.GetProperty("phase").GetString(),
                DiagnosticWireNames.Phase);
            var severity = Parse<DiagnosticSeverity>(
                root.GetProperty("severity").GetString(),
                DiagnosticWireNames.Severity);
            var buildVersion = DiagnosticBuildIdentity.ValidateHistorical(
                root.GetProperty("build_version").GetString() ?? string.Empty);
            var fields = new List<DiagnosticEventField>(capacity: 7);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    return false;
                }

                if (EnvelopeFields.Contains(property.Name))
                {
                    continue;
                }

                fields.Add(ParseField(property));
            }

            var recreated = StructuredDiagnosticEvent.CreateForTesting(
                eventCode,
                component,
                phase,
                severity,
                DiagnosticCorrelationId.CreateForTesting(
                    root.GetProperty("correlation_id").GetString() ?? string.Empty),
                fields,
                root.GetProperty("event_id").GetString() ?? string.Empty,
                expectedTimestamp,
                buildVersion);

            return bytes.AsSpan().SequenceEqual(recreated.GetUtf8Json());
        }
        catch (Exception exception) when (exception is JsonException or
            KeyNotFoundException or
            InvalidOperationException or
            ArgumentException or
            DiagnosticContractException)
        {
            return false;
        }
    }

    private static DiagnosticEventField ParseField(JsonProperty property) => property.Name switch
    {
        "operation_type" => DiagnosticEventField.OperationType(
            Parse<DiagnosticOperationType>(property.Value.GetString(), DiagnosticWireNames.Operation)),
        "result_code" => DiagnosticEventField.ResultCode(
            Parse<DiagnosticResultCode>(property.Value.GetString(), DiagnosticWireNames.Result)),
        "sanitized_error_category" => DiagnosticEventField.SanitizedErrorCategory(
            Parse<DiagnosticErrorCategory>(property.Value.GetString(), DiagnosticWireNames.ErrorCategory)),
        "retryable" => DiagnosticEventField.Retryable(property.Value.GetBoolean()),
        "duration_ms" => DiagnosticEventField.DurationMilliseconds(property.Value.GetInt64()),
        "bounded_count" => DiagnosticEventField.BoundedCount(property.Value.GetInt64()),
        "limitation_code" => DiagnosticEventField.LimitationCode(
            Parse<DiagnosticLimitationCode>(property.Value.GetString(), DiagnosticWireNames.Limitation)),
        _ => throw new InvalidOperationException("The persisted diagnostic field is not registered."),
    };

    private static TEnum Parse<TEnum>(string? value, Func<TEnum, string> getWireName)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(value, getWireName(candidate), StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The persisted diagnostic value is not registered.");
    }
}
