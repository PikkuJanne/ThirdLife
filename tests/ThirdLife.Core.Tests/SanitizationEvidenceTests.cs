using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Tests;

public sealed class SanitizationEvidenceTests
{
    public static TheoryData<SanitizationState, SanitizationVerificationState, bool> ValidStates => new()
    {
        { SanitizationState.Verified, SanitizationVerificationState.Verified, true },
        { SanitizationState.ReplacementStorage, SanitizationVerificationState.Verified, true },
        { SanitizationState.NoDonorStorage, SanitizationVerificationState.Verified, false },
        { SanitizationState.Unknown, SanitizationVerificationState.NotAvailable, false },
        { SanitizationState.Failed, SanitizationVerificationState.Failed, true },
    };

    [Theory]
    [MemberData(nameof(ValidStates))]
    public void EveryGovernedSanitizationStateRoundTrips(
        SanitizationState state,
        SanitizationVerificationState verificationState,
        bool hasMedia)
    {
        var expected = CreateEvidence(state, verificationState, hasMedia);

        var json = JsonSerializer.Serialize(expected);
        var actual = JsonSerializer.Deserialize<SanitizationEvidence>(json);

        Assert.Contains($"\"sanitization_state\":{JsonSerializer.Serialize(state)}", json, StringComparison.Ordinal);
        Assert.Contains($"\"verification_state\":{JsonSerializer.Serialize(verificationState)}", json, StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SanitizationStateCombinationsAreValidatedExhaustively()
    {
        foreach (var state in Enum.GetValues<SanitizationState>())
        {
            foreach (var verification in Enum.GetValues<SanitizationVerificationState>())
            {
                foreach (var classification in Enum.GetValues<EvidenceClassification>())
                {
                    foreach (var availability in Enum.GetValues<ValueAvailability>())
                    {
                        foreach (var hasOperator in new[] { false, true })
                        {
                            foreach (var hasOccurredAt in new[] { false, true })
                            {
                                foreach (var hasMedia in new[] { false, true })
                                {
                                    var trustedClassification = classification is EvidenceClassification.Observed or
                                        EvidenceClassification.HumanConfirmed;
                                    var expectedValid = state switch
                                    {
                                        SanitizationState.Verified or SanitizationState.ReplacementStorage =>
                                            verification == SanitizationVerificationState.Verified &&
                                            availability == ValueAvailability.Available &&
                                            trustedClassification &&
                                            hasOperator && hasOccurredAt && hasMedia,
                                        SanitizationState.NoDonorStorage =>
                                            verification == SanitizationVerificationState.Verified &&
                                            availability == ValueAvailability.Available &&
                                            trustedClassification &&
                                            hasOperator && hasOccurredAt && !hasMedia,
                                        SanitizationState.Unknown =>
                                            verification == SanitizationVerificationState.NotAvailable &&
                                            availability == ValueAvailability.Unknown &&
                                            classification == EvidenceClassification.NotAvailable &&
                                            !hasOperator && !hasOccurredAt && !hasMedia,
                                        SanitizationState.Failed =>
                                            verification == SanitizationVerificationState.Failed &&
                                            availability == ValueAvailability.Available &&
                                            trustedClassification &&
                                            hasOperator && hasOccurredAt && hasMedia,
                                        _ => false,
                                    };
                                    var provenance = classification == EvidenceClassification.HumanConfirmed
                                        ? ProvenanceKind.HumanConfirmation
                                        : ProvenanceKind.ImportedRecord;

                                    var create = () => new SanitizationEvidence(
                                        new EvidenceMetadata(
                                            new EvidenceId("evidence-sanitization-matrix"),
                                            PrivacyClassification.WorkshopRestricted,
                                            classification,
                                            new ProviderId("sanitization-provider"),
                                            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                                            new EvidenceProvenance(provenance, "sanitization-record-matrix"),
                                            availability),
                                        state,
                                        state == SanitizationState.Unknown ? "not_available" : "external_sanitization",
                                        hasOperator ? new OperatorId("operator-001") : null,
                                        hasOccurredAt
                                            ? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
                                            : null,
                                        hasMedia ? new MediaIdentifier("SYNTHETIC-MEDIA-001") : null,
                                        verification,
                                        "community-policy@1.0.0");

                                    if (expectedValid)
                                    {
                                        Assert.NotNull(create());
                                    }
                                    else
                                    {
                                        Assert.ThrowsAny<ArgumentException>(create);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void KnownSanitizationRequiresOperatorAndOccurrenceTime()
    {
        var metadata = CreateMetadata(SanitizationState.Verified);
        var media = new MediaIdentifier("SYNTHETIC-MEDIA-001");

        Assert.Throws<ArgumentException>(() => new SanitizationEvidence(
            metadata,
            SanitizationState.Verified,
            "external_sanitization",
            operatorId: null,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            media,
            SanitizationVerificationState.Verified,
            "policy@1.0.0"));
        Assert.Throws<ArgumentException>(() => new SanitizationEvidence(
            metadata,
            SanitizationState.Verified,
            "external_sanitization",
            new OperatorId("operator-001"),
            occurredAtUtc: null,
            media,
            SanitizationVerificationState.Verified,
            "policy@1.0.0"));
    }

    [Fact]
    public void MediaIdentifierIsRedactedFromOrdinaryStringFormatting()
    {
        var media = new MediaIdentifier("FULL-SERIAL-LIKE-VALUE");

        Assert.Equal("[media_identifier]", media.ToString());
        Assert.Equal("\"FULL-SERIAL-LIKE-VALUE\"", JsonSerializer.Serialize(media));
    }

    private static SanitizationEvidence CreateEvidence(
        SanitizationState state,
        SanitizationVerificationState verificationState,
        bool hasMedia)
    {
        var known = state != SanitizationState.Unknown;
        return new SanitizationEvidence(
            CreateMetadata(state),
            state,
            known ? "external_sanitization" : "not_available",
            known ? new OperatorId("operator-001") : null,
            known ? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) : null,
            hasMedia ? new MediaIdentifier("SYNTHETIC-MEDIA-001") : null,
            verificationState,
            "community-policy@1.0.0");
    }

    private static EvidenceMetadata CreateMetadata(SanitizationState state)
    {
        var unknown = state == SanitizationState.Unknown;
        return new EvidenceMetadata(
            new EvidenceId("evidence-sanitization-001"),
            PrivacyClassification.WorkshopRestricted,
            unknown ? EvidenceClassification.NotAvailable : EvidenceClassification.Observed,
            new ProviderId("sanitization-provider"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new EvidenceProvenance(ProvenanceKind.ImportedRecord, "sanitization-record-001"),
            unknown ? ValueAvailability.Unknown : ValueAvailability.Available);
    }
}
