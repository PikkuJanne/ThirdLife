using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Tests;

public sealed class PolicySchemaTests
{
    private static readonly string CandidateJson =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy.json"));

    [Fact]
    public void ParsesCandidateWithoutChangingItsRequirements()
    {
        var policy = Parse(CandidateJson);

        Assert.Equal("community-laptop-policy", policy.PolicyId);
        Assert.Equal("0.1.0-candidate.1", policy.Version);
        Assert.Equal(PolicyStatus.Candidate, policy.Status);
        Assert.Null(policy.Effective.FromUtc);
        Assert.Null(policy.Effective.UntilUtc);
        Assert.Equal("windows_11", policy.Target.OperatingSystem);
        Assert.Equal("supported", policy.Target.SupportState);
        Assert.Equal("x64", policy.Target.Architecture);
        Assert.Equal(27, policy.Rules.Length);
        Assert.Equal(
            Enum.GetValues<RequirementType>(),
            policy.Rules.Select(rule => rule.Requirement.RequirementType).Distinct().Order());

        var sanitization = Assert.Single(policy.Rules, rule => rule.Requirement.RequirementId.Value == "POL-SAN-001");
        Assert.Equal("sanitization.state", sanitization.Requirement.EvidenceKey.Value);
        Assert.Equal(RequirementSeverity.Critical, sanitization.Requirement.Severity);
        Assert.True(sanitization.Decision.BlocksReady);
        Assert.Equal(Disposition.HumanReviewRequired, sanitization.Decision.UnavailableDisposition);
        var alternatives = Assert.IsType<OneOfCondition>(sanitization.Condition);
        Assert.Equal(["verified", "replacement_storage", "no_donor_storage"], alternatives.Values.AsEnumerable());

        var memory = Assert.Single(policy.Rules, rule => rule.Requirement.RequirementId.Value == "POL-MEM-001");
        Assert.Equal(8_589_934_592L, Assert.IsType<MinimumIntegerCondition>(memory.Condition).Value);

        var battery = Assert.Single(policy.Rules, rule => rule.Requirement.RequirementId.Value == "POL-BATTERY-001");
        Assert.True(Assert.IsType<EqualCondition>(battery.Condition).Value.BooleanValue);

        var batteryAdvice = Assert.Single(policy.Rules, rule => rule.Requirement.RequirementId.Value == "POL-BATTERY-003");
        Assert.False(batteryAdvice.Decision.BlocksReady);
        Assert.Null(batteryAdvice.Decision.UnmetDisposition);
        Assert.Null(batteryAdvice.Decision.UnavailableDisposition);

        var audio = Assert.Single(policy.Rules, rule => rule.Requirement.RequirementId.Value == "POL-FUNCTION-005");
        Assert.Equal("video_calling", Assert.Single(audio.ActiveForCapabilityIds));
    }

    [Fact]
    public void ParsesEffectiveMetadataWithUtcWindow()
    {
        var candidate = Candidate();
        candidate["status"] = "effective";
        candidate["effective"] = JsonNode.Parse("""
            {
              "status": "effective",
              "effective_from_utc": "2030-01-01T12:00:00.1234567Z",
              "effective_until_utc": "2031-01-01T12:00:00Z"
            }
            """);

        var policy = Parse(candidate.ToJsonString());

        Assert.Equal(PolicyStatus.Effective, policy.Status);
        Assert.Equal(
            new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_567),
            policy.Effective.FromUtc);
        Assert.Equal(new DateTimeOffset(2031, 1, 1, 12, 0, 0, TimeSpan.Zero), policy.Effective.UntilUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("effective")]
    [InlineData("target")]
    [InlineData("rules.0")]
    [InlineData("rules.0.condition")]
    [InlineData("rules.1.condition")]
    [InlineData("rules.5.condition")]
    [InlineData("rules.0.decision")]
    [InlineData("rules.24")]
    public void EveryObjectRejectsUnknownMembersAndRequiresItsKnownMembers(string path)
    {
        var candidate = Candidate();
        var selected = At(candidate, path).AsObject();
        foreach (var key in selected.Select(property => property.Key).ToArray())
        {
            var removed = selected[key];
            selected.Remove(key);
            Reject(candidate.ToJsonString());
            selected[key] = removed;
        }

        selected["command"] = "unexpected";
        Reject(candidate.ToJsonString());
    }

    [Theory]
    [InlineData("schema_version", "\"thirdlife.policy.v2\"")]
    [InlineData("status", "\"approved\"")]
    [InlineData("status", "\"effective\"")]
    [InlineData("synthetic_data", "null")]
    [InlineData("version", "\"1.02.3\"")]
    [InlineData("version", "\"1.2.3-01\"")]
    [InlineData("version", "\"1.2.3+rebuild\"")]
    [InlineData("effective.status", "\"effective\"")]
    [InlineData("effective.effective_from_utc", "\"2030-01-01T12:00:00Z\"")]
    [InlineData("target.operating_system", "\"windows_10\"")]
    [InlineData("target.support_state", "\"unsupported\"")]
    [InlineData("target.architecture", "\"arm64\"")]
    [InlineData("target.device_class", "\"unknown\"")]
    [InlineData("rules", "[]")]
    [InlineData("rules.0", "null")]
    [InlineData("rules.0.id", "\"pol-san-001\"")]
    [InlineData("rules.0.requirement_type", "\"execute\"")]
    [InlineData("rules.0.requirement_type", "1")]
    [InlineData("rules.0.severity", "\"advisory\"")]
    [InlineData("rules.0.condition", "{\"type\":\"script\",\"value\":true}")]
    [InlineData("rules.0.condition.values", "[]")]
    [InlineData("rules.0.condition.values", "[\"verified\",\"verified\"]")]
    [InlineData("rules.0.condition.values", "[true]")]
    [InlineData("rules.0.decision.blocks_ready", "false")]
    [InlineData("rules.0.decision.unmet_disposition", "\"ready_to_prepare\"")]
    [InlineData("rules.0.decision.unavailable_disposition", "null")]
    [InlineData("rules.0.decision.unavailable_disposition", "\"repair_and_retest\"")]
    [InlineData("rules.0.active_for_capability_ids", "[\"video_calling\"]")]
    [InlineData("rules.1.condition.value", "null")]
    [InlineData("rules.1.condition.value", "12.5")]
    [InlineData("rules.1.condition.value", "9223372036854775808")]
    [InlineData("rules.5.condition.value", "9223372036854775808")]
    [InlineData("rules.5.condition.value", "-1")]
    [InlineData("rules.5.condition.unit", "\"megabytes\"")]
    [InlineData("rules.5.decision.unmet_disposition", "\"human_review_required\"")]
    [InlineData("rules.11.condition.value", "101")]
    [InlineData("rules.11.decision.unmet_disposition", "\"repair_and_retest\"")]
    [InlineData("rules.24.active_for_capability_ids", "null")]
    [InlineData("rules.24.active_for_capability_ids", "[]")]
    [InlineData("rules.24.active_for_capability_ids", "[\"video_calling\",\"video_calling\"]")]
    public void RejectsValuesOutsideTheClosedContract(string path, string value)
    {
        var candidate = Candidate();
        Set(candidate, path, JsonNode.Parse(value));

        Reject(candidate.ToJsonString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("2030-01-01T12:00:00+00:00", null)]
    [InlineData("2030-01-01T12:00Z", null)]
    [InlineData("2030-02-30T12:00:00Z", null)]
    [InlineData("2030-01-01T12:00:00Z", "2029-12-31T12:00:00Z")]
    [InlineData("2030-01-01T12:00:00Z", "2030-01-01T12:00:00Z")]
    public void RejectsAnInvalidEffectiveWindow(string? from, string? until)
    {
        var candidate = Candidate();
        candidate["status"] = "effective";
        candidate["effective"] = new JsonObject
        {
            ["status"] = "effective",
            ["effective_from_utc"] = from,
            ["effective_until_utc"] = until,
        };

        Reject(candidate.ToJsonString());
    }

    [Theory]
    [InlineData("../policy")]
    [InlineData("C:\\workshop\\policy")]
    [InlineData("https://example.invalid/policy")]
    [InlineData("/tmp/policy")]
    [InlineData("HKLM\\Software\\Example")]
    [InlineData("storage..state")]
    [InlineData("disk:serial")]
    [InlineData("two words")]
    public void RejectsPathLikeStringsInIdentifiersAndConditionTokens(string value)
    {
        string[] fields =
        [
            "policy_id",
            "rules.0.id",
            "rules.0.evidence_key",
            "rules.1.condition.value",
            "rules.24.active_for_capability_ids.0",
        ];
        foreach (var field in fields)
        {
            var candidate = Candidate();
            Set(candidate, field, JsonValue.Create(value));
            Reject(candidate.ToJsonString());
        }
    }

    [Fact]
    public void RejectsDuplicatePropertiesEvenWhenOneSpellingIsEscaped()
    {
        var duplicates = """
            "\u0073chema_version":"thirdlife.policy.v1",
            """;
        Reject(CandidateJson.Insert(CandidateJson.IndexOf('{', StringComparison.Ordinal) + 1, duplicates));
    }

    [Fact]
    public void RejectsDuplicateRuleIdsAtDifferentPositions()
    {
        var candidate = Candidate();
        var rules = candidate["rules"]!.AsArray();
        for (var index = 1; index < rules.Count; index++)
        {
            var rule = rules[index]!.AsObject();
            var originalId = rule["id"]!.GetValue<string>();
            rule["id"] = rules[0]!["id"]!.GetValue<string>();
            Reject(candidate.ToJsonString());
            rule["id"] = originalId;
        }
    }

    [Fact]
    public void RejectsMalformedUtf8InAnOtherwiseRecognizedProperty()
    {
        var candidate = CandidateJson.Replace("community-laptop-policy", "~", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(candidate);
        bytes[Array.IndexOf(bytes, (byte)'~')] = 0xFF;

        Assert.Throws<JsonException>(() => PolicySchema.Parse(bytes));
    }

    [Fact]
    public void RejectsAnEscapedLoneSurrogateInAnOtherwiseRecognizedProperty()
    {
        var candidate = CandidateJson.Replace(
            "\"community-laptop-policy\"",
            "\"\\uD800\"",
            StringComparison.Ordinal);

        Reject(candidate);
    }

    [Fact]
    public void EnforcesTheRuleAndConditionListLimits()
    {
        var candidate = Candidate();
        var rules = candidate["rules"]!.AsArray();
        var rule = rules[0]!.DeepClone();
        for (var index = rules.Count; index < 128; index++)
        {
            var additional = rule.DeepClone();
            additional["id"] = string.Create(CultureInfo.InvariantCulture, $"POL-TEST-{index}");
            rules.Add(additional);
        }

        Assert.Equal(128, Parse(candidate.ToJsonString()).Rules.Length);
        rule["id"] = "POL-TEST-128";
        rules.Add(rule);
        Reject(candidate.ToJsonString());

        candidate = Candidate();
        var values = new JsonArray();
        for (var index = 0; index < 16; index++)
        {
            values.Add(string.Create(CultureInfo.InvariantCulture, $"sample_{index}"));
        }

        Set(candidate, "rules.0.condition.values", values);
        Assert.Equal(16, Assert.IsType<OneOfCondition>(Parse(candidate.ToJsonString()).Rules[0].Condition).Values.Length);
        values.Add("too_many");
        Reject(candidate.ToJsonString());
    }

    [Fact]
    public void EnforcesDocumentSizeAndRejectsDeepInputs()
    {
        var padding = PolicySchema.MaximumDocumentBytes - Encoding.UTF8.GetByteCount(CandidateJson);
        var boundaryDocument = CandidateJson + new string(' ', padding);
        Assert.Equal("community-laptop-policy", Parse(boundaryDocument).PolicyId);
        foreach (var excess in new[] { 1, 17, 1_024 })
        {
            Reject(boundaryDocument + new string(' ', excess));
        }

        for (var depth = PolicySchema.MaximumDepth + 1; depth <= PolicySchema.MaximumDepth + 12; depth++)
        {
            Reject(new string('[', depth) + "null" + new string(']', depth));
        }
    }

    private static JsonObject Candidate() => JsonNode.Parse(CandidateJson)!.AsObject();

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
        if (parent is JsonArray array)
        {
            array[int.Parse(member, CultureInfo.InvariantCulture)] = value;
        }
        else
        {
            parent[member] = value;
        }
    }

    private static PolicyDocument Parse(string json) => PolicySchema.Parse(Encoding.UTF8.GetBytes(json));

    private static void Reject(string json) => Assert.Throws<JsonException>(() => Parse(json));
}
