using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThirdLife.Diagnostics.Redaction;

public enum SupportFieldName
{
    SchemaVersion,
    ManifestVersion,
    InternalSupportId,
    ApplicationVersion,
    BuildVersion,
    OsVersion,
    HardwareArchitecture,
    MemoryBucket,
    StorageClass,
    EventTimeUtc,
    ExportCreatedAtUtc,
    CheckId,
    CheckOutcome,
    ActionCode,
    ResultCode,
    ComponentId,
    OperationType,
    EvidenceState,
    SanitizedErrorCategory,
    Retryable,
    DurationMs,
    BoundedCount,
    LimitationCode,
    PreviewContentDigestSha256,
    ExportContentDigestSha256,
}

public enum SupportValueKind
{
    Code,
    Version,
    OpaqueIdentifier,
    Timestamp,
    Boolean,
    NonNegativeInteger,
    ResourceBucket,
    Sha256Digest,
}

internal sealed class SupportValue
{
    private SupportValue(SupportValueKind kind, object value)
    {
        Kind = kind;
        Value = value;
    }

    public SupportValueKind Kind { get; }

    internal object Value { get; }

    public static SupportValue Code(string value) =>
        new(SupportValueKind.Code, DiagnosticText.RequireCode(value, nameof(value)));

    public static SupportValue Version(string value) =>
        new(SupportValueKind.Version, DiagnosticText.RequireVersion(value, nameof(value)));

    public static SupportValue OpaqueIdentifier(string value) =>
        new(
            SupportValueKind.OpaqueIdentifier,
            DiagnosticText.RequireOpaqueIdentifier(value, nameof(value)));

    public static SupportValue Timestamp(DateTimeOffset value)
    {
        if (value == default)
        {
            throw new ArgumentException("A non-default timestamp is required.", nameof(value));
        }

        return new SupportValue(SupportValueKind.Timestamp, value.ToUniversalTime());
    }

    public static SupportValue Boolean(bool value) => new(SupportValueKind.Boolean, value);

    public static SupportValue NonNegativeInteger(long value)
    {
        if (value is < 0 or > SupportFieldCatalog.MaximumBoundedNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"The value must be between 0 and {SupportFieldCatalog.MaximumBoundedNumber}.");
        }

        return new SupportValue(SupportValueKind.NonNegativeInteger, value);
    }

    public static SupportValue ResourceBucket(string value) =>
        new(
            SupportValueKind.ResourceBucket,
            DiagnosticText.RequireResourceBucket(value, nameof(value)));

    public static SupportValue Sha256Digest(string value) =>
        new(
            SupportValueKind.Sha256Digest,
            DiagnosticText.RequireDigest(value, nameof(value)));

    public override string ToString() => "[SAFE:TYPED-DIAGNOSTIC-VALUE]";
}

internal sealed class SupportField
{
    public SupportField(SupportFieldName name, SupportValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SupportFieldCatalog.Validate(name, value);
        Name = name;
        Value = value;
    }

    public SupportFieldName Name { get; }

    public SupportValue Value { get; }
}

public sealed class SupportProjection
{
    private readonly byte[] _utf8Json;

    private SupportProjection(byte[] utf8Json)
    {
        _utf8Json = utf8Json;
        ContentDigestSha256 = Convert.ToHexStringLower(SHA256.HashData(utf8Json));
    }

    public string ContentDigestSha256 { get; }

    public int ByteCount => _utf8Json.Length;

    internal static SupportProjection Create(IEnumerable<SupportField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var materialized = new List<SupportField>(SupportFieldCatalog.AllFields.Length);
        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (materialized.Count == SupportFieldCatalog.AllFields.Length)
            {
                throw new DiagnosticContractException(
                    "support_field_count_exceeded",
                    "The sanitized support projection contains too many fields.");
            }

            materialized.Add(field);
        }

        var distinctNames = materialized.Select(static field => field.Name).Distinct().Count();
        if (distinctNames != materialized.Count)
        {
            throw new DiagnosticContractException(
                "support_field_duplicate",
                "The sanitized support projection contains a duplicate field.");
        }

        var sorted = materialized
            .OrderBy(static field => SupportFieldCatalog.GetOrder(field.Name))
            .ToArray();

        using var stream = new MemoryStream(capacity: 1024);
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var field in sorted)
            {
                writer.WritePropertyName(SupportFieldCatalog.GetWireName(field.Name));
                SupportFieldCatalog.WriteValue(writer, field.Value);
            }

            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        if (bytes.Length > SupportFieldCatalog.MaximumProjectionBytes)
        {
            throw new DiagnosticContractException(
                "support_projection_too_large",
                "The sanitized support projection exceeds its byte bound.");
        }

        return new SupportProjection(bytes);
    }

    public byte[] GetUtf8Json() => (byte[])_utf8Json.Clone();
}

internal static class SupportFieldCatalog
{
    public const long MaximumBoundedNumber = 86_400_000;
    public const int MaximumProjectionBytes = 16 * 1024;

    public static readonly SupportFieldName[] AllFields =
    [
        SupportFieldName.SchemaVersion,
        SupportFieldName.ManifestVersion,
        SupportFieldName.InternalSupportId,
        SupportFieldName.ApplicationVersion,
        SupportFieldName.BuildVersion,
        SupportFieldName.OsVersion,
        SupportFieldName.HardwareArchitecture,
        SupportFieldName.MemoryBucket,
        SupportFieldName.StorageClass,
        SupportFieldName.EventTimeUtc,
        SupportFieldName.ExportCreatedAtUtc,
        SupportFieldName.CheckId,
        SupportFieldName.CheckOutcome,
        SupportFieldName.ActionCode,
        SupportFieldName.ResultCode,
        SupportFieldName.ComponentId,
        SupportFieldName.OperationType,
        SupportFieldName.EvidenceState,
        SupportFieldName.SanitizedErrorCategory,
        SupportFieldName.Retryable,
        SupportFieldName.DurationMs,
        SupportFieldName.BoundedCount,
        SupportFieldName.LimitationCode,
        SupportFieldName.PreviewContentDigestSha256,
        SupportFieldName.ExportContentDigestSha256,
    ];

    public static void Validate(SupportFieldName field, SupportValue value)
    {
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "The support field is not defined.");
        }

        var expectedKind = GetExpectedKind(field);
        if (value.Kind != expectedKind)
        {
            throw new DiagnosticContractException(
                "support_field_type_invalid",
                "The sanitized support field uses the wrong value family.");
        }

        var redactionField = RedactionFieldCatalog.Parse(GetWireName(field));
        if (!IsRegisteredValue(redactionField, value.Value))
        {
            throw new DiagnosticContractException(
                "support_field_value_unregistered",
                "The sanitized support field value is not registered for that field.");
        }
    }

    public static bool IsRegisteredValue(RedactionField field, object? value) => field switch
    {
        RedactionField.SchemaVersion => value is "thirdlife.support.synthetic.v1",
        RedactionField.ManifestVersion => value is "thirdlife.support-manifest.synthetic.v1",
        RedactionField.InternalSupportId => value is "SUP-SYNTHETIC-0001",
        RedactionField.ApplicationVersion => value is "0.0.0-synthetic",
        RedactionField.BuildVersion => value is "synthetic-build-0001",
        RedactionField.OsVersion => value is "SYNTHETIC-OS-VERSION",
        RedactionField.HardwareArchitecture => value is "x64" or "arm64" or "x86",
        RedactionField.MemoryBucket => value is "8-15 GiB",
        RedactionField.StorageClass => value is "ssd" or "hdd" or "nvme" or "emmc" or "unknown",
        RedactionField.EventTimeUtc => IsRegisteredTimestamp(
            value,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        RedactionField.ExportCreatedAtUtc => IsRegisteredTimestamp(
            value,
            new DateTimeOffset(2030, 1, 1, 0, 1, 0, TimeSpan.Zero)),
        RedactionField.CheckId => value is "synthetic_storage_capacity_check",
        RedactionField.CheckOutcome => value is "passed" or "failed" or "not_available" or "unknown",
        RedactionField.ActionCode => value is "synthetic_observe_only",
        RedactionField.ResultCode => value is "provider_unavailable" or "synthetic_success",
        RedactionField.ComponentId => value is "synthetic_storage_observer",
        RedactionField.OperationType => value is "synthetic_observe_storage",
        RedactionField.EvidenceState => value is "observed" or "inferred" or "not_available" or "human_confirmed",
        RedactionField.SanitizedErrorCategory => value is "provider_timeout",
        RedactionField.Retryable => value is bool,
        RedactionField.DurationMs or RedactionField.BoundedCount => value is long,
        RedactionField.LimitationCode => value is "capability_not_present",
        RedactionField.PreviewContentDigestSha256 or RedactionField.ExportContentDigestSha256 =>
            value is "00347ba72e5a15b003127444a084fc2db1c15ea1199ccfb4cfb40c732cc4562d",
        _ => false,
    };

    public static bool TryValidateFixtureValue(
        RedactionField field,
        object? value,
        out object? normalized)
    {
        normalized = null;
        if (!TryMap(field, out var supportField))
        {
            return false;
        }

        try
        {
            SupportValue typedValue;
            switch (GetExpectedKind(supportField))
            {
                case SupportValueKind.Code when value is string code:
                    typedValue = SupportValue.Code(code);
                    normalized = code;
                    break;
                case SupportValueKind.Version when value is string version:
                    typedValue = SupportValue.Version(version);
                    normalized = version;
                    break;
                case SupportValueKind.OpaqueIdentifier when value is string identifier:
                    typedValue = SupportValue.OpaqueIdentifier(identifier);
                    normalized = identifier;
                    break;
                case SupportValueKind.Timestamp when value is string timestamp &&
                    IsCanonicalUtcTimestamp(
                        DiagnosticText.RequireAsciiText(timestamp, nameof(value), 64),
                        out var parsedTimestamp):
                    typedValue = SupportValue.Timestamp(parsedTimestamp);
                    normalized = timestamp;
                    break;
                case SupportValueKind.Boolean when value is bool boolean:
                    typedValue = SupportValue.Boolean(boolean);
                    normalized = boolean;
                    break;
                case SupportValueKind.NonNegativeInteger when TryConvertInteger(value, out var number):
                    typedValue = SupportValue.NonNegativeInteger(number);
                    normalized = number;
                    break;
                case SupportValueKind.ResourceBucket when value is string bucket:
                    typedValue = SupportValue.ResourceBucket(bucket);
                    normalized = bucket;
                    break;
                case SupportValueKind.Sha256Digest when value is string digest:
                    typedValue = SupportValue.Sha256Digest(digest);
                    normalized = digest;
                    break;
                default:
                    return false;
            }

            Validate(supportField, typedValue);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = null;
            return false;
        }
        catch (DiagnosticContractException)
        {
            normalized = null;
            return false;
        }
    }

    public static bool TryMap(RedactionField field, out SupportFieldName supportField)
    {
        supportField = field switch
        {
            RedactionField.SchemaVersion => SupportFieldName.SchemaVersion,
            RedactionField.ManifestVersion => SupportFieldName.ManifestVersion,
            RedactionField.InternalSupportId => SupportFieldName.InternalSupportId,
            RedactionField.ApplicationVersion => SupportFieldName.ApplicationVersion,
            RedactionField.BuildVersion => SupportFieldName.BuildVersion,
            RedactionField.OsVersion => SupportFieldName.OsVersion,
            RedactionField.HardwareArchitecture => SupportFieldName.HardwareArchitecture,
            RedactionField.MemoryBucket => SupportFieldName.MemoryBucket,
            RedactionField.StorageClass => SupportFieldName.StorageClass,
            RedactionField.EventTimeUtc => SupportFieldName.EventTimeUtc,
            RedactionField.ExportCreatedAtUtc => SupportFieldName.ExportCreatedAtUtc,
            RedactionField.CheckId => SupportFieldName.CheckId,
            RedactionField.CheckOutcome => SupportFieldName.CheckOutcome,
            RedactionField.ActionCode => SupportFieldName.ActionCode,
            RedactionField.ResultCode => SupportFieldName.ResultCode,
            RedactionField.ComponentId => SupportFieldName.ComponentId,
            RedactionField.OperationType => SupportFieldName.OperationType,
            RedactionField.EvidenceState => SupportFieldName.EvidenceState,
            RedactionField.SanitizedErrorCategory => SupportFieldName.SanitizedErrorCategory,
            RedactionField.Retryable => SupportFieldName.Retryable,
            RedactionField.DurationMs => SupportFieldName.DurationMs,
            RedactionField.BoundedCount => SupportFieldName.BoundedCount,
            RedactionField.LimitationCode => SupportFieldName.LimitationCode,
            RedactionField.PreviewContentDigestSha256 => SupportFieldName.PreviewContentDigestSha256,
            RedactionField.ExportContentDigestSha256 => SupportFieldName.ExportContentDigestSha256,
            _ => default,
        };

        return field is >= RedactionField.SchemaVersion and <= RedactionField.ExportContentDigestSha256;
    }

    public static string GetWireName(SupportFieldName field) => field switch
    {
        SupportFieldName.SchemaVersion => "schema_version",
        SupportFieldName.ManifestVersion => "manifest_version",
        SupportFieldName.InternalSupportId => "internal_support_id",
        SupportFieldName.ApplicationVersion => "application_version",
        SupportFieldName.BuildVersion => "build_version",
        SupportFieldName.OsVersion => "os_version",
        SupportFieldName.HardwareArchitecture => "hardware_architecture",
        SupportFieldName.MemoryBucket => "memory_bucket",
        SupportFieldName.StorageClass => "storage_class",
        SupportFieldName.EventTimeUtc => "event_time_utc",
        SupportFieldName.ExportCreatedAtUtc => "export_created_at_utc",
        SupportFieldName.CheckId => "check_id",
        SupportFieldName.CheckOutcome => "check_outcome",
        SupportFieldName.ActionCode => "action_code",
        SupportFieldName.ResultCode => "result_code",
        SupportFieldName.ComponentId => "component_id",
        SupportFieldName.OperationType => "operation_type",
        SupportFieldName.EvidenceState => "evidence_state",
        SupportFieldName.SanitizedErrorCategory => "sanitized_error_category",
        SupportFieldName.Retryable => "retryable",
        SupportFieldName.DurationMs => "duration_ms",
        SupportFieldName.BoundedCount => "bounded_count",
        SupportFieldName.LimitationCode => "limitation_code",
        SupportFieldName.PreviewContentDigestSha256 => "preview_content_digest_sha256",
        SupportFieldName.ExportContentDigestSha256 => "export_content_digest_sha256",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "The support field is not defined."),
    };

    public static int GetOrder(SupportFieldName field)
    {
        var index = Array.IndexOf(AllFields, field);
        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(field), field, "The support field is not defined.");
    }

    public static void WriteValue(Utf8JsonWriter writer, SupportValue value)
    {
        switch (value.Kind)
        {
            case SupportValueKind.Code:
            case SupportValueKind.Version:
            case SupportValueKind.OpaqueIdentifier:
            case SupportValueKind.ResourceBucket:
            case SupportValueKind.Sha256Digest:
                writer.WriteStringValue((string)value.Value);
                break;
            case SupportValueKind.Timestamp:
                writer.WriteStringValue(FormatCanonicalUtcTimestamp((DateTimeOffset)value.Value));
                break;
            case SupportValueKind.Boolean:
                writer.WriteBooleanValue((bool)value.Value);
                break;
            case SupportValueKind.NonNegativeInteger:
                writer.WriteNumberValue((long)value.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "The support value kind is not defined.");
        }
    }

    private static SupportValueKind GetExpectedKind(SupportFieldName field) => field switch
    {
        SupportFieldName.SchemaVersion or
        SupportFieldName.ManifestVersion or
        SupportFieldName.ApplicationVersion or
        SupportFieldName.BuildVersion or
        SupportFieldName.OsVersion => SupportValueKind.Version,
        SupportFieldName.InternalSupportId => SupportValueKind.OpaqueIdentifier,
        SupportFieldName.EventTimeUtc or
        SupportFieldName.ExportCreatedAtUtc => SupportValueKind.Timestamp,
        SupportFieldName.Retryable => SupportValueKind.Boolean,
        SupportFieldName.DurationMs or
        SupportFieldName.BoundedCount => SupportValueKind.NonNegativeInteger,
        SupportFieldName.MemoryBucket => SupportValueKind.ResourceBucket,
        SupportFieldName.PreviewContentDigestSha256 or
        SupportFieldName.ExportContentDigestSha256 => SupportValueKind.Sha256Digest,
        _ => SupportValueKind.Code,
    };

    private static bool TryConvertInteger(object? value, out long number)
    {
        switch (value)
        {
            case byte byteValue:
                number = byteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool IsCanonicalUtcTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value.Length is < 20 or > 28 || !value.EndsWith('Z'))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            value,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }

    private static bool IsRegisteredTimestamp(object? value, DateTimeOffset expected) => value switch
    {
        DateTimeOffset timestamp => timestamp.ToUniversalTime() == expected,
        string timestamp when IsCanonicalUtcTimestamp(timestamp, out var parsed) =>
            parsed == expected && string.Equals(timestamp, FormatCanonicalUtcTimestamp(parsed), StringComparison.Ordinal),
        _ => false,
    };

    private static string FormatCanonicalUtcTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }
}
