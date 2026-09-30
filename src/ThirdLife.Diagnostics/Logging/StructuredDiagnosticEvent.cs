using System.Text.Json;

namespace ThirdLife.Diagnostics.Logging;

public sealed class StructuredDiagnosticEvent
{
    public const string CurrentSchemaVersion = "thirdlife.diagnostics.event.v1";
    internal const int MaximumSerializedBytes = 16 * 1024;

    private readonly IReadOnlyList<DiagnosticEventField> _fields;

    private StructuredDiagnosticEvent(
        DiagnosticEventId eventId,
        DiagnosticEventCode eventCode,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        DateTimeOffset occurredAtUtc,
        DiagnosticCorrelationId correlationId,
        SafeMessageKey safeMessageKey,
        string buildVersion,
        IReadOnlyList<DiagnosticEventField> fields)
    {
        EventId = eventId;
        EventCode = eventCode;
        Component = component;
        Phase = phase;
        Severity = severity;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        CorrelationId = correlationId;
        SafeMessageKey = safeMessageKey;
        BuildVersion = DiagnosticBuildIdentity.ValidateHistorical(buildVersion);
        _fields = fields;
    }

    internal DiagnosticEventId EventId { get; }

    public DiagnosticEventCode EventCode { get; }

    public DiagnosticComponent Component { get; }

    public DiagnosticPhase Phase { get; }

    public DiagnosticSeverity Severity { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public DiagnosticCorrelationId CorrelationId { get; }

    internal SafeMessageKey SafeMessageKey { get; }

    internal string BuildVersion { get; }

    internal IReadOnlyList<DiagnosticEventField> Fields => _fields;

    public static StructuredDiagnosticEvent Create(
        DiagnosticEventCode eventCode,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        DiagnosticCorrelationId correlationId,
        IEnumerable<DiagnosticEventField> fields)
    {
        return CreateCore(
            eventCode,
            component,
            phase,
            severity,
            correlationId,
            fields,
            DiagnosticEventId.CreateRandom(),
            TimeProvider.System.GetUtcNow(),
            DiagnosticBuildIdentity.Current);
    }

    internal static StructuredDiagnosticEvent CreateForTesting(
        DiagnosticEventCode eventCode,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        DiagnosticCorrelationId correlationId,
        IEnumerable<DiagnosticEventField> fields,
        string eventId,
        DateTimeOffset occurredAtUtc,
        string? buildVersion = null) =>
        CreateCore(
            eventCode,
            component,
            phase,
            severity,
            correlationId,
            fields,
            DiagnosticEventId.CreateForTesting(eventId),
            occurredAtUtc,
            buildVersion ?? DiagnosticBuildIdentity.Current);

    public byte[] GetUtf8Json()
    {
        using var stream = new MemoryStream(capacity: 1024);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", CurrentSchemaVersion);
            writer.WriteString("event_id", EventId.Value);
            writer.WriteString("event_code", DiagnosticWireNames.EventCode(EventCode));
            writer.WriteString("component", DiagnosticWireNames.Component(Component));
            writer.WriteString("phase", DiagnosticWireNames.Phase(Phase));
            writer.WriteString("severity", DiagnosticWireNames.Severity(Severity));
            writer.WriteString("occurred_at_utc", OccurredAtUtc);
            writer.WriteString("correlation_id", CorrelationId.Value);
            writer.WriteString("build_version", BuildVersion);
            writer.WriteString("safe_message_key", DiagnosticWireNames.MessageKey(SafeMessageKey));

            foreach (var field in _fields.OrderBy(static field => (int)field.Name))
            {
                writer.WritePropertyName(DiagnosticWireNames.Field(field.Name));
                WriteFieldValue(writer, field);
            }

            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        if (bytes.Length > MaximumSerializedBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_event_too_large",
                "The structured diagnostic event exceeds its byte bound.");
        }

        return bytes;
    }

    private static StructuredDiagnosticEvent CreateCore(
        DiagnosticEventCode eventCode,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        DiagnosticCorrelationId correlationId,
        IEnumerable<DiagnosticEventField> fields,
        DiagnosticEventId eventId,
        DateTimeOffset occurredAtUtc,
        string buildVersion)
    {
        if (!Enum.IsDefined(eventCode))
        {
            throw new ArgumentOutOfRangeException(nameof(eventCode), eventCode, "The event code is not defined.");
        }

        if (!Enum.IsDefined(component))
        {
            throw new ArgumentOutOfRangeException(nameof(component), component, "The component is not defined.");
        }

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "The phase is not defined.");
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, "The severity is not defined.");
        }

        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(fields);
        if (occurredAtUtc == default)
        {
            throw new ArgumentException("A non-default event timestamp is required.", nameof(occurredAtUtc));
        }

        var materialized = new List<DiagnosticEventField>(capacity: 7);
        foreach (var field in fields)
        {
            if (field is null)
            {
                throw new DiagnosticContractException(
                    "diagnostic_field_null",
                    "The diagnostic event contains a null field.");
            }

            if (materialized.Count == 7)
            {
                throw new DiagnosticContractException(
                    "diagnostic_field_count_exceeded",
                    "The diagnostic event contains too many fields.");
            }

            materialized.Add(field);
        }

        DiagnosticEventCatalog.Validate(eventCode, materialized);
        DiagnosticEventCatalog.ValidateEnvelope(eventCode, component, phase, severity, materialized);
        return new StructuredDiagnosticEvent(
            eventId,
            eventCode,
            component,
            phase,
            severity,
            occurredAtUtc,
            correlationId,
            DiagnosticEventCatalog.GetMessageKey(eventCode),
            buildVersion,
            materialized.AsReadOnly());
    }

    private static void WriteFieldValue(Utf8JsonWriter writer, DiagnosticEventField field)
    {
        switch (field.Name)
        {
            case DiagnosticEventFieldName.OperationType:
                writer.WriteStringValue(DiagnosticWireNames.Operation((DiagnosticOperationType)field.Value));
                break;
            case DiagnosticEventFieldName.ResultCode:
                writer.WriteStringValue(DiagnosticWireNames.Result((DiagnosticResultCode)field.Value));
                break;
            case DiagnosticEventFieldName.SanitizedErrorCategory:
                writer.WriteStringValue(DiagnosticWireNames.ErrorCategory((DiagnosticErrorCategory)field.Value));
                break;
            case DiagnosticEventFieldName.Retryable:
                writer.WriteBooleanValue((bool)field.Value);
                break;
            case DiagnosticEventFieldName.DurationMs:
            case DiagnosticEventFieldName.BoundedCount:
                writer.WriteNumberValue((long)field.Value);
                break;
            case DiagnosticEventFieldName.LimitationCode:
                writer.WriteStringValue(DiagnosticWireNames.Limitation((DiagnosticLimitationCode)field.Value));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field.Name, "The event field is not defined.");
        }
    }
}

internal static class DiagnosticEventCatalog
{
    public static void ValidateEnvelope(
        DiagnosticEventCode eventCode,
        DiagnosticComponent component,
        DiagnosticPhase phase,
        DiagnosticSeverity severity,
        IReadOnlyList<DiagnosticEventField> fields)
    {
        var result = GetValue<DiagnosticResultCode>(fields, DiagnosticEventFieldName.ResultCode);
        var limitation = TryGetValue<DiagnosticLimitationCode>(fields, DiagnosticEventFieldName.LimitationCode);
        var operationEnvelopeValid = eventCode is DiagnosticEventCode.UnhandledException or
            DiagnosticEventCode.RetentionCleanup ||
            IsRegisteredOperationEnvelope(
                GetValue<DiagnosticOperationType>(fields, DiagnosticEventFieldName.OperationType),
                component,
                phase);

        var valid = operationEnvelopeValid && eventCode switch
        {
            DiagnosticEventCode.OperationCompleted =>
                result == DiagnosticResultCode.Succeeded &&
                severity == (limitation is null ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning),
            DiagnosticEventCode.OperationFailed =>
                severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error &&
                IsRegisteredFailureCombination(
                    result,
                    GetValue<DiagnosticErrorCategory>(fields, DiagnosticEventFieldName.SanitizedErrorCategory),
                    GetValue<bool>(fields, DiagnosticEventFieldName.Retryable)),
            DiagnosticEventCode.DiagnosticRejected =>
                result == DiagnosticResultCode.DiagnosticRejected &&
                severity == DiagnosticSeverity.Warning &&
                limitation is DiagnosticLimitationCode.InputRejected or DiagnosticLimitationCode.BoundExceeded,
            DiagnosticEventCode.UnhandledException =>
                severity == DiagnosticSeverity.Error &&
                IsRegisteredFailureCombination(
                    result,
                    GetValue<DiagnosticErrorCategory>(fields, DiagnosticEventFieldName.SanitizedErrorCategory),
                    GetValue<bool>(fields, DiagnosticEventFieldName.Retryable)) &&
                limitation == DiagnosticLimitationCode.DurableStateAmbiguous,
            DiagnosticEventCode.RetentionCleanup =>
                component == DiagnosticComponent.Diagnostics &&
                phase == DiagnosticPhase.Cleanup &&
                IsRegisteredCleanupCombination(result, severity, limitation),
            _ => false,
        };

        if (!valid)
        {
            throw InvalidEnvelope();
        }
    }

    private static bool IsRegisteredFailureCombination(
        DiagnosticResultCode result,
        DiagnosticErrorCategory category,
        bool retryable) => (result, category, retryable) switch
        {
            (DiagnosticResultCode.OperationCancelled, DiagnosticErrorCategory.Cancellation, false) => true,
            (DiagnosticResultCode.OperationTimedOut, DiagnosticErrorCategory.Timeout, true) => true,
            (DiagnosticResultCode.AccessDenied, DiagnosticErrorCategory.AccessDenied, false) => true,
            (DiagnosticResultCode.IoFailure, DiagnosticErrorCategory.IoFailure, true) => true,
            (DiagnosticResultCode.InvalidInput, DiagnosticErrorCategory.InvalidInput, false) => true,
            (DiagnosticResultCode.InvalidState, DiagnosticErrorCategory.InvalidState, false) => true,
            (DiagnosticResultCode.UnexpectedFailure, DiagnosticErrorCategory.UnexpectedFailure, false) => true,
            _ => false,
        };

    private static bool IsRegisteredCleanupCombination(
        DiagnosticResultCode result,
        DiagnosticSeverity severity,
        DiagnosticLimitationCode? limitation) => (result, severity, limitation) switch
        {
            (DiagnosticResultCode.CleanupCompleted, DiagnosticSeverity.Information, null) => true,
            (DiagnosticResultCode.CleanupFailed, DiagnosticSeverity.Warning, DiagnosticLimitationCode.CleanupIncomplete) => true,
            _ => false,
        };

    private static bool IsRegisteredOperationEnvelope(
        DiagnosticOperationType operation,
        DiagnosticComponent component,
        DiagnosticPhase phase) => operation switch
        {
            DiagnosticOperationType.StartApplication =>
                phase == DiagnosticPhase.Execute &&
                component is DiagnosticComponent.Core or DiagnosticComponent.Ui,
            DiagnosticOperationType.CollectEvidence =>
                phase == DiagnosticPhase.Collect &&
                component is DiagnosticComponent.Core or
                    DiagnosticComponent.Inventory or
                    DiagnosticComponent.Packages,
            DiagnosticOperationType.NormalizeInput =>
                phase == DiagnosticPhase.Normalize &&
                component is DiagnosticComponent.Core or
                    DiagnosticComponent.Diagnostics or
                    DiagnosticComponent.Policy or
                    DiagnosticComponent.Catalog,
            DiagnosticOperationType.PersistEvent =>
                phase == DiagnosticPhase.Persist && component == DiagnosticComponent.Diagnostics,
            DiagnosticOperationType.VerifyState =>
                phase == DiagnosticPhase.Verify && component == DiagnosticComponent.Verification,
            DiagnosticOperationType.BuildSupportProjection =>
                phase == DiagnosticPhase.Export &&
                component is DiagnosticComponent.Diagnostics or DiagnosticComponent.Reports,
            DiagnosticOperationType.CleanRetention =>
                phase == DiagnosticPhase.Cleanup && component == DiagnosticComponent.Diagnostics,
            DiagnosticOperationType.RecoverState =>
                phase == DiagnosticPhase.Recover &&
                component is DiagnosticComponent.Core or
                    DiagnosticComponent.Diagnostics or
                    DiagnosticComponent.Persistence,
            _ => false,
        };

    private static T GetValue<T>(
        IReadOnlyList<DiagnosticEventField> fields,
        DiagnosticEventFieldName name) where T : struct =>
        (T)fields.Single(field => field.Name == name).Value;

    private static T? TryGetValue<T>(
        IReadOnlyList<DiagnosticEventField> fields,
        DiagnosticEventFieldName name) where T : struct =>
        fields.SingleOrDefault(field => field.Name == name)?.Value is T value ? value : null;

    public static void Validate(DiagnosticEventCode eventCode, IReadOnlyList<DiagnosticEventField> fields)
    {
        if (fields.Count > 7)
        {
            throw new DiagnosticContractException(
                "diagnostic_field_count_exceeded",
                "The diagnostic event contains too many fields.");
        }

        var names = fields.Select(static field => field.Name).ToArray();
        if (names.Distinct().Count() != names.Length)
        {
            throw new DiagnosticContractException(
                "diagnostic_field_duplicate",
                "The diagnostic event contains a duplicate field.");
        }

        var (required, allowed) = GetContract(eventCode);
        if (names.Any(name => !allowed.Contains(name)) || required.Any(name => !names.Contains(name)))
        {
            throw new DiagnosticContractException(
                "diagnostic_event_contract_invalid",
                "The diagnostic event does not match its registered field contract.");
        }
    }

    public static SafeMessageKey GetMessageKey(DiagnosticEventCode eventCode) => eventCode switch
    {
        DiagnosticEventCode.OperationCompleted => SafeMessageKey.OperationCompleted,
        DiagnosticEventCode.OperationFailed => SafeMessageKey.OperationFailed,
        DiagnosticEventCode.DiagnosticRejected => SafeMessageKey.DiagnosticRejected,
        DiagnosticEventCode.UnhandledException => SafeMessageKey.UnexpectedFailure,
        DiagnosticEventCode.RetentionCleanup => SafeMessageKey.RetentionCleanup,
        _ => throw new ArgumentOutOfRangeException(nameof(eventCode), eventCode, "The event code is not defined."),
    };

    private static (IReadOnlySet<DiagnosticEventFieldName> Required, IReadOnlySet<DiagnosticEventFieldName> Allowed)
        GetContract(DiagnosticEventCode eventCode)
    {
        return eventCode switch
        {
            DiagnosticEventCode.OperationCompleted => Contract(
                [DiagnosticEventFieldName.OperationType, DiagnosticEventFieldName.ResultCode],
                [
                    DiagnosticEventFieldName.OperationType,
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.DurationMs,
                    DiagnosticEventFieldName.BoundedCount,
                ]),
            DiagnosticEventCode.OperationFailed => Contract(
                [
                    DiagnosticEventFieldName.OperationType,
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.SanitizedErrorCategory,
                    DiagnosticEventFieldName.Retryable,
                ],
                [
                    DiagnosticEventFieldName.OperationType,
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.SanitizedErrorCategory,
                    DiagnosticEventFieldName.Retryable,
                    DiagnosticEventFieldName.DurationMs,
                ]),
            DiagnosticEventCode.DiagnosticRejected => Contract(
                [
                    DiagnosticEventFieldName.OperationType,
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.LimitationCode,
                ],
                [
                    DiagnosticEventFieldName.OperationType,
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.LimitationCode,
                    DiagnosticEventFieldName.BoundedCount,
                ]),
            DiagnosticEventCode.UnhandledException => Contract(
                [
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.SanitizedErrorCategory,
                    DiagnosticEventFieldName.Retryable,
                    DiagnosticEventFieldName.LimitationCode,
                ],
                [
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.SanitizedErrorCategory,
                    DiagnosticEventFieldName.Retryable,
                    DiagnosticEventFieldName.LimitationCode,
                ]),
            DiagnosticEventCode.RetentionCleanup => Contract(
                [DiagnosticEventFieldName.ResultCode, DiagnosticEventFieldName.BoundedCount],
                [
                    DiagnosticEventFieldName.ResultCode,
                    DiagnosticEventFieldName.BoundedCount,
                    DiagnosticEventFieldName.LimitationCode,
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(eventCode), eventCode, "The event code is not defined."),
        };
    }

    private static (IReadOnlySet<DiagnosticEventFieldName>, IReadOnlySet<DiagnosticEventFieldName>) Contract(
        DiagnosticEventFieldName[] required,
        DiagnosticEventFieldName[] allowed) =>
        (required.ToHashSet(), allowed.ToHashSet());

    private static DiagnosticContractException InvalidEnvelope() =>
        new(
            "diagnostic_event_envelope_invalid",
            "The diagnostic event envelope does not match its registered semantics.");
}
