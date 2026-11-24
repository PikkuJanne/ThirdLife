using System.Collections.Immutable;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Evaluation;

public static class PolicyEvaluator
{
    public const int MaximumObservations = 10_000;
    public const int MaximumCapabilityIds = 128;

    /// <summary>
    /// Evaluates one evidence snapshot. A null capability selection means the selection is unavailable.
    /// An empty selection means it is known to contain no active capabilities.
    /// </summary>
    public static PolicyEvaluation Evaluate(
        PolicyDocument policy,
        IEnumerable<Observation> observations,
        IEnumerable<string>? activeCapabilityIds) =>
        EvaluateWithTarget(policy, observations, activeCapabilityIds, []).Evaluation;

    internal static (PolicyEvaluation Evaluation, ImmutableArray<PolicyRuleOutcome> Target) EvaluateWithTarget(
        PolicyDocument policy,
        IEnumerable<Observation> observations,
        IEnumerable<string>? activeCapabilityIds,
        ImmutableArray<TargetRule> targetRules)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(observations);

        var referencedKeys = policy.Rules.Select(rule => rule.Requirement.EvidenceKey)
            .Concat(targetRules.Select(target => target.Rule.Requirement.EvidenceKey))
            .ToHashSet();
        var observationsByKey = IndexByKey(observations, referencedKeys);
        var capabilities = activeCapabilityIds is null ? null : CollectCapabilities(activeCapabilityIds);
        var outcomes = policy.Rules
            .OrderBy(rule => rule.Requirement.RequirementId.Value, StringComparer.Ordinal)
            .Select(rule => EvaluateRule(rule, observationsByKey.GetValueOrDefault(rule.Requirement.EvidenceKey), capabilities))
            .ToImmutableArray();
        var targetOutcomes = targetRules
            .OrderBy(target => target.Rule.Requirement.RequirementId.Value, StringComparer.Ordinal)
            .Select(target => EvaluateRule(
                target.Rule, observationsByKey.GetValueOrDefault(target.Rule.Requirement.EvidenceKey), capabilities, target.KnownValues))
            .ToImmutableArray();
        return (new PolicyEvaluation(policy, outcomes), targetOutcomes);
    }

    private static Dictionary<EvidenceKey, IndexedEvidence> IndexByKey(
        IEnumerable<Observation> observations,
        HashSet<EvidenceKey> referencedKeys)
    {
        var observationsByKey = new Dictionary<EvidenceKey, List<Observation>>();
        var observationsById = new Dictionary<EvidenceId, Observation>();
        var count = 0;
        foreach (var observation in observations)
        {
            if (++count > MaximumObservations || observation is null)
            {
                throw new ArgumentException("The evidence snapshot exceeds its observation limit or contains a null item.", nameof(observations));
            }

            if (!observationsById.TryAdd(observation.Metadata.EvidenceId, observation))
            {
                if (observation != observationsById[observation.Metadata.EvidenceId])
                {
                    throw new ArgumentException("An evidence ID refers to multiple observations.", nameof(observations));
                }

                continue;
            }

            if (!referencedKeys.Contains(observation.EvidenceKey))
            {
                continue;
            }

            if (!observationsByKey.TryGetValue(observation.EvidenceKey, out var entries))
            {
                entries = [];
                observationsByKey.Add(observation.EvidenceKey, entries);
            }

            entries.Add(observation);
        }

        return observationsByKey.ToDictionary(
            group => group.Key,
            group => new IndexedEvidence(
                group.Value.Count == 1 ? group.Value[0] : null,
                group.Value.OrderBy(observation => observation.Metadata.EvidenceId.Value, StringComparer.Ordinal)
                    .Select(Reference)
                    .ToImmutableArray()));
    }

    private static HashSet<string> CollectCapabilities(IEnumerable<string> activeCapabilityIds)
    {
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var id in activeCapabilityIds)
        {
            if (++count > MaximumCapabilityIds || id is null || !PolicySchema.IsCapabilityId(id))
            {
                throw new ArgumentException("The capability selection has an invalid ID or exceeds its limit.", nameof(activeCapabilityIds));
            }

            capabilities.Add(id);
        }

        return capabilities;
    }

    private static PolicyRuleOutcome EvaluateRule(
        PolicyRule rule,
        IndexedEvidence? input,
        HashSet<string>? capabilities,
        ImmutableArray<string> knownValues = default)
    {
        if (rule.Requirement.RequirementType == RequirementType.ProfileDependent)
        {
            if (capabilities is null)
            {
                return new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.CapabilitySelectionUnavailable, []);
            }

            if (!rule.ActiveForCapabilityIds.Any(capabilities.Contains))
            {
                return new(rule, PolicyRuleState.Inactive, PolicyRuleReason.CapabilityInactive, []);
            }
        }

        var needsConfirmation = rule.Requirement.RequirementType == RequirementType.HumanConfirmed;
        if (input is null)
        {
            return needsConfirmation
                ? new(rule, PolicyRuleState.PendingConfirmation, PolicyRuleReason.HumanConfirmationRequired, [])
                : new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceMissing, []);
        }

        if (input.Single is not { } evidence)
        {
            return new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceConflict, input.References);
        }

        var cited = input.References;
        switch (evidence.LimitationCode)
        {
            case "provider_values_conflict":
                return new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceConflict, cited);
            case "provider_value_stale":
                return new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceStale, cited);
        }

        var available = evidence.Metadata.ValueAvailability == ValueAvailability.Available;
        if (needsConfirmation && (!available || evidence.Metadata.EvidenceClassification != EvidenceClassification.HumanConfirmed))
        {
            return new(rule, PolicyRuleState.PendingConfirmation, PolicyRuleReason.HumanConfirmationRequired, cited);
        }

        if (!available || evidence.LimitationCode is not null)
        {
            return new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceUnavailable, cited);
        }

        if (!knownValues.IsDefaultOrEmpty &&
            (evidence.Value?.StringValue is not { } token || !knownValues.Contains(token, StringComparer.Ordinal)))
        {
            return new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceValueUnrecognized, cited);
        }

        return Matches(rule.Condition, evidence) switch
        {
            true => new(rule, PolicyRuleState.Met, PolicyRuleReason.ConditionMet, cited),
            false => new(rule, PolicyRuleState.Unmet, PolicyRuleReason.ConditionUnmet, cited),
            null => new(rule, PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceTypeOrUnitMismatch, cited),
        };
    }

    private static PolicyEvidenceReference Reference(Observation observation) =>
        new(observation.Metadata.EvidenceId, observation.Metadata.EvidenceClassification, observation.Metadata.ValueAvailability);

    private static bool? Matches(PolicyCondition condition, Observation evidence) => condition switch
    {
        EqualCondition equals when evidence.Unit is null => MatchesEqual(equals.Value, evidence.Value!),
        OneOfCondition oneOf when evidence.Unit is null && IsToken(evidence.Value!) =>
            oneOf.Values.Contains(evidence.Value!.StringValue!, StringComparer.Ordinal),
        MinimumIntegerCondition minimum => MeetsMinimum(minimum, evidence),
        _ => null,
    };

    private static bool IsToken(EvidenceValue value) => value.Kind is EvidenceValueKind.Text or EvidenceValueKind.Enum;

    private static bool? MatchesEqual(EvidenceValue expected, EvidenceValue actual)
    {
        if (IsToken(expected) && IsToken(actual))
        {
            return string.Equals(expected.StringValue, actual.StringValue, StringComparison.Ordinal);
        }

        if (expected.Kind != actual.Kind)
        {
            return null;
        }

        return expected.Kind switch
        {
            EvidenceValueKind.Boolean => expected.BooleanValue == actual.BooleanValue,
            EvidenceValueKind.WholeNumber => expected.IntegerValue == actual.IntegerValue,
            _ => null,
        };
    }

    private static bool? MeetsMinimum(MinimumIntegerCondition minimum, Observation evidence)
    {
        if (evidence.Value?.Kind != EvidenceValueKind.WholeNumber ||
            evidence.Value.IntegerValue is not long value || value < 0)
        {
            return null;
        }

        var compatible = minimum.Unit switch
        {
            PolicyThresholdUnit.Bytes => evidence.Unit == "bytes",
            PolicyThresholdUnit.Percent => evidence.Unit == "percent",
            _ => false,
        };
        return compatible ? value >= minimum.Value : null;
    }

    private sealed record IndexedEvidence(Observation? Single, ImmutableArray<PolicyEvidenceReference> References);

    internal sealed record TargetRule(PolicyRule Rule, ImmutableArray<string> KnownValues);
}
