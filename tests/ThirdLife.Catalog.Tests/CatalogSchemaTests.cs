using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Catalog.Schema;

namespace ThirdLife.Catalog.Tests;

public sealed class CatalogSchemaTests
{
    private const string ContractError = "Catalog input does not satisfy the ThirdLife catalog v1 contract.";
    private static readonly DateTimeOffset AsOfUtc = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CandidateBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "catalog.json"));
    private static readonly string CandidateJson = Encoding.UTF8.GetString(CandidateBytes);

    [Fact]
    public void ParsesTheGovernedFixtureWithOnlySyntheticCapabilities()
    {
        var catalog = CatalogSchema.Parse(CandidateBytes, AsOfUtc);

        Assert.Equal("community-laptop-candidate", catalog.CatalogId);
        Assert.Equal("0.1.0-candidate.1", catalog.Version);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(CandidateBytes)), catalog.ContentSha256);
        Assert.True(catalog.SyntheticData);
        Assert.Equal(
            ["web_browsing", "document_editing", "pdf_reading", "video_calling"],
            catalog.Applications.Select(application => application.CapabilityId));
        Assert.All(catalog.Applications, application =>
        {
            Assert.StartsWith("generic.synthetic.", application.Id, StringComparison.Ordinal);
            Assert.Equal("0.0.0-fixture.1", application.Version);
            Assert.False(application.ProductionEligible);
            Assert.Equal("synthetic-fixture-source", application.Package.SourceId);
            Assert.False(application.Package.ExternalArtifact);
            Assert.False(application.Review.InstallationAllowed);
            Assert.False(application.Review.RedistributionAllowed);
            Assert.False(application.Verification.Reviewed);
        });
    }

    [Fact]
    public void AllowsExactVendorVersionAndIndependentRedistributionStatus()
    {
        var reviewed = CreateReviewedCatalog();
        var catalog = Parse(reviewed.ToJsonString());

        Assert.False(catalog.SyntheticData);
        var application = Assert.Single(catalog.Applications);
        Assert.True(application.ProductionEligible);
        Assert.True(application.Package.ExternalArtifact);
        Assert.True(application.Review.InstallationAllowed);
        Assert.False(application.Review.RedistributionAllowed);
        Assert.True(application.Verification.Reviewed);
        Assert.Equal("Fictional.Tests.WebBrowser", application.Package.PackageId);
        Assert.Equal("2.15.236.8", application.Package.ExactVersion);
    }

    [Theory]
    [InlineData("publisher", "Synthetic-Fixture-Publisher")]
    [InlineData("publisher", "Unsafe&Command")]
    [InlineData("declared_license", "noassertion")]
    public void RejectsReservedOrUnsafeReviewedMetadata(string field, string value)
    {
        var reviewed = CreateReviewedCatalog();
        At(reviewed, "applications.0.review")[field] = value;
        Reject(reviewed.ToJsonString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("applications.0")]
    [InlineData("applications.0.package")]
    [InlineData("applications.0.review")]
    [InlineData("applications.0.behavior")]
    [InlineData("applications.0.verification")]
    public void RejectsUnknownAndMissingFieldsAtEveryObjectLevel(string path)
    {
        var candidate = Candidate();
        var selected = At(candidate, path).AsObject();
        foreach (var name in selected.Select(property => property.Key).ToArray())
        {
            var original = selected[name];
            selected.Remove(name);
            Reject(candidate.ToJsonString());
            selected[name] = original;
        }

        // A recognized object must not become an open-ended execution contract.
        selected["installer_arguments"] = "unrecognized";
        Reject(candidate.ToJsonString());
    }

    [Theory]
    [InlineData("version", "\"1.02.3\"")]
    [InlineData("version", "\"1.2.3+rebuild\"")]
    [InlineData("schema_version", "\"thirdlife.catalog.v2\"")]
    [InlineData("catalog_id", "\"../catalog\"")]
    [InlineData("classification", "\"WORKSHOP_RESTRICTED\"")]
    [InlineData("status", "\"reviewed\"")]
    [InlineData("applications", "[]")]
    [InlineData("applications.0.capability_id", "\"unreviewed_capability\"")]
    [InlineData("applications.0.entry_status", "\"reviewed\"")]
    [InlineData("applications.0.production_eligible", "true")]
    [InlineData("applications.0.package.source_id", "\"https://example.invalid/catalog\"")]
    [InlineData("applications.0.package.package_id", "\"../package\"")]
    [InlineData("applications.0.package.exact_version", "\"latest\"")]
    [InlineData("applications.0.package.exact_version", "\"1.*\"")]
    [InlineData("applications.0.package.scope", "\"all\"")]
    [InlineData("applications.0.package.architectures", "[\"x64\",\"x64\"]")]
    [InlineData("applications.0.package.architectures", "[\"unknown\"]")]
    [InlineData("applications.0.package.minimum_os", "\"windows_10\"")]
    [InlineData("applications.0.package.external_artifact", "true")]
    [InlineData("applications.0.review.redistribution_status", "\"allowed\"")]
    [InlineData("applications.0.behavior.download_bytes", "-1")]
    [InlineData("applications.0.behavior.installed_bytes", "9007199254740992")]
    [InlineData("applications.0.behavior.installed_bytes", "0.5")]
    [InlineData("applications.0.behavior.auto_update", "\"always\"")]
    [InlineData("applications.0.behavior.network_use", "\"required\"")]
    [InlineData("applications.0.verification.type", "\"script\"")]
    [InlineData("applications.0.verification.expected_package_id", "\"generic.synthetic.package.document-editor\"")]
    [InlineData("applications.0.verification.expected_source_id", "\"unreviewed-source\"")]
    [InlineData("applications.0.verification.expected_version", "\"0.0.0-fixture.2\"")]
    public void RejectsUnsupportedValuesAndInconsistentSyntheticMetadata(string path, string jsonValue)
    {
        var candidate = Candidate();
        Set(candidate, path, JsonNode.Parse(jsonValue));

        Reject(candidate.ToJsonString());
    }

    [Theory]
    [InlineData("review.publisher", "\"Fictional Publisher | Invalid\"")]
    [InlineData("review.declared_license", "\"NOASSERTION\"")]
    [InlineData("review.license_review_status", "\"pending\"")]
    [InlineData("review.license_reviewed_at_utc", "null")]
    [InlineData("review.license_reviewed_at_utc", "\"2030-02-30T00:00:00Z\"")]
    [InlineData("review.privacy_reviewed_at_utc", "\"2030-06-02T00:00:00Z\"")]
    [InlineData("review.privacy_reviewed_at_utc", "\"2030-05-01T00:00:00+00:00\"")]
    [InlineData("review.valid_until_utc", "\"2030-05-31T00:00:00Z\"")]
    [InlineData("review.valid_until_utc", "\"2030-06-01T12:00:00Z\"")]
    [InlineData("review.installation_status", "\"withheld\"")]
    [InlineData("verification.expected_package_id", "\"fictional.tests.webbrowser\"")]
    public void RejectsStaleUnsupportedOrInconsistentReviewedMetadata(string field, string jsonValue)
    {
        var reviewed = CreateReviewedCatalog();
        Set(reviewed, $"applications.0.{field}", JsonNode.Parse(jsonValue));

        Reject(reviewed.ToJsonString());
    }

    [Theory]
    [InlineData("installation_status")]
    [InlineData("valid_until_utc")]
    public void ReviewedEntriesRequireInstallationAndExpiryFields(string field)
    {
        var reviewed = CreateReviewedCatalog();
        At(reviewed, "applications.0.review").AsObject().Remove(field);

        Reject(reviewed.ToJsonString());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("capability_id")]
    public void RejectsDuplicateApplicationKeysAcrossDistinctRows(string field)
    {
        var candidate = Candidate();
        var applications = candidate["applications"]!.AsArray();
        applications[1]![field] = applications[0]![field]!.GetValue<string>();

        Reject(candidate.ToJsonString());
    }

    [Fact]
    public void RejectsPackageIdentityCollisionsRegardlessOfCaseOrVersion()
    {
        var reviewed = CreateReviewedCatalog();
        var applications = reviewed["applications"]!.AsArray();
        var second = applications[0]!.DeepClone();
        second["id"] = "fictional.document-editor";
        second["capability_id"] = "document_editing";
        second["package"]!["package_id"] = "fictional.tests.webbrowser";
        second["package"]!["exact_version"] = "3.0.0";
        second["verification"]!["expected_package_id"] = "fictional.tests.webbrowser";
        second["verification"]!["expected_version"] = "3.0.0";
        applications.Add(second);

        Reject(reviewed.ToJsonString());
    }

    [Fact]
    public void RejectsDuplicatePropertiesEvenWhenOneSpellingIsEscaped()
    {
        const string duplicate = """
            "\u0073chema_version":"thirdlife.catalog.v1",
            """;
        Reject(CandidateJson.Insert(CandidateJson.IndexOf('{', StringComparison.Ordinal) + 1, duplicate));
    }

    [Fact]
    public void BoundsInputBytesAndRejectsMalformedUtf8WithSanitizedErrors()
    {
        var padding = CatalogSchema.MaximumDocumentBytes - CandidateBytes.Length;
        var boundaryDocument = CandidateJson + new string(' ', padding);
        Assert.Equal("community-laptop-candidate", Parse(boundaryDocument).CatalogId);
        Reject(boundaryDocument + " ");

        var invalidUtf8 = Encoding.UTF8.GetBytes(
            CandidateJson.Replace("community-laptop-candidate", "~", StringComparison.Ordinal));
        invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'~')] = 0xFF;
        Reject(invalidUtf8);
        Reject(CandidateJson.Replace("community-laptop-candidate", "\\uD800", StringComparison.Ordinal));
        Reject("\uFEFF" + CandidateJson);
        Reject(string.Empty);
    }

    [Theory]
    [InlineData("0.1.0-candidate.2", "0.1.0-candidate.10")]
    [InlineData("0.1.0-candidate.10", "0.1.0")]
    [InlineData("0.9.0", "0.10.0")]
    public void AcceptsIncreasingCatalogVersionAndRejectsItsDowngrade(string oldVersion, string newVersion)
    {
        var candidate = Candidate();
        candidate["version"] = oldVersion;
        var priorJson = candidate.ToJsonString();
        var priorCatalog = Parse(priorJson);

        candidate["version"] = newVersion;
        var updated = Parse(candidate.ToJsonString(), priorCatalog);
        Assert.Equal(newVersion, updated.Version);
        Reject(priorJson, updated);
    }

    [Fact]
    public void AllowsExactReplayAndRejectsSameVersionWithChangedBytes()
    {
        var current = CatalogSchema.Parse(CandidateBytes, AsOfUtc);

        Assert.Equal(current.ContentSha256, CatalogSchema.Parse(CandidateBytes, AsOfUtc, current).ContentSha256);
        Reject(CandidateJson + " ", current);
    }

    [Fact]
    public void RejectsReplacingTheCurrentCatalogWithADifferentIdentity()
    {
        var current = Parse(CandidateJson);
        var changed = Candidate();
        changed["version"] = "1.0.0";
        changed["catalog_id"] = "different-catalog";

        Reject(changed.ToJsonString(), current);
    }

    [Fact]
    public void RejectsReturningFromReviewedCatalogToSyntheticEvenAtHigherVersion()
    {
        var current = Parse(CreateReviewedCatalog().ToJsonString());
        var synthetic = Candidate();
        synthetic["catalog_id"] = current.CatalogId;
        synthetic["version"] = "2.0.0";

        Reject(synthetic.ToJsonString(), current);
    }

    private static JsonObject Candidate() => JsonNode.Parse(CandidateJson)!.AsObject();

    internal static JsonObject CreateReviewedCatalog()
    {
        var candidate = Candidate();
        candidate["catalog_id"] = "reviewed-generic-catalog";
        candidate["version"] = "1.0.0";
        candidate["status"] = "reviewed";
        candidate["synthetic_data"] = false;

        const string example = """
            [
              {
                "id": "fictional.web-browser",
                "version": "1.0.0",
                "capability_id": "web_browsing",
                "entry_status": "reviewed",
                "production_eligible": true,
                "package": {
                  "source_id": "test-repository",
                  "package_id": "Fictional.Tests.WebBrowser",
                  "exact_version": "2.15.236.8",
                  "scope": "machine",
                  "architectures": ["x64", "arm64"],
                  "minimum_os": "windows_11",
                  "external_artifact": true
                },
                "review": {
                  "publisher": "Fictional Publisher",
                  "declared_license": "MIT",
                  "license_review_status": "approved",
                  "privacy_review_status": "approved",
                  "license_reviewed_at_utc": "2030-04-01T00:00:00Z",
                  "privacy_reviewed_at_utc": "2030-05-01T00:00:00Z",
                  "installation_status": "allowed",
                  "redistribution_status": "withheld",
                  "valid_until_utc": "2030-07-01T00:00:00Z"
                },
                "behavior": {
                  "reboot": "none",
                  "background_service": "no",
                  "auto_update": "no",
                  "network_use": "required",
                  "download_bytes": 1024,
                  "installed_bytes": 2048
                },
                "verification": {
                  "status": "reviewed",
                  "type": "exact_package_identity",
                  "expected_source_id": "test-repository",
                  "expected_package_id": "Fictional.Tests.WebBrowser",
                  "expected_version": "2.15.236.8"
                }
              }
            ]
            """;
        candidate["applications"] = JsonNode.Parse(example);
        return candidate;
    }

    private static JsonNode At(JsonNode node, string path)
    {
        if (path.Length == 0)
        {
            return node;
        }

        foreach (var segment in path.Split('.'))
        {
            node = node is JsonArray array
                ? array[int.Parse(segment, CultureInfo.InvariantCulture)]!
                : node[segment]!;
        }

        return node;
    }

    private static void Set(JsonObject candidate, string path, JsonNode? value)
    {
        var separator = path.LastIndexOf('.');
        var member = path[(separator + 1)..];
        var parent = separator < 0 ? candidate : At(candidate, path[..separator]);
        parent[member] = value;
    }

    private static CatalogDocument Parse(string json, CatalogDocument? current = null) =>
        CatalogSchema.Parse(Encoding.UTF8.GetBytes(json), AsOfUtc, current);

    private static void Reject(string json, CatalogDocument? current = null) => Reject(Encoding.UTF8.GetBytes(json), current);

    private static void Reject(byte[] utf8Json, CatalogDocument? current = null)
    {
        var exception = Assert.Throws<JsonException>(() => CatalogSchema.Parse(utf8Json, AsOfUtc, current));
        Assert.Equal(ContractError, exception.Message);
    }
}
