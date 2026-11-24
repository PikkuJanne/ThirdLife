using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;
using static ThirdLife.Policy.Tests.PolicyEvaluationFixture;

namespace ThirdLife.Policy.Tests;

public sealed class DispositionTests
{
    public static TheoryData<Disposition, string, string> GoldenCases => new()
    {
        {
            Disposition.ReadyToPrepare, "Ready to prepare",
            """
            The required checks passed for this assessment.
            Core rule CORE-OS-ARCHITECTURE passed. Evidence ID SYNTHETIC-EVIDENCE-READY-003.
            Core rule CORE-OS-RELEASE passed. Evidence ID SYNTHETIC-EVIDENCE-READY-001.
            Core rule CORE-OS-SUPPORT passed. Evidence ID SYNTHETIC-EVIDENCE-READY-002.
            """
        },
        {
            Disposition.RepairAndRetest, "Repair and retest",
            """
            A required check failed. Record the repair and retest the affected checks.
            Policy rule POL-STORAGE-002 failed. Evidence ID storage-below-minimum.
            """
        },
        {
            Disposition.HumanReviewRequired, "Human review required",
            """
            Review the cited findings before proceeding.
            Core rule CORE-OS-SUPPORT requires current evidence. Evidence ID stale-lifecycle.
            Policy rule POL-OS-002 requires current evidence. Evidence ID stale-lifecycle.
            """
        },
        {
            Disposition.AlternativeOperatingSystemCandidate, "Alternative operating system candidate",
            """
            A Windows preparation requirement failed. Consider a supported alternative operating system.
            Core rule CORE-OS-RELEASE failed. Evidence ID other-windows-release.
            Core rule CORE-OS-SUPPORT failed. Evidence ID audit-only-lifecycle.
            """
        },
        {
            Disposition.DoNotDeploy, "Do not deploy",
            """
            A blocking requirement failed. Stop deployment and review the cited findings.
            Policy rule POL-OWNERSHIP-001 failed. Evidence ID ownership-lock.
            """
        },
    };

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void EachDispositionHasFixedOperatorText(
        Disposition expectedDisposition, string expectedTitle, string expectedExplanation)
    {
        var decision = GoldenDecision(expectedDisposition);
        var explanation = expectedExplanation.ReplaceLineEndings("\n");

        Assert.Equal(expectedDisposition, decision.Disposition);
        Assert.Equal(expectedTitle, decision.Title);
        Assert.Equal(explanation[..explanation.IndexOf('\n')], decision.Summary);
        Assert.Equal(explanation, decision.Explanation);
    }

    [Fact]
    public void ReadyCitesEveryControllingRequiredCheckAndRetainsAllRuleOutcomes()
    {
        var policy = CandidatePolicy();
        var observations = ReadyObservations();
        var factsByKey = observations.ToDictionary(observation => observation.EvidenceKey);

        var decision = Evaluate(policy, observations);

        Assert.Equal(Disposition.ReadyToPrepare, decision.Disposition);
        Assert.Same(policy, decision.Evaluation.Policy);
        Assert.Equal(27, decision.Evaluation.Outcomes.Length);
        Assert.Equal(29, decision.Citations.Length);
        string[] coreRuleIds = ["CORE-OS-ARCHITECTURE", "CORE-OS-RELEASE", "CORE-OS-SUPPORT"];
        Assert.Equal(coreRuleIds, decision.Citations.Take(3).Select(citation => citation.RuleId.Value));
        Assert.All(decision.Citations.Take(3), citation => Assert.Equal(DispositionSource.CoreTarget, citation.Source));
        Assert.Equal(
            policy.Rules.Where(rule => rule.Decision.BlocksReady)
                .Select(rule => rule.Requirement.RequirementId.Value).Order(StringComparer.Ordinal),
            decision.Citations.Skip(3).Select(citation => citation.RuleId.Value));
        Assert.All(decision.Citations.Skip(3), citation => Assert.Equal(DispositionSource.OrganizationPolicy, citation.Source));
        Assert.All(decision.Citations, citation =>
        {
            var fact = factsByKey[citation.EvidenceKey].Metadata;
            var reference = Assert.Single(citation.Evidence);
            Assert.Equal(fact.EvidenceId, reference.EvidenceId);
            Assert.Equal(fact.EvidenceClassification, reference.Classification);
            Assert.Equal(fact.ValueAvailability, reference.Availability);
            Assert.Equal(PolicyRuleState.Met, citation.State);
            Assert.Equal(PolicyRuleReason.ConditionMet, citation.Reason);
        });
        Assert.Contains("25 more controlling rules are listed in the details.", decision.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageAndBatteryFailuresCiteBothRequiredChecksInRuleOrder()
    {
        var decision = Evaluate(CandidatePolicy(), WithFacts(
            LowStorage(),
            Fact("battery-not-present", "battery.present", EvidenceValue.FromBoolean(false))));

        Assert.Equal(Disposition.RepairAndRetest, decision.Disposition);
        Assert.Collection(
            decision.Citations,
            citation => AssertFailure(citation, "POL-BATTERY-001", "battery-not-present"),
            citation => AssertFailure(citation, "POL-STORAGE-002", "storage-below-minimum"));
    }

    [Fact]
    public void AnIndependentBlockingRulePreservesTheNonControllingTargetOutcome()
    {
        var facts = WithFacts(
            Fact("ownership-lock", "ownership.lock_state", EvidenceValue.FromEnum("locked")),
            Fact("other-windows-release", "operating_system.release", EvidenceValue.FromEnum("windows_10")));

        var decision = Evaluate(CandidatePolicy(), facts);

        Assert.Equal(Disposition.DoNotDeploy, decision.Disposition);
        AssertFailure(Assert.Single(decision.Citations), "POL-OWNERSHIP-001", "ownership-lock");
        var release = Assert.Single(decision.TargetOutcomes,
            outcome => outcome.Rule.Requirement.RequirementId.Value == "CORE-OS-RELEASE");
        Assert.Equal(PolicyRuleState.Unmet, release.State);
        Assert.Equal(PolicyRuleReason.ConditionUnmet, release.Reason);
        Assert.Equal("other-windows-release", Assert.Single(release.Evidence).EvidenceId.Value);
    }

    [Theory]
    [InlineData(false, PolicyRuleState.Unmet)]
    [InlineData(true, PolicyRuleState.Unavailable)]
    public void AdvisoryFailureOrMissingAdvisoryDataDoesNotControlDisposition(
        bool missing, PolicyRuleState advisoryState)
    {
        var facts = missing
            ? ReadyObservations().Where(observation => observation.EvidenceKey.Value != "battery.full_charge_ratio_percent")
            : WithFacts(Fact("low-charge-ratio", "battery.full_charge_ratio_percent", EvidenceValue.FromInteger(59), unit: "percent"));
        var decision = Evaluate(CandidatePolicy(), facts);

        Assert.Equal(Disposition.ReadyToPrepare, decision.Disposition);
        Assert.DoesNotContain(decision.Citations, citation => citation.RuleId.Value == "POL-BATTERY-003");
        var advisory = Assert.Single(decision.Evaluation.Outcomes,
            outcome => outcome.Rule.Requirement.RequirementId.Value == "POL-BATTERY-003");
        Assert.Equal(advisoryState, advisory.State);
    }

    [Fact]
    public void ContradictoryTargetEvidenceCitesAllReferencesAndControlsOverRepair()
    {
        var facts = WithFacts(LowStorage()).Concat(new[]
        {
            Fact("a-audit-only", "operating_system.support_state", EvidenceValue.FromEnum("audit_only"), EvidenceClassification.Inferred),
            Fact("b-unsupported", "operating_system.support_state", EvidenceValue.FromEnum("unsupported"), EvidenceClassification.Inferred),
            Fact("c-unknown-support", "operating_system.support_state", null, EvidenceClassification.NotAvailable,
                availability: ValueAvailability.Unknown, limitation: "provider_value_unavailable"),
        }).ToArray();
        var policy = CandidatePolicy();

        var decision = Evaluate(policy, facts);
        var reverse = Evaluate(policy, facts.Reverse());

        Assert.Equal(Disposition.HumanReviewRequired, decision.Disposition);
        Assert.Equal(decision.Explanation, reverse.Explanation);
        Assert.Equal(2, decision.Citations.Length);
        Assert.Equal("CORE-OS-SUPPORT", decision.Citations[0].RuleId.Value);
        Assert.Equal("POL-OS-002", decision.Citations[1].RuleId.Value);
        string[] citedIds = ["SYNTHETIC-EVIDENCE-READY-002", "a-audit-only", "b-unsupported", "c-unknown-support"];
        Assert.All(decision.Citations, citation =>
        {
            Assert.Equal(PolicyRuleState.Unavailable, citation.State);
            Assert.Equal(PolicyRuleReason.EvidenceConflict, citation.Reason);
            Assert.Equal(citedIds, citation.Evidence.Select(reference => reference.EvidenceId.Value));
        });
        Assert.Contains("1 more evidence record is linked to this check.", decision.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("c-unknown-support", decision.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTargetEvidenceBlocksEvenAnAdvisoryOnlyPolicy()
    {
        var facts = ReadyObservations().Where(observation => observation.EvidenceKey.Value != "system.architecture");

        var decision = Evaluate(AdvisoryOnlyPolicy(), facts);

        Assert.Equal(Disposition.HumanReviewRequired, decision.Disposition);
        var citation = Assert.Single(decision.Citations);
        Assert.Equal(DispositionSource.CoreTarget, citation.Source);
        Assert.Equal("CORE-OS-ARCHITECTURE", citation.RuleId.Value);
        Assert.Equal(PolicyRuleReason.EvidenceMissing, citation.Reason);
        Assert.Empty(citation.Evidence);
        Assert.Contains("No saved evidence is linked to this check.", decision.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrecognizedLifecycleValueRequiresReviewInsteadOfAnUnsupportedConclusion()
    {
        var facts = WithFacts(Fact("unrecognized-lifecycle", "operating_system.support_state",
            EvidenceValue.FromEnum("unknown"), EvidenceClassification.Inferred));

        var decision = Evaluate(AdvisoryOnlyPolicy(), facts);

        Assert.Equal(Disposition.HumanReviewRequired, decision.Disposition);
        var citation = Assert.Single(decision.Citations);
        Assert.Equal("CORE-OS-SUPPORT", citation.RuleId.Value);
        Assert.Equal(PolicyRuleState.Unavailable, citation.State);
        Assert.Equal(PolicyRuleReason.EvidenceValueUnrecognized, citation.Reason);
        Assert.Equal("unrecognized-lifecycle", Assert.Single(citation.Evidence).EvidenceId.Value);
        Assert.Contains("has an unrecognized evidence value.", decision.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x86", "windows_11", "supported", "CORE-OS-ARCHITECTURE")]
    [InlineData("x64", "windows_10", "supported", "CORE-OS-RELEASE")]
    [InlineData("x64", "windows_11", "unsupported", "CORE-OS-SUPPORT")]
    public void KnownUnsupportedTargetFactsCannotProduceReadyEvenWithoutOrganizationRules(
        string architecture, string release, string support, string ruleId)
    {
        var facts = WithFacts(
            Fact("architecture-fact", "system.architecture", EvidenceValue.FromEnum(architecture)),
            Fact("os-release-fact", "operating_system.release", EvidenceValue.FromEnum(release)),
            Fact("os-support-fact", "operating_system.support_state", EvidenceValue.FromEnum(support), EvidenceClassification.Inferred));

        var decision = Evaluate(AdvisoryOnlyPolicy(), facts);

        Assert.Equal(Disposition.AlternativeOperatingSystemCandidate, decision.Disposition);
        var citation = Assert.Single(decision.Citations);
        Assert.Equal(DispositionSource.CoreTarget, citation.Source);
        Assert.Equal(ruleId, citation.RuleId.Value);
        Assert.Equal(PolicyRuleState.Unmet, citation.State);
    }

    [Fact]
    public void SerializedDecisionKeepsTraceableReferencesWithoutAnAggregateScoreOrRawFactValues()
    {
        const string rawTestValue = "synthetic-unexpected-state-for-serialization";
        var decision = Evaluate(CandidatePolicy(), WithFacts(
            Fact("storage-health-fact", "storage.system.health_state", EvidenceValue.FromEnum(rawTestValue))));
        var json = JsonSerializer.SerializeToElement(decision);

        Assert.Equal(Disposition.RepairAndRetest, decision.Disposition);
        Assert.Equal(JsonValueKind.String, json.GetProperty("Disposition").ValueKind);
        Assert.Equal("repair_and_retest", json.GetProperty("Disposition").GetString());
        var citation = Assert.Single(json.GetProperty("Citations").EnumerateArray().ToArray());
        Assert.Equal("POL-STORAGE-003", citation.GetProperty("RuleId").GetString());
        var reference = Assert.Single(citation.GetProperty("Evidence").EnumerateArray().ToArray());
        Assert.Equal("storage-health-fact", reference.GetProperty("EvidenceId").GetString());
        Assert.DoesNotContain(PropertyNames(json), property => property.Contains("score", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rawTestValue, json.GetRawText(), StringComparison.Ordinal);
    }

    private static DispositionDecision GoldenDecision(Disposition expected) => expected switch
    {
        Disposition.ReadyToPrepare => Evaluate(AdvisoryOnlyPolicy(), ReadyObservations()),
        Disposition.RepairAndRetest => Evaluate(CandidatePolicy(), WithFacts(LowStorage())),
        Disposition.HumanReviewRequired => Evaluate(CandidatePolicy(), WithFacts(StaleLifecycle())),
        Disposition.AlternativeOperatingSystemCandidate => Evaluate(AdvisoryOnlyPolicy(), WithFacts(
            Fact("other-windows-release", "operating_system.release", EvidenceValue.FromEnum("windows_10")),
            Fact("audit-only-lifecycle", "operating_system.support_state", EvidenceValue.FromEnum("audit_only"), EvidenceClassification.Inferred))),
        Disposition.DoNotDeploy => Evaluate(CandidatePolicy(), WithFacts(
            Fact("ownership-lock", "ownership.lock_state", EvidenceValue.FromEnum("locked")), StaleLifecycle())),
        _ => throw new ArgumentOutOfRangeException(nameof(expected)),
    };

    private static DispositionDecision Evaluate(PolicyDocument policy, IEnumerable<Observation> observations) =>
        DispositionEngine.Evaluate(policy, observations, ActiveCapabilities());

    private static Observation[] WithFacts(params Observation[] replacements)
    {
        var byKey = replacements.ToDictionary(observation => observation.EvidenceKey);
        return ReadyObservations().Select(observation => byKey.GetValueOrDefault(observation.EvidenceKey, observation)).ToArray();
    }

    private static Observation LowStorage() =>
        Fact("storage-below-minimum", "storage.system.free_bytes", EvidenceValue.FromInteger(10_737_418_240), unit: "bytes");

    private static Observation StaleLifecycle() =>
        Fact("stale-lifecycle", "operating_system.support_state", null, EvidenceClassification.NotAvailable,
            availability: ValueAvailability.Unknown, limitation: "provider_value_stale");

    private static PolicyDocument AdvisoryOnlyPolicy()
    {
        var json = JsonNode.Parse(CandidatePolicyJson())!.AsObject();
        var advisory = Assert.Single(json["rules"]!.AsArray(), rule =>
            rule!["id"]!.GetValue<string>() == "POL-BATTERY-003");
        json["rules"] = new JsonArray(advisory!.DeepClone());
        return PolicySchema.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    private static void AssertFailure(DispositionCitation citation, string ruleId, string evidenceId)
    {
        Assert.Equal(DispositionSource.OrganizationPolicy, citation.Source);
        Assert.Equal(ruleId, citation.RuleId.Value);
        Assert.Equal(PolicyRuleState.Unmet, citation.State);
        Assert.Equal(PolicyRuleReason.ConditionUnmet, citation.Reason);
        Assert.Equal(evidenceId, Assert.Single(citation.Evidence).EvidenceId.Value);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var nested in PropertyNames(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in PropertyNames(item))
                {
                    yield return nested;
                }
            }
        }
    }
}
