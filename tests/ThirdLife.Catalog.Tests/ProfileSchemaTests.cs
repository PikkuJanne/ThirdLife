using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;

namespace ThirdLife.Catalog.Tests;

public sealed class ProfileSchemaTests
{
    private const string ContractError = "Profile input does not satisfy the ThirdLife profile v1 contract.";
    private static readonly DateTimeOffset AsOfUtc = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CatalogBytes = ReadFixture("catalog");
    private static readonly CatalogDocument Catalog = CatalogSchema.Parse(CatalogBytes, AsOfUtc);
    private static readonly string BasicJson = Encoding.UTF8.GetString(ReadFixture("basic"));

    [Theory]
    [InlineData("basic", CapabilityIntent.Optional, "optional_remote_communication")]
    [InlineData("job-seeker", CapabilityIntent.Essential, "remote_interview_participation")]
    public void ParsesGovernedProfileWithExplicitCapabilityIntentAndRecipientChoices(
        string fixtureId,
        CapabilityIntent videoIntent,
        string videoReasonCode)
    {
        var fixture = ReadFixture(fixtureId);
        var profile = ProfileSchema.Parse(fixture, Catalog);

        Assert.Equal(fixtureId, profile.ProfileId);
        Assert.Equal("0.1.0-candidate.2", profile.Version);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fixture)), profile.ContentSha256);
        Assert.True(profile.SyntheticData);
        Assert.Equal("community-laptop-policy", profile.Policy.Id);
        Assert.Equal("0.1.0-candidate.2", profile.Policy.Version);
        Assert.Equal(Catalog.CatalogId, profile.Catalog.Id);
        Assert.Equal(Catalog.Version, profile.Catalog.Version);
        Assert.Equal(Catalog.ContentSha256, profile.CatalogContentSha256);
        Assert.Equal(
            ["web_browsing", "document_editing", "pdf_reading", "video_calling"],
            profile.WorkshopCapabilities.Select(capability => capability.CapabilityId));
        Assert.All(
            profile.WorkshopCapabilities.Where(capability => capability.CapabilityId != "video_calling"),
            capability => Assert.Equal(CapabilityIntent.Essential, capability.Intent));
        var video = Assert.Single(profile.WorkshopCapabilities, capability => capability.CapabilityId == "video_calling");
        Assert.Equal(videoIntent, video.Intent);
        Assert.Equal(videoReasonCode, video.ReasonCode);
        Assert.Equal(
            new[]
            {
                RecipientChoiceType.BrowserPreference,
                RecipientChoiceType.AccessibilityPreferences,
                RecipientChoiceType.CloudAccountSignIn,
                RecipientChoiceType.BackupOnboarding,
            },
            profile.RecipientChoices.Select(choice => choice.Type));
        Assert.All(profile.RecipientChoices, choice =>
            Assert.StartsWith(fixtureId + "-", choice.ChoiceId, StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogueAndProfileKeepTheExactValidatedBytesAfterCallerMutation()
    {
        var catalogueInput = ReadFixture("catalog");
        var profileInput = ReadFixture("basic");
        var catalogue = CatalogSchema.Parse(catalogueInput, AsOfUtc);
        var profile = ProfileSchema.Parse(profileInput, catalogue);
        Array.Clear(catalogueInput);
        Array.Clear(profileInput);

        Assert.Equal(ReadFixture("catalog"), catalogue.Utf8Json.AsEnumerable());
        Assert.Equal(ReadFixture("basic"), profile.Utf8Json.AsEnumerable());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(catalogue.Utf8Json.AsSpan())), profile.CatalogContentSha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(profile.Utf8Json.AsSpan())), profile.ContentSha256);
    }

    [Fact]
    public void AllowsAWorkshopProfileWithNoRecipientChoices()
    {
        var basic = Basic();
        basic["recipient"]!["choices"] = new JsonArray();

        var profile = Parse(basic.ToJsonString());

        Assert.Empty(profile.RecipientChoices);
        Assert.Contains(profile.WorkshopCapabilities, capability => capability.Intent == CapabilityIntent.Essential);
    }

    [Fact]
    public void AcceptsReviewedProfileAgainstItsReviewedCatalog()
    {
        var reviewedCatalog = CatalogSchemaTests.CreateReviewedCatalog();
        var catalog = CatalogSchema.Parse(Encoding.UTF8.GetBytes(reviewedCatalog.ToJsonString()), AsOfUtc);
        var profileJson = Basic();
        profileJson["status"] = "reviewed";
        profileJson["synthetic_data"] = false;
        profileJson["catalog"]!["id"] = catalog.CatalogId;
        profileJson["catalog"]!["version"] = catalog.Version;
        var workshop = profileJson["workshop"]!;
        workshop["capabilities"] = new JsonArray(workshop["capabilities"]![0]!.DeepClone());

        var profile = Parse(profileJson.ToJsonString(), catalog);

        Assert.False(profile.SyntheticData);
        Assert.Equal(catalog.ContentSha256, profile.CatalogContentSha256);
        var capability = Assert.Single(profile.WorkshopCapabilities);
        Assert.Equal("web_browsing", capability.CapabilityId);
        Assert.Equal(CapabilityIntent.Essential, capability.Intent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("policy")]
    [InlineData("catalog")]
    [InlineData("workshop")]
    [InlineData("workshop.capabilities.0")]
    [InlineData("recipient")]
    [InlineData("recipient.choices.0")]
    public void RejectsUnknownAndMissingFieldsAtEveryObjectLevel(string path)
    {
        var basic = Basic();
        var selected = At(basic, path).AsObject();
        foreach (var name in selected.Select(property => property.Key).ToArray())
        {
            var original = selected[name];
            selected.Remove(name);
            Reject(basic.ToJsonString());
            selected[name] = original;
        }

        selected["script"] = "unrecognized";
        Reject(basic.ToJsonString());
    }

    [Theory]
    [InlineData("schema_version", "\"thirdlife.profile.v2\"")]
    [InlineData("profile_id", "\"../basic\"")]
    [InlineData("version", "\"0.01.0\"")]
    [InlineData("status", "\"reviewed\"")]
    [InlineData("classification", "\"WORKSHOP_RESTRICTED\"")]
    [InlineData("policy.id", "\"C:/workshop/policy\"")]
    [InlineData("policy.version", "\"latest\"")]
    [InlineData("workshop.capabilities", "[]")]
    [InlineData("workshop.capabilities.0.capability_id", "\"generic.synthetic.package.web-browser\"")]
    [InlineData("workshop.capabilities.0.capability_id", "\"https://example.invalid/package\"")]
    [InlineData("workshop.capabilities.0.capability_id", "\"cloud_account_sign_in\"")]
    [InlineData("workshop.capabilities.0.intent", "\"install_immediately\"")]
    [InlineData("workshop.capabilities.0.reason_code", "\"../reason\"")]
    [InlineData("recipient.choices.0.choice_id", "\"../preferences\"")]
    [InlineData("recipient.choices.0.choice_type", "\"external_adapter\"")]
    [InlineData("recipient.choices.0.state", "\"approved\"")]
    [InlineData("recipient.choices.0.requires_recipient_presence", "false")]
    [InlineData("recipient.choices.0.workshop_action", "true")]
    public void RejectsUnsupportedValuesAndExecutableOrPersonalSettings(string path, string jsonValue)
    {
        var basic = Basic();
        Set(basic, path, JsonNode.Parse(jsonValue));

        Reject(basic.ToJsonString());
    }

    [Fact]
    public void RejectsWorkshopAccountAndExecutionFields()
    {
        var basic = Basic();
        basic["workshop"]!["actions"] = JsonNode.Parse("""[{"type":"cloud_account_sign_in"}]""");
        Reject(basic.ToJsonString());

        basic = Basic();
        basic["recipient"]!["choices"]![2]!["credential"] = "fixture-value";
        Reject(basic.ToJsonString());
    }

    [Fact]
    public void RequiresAnExplicitEssentialCapability()
    {
        var basic = Basic();
        foreach (var capability in basic["workshop"]!["capabilities"]!.AsArray())
        {
            capability!["intent"] = "optional";
        }

        Reject(basic.ToJsonString());
    }

    [Theory]
    [InlineData("workshop.capabilities", "capability_id")]
    [InlineData("recipient.choices", "choice_id")]
    [InlineData("recipient.choices", "choice_type")]
    public void RejectsSeparateRowsWithTheSameKey(string path, string field)
    {
        var basic = Basic();
        var entries = At(basic, path).AsArray();
        entries[1]![field] = entries[0]![field]!.GetValue<string>();

        Reject(basic.ToJsonString());
    }

    [Theory]
    [InlineData("id", "other-catalog")]
    [InlineData("version", "0.1.0-candidate.2")]
    public void RequiresTheExactSuppliedCatalogReference(string field, string replacement)
    {
        var basic = Basic();
        basic["catalog"]![field] = replacement;

        Reject(basic.ToJsonString());
    }

    [Fact]
    public void RequiresEveryCapabilityToExistInTheReferencedCatalog()
    {
        var narrowCatalog = JsonNode.Parse(CatalogBytes)!;
        narrowCatalog["applications"]!.AsArray().RemoveAt(3);
        var catalog = CatalogSchema.Parse(Encoding.UTF8.GetBytes(narrowCatalog.ToJsonString()), AsOfUtc);

        Reject(BasicJson, catalog);

        var basic = Basic();
        basic["workshop"]!["capabilities"]!.AsArray().RemoveAt(3);
        var profile = Parse(basic.ToJsonString(), catalog);
        Assert.Equal(3, profile.WorkshopCapabilities.Length);
    }

    [Fact]
    public void RejectsReviewedProfileClaimsAgainstSyntheticCatalog()
    {
        var basic = Basic();
        basic["status"] = "reviewed";
        basic["synthetic_data"] = false;

        Reject(basic.ToJsonString());
    }

    [Fact]
    public void RejectsDuplicateEscapedPropertiesAndMalformedUtf8()
    {
        const string duplicate = """
            "\u0070rofile_id":"basic",
            """;
        Reject(BasicJson.Insert(BasicJson.IndexOf('{', StringComparison.Ordinal) + 1, duplicate));

        var invalidUtf8 = Encoding.UTF8.GetBytes(BasicJson.Replace("basic_web_access", "~", StringComparison.Ordinal));
        invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'~')] = 0xFF;
        Reject(invalidUtf8);
    }

    [Fact]
    public void EnforcesProfileByteAndCollectionLimits()
    {
        var padding = ProfileSchema.MaximumDocumentBytes - Encoding.UTF8.GetByteCount(BasicJson);
        var boundaryDocument = BasicJson + new string(' ', padding);
        Assert.Equal("basic", Parse(boundaryDocument).ProfileId);
        Reject(boundaryDocument + " ");
        Reject(string.Empty);

        var basic = Basic();
        var capabilities = basic["workshop"]!["capabilities"]!.AsArray();
        capabilities.Add(capabilities[0]!.DeepClone());
        Reject(basic.ToJsonString());

        basic = Basic();
        var choices = basic["recipient"]!["choices"]!.AsArray();
        choices.Add(choices[0]!.DeepClone());
        Reject(basic.ToJsonString());
    }

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name + ".json"));

    private static JsonObject Basic() => JsonNode.Parse(BasicJson)!.AsObject();

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

    private static void Set(JsonObject profile, string path, JsonNode? value)
    {
        var separator = path.LastIndexOf('.');
        var member = path[(separator + 1)..];
        var parent = separator < 0 ? profile : At(profile, path[..separator]);
        parent[member] = value;
    }

    private static ProfileDocument Parse(string json, CatalogDocument? catalog = null) =>
        ProfileSchema.Parse(Encoding.UTF8.GetBytes(json), catalog ?? Catalog);

    private static void Reject(string json, CatalogDocument? catalog = null) => Reject(Encoding.UTF8.GetBytes(json), catalog);

    private static void Reject(byte[] utf8Json, CatalogDocument? catalog = null)
    {
        var exception = Assert.Throws<JsonException>(() => ProfileSchema.Parse(utf8Json, catalog ?? Catalog));
        Assert.Equal(ContractError, exception.Message);
    }
}
