using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class ActionJournalStoreTests
{
    private static readonly DateTimeOffset Now = PersistenceTestData.Timestamp.AddHours(1);
    private static readonly Guid ApprovalId = new("6f08811d-8c60-4518-8c95-bd6997b389ce");
    private static readonly Guid CorrelationId = new("2b84b258-3013-4900-b14f-f859fd2c5f1d");
    private static readonly Guid ApprovedEntryId = new("0960e87f-4ec7-4314-977c-4c9034f67d82");
    private static readonly Guid StartedEntryId = new("190e850f-4613-4696-a14c-d7f1d359b27c");
    private static readonly Guid AppliedEntryId = new("28c37ee2-d51c-4146-940f-eacddafce8e1");
    private static readonly Guid VerifiedEntryId = new("be149a65-b78b-4b94-845e-84305181a4ae");
    private static readonly OperatorId Approver = new("operator-journal-fixture");
    private static readonly PolicyDocument Policy = PolicySchema.Parse("""
        {
          "schema_version":"thirdlife.policy.v2","policy_id":"community-policy","version":"1.0.0",
          "status":"candidate","synthetic_data":true,"classification":"PUBLIC_REFERENCE","claim_scope":"journal_fixture",
          "effective":{"status":"candidate_not_effective","effective_from_utc":null,"effective_until_utc":null},
          "target":{"operating_system":"windows_11","support_state":"supported","architecture":"x64","device_class":"laptop"},
          "rules":[{
            "id":"POL-SAN-001","requirement_type":"blocking","severity":"critical","evidence_key":"sanitization.state",
            "condition":{"type":"equals","value":"verified"},
            "decision":{"blocks_ready":true,"unmet_disposition":"human_review_required","unavailable_disposition":"human_review_required"}
          }]
        }
        """u8.ToArray());

    [Fact]
    public async Task BrokerApprovalAndDispatchRequireTheWholeExactBatch()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace);
        var (job, intake) = await CreateJobAsync(store);
        var plan = CreateSnapshot(job, intake, "0.1.0-candidate.1", actionCount: 2);
        await store.SaveJournalPlanAsync(plan);
        var approval = new JournalApproval(ApprovalId, plan.Key, Approver, CorrelationId, Now);
        var approved = BrokerEntries(plan, approval, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None, JournalCheckpoint.BrokerApproval, approval.CorrelationId, Now);
        var invalid = approved.SetItem(1, new JournalEntry(Guid.NewGuid(), plan.Key, approved[1].ActionId,
            approval.ApprovalId, JournalState.Planned, JournalState.Approved, JournalActor.Coordinator, approval.ActorId,
            approval.CorrelationId, Now, JournalResult.None, JournalCheckpoint.BrokerApproval));

        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.RecordBrokerApprovalAsync(approval, invalid));
        var unapproved = await store.LoadJournalAsync(plan.Key);
        Assert.NotNull(unapproved);
        Assert.Empty(unapproved.Approvals);
        Assert.Empty(unapproved.Entries);

        var dispatch = BrokerEntries(plan, approval, JournalState.Approved, JournalState.Started,
            JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.BrokerDispatchIntent, Guid.NewGuid(), Now.AddMinutes(1));
        Assert.Equal(JournalWriteResult.Recorded, await store.RecordBrokerApprovalAsync(approval, approved));
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await store.RecordBrokerApprovalAsync(approval, approved));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(dispatch[0]));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendBrokerBatchAsync([dispatch[0]]));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendBrokerBatchAsync(dispatch));
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await store.AppendBrokerBatchAsync(dispatch));
        var history = await store.LoadJournalAsync(plan.Key);
        Assert.NotNull(history);
        Assert.Equal(approved.Concat(dispatch), history.Entries.AsEnumerable());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokerDispatchCommitInterruptionReopensAtomicallyAndDeclineRequiresFreshApproval(bool committed)
    {
        using var workspace = new PersistenceTestWorkspace();
        JournalPlanSnapshot plan;
        JournalApproval approval;
        await using (var store = await OpenAsync(workspace))
        {
            var (job, intake) = await CreateJobAsync(store);
            plan = CreateSnapshot(job, intake, "0.1.0-candidate.1", actionCount: 2);
            await store.SaveJournalPlanAsync(plan);
            approval = new JournalApproval(ApprovalId, plan.Key, Approver, CorrelationId, Now);
            await store.RecordBrokerApprovalAsync(approval, BrokerEntries(plan, approval, JournalState.Planned, JournalState.Approved,
                JournalActor.Operator, JournalResult.None, JournalCheckpoint.BrokerApproval, approval.CorrelationId, Now));
        }

        var dispatch = BrokerEntries(plan, approval, JournalState.Approved, JournalState.Started,
            JournalActor.Coordinator, JournalResult.None, JournalCheckpoint.BrokerDispatchIntent, Guid.NewGuid(), Now.AddMinutes(1));
        var point = committed ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit;
        await using (var interrupted = await OpenAsync(workspace, new ThrowingFaultInjector(point)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.AppendBrokerBatchAsync(dispatch));
        }

        await using var reopened = await OpenAsync(workspace);
        var afterFault = await reopened.LoadJournalAsync(plan.Key);
        Assert.NotNull(afterFault);
        Assert.All(plan.Actions, action =>
            Assert.Equal(committed ? JournalState.Started : JournalState.Approved, afterFault.StateOf(action.ActionId)));
        Assert.Equal(committed ? JournalWriteResult.AlreadyRecorded : JournalWriteResult.Recorded,
            await reopened.AppendBrokerBatchAsync(dispatch));

        var declined = BrokerEntries(plan, approval, JournalState.Started, JournalState.Failed,
            JournalActor.Coordinator, JournalResult.AuthorizationDeclined, JournalCheckpoint.BrokerPreDispatchFailure,
            dispatch[0].CorrelationId, Now.AddMinutes(2));
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendBrokerBatchAsync(declined));
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await reopened.AppendBrokerBatchAsync(declined));
        var renewed = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), Now.AddMinutes(3));
        Assert.Equal(JournalWriteResult.Recorded, await reopened.RecordBrokerApprovalAsync(renewed,
            BrokerEntries(plan, renewed, JournalState.Failed, JournalState.Approved, JournalActor.Operator, JournalResult.None,
                JournalCheckpoint.BrokerApproval, renewed.CorrelationId, renewed.OccurredAtUtc)));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendBrokerBatchAsync(
            BrokerEntries(plan, approval, JournalState.Approved, JournalState.Started, JournalActor.Coordinator, JournalResult.None,
                JournalCheckpoint.BrokerDispatchIntent, Guid.NewGuid(), Now.AddMinutes(4))));

        var history = await reopened.LoadJournalAsync(plan.Key);
        Assert.NotNull(history);
        Assert.Equal(renewed, history.ActiveApproval);
        Assert.All(plan.Actions, action => Assert.Equal(JournalState.Approved, history.StateOf(action.ActionId)));
        Assert.Equal(plan.Actions.Length, history.Entries.Count(entry => entry.Checkpoint == JournalCheckpoint.BrokerDispatchIntent));
        Assert.Equal(plan.Actions.Length, history.Entries.Count(entry => entry.Result == JournalResult.AuthorizationDeclined));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchIntentCommitInterruptionReopensAndRetriesWithoutDuplicateTransitions(bool committed)
    {
        using var workspace = new PersistenceTestWorkspace();
        JournalPlanSnapshot plan;
        await using (var store = await OpenAsync(workspace))
        {
            plan = await SaveApprovedPlanAsync(store);
        }

        var start = Start(plan);
        var point = committed ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit;
        await using (var interrupted = await OpenAsync(workspace, new ThrowingFaultInjector(point)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.AppendJournalEntryAsync(start));
        }

        await using (var reopened = await OpenAsync(workspace))
        {
            var afterFault = await reopened.LoadJournalAsync(plan.Key);
            Assert.NotNull(afterFault);
            Assert.Equal(committed ? JournalState.Started : JournalState.Approved, afterFault.StateOf(start.ActionId));
            Assert.Equal(committed ? JournalWriteResult.AlreadyRecorded : JournalWriteResult.Recorded,
                await reopened.AppendJournalEntryAsync(start));

            var recover = Entry(plan, AppliedEntryId, JournalState.Started, JournalState.RequiresReview,
                JournalActor.Coordinator, JournalResult.Interrupted, Now.AddMinutes(2), JournalCheckpoint.Reconcile);
            Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(recover));
        }

        await using var recovered = await OpenAsync(workspace);
        var history = await recovered.LoadJournalAsync(plan.Key);
        Assert.NotNull(history);
        Assert.Equal(plan.Key, history.Plan.Key);
        Assert.Equal(plan.CanonicalPlan.ToArray(), history.Plan.CanonicalPlan.ToArray());
        Assert.Equal(plan.CanonicalImpact.ToArray(), history.Plan.CanonicalImpact.ToArray());
        Assert.Equal(ApprovalId, Assert.Single(history.Approvals).ApprovalId);
        Assert.Equal(ApprovalId, history.ActiveApproval?.ApprovalId);
        Assert.Equal(JournalState.RequiresReview, history.StateOf(start.ActionId));
        Assert.Equal(
            [JournalState.Approved, JournalState.Started, JournalState.RequiresReview],
            history.Entries.Select(entry => entry.State));
        Assert.Equal(CorrelationId, history.Entries[1].CorrelationId);
        Assert.Equal(JournalCheckpoint.DispatchIntent, history.Entries[1].Checkpoint);
        Assert.Equal(Now.AddMinutes(1), history.Entries[1].OccurredAtUtc);
    }

    [Fact]
    public async Task AppliedRequiresSeparateVerificationAndRoundTripsTheVerifiedTransition()
    {
        using var workspace = new PersistenceTestWorkspace();
        JournalPlanSnapshot plan;
        await using (var store = await OpenAsync(workspace))
        {
            plan = await SaveApprovedPlanAsync(store);
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(Start(plan)));
            var applied = Entry(plan, AppliedEntryId, JournalState.Started, JournalState.Applied,
                JournalActor.Broker, JournalResult.BackendApplied, Now.AddMinutes(2));
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied));
            var missingVerifierProof = Entry(plan, VerifiedEntryId, JournalState.Applied, JournalState.Verified,
                JournalActor.Verifier, JournalResult.VerificationPassed, Now.AddMinutes(3));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(missingVerifierProof));
        }

        await using var reopened = await OpenAsync(workspace);
        var afterApply = await reopened.LoadJournalAsync(plan.Key);
        Assert.NotNull(afterApply);
        Assert.Equal(JournalState.Applied, afterApply.StateOf(plan.Actions[0].Request.ActionId));
        var verified = Entry(plan, VerifiedEntryId, JournalState.Applied, JournalState.Verified,
            JournalActor.Verifier, JournalResult.VerificationPassed, Now.AddMinutes(3),
            verification: ActionVerification.NoMachineChange);
        Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(verified));
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await reopened.AppendJournalEntryAsync(verified));
        var final = await reopened.LoadJournalAsync(plan.Key);
        Assert.NotNull(final);
        Assert.Equal(JournalState.Verified, final.StateOf(verified.ActionId));
        var persisted = final.Entries[^1];
        Assert.Equal(JournalState.Applied, persisted.PriorState);
        Assert.Equal(JournalActor.Verifier, persisted.Actor);
        Assert.Equal(verified.ActorId, persisted.ActorId);
        Assert.Equal(ApprovalId, persisted.ApprovalId);
        Assert.Equal(CorrelationId, persisted.CorrelationId);
        Assert.Equal(ActionVerification.NoMachineChange, persisted.Verification);
    }

    [Fact]
    public async Task ActionScheduleProjectsSavedDependenciesAndRecoveryAfterReopen()
    {
        using var workspace = new PersistenceTestWorkspace();
        JournalPlanSnapshot plan;
        JournalApproval approval;
        ScheduledAction[] steps;
        Guid first;
        Guid second;
        Guid later;
        await using (var store = await OpenAsync(workspace))
        {
            var (job, intake) = await CreateJobAsync(store);
            plan = CreateSnapshot(job, intake, "0.1.0-candidate.1", actionCount: 3);
            first = plan.Actions[0].ActionId;
            second = plan.Actions[1].ActionId;
            later = plan.Actions[2].ActionId;
            steps = [new(later, 3, []), new(second, 2, [first]), new(first, 1, [])];
            _ = ActionSchedule.Create(plan, steps);
            Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
            approval = new JournalApproval(ApprovalId, plan.Key, Approver, CorrelationId, Now.AddMinutes(-1));
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalApprovalAsync(approval));
            foreach (var action in plan.Actions)
            {
                Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(
                    Entry(plan, Guid.NewGuid(), JournalState.Planned, JournalState.Approved, JournalActor.Operator,
                        JournalResult.None, Now, actionId: action.ActionId)));
            }

            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(Start(plan)));
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(
                Entry(plan, AppliedEntryId, JournalState.Started, JournalState.Applied, JournalActor.Broker,
                    JournalResult.BackendApplied, Now.AddMinutes(2))));
        }

        await using (var reopened = await OpenAsync(workspace))
        {
            var saved = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
            var schedule = ActionSchedule.Create(saved.Plan, steps);
            Assert.Equal(plan.Key, schedule.PlanKey);
            Assert.Equal(plan.Key, saved.Plan.Key);
            Assert.Equal(plan.CanonicalPlan.ToArray(), saved.Plan.CanonicalPlan.ToArray());
            Assert.Equal(plan.CanonicalImpact.ToArray(), saved.Plan.CanonicalImpact.ToArray());
            Assert.Equal(approval, saved.ActiveApproval);

            var pending = schedule.Project(saved);
            Assert.True(pending.RequiresRecovery);
            Assert.Null(pending.NextForReview);
            Assert.Equal(JournalState.Applied, pending.Actions.Single(action => action.ActionId == first).State);
            Assert.Equal(first, Assert.Single(pending.Actions.Single(action => action.ActionId == second).PendingDependencies));
            Assert.True(pending.Actions.Single(action => action.ActionId == later).EarlierPhasePending);

            Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(
                Entry(plan, VerifiedEntryId, JournalState.Applied, JournalState.Verified, JournalActor.Verifier,
                    JournalResult.VerificationPassed, Now.AddMinutes(3), verification: ActionVerification.NoMachineChange)));
            var ready = schedule.Project(Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key)));
            Assert.False(ready.RequiresRecovery);
            Assert.Equal(second, ready.NextForReview);
            Assert.Empty(ready.Actions.Single(action => action.ActionId == second).PendingDependencies);

            Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(
                Entry(plan, Guid.NewGuid(), JournalState.Approved, JournalState.Started, JournalActor.Coordinator,
                    JournalResult.None, Now.AddMinutes(4), JournalCheckpoint.DispatchIntent, actionId: second)));
            Assert.Equal(JournalWriteResult.Recorded, await reopened.AppendJournalEntryAsync(
                Entry(plan, Guid.NewGuid(), JournalState.Started, JournalState.RequiresReview, JournalActor.Coordinator,
                    JournalResult.Interrupted, Now.AddMinutes(5), JournalCheckpoint.Reconcile, actionId: second)));
        }

        await using var recovered = await OpenAsync(workspace);
        var durable = Assert.IsType<JournalHistory>(await recovered.LoadJournalAsync(plan.Key));
        Assert.Equal(approval, durable.ActiveApproval);
        var projection = ActionSchedule.Create(durable.Plan, steps).Project(durable);
        Assert.True(projection.RequiresRecovery);
        Assert.Null(projection.NextForReview);
        Assert.Collection(projection.Actions,
            verified =>
            {
                Assert.Equal(first, verified.ActionId);
                Assert.Equal(JournalState.Verified, verified.State);
                Assert.Equal(JournalResult.VerificationPassed, verified.Result);
            },
            interrupted =>
            {
                Assert.Equal(second, interrupted.ActionId);
                Assert.Equal(JournalState.RequiresReview, interrupted.State);
                Assert.Equal(JournalCheckpoint.Reconcile, interrupted.Checkpoint);
                Assert.Equal(JournalResult.Interrupted, interrupted.Result);
            },
            waiting =>
            {
                Assert.Equal(later, waiting.ActionId);
                Assert.Equal(JournalState.Approved, waiting.State);
                Assert.True(waiting.EarlierPhasePending);
            });
    }

    [Fact]
    public async Task ReopenedLegacyPlanRetainsApprovalHistoryWithoutNewDispatchAuthority()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        JournalPlanSnapshot legacy;
        await using (var store = await OpenAsync(workspace))
        {
            var (stored, intake) = await CreateJobAsync(store);
            var current = CreateSnapshot(stored, intake, "0.1.0-candidate.2");
            databasePath = store.DatabasePath;
            var plan = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current.CanonicalPlan.AsSpan())
                .Replace("thirdlife.plan.v2", "thirdlife.plan.v1", StringComparison.Ordinal));
            var planSha = Digest(plan);
            var impact = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current.CanonicalImpact.AsSpan())
                .Replace(current.Key.PlanSha256, planSha, StringComparison.Ordinal));
            legacy = JournalPlanSnapshot.Restore(new JournalPlanKey(current.Key.JobId, planSha, Digest(impact)), plan, impact);
        }

        // Seed a representative pre-amendment approval and transition in this disposable store.
        var key = legacy.Key;
        var approval = new JournalApproval(ApprovalId, key, Approver, CorrelationId, Now.AddMinutes(-1));
        var approved = Entry(legacy, ApprovedEntryId, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None, Now);
        var approvalJson = JsonSerializer.SerializeToUtf8Bytes(approval, DomainJson.CreateStrictOptions());
        var approvedJson = JsonSerializer.SerializeToUtf8Bytes(approved, DomainJson.CreateStrictOptions());
        await SqliteTestControl.ExecuteAsync(databasePath, string.Create(CultureInfo.InvariantCulture, $"""
            INSERT INTO action_journal_plans (job_id, plan_sha256, impact_sha256, plan_json, impact_json)
            VALUES ('{key.JobId.Value}', '{key.PlanSha256}', '{key.ImpactSha256}',
                X'{Convert.ToHexString(legacy.CanonicalPlan.AsSpan())}', X'{Convert.ToHexString(legacy.CanonicalImpact.AsSpan())}');
            INSERT INTO action_journal_approvals (job_id, approval_id, plan_sha256, impact_sha256, payload_json, payload_sha256)
            VALUES ('{key.JobId.Value}', '{approval.ApprovalId:D}', '{key.PlanSha256}', '{key.ImpactSha256}',
                CAST(X'{Convert.ToHexString(approvalJson)}' AS TEXT), '{Digest(approvalJson)}');
            INSERT INTO action_journal_entries (job_id, entry_id, plan_sha256, impact_sha256, action_id, state, payload_json, payload_sha256)
            VALUES ('{key.JobId.Value}', '{approved.EntryId:D}', '{key.PlanSha256}', '{key.ImpactSha256}',
                '{approved.ActionId:D}', {(int)approved.State}, CAST(X'{Convert.ToHexString(approvedJson)}' AS TEXT), '{Digest(approvedJson)}');
            """));

        await using var reopened = await OpenAsync(workspace);
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await reopened.AppendJournalApprovalAsync(approval));
        var replacement = new JournalApproval(StartedEntryId, key, Approver, CorrelationId, Now.AddMinutes(1));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalApprovalAsync(replacement));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(Start(legacy)));

        var history = await reopened.LoadJournalAsync(key);
        Assert.NotNull(history);
        Assert.False(history.Plan.IsCurrentPolicyPlan);
        Assert.False(history.Plan.HasPlanningProvenance);
        Assert.Equal(legacy.CanonicalPlan.AsEnumerable(), history.Plan.CanonicalPlan.AsEnumerable());
        Assert.Equal(legacy.CanonicalImpact.AsEnumerable(), history.Plan.CanonicalImpact.AsEnumerable());
        Assert.Equal(approval, Assert.Single(history.Approvals));
        Assert.Equal(approved, Assert.Single(history.Entries));
        Assert.Null(history.ActiveApproval);
    }

    [Fact]
    public async Task ANewIntakeGateCannotDispatchThePreviouslyApprovedPlan()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace);
        var plan = await SaveApprovedPlanAsync(store);
        var replacement = PersistenceTestData.CreateSanitization(
            SanitizationState.Verified, "journal-replacement", Now.AddMinutes(1));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(plan.Key.JobId, sanitizationEvidence: [replacement]));
        var current = await store.RecordSanitizationGateDecisionAsync(
            SanitizationGate.Evaluate(plan.Key.JobId, replacement, Now.AddMinutes(2)));
        Assert.NotEqual(plan.GateDecisionId, current.DecisionId);

        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(Start(plan)));

        var history = await store.LoadJournalAsync(plan.Key);
        Assert.NotNull(history);
        Assert.Equal(ApprovalId, Assert.Single(history.Approvals).ApprovalId);
        Assert.Equal(JournalState.Approved, Assert.Single(history.Entries).State);
        Assert.Null(history.ActiveApproval);
    }

    [Fact]
    public async Task AnIndexedStateThatDisagreesWithItsPayloadBlocksAnotherPlanApproval()
    {
        using var workspace = new PersistenceTestWorkspace();
        JournalPlanSnapshot proposed;
        string databasePath;
        await using (var store = await OpenAsync(workspace))
        {
            var approved = await SaveApprovedPlanAsync(store);
            Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(Start(approved)));
            var stored = await store.LoadJobAsync(approved.Key.JobId);
            Assert.NotNull(stored);
            proposed = CreateSnapshot(stored, Assert.Single(stored.SanitizationEvidence), "0.1.0-candidate.2");
            Assert.NotEqual(approved.Key, proposed.Key);
            Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(proposed));
            databasePath = store.DatabasePath;
        }

        // Intentionally corrupt only the index projection in this disposable test database.
        await SqliteTestControl.ExecuteAsync(databasePath, string.Create(CultureInfo.InvariantCulture,
            $"UPDATE action_journal_entries SET state = {(int)JournalState.Verified} WHERE state = {(int)JournalState.Started};"));

        await using var reopened = await OpenAsync(workspace);
        var replacementApproval = new JournalApproval(new Guid("6ad285fe-8912-4dc8-a4bb-f4089582825a"),
            proposed.Key, Approver, CorrelationId, Now.AddMinutes(2));
        await Assert.ThrowsAsync<JobStoreCorruptionException>(() => reopened.AppendJournalApprovalAsync(replacementApproval));
    }

    private static async Task<(StoredJob Job, SanitizationEvidence Intake)> CreateJobAsync(SqliteJobStore store)
    {
        var job = PersistenceTestData.CreateJob("action-journal");
        var intake = PersistenceTestData.CreateSanitization("action-journal");
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [intake]));
        var gate = await store.RecordSanitizationGateDecisionAsync(
            SanitizationGate.Evaluate(job.JobId, intake, PersistenceTestData.Timestamp.AddMinutes(2)));
        var stored = await store.LoadJobAsync(job.JobId);
        Assert.NotNull(stored);
        Assert.Equal(gate.DecisionId, SanitizationGate.Inspect(stored).Decision?.DecisionId);
        return (stored, intake);
    }

    private static async Task<JournalPlanSnapshot> SaveApprovedPlanAsync(SqliteJobStore store)
    {
        var (stored, intake) = await CreateJobAsync(store);
        var plan = CreateSnapshot(stored, intake, "0.1.0-candidate.1");
        var restoredOnly = JournalPlanSnapshot.Restore(plan.Key, plan.CanonicalPlan.ToArray(), plan.CanonicalImpact.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveJournalPlanAsync(restoredOnly));
        Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await store.SaveJournalPlanAsync(plan));
        var approval = new JournalApproval(ApprovalId, plan.Key, Approver, CorrelationId, Now.AddMinutes(-1));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalApprovalAsync(approval));
        Assert.Equal(JournalWriteResult.AlreadyRecorded, await store.AppendJournalApprovalAsync(approval));
        var approved = Entry(plan, ApprovedEntryId, JournalState.Planned, JournalState.Approved,
            JournalActor.Operator, JournalResult.None, Now);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(approved));
        return plan;
    }

    private static JournalPlanSnapshot CreateSnapshot(StoredJob stored, SanitizationEvidence intake, string profileVersion, int actionCount = 1)
    {
        var catalog = CatalogSchema.Parse(ReadFixture("catalog"), Now);
        var profileJson = JsonNode.Parse(ReadFixture("basic"))!;
        profileJson["version"] = profileVersion;
        profileJson["policy"]!["id"] = Policy.PolicyId;
        profileJson["policy"]!["version"] = Policy.Version;
        var workshop = profileJson["workshop"]!;
        workshop["capabilities"] = new JsonArray(workshop["capabilities"]!.AsArray().Take(actionCount)
            .Select(capability => capability!.DeepClone()).ToArray());
        var profile = ProfileSchema.Parse(Encoding.UTF8.GetBytes(profileJson.ToJsonString()), catalog);
        Observation[] facts =
        [
            new(intake.Metadata, new EvidenceKey("sanitization.state"), EvidenceValue.FromEnum("verified"), null, null),
            ObservedFact("system.architecture", "x64", "architecture"),
            ObservedFact("operating_system.release", "windows_11", "release"),
            ObservedFact("operating_system.support_state", "supported", "support"),
        ];
        var proposal = PlanResolver.Resolve(stored, Policy, profile, catalog, facts, [], Now);
        Assert.Null(proposal.BlockedReason);
        return JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
    }

    private static ImmutableArray<JournalEntry> BrokerEntries(JournalPlanSnapshot plan, JournalApproval approval, JournalState prior,
        JournalState state, JournalActor actor, JournalResult result, JournalCheckpoint checkpoint, Guid correlation, DateTimeOffset time) =>
        plan.Actions.Select(action => new JournalEntry(Guid.NewGuid(), plan.Key, action.ActionId, approval.ApprovalId, prior, state,
            actor, actor == JournalActor.Operator ? approval.ActorId : new OperatorId("actor-" + actor.ToString().ToLowerInvariant()),
            correlation, time, result, checkpoint)).ToImmutableArray();

    private static JournalEntry Start(JournalPlanSnapshot plan) =>
        Entry(plan, StartedEntryId, JournalState.Approved, JournalState.Started,
            JournalActor.Coordinator, JournalResult.None, Now.AddMinutes(1), JournalCheckpoint.DispatchIntent);

    private static JournalEntry Entry(
        JournalPlanSnapshot plan,
        Guid id,
        JournalState prior,
        JournalState next,
        JournalActor actor,
        JournalResult result,
        DateTimeOffset time,
        JournalCheckpoint checkpoint = JournalCheckpoint.None,
        ActionVerification? verification = null,
        Guid? actionId = null) =>
        new(id, plan.Key, actionId ?? plan.Actions[0].ActionId, ApprovalId, prior, next, actor,
            actor == JournalActor.Operator ? Approver : new OperatorId("actor-" + actor.ToString().ToLowerInvariant()),
            CorrelationId, time, result, checkpoint, verification);

    private static Observation ObservedFact(string key, string value, string suffix) =>
        new(PersistenceTestData.CreateMetadata("evidence-journal-" + suffix, EvidenceClassification.Observed,
                ProvenanceKind.SyntheticFixture, ValueAvailability.Available),
            new EvidenceKey(key), EvidenceValue.FromEnum(value), null, null);

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, IJobStoreFaultInjector? fault = null) =>
        SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion, new FixedTimeProvider(Now.AddMinutes(15)), fault);

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name + ".json"));
}
