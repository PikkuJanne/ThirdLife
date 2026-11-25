using System.Collections.Immutable;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;

namespace ThirdLife.Policy.Decisions;

public enum DispositionSource
{
    CoreTarget = 1,
    OrganizationPolicy,
}

public sealed record DispositionCitation(
    DispositionSource Source,
    RequirementId RuleId,
    EvidenceKey EvidenceKey,
    PolicyRuleState State,
    PolicyRuleReason Reason,
    ImmutableArray<PolicyEvidenceReference> Evidence,
    PolicyExceptionRecord? Exception = null);

public sealed class DispositionDecision
{
    internal DispositionDecision(
        Disposition disposition,
        PolicyEvaluation evaluation,
        ImmutableArray<PolicyRuleOutcome> targetOutcomes,
        ImmutableArray<DispositionCitation> citations,
        ImmutableArray<DispositionRuleDetail> ruleDetails,
        PolicyExceptionHistory? exceptionHistory,
        DateTimeOffset? evaluatedAtUtc)
    {
        Disposition = disposition;
        Evaluation = evaluation;
        TargetOutcomes = targetOutcomes;
        Citations = citations;
        RuleDetails = ruleDetails;
        ExceptionHistory = exceptionHistory;
        EvaluatedAtUtc = evaluatedAtUtc;
        (Title, Summary, Explanation) = DispositionExplanation.Describe(disposition, citations, evaluation.Policy.IsCurrentSchema);
    }

    public Disposition Disposition { get; }

    public string Title { get; }

    public string Summary { get; }

    public string Explanation { get; }

    public PolicyEvaluation Evaluation { get; }

    public ImmutableArray<PolicyRuleOutcome> TargetOutcomes { get; }

    public ImmutableArray<DispositionCitation> Citations { get; }

    public ImmutableArray<DispositionRuleDetail> RuleDetails { get; }

    public JobId? JobId => ExceptionHistory?.JobId;

    public DateTimeOffset? EvaluatedAtUtc { get; }

    internal PolicyExceptionHistory? ExceptionHistory { get; }
}
