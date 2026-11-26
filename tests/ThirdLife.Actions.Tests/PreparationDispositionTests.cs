using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Actions.Tests;

public sealed class PreparationDispositionTests
{
    private static readonly DateTimeOffset ReviewTime = new(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly OperatorId Operator = new("operator-preparation-fixture");

    [Theory]
    [InlineData(CapabilityIntent.Optional, JournalState.Skipped, PreparationActionEffect.OptionalOmission)]
    [InlineData(CapabilityIntent.Optional, JournalState.Failed, PreparationActionEffect.OptionalOmission)]
    [InlineData(CapabilityIntent.Essential, JournalState.Skipped, PreparationActionEffect.ReviewRequired)]
    [InlineData(CapabilityIntent.Essential, JournalState.Failed, PreparationActionEffect.ReviewRequired)]
    public void UsesTheReviewedIntentAndKeepsTheRealOutcome(CapabilityIntent intent, JournalState state,
        PreparationActionEffect effect)
    {
        var (history, action, entry) = WithOutcome(intent, state);
        var disposition = PreparationDisposition.Evaluate(history);

        Assert.Equal(history.Plan.Key, disposition.Plan);
        Assert.Equal(intent == CapabilityIntent.Essential ? Disposition.HumanReviewRequired : Disposition.ReadyToPrepare,
            disposition.Disposition);
        var selected = Assert.Single(disposition.Actions, candidate => candidate.Action == action);
        Assert.All(disposition.Actions.Where(candidate => candidate != selected), candidate =>
            Assert.Equal(PreparationActionEffect.Verified, candidate.Effect));
        Assert.Equal(state, selected.State);
        Assert.Equal(entry, selected.Entry);
        Assert.Equal(effect, selected.Effect);
        Assert.Equal(selected, Assert.Single(disposition.History));
    }

    [Theory]
    [InlineData(JournalState.Approved)]
    [InlineData(JournalState.Started)]
    [InlineData(JournalState.Applied)]
    [InlineData(JournalState.RequiresReview)]
    public void PendingOptionalWorkRequiresReview(JournalState state)
    {
        var (history, action, _) = WithOutcome(CapabilityIntent.Optional, state);

        var disposition = PreparationDisposition.Evaluate(history);

        Assert.Equal(Disposition.HumanReviewRequired, disposition.Disposition);
        Assert.Equal(PreparationActionEffect.ReviewRequired,
            Assert.Single(disposition.Actions, candidate => candidate.Action == action).Effect);
    }

    [Theory]
    [InlineData(JournalState.Skipped)]
    [InlineData(JournalState.Failed)]
    public void NamedExceptionAppliesOnlyToItsExactCurrentEvidence(JournalState state)
    {
        var policy = ExceptionPolicy();
        var (history, action, entry) = WithOutcome(CapabilityIntent.Essential, state, policy);
        var exceptions = new PolicyExceptionHistory(history.Plan.Key.JobId);
        var at = ReviewTime.AddMinutes(5);
        const string reason = "Restricted fixture rationale";

        var accepted = PreparationDisposition.RecordException(history, action.ActionId, policy, exceptions, Operator,
            reason, at);
        var disposition = PreparationDisposition.Evaluate(history, policy, accepted, at);

        Assert.Empty(exceptions.Records);
        var record = Assert.Single(accepted.Records);
        Assert.Equal("allow-preparation-fixture-omission", record.AuthorizationId);
        Assert.Equal("POL-PREP-001", record.RequirementId.Value);
        Assert.Equal(new EvidenceId($"journal-entry-{entry!.EntryId:N}"), record.EvidenceId);
        Assert.Equal(Operator, record.OperatorId);
        Assert.Equal(at, record.RecordedAtUtc);
        Assert.Equal(reason, record.Reason);
        Assert.DoesNotContain(reason, JsonSerializer.Serialize(record));
        Assert.Equal(Disposition.ReadyToPrepare, disposition.Disposition);
        var outcome = Assert.Single(disposition.History);
        Assert.Equal(PreparationActionEffect.AuthorizedException, outcome.Effect);
        Assert.Equal(state, outcome.State);
        Assert.Same(record, outcome.Exception);

        var (anotherOutcome, _, _) = WithOutcome(CapabilityIntent.Essential, state, policy);
        Assert.Equal(Disposition.HumanReviewRequired,
            PreparationDisposition.Evaluate(anotherOutcome, policy, accepted, at).Disposition);
        Assert.Equal(Disposition.HumanReviewRequired,
            PreparationDisposition.Evaluate(history, policy, accepted, at.AddSeconds(-1)).Disposition);
        Assert.Equal(Disposition.HumanReviewRequired,
            PreparationDisposition.Evaluate(history, ExceptionPolicy("different-policy-authorization"), accepted, at).Disposition);
    }

    [Theory]
    [InlineData("preparation.action.document_editing", "wrong_activation", "human_review_required")]
    [InlineData("preparation.action.web_browsing", "prepared_action_outcomes", "human_review_required")]
    [InlineData("preparation.action.document_editing", "prepared_action_outcomes", "do_not_deploy")]
    public void RejectsUnrelatedOrProhibitedPolicyRules(string key, string activation, string unmet)
    {
        var policy = ExceptionPolicy(key: key, activation: activation, unmet: unmet);
        var (history, action, _) = WithOutcome(CapabilityIntent.Essential, JournalState.Failed, policy);

        Assert.Throws<InvalidOperationException>(() => PreparationDisposition.RecordException(history, action.ActionId, policy,
            new PolicyExceptionHistory(history.Plan.Key.JobId), Operator, "fixture disposition", ReviewTime.AddMinutes(5)));
    }

    [Fact]
    public void EvenTheNamedRuleCannotWaiveUnresolvedMutationOrAnUnrelatedJob()
    {
        var policy = ExceptionPolicy();
        var (history, action, _) = WithOutcome(CapabilityIntent.Essential, JournalState.RequiresReview, policy);
        Assert.Throws<InvalidOperationException>(() => PreparationDisposition.RecordException(history, action.ActionId, policy,
            new PolicyExceptionHistory(history.Plan.Key.JobId), Operator, "fixture disposition", ReviewTime.AddMinutes(5)));

        var (failed, failedAction, _) = WithOutcome(CapabilityIntent.Essential, JournalState.Failed, policy);
        Assert.Throws<InvalidOperationException>(() => PreparationDisposition.RecordException(failed, failedAction.ActionId, policy,
            new PolicyExceptionHistory(new JobId("unrelated-fixture-job")), Operator, "fixture disposition", ReviewTime.AddMinutes(5)));
    }

    [Fact]
    public void WholePlanDeclineCannotBeConvertedIntoPreparationApprovalByAnException()
    {
        var policy = ExceptionPolicy();
        var (approved, action, _) = WithOutcome(CapabilityIntent.Essential, JournalState.Skipped, policy);
        var entries = approved.Plan.Actions.Select(candidate => new JournalEntry(Guid.NewGuid(), approved.Plan.Key,
            candidate.ActionId, null, JournalState.Planned, JournalState.Skipped, JournalActor.Operator, Operator,
            Guid.NewGuid(), ReviewTime, JournalResult.Declined)).ToImmutableArray();
        var declined = new JournalHistory(approved.Plan, [], entries, null);

        Assert.Equal(Disposition.HumanReviewRequired, PreparationDisposition.Evaluate(declined).Disposition);
        Assert.Throws<InvalidOperationException>(() => PreparationDisposition.RecordException(declined, action.ActionId, policy,
            new PolicyExceptionHistory(declined.Plan.Key.JobId), Operator, "fixture disposition", ReviewTime.AddMinutes(5)));
    }

    [Fact]
    public void LaterVerificationKeepsTheHistoricFailureAndEndsItsCurrentEffect()
    {
        var (history, action, failure) = WithOutcome(CapabilityIntent.Essential, JournalState.Failed,
            result: JournalResult.AuthorizationDeclined, broker: true);
        var renewed = new JournalApproval(Guid.NewGuid(), history.Plan.Key, Operator, Guid.NewGuid(), ReviewTime.AddMinutes(5));
        JournalEntry[] continuation =
        [
            Entry(history.Plan, renewed, action, JournalState.Failed, JournalState.Approved, JournalActor.Operator,
                JournalResult.None, 0, JournalCheckpoint.BrokerApproval),
            Entry(history.Plan, renewed, action, JournalState.Approved, JournalState.Started, JournalActor.Coordinator,
                JournalResult.None, 1, JournalCheckpoint.BrokerDispatchIntent),
            .. Complete(history.Plan, renewed, action),
        ];
        var reopened = new JournalHistory(history.Plan, [.. history.Approvals, renewed], [.. history.Entries, .. continuation], renewed);

        var disposition = PreparationDisposition.Evaluate(reopened);

        Assert.Equal(Disposition.ReadyToPrepare, disposition.Disposition);
        Assert.All(disposition.Actions, outcome => Assert.Equal(PreparationActionEffect.Verified, outcome.Effect));
        var historical = Assert.Single(disposition.History);
        Assert.False(historical.IsCurrent);
        Assert.Equal(failure, historical.Entry);
        Assert.Equal(PreparationActionEffect.ReviewRequired, historical.Effect);
    }

    private static PolicyDocument ExceptionPolicy(string authorization = "allow-preparation-fixture-omission",
        string key = "preparation.action.document_editing", string activation = "prepared_action_outcomes",
        string unmet = "human_review_required") => PlanResolverTests.CreateSyntheticPolicy(JsonNode.Parse($$"""
            {
              "id": "POL-PREP-001",
              "requirement_type": "profile_dependent",
              "severity": "required",
              "evidence_key": "{{key}}",
              "active_for_capability_ids": ["{{activation}}"],
              "exception_authorization_id": "{{authorization}}",
              "condition": { "type": "equals", "value": true },
              "decision": { "blocks_ready": true, "unmet_disposition": "{{unmet}}", "unavailable_disposition": "human_review_required" }
            }
            """)!);

    private static (JournalHistory History, JournalAction Action, JournalEntry? Outcome) WithOutcome(CapabilityIntent intent,
        JournalState state, PolicyDocument? policy = null, JournalResult result = JournalResult.OperationFailed, bool broker = false)
    {
        var proposal = PlanResolverTests.CreateSyntheticPlan(includeVideoCalling: true, policy: policy);
        var snapshot = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
        var plan = JournalPlanSnapshot.Restore(snapshot.Key, snapshot.CanonicalPlan.ToArray(), snapshot.CanonicalImpact.ToArray());
        var action = Assert.Single(plan.Actions, candidate => candidate.CapabilityId ==
            (intent == CapabilityIntent.Essential ? "document_editing" : "video_calling"));
        var approval = new JournalApproval(Guid.NewGuid(), plan.Key, Operator, Guid.NewGuid(), ReviewTime);
        var entries = ImmutableArray.CreateBuilder<JournalEntry>();
        JournalEntry? outcome = null;
        foreach (var candidate in plan.Actions)
        {
            entries.Add(Entry(plan, approval, candidate, JournalState.Planned, JournalState.Approved, JournalActor.Operator,
                JournalResult.None, 0, broker ? JournalCheckpoint.BrokerApproval : JournalCheckpoint.None));
            if (candidate == action && state == JournalState.Skipped)
            {
                outcome = Entry(plan, approval, action, JournalState.Approved, JournalState.Skipped, JournalActor.Operator,
                    JournalResult.Declined, 1);
                entries.Add(outcome);
                continue;
            }

            if (candidate == action && state == JournalState.Approved)
            {
                continue;
            }

            var started = Entry(plan, approval, candidate, JournalState.Approved, JournalState.Started, JournalActor.Coordinator,
                JournalResult.None, 1, broker ? JournalCheckpoint.BrokerDispatchIntent : JournalCheckpoint.DispatchIntent);
            entries.Add(started);
            if (candidate != action)
            {
                entries.AddRange(Complete(plan, approval, candidate));
                continue;
            }

            outcome = state switch
            {
                JournalState.Started => started,
                JournalState.Failed when broker => Entry(plan, approval, action, JournalState.Started, state,
                    JournalActor.Coordinator, result, 2, JournalCheckpoint.BrokerPreDispatchFailure),
                JournalState.Failed => Entry(plan, approval, action, JournalState.Started, state, JournalActor.Broker, result, 2),
                JournalState.Applied => Complete(plan, approval, action)[0],
                JournalState.RequiresReview => Entry(plan, approval, action, JournalState.Started, state,
                    JournalActor.Coordinator, JournalResult.Interrupted, 2, JournalCheckpoint.Reconcile),
                _ => throw new ArgumentOutOfRangeException(nameof(state)),
            };
            if (outcome != started)
            {
                entries.Add(outcome);
            }
        }

        return (new JournalHistory(plan, [approval], entries.ToImmutable(), approval), action, outcome);
    }

    private static JournalEntry[] Complete(JournalPlanSnapshot plan, JournalApproval approval, JournalAction action) =>
    [
        Entry(plan, approval, action, JournalState.Started, JournalState.Applied, JournalActor.Broker, JournalResult.BackendApplied, 2),
        Entry(plan, approval, action, JournalState.Applied, JournalState.Verified, JournalActor.Verifier,
            JournalResult.VerificationPassed, 3, verification: ActionVerification.NoMachineChange),
    ];

    private static JournalEntry Entry(JournalPlanSnapshot plan, JournalApproval approval, JournalAction action,
        JournalState prior, JournalState state, JournalActor actor, JournalResult result, int atMinutes,
        JournalCheckpoint checkpoint = JournalCheckpoint.None, ActionVerification? verification = null) =>
        new(Guid.NewGuid(), plan.Key, action.ActionId, approval.ApprovalId, prior, state, actor,
            actor == JournalActor.Operator ? Operator : new OperatorId("actor-preparation-fixture"),
            approval.CorrelationId, approval.OccurredAtUtc.AddMinutes(atMinutes), result, checkpoint, verification);
}
