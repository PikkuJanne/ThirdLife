using System.Text.Json;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Core.Tests;

public sealed class EvidenceModelTests
{
    [Fact]
    public void EvidenceClassificationAndAvailabilityCombinationsAreValidatedExhaustively()
    {
        foreach (var classification in Enum.GetValues<EvidenceClassification>())
        {
            foreach (var availability in Enum.GetValues<ValueAvailability>())
            {
                var expectedValid = availability switch
                {
                    ValueAvailability.Available => classification != EvidenceClassification.NotAvailable,
                    ValueAvailability.Unknown => classification == EvidenceClassification.NotAvailable,
                    ValueAvailability.NotApplicable => true,
                    _ => false,
                };

                var create = () => CreateMetadata(classification, availability);

                if (expectedValid)
                {
                    Assert.NotNull(create());
                }
                else
                {
                    Assert.Throws<ArgumentException>(create);
                }
            }
        }
    }

    [Fact]
    public void EvidencePrivacyAndProvenanceCombinationsAreValidatedExhaustively()
    {
        foreach (var privacy in Enum.GetValues<PrivacyClassification>())
        {
            foreach (var classification in Enum.GetValues<EvidenceClassification>())
            {
                foreach (var provenanceKind in Enum.GetValues<ProvenanceKind>())
                {
                    var availability = classification == EvidenceClassification.NotAvailable
                        ? ValueAvailability.Unknown
                        : ValueAvailability.Available;
                    var privacyAndProvenanceMatch = privacy switch
                    {
                        PrivacyClassification.PublicReference => provenanceKind == ProvenanceKind.SyntheticFixture,
                        PrivacyClassification.WorkshopRestricted => true,
                        _ => false,
                    };
                    var humanClassificationMatches =
                        (classification == EvidenceClassification.HumanConfirmed) ==
                        (provenanceKind == ProvenanceKind.HumanConfirmation);
                    var expectedValid = privacyAndProvenanceMatch && humanClassificationMatches;

                    var create = () => new EvidenceMetadata(
                        new EvidenceId("evidence-privacy-001"),
                        privacy,
                        classification,
                        new ProviderId("provider-001"),
                        new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new EvidenceProvenance(provenanceKind, "source-001"),
                        availability);

                    if (expectedValid)
                    {
                        Assert.NotNull(create());
                    }
                    else
                    {
                        Assert.Throws<ArgumentException>(create);
                    }
                }
            }
        }
    }

    [Fact]
    public void ObservationValueAndLimitationCombinationsAreValidatedExhaustively()
    {
        foreach (var availability in Enum.GetValues<ValueAvailability>())
        {
            foreach (var hasValue in new[] { false, true })
            {
                foreach (var hasLimitation in new[] { false, true })
                {
                    var classification = availability == ValueAvailability.Unknown
                        ? EvidenceClassification.NotAvailable
                        : EvidenceClassification.Observed;
                    var metadata = CreateMetadata(classification, availability);
                    var value = hasValue ? EvidenceValue.FromBoolean(false) : null;
                    var limitation = hasLimitation ? "provider_unavailable" : null;
                    var expectedValid = availability == ValueAvailability.Available
                        ? hasValue
                        : !hasValue && hasLimitation;

                    var create = () => new Observation(
                        metadata,
                        new EvidenceKey("battery.present"),
                        value,
                        unit: null,
                        limitation);

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

    [Fact]
    public void UnknownAndNotApplicableRemainDistinctAfterRoundTrip()
    {
        var unknown = new Observation(
            CreateMetadata(EvidenceClassification.NotAvailable, ValueAvailability.Unknown),
            new EvidenceKey("battery.present"),
            value: null,
            unit: null,
            limitationCode: "provider_unavailable");
        var notApplicable = new Observation(
            CreateMetadata(EvidenceClassification.Observed, ValueAvailability.NotApplicable),
            new EvidenceKey("function.touch"),
            value: null,
            unit: null,
            limitationCode: "capability_not_present");

        var unknownJson = JsonSerializer.Serialize(unknown);
        var notApplicableJson = JsonSerializer.Serialize(notApplicable);
        var unknownRoundTrip = JsonSerializer.Deserialize<Observation>(unknownJson);
        var notApplicableRoundTrip = JsonSerializer.Deserialize<Observation>(notApplicableJson);

        Assert.Contains("\"value_availability\":\"unknown\"", unknownJson, StringComparison.Ordinal);
        Assert.Contains("\"value_availability\":\"not_applicable\"", notApplicableJson, StringComparison.Ordinal);
        Assert.Equal(ValueAvailability.Unknown, unknownRoundTrip!.Metadata.ValueAvailability);
        Assert.Equal(ValueAvailability.NotApplicable, notApplicableRoundTrip!.Metadata.ValueAvailability);
        Assert.NotEqual(unknownRoundTrip.Metadata.ValueAvailability, notApplicableRoundTrip.Metadata.ValueAvailability);
    }

    [Fact]
    public void HumanConfirmedObservationsRequireTypedOperatorAttribution()
    {
        var humanMetadata = CreateMetadata(
            EvidenceClassification.HumanConfirmed,
            ValueAvailability.Available);
        var observedMetadata = CreateMetadata(
            EvidenceClassification.Observed,
            ValueAvailability.Available);
        var key = new EvidenceKey("keyboard.functional");
        var value = EvidenceValue.FromBoolean(true);
        var operatorId = new OperatorId("operator-001");

        Assert.Throws<ArgumentException>(() => new Observation(
            humanMetadata,
            key,
            value,
            unit: null,
            limitationCode: null));
        Assert.Throws<ArgumentException>(() => new Observation(
            observedMetadata,
            key,
            value,
            unit: null,
            limitationCode: null,
            operatorId));

        var expected = new Observation(
            humanMetadata,
            key,
            value,
            unit: null,
            limitationCode: null,
            operatorId);
        var actual = JsonSerializer.Deserialize<Observation>(JsonSerializer.Serialize(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EvidenceMetadataRequiresEveryAttributionField()
    {
        var timestamp = DateTimeOffset.Parse("2030-01-01T00:00:00Z", provider: null);
        var evidenceId = new EvidenceId("evidence-001");
        var providerId = new ProviderId("provider-001");
        var provenance = new EvidenceProvenance(ProvenanceKind.ProviderObservation, "provider-run-001");

        Assert.Throws<ArgumentNullException>(() => new EvidenceMetadata(
            null!, PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            providerId, timestamp, provenance, ValueAvailability.Available));
        Assert.Throws<ArgumentNullException>(() => new EvidenceMetadata(
            evidenceId, PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            null!, timestamp, provenance, ValueAvailability.Available));
        Assert.Throws<ArgumentException>(() => new EvidenceMetadata(
            evidenceId, PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            providerId, default, provenance, ValueAvailability.Available));
        Assert.Throws<ArgumentNullException>(() => new EvidenceMetadata(
            evidenceId, PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            providerId, timestamp, null!, ValueAvailability.Available));
        Assert.Throws<ArgumentException>(() => new EvidenceProvenance(
            ProvenanceKind.ProviderObservation,
            "C:\\Users\\Person\\raw-provider-output.txt"));
    }

    [Fact]
    public void MetadataSerializationUsesStableExplicitNames()
    {
        var metadata = CreateMetadata(EvidenceClassification.HumanConfirmed, ValueAvailability.Available);

        var json = JsonSerializer.Serialize(metadata);
        var roundTrip = JsonSerializer.Deserialize<EvidenceMetadata>(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("WORKSHOP_RESTRICTED", root.GetProperty("classification").GetString());
        Assert.Equal("human_confirmed", root.GetProperty("evidence_classification").GetString());
        Assert.Equal("provider-001", root.GetProperty("provider_id").GetString());
        Assert.Equal("human_confirmation", root.GetProperty("provenance").GetProperty("kind").GetString());
        Assert.Equal("available", root.GetProperty("value_availability").GetString());
        Assert.Equal(metadata, roundTrip);
    }

    [Theory]
    [InlineData("boolean", "false")]
    [InlineData("integer", "0")]
    [InlineData("decimal", "12.5")]
    [InlineData("string", "\"bounded text\"")]
    [InlineData("enum", "\"supported\"")]
    public void TypedEvidenceValuesRoundTripWithExplicitTypeAndData(string type, string data)
    {
        var json = $"{{\"type\":\"{type}\",\"data\":{data}}}";

        var value = JsonSerializer.Deserialize<EvidenceValue>(json);
        var roundTripJson = JsonSerializer.Serialize(value);

        Assert.Equal(json, roundTripJson);
    }

    [Theory]
    [InlineData("{\"type\":\"integer\",\"data\":true}")]
    [InlineData("{\"type\":\"boolean\",\"data\":1}")]
    [InlineData("{\"type\":\"unknown\",\"data\":1}")]
    [InlineData("{\"type\":\"string\",\"data\":null}")]
    [InlineData("{\"type\":\"integer\",\"data\":1,\"extra\":0}")]
    [InlineData("{\"data\":1}")]
    public void TypedEvidenceValuesRejectInvalidShapes(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EvidenceValue>(json));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"Observed\"")]
    [InlineData("\" observed \"")]
    [InlineData("\"invented\"")]
    public void EvidenceEnumsRejectNumericCaseChangedAndUnknownTokens(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EvidenceClassification>(json));
    }

    private static EvidenceMetadata CreateMetadata(
        EvidenceClassification classification,
        ValueAvailability availability) =>
        new(
            new EvidenceId("evidence-001"),
            PrivacyClassification.WorkshopRestricted,
            classification,
            new ProviderId("provider-001"),
            DateTimeOffset.Parse("2030-01-01T00:00:00Z", provider: null),
            new EvidenceProvenance(
                classification == EvidenceClassification.HumanConfirmed
                    ? ProvenanceKind.HumanConfirmation
                    : ProvenanceKind.ProviderObservation,
                "provider-run-001"),
            availability);
}
