using System.Collections.Immutable;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Decisions;

public static class DispositionEngine
{
    public static DispositionDecision Evaluate(
        PolicyDocument policy,
        IEnumerable<Observation> observations,
        IEnumerable<string>? activeCapabilityIds) =>
        EvaluateCore(policy, observations, activeCapabilityIds, exceptionHistory: null, evaluatedAtUtc: null);

    public static DispositionDecision Evaluate(
        PolicyDocument policy,
        IEnumerable<Observation> observations,
        IEnumerable<string>? activeCapabilityIds,
        PolicyExceptionHistory exceptionHistory,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(exceptionHistory);
        var utc = evaluatedAtUtc.ToUniversalTime();
        if (utc == default)
        {
            throw new ArgumentException("An evaluation timestamp is required.", nameof(evaluatedAtUtc));
        }

        return EvaluateCore(policy, observations, activeCapabilityIds, exceptionHistory, utc);
    }

    private static DispositionDecision EvaluateCore(
        PolicyDocument policy,
        IEnumerable<Observation> observations,
        IEnumerable<string>? activeCapabilityIds,
        PolicyExceptionHistory? exceptionHistory,
        DateTimeOffset? evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var (evaluation, target) = PolicyEvaluator.EvaluateWithTarget(
            policy, observations, activeCapabilityIds, TargetRules(policy.Target));
        var details = target.Select(outcome => new DispositionRuleDetail(DispositionSource.CoreTarget, outcome, null))
            .Concat(evaluation.Outcomes.Select(outcome => new DispositionRuleDetail(
                DispositionSource.OrganizationPolicy,
                outcome,
                exceptionHistory?.FindApplicable(policy, outcome, evaluatedAtUtc!.Value))))
            .ToImmutableArray();

        // The more restrictive disposition controls. Preserve other rule outcomes for review.
        var disposition = Disposition.ReadyToPrepare;
        var citations = ImmutableArray.CreateBuilder<DispositionCitation>();
        foreach (var detail in details)
        {
            var outcome = detail.Outcome;
            if (!outcome.Rule.Decision.BlocksReady || outcome.State == PolicyRuleState.Inactive)
            {
                continue;
            }

            var candidate = detail.Exception is null ? RuleDisposition(outcome) : Disposition.ReadyToPrepare;
            if (Priority(candidate) < Priority(disposition))
            {
                continue;
            }

            if (candidate != disposition)
            {
                disposition = candidate;
                citations.Clear();
            }

            citations.Add(new DispositionCitation(
                detail.Source,
                outcome.Rule.Requirement.RequirementId,
                outcome.Rule.Requirement.EvidenceKey,
                outcome.State,
                outcome.Reason,
                outcome.Evidence,
                detail.Exception));
        }

        return new DispositionDecision(disposition, evaluation, target, citations.ToImmutable(), details, exceptionHistory, evaluatedAtUtc);
    }

    private static Disposition RuleDisposition(PolicyRuleOutcome outcome) => outcome.State switch
    {
        PolicyRuleState.Met => Disposition.ReadyToPrepare,
        PolicyRuleState.Unmet => outcome.Rule.Decision.UnmetDisposition ?? Disposition.HumanReviewRequired,
        _ => outcome.Rule.Decision.UnavailableDisposition ?? Disposition.HumanReviewRequired,
    };

    private static int Priority(Disposition disposition) => disposition switch
    {
        Disposition.DoNotDeploy => 5,
        Disposition.HumanReviewRequired => 4,
        Disposition.AlternativeOperatingSystemCandidate => 3,
        Disposition.RepairAndRetest => 2,
        Disposition.ReadyToPrepare => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };

    private static ImmutableArray<PolicyEvaluator.TargetRule> TargetRules(PolicyTarget target) =>
    [
        TargetRule("CORE-OS-ARCHITECTURE", "system.architecture", target.Architecture, ["x64", "x86", "arm64", "arm32", "ia64"]),
        TargetRule("CORE-OS-RELEASE", "operating_system.release", target.OperatingSystem, ["windows_11", "windows_10"]),
        TargetRule("CORE-OS-SUPPORT", "operating_system.support_state", target.SupportState, ["supported", "unsupported", "audit_only"]),
    ];

    private static PolicyEvaluator.TargetRule TargetRule(
        string id, string evidenceKey, string expected, ImmutableArray<string> knownValues) =>
        new(
            new PolicyRule(
                new RequirementReference(new RequirementId(id), new EvidenceKey(evidenceKey), RequirementType.Blocking, RequirementSeverity.Critical),
                [],
                new EqualCondition(EvidenceValue.FromEnum(expected)),
                new PolicyRuleDecision(true, Disposition.AlternativeOperatingSystemCandidate, Disposition.HumanReviewRequired)),
            knownValues);
}
