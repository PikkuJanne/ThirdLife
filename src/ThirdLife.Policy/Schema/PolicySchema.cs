using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Policy.Schema;

public static partial class PolicySchema
{
    public const string Version = "thirdlife.policy.v1";
    public const int MaximumDocumentBytes = 65_536;
    public const int MaximumDepth = 8;
    private const int MaximumRules = 128;
    private const int MaximumListItems = 16;
    private const int MaximumCapabilityIdLength = 64;
    private const int MaximumLowerKebabIdLength = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static PolicyDocument Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > MaximumDocumentBytes)
        {
            throw new JsonException("Policy input must contain between 1 and 65,536 UTF-8 bytes.");
        }

        try
        {
            var ownedUtf8Json = utf8Json.ToArray();
            _ = StrictUtf8.GetCharCount(ownedUtf8Json);
            using var document = JsonDocument.Parse(ownedUtf8Json, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = MaximumDepth,
            });
            var contentSha256 = Convert.ToHexStringLower(SHA256.HashData(ownedUtf8Json));
            return ReadPolicy(document.RootElement, contentSha256);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            // Reader exceptions can contain input property names, input values, or raw bytes.
            throw new JsonException("Policy input does not satisfy the ThirdLife policy v1 schema.");
        }
    }

    internal static bool IsCapabilityId(string value) =>
        value.Length <= MaximumCapabilityIdLength && CapabilityIdPattern().IsMatch(value);

    private static PolicyDocument ReadPolicy(JsonElement policy, string contentSha256)
    {
        RequireFields(policy,
            ["schema_version", "policy_id", "version", "status", "synthetic_data", "classification", "claim_scope", "effective", "target", "rules"]);
        if (!policy.GetProperty("schema_version").ValueEquals(Version))
        {
            throw new JsonException();
        }

        var status = ReadString(policy.GetProperty("status")) switch
        {
            "candidate" => PolicyStatus.Candidate,
            "effective" => PolicyStatus.Effective,
            _ => throw new JsonException(),
        };
        return new PolicyDocument(
            ReadLowerKebabId(policy.GetProperty("policy_id")),
            ReadPattern(policy.GetProperty("version"), PolicyVersionPattern(), 64),
            contentSha256,
            status,
            policy.GetProperty("synthetic_data").GetBoolean(),
            ReadPattern(policy.GetProperty("classification"), TokenPattern(), 128),
            ReadPattern(policy.GetProperty("claim_scope"), TokenPattern(), 128),
            ReadWindow(policy.GetProperty("effective"), status),
            ReadTarget(policy.GetProperty("target")),
            ReadRules(policy.GetProperty("rules")));
    }

    private static PolicyEffectiveWindow ReadWindow(JsonElement effective, PolicyStatus policyStatus)
    {
        RequireFields(effective, ["status", "effective_from_utc", "effective_until_utc"]);
        var from = ReadTimestamp(effective.GetProperty("effective_from_utc"));
        var until = ReadTimestamp(effective.GetProperty("effective_until_utc"));
        var expectedStatus = policyStatus == PolicyStatus.Candidate ? "candidate_not_effective" : "effective";
        if (!effective.GetProperty("status").ValueEquals(expectedStatus) ||
            (policyStatus == PolicyStatus.Candidate && (from is not null || until is not null)) ||
            (policyStatus == PolicyStatus.Effective && (from is null || until <= from)))
        {
            throw new JsonException();
        }

        return new PolicyEffectiveWindow(from, until);
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement timestamp)
    {
        if (timestamp.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        _ = ReadPattern(timestamp, UtcTimestampPattern(), 28);
        return timestamp.TryGetDateTimeOffset(out var value) ? value : throw new JsonException();
    }

    private static PolicyTarget ReadTarget(JsonElement target)
    {
        RequireFields(target, ["operating_system", "support_state", "architecture", "device_class"]);
        if (!target.GetProperty("operating_system").ValueEquals("windows_11") ||
            !target.GetProperty("support_state").ValueEquals("supported") ||
            !target.GetProperty("architecture").ValueEquals("x64"))
        {
            throw new JsonException();
        }

        var deviceClass = ReadString(target.GetProperty("device_class")) switch
        {
            "laptop" => PolicyDeviceClass.Laptop,
            "desktop" => PolicyDeviceClass.Desktop,
            _ => throw new JsonException(),
        };
        return new PolicyTarget("windows_11", "supported", "x64", deviceClass);
    }

    private static ImmutableArray<PolicyRule> ReadRules(JsonElement rules)
    {
        var count = RequireArray(rules, MaximumRules);
        var result = ImmutableArray.CreateBuilder<PolicyRule>(count);
        var ids = new HashSet<RequirementId>();
        foreach (var rule in rules.EnumerateArray())
        {
            var parsed = ReadRule(rule);
            if (!ids.Add(parsed.Requirement.RequirementId))
            {
                throw new JsonException();
            }

            result.Add(parsed);
        }

        return result.MoveToImmutable();
    }

    private static PolicyRule ReadRule(JsonElement rule)
    {
        const string capabilitiesField = "active_for_capability_ids";
        const string exceptionAuthorizationField = "exception_authorization_id";
        RequireFields(rule, ["id", "requirement_type", "severity", "evidence_key", "condition", "decision"],
            [capabilitiesField, exceptionAuthorizationField]);
        var type = rule.GetProperty("requirement_type").Deserialize<RequirementType>();
        var severity = rule.GetProperty("severity").Deserialize<RequirementSeverity>();
        var severityMatches = type switch
        {
            RequirementType.Blocking or RequirementType.HumanConfirmed => severity is RequirementSeverity.Required or RequirementSeverity.Critical,
            RequirementType.Repairable or RequirementType.ProfileDependent => severity == RequirementSeverity.Required,
            RequirementType.Advisory => severity == RequirementSeverity.Advisory,
            _ => false,
        };
        var hasCapabilities = rule.TryGetProperty(capabilitiesField, out var capabilities);
        if (!severityMatches || hasCapabilities != (type == RequirementType.ProfileDependent))
        {
            throw new JsonException();
        }

        return new PolicyRule(
            new RequirementReference(
                new RequirementId(ReadPattern(rule.GetProperty("id"), RuleIdPattern(), 64)),
                new EvidenceKey(ReadPattern(rule.GetProperty("evidence_key"), EvidenceKeyPattern(), 128)),
                type,
                severity),
            hasCapabilities ? ReadTokens(capabilities, CapabilityIdPattern(), MaximumCapabilityIdLength) : [],
            ReadCondition(rule.GetProperty("condition")),
            ReadDecision(rule.GetProperty("decision"), type),
            rule.TryGetProperty(exceptionAuthorizationField, out var authorization) ? ReadLowerKebabId(authorization) : null);
    }

    private static PolicyCondition ReadCondition(JsonElement condition)
    {
        if (condition.ValueKind != JsonValueKind.Object || !condition.TryGetProperty("type", out var type))
        {
            throw new JsonException();
        }

        switch (ReadString(type))
        {
            case "equals":
                RequireFields(condition, ["type", "value"]);
                return new EqualCondition(ReadScalar(condition.GetProperty("value")));
            case "one_of":
                RequireFields(condition, ["type", "values"]);
                return new OneOfCondition(ReadTokens(condition.GetProperty("values"), TokenPattern(), 128));
            case "minimum_integer":
                RequireFields(condition, ["type", "value", "unit"]);
                return ReadMinimum(condition);
            default:
                throw new JsonException();
        }
    }

    private static EvidenceValue ReadScalar(JsonElement scalar) => scalar.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => EvidenceValue.FromBoolean(scalar.GetBoolean()),
        JsonValueKind.Number when scalar.TryGetInt64(out var value) => EvidenceValue.FromInteger(value),
        JsonValueKind.String => EvidenceValue.FromEnum(ReadPattern(scalar, TokenPattern(), 128)),
        _ => throw new JsonException(),
    };

    private static MinimumIntegerCondition ReadMinimum(JsonElement condition)
    {
        var value = condition.GetProperty("value");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var threshold) || threshold < 0)
        {
            throw new JsonException();
        }

        var unit = ReadString(condition.GetProperty("unit")) switch
        {
            "bytes" => PolicyThresholdUnit.Bytes,
            "percent" when threshold <= 100 => PolicyThresholdUnit.Percent,
            _ => throw new JsonException(),
        };
        return new MinimumIntegerCondition(threshold, unit);
    }

    private static PolicyRuleDecision ReadDecision(JsonElement decision, RequirementType requirementType)
    {
        RequireFields(decision, ["blocks_ready", "unmet_disposition", "unavailable_disposition"]);
        var blocksReady = decision.GetProperty("blocks_ready").GetBoolean();
        var unmet = decision.GetProperty("unmet_disposition").Deserialize<Disposition?>();
        var unavailable = decision.GetProperty("unavailable_disposition").Deserialize<Disposition?>();
        var valid = requirementType == RequirementType.Advisory
            ? !blocksReady && unmet is null && unavailable is null
            : blocksReady && unmet is not (null or Disposition.ReadyToPrepare) &&
                unavailable == Disposition.HumanReviewRequired &&
                (requirementType != RequirementType.Repairable || unmet == Disposition.RepairAndRetest);
        return valid ? new PolicyRuleDecision(blocksReady, unmet, unavailable) : throw new JsonException();
    }

    private static ImmutableArray<string> ReadTokens(JsonElement array, Regex pattern, int maximumLength)
    {
        var count = RequireArray(array, MaximumListItems);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var values = ImmutableArray.CreateBuilder<string>(count);
        foreach (var item in array.EnumerateArray())
        {
            var value = ReadPattern(item, pattern, maximumLength);
            if (!unique.Add(value))
            {
                throw new JsonException();
            }

            values.Add(value);
        }

        return values.MoveToImmutable();
    }

    private static int RequireArray(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException();
        }

        var count = value.GetArrayLength();
        return count > 0 && count <= maximum ? count : throw new JsonException();
    }

    private static void RequireFields(JsonElement value, ReadOnlySpan<string> required, ReadOnlySpan<string> optional = default)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }

        var requiredFound = 0;
        foreach (var property in value.EnumerateObject())
        {
            var name = property.Name;
            if (required.Contains(name))
            {
                requiredFound++;
            }
            else if (!optional.Contains(name))
            {
                throw new JsonException();
            }
        }

        if (requiredFound != required.Length)
        {
            throw new JsonException();
        }
    }

    private static string ReadString(JsonElement value) => value.GetString() ?? throw new JsonException();

    private static string ReadLowerKebabId(JsonElement element) =>
        ReadPattern(element, LowerKebabIdPattern(), MaximumLowerKebabIdLength);

    private static string ReadPattern(JsonElement element, Regex pattern, int maximumLength)
    {
        var value = ReadString(element);
        return value.Length <= maximumLength && pattern.IsMatch(value) ? value : throw new JsonException();
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z")]
    private static partial Regex LowerKebabIdPattern();

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?\z", RegexOptions.NonBacktracking)]
    private static partial Regex PolicyVersionPattern();

    [GeneratedRegex(@"\A[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+\z")]
    private static partial Regex RuleIdPattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z")]
    private static partial Regex EvidenceKeyPattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:_[a-z0-9]+)*\z")]
    private static partial Regex CapabilityIdPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)*\z")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z\z")]
    private static partial Regex UtcTimestampPattern();
}
