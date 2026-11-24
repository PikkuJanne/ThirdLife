using System.Collections.Immutable;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Evaluation;

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
    ImmutableArray<PolicyEvidenceReference> Evidence);

public sealed class DispositionDecision
{
    internal DispositionDecision(
        Disposition disposition,
        PolicyEvaluation evaluation,
        ImmutableArray<PolicyRuleOutcome> targetOutcomes,
        ImmutableArray<DispositionCitation> citations)
    {
        Disposition = disposition;
        Evaluation = evaluation;
        TargetOutcomes = targetOutcomes;
        Citations = citations;
        (Title, Summary, Explanation) = DispositionExplanation.Describe(disposition, citations);
    }

    public Disposition Disposition { get; }

    public string Title { get; }

    public string Summary { get; }

    public string Explanation { get; }

    public PolicyEvaluation Evaluation { get; }

    public ImmutableArray<PolicyRuleOutcome> TargetOutcomes { get; }

    public ImmutableArray<DispositionCitation> Citations { get; }
}
