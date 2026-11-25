using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.Recovery;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class RebootCheckpointStoreTests
{
    private static readonly DateTimeOffset IssueTime = PersistenceTestData.Timestamp.AddHours(1);
    private static readonly Guid ApprovalId = new("3ae8da15-9bb9-4269-b42b-22af93d59eb2");
    private static readonly Guid CorrelationId = new("9f2d59b4-fbac-45f3-a406-3724fad03b0e");
    private static readonly Guid ApprovedId = new("c2cded1b-83b8-4a0b-bff6-300c437cb215");
    private static readonly Guid StartedId = new("165ca0fe-339a-4bfa-8fb3-8fdeccdefc88");
    private static readonly Guid AppliedId = new("d3732ee7-14f5-44f6-9ed9-df2635cb32cd");
    private static readonly Guid VerifiedId = new("bf24cad0-40a6-4e41-a68a-310d66ab973d");
    private static readonly OperatorId Approver = new("operator-reboot-fixture");
    private static readonly PolicyDocument Policy = PolicySchema.Parse("""
        {
          "schema_version":"thirdlife.policy.v1","policy_id":"community-policy","version":"1.0.0",
          "status":"effective","synthetic_data":false,"classification":"PUBLIC_REFERENCE","claim_scope":"reboot_fixture",
          "effective":{
            "status":"effective","effective_from_utc":"2029-12-01T00:00:00Z","effective_until_utc":"2031-01-01T00:00:00Z"
          },
          "target":{"operating_system":"windows_11","support_state":"supported","architecture":"x64","device_class":"laptop"},
          "rules":[{
            "id":"POL-SAN-001","requirement_type":"blocking","severity":"critical","evidence_key":"sanitization.state",
            "condition":{"type":"equals","value":"verified"},
            "decision":{"blocks_ready":true,"unmet_disposition":"human_review_required","unavailable_disposition":"human_review_required"}
          }]
        }
        """u8.ToArray());

    [Fact]
    public async Task ReopenedOfferRequiresAnExplicitDecisionAndRetainsTheAppliedJournalState()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        RebootResumeCandidate issued;
        JournalEntry applied;
        await using (var store = await OpenAsync(workspace, clock))
        {
            applied = await SaveRestartRequiredAsync(store, "reopen");
            issued = Assert.IsType<RebootResumeCandidate>(await store.IssueRebootCheckpointAsync(applied));
            var repeated = Assert.IsType<RebootResumeCandidate>(await store.IssueRebootCheckpointAsync(applied));
            Assert.Equal(issued.Checkpoint.Id, repeated.Checkpoint.Id);
            Assert.True(issued.Token.AuthenticityBlob.AsSpan().SequenceEqual(repeated.Token.AuthenticityBlob.AsSpan()));
            var pending = Assert.Single(await store.ListPendingRebootCheckpointsAsync(applied.Plan.JobId));
            Assert.Equal(issued.Checkpoint.Id, pending.Checkpoint.Id);
        }

        await using var reopened = await OpenAsync(workspace, clock);
        var jobId = applied.Plan.JobId;
        var id = issued.Checkpoint.Id;
        var current = Assert.IsType<RebootResumeCandidate>(await reopened.LoadRebootCheckpointAsync(jobId, id));
        Assert.Equal(current.Checkpoint.Id, issued.Checkpoint.Id);
        Assert.Equal(issued.Checkpoint.CreateAuthenticationData(), current.Checkpoint.CreateAuthenticationData());
        Assert.True(issued.Token.AuthenticityBlob.AsSpan().SequenceEqual(current.Token.AuthenticityBlob.AsSpan()));
        Assert.True(current.Checkpoint.MatchesRestart(applied));
        await AssertAppliedAsync(reopened, applied);

        var resumed = Assert.IsType<RebootCheckpoint>(
            await reopened.DecideRebootCheckpointAsync(jobId, id, current.Token, RebootDisposition.Resume));
        Assert.Equal(id, resumed.Id);
        Assert.Null(await reopened.LoadRebootCheckpointAsync(jobId, id));
        Assert.Null(await reopened.IssueRebootCheckpointAsync(applied));
        Assert.Null(await reopened.DecideRebootCheckpointAsync(jobId, id, current.Token, RebootDisposition.Resume));
        Assert.Empty(await reopened.ListPendingRebootCheckpointsAsync(jobId));
        await using var afterDecision = await OpenAsync(workspace, clock);
        Assert.Equal(new RebootRecoveryDecision(issued.Checkpoint, RebootDisposition.Resume, IssueTime),
            await afterDecision.LoadLatestRebootDecisionAsync(jobId));
        await AssertAppliedAsync(reopened, applied);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(
            Entry(applied.Plan, applied.ActionId, VerifiedId, JournalState.Applied, JournalState.Verified,
                JournalActor.Verifier, JournalResult.VerificationPassed, IssueTime,
                verification: ActionVerification.ExactPackageIdentity)));
    }

    [Fact]
    public async Task ClockRollbackAndExpiryDenyResumeButAnExpiredOfferCanBeAbandoned()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        await using var store = await OpenAsync(workspace, clock);
        var applied = await SaveRestartRequiredAsync(store, "expiry");
        var candidate = Assert.IsType<RebootResumeCandidate>(await store.IssueRebootCheckpointAsync(applied));
        var checkpoint = candidate.Checkpoint;

        clock.Advance(TimeSpan.FromTicks(-1));
        Assert.Null(await store.DecideRebootCheckpointAsync(
            checkpoint.Plan.JobId, checkpoint.Id, candidate.Token, RebootDisposition.Resume));
        clock.Advance(checkpoint.ExpiresAtUtc - clock.GetUtcNow());
        Assert.Null(await store.DecideRebootCheckpointAsync(
            checkpoint.Plan.JobId, checkpoint.Id, candidate.Token, RebootDisposition.Resume));

        var abandoned = Assert.IsType<RebootCheckpoint>(await store.DecideRebootCheckpointAsync(
            checkpoint.Plan.JobId, checkpoint.Id, candidate.Token, RebootDisposition.Abandon));
        Assert.Equal(checkpoint.CreateAuthenticationData(), abandoned.CreateAuthenticationData());
        Assert.Null(await store.LoadRebootCheckpointAsync(checkpoint.Plan.JobId, checkpoint.Id));
        Assert.Empty(await store.ListPendingRebootCheckpointsAsync(checkpoint.Plan.JobId));
        await AssertAppliedAsync(store, applied);
    }

    [Fact]
    public async Task VerifiedActionsCannotProduceResumeOffers()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace, new FixedTimeProvider(IssueTime));
        var applied = await SaveAppliedAsync(store, "already-verified", needsRestart: false);
        var verified = Entry(applied.Plan, applied.ActionId, VerifiedId, JournalState.Applied, JournalState.Verified,
            JournalActor.Verifier, JournalResult.VerificationPassed, IssueTime,
            verification: ActionVerification.ExactPackageIdentity);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(verified));

        Assert.Null(await store.IssueRebootCheckpointAsync(applied));
        Assert.Null(await store.IssueRebootCheckpointAsync(verified));
        Assert.Empty(await store.ListPendingRebootCheckpointsAsync(applied.Plan.JobId));
        var history = await store.LoadJournalAsync(applied.Plan);
        Assert.NotNull(history);
        Assert.Equal(JournalState.Verified, history.StateOf(applied.ActionId));
    }

    [Fact]
    public async Task ANewIntakeRecordBlocksResumeAndLeavesTheOfferAvailableToAbandon()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace, new FixedTimeProvider(IssueTime));
        var applied = await SaveRestartRequiredAsync(store, "stale-gate");
        var candidate = Assert.IsType<RebootResumeCandidate>(await store.IssueRebootCheckpointAsync(applied));
        var checkpoint = candidate.Checkpoint;
        var jobId = applied.Plan.JobId;
        await store.AppendEvidenceAsync(new JobEvidenceBatch(jobId,
            sanitizationEvidence: [PersistenceTestData.CreateSanitization("reboot-replaced-intake")]));

        Assert.Null(await store.DecideRebootCheckpointAsync(jobId, checkpoint.Id, candidate.Token, RebootDisposition.Resume));
        Assert.NotNull(await store.LoadRebootCheckpointAsync(jobId, checkpoint.Id));
        Assert.Equal(checkpoint, await store.DecideRebootCheckpointAsync(
            jobId, checkpoint.Id, candidate.Token, RebootDisposition.Abandon));
        Assert.Equal(new RebootRecoveryDecision(checkpoint, RebootDisposition.Abandon, IssueTime),
            await store.LoadLatestRebootDecisionAsync(jobId));
        await AssertAppliedAsync(store, applied);
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task TokenTamperCrossJobAndConcurrentReplayStayFailClosed()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        await using var firstStore = await OpenAsync(workspace, clock);
        var first = await SaveRestartRequiredAsync(firstStore, "first-token");
        var second = await SaveRestartRequiredAsync(firstStore, "second-token");
        var issued = Assert.IsType<RebootResumeCandidate>(await firstStore.IssueRebootCheckpointAsync(first));
        var other = Assert.IsType<RebootResumeCandidate>(await firstStore.IssueRebootCheckpointAsync(second));
        var changed = issued.Token.AuthenticityBlob.ToArray();
        changed[changed.Length / 2] ^= 1;
        var tampered = new RebootResumeToken(changed);
        using (var otherWorkspace = new PersistenceTestWorkspace())
        using (var otherLayout = RestrictedStoreLayout.CreateOrOpen(otherWorkspace.StoreRoot))
        using (var otherGuard = otherLayout.OpenDatabaseGuard())
        {
            Assert.False(WindowsRebootAuthenticator.Matches(issued.Checkpoint, issued.Token, otherGuard));
        }

        Assert.Null(await firstStore.DecideRebootCheckpointAsync(
            first.Plan.JobId, issued.Checkpoint.Id, tampered, RebootDisposition.Resume));
        Assert.Null(await firstStore.DecideRebootCheckpointAsync(
            second.Plan.JobId, other.Checkpoint.Id, issued.Token, RebootDisposition.Resume));
        Assert.Null(await firstStore.DecideRebootCheckpointAsync(
            second.Plan.JobId, issued.Checkpoint.Id, issued.Token, RebootDisposition.Resume));

        await using var secondStore = await OpenAsync(workspace, clock);
        var decisions = await Task.WhenAll(
            Task.Run(() => firstStore.DecideRebootCheckpointAsync(
                first.Plan.JobId, issued.Checkpoint.Id, issued.Token, RebootDisposition.Resume)),
            Task.Run(() => secondStore.DecideRebootCheckpointAsync(
                first.Plan.JobId, issued.Checkpoint.Id, issued.Token, RebootDisposition.Resume)));
        Assert.Equal(issued.Checkpoint.Id, Assert.Single(decisions.OfType<RebootCheckpoint>()).Id);
        Assert.Null(await firstStore.DecideRebootCheckpointAsync(
            first.Plan.JobId, issued.Checkpoint.Id, issued.Token, RebootDisposition.Resume));
        Assert.Single(await firstStore.ListPendingRebootCheckpointsAsync(second.Plan.JobId));
        await AssertAppliedAsync(firstStore, first);
        await AssertAppliedAsync(firstStore, second);
        await CheckBoundMetadataTamperAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Extended")]
    public async Task InterruptedDecisionCommitPreservesPendingOrRecordedAbandonment(bool committed)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        JournalEntry applied;
        RebootResumeCandidate issued;
        await using (var store = await OpenAsync(workspace, clock))
        {
            applied = await SaveRestartRequiredAsync(store, "decision-commit");
            issued = Assert.IsType<RebootResumeCandidate>(await store.IssueRebootCheckpointAsync(applied));
        }

        var point = committed ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit;
        var jobId = applied.Plan.JobId;
        var id = issued.Checkpoint.Id;
        var expected = new RebootRecoveryDecision(issued.Checkpoint, RebootDisposition.Abandon, IssueTime);
        await using (var interrupted = await OpenAsync(workspace, clock, new ThrowingFaultInjector(point)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() =>
                interrupted.DecideRebootCheckpointAsync(jobId, id, issued.Token, RebootDisposition.Abandon));
        }

        await using (var reopened = await OpenAsync(workspace, clock))
        {
            var pending = await reopened.LoadRebootCheckpointAsync(jobId, id);
            Assert.Equal(!committed, pending is not null);
            Assert.Equal(committed ? expected : null, await reopened.LoadLatestRebootDecisionAsync(jobId));
            var retried = await reopened.DecideRebootCheckpointAsync(jobId, id, issued.Token, RebootDisposition.Abandon);
            Assert.Equal(!committed, retried is not null);
            Assert.Empty(await reopened.ListPendingRebootCheckpointsAsync(jobId));
        }

        await using var recovered = await OpenAsync(workspace, clock);
        Assert.Null(await recovered.DecideRebootCheckpointAsync(jobId, id, issued.Token, RebootDisposition.Resume));
        Assert.Equal(expected, await recovered.LoadLatestRebootDecisionAsync(jobId));
        await AssertAppliedAsync(recovered, applied);
    }

    private static async Task CheckBoundMetadataTamperAsync()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        RebootResumeCandidate issued;
        JournalEntry applied;
        string databasePath;
        await using (var store = await OpenAsync(workspace, clock))
        {
            applied = await SaveRestartRequiredAsync(store, "rewritten-lifetime");
            issued = Assert.IsType<RebootResumeCandidate>(await store.IssueRebootCheckpointAsync(applied));
            databasePath = store.DatabasePath;
        }

        var original = issued.Checkpoint;
        var rewritten = new RebootCheckpoint(original.Id, original.Plan, original.ActionId,
            original.JournalEntryId, original.ApprovalId, original.CorrelationId,
            original.IssuedAtUtc.AddMinutes(1), original.ExpiresAtUtc.AddMinutes(1));
        var payload = JsonSerializer.SerializeToUtf8Bytes(rewritten, DomainJson.CreateStrictOptions());
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        // Change the public timestamps and ordinary checksum in a disposable fixture while retaining its native authenticator.
        await SqliteTestControl.ExecuteAsync(databasePath,
            $"UPDATE reboot_checkpoints SET payload_json = CAST(X'{Convert.ToHexString(payload)}' AS TEXT), payload_sha256 = '{hash}';");
        clock.Advance(original.ExpiresAtUtc.AddSeconds(30) - clock.GetUtcNow());
        Assert.False(original.IsCurrent(clock.GetUtcNow()));
        Assert.True(rewritten.IsCurrent(clock.GetUtcNow()));
        Assert.True(rewritten.MatchesRestart(applied));

        await using var reopened = await OpenAsync(workspace, clock);
        var jobId = original.Plan.JobId;
        Assert.Null(await reopened.LoadRebootCheckpointAsync(jobId, original.Id));
        Assert.Null(await reopened.DecideRebootCheckpointAsync(jobId, original.Id, issued.Token, RebootDisposition.Resume));
        Assert.Null(await reopened.LoadLatestRebootDecisionAsync(jobId));
        await Assert.ThrowsAsync<JobStoreCorruptionException>(() => reopened.ListPendingRebootCheckpointsAsync(jobId));
        await AssertAppliedAsync(reopened, applied);
    }

    private static async Task AssertAppliedAsync(SqliteJobStore store, JournalEntry applied)
    {
        var history = await store.LoadJournalAsync(applied.Plan);
        Assert.NotNull(history);
        Assert.Equal(JournalState.Applied, history.StateOf(applied.ActionId));
        Assert.Equal(applied, history.Entries[^1]);
        Assert.Equal(3, history.Entries.Length);
    }

    private static Task<JournalEntry> SaveRestartRequiredAsync(SqliteJobStore store, string suffix) =>
        SaveAppliedAsync(store, suffix, needsRestart: true);

    private static async Task<JournalEntry> SaveAppliedAsync(SqliteJobStore store, string suffix, bool needsRestart)
    {
        var job = PersistenceTestData.CreateJob("reboot-" + suffix);
        var intake = new SanitizationEvidence(
            PersistenceTestData.CreateMetadata("evidence-reboot-intake-" + suffix, EvidenceClassification.Observed,
                ProvenanceKind.ProviderObservation, ValueAvailability.Available),
            SanitizationState.Verified, "external_sanitization", Approver, PersistenceTestData.Timestamp,
            new MediaIdentifier("FIXTURE-REBOOT-MEDIA"), SanitizationVerificationState.Verified, "community-policy@1.0.0");
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [intake]));
        await store.RecordSanitizationGateDecisionAsync(
            SanitizationGate.Evaluate(job.JobId, intake, PersistenceTestData.Timestamp.AddMinutes(2)));
        var stored = await store.LoadJobAsync(job.JobId);
        Assert.NotNull(stored);
        var catalog = CreateReviewedCatalog();
        var profile = CreateReviewedProfile(catalog);
        Observation[] facts =
        [
            new(intake.Metadata, new EvidenceKey("sanitization.state"), EvidenceValue.FromEnum("verified"), null, null),
            Observed("system.architecture", "x64", "architecture"),
            Observed("operating_system.release", "windows_11", "release"),
            Observed("operating_system.support_state", "supported", "support"),
        ];
        var proposal = PlanResolver.Resolve(stored, Policy, profile, catalog, facts, [], IssueTime);
        Assert.Null(proposal.BlockedReason);
        var plan = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
        var action = Assert.Single(plan.Actions);
        Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalApprovalAsync(
            new JournalApproval(ApprovalId, plan.Key, Approver, CorrelationId, IssueTime.AddMinutes(-4))));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(
            Entry(plan.Key, action.ActionId, ApprovedId, JournalState.Planned, JournalState.Approved,
                JournalActor.Operator, JournalResult.None, IssueTime.AddMinutes(-3))));
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(
            Entry(plan.Key, action.ActionId, StartedId, JournalState.Approved, JournalState.Started,
                JournalActor.Coordinator, JournalResult.None, IssueTime.AddMinutes(-2), JournalCheckpoint.DispatchIntent)));
        var applied = Entry(plan.Key, action.ActionId, AppliedId, JournalState.Started, JournalState.Applied,
            JournalActor.Broker, JournalResult.BackendApplied, IssueTime.AddMinutes(-1),
            needsRestart ? JournalCheckpoint.RestartRequired : JournalCheckpoint.None);
        Assert.Equal(JournalWriteResult.Recorded, await store.AppendJournalEntryAsync(applied));
        return applied;
    }

    private static JournalEntry Entry(
        JournalPlanKey plan, Guid action, Guid id, JournalState prior, JournalState next, JournalActor actor,
        JournalResult result, DateTimeOffset time, JournalCheckpoint checkpoint = JournalCheckpoint.None,
        ActionVerification? verification = null) =>
        new(id, plan, action, ApprovalId, prior, next, actor,
            actor == JournalActor.Operator ? Approver : new OperatorId("actor-" + actor.ToString().ToLowerInvariant()),
            CorrelationId, time, result, checkpoint, verification);

    private static CatalogDocument CreateReviewedCatalog()
    {
        var json = JsonNode.Parse(ReadFixture("catalog"))!;
        json["catalog_id"] = "reviewed-reboot-fixture";
        json["status"] = "reviewed";
        json["synthetic_data"] = false;
        json["version"] = "1.0.0";
        var entry = JsonNode.Parse("""
            {
              "id":"fictional.web-browser","version":"1.0.0","capability_id":"web_browsing",
              "entry_status":"reviewed","production_eligible":true,
              "package":{
                "source_id":"test-repository","package_id":"Fictional.Tests.WebBrowser","exact_version":"2.15.236.8",
                "scope":"machine","architectures":["x64"],"minimum_os":"windows_11","external_artifact":true
              },
              "review":{
                "publisher":"Fictional Publisher","declared_license":"MIT","license_review_status":"approved",
                "privacy_review_status":"approved","license_reviewed_at_utc":"2029-12-01T00:00:00Z",
                "privacy_reviewed_at_utc":"2029-12-01T00:00:00Z","installation_status":"allowed",
                "redistribution_status":"withheld","valid_until_utc":"2031-01-01T00:00:00Z"
              },
              "behavior":{
                "reboot":"required","background_service":"unknown","auto_update":"unknown","network_use":"required",
                "download_bytes":1024,"installed_bytes":4096
              },
              "verification":{
                "status":"reviewed","type":"exact_package_identity","expected_source_id":"test-repository",
                "expected_package_id":"Fictional.Tests.WebBrowser","expected_version":"2.15.236.8"
              }
            }
            """)!;
        json["applications"] = new JsonArray(entry);
        return CatalogSchema.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()), IssueTime);
    }

    private static ProfileDocument CreateReviewedProfile(CatalogDocument catalog)
    {
        var json = JsonNode.Parse(ReadFixture("basic"))!;
        json["status"] = "reviewed";
        json["synthetic_data"] = false;
        json["policy"]!["id"] = Policy.PolicyId;
        json["policy"]!["version"] = Policy.Version;
        json["catalog"]!["id"] = catalog.CatalogId;
        json["catalog"]!["version"] = catalog.Version;
        var workshop = json["workshop"]!;
        workshop["capabilities"] = new JsonArray(workshop["capabilities"]![0]!.DeepClone());
        return ProfileSchema.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()), catalog);
    }

    private static Observation Observed(string key, string token, string suffix) =>
        new(PersistenceTestData.CreateMetadata("evidence-reboot-" + suffix, EvidenceClassification.Observed,
                ProvenanceKind.ProviderObservation, ValueAvailability.Available),
            new EvidenceKey(key), EvidenceValue.FromEnum(token), null, null);

    private static Task<SqliteJobStore> OpenAsync(
        PersistenceTestWorkspace workspace, FixedTimeProvider clock, IJobStoreFaultInjector? fault = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion, clock, fault);

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name + ".json"));
}
