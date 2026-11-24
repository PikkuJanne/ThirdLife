using System.Collections.Immutable;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Evaluation;

public enum PolicyRuleState
{
    Met = 1,
    Unmet,
    Unavailable,
    PendingConfirmation,
    Inactive,
}

public enum PolicyRuleReason
{
    ConditionMet = 1,
    ConditionUnmet,
    EvidenceMissing,
    EvidenceUnavailable,
    EvidenceConflict,
    EvidenceStale,
    EvidenceTypeOrUnitMismatch,
    HumanConfirmationRequired,
    CapabilitySelectionUnavailable,
    CapabilityInactive,
    EvidenceValueUnrecognized,
}

public sealed record PolicyEvidenceReference(
    EvidenceId EvidenceId, EvidenceClassification Classification, ValueAvailability Availability);

public sealed record PolicyRuleOutcome(
    PolicyRule Rule, PolicyRuleState State, PolicyRuleReason Reason, ImmutableArray<PolicyEvidenceReference> Evidence);

public sealed class PolicyEvaluation
{
    internal PolicyEvaluation(PolicyDocument policy, ImmutableArray<PolicyRuleOutcome> outcomes)
    {
        Policy = policy;
        Outcomes = outcomes;
    }

    public PolicyDocument Policy { get; }

    public ImmutableArray<PolicyRuleOutcome> Outcomes { get; }
}
