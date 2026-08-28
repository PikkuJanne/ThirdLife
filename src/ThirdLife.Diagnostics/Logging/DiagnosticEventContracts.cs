using System.Reflection;

namespace ThirdLife.Diagnostics.Logging;

public enum DiagnosticEventCode
{
    OperationCompleted,
    OperationFailed,
    DiagnosticRejected,
    UnhandledException,
    RetentionCleanup,
}

public enum DiagnosticComponent
{
    Diagnostics,
    Core,
    Persistence,
    Inventory,
    Packages,
    Verification,
    Reports,
    Ui,
    Broker,
    Actions,
    Policy,
    Catalog,
}

public enum DiagnosticPhase
{
    Collect,
    Normalize,
    Persist,
    Plan,
    Execute,
    Verify,
    Export,
    Cleanup,
    Recover,
}

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public enum DiagnosticOperationType
{
    StartApplication,
    CollectEvidence,
    NormalizeInput,
    PersistEvent,
    VerifyState,
    BuildSupportProjection,
    CleanRetention,
    RecoverState,
}

public enum DiagnosticResultCode
{
    Succeeded,
    DiagnosticRejected,
    DiagnosticUnavailable,
    OperationCancelled,
    OperationTimedOut,
    AccessDenied,
    IoFailure,
    InvalidInput,
    InvalidState,
    UnexpectedFailure,
    CleanupCompleted,
    CleanupFailed,
}

public enum DiagnosticErrorCategory
{
    Cancellation,
    Timeout,
    AccessDenied,
    IoFailure,
    InvalidInput,
    InvalidState,
    UnexpectedFailure,
}

public enum DiagnosticLimitationCode
{
    DurableStateAmbiguous,
    DiagnosticUnavailable,
    InputRejected,
    BoundExceeded,
    CleanupIncomplete,
}

public enum DiagnosticEventFieldName
{
    OperationType,
    ResultCode,
    SanitizedErrorCategory,
    Retryable,
    DurationMs,
    BoundedCount,
    LimitationCode,
}

internal enum SafeMessageKey
{
    OperationCompleted,
    OperationFailed,
    DiagnosticRejected,
    UnexpectedFailure,
    RetentionCleanup,
}

public sealed class DiagnosticCorrelationId
{
    private DiagnosticCorrelationId(string value)
    {
        Value = DiagnosticText.RequireOpaqueIdentifier(value, nameof(value), 64);
    }

    internal string Value { get; }

    public static DiagnosticCorrelationId CreateRandom() => new(Guid.NewGuid().ToString("N"));

    internal static DiagnosticCorrelationId CreateForTesting(string value) => new(value);

    public override string ToString() => Value;
}

internal sealed class DiagnosticEventId
{
    private DiagnosticEventId(string value)
    {
        Value = DiagnosticText.RequireOpaqueIdentifier(value, nameof(value), 64);
    }

    public string Value { get; }

    public static DiagnosticEventId CreateRandom() => new(Guid.NewGuid().ToString("N"));

    public static DiagnosticEventId CreateForTesting(string value) => new(value);
}

internal static class DiagnosticBuildIdentity
{
    public static string Current { get; } = Resolve(typeof(AssemblyMarker).Assembly);

    public static string ValidateHistorical(string value)
    {
        var normalized = DiagnosticText.RequireVersion(value, nameof(value), 64);
        if (normalized.Length is >= 7 and <= 64 && normalized.All(char.IsAsciiHexDigit))
        {
            return normalized;
        }

        var separatorIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        var core = separatorIndex < 0 ? normalized : normalized[..separatorIndex];
        var suffix = separatorIndex < 0 ? null : normalized[(separatorIndex + 1)..];
        var components = core.Split('.');
        if (components.Length is < 3 or > 4 ||
            components.Any(static component =>
                component.Length == 0 || component.Any(static character => !char.IsAsciiDigit(character))) ||
            suffix is not null &&
            (suffix.Length == 0 || suffix.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '.')))
        {
            throw new ArgumentException("The historical diagnostic build identity is not registered.", nameof(value));
        }

        return normalized;
    }

    private static string Resolve(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var candidate = string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString()
            : informational.Split('+', 2)[0];

        return ValidateHistorical(candidate ?? "0.0.0");
    }
}

public sealed class DiagnosticEventField
{
    private DiagnosticEventField(DiagnosticEventFieldName name, object value)
    {
        Name = name;
        Value = value;
    }

    public DiagnosticEventFieldName Name { get; }

    internal object Value { get; }

    public static DiagnosticEventField OperationType(DiagnosticOperationType value) =>
        new(DiagnosticEventFieldName.OperationType, RequireDefined(value, nameof(value)));

    public static DiagnosticEventField ResultCode(DiagnosticResultCode value) =>
        new(DiagnosticEventFieldName.ResultCode, RequireDefined(value, nameof(value)));

    public static DiagnosticEventField SanitizedErrorCategory(DiagnosticErrorCategory value) =>
        new(DiagnosticEventFieldName.SanitizedErrorCategory, RequireDefined(value, nameof(value)));

    public static DiagnosticEventField Retryable(bool value) =>
        new(DiagnosticEventFieldName.Retryable, value);

    internal static DiagnosticEventField DurationMilliseconds(long value)
    {
        RequireBoundedNumber(value, nameof(value));
        return new DiagnosticEventField(DiagnosticEventFieldName.DurationMs, value);
    }

    internal static DiagnosticEventField BoundedCount(long value)
    {
        RequireBoundedNumber(value, nameof(value));
        return new DiagnosticEventField(DiagnosticEventFieldName.BoundedCount, value);
    }

    public static DiagnosticEventField LimitationCode(DiagnosticLimitationCode value) =>
        new(DiagnosticEventFieldName.LimitationCode, RequireDefined(value, nameof(value)));

    private static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The diagnostic enum value is not defined.");
        }

        return value;
    }

    private static void RequireBoundedNumber(long value, string parameterName)
    {
        if (value is < 0 or > 86_400_000)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Diagnostic counts and durations must be between 0 and 86,400,000.");
        }
    }
}

internal static class DiagnosticWireNames
{
    public static string EventCode(DiagnosticEventCode value) => value switch
    {
        DiagnosticEventCode.OperationCompleted => "operation_completed",
        DiagnosticEventCode.OperationFailed => "operation_failed",
        DiagnosticEventCode.DiagnosticRejected => "diagnostic_rejected",
        DiagnosticEventCode.UnhandledException => "unhandled_exception",
        DiagnosticEventCode.RetentionCleanup => "retention_cleanup",
        _ => throw Undefined(value),
    };

    public static string Component(DiagnosticComponent value) => value switch
    {
        DiagnosticComponent.Diagnostics => "diagnostics",
        DiagnosticComponent.Core => "core",
        DiagnosticComponent.Persistence => "persistence",
        DiagnosticComponent.Inventory => "inventory",
        DiagnosticComponent.Packages => "packages",
        DiagnosticComponent.Verification => "verification",
        DiagnosticComponent.Reports => "reports",
        DiagnosticComponent.Ui => "ui",
        DiagnosticComponent.Broker => "broker",
        DiagnosticComponent.Actions => "actions",
        DiagnosticComponent.Policy => "policy",
        DiagnosticComponent.Catalog => "catalog",
        _ => throw Undefined(value),
    };

    public static string Phase(DiagnosticPhase value) => value switch
    {
        DiagnosticPhase.Collect => "collect",
        DiagnosticPhase.Normalize => "normalize",
        DiagnosticPhase.Persist => "persist",
        DiagnosticPhase.Plan => "plan",
        DiagnosticPhase.Execute => "execute",
        DiagnosticPhase.Verify => "verify",
        DiagnosticPhase.Export => "export",
        DiagnosticPhase.Cleanup => "cleanup",
        DiagnosticPhase.Recover => "recover",
        _ => throw Undefined(value),
    };

    public static string Severity(DiagnosticSeverity value) => value switch
    {
        DiagnosticSeverity.Information => "information",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Error => "error",
        _ => throw Undefined(value),
    };

    public static string Operation(DiagnosticOperationType value) => value switch
    {
        DiagnosticOperationType.StartApplication => "start_application",
        DiagnosticOperationType.CollectEvidence => "collect_evidence",
        DiagnosticOperationType.NormalizeInput => "normalize_input",
        DiagnosticOperationType.PersistEvent => "persist_event",
        DiagnosticOperationType.VerifyState => "verify_state",
        DiagnosticOperationType.BuildSupportProjection => "build_support_projection",
        DiagnosticOperationType.CleanRetention => "clean_retention",
        DiagnosticOperationType.RecoverState => "recover_state",
        _ => throw Undefined(value),
    };

    public static string Result(DiagnosticResultCode value) => value switch
    {
        DiagnosticResultCode.Succeeded => "succeeded",
        DiagnosticResultCode.DiagnosticRejected => "diagnostic_rejected",
        DiagnosticResultCode.DiagnosticUnavailable => "diagnostic_unavailable",
        DiagnosticResultCode.OperationCancelled => "operation_cancelled",
        DiagnosticResultCode.OperationTimedOut => "operation_timed_out",
        DiagnosticResultCode.AccessDenied => "access_denied",
        DiagnosticResultCode.IoFailure => "io_failure",
        DiagnosticResultCode.InvalidInput => "invalid_input",
        DiagnosticResultCode.InvalidState => "invalid_state",
        DiagnosticResultCode.UnexpectedFailure => "unexpected_failure",
        DiagnosticResultCode.CleanupCompleted => "cleanup_completed",
        DiagnosticResultCode.CleanupFailed => "cleanup_failed",
        _ => throw Undefined(value),
    };

    public static string ErrorCategory(DiagnosticErrorCategory value) => value switch
    {
        DiagnosticErrorCategory.Cancellation => "cancellation",
        DiagnosticErrorCategory.Timeout => "timeout",
        DiagnosticErrorCategory.AccessDenied => "access_denied",
        DiagnosticErrorCategory.IoFailure => "io_failure",
        DiagnosticErrorCategory.InvalidInput => "invalid_input",
        DiagnosticErrorCategory.InvalidState => "invalid_state",
        DiagnosticErrorCategory.UnexpectedFailure => "unexpected_failure",
        _ => throw Undefined(value),
    };

    public static string Limitation(DiagnosticLimitationCode value) => value switch
    {
        DiagnosticLimitationCode.DurableStateAmbiguous => "durable_state_ambiguous",
        DiagnosticLimitationCode.DiagnosticUnavailable => "diagnostic_unavailable",
        DiagnosticLimitationCode.InputRejected => "input_rejected",
        DiagnosticLimitationCode.BoundExceeded => "bound_exceeded",
        DiagnosticLimitationCode.CleanupIncomplete => "cleanup_incomplete",
        _ => throw Undefined(value),
    };

    public static string Field(DiagnosticEventFieldName value) => value switch
    {
        DiagnosticEventFieldName.OperationType => "operation_type",
        DiagnosticEventFieldName.ResultCode => "result_code",
        DiagnosticEventFieldName.SanitizedErrorCategory => "sanitized_error_category",
        DiagnosticEventFieldName.Retryable => "retryable",
        DiagnosticEventFieldName.DurationMs => "duration_ms",
        DiagnosticEventFieldName.BoundedCount => "bounded_count",
        DiagnosticEventFieldName.LimitationCode => "limitation_code",
        _ => throw Undefined(value),
    };

    public static string MessageKey(SafeMessageKey value) => value switch
    {
        SafeMessageKey.OperationCompleted => "diagnostic.operation_completed",
        SafeMessageKey.OperationFailed => "diagnostic.operation_failed",
        SafeMessageKey.DiagnosticRejected => "diagnostic.rejected",
        SafeMessageKey.UnexpectedFailure => "diagnostic.unexpected_failure",
        SafeMessageKey.RetentionCleanup => "diagnostic.retention_cleanup",
        _ => throw Undefined(value),
    };

    private static ArgumentOutOfRangeException Undefined<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        new(nameof(value), value, $"The {typeof(TEnum).Name} value is not defined.");
}
