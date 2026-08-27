using System.Text.Json.Serialization;

namespace ThirdLife.Core.Evidence;

[JsonConverter(typeof(StableStringEnumConverter<RequirementType>))]
public enum RequirementType
{
    [JsonStringEnumMemberName("blocking")]
    Blocking = 1,

    [JsonStringEnumMemberName("repairable")]
    Repairable,

    [JsonStringEnumMemberName("advisory")]
    Advisory,

    [JsonStringEnumMemberName("profile_dependent")]
    ProfileDependent,

    [JsonStringEnumMemberName("human_confirmed")]
    HumanConfirmed,
}

[JsonConverter(typeof(StableStringEnumConverter<RequirementSeverity>))]
public enum RequirementSeverity
{
    [JsonStringEnumMemberName("advisory")]
    Advisory = 1,

    [JsonStringEnumMemberName("required")]
    Required,

    [JsonStringEnumMemberName("critical")]
    Critical,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementReference
{
    [JsonConstructor]
    public RequirementReference(
        RequirementId requirementId,
        EvidenceKey evidenceKey,
        RequirementType requirementType,
        RequirementSeverity severity)
    {
        RequirementId = requirementId ?? throw new ArgumentNullException(nameof(requirementId));
        EvidenceKey = evidenceKey ?? throw new ArgumentNullException(nameof(evidenceKey));
        RequirementType = DomainValue.RequireDefined(requirementType, nameof(requirementType));
        Severity = DomainValue.RequireDefined(severity, nameof(severity));
    }

    [JsonPropertyName("requirement_id")]
    public RequirementId RequirementId { get; }

    [JsonPropertyName("evidence_key")]
    public EvidenceKey EvidenceKey { get; }

    [JsonPropertyName("requirement_type")]
    public RequirementType RequirementType { get; }

    [JsonPropertyName("severity")]
    public RequirementSeverity Severity { get; }
}
