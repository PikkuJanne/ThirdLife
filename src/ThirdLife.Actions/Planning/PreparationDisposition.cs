using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Actions.Planning;

public enum PreparationActionEffect
{
    Verified,
    OptionalOmission,
    ReviewRequired,
    AuthorizedException,
}

public sealed record PreparationActionOutcome(JournalAction Action, JournalEntry? Entry,
    PreparationActionEffect Effect, PolicyExceptionRecord? Exception, bool IsCurrent)
{
    public JournalState State => Entry?.State ?? JournalState.Planned;
}

/// <summary>Evaluates completion of the actions in one exact saved plan.</summary>
/// <remarks>The saved assessment policy decision and other handover requirements remain independent inputs.</remarks>
public sealed class PreparationDisposition
{
    // The catalog's closed workshop capability list cannot activate this rule during plan resolution.
    private const string OutcomePolicyCapability = "prepared_action_outcomes";

    private PreparationDisposition(JournalPlanKey plan, ImmutableArray<PreparationActionOutcome> actions,
        ImmutableArray<PreparationActionOutcome> history, bool hasApproval)
    {
        Plan = plan;
        Actions = actions;
        History = history;
        Disposition = !hasApproval || actions.Any(action => action.Effect == PreparationActionEffect.ReviewRequired)
            ? Disposition.HumanReviewRequired
            : Disposition.ReadyToPrepare;
    }

    public JournalPlanKey Plan { get; }
    public Disposition Disposition { get; }
    public ImmutableArray<PreparationActionOutcome> Actions { get; }
    public ImmutableArray<PreparationActionOutcome> History { get; }

    public static PreparationDisposition Evaluate(JournalHistory history, PolicyDocument? policy = null,
        PolicyExceptionHistory? exceptions = null, DateTimeOffset? evaluatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(history);

        var latest = new Dictionary<Guid, JournalEntry>();
        foreach (var entry in history.Entries)
        {
            latest[entry.ActionId] = entry;
        }

        var actions = history.Plan.Actions.ToImmutableDictionary(action => action.ActionId);
        var outcomes = history.Plan.Actions.Select(action => Project(action, latest.GetValueOrDefault(action.ActionId), true))
            .ToImmutableArray();
        var failureHistory = history.Entries.Where(entry => entry.State is JournalState.Skipped or JournalState.Failed)
            .Select(entry => Project(actions[entry.ActionId], entry, latest[entry.ActionId] == entry))
            .ToImmutableArray();
        return new PreparationDisposition(history.Plan.Key, outcomes, failureHistory, !history.Approvals.IsEmpty);

        PreparationActionOutcome Project(JournalAction action, JournalEntry? entry, bool current)
        {
            if (entry is { State: JournalState.Verified })
            {
                return new(action, entry, PreparationActionEffect.Verified, null, current);
            }

            if (entry is not null && IsSettledOmission(action, entry))
            {
                if (action.Intent == CapabilityIntent.Optional)
                {
                    return new(action, entry, PreparationActionEffect.OptionalOmission, null, current);
                }

                if (entry.ApprovalId is not null && entry.State != JournalState.RolledBack &&
                    policy is not null && exceptions is not null && evaluatedAtUtc is { } now &&
                    EvaluateException(history.Plan, action, entry, policy, exceptions, now) is { } candidate &&
                    candidate.Decision.RuleDetails.Single(detail =>
                        detail.Outcome.Rule.Requirement.RequirementId == candidate.Rule).Exception is { } exception)
                {
                    return new(action, entry, PreparationActionEffect.AuthorizedException, exception, current);
                }
            }

            return new(action, entry, PreparationActionEffect.ReviewRequired, null, current);
        }
    }

    /// <summary>Appends a policy-authorized exception for the current settled essential action outcome.</summary>
    public static PolicyExceptionHistory RecordException(JournalHistory history, Guid actionId, PolicyDocument policy,
        PolicyExceptionHistory exceptions, OperatorId operatorId, string reason, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(exceptions);

        var action = history.Plan.Actions.SingleOrDefault(action => action.ActionId == actionId);
        var entry = history.Entries.LastOrDefault(entry => entry.ActionId == actionId);
        if (action is not { Intent: CapabilityIntent.Essential } ||
            entry is not { State: JournalState.Skipped or JournalState.Failed, ApprovalId: not null } ||
            !IsSettledOmission(action, entry) ||
            EvaluateException(history.Plan, action, entry, policy, exceptions, recordedAtUtc) is not { } candidate)
        {
            throw new InvalidOperationException("The current action outcome does not permit a preparation exception.");
        }

        return exceptions.Record(candidate.Decision, candidate.Rule, operatorId, reason, recordedAtUtc);
    }

    internal static bool IsSettledOmission(JournalAction action, JournalEntry entry) => entry.State switch
    {
        JournalState.Skipped => entry.Result == JournalResult.Declined,
        JournalState.RolledBack => entry.Result == JournalResult.RollbackVerified,
        JournalState.Failed => entry.Result is JournalResult.AuthorizationDeclined or JournalResult.BackendUnavailable or
            JournalResult.NoChangeConfirmed or JournalResult.PackageDownloadUnavailable or JournalResult.PackageElevationUnavailable ||
            entry.Checkpoint is JournalCheckpoint.PackagePreDispatchFailure or JournalCheckpoint.BrokerPreDispatchFailure &&
                entry.Result is JournalResult.NetworkUnavailable or JournalResult.Cancelled ||
            action.Definition.Verification == ActionVerification.NoMachineChange && entry.Result == JournalResult.OperationFailed,
        _ => false,
    };

    private static (DispositionDecision Decision, RequirementId Rule)? EvaluateException(JournalPlanSnapshot plan,
        JournalAction action, JournalEntry entry, PolicyDocument policy, PolicyExceptionHistory exceptions, DateTimeOffset now)
    {
        if (!plan.IsCurrentPolicyPlan || exceptions.JobId != plan.Key.JobId || now < entry.OccurredAtUtc ||
            policy.ContentSha256 != plan.PolicySha256 || !PlanResolver.PolicyApplies(policy, plan.SyntheticData, now))
        {
            return null;
        }

        var key = new EvidenceKey($"preparation.action.{action.CapabilityId}");
        var rules = policy.Rules.Where(rule => rule.Requirement.EvidenceKey == key).ToArray();
        if (rules is not [var rule] || rule.Requirement.RequirementType != RequirementType.ProfileDependent ||
            rule.ActiveForCapabilityIds is not [OutcomePolicyCapability] ||
            rule.Condition is not EqualCondition { Value.BooleanValue: true } || rule.ExceptionAuthorizationId is null)
        {
            return null;
        }

        var outcome = new Observation(new EvidenceMetadata(new EvidenceId($"journal-entry-{entry.EntryId:N}"),
            PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed, new ProviderId("action-journal"),
            entry.OccurredAtUtc, new EvidenceProvenance(ProvenanceKind.SystemGenerated, "settled-action-outcome"),
            ValueAvailability.Available), key, EvidenceValue.FromBoolean(false), unit: null, limitationCode: null);
        var evaluated = DispositionEngine.Evaluate(policy, [outcome], [OutcomePolicyCapability], exceptions, now);
        return (evaluated, rule.Requirement.RequirementId);
    }
}
