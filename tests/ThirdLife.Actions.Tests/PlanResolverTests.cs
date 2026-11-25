using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Actions.Tests;

public sealed class PlanResolverTests
{
    private const string PolicyReference = "community-laptop-policy@0.1.0-candidate.1";
    private const string VideoCalling = "video_calling";
    private static readonly DateTimeOffset IntakeAt = new(2030, 5, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOfUtc = IntakeAt.AddHours(1);
    private static readonly JobId TestJobId = new("job-synthetic-plan");
    private static readonly DeviceId Device = new("device-synthetic-plan");
    private static readonly SanitizationGateDecisionId GateId = new("gate-synthetic-plan");
    private static readonly SanitizationEvidence Intake = CreateIntake("current");
    private static readonly SanitizationGateDecision Gate = EvaluateGate(Intake);
    private static readonly byte[] CatalogBytes = ReadFixture("catalog");
    private static readonly CatalogDocument Catalog = CatalogSchema.Parse(CatalogBytes, AsOfUtc);
    private static readonly ProfileDocument Basic = ProfileSchema.Parse(ReadFixture("basic"), Catalog);
    private static readonly PolicyDocument Policy = PolicySchema.Parse(Encoding.UTF8.GetBytes("""
        {
          "schema_version": "thirdlife.policy.v1",
          "policy_id": "community-laptop-policy",
          "version": "0.1.0-candidate.1",
          "status": "candidate",
          "synthetic_data": true,
          "classification": "PUBLIC_REFERENCE",
          "claim_scope": "synthetic_plan_fixture",
          "effective": {
            "status": "candidate_not_effective",
            "effective_from_utc": null,
            "effective_until_utc": null
          },
          "target": {
            "operating_system": "windows_11",
            "support_state": "supported",
            "architecture": "x64",
            "device_class": "laptop"
          },
          "rules": [
            {
              "id": "POL-SAN-001",
              "requirement_type": "blocking",
              "severity": "critical",
              "evidence_key": "sanitization.state",
              "condition": { "type": "equals", "value": "verified" },
              "decision": {
                "blocks_ready": true,
                "unmet_disposition": "human_review_required",
                "unavailable_disposition": "human_review_required"
              }
            }
          ]
        }
        """.ReplaceLineEndings("\n")));

    [Theory]
    [InlineData("basic", false, "c53f588c6406222f9b249055c72246d7ce8f6f38ab2f61b37ba57e73a60146a3")]
    [InlineData("basic", true, "0e912b4aa3d5c1480299a9a4749c633fe1c8376151bcae6820033db0790b3e1b")]
    [InlineData("job-seeker", false, "7a37bda000ca4f86ad8102050914540010f839249d2e195b78872e013b6cfe58")]
    public void ResolvesTheGovernedSyntheticProfileToAnOrderedNoOpPlan(string fixture, bool selectVideo, string expectedPlanSha256)
    {
        var profile = ProfileSchema.Parse(ReadFixture(fixture), Catalog);
        var observations = ReadyObservations();
        var plan = Resolve(Store(observations), observations, profile, selected: selectVideo ? [VideoCalling] : []);

        Assert.Null(plan.BlockedReason);
        Assert.Equal(Disposition.ReadyToPrepare, plan.PolicyDecision?.Disposition);
        Assert.Equal(TestJobId, plan.JobId);
        Assert.Equal(GateId, plan.GateDecisionId);
        Assert.True(plan.SyntheticData);
        Assert.Equal(Policy.ContentSha256, plan.PolicySha256);
        Assert.Equal(profile.ContentSha256, plan.ProfileSha256);
        Assert.Equal(Catalog.ContentSha256, plan.CatalogSha256);
        Assert.NotNull(plan.EvidenceSha256);
        Assert.Matches("^[0-9a-f]{64}$", plan.EvidenceSha256);
        Assert.Equal(expectedPlanSha256, plan.ContentSha256);

        var videoIsEssential = fixture == "job-seeker";
        string[] expectedOrder = videoIsEssential
            ? ["document_editing", "pdf_reading", VideoCalling, "web_browsing"]
            : selectVideo
                ? ["document_editing", "pdf_reading", "web_browsing", VideoCalling]
                : ["document_editing", "pdf_reading", "web_browsing"];
        Assert.Equal(expectedOrder, plan.Actions.Select(action => action.CapabilityId));
        Assert.Equal(expectedOrder.Order(StringComparer.Ordinal), plan.ActiveCapabilityIds);
        Assert.Equal(plan.Actions.Length, plan.Actions.Select(action => action.Request.ActionId).Distinct().Count());
        Assert.All(plan.Actions, action =>
        {
            var capability = Assert.Single(profile.WorkshopCapabilities, capability => capability.CapabilityId == action.CapabilityId);
            Assert.Equal(capability.Intent, action.Intent);
            Assert.Equal(capability.ReasonCode, action.ReasonCode);
            Assert.Equal(action.CapabilityId, action.CatalogEntry.CapabilityId);
            Assert.IsType<NoOpActionRequest>(action.Request);
            Assert.Equal(ActionScope.Machine, action.Definition.Scope);
            Assert.Equal(ActionVerification.NoMachineChange, action.Definition.Verification);
            Assert.Equal(ActionNetworkUse.None, action.Definition.NetworkUse);
        });
        Assert.Equal(
            profile.RecipientChoices.OrderBy(choice => choice.ChoiceId, StringComparer.Ordinal),
            plan.RecipientChoices);
    }

    [Fact]
    public void DeduplicatesIdenticalInputsAndKeepsActionOrderAndHashAcrossPermutations()
    {
        var profileJson = JsonNode.Parse(ReadFixture("basic"))!;
        profileJson["workshop"]!["capabilities"]![2]!["intent"] = "optional";
        var profile = ProfileSchema.Parse(Encoding.UTF8.GetBytes(profileJson.ToJsonString()), Catalog);
        var observations = ReadyObservations();
        string[] selected = [VideoCalling, "pdf_reading"];
        var job = Store(observations);
        var canonical = Resolve(job, observations, profile, selected: selected);
        Assert.Null(canonical.BlockedReason);

        var random = new Random(30_004);
        for (var trial = 0; trial < 24; trial++)
        {
            Observation[] permutedObservations = [.. observations, observations[0], observations[2]];
            string[] permutedSelection = [.. selected, VideoCalling, "pdf_reading"];
            random.Shuffle(permutedObservations);
            random.Shuffle(permutedSelection);

            var actual = Resolve(job, permutedObservations, profile, selected: permutedSelection);

            Assert.Null(actual.BlockedReason);
            Assert.Equal(canonical.EvidenceSha256, actual.EvidenceSha256);
            Assert.Equal(canonical.ContentSha256, actual.ContentSha256);
            Assert.Equal(
                canonical.Actions.Select(action => action.Request.ActionId),
                actual.Actions.Select(action => action.Request.ActionId));
        }
    }

    [Fact]
    public void OptionalSelectionMateriallyChangesThePlan()
    {
        var observations = ReadyObservations();
        var job = Store(observations);
        var essential = Resolve(job, observations);
        var withOptional = Resolve(job, observations, selected: [VideoCalling]);

        Assert.Null(essential.BlockedReason);
        Assert.Null(withOptional.BlockedReason);
        Assert.Equal(essential.EvidenceSha256, withOptional.EvidenceSha256);
        Assert.NotEqual(essential.ContentSha256, withOptional.ContentSha256);
        Assert.DoesNotContain(essential.Actions, action => action.CapabilityId == VideoCalling);
        Assert.Equal(CapabilityIntent.Optional, Assert.Single(withOptional.Actions, action => action.CapabilityId == VideoCalling).Intent);
    }

    [Fact]
    public void NumericallyEqualMeasurementsHaveTheSameEvidenceAndPlanHashesRegardlessOfScale()
    {
        var observations = ReadyObservations();
        var metadata = Metadata("evidence-fixture-measurement");
        var key = new EvidenceKey("fixture.measurement");
        var oneDecimalPlace = new Observation(metadata, key, EvidenceValue.FromDecimal(1.0m), null, null);
        var twoDecimalPlaces = new Observation(metadata, key, EvidenceValue.FromDecimal(1.00m), null, null);
        var job = Store(observations);
        var reference = Resolve(job, [.. observations, oneDecimalPlace]);
        var reordered = Resolve(job, [twoDecimalPlaces, .. observations, oneDecimalPlace]);

        Assert.Null(reference.BlockedReason);
        Assert.Null(reordered.BlockedReason);
        Assert.Equal(reference.EvidenceSha256, reordered.EvidenceSha256);
        Assert.Equal(reference.ContentSha256, reordered.ContentSha256);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("archived")]
    [InlineData("stale")]
    [InlineData("other-policy")]
    [InlineData("future")]
    public void RequiresAnActiveGateForTheSameCurrentEvidenceAndPolicy(string scenario)
    {
        var observations = ReadyObservations();
        var job = scenario switch
        {
            "missing" => Store(observations, decisions: []),
            "archived" => Store(observations, archived: true),
            "stale" => Store(observations, intakes: [Intake, CreateIntake("latest")]),
            "other-policy" => StoreWithDifferentGatePolicy(observations),
            "future" => Store(observations, decisions: [SanitizationGate.Evaluate(TestJobId, Intake, AsOfUtc.AddSeconds(1), GateId)]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        AssertBlocked(PlanBlockReason.SanitizationRequired, Resolve(job, observations));
    }

    [Theory]
    [InlineData("different-id")]
    [InlineData("different-value")]
    [InlineData("different-metadata")]
    [InlineData("missing")]
    public void RefusesToSubstituteThePersistedGateEvidence(string scenario)
    {
        var observations = ReadyObservations();
        Observation[] passed = scenario switch
        {
            "different-id" => [Observe("sanitization.state", "verified", "evidence-other-gate"), .. observations.Skip(1)],
            "different-value" =>
                [new Observation(Intake.Metadata, new EvidenceKey("sanitization.state"), EvidenceValue.FromEnum("failed"), null, null),
                    .. observations.Skip(1)],
            "different-metadata" =>
                [Observe("sanitization.state", "verified", Intake.Metadata.EvidenceId.Value, IntakeAt.AddMinutes(2)),
                    .. observations.Skip(1)],
            "missing" => [.. observations.Skip(1)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        AssertBlocked(PlanBlockReason.SanitizationRequired, Resolve(Store(observations), passed));
    }

    [Fact]
    public void PreservesThePolicyBlockAndReturnsNoActions()
    {
        var observations = ReadyObservations();
        observations[2] = Observe("operating_system.support_state", "unsupported", "evidence-os-support");

        var plan = Resolve(Store(observations), observations, selected: [VideoCalling]);

        AssertBlocked(PlanBlockReason.PolicyBlocksPreparation, plan);
        Assert.Equal(Disposition.AlternativeOperatingSystemCandidate, plan.PolicyDecision?.Disposition);
    }

    [Fact]
    public void ConflictingObservationsCannotUseEnumerationOrderToChooseAValue()
    {
        var observations = ReadyObservations();
        var conflicting = Observe("operating_system.support_state", "unsupported", "evidence-os-support");
        var job = Store(observations);

        AssertBlocked(PlanBlockReason.EvidenceInvalid, Resolve(job, [.. observations, conflicting]));
        AssertBlocked(PlanBlockReason.EvidenceInvalid, Resolve(job, [conflicting, .. observations]));
    }

    [Theory]
    [InlineData("unreviewed_capability")]
    [InlineData("web_browsing")]
    public void RejectsSelectionOutsideTheOptionalProfileCapabilities(string capabilityId)
    {
        var observations = ReadyObservations();

        AssertBlocked(PlanBlockReason.CapabilitySelectionInvalid, Resolve(Store(observations), observations, selected: [capabilityId]));
    }

    [Fact]
    public void RefusesDifferentCatalogContentWithTheSameIdAndVersion()
    {
        var catalogJson = JsonNode.Parse(CatalogBytes)!;
        catalogJson["applications"]![0]!["behavior"]!["reboot"] = "possible";
        var alternate = CatalogSchema.Parse(Encoding.UTF8.GetBytes(catalogJson.ToJsonString()), AsOfUtc);
        var observations = ReadyObservations();

        Assert.Equal(Catalog.CatalogId, alternate.CatalogId);
        Assert.Equal(Catalog.Version, alternate.Version);
        Assert.NotEqual(Catalog.ContentSha256, alternate.ContentSha256);
        AssertBlocked(PlanBlockReason.ProfileCatalogMismatch, Resolve(Store(observations), observations, catalog: alternate));
    }

    [Fact]
    public void RequiresThePolicyIdAndVersionFromTheProfile()
    {
        var profileJson = JsonNode.Parse(ReadFixture("basic"))!;
        profileJson["policy"]!["version"] = "0.1.0-candidate.2";
        var alternate = ProfileSchema.Parse(Encoding.UTF8.GetBytes(profileJson.ToJsonString()), Catalog);
        var observations = ReadyObservations();

        AssertBlocked(PlanBlockReason.ProfilePolicyMismatch, Resolve(Store(observations), observations, alternate));
    }

    [Fact]
    public void IncompatiblePackageSuppressesEvenEarlierApplicableActions()
    {
        var catalogJson = JsonNode.Parse(CatalogBytes)!;
        catalogJson["applications"]![0]!["package"]!["architectures"] = new JsonArray("arm64");
        var catalog = CatalogSchema.Parse(Encoding.UTF8.GetBytes(catalogJson.ToJsonString()), AsOfUtc);
        var profile = ProfileSchema.Parse(ReadFixture("basic"), catalog);
        var observations = ReadyObservations();

        var plan = Resolve(Store(observations), observations, profile, catalog);

        AssertBlocked(PlanBlockReason.PackageNotApplicable, plan);
        Assert.Equal(Disposition.ReadyToPrepare, plan.PolicyDecision?.Disposition);
    }

    private static ProposedPlan Resolve(
        StoredJob job,
        IEnumerable<Observation> observations,
        ProfileDocument? profile = null,
        CatalogDocument? catalog = null,
        IEnumerable<string>? selected = null) =>
        PlanResolver.Resolve(job, Policy, profile ?? Basic, catalog ?? Catalog, observations, selected ?? [], AsOfUtc);

    private static void AssertBlocked(PlanBlockReason reason, ProposedPlan plan)
    {
        Assert.Equal(reason, plan.BlockedReason);
        Assert.Empty(plan.Actions);
    }

    private static StoredJob Store(
        IReadOnlyList<Observation> observations,
        bool archived = false,
        IReadOnlyList<SanitizationEvidence>? intakes = null,
        IReadOnlyList<SanitizationGateDecision>? decisions = null) =>
        new(new Job(TestJobId, Device, IntakeAt), archived, archived ? IntakeAt.AddMinutes(4) : null,
            observations, intakes ?? [Intake], humanTests: [], sanitizationGateDecisions: decisions ?? [Gate], checkpoints: []);

    private static StoredJob StoreWithDifferentGatePolicy(IReadOnlyList<Observation> observations)
    {
        var intake = CreateIntake("current", "community-laptop-policy@0.1.0-candidate.2");
        return Store(observations, intakes: [intake], decisions: [EvaluateGate(intake)]);
    }

    private static SanitizationGateDecision EvaluateGate(SanitizationEvidence intake) =>
        SanitizationGate.Evaluate(TestJobId, intake, IntakeAt.AddMinutes(2), GateId);

    private static SanitizationEvidence CreateIntake(string suffix, string policyReference = PolicyReference) =>
        new(Metadata("evidence-intake-" + suffix), SanitizationState.Verified, "external_sanitization",
            new OperatorId("operator-synthetic-plan"), IntakeAt, new MediaIdentifier("SYNTHETIC-MEDIA-PLAN"),
            SanitizationVerificationState.Verified, policyReference);

    private static Observation[] ReadyObservations() =>
    [
        new(Intake.Metadata, new EvidenceKey("sanitization.state"), EvidenceValue.FromEnum("verified"), null, null),
        Observe("system.architecture", "x64", "evidence-architecture"),
        Observe("operating_system.support_state", "supported", "evidence-os-support"),
        Observe("operating_system.release", "windows_11", "evidence-os-release"),
    ];

    private static Observation Observe(string key, string value, string evidenceId, DateTimeOffset? collectedAt = null) =>
        new(Metadata(evidenceId, collectedAt), new EvidenceKey(key), EvidenceValue.FromEnum(value), null, null);

    private static EvidenceMetadata Metadata(string evidenceId, DateTimeOffset? collectedAt = null) =>
        new(new EvidenceId(evidenceId), PrivacyClassification.PublicReference, EvidenceClassification.Observed,
            new ProviderId("provider-synthetic-plan"), collectedAt ?? IntakeAt.AddMinutes(1),
            new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "synthetic_plan"), ValueAvailability.Available);

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name + ".json"));
}
