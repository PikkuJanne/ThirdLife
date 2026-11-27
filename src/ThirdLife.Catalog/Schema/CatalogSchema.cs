using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using static ThirdLife.Catalog.Schema.DeclarativeJson;

namespace ThirdLife.Catalog.Schema;

public static partial class CatalogSchema
{
    public const string Version = "thirdlife.catalog.v1";
    public const int MaximumDocumentBytes = 65_536;
    private const int MaximumApplications = 32;
    private const long MaximumBytes = 9_007_199_254_740_991;
    private const string SyntheticPrefix = "generic.synthetic.";
    private const string SyntheticSource = "synthetic-fixture-source";
    private const string SyntheticPublisher = "synthetic-fixture-publisher";
    private const string UnreviewedLicense = "NOASSERTION";

    /// <summary>Validates catalog metadata at a supplied instant against an optional trusted catalog snapshot.</summary>
    /// <remarks>The result describes catalog claims. The package and its rights still need independent supply-chain verification.</remarks>
    public static CatalogDocument Parse(ReadOnlyMemory<byte> utf8Json, DateTimeOffset asOfUtc, CatalogDocument? current = null)
    {
        try
        {
            using var document = Open(utf8Json, MaximumDocumentBytes, out var contentSha256, out var bytes);
            var catalog = ReadCatalog(document.RootElement, asOfUtc, contentSha256, bytes);
            if (current is not null)
            {
                RequireCurrentVersion(catalog, current);
            }

            return catalog;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new JsonException("Catalog input does not satisfy the ThirdLife catalog v1 contract.");
        }
    }

    private static CatalogDocument ReadCatalog(JsonElement root, DateTimeOffset asOfUtc, string digest,
        ImmutableArray<byte> utf8Json)
    {
        RequireFields(root, ["schema_version", "catalog_id", "version", "status", "synthetic_data", "classification", "applications"]);
        var synthetic = root.GetProperty("synthetic_data").GetBoolean();
        RequireValue(root, "schema_version", Version);
        RequireValue(root, "classification", "PUBLIC_REFERENCE");
        RequireValue(root, "status", synthetic ? "candidate" : "reviewed");
        return new CatalogDocument(
            ReadToken(root, "catalog_id", LowerKebabPattern(), 64),
            ReadToken(root, "version", SemanticVersionPattern(), 64),
            digest,
            utf8Json,
            synthetic,
            ReadApplications(root.GetProperty("applications"), synthetic, asOfUtc));
    }

    private static ImmutableArray<CatalogApplication> ReadApplications(JsonElement array, bool synthetic, DateTimeOffset asOfUtc)
    {
        var count = array.GetArrayLength();
        if (count is < 1 or > MaximumApplications)
        {
            throw new JsonException();
        }

        var applications = ImmutableArray.CreateBuilder<CatalogApplication>(count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var packageIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in array.EnumerateArray())
        {
            var application = ReadApplication(element, synthetic, asOfUtc);
            // Both identity components forbid the separator.
            var packageKey = application.Package.SourceId + ":" + application.Package.PackageId;
            if (!ids.Add(application.Id) || !capabilities.Add(application.CapabilityId) || !packageIdentities.Add(packageKey))
            {
                throw new JsonException();
            }

            applications.Add(application);
        }

        return applications.MoveToImmutable();
    }

    internal static bool IsGenericCapability(string? value) =>
        value is "web_browsing" or "document_editing" or "pdf_reading" or "video_calling";

    private static CatalogApplication ReadApplication(JsonElement element, bool synthetic, DateTimeOffset asOfUtc)
    {
        RequireFields(element,
            ["id", "version", "capability_id", "entry_status", "production_eligible", "package", "review", "behavior", "verification"]);
        RequireValue(element, "entry_status", synthetic ? "synthetic_placeholder" : "reviewed");
        var productionEligible = element.GetProperty("production_eligible").GetBoolean();
        var id = ReadToken(element, "id", EntryIdPattern(), 128);
        var capability = element.GetProperty("capability_id").GetString();
        if (productionEligible == synthetic || id.StartsWith(SyntheticPrefix, StringComparison.Ordinal) != synthetic ||
            capability is null || !IsGenericCapability(capability))
        {
            throw new JsonException();
        }

        var package = ReadPackage(element.GetProperty("package"), synthetic);
        return new CatalogApplication(
            id,
            ReadToken(element, "version", SemanticVersionPattern(), 64),
            capability,
            productionEligible,
            package,
            ReadReview(element.GetProperty("review"), synthetic, asOfUtc),
            ReadBehavior(element.GetProperty("behavior"), synthetic),
            ReadVerification(element.GetProperty("verification"), package, synthetic));
    }

    private static CatalogPackage ReadPackage(JsonElement element, bool synthetic)
    {
        ReadOnlySpan<string> required = ["source_id", "package_id", "exact_version", "scope",
            "architectures", "minimum_os", "external_artifact"];
        var hasInstallerDigest = !synthetic && element.TryGetProperty("installer_sha256", out _);
        RequireFields(element, hasInstallerDigest ? [.. required, "installer_sha256"] : required);
        var source = ReadToken(element, "source_id", LowerKebabPattern(), 64);
        var id = ReadToken(element, "package_id", PackageIdPattern(), 128);
        var expectedPrefixComparison = synthetic ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var hasArtifact = element.GetProperty("external_artifact").GetBoolean();
        if (hasArtifact == synthetic || (source == SyntheticSource) != synthetic ||
            id.StartsWith(SyntheticPrefix, expectedPrefixComparison) != synthetic)
        {
            throw new JsonException();
        }

        var scope = element.GetProperty("scope").GetString() switch
        {
            "user" => CatalogScope.User,
            "machine" => CatalogScope.Machine,
            _ => throw new JsonException(),
        };
        RequireValue(element, "minimum_os", "windows_11");
        return new CatalogPackage(
            source,
            id,
            ReadToken(element, "exact_version", PackageVersionPattern(), 64),
            scope,
            ReadArchitectures(element.GetProperty("architectures")),
            "windows_11",
            hasArtifact)
        {
            InstallerSha256 = hasInstallerDigest ? ReadToken(element, "installer_sha256", Sha256Pattern(), 64) : null,
        };
    }

    private static ImmutableArray<CatalogArchitecture> ReadArchitectures(JsonElement array)
    {
        var count = array.GetArrayLength();
        if (count is < 1 or > 3)
        {
            throw new JsonException();
        }

        var architectures = ImmutableArray.CreateBuilder<CatalogArchitecture>(count);
        foreach (var element in array.EnumerateArray())
        {
            var architecture = element.GetString() switch
            {
                "x64" => CatalogArchitecture.X64,
                "arm64" => CatalogArchitecture.Arm64,
                "x86" => CatalogArchitecture.X86,
                _ => throw new JsonException(),
            };
            if (architectures.Contains(architecture))
            {
                throw new JsonException();
            }

            architectures.Add(architecture);
        }

        return architectures.MoveToImmutable();
    }

    private static CatalogReview ReadReview(JsonElement element, bool synthetic, DateTimeOffset asOfUtc)
    {
        ReadOnlySpan<string> required =
            ["publisher", "declared_license", "license_review_status", "privacy_review_status", "license_reviewed_at_utc",
                "privacy_reviewed_at_utc", "redistribution_status"];
        RequireFields(element, synthetic ? required : [.. required, "installation_status", "valid_until_utc"]);
        var publisher = ReadToken(element, "publisher", PublisherPattern(), 128);
        var license = ReadToken(element, "declared_license", LicensePattern(), 64);
        var licenseDate = element.GetProperty("license_reviewed_at_utc");
        var privacyDate = element.GetProperty("privacy_reviewed_at_utc");
        if (synthetic)
        {
            RequireValue(element, "license_review_status", "pending");
            RequireValue(element, "privacy_review_status", "pending");
            RequireValue(element, "redistribution_status", "withheld");
            if (publisher != SyntheticPublisher || license != UnreviewedLicense ||
                licenseDate.ValueKind != JsonValueKind.Null || privacyDate.ValueKind != JsonValueKind.Null)
            {
                throw new JsonException();
            }

            return new CatalogReview(publisher, license, null, null, null, InstallationAllowed: false, RedistributionAllowed: false);
        }

        RequireValue(element, "license_review_status", "approved");
        RequireValue(element, "privacy_review_status", "approved");
        RequireValue(element, "installation_status", "allowed");
        var licenseReviewed = ReadTimestamp(licenseDate);
        var privacyReviewed = ReadTimestamp(privacyDate);
        var validUntil = ReadTimestamp(element.GetProperty("valid_until_utc"));
        if (publisher.Equals(SyntheticPublisher, StringComparison.OrdinalIgnoreCase) ||
            license.Equals(UnreviewedLicense, StringComparison.OrdinalIgnoreCase) ||
            licenseReviewed > asOfUtc || privacyReviewed > asOfUtc || validUntil <= asOfUtc)
        {
            throw new JsonException();
        }

        var redistributionAllowed = element.GetProperty("redistribution_status").GetString() switch
        {
            "allowed" => true,
            "withheld" => false,
            _ => throw new JsonException(),
        };
        return new CatalogReview(publisher, license, licenseReviewed, privacyReviewed, validUntil, InstallationAllowed: true, redistributionAllowed);
    }

    private static CatalogBehavior ReadBehavior(JsonElement element, bool synthetic)
    {
        RequireFields(element, ["reboot", "background_service", "auto_update", "network_use", "download_bytes", "installed_bytes"]);
        var reboot = element.GetProperty("reboot").GetString() switch
        {
            "none" => CatalogReboot.None,
            "possible" => CatalogReboot.Possible,
            "required" => CatalogReboot.Required,
            "unknown" => CatalogReboot.Unknown,
            _ => throw new JsonException(),
        };
        var network = element.GetProperty("network_use").GetString() switch
        {
            "not_applicable" when synthetic => CatalogNetworkUse.NotApplicable,
            "none" when !synthetic => CatalogNetworkUse.None,
            "optional" when !synthetic => CatalogNetworkUse.Optional,
            "required" when !synthetic => CatalogNetworkUse.Required,
            "unknown" when !synthetic => CatalogNetworkUse.Unknown,
            _ => throw new JsonException(),
        };
        return new CatalogBehavior(
            reboot,
            ReadPresence(element.GetProperty("background_service")),
            ReadPresence(element.GetProperty("auto_update")),
            network,
            ReadBytes(element.GetProperty("download_bytes")),
            ReadBytes(element.GetProperty("installed_bytes")));
    }

    private static CatalogVerification ReadVerification(JsonElement element, CatalogPackage package, bool synthetic)
    {
        RequireFields(element, ["status", "type", "expected_source_id", "expected_package_id", "expected_version"]);
        RequireValue(element, "status", synthetic ? "placeholder_unimplemented" : "reviewed");
        RequireValue(element, "type", "exact_package_identity");
        RequireValue(element, "expected_source_id", package.SourceId);
        RequireValue(element, "expected_package_id", package.PackageId);
        RequireValue(element, "expected_version", package.ExactVersion);
        return new CatalogVerification(!synthetic, package.SourceId, package.PackageId, package.ExactVersion);
    }

    private static CatalogPresence ReadPresence(JsonElement value) => value.GetString() switch
    {
        "no" => CatalogPresence.No,
        "yes" => CatalogPresence.Yes,
        "unknown" => CatalogPresence.Unknown,
        _ => throw new JsonException(),
    };

    private static long? ReadBytes(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number when value.TryGetInt64(out var bytes) && bytes is >= 0 and <= MaximumBytes => bytes,
        _ => throw new JsonException(),
    };

    private static DateTimeOffset ReadTimestamp(JsonElement value)
    {
        var text = value.GetString();
        return text is { Length: >= 20 and <= 28 } && TimestampPattern().IsMatch(text) && value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : throw new JsonException();
    }

    private static void RequireCurrentVersion(CatalogDocument catalog, CatalogDocument current)
    {
        if (catalog.CatalogId != current.CatalogId || (!current.SyntheticData && catalog.SyntheticData))
        {
            throw new JsonException();
        }

        var comparison = CatalogVersionOrder.Compare(catalog.Version, current.Version);
        if (comparison < 0 || (comparison == 0 && catalog.ContentSha256 != current.ContentSha256))
        {
            throw new JsonException();
        }
    }

    [GeneratedRegex(@"\A(?:[a-z][a-z0-9]*(?:-[a-z0-9]+)*\.)+[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex EntryIdPattern();

    [GeneratedRegex(@"\A(?:[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)*\+*\.)+[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)*\+*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.NonBacktracking)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(@"\A[0-9][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PackageVersionPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?: [A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PublisherPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\z", RegexOptions.NonBacktracking)]
    private static partial Regex LicensePattern();

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z\z", RegexOptions.NonBacktracking)]
    private static partial Regex TimestampPattern();
}
