using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Tests;

public sealed class VocabularySerializationTests
{
    [Fact]
    public void EveryGovernedEnumHasOneExactStableWireName()
    {
        AssertWireNames(new Dictionary<PrivacyClassification, string>
        {
            [PrivacyClassification.PublicReference] = "PUBLIC_REFERENCE",
            [PrivacyClassification.WorkshopRestricted] = "WORKSHOP_RESTRICTED",
            [PrivacyClassification.RecipientGuide] = "RECIPIENT_GUIDE",
            [PrivacyClassification.SupportSanitized] = "SUPPORT_SANITIZED",
            [PrivacyClassification.RawUntrustedSensitive] = "RAW_UNTRUSTED_SENSITIVE",
        });
        AssertWireNames(new Dictionary<EvidenceClassification, string>
        {
            [EvidenceClassification.Observed] = "observed",
            [EvidenceClassification.Inferred] = "inferred",
            [EvidenceClassification.NotAvailable] = "not_available",
            [EvidenceClassification.HumanConfirmed] = "human_confirmed",
        });
        AssertWireNames(new Dictionary<ValueAvailability, string>
        {
            [ValueAvailability.Available] = "available",
            [ValueAvailability.Unknown] = "unknown",
            [ValueAvailability.NotApplicable] = "not_applicable",
        });
        AssertWireNames(new Dictionary<ProvenanceKind, string>
        {
            [ProvenanceKind.ProviderObservation] = "provider_observation",
            [ProvenanceKind.HumanConfirmation] = "human_confirmation",
            [ProvenanceKind.ImportedRecord] = "imported_record",
            [ProvenanceKind.SyntheticFixture] = "synthetic_fixture",
            [ProvenanceKind.SystemGenerated] = "system_generated",
        });
        AssertWireNames(new Dictionary<EvidenceValueKind, string>
        {
            [EvidenceValueKind.Boolean] = "boolean",
            [EvidenceValueKind.WholeNumber] = "integer",
            [EvidenceValueKind.DecimalNumber] = "decimal",
            [EvidenceValueKind.Text] = "string",
            [EvidenceValueKind.Enum] = "enum",
        });
        AssertWireNames(new Dictionary<HumanTestResult, string>
        {
            [HumanTestResult.Pass] = "pass",
            [HumanTestResult.Fail] = "fail",
            [HumanTestResult.NotTested] = "not_tested",
            [HumanTestResult.NotAvailable] = "not_available",
        });
        AssertWireNames(new Dictionary<RequirementType, string>
        {
            [RequirementType.Blocking] = "blocking",
            [RequirementType.Repairable] = "repairable",
            [RequirementType.Advisory] = "advisory",
            [RequirementType.ProfileDependent] = "profile_dependent",
            [RequirementType.HumanConfirmed] = "human_confirmed",
        });
        AssertWireNames(new Dictionary<RequirementSeverity, string>
        {
            [RequirementSeverity.Advisory] = "advisory",
            [RequirementSeverity.Required] = "required",
            [RequirementSeverity.Critical] = "critical",
        });
        AssertWireNames(new Dictionary<Disposition, string>
        {
            [Disposition.ReadyToPrepare] = "ready_to_prepare",
            [Disposition.RepairAndRetest] = "repair_and_retest",
            [Disposition.HumanReviewRequired] = "human_review_required",
            [Disposition.AlternativeOperatingSystemCandidate] = "alternative_operating_system_candidate",
            [Disposition.DoNotDeploy] = "do_not_deploy",
        });
        AssertWireNames(new Dictionary<ActionState, string>
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
        });
        AssertWireNames(new Dictionary<SanitizationState, string>
        {
            [SanitizationState.Verified] = "verified",
            [SanitizationState.ReplacementStorage] = "replacement_storage",
            [SanitizationState.NoDonorStorage] = "no_donor_storage",
            [SanitizationState.Unknown] = "unknown",
            [SanitizationState.Failed] = "failed",
        });
        AssertWireNames(new Dictionary<SanitizationVerificationState, string>
        {
            [SanitizationVerificationState.Verified] = "verified",
            [SanitizationVerificationState.Failed] = "failed",
            [SanitizationVerificationState.NotAvailable] = "not_available",
        });
        AssertWireNames(new Dictionary<SanitizationGateOutcome, string>
        {
            [SanitizationGateOutcome.AllowAssessment] = "allow_assessment",
            [SanitizationGateOutcome.Blocked] = "blocked",
        });
        AssertWireNames(new Dictionary<SanitizationGateReason, string>
        {
            [SanitizationGateReason.SanitizationVerified] = "sanitization_verified",
            [SanitizationGateReason.ReplacementStorageVerified] = "replacement_storage_verified",
            [SanitizationGateReason.NoDonorStorageVerified] = "no_donor_storage_verified",
            [SanitizationGateReason.SanitizationUnknown] = "sanitization_unknown",
            [SanitizationGateReason.SanitizationFailed] = "sanitization_failed",
            [SanitizationGateReason.SanitizationEvidenceMissing] = "sanitization_evidence_missing",
            [SanitizationGateReason.GateDecisionMissing] = "gate_decision_missing",
            [SanitizationGateReason.GateDecisionStale] = "gate_decision_stale",
            [SanitizationGateReason.JobArchived] = "job_archived",
        });
    }

    [Fact]
    public void EveryStrongIdentifierUsesScalarStringJson()
    {
        Assert.Equal("\"job-001\"", JsonSerializer.Serialize(new JobId("job-001")));
        Assert.Equal("\"device-001\"", JsonSerializer.Serialize(new DeviceId("device-001")));
        Assert.Equal("\"action-001\"", JsonSerializer.Serialize(new ActionId("action-001")));
        Assert.Equal("\"evidence-001\"", JsonSerializer.Serialize(new EvidenceId("evidence-001")));
        Assert.Equal("\"provider-001\"", JsonSerializer.Serialize(new ProviderId("provider-001")));
        Assert.Equal("\"operator-001\"", JsonSerializer.Serialize(new OperatorId("operator-001")));
        Assert.Equal("\"human-test-001\"", JsonSerializer.Serialize(new HumanTestId("human-test-001")));
        Assert.Equal("\"gate-001\"", JsonSerializer.Serialize(new SanitizationGateDecisionId("gate-001")));
        Assert.Equal("\"POL-MEM-001\"", JsonSerializer.Serialize(new RequirementId("POL-MEM-001")));
        Assert.Equal("\"memory.installed_bytes\"", JsonSerializer.Serialize(new EvidenceKey("memory.installed_bytes")));
    }

    private static void AssertWireNames<TEnum>(Dictionary<TEnum, string> wireNames)
        where TEnum : struct, Enum
    {
        Assert.Equal(Enum.GetValues<TEnum>().Length, wireNames.Count);

        foreach (var (value, wireName) in wireNames)
        {
            Assert.Equal($"\"{wireName}\"", JsonSerializer.Serialize(value));
            Assert.Equal(value, JsonSerializer.Deserialize<TEnum>($"\"{wireName}\""));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>($"\" {wireName} \""));
        }

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TEnum>("0"));
    }
}
