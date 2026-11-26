using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Decisions;

namespace ThirdLife.Actions.Planning;

public sealed class ProposedPlan
{
    private readonly PlanSubject _subject;

    internal ProposedPlan(
        PlanSubject subject,
        DispositionDecision? policyDecision,
        PlanBlockReason? blockedReason,
        ImmutableArray<PlannedAction> actions,
        ImmutableArray<ProfileRecipientChoice> recipientChoices,
        IEnumerable<ScheduledAction>? steps = null)
    {
        _subject = subject;
        JobId = subject.JobId;
        GateDecisionId = subject.GateDecisionId;
        ProfileSha256 = subject.ProfileSha256;
        CatalogSha256 = subject.CatalogSha256;
        PolicySha256 = subject.PolicySha256;
        EvidenceSha256 = subject.EvidenceSha256;
        SyntheticData = subject.SyntheticData;
        ActiveCapabilityIds = subject.ActiveCapabilityIds;
        PolicyDecision = policyDecision;
        BlockedReason = blockedReason;
        Actions = actions;
        RecipientChoices = recipientChoices;
        Schedule = steps is null ? [] : ActionSchedule.Order(actions.Select(action => action.Request.ActionId), steps);
        ContentSha256 = PlanIdentity.PlanHash(subject, policyDecision, blockedReason, actions, recipientChoices, Schedule);
    }

    public JobId JobId { get; }

    public SanitizationGateDecisionId? GateDecisionId { get; }

    public string ProfileSha256 { get; }

    public string CatalogSha256 { get; }

    public string PolicySha256 { get; }

    public string? EvidenceSha256 { get; }

    public bool SyntheticData { get; }

    public ImmutableArray<string> ActiveCapabilityIds { get; }

    public DispositionDecision? PolicyDecision { get; }

    public PlanBlockReason? BlockedReason { get; }

    public ImmutableArray<PlannedAction> Actions { get; }

    public ImmutableArray<ProfileRecipientChoice> RecipientChoices { get; }

    public ImmutableArray<ScheduledAction> Schedule { get; }

    public string ContentSha256 { get; }

    /// <summary>Creates a reviewable proposal with a validated, immutable action graph.</summary>
    public ProposedPlan WithSchedule(IEnumerable<ScheduledAction> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (BlockedReason is not null || Actions.IsEmpty)
        {
            throw new InvalidOperationException("An action schedule requires a nonblocked proposal with at least one action.");
        }

        return new ProposedPlan(_subject, PolicyDecision, BlockedReason, Actions, RecipientChoices, steps);
    }
}

internal sealed record PlanSubject(
    JobId JobId,
    SanitizationGateDecisionId? GateDecisionId,
    string ProfileSha256,
    string CatalogSha256,
    string PolicySha256,
    string? EvidenceSha256,
    bool SyntheticData,
    ImmutableArray<string> ActiveCapabilityIds);

public sealed record PlannedAction(
    string CapabilityId,
    string ReasonCode,
    CapabilityIntent Intent,
    ActionRequest Request,
    ActionDefinition Definition,
    CatalogApplication CatalogEntry);

public enum PlanBlockReason
{
    SanitizationRequired = 1,
    ProfileCatalogMismatch,
    ProfilePolicyMismatch,
    PolicyNotApplicable,
    CapabilitySelectionInvalid,
    EvidenceInvalid,
    PolicyBlocksPreparation,
    PackageNotApplicable,
}
