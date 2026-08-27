using System.Text.Json.Serialization;

namespace ThirdLife.Core.Evidence;

[JsonConverter(typeof(StableStringEnumConverter<PrivacyClassification>))]
public enum PrivacyClassification
{
    [JsonStringEnumMemberName("PUBLIC_REFERENCE")]
    PublicReference = 1,

    [JsonStringEnumMemberName("WORKSHOP_RESTRICTED")]
    WorkshopRestricted,

    [JsonStringEnumMemberName("RECIPIENT_GUIDE")]
    RecipientGuide,

    [JsonStringEnumMemberName("SUPPORT_SANITIZED")]
    SupportSanitized,

    [JsonStringEnumMemberName("RAW_UNTRUSTED_SENSITIVE")]
    RawUntrustedSensitive,
}

[JsonConverter(typeof(StableStringEnumConverter<EvidenceClassification>))]
public enum EvidenceClassification
{
    [JsonStringEnumMemberName("observed")]
    Observed = 1,

    [JsonStringEnumMemberName("inferred")]
    Inferred,

    [JsonStringEnumMemberName("not_available")]
    NotAvailable,

    [JsonStringEnumMemberName("human_confirmed")]
    HumanConfirmed,
}

[JsonConverter(typeof(StableStringEnumConverter<ValueAvailability>))]
public enum ValueAvailability
{
    [JsonStringEnumMemberName("available")]
    Available = 1,

    [JsonStringEnumMemberName("unknown")]
    Unknown,

    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable,
}

[JsonConverter(typeof(StableStringEnumConverter<ProvenanceKind>))]
public enum ProvenanceKind
{
    [JsonStringEnumMemberName("provider_observation")]
    ProviderObservation = 1,

    [JsonStringEnumMemberName("human_confirmation")]
    HumanConfirmation,

    [JsonStringEnumMemberName("imported_record")]
    ImportedRecord,

    [JsonStringEnumMemberName("synthetic_fixture")]
    SyntheticFixture,

    [JsonStringEnumMemberName("system_generated")]
    SystemGenerated,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvidenceProvenance
{
    [JsonConstructor]
    public EvidenceProvenance(ProvenanceKind kind, string sourceReference)
    {
        Kind = DomainValue.RequireDefined(kind, nameof(kind));
        SourceReference = DomainValue.RequireCode(sourceReference, nameof(sourceReference));
    }

    [JsonPropertyName("kind")]
    public ProvenanceKind Kind { get; }

    [JsonPropertyName("source_reference")]
    public string SourceReference { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvidenceMetadata
{
    [JsonConstructor]
    public EvidenceMetadata(
        EvidenceId evidenceId,
        PrivacyClassification privacyClassification,
        EvidenceClassification evidenceClassification,
        ProviderId providerId,
        DateTimeOffset collectedAtUtc,
        EvidenceProvenance provenance,
        ValueAvailability valueAvailability)
    {
        EvidenceId = evidenceId ?? throw new ArgumentNullException(nameof(evidenceId));
        PrivacyClassification = DomainValue.RequireDefined(privacyClassification, nameof(privacyClassification));
        EvidenceClassification = DomainValue.RequireDefined(evidenceClassification, nameof(evidenceClassification));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        CollectedAtUtc = DomainValue.RequireTimestamp(collectedAtUtc, nameof(collectedAtUtc));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        ValueAvailability = DomainValue.RequireDefined(valueAvailability, nameof(valueAvailability));

        ValidatePrivacyAndProvenance(PrivacyClassification, EvidenceClassification, Provenance);
        ValidateClassificationAndAvailability(EvidenceClassification, ValueAvailability);
    }

    [JsonPropertyName("evidence_id")]
    public EvidenceId EvidenceId { get; }

    [JsonPropertyName("classification")]
    public PrivacyClassification PrivacyClassification { get; }

    [JsonPropertyName("evidence_classification")]
    public EvidenceClassification EvidenceClassification { get; }

    [JsonPropertyName("provider_id")]
    public ProviderId ProviderId { get; }

    [JsonPropertyName("collected_at_utc")]
    public DateTimeOffset CollectedAtUtc { get; }

    [JsonPropertyName("provenance")]
    public EvidenceProvenance Provenance { get; }

    [JsonPropertyName("value_availability")]
    public ValueAvailability ValueAvailability { get; }

    private static void ValidatePrivacyAndProvenance(
        PrivacyClassification privacyClassification,
        EvidenceClassification evidenceClassification,
        EvidenceProvenance provenance)
    {
        if (privacyClassification is not PrivacyClassification.PublicReference and
            not PrivacyClassification.WorkshopRestricted)
        {
            throw new ArgumentException(
                "Core evidence must remain workshop-restricted or be a wholly synthetic public fixture.",
                nameof(privacyClassification));
        }

        if (privacyClassification == PrivacyClassification.PublicReference &&
            provenance.Kind != ProvenanceKind.SyntheticFixture)
        {
            throw new ArgumentException(
                "Public-reference evidence must be wholly synthetic; synthetic fixtures may retain a stricter workshop classification.",
                nameof(privacyClassification));
        }

        if ((evidenceClassification == EvidenceClassification.HumanConfirmed) !=
            (provenance.Kind == ProvenanceKind.HumanConfirmation))
        {
            throw new ArgumentException(
                "Human-confirmed evidence requires human-confirmation provenance, which is reserved for that class.",
                nameof(evidenceClassification));
        }
    }

    private static void ValidateClassificationAndAvailability(
        EvidenceClassification evidenceClassification,
        ValueAvailability valueAvailability)
    {
        if (valueAvailability == ValueAvailability.Available && evidenceClassification == EvidenceClassification.NotAvailable)
        {
            throw new ArgumentException("Available evidence cannot use the not-available classification.");
        }

        if (valueAvailability == ValueAvailability.Unknown && evidenceClassification != EvidenceClassification.NotAvailable)
        {
            throw new ArgumentException("Unknown evidence must use the not-available classification.");
        }
    }
}
