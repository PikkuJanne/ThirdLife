using System.Security.Cryptography;
using System.Text.Json;
using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Tests;

internal sealed record RedactionFixtureCase(
    string Id,
    string InputField,
    object InputValue,
    DiagnosticContext Context,
    RedactionAction Action,
    object? RedactedForm,
    PersistenceDisposition Persistence,
    SupportExportOutcome SupportOutcome,
    object? ExportedForm);

internal sealed class RedactionFixtureSet
{
    public const string ExpectedSha256 = "26fca97a6e0ef5e350b041ba586638975a675e4fc16585bcca33a98b70cbb530";

    private RedactionFixtureSet(
        string sha256,
        IReadOnlyList<string> supportAllowlist,
        IReadOnlyList<RedactionFixtureCase> cases)
    {
        Sha256 = sha256;
        SupportAllowlist = supportAllowlist;
        Cases = cases;
    }

    public string Sha256 { get; }

    public IReadOnlyList<string> SupportAllowlist { get; }

    public IReadOnlyList<RedactionFixtureCase> Cases { get; }

    public static RedactionFixtureSet Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "redaction-test-cases.yaml");
        var bytes = File.ReadAllBytes(path);
        var lines = File.ReadAllLines(path);
        var allowlist = ParseAllowlist(lines);
        var cases = ParseCases(lines);
        return new RedactionFixtureSet(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            allowlist.AsReadOnly(),
            cases.AsReadOnly());
    }

    private static List<string> ParseAllowlist(IReadOnlyList<string> lines)
    {
        var values = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            if (line == "support_export_allowlist:")
            {
                inside = true;
                continue;
            }

            if (inside && line.Length > 0 && !line.StartsWith(' '))
            {
                break;
            }

            if (inside && line.StartsWith("  - ", StringComparison.Ordinal))
            {
                values.Add(line[4..]);
            }
        }

        return values;
    }

    private static List<RedactionFixtureCase> ParseCases(IReadOnlyList<string> lines)
    {
        var cases = new List<RedactionFixtureCase>();
        CaseBuilder? current = null;
        string? section = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("  - id: ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    cases.Add(current.Build());
                }

                current = new CaseBuilder(ParseString(line[8..]));
                section = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("    ", StringComparison.Ordinal) &&
                !line.StartsWith("      ", StringComparison.Ordinal) &&
                line.EndsWith(':'))
            {
                section = line.Trim()[..^1];
                continue;
            }

            if (section is null || !line.StartsWith("      ", StringComparison.Ordinal))
            {
                continue;
            }

            var trimmed = line.Trim();
            var separator = trimmed.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator];
            var scalarText = trimmed[(separator + 1)..].Trim();
            current.Add(section, key, ParseScalar(scalarText));
        }

        if (current is not null)
        {
            cases.Add(current.Build());
        }

        return cases;
    }

    private static object? ParseScalar(string value)
    {
        if (value == "null")
        {
            return null;
        }

        if (value == "true")
        {
            return true;
        }

        if (value == "false")
        {
            return false;
        }

        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return JsonSerializer.Deserialize<string>(value)!;
        }

        if (value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : value;
    }

    private static string ParseString(string value) =>
        ParseScalar(value) as string ?? throw new InvalidDataException("The fixture string is invalid.");

    private sealed class CaseBuilder
    {
        private readonly string _id;
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public CaseBuilder(string id)
        {
            _id = id;
        }

        public void Add(string section, string key, object? value) =>
            _values[$"{section}.{key}"] = value;

        public RedactionFixtureCase Build() => new(
            _id,
            RequireString("input.field"),
            _values["input.value"] ?? throw Invalid("input.value"),
            ParseContext(RequireString("classification.context")),
            ParseAction(RequireString("expected.action")),
            _values.GetValueOrDefault("expected.redacted_form"),
            ParsePersistence(RequireString("expected.persistence")),
            ParseSupportOutcome(RequireString("support_export.outcome")),
            _values.GetValueOrDefault("support_export.exported_form"));

        private string RequireString(string key) =>
            _values.GetValueOrDefault(key) as string ?? throw Invalid(key);

        private InvalidDataException Invalid(string key) =>
            new($"Fixture case {_id} is missing a valid {key} scalar.");
    }

    private static DiagnosticContext ParseContext(string value) => value switch
    {
        "ordinary_log" => DiagnosticContext.OrdinaryLog,
        "crash_report" => DiagnosticContext.CrashReport,
        "workshop_record" => DiagnosticContext.WorkshopRecord,
        "support_export" => DiagnosticContext.SupportExport,
        "command_ingest" => DiagnosticContext.CommandIngest,
        "provider_ingest" => DiagnosticContext.ProviderIngest,
        "installer_ingest" => DiagnosticContext.InstallerIngest,
        "external_private_input" => DiagnosticContext.ExternalPrivateInput,
        "telemetry" => DiagnosticContext.Telemetry,
        _ => throw new InvalidDataException("The fixture context is unsupported."),
    };

    private static RedactionAction ParseAction(string value) => value switch
    {
        "redact" => RedactionAction.Redact,
        "omit" => RedactionAction.Omit,
        "reject_and_do_not_persist" => RedactionAction.RejectAndDoNotPersist,
        "preserve_workshop_only" => RedactionAction.PreserveWorkshopOnly,
        "reject_raw_and_extract_allowlisted_fields" => RedactionAction.RejectRawAndExtractAllowlistedFields,
        "reject_out_of_scope" => RedactionAction.RejectOutOfScope,
        "preserve_allowlisted" => RedactionAction.PreserveAllowlisted,
        "suppress_telemetry" => RedactionAction.SuppressTelemetry,
        _ => throw new InvalidDataException("The fixture action is unsupported."),
    };

    private static PersistenceDisposition ParsePersistence(string value) => value switch
    {
        "redacted_value_only" => PersistenceDisposition.RedactedValueOnly,
        "none" => PersistenceDisposition.None,
        "workshop_record_only" => PersistenceDisposition.WorkshopRecordOnly,
        "none_in_support_export" => PersistenceDisposition.NoneInSupportExport,
        "structured_projection_only" => PersistenceDisposition.StructuredProjectionOnly,
        "structured_value_only" => PersistenceDisposition.StructuredValueOnly,
        "none_for_telemetry" => PersistenceDisposition.NoneForTelemetry,
        _ => throw new InvalidDataException("The fixture persistence disposition is unsupported."),
    };

    private static SupportExportOutcome ParseSupportOutcome(string value) => value switch
    {
        "omit" => SupportExportOutcome.Omit,
        "omit_by_default_truncation_requires_explicit_review" =>
            SupportExportOutcome.OmitByDefaultTruncationRequiresExplicitReview,
        "omit_raw_allow_structured_projection_only" =>
            SupportExportOutcome.OmitRawAllowStructuredProjectionOnly,
        "include_unchanged_if_allowlisted_and_previewed" =>
            SupportExportOutcome.IncludeUnchangedIfAllowlistedAndPreviewed,
        _ => throw new InvalidDataException("The fixture support outcome is unsupported."),
    };
}
