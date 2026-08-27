using System.Text.Json.Serialization;

namespace ThirdLife.Core.Evidence;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Observation
{
    [JsonConstructor]
    public Observation(
        EvidenceMetadata metadata,
        EvidenceKey evidenceKey,
        EvidenceValue? value,
        string? unit,
        string? limitationCode,
        OperatorId? operatorId = null)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        EvidenceKey = evidenceKey ?? throw new ArgumentNullException(nameof(evidenceKey));
        Unit = DomainValue.OptionalCode(unit, nameof(unit));
        LimitationCode = DomainValue.OptionalCode(limitationCode, nameof(limitationCode));
        OperatorId = operatorId;

        if ((metadata.EvidenceClassification == EvidenceClassification.HumanConfirmed) != (operatorId is not null))
        {
            throw new ArgumentException(
                "Human-confirmed observations require operator attribution, which is reserved for that class.",
                nameof(operatorId));
        }

        if (metadata.ValueAvailability == ValueAvailability.Available)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value), "Available observations require a value.");
        }
        else
        {
            if (value is not null)
            {
                throw new ArgumentException("Unknown and not-applicable observations cannot contain a value.", nameof(value));
            }

            if (LimitationCode is null)
            {
                throw new ArgumentException(
                    "Unknown and not-applicable observations require a limitation code.",
                    nameof(limitationCode));
            }

            Value = null;
        }
    }

    [JsonPropertyName("metadata")]
    public EvidenceMetadata Metadata { get; }

    [JsonPropertyName("evidence_key")]
    public EvidenceKey EvidenceKey { get; }

    [JsonPropertyName("value")]
    public EvidenceValue? Value { get; }

    [JsonPropertyName("unit")]
    public string? Unit { get; }

    [JsonPropertyName("limitation_code")]
    public string? LimitationCode { get; }

    [JsonPropertyName("operator_id")]
    public OperatorId? OperatorId { get; }
}
