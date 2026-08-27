using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Tests;

public sealed class HumanTestAndVocabularyTests
{
    [Fact]
    public void HumanTestResultsKeepPassFailNotTestedAndNotAvailableDistinct()
    {
        var pass = CreateHumanTest(
            HumanTestResult.Pass,
            CreateMetadata(EvidenceClassification.HumanConfirmed, ValueAvailability.Available, ProvenanceKind.HumanConfirmation),
            new OperatorId("operator-001"),
            limitationCode: null);
        var fail = CreateHumanTest(
            HumanTestResult.Fail,
            CreateMetadata(EvidenceClassification.Observed, ValueAvailability.Available, ProvenanceKind.ProviderObservation),
            new OperatorId("operator-001"),
            limitationCode: "key_group_failed");
        var notTested = CreateHumanTest(
            HumanTestResult.NotTested,
            CreateMetadata(EvidenceClassification.NotAvailable, ValueAvailability.Unknown, ProvenanceKind.SystemGenerated),
            new OperatorId("operator-001"),
            limitationCode: "test_deferred");
        var notAvailable = CreateHumanTest(
            HumanTestResult.NotAvailable,
            CreateMetadata(EvidenceClassification.Observed, ValueAvailability.NotApplicable, ProvenanceKind.ProviderObservation),
            new OperatorId("operator-001"),
            limitationCode: "capability_not_present");

        var values = new[] { pass.TestResult, fail.TestResult, notTested.TestResult, notAvailable.TestResult };

        Assert.Equal(4, values.Distinct().Count());
        Assert.Equal("\"pass\"", JsonSerializer.Serialize(pass.TestResult));
        Assert.Equal("\"fail\"", JsonSerializer.Serialize(fail.TestResult));
        Assert.Equal("\"not_tested\"", JsonSerializer.Serialize(notTested.TestResult));
        Assert.Equal("\"not_available\"", JsonSerializer.Serialize(notAvailable.TestResult));
    }

    [Fact]
    public void HumanTestRejectsInvalidEvidencePromotion()
    {
        Assert.Throws<ArgumentException>(() => CreateHumanTest(
            HumanTestResult.Pass,
            CreateMetadata(EvidenceClassification.Inferred, ValueAvailability.Available, ProvenanceKind.ProviderObservation),
            new OperatorId("operator-001"),
            limitationCode: null));
        Assert.Throws<ArgumentException>(() => CreateHumanTest(
            HumanTestResult.Pass,
            CreateMetadata(EvidenceClassification.Observed, ValueAvailability.Available, ProvenanceKind.ProviderObservation),
            new OperatorId("operator-001"),
            limitationCode: null));
        Assert.Throws<ArgumentNullException>(() => CreateHumanTest(
            HumanTestResult.Pass,
            CreateMetadata(EvidenceClassification.HumanConfirmed, ValueAvailability.Available, ProvenanceKind.HumanConfirmation),
            operatorId: null,
            limitationCode: null));
        Assert.Throws<ArgumentException>(() => CreateHumanTest(
            HumanTestResult.Pass,
            CreateMetadata(EvidenceClassification.HumanConfirmed, ValueAvailability.Available, ProvenanceKind.SyntheticFixture),
            new OperatorId("operator-001"),
            limitationCode: null));
        Assert.Throws<ArgumentException>(() => CreateHumanTest(
            HumanTestResult.NotTested,
            CreateMetadata(EvidenceClassification.Observed, ValueAvailability.NotApplicable, ProvenanceKind.ProviderObservation),
            new OperatorId("operator-001"),
            limitationCode: "test_deferred"));
        Assert.Throws<ArgumentException>(() => CreateHumanTest(
            HumanTestResult.NotAvailable,
            CreateMetadata(EvidenceClassification.NotAvailable, ValueAvailability.Unknown, ProvenanceKind.ProviderObservation),
            new OperatorId("operator-001"),
            limitationCode: null));
    }

    [Fact]
    public void HumanTestStateCombinationsAreValidatedExhaustively()
    {
        foreach (var result in Enum.GetValues<HumanTestResult>())
        {
            foreach (var classification in Enum.GetValues<EvidenceClassification>())
            {
                foreach (var availability in Enum.GetValues<ValueAvailability>())
                {
                    foreach (var hasOperator in new[] { false, true })
                    {
                        foreach (var hasLimitation in new[] { false, true })
                        {
                            var metadataValid = availability switch
                            {
                                ValueAvailability.Available => classification != EvidenceClassification.NotAvailable,
                                ValueAvailability.Unknown => classification == EvidenceClassification.NotAvailable,
                                ValueAvailability.NotApplicable => true,
                                _ => false,
                            };
                            var resultValid = result switch
                            {
                                HumanTestResult.Pass =>
                                    availability == ValueAvailability.Available &&
                                    classification == EvidenceClassification.HumanConfirmed,
                                HumanTestResult.Fail =>
                                    availability == ValueAvailability.Available &&
                                    classification is EvidenceClassification.Observed or
                                        EvidenceClassification.HumanConfirmed,
                                HumanTestResult.NotTested =>
                                    availability == ValueAvailability.Unknown &&
                                    classification == EvidenceClassification.NotAvailable &&
                                    hasLimitation,
                                HumanTestResult.NotAvailable =>
                                    ((availability == ValueAvailability.Unknown &&
                                      classification == EvidenceClassification.NotAvailable) ||
                                     (availability == ValueAvailability.NotApplicable &&
                                      classification is EvidenceClassification.Observed or
                                          EvidenceClassification.HumanConfirmed)) &&
                                    hasLimitation,
                                _ => false,
                            };
                            var expectedValid = metadataValid && resultValid && hasOperator;
                            var provenance = classification == EvidenceClassification.HumanConfirmed
                                ? ProvenanceKind.HumanConfirmation
                                : ProvenanceKind.ProviderObservation;

                            var create = () => CreateHumanTest(
                                result,
                                CreateMetadata(classification, availability, provenance),
                                hasOperator ? new OperatorId("operator-001") : null,
                                hasLimitation ? "bounded_limitation" : null);

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

    [Fact]
    public void HumanTestRoundTripRetainsAttributionAndExplicitNames()
    {
        var expected = CreateHumanTest(
            HumanTestResult.Pass,
            CreateMetadata(EvidenceClassification.HumanConfirmed, ValueAvailability.Available, ProvenanceKind.HumanConfirmation),
            new OperatorId("operator-001"),
            limitationCode: null);

        var json = JsonSerializer.Serialize(expected);
        var actual = JsonSerializer.Deserialize<HumanTestEvidence>(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("human-test-001", root.GetProperty("human_test_id").GetString());
        Assert.Equal("pass", root.GetProperty("test_result").GetString());
        Assert.Equal("operator-001", root.GetProperty("operator_id").GetString());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RequirementReferenceRoundTripsWithoutAddingPolicyLogic()
    {
        var expected = new RequirementReference(
            new RequirementId("POL-MEM-001"),
            new EvidenceKey("memory.installed_bytes"),
            RequirementType.Repairable,
            RequirementSeverity.Required);

        var json = JsonSerializer.Serialize(expected);
        var actual = JsonSerializer.Deserialize<RequirementReference>(json);

        Assert.Equal(
            "{\"requirement_id\":\"POL-MEM-001\",\"evidence_key\":\"memory.installed_bytes\",\"requirement_type\":\"repairable\",\"severity\":\"required\"}",
            json);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GovernedDispositionAndActionVocabulariesUseExactWireNames()
    {
        var dispositions = new Dictionary<Disposition, string>
        {
            [Disposition.ReadyToPrepare] = "ready_to_prepare",
            [Disposition.RepairAndRetest] = "repair_and_retest",
            [Disposition.HumanReviewRequired] = "human_review_required",
            [Disposition.AlternativeOperatingSystemCandidate] = "alternative_operating_system_candidate",
            [Disposition.DoNotDeploy] = "do_not_deploy",
        };
        var actionStates = new Dictionary<ActionState, string>
        {
            [ActionState.Planned] = "planned",
            [ActionState.Approved] = "approved",
            [ActionState.Started] = "started",
            [ActionState.Applied] = "applied",
            [ActionState.Verified] = "verified",
            [ActionState.Failed] = "failed",
            [ActionState.Skipped] = "skipped",
            [ActionState.RolledBack] = "rolled_back",
            [ActionState.RequiresReview] = "requires_review",
        };

        foreach (var (value, wireName) in dispositions)
        {
            Assert.Equal($"\"{wireName}\"", JsonSerializer.Serialize(value));
            Assert.Equal(value, JsonSerializer.Deserialize<Disposition>($"\"{wireName}\""));
        }

        Assert.Equal(Enum.GetValues<Disposition>().Length, dispositions.Count);

        foreach (var (value, wireName) in actionStates)
        {
            Assert.Equal($"\"{wireName}\"", JsonSerializer.Serialize(value));
            Assert.Equal(value, JsonSerializer.Deserialize<ActionState>($"\"{wireName}\""));
        }

        Assert.Equal(Enum.GetValues<ActionState>().Length, actionStates.Count);

        Assert.NotEqual(ActionState.Applied, ActionState.Verified);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ActionState>("3"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Disposition>("\"ready\""));
    }

    private static HumanTestEvidence CreateHumanTest(
        HumanTestResult result,
        EvidenceMetadata metadata,
        OperatorId? operatorId,
        string? limitationCode) =>
        new(
            new HumanTestId("human-test-001"),
            new JobId("job-001"),
            new DeviceId("device-001"),
            metadata,
            result,
            operatorId,
            limitationCode);

    private static EvidenceMetadata CreateMetadata(
        EvidenceClassification classification,
        ValueAvailability availability,
        ProvenanceKind provenanceKind) =>
        new(
            new EvidenceId("evidence-001"),
            provenanceKind == ProvenanceKind.SyntheticFixture
                ? PrivacyClassification.PublicReference
                : PrivacyClassification.WorkshopRestricted,
            classification,
            new ProviderId("human-test-provider"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new EvidenceProvenance(provenanceKind, "source-001"),
            availability);
}
