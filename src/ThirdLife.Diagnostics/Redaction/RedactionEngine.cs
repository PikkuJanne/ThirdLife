namespace ThirdLife.Diagnostics.Redaction;

internal static class RedactionEngine
{
    public static RedactionResult Transform(
        RedactionField field,
        DiagnosticContext context,
        object? value)
    {
        if (!Enum.IsDefined(field))
        {
            field = RedactionField.Unknown;
        }

        if (!Enum.IsDefined(context))
        {
            throw new ArgumentOutOfRangeException(nameof(context), context, "The diagnostic context is not defined.");
        }

        if (context == DiagnosticContext.Telemetry)
        {
            return Result(
                RedactionAction.SuppressTelemetry,
                "[NOT-EMITTED:telemetry-disabled]",
                PersistenceDisposition.NoneForTelemetry,
                SupportExportOutcome.Omit);
        }

        if (SupportFieldCatalog.TryMap(field, out _))
        {
            return TransformAllowlisted(field, context, value);
        }

        return field switch
        {
            RedactionField.Unknown => Omit("[OMITTED:unknown-field]"),
            RedactionField.PersonName => Redact("[REDACTED:person-name]"),
            RedactionField.Username => context == DiagnosticContext.SupportExport
                ? Omit("[OMITTED:username]")
                : Omit("[OMITTED:username]"),
            RedactionField.EmailAddress => Redact("[REDACTED:email-address]"),
            RedactionField.WifiSsid => Redact("[REDACTED:wifi-ssid]"),
            RedactionField.Ipv4Address => context == DiagnosticContext.SupportExport
                ? Omit("[OMITTED:ip-address]")
                : Redact("[REDACTED:ip-address]"),
            RedactionField.Ipv6Address => context == DiagnosticContext.SupportExport
                ? Omit("[OMITTED:ip-address]")
                : Redact("[REDACTED:ip-address]"),
            RedactionField.MacAddress => Redact("[REDACTED:mac-address]"),
            RedactionField.FullSerialNumber => TransformFullSerial(context, value),
            RedactionField.FilePath or RedactionField.NetworkPath =>
                Redact("[REDACTED:personal-path]"),
            RedactionField.PackageDownloadUrl => Redact("[REDACTED:package-download-url]"),
            RedactionField.Credential => RejectSecret("[REDACTED:credential]"),
            RedactionField.AccessToken => RejectSecret("[REDACTED:token]"),
            RedactionField.Password => RejectSecret("[REDACTED:password]"),
            RedactionField.RecoveryKey => RejectSecret("[REDACTED:recovery-key]"),
            RedactionField.EncryptionKey => RejectSecret("[REDACTED:encryption-key]"),
            RedactionField.DonorFileContent => RejectSecret("[REDACTED:donor-content]"),
            RedactionField.RecipientFileContent => RejectSecret("[REDACTED:recipient-content]"),
            RedactionField.RawCommandOutput => RejectRaw("[OMITTED:raw-command-output]"),
            RedactionField.RawProviderOutput => RejectRaw("[OMITTED:raw-provider-output]"),
            RedactionField.RawInstallerOutput => RejectRaw("[OMITTED:raw-installer-output]"),
            RedactionField.SiblingPrivateDatabaseRecord =>
                RejectOutOfScope("[REJECTED:out-of-scope-private-data]"),
            RedactionField.SiblingAssessmentEvidence =>
                RejectOutOfScope("[REJECTED:out-of-scope-assessment]"),
            RedactionField.SiblingBackupKey =>
                RejectOutOfScope("[REJECTED:out-of-scope-secret]"),
            RedactionField.DeviceName => Redact("[REDACTED:device-name]"),
            RedactionField.ClipboardSecret => RejectSecret("[REDACTED:clipboard-secret]"),
            RedactionField.WindowsSid => Redact("[REDACTED:windows-sid]"),
            RedactionField.AssetTag => Omit("[OMITTED:asset-tag]"),
            _ => Omit("[OMITTED:unknown-field]"),
        };
    }

    internal static RedactionResult Transform(
        string? fieldName,
        DiagnosticContext context,
        object? value) =>
        Transform(RedactionFieldCatalog.Parse(fieldName), context, value);

    private static RedactionResult TransformAllowlisted(
        RedactionField field,
        DiagnosticContext context,
        object? value)
    {
        if (!SupportFieldCatalog.TryValidateFixtureValue(field, value, out var normalized) ||
            !SupportFieldCatalog.IsRegisteredValue(field, normalized))
        {
            return Omit("[OMITTED:unknown-field]");
        }

        if (context != DiagnosticContext.SupportExport)
        {
            return Result(
                RedactionAction.PreserveAllowlisted,
                normalized,
                PersistenceDisposition.StructuredValueOnly,
                SupportExportOutcome.Omit);
        }

        return Result(
            RedactionAction.PreserveAllowlisted,
            normalized,
            PersistenceDisposition.StructuredValueOnly,
            SupportExportOutcome.IncludeUnchangedIfAllowlistedAndPreviewed,
            normalized);
    }

    private static RedactionResult TransformFullSerial(DiagnosticContext context, object? value)
    {
        if (context == DiagnosticContext.WorkshopRecord && value is string serial && IsSafeWorkshopSerial(serial))
        {
            return Result(
                RedactionAction.PreserveWorkshopOnly,
                serial,
                PersistenceDisposition.WorkshopRecordOnly,
                SupportExportOutcome.OmitByDefaultTruncationRequiresExplicitReview);
        }

        return Result(
            RedactionAction.Omit,
            "[OMITTED:full-serial]",
            PersistenceDisposition.NoneInSupportExport,
            SupportExportOutcome.OmitByDefaultTruncationRequiresExplicitReview);
    }

    private static bool IsSafeWorkshopSerial(string value)
    {
        if (value.Length is < 1 or > 256 || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    private static RedactionResult Redact(string marker) =>
        Result(
            RedactionAction.Redact,
            marker,
            PersistenceDisposition.RedactedValueOnly,
            SupportExportOutcome.Omit);

    private static RedactionResult Omit(string marker) =>
        Result(
            RedactionAction.Omit,
            marker,
            PersistenceDisposition.NoneInSupportExport,
            SupportExportOutcome.Omit);

    private static RedactionResult RejectSecret(string marker) =>
        Result(
            RedactionAction.RejectAndDoNotPersist,
            marker,
            PersistenceDisposition.None,
            SupportExportOutcome.Omit);

    private static RedactionResult RejectRaw(string marker) =>
        Result(
            RedactionAction.RejectRawAndExtractAllowlistedFields,
            marker,
            PersistenceDisposition.StructuredProjectionOnly,
            SupportExportOutcome.OmitRawAllowStructuredProjectionOnly);

    private static RedactionResult RejectOutOfScope(string marker) =>
        Result(
            RedactionAction.RejectOutOfScope,
            marker,
            PersistenceDisposition.None,
            SupportExportOutcome.Omit);

    private static RedactionResult Result(
        RedactionAction action,
        object? redactedForm,
        PersistenceDisposition persistence,
        SupportExportOutcome supportOutcome,
        object? exportedForm = null) =>
        new(
            action,
            redactedForm is null ? null : RedactedScalar.FromValidated(redactedForm),
            persistence,
            supportOutcome,
            exportedForm is null ? null : RedactedScalar.FromValidated(exportedForm));

}
