using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Core.Sanitization;

[JsonConverter(typeof(StableStringEnumConverter<SanitizationState>))]
public enum SanitizationState
{
    [JsonStringEnumMemberName("verified")]
    Verified = 1,

    [JsonStringEnumMemberName("replacement_storage")]
    ReplacementStorage,

    [JsonStringEnumMemberName("no_donor_storage")]
    NoDonorStorage,

    [JsonStringEnumMemberName("unknown")]
    Unknown,

    [JsonStringEnumMemberName("failed")]
    Failed,
}

[JsonConverter(typeof(StableStringEnumConverter<SanitizationVerificationState>))]
public enum SanitizationVerificationState
{
    [JsonStringEnumMemberName("verified")]
    Verified = 1,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("not_available")]
    NotAvailable,
}

[JsonConverter(typeof(MediaIdentifierJsonConverter))]
public sealed record MediaIdentifier
{
    public MediaIdentifier(string value)
    {
        Value = DomainValue.RequireText(value, nameof(value), maximumLength: 128);
    }

    public string Value { get; }

    public override string ToString() => "[media_identifier]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SanitizationEvidence
{
    [JsonConstructor]
    public SanitizationEvidence(
        EvidenceMetadata metadata,
        SanitizationState sanitizationState,
        string methodCode,
        OperatorId? operatorId,
        DateTimeOffset? occurredAtUtc,
        MediaIdentifier? mediaIdentifier,
        SanitizationVerificationState verificationState,
        string policyVersion)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        SanitizationState = DomainValue.RequireDefined(sanitizationState, nameof(sanitizationState));
        MethodCode = DomainValue.RequireCode(methodCode, nameof(methodCode));
        OperatorId = operatorId;
        OccurredAtUtc = occurredAtUtc is null
            ? null
            : DomainValue.RequireTimestamp(occurredAtUtc.Value, nameof(occurredAtUtc));
        MediaIdentifier = mediaIdentifier;
        VerificationState = DomainValue.RequireDefined(verificationState, nameof(verificationState));
        PolicyVersion = DomainValue.RequireVersion(policyVersion, nameof(policyVersion));

        ValidateState();
    }

    [JsonPropertyName("metadata")]
    public EvidenceMetadata Metadata { get; }

    [JsonPropertyName("sanitization_state")]
    public SanitizationState SanitizationState { get; }

    [JsonPropertyName("method_code")]
    public string MethodCode { get; }

    [JsonPropertyName("operator_id")]
    public OperatorId? OperatorId { get; }

    [JsonPropertyName("occurred_at_utc")]
    public DateTimeOffset? OccurredAtUtc { get; }

    [JsonPropertyName("media_identifier")]
    public MediaIdentifier? MediaIdentifier { get; }

    [JsonPropertyName("verification_state")]
    public SanitizationVerificationState VerificationState { get; }

    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; }

    private void ValidateState()
    {
        if (SanitizationState == SanitizationState.Unknown)
        {
            if (Metadata.ValueAvailability != ValueAvailability.Unknown ||
                Metadata.EvidenceClassification != EvidenceClassification.NotAvailable ||
                VerificationState != SanitizationVerificationState.NotAvailable ||
                OperatorId is not null ||
                OccurredAtUtc is not null ||
                MediaIdentifier is not null)
            {
                throw new ArgumentException(
                    "Unknown sanitization requires unknown, not-available evidence, unavailable verification, and no event attribution.");
            }

            return;
        }

        if (Metadata.ValueAvailability != ValueAvailability.Available)
        {
            throw new ArgumentException("Known sanitization results require available evidence.");
        }

        if (Metadata.EvidenceClassification is not EvidenceClassification.Observed and
            not EvidenceClassification.HumanConfirmed)
        {
            throw new ArgumentException("Known sanitization results require observed or human-confirmed evidence.");
        }

        if (OperatorId is null || OccurredAtUtc is null)
        {
            throw new ArgumentException("Known sanitization results require an operator and occurrence time.");
        }

        switch (SanitizationState)
        {
            case SanitizationState.Verified:
            case SanitizationState.ReplacementStorage:
                if (VerificationState != SanitizationVerificationState.Verified || MediaIdentifier is null)
                {
                    throw new ArgumentException(
                        "Verified and replacement-storage sanitization require verified evidence and a media identifier.");
                }

                break;
            case SanitizationState.NoDonorStorage:
                if (VerificationState != SanitizationVerificationState.Verified || MediaIdentifier is not null)
                {
                    throw new ArgumentException(
                        "No-donor-storage evidence requires verified confirmation and cannot identify absent media.");
                }

                break;
            case SanitizationState.Failed:
                if (VerificationState != SanitizationVerificationState.Failed || MediaIdentifier is null)
                {
                    throw new ArgumentException("Failed sanitization requires failed verification and a media identifier.");
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(SanitizationState),
                    SanitizationState,
                    "The sanitization state is not defined.");
        }
    }
}

internal sealed class MediaIdentifierJsonConverter : StringValueJsonConverter<MediaIdentifier>
{
    protected override MediaIdentifier Create(string value) => new(value);

    protected override string GetValue(MediaIdentifier value) => value.Value;
}
