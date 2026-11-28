using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class PolicyHistoryStoreTests
{
    private const string AssessmentId = "assessment-synthetic-policy";
    private static readonly DateTimeOffset FirstEvaluation = PersistenceTestData.Timestamp.AddMinutes(2);

    [Fact]
    public async Task ReevaluationAppendsWithExactPolicyAndHistoricalReportAfterReopen()
    {
        using var workspace = new PersistenceTestWorkspace();
        var originalSnapshot = Snapshot("1.0.0");
        var newSnapshot = Snapshot("1.1.0", minimumBytes: 4_294_967_296);
        var job = PersistenceTestData.CreateJob();
        var observations = Observations();
        var original = Decide(originalSnapshot, job, observations);
        var reevaluated = Decide(newSnapshot, job, observations, original.DecisionId);
        Assert.Equal(Disposition.RepairAndRetest, original.Disposition);
        Assert.Equal(Disposition.ReadyToPrepare, reevaluated.Disposition);

        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await CreateJobWithEvidenceAsync(store, job, observations);
            await store.AppendPolicyDecisionAsync(originalSnapshot, original);
            await store.AppendPolicyDecisionAsync(newSnapshot, reevaluated);
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var historical = Assert.IsType<StoredPolicyDecision>(
            await reopened.LoadPolicyDecisionAsync(job.JobId, original.DecisionId));
        var latest = Assert.IsType<StoredPolicyDecision>(
            await reopened.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));
        Assert.Equal(originalSnapshot.Utf8Json.ToArray(), historical.Snapshot.Utf8Json.ToArray());
        Assert.Equal("1.0.0", historical.Decision.PolicyVersion);
        Assert.Equal(original.PolicySha256, historical.Decision.PolicySha256);
        Assert.Equal(original.Disposition, historical.Decision.Disposition);
        Assert.Equal(original.Title, historical.Decision.Title);
        Assert.Equal(original.Summary, historical.Decision.Summary);
        Assert.Equal(original.Explanation, historical.Decision.Explanation);
        Assert.Equal(original.RuleReport, historical.Decision.RuleReport);
        Assert.Equal(newSnapshot.Utf8Json.ToArray(), latest.Snapshot.Utf8Json.ToArray());
        Assert.Equal(reevaluated.DecisionId, latest.Decision.DecisionId);
        Assert.Equal(original.DecisionId, latest.Decision.PreviousDecisionId);
        Assert.Equal(original.EvidenceIds.AsEnumerable(), latest.Decision.EvidenceIds.AsEnumerable());
        Assert.NotEqual(historical.Decision.Explanation, latest.Decision.Explanation);
        Assert.Equal(observations, (await reopened.LoadJobAsync(job.JobId))!.Observations);
    }

    [Fact]
    public async Task DeletingOneJobRemovesItsExclusivePolicyBytesAndPreservesReferencedSnapshots()
    {
        using var workspace = new PersistenceTestWorkspace();
        var removedJob = PersistenceTestData.CreateJob("policy-remove");
        var retainedJob = PersistenceTestData.CreateJob("policy-retain");
        var shared = Snapshot("1.0.0");
        var exclusive = Snapshot("1.1.0");
        var removedObservations = Observations();
        var retainedObservations = Observations("-retained");
        var initial = Decide(shared, removedJob, removedObservations);
        var reevaluated = Decide(exclusive, removedJob, removedObservations, initial.DecisionId);
        var retainedDecision = Decide(shared, retainedJob, retainedObservations);
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
            await CreateJobWithEvidenceAsync(store, removedJob, removedObservations);
            await store.AppendPolicyDecisionAsync(shared, initial);
            await CreateJobWithEvidenceAsync(store, retainedJob, retainedObservations);
            await store.AppendPolicyDecisionAsync(shared, retainedDecision);
            await store.AppendPolicyDecisionAsync(exclusive, reevaluated);
            await store.SetArchiveStateAsync(removedJob.JobId, true, FirstEvaluation.AddMinutes(10));
            var archived = Assert.IsType<StoredJob>(await store.LoadJobAsync(removedJob.JobId));
            var checkpoint = archived.Checkpoints[^1].CheckpointId;
            var review = Assert.IsType<ArchivedJobDeletionReview>(await store.ReviewArchivedJobDeletionAsync(removedJob.JobId, checkpoint));
            await store.ApproveArchivedJobDeletionAsync(review, new OperatorId("operator-policy-removal"));
            var outcome = await store.ExecuteArchivedJobDeletionAsync(removedJob.JobId, checkpoint,
                (_, _) => throw new InvalidOperationException("No local files were recorded for the job."));
            Assert.True(outcome.Completed);
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Null(await reopened.LoadJobAsync(removedJob.JobId));
        var retained = Assert.IsType<StoredPolicyDecision>(await reopened.LoadLatestPolicyDecisionAsync(retainedJob.JobId, AssessmentId));
        Assert.Equal(retainedDecision.DecisionId, retained.Decision.DecisionId);
        Assert.Equal(shared.Utf8Json.ToArray(), retained.Snapshot.Utf8Json.ToArray());

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT group_concat(policy_version) FROM policy_snapshots;";
        Assert.Equal("1.0.0", await select.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DifferentBytesAndDowngradeFailWhileCommittedRetriesRemainIdempotent()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var job = PersistenceTestData.CreateJob();
        var observations = Observations();
        var originalSnapshot = Snapshot("1.0.0");
        var upgradedSnapshot = Snapshot("1.1.0");
        var original = Decide(originalSnapshot, job, observations);
        var upgraded = Decide(upgradedSnapshot, job, observations, original.DecisionId);
        await CreateJobWithEvidenceAsync(store, job, observations);
        await store.AppendPolicyDecisionAsync(originalSnapshot, original);
        await store.AppendPolicyDecisionAsync(upgradedSnapshot, upgraded);

        var whitespaceVariant = PolicySnapshot.Parse(
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(upgradedSnapshot.Utf8Json.ToArray()) + "\n"));
        var changedVersionReuse = Decide(whitespaceVariant, job, observations, upgraded.DecisionId, minutesAfterFirst: 2);
        var downgraded = Decide(originalSnapshot, job, observations, upgraded.DecisionId, minutesAfterFirst: 2);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendPolicyDecisionAsync(whitespaceVariant, changedVersionReuse));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendPolicyDecisionAsync(originalSnapshot, downgraded));

        await store.AppendPolicyDecisionAsync(originalSnapshot, original);
        await store.AppendPolicyDecisionAsync(upgradedSnapshot, upgraded);
        Assert.Null(await store.LoadPolicyDecisionAsync(job.JobId, changedVersionReuse.DecisionId));
        Assert.Null(await store.LoadPolicyDecisionAsync(job.JobId, downgraded.DecisionId));
        Assert.Equal(upgraded.DecisionId, (await store.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId))!.Decision.DecisionId);
    }

    [Fact]
    public async Task DeserializedDecisionCanRetryExactCommitButCannotCreateHistory()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var job = PersistenceTestData.CreateJob();
        var observations = Observations();
        var snapshot = Snapshot("1.0.0");
        var evaluated = Decide(snapshot, job, observations);
        var json = DomainJson.CreateStrictOptions();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evaluated, json);
        var restored = Assert.IsType<PolicyDecisionRecord>(JsonSerializer.Deserialize<PolicyDecisionRecord>(bytes, json));
        Assert.True(evaluated.HasEvaluationProvenance);
        Assert.False(restored.HasEvaluationProvenance);
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(restored, json));
        await CreateJobWithEvidenceAsync(store, job, observations);

        await Assert.ThrowsAsync<ArgumentException>("decision", () => store.AppendPolicyDecisionAsync(snapshot, restored));
        Assert.Null(await store.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));

        await store.AppendPolicyDecisionAsync(snapshot, evaluated);
        await store.AppendPolicyDecisionAsync(snapshot, restored);
        var committed = Assert.IsType<StoredPolicyDecision>(
            await store.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));
        Assert.Equal(bytes, JsonSerializer.SerializeToUtf8Bytes(committed.Decision, json));
    }

    [Fact]
    public async Task InvalidEvidenceAndStalePredecessorsCannotChangeHistory()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var job = PersistenceTestData.CreateJob();
        var otherJob = PersistenceTestData.CreateJob("different");
        var observations = Observations();
        var snapshot = Snapshot("1.0.0");
        var original = Decide(snapshot, job, observations);
        var latest = Decide(snapshot, job, observations, original.DecisionId);
        await CreateJobWithEvidenceAsync(store, job, observations);
        await store.CreateJobAsync(otherJob);
        await store.AppendPolicyDecisionAsync(snapshot, original);
        await store.AppendPolicyDecisionAsync(snapshot, latest);

        const string otherAssessment = "assessment-synthetic-other";
        var laterObservation = PersistenceTestData.CreateObservation("later");
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, [laterObservation]));
        var foreignJobRecord = Decide(snapshot, otherJob, observations);
        var missingEvidenceRecord = Decide(
            snapshot, job, [.. observations, PersistenceTestData.CreateObservation("absent")],
            assessmentId: otherAssessment);
        var changedEvidenceRecord = Decide(
            snapshot, job, [.. observations, laterObservation], latest.DecisionId, minutesAfterFirst: 2);
        var stale = Decide(snapshot, job, observations, original.DecisionId, minutesAfterFirst: 2);
        foreach (var rejected in new[] { foreignJobRecord, missingEvidenceRecord, changedEvidenceRecord, stale })
        {
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendPolicyDecisionAsync(snapshot, rejected));
            Assert.Null(await store.LoadPolicyDecisionAsync(rejected.JobId, rejected.DecisionId));
        }

        Assert.Null(await store.LoadPolicyDecisionAsync(otherJob.JobId, original.DecisionId));
        Assert.Null(await store.LoadLatestPolicyDecisionAsync(otherJob.JobId, AssessmentId));
        Assert.Null(await store.LoadLatestPolicyDecisionAsync(job.JobId, otherAssessment));
        Assert.Equal(latest.DecisionId, (await store.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId))!.Decision.DecisionId);
    }

    [Fact]
    public async Task BackdatedEvidenceCommittedBetweenEvaluationAndAppendRequiresFreshDecision()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var job = PersistenceTestData.CreateJob();
        await CreateJobWithEvidenceAsync(store, job, Observations());
        var snapshot = Snapshot("1.0.0");
        var initial = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId));
        var stale = Evaluate(initial);

        var backdated = PersistenceTestData.CreateHumanTest(job, "concurrent");
        Assert.True(backdated.Metadata.CollectedAtUtc < stale.EvaluatedAtUtc);
        await using (var writer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await writer.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, humanTests: [backdated]));
        }

        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendPolicyDecisionAsync(snapshot, stale));
        Assert.Null(await store.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));
        var current = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId));
        Assert.Equal(backdated, Assert.Single(current.HumanTests));
        var fresh = Evaluate(current);
        // The selected observations and evaluation time are identical. Only the job evidence count changed.
        Assert.Equal(stale.EvidenceIds.AsEnumerable(), fresh.EvidenceIds.AsEnumerable());
        Assert.Equal(stale.JobEvidenceCount + 1, fresh.JobEvidenceCount);
        await store.AppendPolicyDecisionAsync(snapshot, fresh);
        var saved = Assert.IsType<StoredPolicyDecision>(await store.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));
        Assert.Equal(fresh.DecisionId, saved.Decision.DecisionId);
        Assert.Equal(fresh.JobEvidenceCount, saved.Decision.JobEvidenceCount);

        PolicyDecisionRecord Evaluate(StoredJob loaded) => PolicyDecisionRecord.Create(snapshot, job.JobId, AssessmentId,
            DispositionEngine.Evaluate(snapshot.Document, loaded.Observations, []),
            loaded.Observations.Select(observation => observation.Metadata.EvidenceId), FirstEvaluation,
            jobEvidenceCount: loaded.Observations.Count + loaded.SanitizationEvidence.Count + loaded.HumanTests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultAroundCommitLeavesPolicySnapshotAndDecisionTogether(bool committed)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var observations = Observations();
        var originalSnapshot = Snapshot("1.0.0");
        var pendingSnapshot = Snapshot("1.1.0");
        var original = Decide(originalSnapshot, job, observations);
        var pending = Decide(pendingSnapshot, job, observations, original.DecisionId);
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await CreateJobWithEvidenceAsync(initializer, job, observations);
            await initializer.AppendPolicyDecisionAsync(originalSnapshot, original);
        }

        var fault = committed ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit;
        await using (var interrupted = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion, TimeProvider.System,
                         new ThrowingFaultInjector(fault)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.AppendPolicyDecisionAsync(pendingSnapshot, pending));
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var stored = await reopened.LoadPolicyDecisionAsync(job.JobId, pending.DecisionId);
        Assert.Equal(committed ? pending.DecisionId : original.DecisionId,
            (await reopened.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId))!.Decision.DecisionId);
        if (committed)
        {
            Assert.NotNull(stored);
            Assert.Equal(pendingSnapshot.Utf8Json.ToArray(), stored.Snapshot.Utf8Json.ToArray());
            await reopened.AppendPolicyDecisionAsync(pendingSnapshot, pending);
        }
        else
        {
            Assert.Null(stored);
            var differentBytes = Snapshot("1.1.0", minimumBytes: 4_294_967_296);
            await reopened.AppendPolicyDecisionAsync(differentBytes, Decide(differentBytes, job, observations, original.DecisionId));
        }
    }

    [Theory]
    [InlineData("UPDATE policy_snapshots SET policy_json = CAST(policy_json || x'0A' AS BLOB);")]
    [InlineData("UPDATE policy_decisions SET payload_json = '{}';")]
    [InlineData("UPDATE policy_decisions SET payload_sha256 = lower(hex(zeroblob(32)));")]
    public async Task CorruptedSnapshotOrDecisionFailsWithoutReplacingEvidence(string mutation)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var observations = Observations();
        var snapshot = Snapshot("1.0.0");
        var decision = Decide(snapshot, job, observations);
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
            await CreateJobWithEvidenceAsync(store, job, observations);
            await store.AppendPolicyDecisionAsync(snapshot, decision);
        }

        await SqliteTestControl.ExecuteAsync(databasePath, mutation);
        var tamperedHash = SqliteTestControl.HashFile(databasePath);
        await Assert.ThrowsAsync<JobStoreCorruptionException>(async () =>
        {
            await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
            _ = await reopened.LoadPolicyDecisionAsync(job.JobId, decision.DecisionId);
        });
        Assert.Equal(tamperedHash, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task InterruptedVersionFourMigrationPreservesVersionThreeJobAndLaterUpgradeContinuity()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("version-three");
        var observations = Observations();
        var sanitization = PersistenceTestData.CreateSanitization();
        var humanTest = PersistenceTestData.CreateHumanTest(job);
        SanitizationGateDecision gate;
        string databasePath;
        await using (var versionThree = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot, maximumMigrationVersion: 3, TimeProvider.System, faultInjector: null))
        {
            databasePath = versionThree.DatabasePath;
            await versionThree.CreateJobAsync(job);
            await versionThree.AppendEvidenceAsync(new JobEvidenceBatch(
                job.JobId, observations, sanitizationEvidence: [sanitization], humanTests: [humanTest]));
            gate = await versionThree.RecordSanitizationGateDecisionAsync(
                SanitizationGate.Evaluate(job.JobId, sanitization, FirstEvaluation));
        }

        await Assert.ThrowsAsync<InjectedPersistenceException>(() => SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot, maximumMigrationVersion: 4, TimeProvider.System,
            new ThrowingFaultInjector(JobStoreFaultPoint.BeforeMigrationCommit, detail: 4)));
        Assert.Equal(3, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        await Assert.ThrowsAsync<SqliteException>(() =>
            SqliteTestControl.ExecuteAsync(databasePath, "SELECT COUNT(*) FROM policy_snapshots;"));

        var policy = Snapshot("1.0.0");
        var decision = Decide(policy, job, observations);
        await using (var migrated = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot, maximumMigrationVersion: 4, TimeProvider.System, faultInjector: null))
        {
            Assert.Equal(4, await SqliteTestControl.ReadUserVersionAsync(databasePath));
            var loaded = (await migrated.LoadJobAsync(job.JobId))!;
            Assert.Equal(job, loaded.Job);
            Assert.Equal(observations, loaded.Observations);
            Assert.Equal(sanitization, Assert.Single(loaded.SanitizationEvidence));
            Assert.Equal(humanTest, Assert.Single(loaded.HumanTests));
            Assert.Equal(gate, Assert.Single(loaded.SanitizationGateDecisions));
            Assert.Null(await migrated.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));
            await migrated.AppendPolicyDecisionAsync(policy, decision);
        }

        await using var reopened = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot, maximumMigrationVersion: 4, TimeProvider.System, faultInjector: null);
        var restored = Assert.IsType<StoredPolicyDecision>(await reopened.LoadLatestPolicyDecisionAsync(job.JobId, AssessmentId));
        Assert.Equal(policy.Utf8Json.ToArray(), restored.Snapshot.Utf8Json.ToArray());
        Assert.Equal(decision.DecisionId, restored.Decision.DecisionId);
    }

    private static PolicySnapshot Snapshot(string version, long minimumBytes = 8_589_934_592)
    {
        var candidateJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy.json"));
        var candidate = JsonNode.Parse(candidateJson)!.AsObject();
        var rule = candidate["rules"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "POL-MEM-001")!.DeepClone();
        rule["condition"]!["value"] = minimumBytes;
        candidate["rules"] = new JsonArray(rule);
        candidate["version"] = version;
        return PolicySnapshot.Parse(Encoding.UTF8.GetBytes(candidate.ToJsonString()));
    }

    private static PolicyDecisionRecord Decide(
        PolicySnapshot snapshot, Job job, IReadOnlyList<Observation> evidence,
        string? predecessor = null, int minutesAfterFirst = 1, string assessmentId = AssessmentId) =>
        PolicyDecisionRecord.Create(
            snapshot, job.JobId, assessmentId, DispositionEngine.Evaluate(snapshot.Document, evidence, []),
            evidence.Select(observation => observation.Metadata.EvidenceId),
            predecessor is null ? FirstEvaluation : FirstEvaluation.AddMinutes(minutesAfterFirst), predecessor);

    private static Observation[] Observations(string suffix = "") =>
    [
        Fact("operating-system" + suffix, "operating_system.release", EvidenceValue.FromEnum("windows_11")),
        Fact("support" + suffix, "operating_system.support_state", EvidenceValue.FromEnum("supported")),
        Fact("architecture" + suffix, "system.architecture", EvidenceValue.FromEnum("x64")),
        Fact("memory" + suffix, "memory.installed_bytes", EvidenceValue.FromInteger(4_294_967_296), "bytes"),
    ];

    private static Observation Fact(string id, string key, EvidenceValue value, string? unit = null) =>
        new(
            PersistenceTestData.CreateMetadata(
                "evidence-policy-" + id, EvidenceClassification.Observed, ProvenanceKind.SyntheticFixture, ValueAvailability.Available),
            new EvidenceKey(key), value, unit, limitationCode: null);

    private static async Task CreateJobWithEvidenceAsync(SqliteJobStore store, Job job, Observation[] observations)
    {
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, observations));
    }
}
