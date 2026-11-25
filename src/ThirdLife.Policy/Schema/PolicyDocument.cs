using System.Collections.Immutable;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Policy.Schema;

public enum PolicyStatus
{
    Candidate = 1,
    Effective,
}

public enum PolicyDeviceClass
{
    Laptop = 1,
    Desktop,
}

public enum PolicyThresholdUnit
{
    Bytes = 1,
    Percent,
}

public sealed record PolicyEffectiveWindow(DateTimeOffset? FromUtc, DateTimeOffset? UntilUtc);

public sealed record PolicyTarget(
    string OperatingSystem, string SupportState, string Architecture, PolicyDeviceClass DeviceClass);

public abstract record PolicyCondition;

public sealed record EqualCondition(EvidenceValue Value) : PolicyCondition;

public sealed record OneOfCondition(ImmutableArray<string> Values) : PolicyCondition;

public sealed record MinimumIntegerCondition(long Value, PolicyThresholdUnit Unit) : PolicyCondition;

public sealed record PolicyRuleDecision(
    bool BlocksReady, Disposition? UnmetDisposition, Disposition? UnavailableDisposition);

public sealed record PolicyRule(
    RequirementReference Requirement,
    ImmutableArray<string> ActiveForCapabilityIds,
    PolicyCondition Condition,
    PolicyRuleDecision Decision,
    string? ExceptionAuthorizationId = null);

public sealed class PolicyDocument
{
    internal PolicyDocument(
        string schemaVersion,
        string policyId,
        string version,
        string contentSha256,
        PolicyStatus status,
        bool syntheticData,
        string classification,
        string claimScope,
        PolicyEffectiveWindow effective,
        PolicyTarget target,
        ImmutableArray<PolicyRule> rules)
    {
        SchemaVersion = schemaVersion;
        PolicyId = policyId;
        Version = version;
        ContentSha256 = contentSha256;
        Status = status;
        SyntheticData = syntheticData;
        Classification = classification;
        ClaimScope = claimScope;
        Effective = effective;
        Target = target;
        Rules = rules;
    }

    public string SchemaVersion { get; }
    public bool IsCurrentSchema => SchemaVersion == PolicySchema.Version;

    public string PolicyId { get; }

    public string Version { get; }

    public string ContentSha256 { get; }

    public PolicyStatus Status { get; }

    public bool SyntheticData { get; }

    public string Classification { get; }

    public string ClaimScope { get; }

    public PolicyEffectiveWindow Effective { get; }

    public PolicyTarget Target { get; }

    public ImmutableArray<PolicyRule> Rules { get; }
}
