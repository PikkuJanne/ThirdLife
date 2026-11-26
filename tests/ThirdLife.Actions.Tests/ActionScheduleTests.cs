using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Actions.Tests;

public sealed class ActionScheduleTests
{
    private static readonly OperatorId Operator = new("operator-schedule-fixture");
    private static readonly DateTimeOffset ReviewTime = new(2030, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DependencyOrderIsDeterministicForADiamondWithAPhaseBoundary()
    {
        var plan = Snapshot();
        Guid[] ids = PlanIds(plan);
        ScheduledAction[] input =
        [
            new(ids[1], 2, [ids[2], ids[0]]),
            new(ids[2], 1, [ids[3]]),
            new(ids[0], 1, [ids[3]]),
            new(ids[3], 1, []),
        ];
        var first = ActionSchedule.Create(plan, input);
        var reordered = ActionSchedule.Create(plan, input.Reverse());

        Assert.Equal(plan.Key, first.PlanKey);
        Assert.Equal([ids[3], ids[0], ids[2], ids[1]], first.Steps.Select(step => step.ActionId));
        Assert.Equal([ids[0], ids[2]], first.Steps[^1].Dependencies.AsEnumerable());
        Assert.Equal(first.Steps.Select(step => step.ActionId), reordered.Steps.Select(step => step.ActionId));
        Assert.Equal(first.Steps[^1].Dependencies.AsEnumerable(), reordered.Steps[^1].Dependencies.AsEnumerable());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate_action")]
    [InlineData("foreign_action")]
    [InlineData("extra_action")]
    [InlineData("foreign_dependency")]
    [InlineData("duplicate_dependency")]
    [InlineData("self_dependency")]
    [InlineData("missing_dependencies")]
    [InlineData("phase_zero")]
    [InlineData("phase_high")]
    [InlineData("phase_inversion")]
    [InlineData("cycle")]
    public void RejectsInvalidScheduleBeforeReview(string defect)
    {
        var plan = Snapshot();
        Guid[] ids = PlanIds(plan);
        ScheduledAction[] steps = ids.Select(id => new ScheduledAction(id, 1, [])).ToArray();
        ScheduledAction[] invalid = defect switch
        {
            "missing" => steps[..^1],
            "duplicate_action" => [steps[0], steps[0], steps[2], steps[3]],
            "foreign_action" => [steps[0] with { ActionId = Guid.Empty }, .. steps[1..]],
            "extra_action" => [.. steps, new ScheduledAction(Guid.NewGuid(), 1, [])],
            "foreign_dependency" => [steps[0] with { Dependencies = [Guid.Empty] }, .. steps[1..]],
            "duplicate_dependency" => [steps[0] with { Dependencies = [ids[1], ids[1]] }, .. steps[1..]],
            "self_dependency" => [steps[0] with { Dependencies = [ids[0]] }, .. steps[1..]],
            "missing_dependencies" => [steps[0] with { Dependencies = default }, .. steps[1..]],
            "phase_zero" => [steps[0] with { Phase = 0 }, .. steps[1..]],
            "phase_high" => [steps[0] with { Phase = 5 }, .. steps[1..]],
            "phase_inversion" => [steps[0] with { Dependencies = [ids[1]] }, steps[1] with { Phase = 2 }, .. steps[2..]],
            "cycle" => [steps[0] with { Dependencies = [ids[1]] }, steps[1] with { Dependencies = [ids[0]] }, .. steps[2..]],
            _ => throw new ArgumentException("Unknown test defect.", nameof(defect)),
        };

        Assert.Throws<ArgumentException>("steps", () => ActionSchedule.Create(plan, invalid));
    }

    [Fact]
    public void KnownFailureBlocksItsBranchAndKeepsEarlierIndependentVerification()
    {
        var plan = Snapshot();
        Guid[] ids = PlanIds(plan);
        var schedule = ActionSchedule.Create(plan,
        [
            new(ids[0], 1, []),
            new(ids[1], 1, []),
            new(ids[2], 1, [ids[1]]),
            new(ids[3], 1, [ids[0]]),
        ]);
        var approval = Approve(plan);
        ImmutableArray<JournalEntry> entries =
        [
            .. ApproveActions(plan, approval),
            .. Verify(plan, approval, ids[0]),
            Start(plan, approval, ids[1]),
            Entry(plan, approval, ids[1], JournalState.Started, JournalState.Failed, JournalActor.Broker,
                JournalResult.OperationFailed, atMinutes: 2),
        ];
        var current = schedule.Project(new JournalHistory(plan, [approval], entries, approval));

        Assert.False(current.RequiresRecovery);
        Assert.Equal(ids[3], current.NextForReview);
        Assert.Equal(JournalState.Verified, current.Actions[0].State);
        Assert.Equal(JournalState.Failed, current.Actions[1].State);
        Assert.Equal(JournalResult.OperationFailed, current.Actions[1].Result);
        Assert.Equal(ids[1], Assert.Single(current.Actions[2].PendingDependencies));
        Assert.Empty(current.Actions[3].PendingDependencies);
    }

    [Fact]
    public void APhaseWaitsForAllEarlierPhaseVerificationEvenWithoutAnExplicitEdge()
    {
        var plan = Snapshot();
        Guid[] ids = PlanIds(plan);
        var schedule = ActionSchedule.Create(plan,
        [
            new(ids[0], 1, []),
            new(ids[1], 2, []),
            new(ids[2], 2, [ids[1]]),
            new(ids[3], 1, []),
        ]);
        var approval = Approve(plan);
        var entries = ApproveActions(plan, approval).AddRange(Verify(plan, approval, ids[0]));
        var incomplete = schedule.Project(new JournalHistory(plan, [approval], entries, approval));
        var phaseTwoFirst = incomplete.Actions.Single(action => action.ActionId == ids[1]);
        Assert.Equal(ids[3], incomplete.NextForReview);
        Assert.True(phaseTwoFirst.EarlierPhasePending);
        Assert.Empty(phaseTwoFirst.PendingDependencies);

        var complete = schedule.Project(new JournalHistory(plan, [approval],
            entries.AddRange(Verify(plan, approval, ids[3])), approval));
        Assert.False(complete.RequiresRecovery);
        Assert.Equal(ids[1], complete.NextForReview);
        Assert.False(complete.Actions.Single(action => action.ActionId == ids[1]).EarlierPhasePending);
    }

    [Theory]
    [InlineData(JournalState.Started, JournalCheckpoint.DispatchIntent, JournalResult.None)]
    [InlineData(JournalState.Applied, JournalCheckpoint.None, JournalResult.BackendApplied)]
    [InlineData(JournalState.RequiresReview, JournalCheckpoint.Reconcile, JournalResult.Interrupted)]
    public void ReopenedUnresolvedWorkTakesPrecedenceAndNeverSatisfiesADependency(JournalState state,
        JournalCheckpoint checkpoint, JournalResult result)
    {
        var plan = Snapshot();
        Guid[] ids = PlanIds(plan);
        var schedule = ActionSchedule.Create(plan,
        [
            new(ids[0], 1, []),
            new(ids[1], 1, [ids[0]]),
            new(ids[2], 1, []),
            new(ids[3], 1, []),
        ]);
        var approval = Approve(plan);
        var entries = ApproveActions(plan, approval).Add(Start(plan, approval, ids[0]));
        if (state != JournalState.Started)
        {
            entries = entries.Add(Entry(plan, approval, ids[0], JournalState.Started, state,
                state == JournalState.Applied ? JournalActor.Broker : JournalActor.Coordinator, result,
                atMinutes: 2, checkpoint));
        }

        var reopened = JournalPlanSnapshot.Restore(plan.Key, plan.CanonicalPlan.ToArray(), plan.CanonicalImpact.ToArray());
        var projection = schedule.Project(new JournalHistory(reopened, [approval], entries, approval));

        Assert.True(projection.RequiresRecovery);
        Assert.Null(projection.NextForReview);
        Assert.Equal(state, projection.Actions[0].State);
        Assert.Equal(checkpoint, projection.Actions[0].Checkpoint);
        Assert.Equal(result, projection.Actions[0].Result);
        Assert.Equal(ids[0], Assert.Single(projection.Actions[1].PendingDependencies));
        Assert.Empty(projection.Actions[2].PendingDependencies);
    }

    [Fact]
    public void DeclineDoesNotSatisfyDependenciesAndSupersededApprovalDoesNotOfferAReviewCandidate()
    {
        var plan = Snapshot();
        Guid[] ids = PlanIds(plan);
        var schedule = ActionSchedule.Create(plan,
        [
            new(ids[0], 1, []),
            new(ids[1], 1, [ids[0]]),
            new(ids[2], 1, []),
            new(ids[3], 1, []),
        ]);
        var oldApproval = Approve(plan);
        var active = new JournalApproval(Guid.NewGuid(), plan.Key, Operator, Guid.NewGuid(), ReviewTime.AddMinutes(5));
        var declined = new JournalEntry(Guid.NewGuid(), plan.Key, ids[0], null, JournalState.Planned,
            JournalState.Skipped, JournalActor.Operator, Operator, Guid.NewGuid(), ReviewTime, JournalResult.Declined);
        var obsolete = Entry(plan, oldApproval, ids[2], JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None, atMinutes: 0);
        var projection = schedule.Project(new JournalHistory(plan, [oldApproval, active], [declined, obsolete], active));

        Assert.Null(projection.NextForReview);
        Assert.False(projection.RequiresRecovery);
        Assert.Equal(JournalState.Skipped, projection.Actions[0].State);
        Assert.Equal(JournalState.Planned, projection.Actions[1].State);
        Assert.Equal(ids[0], Assert.Single(projection.Actions[1].PendingDependencies));
        Assert.Equal(JournalState.Approved, projection.Actions[2].State);
    }

    [Fact]
    public void RejectsJournalForAnotherExactPlan()
    {
        var plan = Snapshot();
        var other = Snapshot(includeVideoCalling: false);
        Assert.Equal(plan.Key.JobId, other.Key.JobId);
        Assert.NotEqual(plan.Key.PlanSha256, other.Key.PlanSha256);
        var schedule = ActionSchedule.Create(plan, PlanIds(plan).Select(id => new ScheduledAction(id, 1, [])));

        Assert.Throws<ArgumentException>("history", () => schedule.Project(new JournalHistory(other, [], [], null)));
    }

    [Fact]
    public void GraphChangesRequireNewReviewWhileEquivalentInputOrderPreservesIdentity()
    {
        var proposal = PlanResolverTests.CreateSyntheticPlan();
        var ids = proposal.Actions.Select(action => action.Request.ActionId).ToArray();
        ScheduledAction[] steps =
        [
            new(ids[0], 2, [ids[2], ids[1]]),
            new(ids[1], 1, []),
            new(ids[2], 1, []),
        ];
        var approved = proposal.WithSchedule(steps);
        var equivalent = proposal.WithSchedule(steps.Reverse().Select(step =>
            step with { Dependencies = step.Dependencies.Reverse().ToImmutableArray() }));
        var revised = proposal.WithSchedule([steps[0] with { Dependencies = [ids[1]] }, .. steps[1..]]);
        var impact = ImpactEstimator.Estimate(approved);
        var snapshot = JournalPlanSnapshot.Create(approved, impact);

        Assert.Equal(approved.ContentSha256, equivalent.ContentSha256);
        Assert.NotEqual(approved.ContentSha256, revised.ContentSha256);
        Assert.Throws<ArgumentException>(() => JournalPlanSnapshot.Create(revised, impact));
        var change = Assert.Single(PlanDiff.Compare(approved, impact, revised, ImpactEstimator.Estimate(revised)).Changes);
        Assert.Equal(PlanChangeKind.ActionSchedule, change.Kind);
        Assert.Throws<ArgumentException>("steps", () => ActionSchedule.Create(snapshot, revised.Schedule));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("reordered")]
    [InlineData("uppercase")]
    [InlineData("cycle")]
    public void SavedScheduleMustBeCanonicalAndValidEvenWithMatchingDigests(string defect)
    {
        var proposal = PlanResolverTests.CreateSyntheticPlan();
        var steps = proposal.Actions.Select(action => new ScheduledAction(action.Request.ActionId, 1, []));
        proposal = proposal.WithSchedule(steps);
        var snapshot = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
        var plan = JsonNode.Parse(snapshot.CanonicalPlan.AsSpan())!.AsObject();
        var schedule = plan["schedule"]!.AsArray();
        switch (defect)
        {
            case "missing":
                plan.Remove("schedule");
                break;
            case "reordered":
                var first = schedule[0]!.DeepClone();
                schedule.RemoveAt(0);
                schedule.Add(first);
                break;
            case "uppercase":
                var id = schedule[0]!["action_id"]!.GetValue<string>();
                Assert.NotEqual(id, id.ToUpperInvariant());
                schedule[0]!["action_id"] = id.ToUpperInvariant();
                break;
            case "cycle":
                schedule[0]!["dependencies"] = new JsonArray(schedule[1]!["action_id"]!.DeepClone());
                schedule[1]!["dependencies"] = new JsonArray(schedule[0]!["action_id"]!.DeepClone());
                break;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(plan);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var impact = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(snapshot.CanonicalImpact.AsSpan())
            .Replace(snapshot.Key.PlanSha256, digest, StringComparison.Ordinal));
        var key = new JournalPlanKey(snapshot.Key.JobId, digest, Convert.ToHexStringLower(SHA256.HashData(impact)));

        Assert.Throws<JsonException>(() => JournalPlanSnapshot.Restore(key, bytes, impact));
    }

    [Fact]
    public void HistoricalPlanProjectsItsSavedApprovalWithoutOfferingAnyActionForReview()
    {
        var current = Snapshot();
        var plan = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current.CanonicalPlan.AsSpan())
            .Replace("thirdlife.plan.v2", "thirdlife.plan.v1", StringComparison.Ordinal));
        var planHash = Convert.ToHexStringLower(SHA256.HashData(plan));
        var impact = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current.CanonicalImpact.AsSpan())
            .Replace(current.Key.PlanSha256, planHash, StringComparison.Ordinal));
        var key = new JournalPlanKey(current.Key.JobId, planHash, Convert.ToHexStringLower(SHA256.HashData(impact)));
        var historical = JournalPlanSnapshot.Restore(key, plan, impact);
        var schedule = ActionSchedule.Create(historical, PlanIds(historical).Select(id => new ScheduledAction(id, 1, [])));
        var approval = Approve(historical);

        var projected = schedule.Project(new JournalHistory(historical, [approval], ApproveActions(historical, approval), approval));

        Assert.False(historical.IsCurrentPolicyPlan);
        Assert.Null(projected.NextForReview);
        Assert.All(projected.Actions, action => Assert.Equal(JournalState.Approved, action.State));
    }

    private static JournalPlanSnapshot Snapshot(bool includeVideoCalling = true)
    {
        var proposal = PlanResolverTests.CreateSyntheticPlan(includeVideoCalling: includeVideoCalling);
        return JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
    }

    private static Guid[] PlanIds(JournalPlanSnapshot plan) => plan.Actions.Select(action => action.ActionId).ToArray();

    private static JournalApproval Approve(JournalPlanSnapshot plan) =>
        new(Guid.NewGuid(), plan.Key, Operator, Guid.NewGuid(), ReviewTime);

    private static ImmutableArray<JournalEntry> ApproveActions(JournalPlanSnapshot plan, JournalApproval approval) =>
        plan.Actions.Select(action => Entry(plan, approval, action.ActionId, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None, atMinutes: 0)).ToImmutableArray();

    private static JournalEntry Start(JournalPlanSnapshot plan, JournalApproval approval, Guid action) =>
        Entry(plan, approval, action, JournalState.Approved, JournalState.Started, JournalActor.Coordinator,
            JournalResult.None, atMinutes: 1, JournalCheckpoint.DispatchIntent);

    private static JournalEntry[] Verify(JournalPlanSnapshot plan, JournalApproval approval, Guid action) =>
    [
        Start(plan, approval, action),
        Entry(plan, approval, action, JournalState.Started, JournalState.Applied, JournalActor.Broker,
            JournalResult.BackendApplied, atMinutes: 2),
        Entry(plan, approval, action, JournalState.Applied, JournalState.Verified, JournalActor.Verifier,
            JournalResult.VerificationPassed, atMinutes: 3, verification: ActionVerification.NoMachineChange),
    ];

    private static JournalEntry Entry(JournalPlanSnapshot plan, JournalApproval approval, Guid action,
        JournalState prior, JournalState state, JournalActor actor, JournalResult result, int atMinutes,
        JournalCheckpoint checkpoint = JournalCheckpoint.None, ActionVerification? verification = null) =>
        new(Guid.NewGuid(), plan.Key, action, approval.ApprovalId, prior, state, actor,
            actor == JournalActor.Operator ? Operator : new OperatorId("actor-schedule-fixture"),
            approval.CorrelationId, ReviewTime.AddMinutes(atMinutes), result, checkpoint, verification);
}
