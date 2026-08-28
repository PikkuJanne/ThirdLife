namespace ThirdLife.Diagnostics.Redaction;

public enum DiagnosticContext
{
    OrdinaryLog,
    CrashReport,
    WorkshopRecord,
    SupportExport,
    CommandIngest,
    ProviderIngest,
    InstallerIngest,
    ExternalPrivateInput,
    Telemetry,
}

public enum RedactionField
{
    Unknown,
    PersonName,
    Username,
    EmailAddress,
    WifiSsid,
    Ipv4Address,
    Ipv6Address,
    MacAddress,
    FullSerialNumber,
    FilePath,
    NetworkPath,
    PackageDownloadUrl,
    Credential,
    AccessToken,
    Password,
    RecoveryKey,
    EncryptionKey,
    DonorFileContent,
    RecipientFileContent,
    RawCommandOutput,
    RawProviderOutput,
    RawInstallerOutput,
    SiblingPrivateDatabaseRecord,
    SiblingAssessmentEvidence,
    SiblingBackupKey,
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
    DeviceName,
    ClipboardSecret,
    WindowsSid,
    AssetTag,
}

public enum RedactionAction
{
    Redact,
    Omit,
    RejectAndDoNotPersist,
    PreserveWorkshopOnly,
    RejectRawAndExtractAllowlistedFields,
    RejectOutOfScope,
    PreserveAllowlisted,
    SuppressTelemetry,
}

public enum PersistenceDisposition
{
    RedactedValueOnly,
    None,
    WorkshopRecordOnly,
    NoneInSupportExport,
    StructuredProjectionOnly,
    StructuredValueOnly,
    NoneForTelemetry,
}

public enum SupportExportOutcome
{
    Omit,
    OmitByDefaultTruncationRequiresExplicitReview,
    OmitRawAllowStructuredProjectionOnly,
    IncludeUnchangedIfAllowlistedAndPreviewed,
}

internal sealed class RedactionResult
{
    internal RedactionResult(
        RedactionAction action,
        RedactedScalar? redactedForm,
        PersistenceDisposition persistence,
        SupportExportOutcome supportOutcome,
        RedactedScalar? exportedForm)
    {
        Action = action;
        RedactedForm = redactedForm;
        Persistence = persistence;
        SupportOutcome = supportOutcome;
        ExportedForm = exportedForm;
    }

    public RedactionAction Action { get; }

    public RedactedScalar? RedactedForm { get; }

    public PersistenceDisposition Persistence { get; }

    public SupportExportOutcome SupportOutcome { get; }

    public RedactedScalar? ExportedForm { get; }
}

internal enum RedactedScalarKind
{
    String,
    Boolean,
    Integer,
}

internal sealed class RedactedScalar
{
    private readonly string? _stringValue;
    private readonly bool _booleanValue;
    private readonly long _integerValue;

    private RedactedScalar(string value)
    {
        Kind = RedactedScalarKind.String;
        _stringValue = value;
    }

    private RedactedScalar(bool value)
    {
        Kind = RedactedScalarKind.Boolean;
        _booleanValue = value;
    }

    private RedactedScalar(long value)
    {
        Kind = RedactedScalarKind.Integer;
        _integerValue = value;
    }

    public RedactedScalarKind Kind { get; }

    public string GetString() => Kind == RedactedScalarKind.String
        ? _stringValue!
        : throw new InvalidOperationException("The redacted scalar is not a string.");

    public bool GetBoolean() => Kind == RedactedScalarKind.Boolean
        ? _booleanValue
        : throw new InvalidOperationException("The redacted scalar is not a Boolean.");

    public long GetInteger() => Kind == RedactedScalarKind.Integer
        ? _integerValue
        : throw new InvalidOperationException("The redacted scalar is not an integer.");

    public override string ToString() => "[SAFE:REDACTED-SCALAR]";

    internal static RedactedScalar FromValidated(object value) => value switch
    {
        string text => new RedactedScalar(text),
        bool boolean => new RedactedScalar(boolean),
        byte number => new RedactedScalar(number),
        short number => new RedactedScalar(number),
        int number => new RedactedScalar(number),
        long number => new RedactedScalar(number),
        _ => throw new ArgumentException("Only a closed sanitized scalar can be represented.", nameof(value)),
    };

    internal object ToObject() => Kind switch
    {
        RedactedScalarKind.String => _stringValue!,
        RedactedScalarKind.Boolean => _booleanValue,
        RedactedScalarKind.Integer => _integerValue,
        _ => throw new InvalidOperationException("The redacted scalar kind is not defined."),
    };
}

internal static class RedactionFieldCatalog
{
    private static readonly Dictionary<string, RedactionField> FieldsByWireName =
        Enum.GetValues<RedactionField>()
            .Where(static field => field != RedactionField.Unknown)
            .ToDictionary(GetWireName, StringComparer.Ordinal);

    public static RedactionField Parse(string? fieldName)
    {
        var canonical = DiagnosticText.CanonicalizeFieldName(fieldName);
        return canonical is not null && FieldsByWireName.TryGetValue(canonical, out var field)
            ? field
            : RedactionField.Unknown;
    }

    public static string GetWireName(RedactionField field) => field switch
    {
        RedactionField.Unknown => "unknown_field",
        RedactionField.PersonName => "person_name",
        RedactionField.Username => "username",
        RedactionField.EmailAddress => "email_address",
        RedactionField.WifiSsid => "wifi_ssid",
        RedactionField.Ipv4Address => "ipv4_address",
        RedactionField.Ipv6Address => "ipv6_address",
        RedactionField.MacAddress => "mac_address",
        RedactionField.FullSerialNumber => "full_serial_number",
        RedactionField.FilePath => "file_path",
        RedactionField.NetworkPath => "network_path",
        RedactionField.PackageDownloadUrl => "package_download_url",
        RedactionField.Credential => "credential",
        RedactionField.AccessToken => "access_token",
        RedactionField.Password => "password",
        RedactionField.RecoveryKey => "recovery_key",
        RedactionField.EncryptionKey => "encryption_key",
        RedactionField.DonorFileContent => "donor_file_content",
        RedactionField.RecipientFileContent => "recipient_file_content",
        RedactionField.RawCommandOutput => "raw_command_output",
        RedactionField.RawProviderOutput => "raw_provider_output",
        RedactionField.RawInstallerOutput => "raw_installer_output",
        RedactionField.SiblingPrivateDatabaseRecord => "sibling_private_database_record",
        RedactionField.SiblingAssessmentEvidence => "sibling_assessment_evidence",
        RedactionField.SiblingBackupKey => "sibling_backup_key",
        RedactionField.SchemaVersion => "schema_version",
        RedactionField.ManifestVersion => "manifest_version",
        RedactionField.InternalSupportId => "internal_support_id",
        RedactionField.ApplicationVersion => "application_version",
        RedactionField.BuildVersion => "build_version",
        RedactionField.OsVersion => "os_version",
        RedactionField.HardwareArchitecture => "hardware_architecture",
        RedactionField.MemoryBucket => "memory_bucket",
        RedactionField.StorageClass => "storage_class",
        RedactionField.EventTimeUtc => "event_time_utc",
        RedactionField.ExportCreatedAtUtc => "export_created_at_utc",
        RedactionField.CheckId => "check_id",
        RedactionField.CheckOutcome => "check_outcome",
        RedactionField.ActionCode => "action_code",
        RedactionField.ResultCode => "result_code",
        RedactionField.ComponentId => "component_id",
        RedactionField.OperationType => "operation_type",
        RedactionField.EvidenceState => "evidence_state",
        RedactionField.SanitizedErrorCategory => "sanitized_error_category",
        RedactionField.Retryable => "retryable",
        RedactionField.DurationMs => "duration_ms",
        RedactionField.BoundedCount => "bounded_count",
        RedactionField.LimitationCode => "limitation_code",
        RedactionField.PreviewContentDigestSha256 => "preview_content_digest_sha256",
        RedactionField.ExportContentDigestSha256 => "export_content_digest_sha256",
        RedactionField.DeviceName => "device_name",
        RedactionField.ClipboardSecret => "clipboard_secret",
        RedactionField.WindowsSid => "windows_sid",
        RedactionField.AssetTag => "asset_tag",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "The redaction field is not defined."),
    };
}
