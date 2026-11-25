using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Actions.Tests;

public sealed class JournalTests
{
    private static readonly DateTimeOffset ApprovalTime = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ApprovalId = new("0afeb624-0133-4adb-8703-dc8954b0fac9");
    private static readonly Guid CorrelationId = new("9e88b754-cda0-4f0e-8583-42f5ba2eaf77");
    private static readonly Guid PreviousEntryId = new("0d6bd5da-6c4c-49c5-904d-de59b9ff8daa");
    private static readonly Guid CandidateEntryId = new("b1cb9e10-dd35-440f-a875-969a7b523cd9");
    private static readonly OperatorId Approver = new("operator-synthetic-journal");

    [Theory]
    [InlineData(JournalState.Planned, JournalState.Approved, JournalActor.Operator, JournalResult.None, JournalCheckpoint.None, null, true)]
    [InlineData(JournalState.Planned, JournalState.Approved, JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.None, null, false)]
    [InlineData(JournalState.Planned, JournalState.Started, JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.DispatchIntent, null, false)]
    [InlineData(JournalState.Approved, JournalState.Started, JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.DispatchIntent, null, true)]
    [InlineData(JournalState.Approved, JournalState.Started, JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.None, null, false)]
    [InlineData(JournalState.Approved, JournalState.Skipped, JournalActor.Operator, JournalResult.Declined, JournalCheckpoint.None, null, true)]
    [InlineData(JournalState.Started, JournalState.Applied, JournalActor.Broker, JournalResult.BackendApplied, JournalCheckpoint.None, null, true)]
    [InlineData(JournalState.Started, JournalState.Applied, JournalActor.Broker, JournalResult.BackendApplied,
        JournalCheckpoint.RestartRequired, null, false)]
    [InlineData(JournalState.Started, JournalState.Applied, JournalActor.Coordinator, JournalResult.BackendApplied, JournalCheckpoint.None, null, false)]
    [InlineData(JournalState.Started, JournalState.Failed, JournalActor.Broker, JournalResult.OperationFailed, JournalCheckpoint.None, null, true)]
    [InlineData(JournalState.Started, JournalState.Verified, JournalActor.Verifier, JournalResult.VerificationPassed,
        JournalCheckpoint.None, ActionVerification.NoMachineChange, false)]
    [InlineData(JournalState.Started, JournalState.RequiresReview, JournalActor.Coordinator, JournalResult.Interrupted, JournalCheckpoint.Reconcile, null, true)]
    [InlineData(JournalState.Applied, JournalState.RequiresReview, JournalActor.Coordinator, JournalResult.Interrupted, JournalCheckpoint.Reconcile, null, true)]
    [InlineData(JournalState.Applied, JournalState.Verified, JournalActor.Verifier, JournalResult.VerificationPassed,
        JournalCheckpoint.None, ActionVerification.NoMachineChange, true)]
    [InlineData(JournalState.Applied, JournalState.Verified, JournalActor.Broker, JournalResult.VerificationPassed,
        JournalCheckpoint.None, ActionVerification.NoMachineChange, false)]
    [InlineData(JournalState.Applied, JournalState.Verified, JournalActor.Verifier, JournalResult.VerificationPassed,
        JournalCheckpoint.None, ActionVerification.ExactPackageIdentity, false)]
    [InlineData(JournalState.Applied, JournalState.Verified, JournalActor.Verifier, JournalResult.VerificationPassed, JournalCheckpoint.None, null, false)]
    [InlineData(JournalState.Applied, JournalState.RolledBack, JournalActor.Verifier, JournalResult.RollbackVerified, JournalCheckpoint.None, null, false)]
    [InlineData(JournalState.RequiresReview, JournalState.Failed, JournalActor.Verifier, JournalResult.NoChangeConfirmed,
        JournalCheckpoint.None, ActionVerification.NoMachineChange, true)]
    [InlineData(JournalState.RequiresReview, JournalState.Failed, JournalActor.Coordinator, JournalResult.NoChangeConfirmed,
        JournalCheckpoint.None, ActionVerification.NoMachineChange, false)]
    [InlineData(JournalState.RequiresReview, JournalState.Failed, JournalActor.Verifier, JournalResult.NoChangeConfirmed,
        JournalCheckpoint.None, null, false)]
    [InlineData(JournalState.RequiresReview, JournalState.Failed, JournalActor.Verifier, JournalResult.NoChangeConfirmed,
        JournalCheckpoint.Reconcile, ActionVerification.NoMachineChange, false)]
    [InlineData(JournalState.Verified, JournalState.Started, JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.DispatchIntent, null, false)]
    public void RequiresTheLegalPriorStateActorCheckpointAndVerification(
        JournalState prior,
        JournalState next,
        JournalActor actor,
        JournalResult result,
        JournalCheckpoint checkpoint,
        ActionVerification? verification,
        bool expected)
    {
        var plan = CreatePlan();
        var actionId = plan.Actions[0].Request.ActionId;
        var approval = Approval(plan);
        var previous = Previous(plan, actionId, prior);
        var candidate = Entry(plan, actionId, CandidateEntryId, prior, next, actor, result, checkpoint, verification);

        Assert.Equal(expected, JournalStateMachine.CanAppend(plan, approval, previous, candidate));
    }

    [Fact]
    public void RequiresTheExactApprovalAndApprovedAction()
    {
        var plan = CreatePlan();
        var actionId = plan.Actions[0].Request.ActionId;
        var approval = Approval(plan);
        var candidate = Entry(plan, actionId, CandidateEntryId, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None);

        Assert.False(JournalStateMachine.CanAppend(plan, null, null, candidate));
        var otherApproval = new JournalApproval(
            PreviousEntryId, plan.Key, Approver, CorrelationId, ApprovalTime);
        Assert.False(JournalStateMachine.CanAppend(plan, otherApproval, null, candidate));
        var otherActor = Entry(plan, actionId, CandidateEntryId, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None, actorId: new OperatorId("operator-other-fixture"));
        Assert.False(JournalStateMachine.CanAppend(plan, approval, null, otherActor));
        var unknownAction = Entry(plan, CandidateEntryId, CandidateEntryId, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None);
        Assert.False(JournalStateMachine.CanAppend(plan, approval, null, unknownAction));
    }

    [Fact]
    public void RequiresMatchingAttemptAndMonotonicTransitionTime()
    {
        var plan = CreatePlan();
        var actionId = plan.Actions[0].Request.ActionId;
        var approval = Approval(plan);
        var started = Previous(plan, actionId, JournalState.Started);
        var differentCorrelation = Entry(plan, actionId, CandidateEntryId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, JournalResult.BackendApplied, correlationId: PreviousEntryId);
        var staleTime = Entry(plan, actionId, CandidateEntryId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, JournalResult.BackendApplied, occurredAt: ApprovalTime);

        Assert.False(JournalStateMachine.CanAppend(plan, approval, started, differentCorrelation));
        Assert.False(JournalStateMachine.CanAppend(plan, approval, started, staleTime));
    }

    [Fact]
    public void PackageContractAllowsARecordedRestartAndIndependentlyVerifiedRollback()
    {
        var proposal = PlanResolverTests.CreateReviewedPlan();
        var plan = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
        var actionId = Assert.Single(plan.Actions).ActionId;
        var approval = Approval(plan);
        var started = Previous(plan, actionId, JournalState.Started);
        var applied = Entry(plan, actionId, CandidateEntryId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired);
        var rolledBack = Entry(plan, actionId, new Guid("9145cdcb-23f8-4948-9922-7fe29efdb283"),
            JournalState.Applied, JournalState.RolledBack, JournalActor.Verifier, JournalResult.RollbackVerified,
            occurredAt: ApprovalTime.AddMinutes(3));

        Assert.True(JournalStateMachine.CanAppend(plan, approval, started, applied));
        Assert.True(JournalStateMachine.CanAppend(plan, approval, applied, rolledBack));
    }

    [Fact]
    public void RestartPendingPackageCannotVerifyFromItsAppliedResult()
    {
        var proposal = PlanResolverTests.CreateReviewedPlan();
        var plan = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
        var actionId = Assert.Single(plan.Actions).ActionId;
        var applied = Entry(plan, actionId, PreviousEntryId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired,
            occurredAt: ApprovalTime.AddMinutes(1));
        var verified = Entry(plan, actionId, CandidateEntryId, JournalState.Applied, JournalState.Verified,
            JournalActor.Verifier, JournalResult.VerificationPassed, verification: ActionVerification.ExactPackageIdentity);

        Assert.False(JournalStateMachine.CanAppend(plan, Approval(plan), applied, verified));
    }

    [Fact]
    public void InterruptedPackageCannotUseNoOpReconciliation()
    {
        var proposal = PlanResolverTests.CreateReviewedPlan();
        var plan = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
        var actionId = Assert.Single(plan.Actions).ActionId;
        var interrupted = Previous(plan, actionId, JournalState.RequiresReview);
        var reconciled = Entry(plan, actionId, CandidateEntryId, JournalState.RequiresReview, JournalState.Failed,
            JournalActor.Verifier, JournalResult.NoChangeConfirmed, verification: ActionVerification.NoMachineChange);

        Assert.False(JournalStateMachine.CanAppend(plan, Approval(plan), interrupted, reconciled));
    }

    [Fact]
    public void RecordsAnOperatorDeclineBeforeApprovalWithoutInventingConsent()
    {
        var plan = CreatePlan();
        var action = plan.Actions[0];
        var decline = new JournalEntry(CandidateEntryId, plan.Key, action.ActionId, null,
            JournalState.Planned, JournalState.Skipped, JournalActor.Operator, Approver, CorrelationId, ApprovalTime,
            JournalResult.Declined);

        Assert.True(JournalStateMachine.CanAppend(plan, null, null, decline));
    }

    [Fact]
    public void SnapshotRoundTripRetainsTheExactProposalImpactAndActionDefinitions()
    {
        var proposal = PlanResolverTests.CreateSyntheticPlan();
        var impact = ImpactEstimator.Estimate(proposal);
        var saved = JournalPlanSnapshot.Create(proposal, impact);
        var restored = JournalPlanSnapshot.Restore(saved.Key, saved.CanonicalPlan.ToArray(), saved.CanonicalImpact.ToArray());

        Assert.True(saved.HasPlanningProvenance);
        Assert.False(restored.HasPlanningProvenance);
        Assert.True(saved.IsCurrentPolicyPlan);
        Assert.True(restored.IsCurrentPolicyPlan);
        Assert.Equal(proposal.ContentSha256, restored.Key.PlanSha256);
        Assert.Equal(saved.Key, restored.Key);
        Assert.Equal(proposal.GateDecisionId, restored.GateDecisionId);
        Assert.Equal(proposal.Actions.Select(action => action.Request.ActionId), restored.Actions.Select(action => action.Request.ActionId));
        Assert.Equal(proposal.Actions.Select(action => action.Definition), restored.Actions.Select(action => action.Definition));
        Assert.Equal(saved.CanonicalPlan.ToArray(), restored.CanonicalPlan.ToArray());
        Assert.Equal(saved.CanonicalImpact.ToArray(), restored.CanonicalImpact.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SnapshotRejectsAnAlteredPlanOrImpactDigest(bool alterPlan)
    {
        var saved = CreatePlan();
        var plan = saved.CanonicalPlan.ToArray();
        var impact = saved.CanonicalImpact.ToArray();
        if (alterPlan)
        {
            plan = [.. plan, (byte)' '];
        }
        else
        {
            impact = [.. impact, (byte)' '];
        }

        Assert.Throws<JsonException>(() => JournalPlanSnapshot.Restore(saved.Key, plan, impact));
    }

    [Fact]
    public void PlanSnapshotRejectsUnknownFieldsEvenWithARecomputedDigest()
    {
        var saved = CreatePlan();
        var plan = JsonNode.Parse(saved.CanonicalPlan.AsSpan())!;
        plan["unrecognized"] = "fixture-value";
        var bytes = Encoding.UTF8.GetBytes(plan.ToJsonString());
        var key = new JournalPlanKey(saved.Key.JobId, Convert.ToHexStringLower(SHA256.HashData(bytes)), saved.Key.ImpactSha256);

        Assert.Throws<JsonException>(() => JournalPlanSnapshot.Restore(key, bytes, saved.CanonicalImpact.ToArray()));
    }

    [Fact]
    public void JournalTransitionSurfaceHasNoRawOutputSlot()
    {
        Assert.DoesNotContain(typeof(JournalEntry).GetProperties(), property =>
            property.PropertyType == typeof(string) || property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void RejectsHistoryOverTheApprovalBound()
    {
        var plan = CreatePlan();
        var approvals = Enumerable.Range(1, JournalHistory.MaximumApprovalsPerJob + 1)
            .Select(index => new JournalApproval(new Guid(index, 0, 0, new byte[8]), plan.Key,
                Approver, CorrelationId, ApprovalTime))
            .ToArray();

        Assert.Throws<ArgumentException>(() => new JournalHistory(plan, [.. approvals], [], null));
    }

    private static JournalPlanSnapshot CreatePlan()
    {
        var plan = PlanResolverTests.CreateSyntheticPlan();
        return JournalPlanSnapshot.Create(plan, ImpactEstimator.Estimate(plan));
    }

    private static JournalApproval Approval(JournalPlanSnapshot plan) =>
        new(ApprovalId, plan.Key, Approver, CorrelationId, ApprovalTime);

    private static JournalEntry? Previous(JournalPlanSnapshot plan, Guid actionId, JournalState state) => state switch
    {
        JournalState.Planned => null,
        JournalState.Approved => Entry(plan, actionId, PreviousEntryId, JournalState.Planned, state,
            JournalActor.Operator, JournalResult.None, occurredAt: ApprovalTime.AddMinutes(1)),
        JournalState.Started => Entry(plan, actionId, PreviousEntryId, JournalState.Approved, state,
            JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.DispatchIntent, occurredAt: ApprovalTime.AddMinutes(1)),
        JournalState.Applied => Entry(plan, actionId, PreviousEntryId, JournalState.Started, state,
            JournalActor.Broker, JournalResult.BackendApplied, occurredAt: ApprovalTime.AddMinutes(1)),
        JournalState.Verified => Entry(plan, actionId, PreviousEntryId, JournalState.Applied, state,
            JournalActor.Verifier, JournalResult.VerificationPassed, verification: ActionVerification.NoMachineChange,
            occurredAt: ApprovalTime.AddMinutes(1)),
        JournalState.RequiresReview => Entry(plan, actionId, PreviousEntryId, JournalState.Started, state,
            JournalActor.Coordinator, JournalResult.Interrupted, JournalCheckpoint.Reconcile,
            occurredAt: ApprovalTime.AddMinutes(1)),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static JournalEntry Entry(
        JournalPlanSnapshot plan,
        Guid actionId,
        Guid entryId,
        JournalState prior,
        JournalState next,
        JournalActor actor,
        JournalResult result,
        JournalCheckpoint checkpoint = JournalCheckpoint.None,
        ActionVerification? verification = null,
        OperatorId? actorId = null,
        Guid? correlationId = null,
        DateTimeOffset? occurredAt = null) =>
        new(entryId, plan.Key, actionId, ApprovalId, prior, next, actor,
            actorId ?? (actor == JournalActor.Operator ? Approver : new OperatorId("actor-" + actor.ToString().ToLowerInvariant())),
            correlationId ?? CorrelationId, occurredAt ?? ApprovalTime.AddMinutes(2), result, checkpoint, verification);
}
