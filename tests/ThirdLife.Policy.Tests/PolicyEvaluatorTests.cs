using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;
using static ThirdLife.Policy.Tests.PolicyEvaluationFixture;

namespace ThirdLife.Policy.Tests;

public sealed class PolicyEvaluatorTests
{
    [Fact]
    public void CandidateReadyFactsSatisfyEveryActiveRuleWithAttribution()
    {
        var policy = CandidatePolicy();
        var observations = ReadyObservations();
        var factsByKey = observations.ToDictionary(observation => observation.EvidenceKey);

        var evaluation = PolicyEvaluator.Evaluate(policy, observations, ActiveCapabilities());

        Assert.Same(policy, evaluation.Policy);
        Assert.Equal(27, evaluation.Outcomes.Length);
        Assert.Equal(
            policy.Rules.Select(rule => rule.Requirement.RequirementId.Value).Order(StringComparer.Ordinal),
            evaluation.Outcomes.Select(outcome => outcome.Rule.Requirement.RequirementId.Value));
        Assert.All(evaluation.Outcomes, outcome =>
        {
            Assert.Equal(PolicyRuleState.Met, outcome.State);
            Assert.Equal(PolicyRuleReason.ConditionMet, outcome.Reason);
            Assert.Contains(policy.Rules, rule => ReferenceEquals(rule, outcome.Rule));
            var observation = factsByKey[outcome.Rule.Requirement.EvidenceKey];
            AssertReference(Assert.Single(outcome.Evidence), observation);
        });
    }

    [Theory]
    [InlineData("POL-OS-002", PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceMissing, true)]
    [InlineData("POL-STORAGE-002", PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceMissing, true)]
    [InlineData("POL-BATTERY-003", PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceMissing, false)]
    [InlineData("POL-BATTERY-002", PolicyRuleState.PendingConfirmation, PolicyRuleReason.HumanConfirmationRequired, true)]
    [InlineData("POL-FUNCTION-005", PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceMissing, true)]
    public void MissingEvidenceIsExplicitForEveryRequirementType(
        string ruleId, PolicyRuleState state, PolicyRuleReason reason, bool blocksReady)
    {
        var unrelated = Fact("unrelated-fact", "unrelated.fact", EvidenceValue.FromBoolean(true));
        var outcome = GetOutcome(ruleId, [unrelated], ActiveCapabilities());

        Assert.Equal(state, outcome.State);
        Assert.Equal(reason, outcome.Reason);
        Assert.Equal(blocksReady, outcome.Rule.Decision.BlocksReady);
        Assert.Empty(outcome.Evidence);
    }

    [Theory]
    [InlineData(ValueAvailability.Unknown, EvidenceClassification.NotAvailable)]
    [InlineData(ValueAvailability.NotApplicable, EvidenceClassification.Observed)]
    public void UnusableEvidenceRemainsCitable(ValueAvailability availability, EvidenceClassification classification)
    {
        var unavailable = Fact("storage-unavailable", "storage.system.free_bytes", null,
            classification, availability: availability, limitation: "source_unavailable");
        var outcome = GetOutcome("POL-STORAGE-002", [unavailable]);

        Assert.Equal(PolicyRuleState.Unavailable, outcome.State);
        Assert.Equal(PolicyRuleReason.EvidenceUnavailable, outcome.Reason);
        AssertReference(Assert.Single(outcome.Evidence), unavailable);
    }

    [Theory]
    [InlineData("provider_value_stale", PolicyRuleReason.EvidenceStale)]
    [InlineData("provider_values_conflict", PolicyRuleReason.EvidenceConflict)]
    public void KnownStaleOrConflictingEvidenceCannotSupportWindows(
        string limitation, PolicyRuleReason reason)
    {
        var lifecycle = Fact("lifecycle-unavailable", "operating_system.support_state", null,
            EvidenceClassification.NotAvailable, availability: ValueAvailability.Unknown, limitation: limitation);
        var outcome = GetOutcome("POL-OS-002", [lifecycle]);

        Assert.Equal(PolicyRuleState.Unavailable, outcome.State);
        Assert.Equal(reason, outcome.Reason);
        AssertReference(Assert.Single(outcome.Evidence), lifecycle);
    }

    [Theory]
    [InlineData(EvidenceClassification.Observed, "no_swelling_observed", PolicyRuleState.PendingConfirmation, PolicyRuleReason.HumanConfirmationRequired)]
    [InlineData(EvidenceClassification.Inferred, "no_swelling_observed", PolicyRuleState.PendingConfirmation, PolicyRuleReason.HumanConfirmationRequired)]
    [InlineData(EvidenceClassification.HumanConfirmed, "no_swelling_observed", PolicyRuleState.Met, PolicyRuleReason.ConditionMet)]
    [InlineData(EvidenceClassification.HumanConfirmed, "swelling_observed", PolicyRuleState.Unmet, PolicyRuleReason.ConditionUnmet)]
    public void HumanRequirementsNeedAttributableConfirmation(
        EvidenceClassification classification, string value, PolicyRuleState state, PolicyRuleReason reason)
    {
        var observation = Fact("battery-inspection", "battery.swelling_inspection", EvidenceValue.FromEnum(value), classification);
        var outcome = GetOutcome("POL-BATTERY-002", [observation]);

        Assert.Equal(state, outcome.State);
        Assert.Equal(reason, outcome.Reason);
        AssertReference(Assert.Single(outcome.Evidence), observation);
    }

    [Fact]
    public void UnknownCapabilitySelectionRemainsDistinctFromAnAbsentCapability()
    {
        var audio = Fact("audio-check", "function.audio_output", EvidenceValue.FromEnum("failed"),
            EvidenceClassification.HumanConfirmed);

        var unknown = GetOutcome("POL-FUNCTION-005", [audio]);
        var absent = GetOutcome("POL-FUNCTION-005", [audio], []);
        var unrelated = GetOutcome("POL-FUNCTION-005", [audio], ["web_browsing"]);
        var selected = GetOutcome("POL-FUNCTION-005", [audio], ["video_calling"]);

        Assert.Equal(PolicyRuleState.Unavailable, unknown.State);
        Assert.Equal(PolicyRuleReason.CapabilitySelectionUnavailable, unknown.Reason);
        Assert.Equal(PolicyRuleState.Inactive, absent.State);
        Assert.Equal(PolicyRuleReason.CapabilityInactive, absent.Reason);
        Assert.Equal(PolicyRuleState.Inactive, unrelated.State);
        Assert.Empty(absent.Evidence);
        Assert.Empty(unrelated.Evidence);
        Assert.Equal(PolicyRuleState.Unmet, selected.State);
        Assert.Equal(PolicyRuleReason.ConditionUnmet, selected.Reason);
        AssertReference(Assert.Single(selected.Evidence), audio);
    }

    [Theory]
    [InlineData(34_359_738_367L, "bytes", PolicyRuleState.Unmet, PolicyRuleReason.ConditionUnmet)]
    [InlineData(34_359_738_368L, "bytes", PolicyRuleState.Met, PolicyRuleReason.ConditionMet)]
    [InlineData(34_359_738_369L, "bytes", PolicyRuleState.Met, PolicyRuleReason.ConditionMet)]
    [InlineData(34_359_738_368L, "percent", PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceTypeOrUnitMismatch)]
    [InlineData(34_359_738_368L, null, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceTypeOrUnitMismatch)]
    [InlineData(-1L, "bytes", PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceTypeOrUnitMismatch)]
    public void MinimumUsesAnInclusiveThresholdAndExactUnit(
        long value, string? unit, PolicyRuleState state, PolicyRuleReason reason)
    {
        var observation = Fact("free-space", "storage.system.free_bytes", EvidenceValue.FromInteger(value), unit: unit);
        var outcome = GetOutcome("POL-STORAGE-002", [observation]);

        Assert.Equal(state, outcome.State);
        Assert.Equal(reason, outcome.Reason);
        AssertReference(Assert.Single(outcome.Evidence), observation);
    }

    [Theory]
    [InlineData(-1L, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceTypeOrUnitMismatch)]
    [InlineData(59L, PolicyRuleState.Unmet, PolicyRuleReason.ConditionUnmet)]
    [InlineData(60L, PolicyRuleState.Met, PolicyRuleReason.ConditionMet)]
    [InlineData(100L, PolicyRuleState.Met, PolicyRuleReason.ConditionMet)]
    [InlineData(101L, PolicyRuleState.Met, PolicyRuleReason.ConditionMet)]
    public void AdvisoryPercentCanExceedDesignCapacityAndNeverBlocksReady(
        long percent, PolicyRuleState state, PolicyRuleReason reason)
    {
        var observation = Fact("battery-ratio", "battery.full_charge_ratio_percent",
            EvidenceValue.FromInteger(percent), EvidenceClassification.Inferred, unit: "percent");
        var outcome = GetOutcome("POL-BATTERY-003", [observation]);

        Assert.Equal(state, outcome.State);
        Assert.Equal(reason, outcome.Reason);
        Assert.False(outcome.Rule.Decision.BlocksReady);
        AssertReference(Assert.Single(outcome.Evidence), observation);
    }

    [Fact]
    public void RejectsValueCoercionAndUnexpectedUnits()
    {
        var facts = new[]
        {
            Fact("binary-as-integer", "battery.present", EvidenceValue.FromInteger(1)),
            Fact("bytes-as-decimal", "memory.installed_bytes", EvidenceValue.FromDecimal(17_179_869_184m), unit: "bytes"),
            Fact("numeric-sanitization", "sanitization.state", EvidenceValue.FromInteger(1)),
            Fact("percent-for-an-enum", "system.architecture", EvidenceValue.FromEnum("x64"), unit: "percent"),
        };

        var outcomes = PolicyEvaluator.Evaluate(CandidatePolicy(), facts, []).Outcomes;

        foreach (var fact in facts)
        {
            var outcome = Assert.Single(outcomes, outcome => outcome.Rule.Requirement.EvidenceKey == fact.EvidenceKey);
            Assert.Equal(PolicyRuleState.Unavailable, outcome.State);
            Assert.Equal(PolicyRuleReason.EvidenceTypeOrUnitMismatch, outcome.Reason);
            AssertReference(Assert.Single(outcome.Evidence), fact);
        }
    }

    [Theory]
    [InlineData("replacement_storage", PolicyRuleState.Met)]
    [InlineData("Replacement_Storage", PolicyRuleState.Unmet)]
    [InlineData("failed", PolicyRuleState.Unmet)]
    public void OneOfComparesTextAndEnumTokensOrdinally(string value, PolicyRuleState state)
    {
        var asText = Fact("sanitization-as-text", "sanitization.state", EvidenceValue.FromString(value));
        var asEnum = Fact("sanitization-as-enum", "sanitization.state", EvidenceValue.FromEnum(value));

        Assert.Equal(state, GetOutcome("POL-SAN-001", [asText]).State);
        Assert.Equal(state, GetOutcome("POL-SAN-001", [asEnum]).State);
    }

    [Fact]
    public void RepeatedIdenticalFactsDoNotCreateAmbiguityOrDuplicateReferences()
    {
        var lifecycle = Fact("support-fact", "operating_system.support_state",
            EvidenceValue.FromEnum("supported"), EvidenceClassification.Inferred);
        var outcome = GetOutcome("POL-OS-002", [lifecycle, lifecycle with { }]);

        Assert.Equal(PolicyRuleState.Met, outcome.State);
        AssertReference(Assert.Single(outcome.Evidence), lifecycle);
    }

    [Fact]
    public void ReusedEvidenceIdWithDifferentContentFailsClosed()
    {
        var supported = Fact("reused-id", "operating_system.support_state", EvidenceValue.FromEnum("supported"));
        var unsupported = Fact("reused-id", "operating_system.support_state", EvidenceValue.FromEnum("unsupported"));
        var differentKey = Fact("reused-id", "storage.system.health_state", EvidenceValue.FromEnum("healthy"));
        var policy = CandidatePolicy();

        Assert.Throws<ArgumentException>(() => PolicyEvaluator.Evaluate(policy, [supported, unsupported], []));
        Assert.Throws<ArgumentException>(() => PolicyEvaluator.Evaluate(policy, [supported, differentKey], []));
    }

    [Fact]
    public void ConflictingFactsAndOrdinaryResultsAreIndependentOfEnumerationOrder()
    {
        var ready = ReadyObservations();
        var conflictingLifecycle = Fact("additional-os-support", "operating_system.support_state",
            EvidenceValue.FromEnum("unsupported"), EvidenceClassification.Inferred);
        var observations = ready.Append(conflictingLifecycle).ToArray();
        var snapshot = observations.ToArray();
        var capabilities = ActiveCapabilities();
        var policy = CandidatePolicy();
        var evaluation = PolicyEvaluator.Evaluate(policy, observations, capabilities);
        var conflict = Assert.Single(evaluation.Outcomes, outcome => outcome.Rule.Requirement.RequirementId.Value == "POL-OS-002");
        var expected = Describe(evaluation);

        Assert.Equal(PolicyRuleState.Unavailable, conflict.State);
        Assert.Equal(PolicyRuleReason.EvidenceConflict, conflict.Reason);
        string[] expectedEvidenceIds = ["SYNTHETIC-EVIDENCE-READY-002", "additional-os-support"];
        Assert.Equal(
            expectedEvidenceIds,
            conflict.Evidence.Select(reference => reference.EvidenceId.Value));
        Assert.Equal(snapshot, observations);
        Assert.Equal(ActiveCapabilities(), capabilities);

        var random = new Random(4_201);
        for (var trial = 0; trial < 40; trial++)
        {
            var shuffledFacts = observations.ToArray();
            var shuffledCapabilities = capabilities.ToArray();
            random.Shuffle(shuffledFacts);
            random.Shuffle(shuffledCapabilities);
            var shuffledPolicy = ShufflePolicyRules(random);
            var originalRuleOrder = shuffledPolicy.Rules.ToArray();

            var actual = PolicyEvaluator.Evaluate(shuffledPolicy, shuffledFacts, shuffledCapabilities);

            Assert.Equal(expected, Describe(actual));
            Assert.Equal(originalRuleOrder, shuffledPolicy.Rules.AsEnumerable());
        }

        Assert.Equal(expected, Describe(PolicyEvaluator.Evaluate(policy, observations, capabilities)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../video_calling")]
    [InlineData("VIDEO_CALLING")]
    public void RejectsMalformedCapabilityIds(string capability)
    {
        var policy = CandidatePolicy();

        Assert.Throws<ArgumentException>(() => PolicyEvaluator.Evaluate(policy, [], [capability]));
    }

    [Fact]
    public void RejectsInputsOverTheirBoundsEvenIfRepeatedItemsCouldBeDeduplicated()
    {
        var observation = Fact("unrelated-fact", "unrelated.fact", EvidenceValue.FromBoolean(true));
        var policy = CandidatePolicy();
        var capabilities = Enumerable.Range(0, 128)
            .Select(index => string.Create(CultureInfo.InvariantCulture, $"capability_{index}"));

        Assert.Equal(27, PolicyEvaluator.Evaluate(policy, Enumerable.Repeat(observation, 10_000), capabilities).Outcomes.Length);
        Assert.Throws<ArgumentException>(() => PolicyEvaluator.Evaluate(policy, Enumerable.Repeat(observation, 10_001), []));
        Assert.Throws<ArgumentException>(() => PolicyEvaluator.Evaluate(policy, [], capabilities.Append("overflow")));
    }

    private static PolicyRuleOutcome GetOutcome(
        string ruleId, IReadOnlyCollection<Observation> evidence, IEnumerable<string>? capabilities = null) =>
        Assert.Single(PolicyEvaluator.Evaluate(CandidatePolicy(), evidence, capabilities).Outcomes,
            outcome => outcome.Rule.Requirement.RequirementId.Value == ruleId);

    private static void AssertReference(PolicyEvidenceReference reference, Observation observation)
    {
        Assert.Equal(observation.Metadata.EvidenceId, reference.EvidenceId);
        Assert.Equal(observation.Metadata.EvidenceClassification, reference.Classification);
        Assert.Equal(observation.Metadata.ValueAvailability, reference.Availability);
    }

    private static PolicyDocument ShufflePolicyRules(Random random)
    {
        var json = JsonNode.Parse(CandidatePolicyJson())!.AsObject();
        var rules = json["rules"]!.AsArray().Select(rule => rule!.DeepClone()).ToArray();
        random.Shuffle(rules);
        json["rules"] = new JsonArray(rules);

        return PolicySchema.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    private static string[] Describe(PolicyEvaluation evaluation) =>
        evaluation.Outcomes.Select(outcome =>
        {
            var references = string.Join(",", outcome.Evidence.Select(reference =>
                $"{reference.EvidenceId.Value}|{reference.Classification}|{reference.Availability}"));
            return $"{outcome.Rule.Requirement.RequirementId.Value}|{outcome.State}|{outcome.Reason}|{references}";
        }).ToArray();
}
