using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Versioning;
using ThirdLife.Verification;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    [Theory]
    [InlineData("clear")]
    [InlineData("managed_attention")]
    public async Task CompleteReviewSurvivesReopenAndRetainsSignOffAfterMaterialChange(string ownership)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        var job = PersistenceTestData.CreateJob("handover-complete");
        var verifier = new HandoverFixtureFiles();
        uint boot = 10;
        ColdBootObservation? observation = null;
        HandoverSignOff signed;
        await using (var store = await Open())
        {
            var policy = HandoverFixturePolicy(ownership);
            var intake = new SanitizationEvidence(PersistenceTestData.CreateMetadata("evidence-handover-intake",
                EvidenceClassification.HumanConfirmed, ProvenanceKind.HumanConfirmation, ValueAvailability.Available),
                SanitizationState.Verified, "external_sanitization", Approver, PersistenceTestData.Timestamp,
                new MediaIdentifier("FIXTURE-HANDOVER-MEDIA"), SanitizationVerificationState.Verified, SanitizationGate.WorkshopIntakePolicyVersion);
            var facts = new[]
            {
                Observed("system.architecture", "x64", "handover-architecture"),
                Observed("operating_system.release", "windows_11", "handover-release"),
                Observed("operating_system.support_state", "supported", "handover-support"),
                Observed("ownership.lock_state", ownership, "handover-ownership"),
                new Observation(intake.Metadata, new("sanitization.state"), EvidenceValue.FromEnum("verified"), null, null, Approver),
            };
            await store.CreateJobAsync(job);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, observations: facts[..^1], sanitizationEvidence: [intake]));
            await store.RecordSanitizationGateDecisionAsync(SanitizationGate.Evaluate(job.JobId, intake, clock.GetUtcNow()));
            var evaluated = DispositionEngine.Evaluate(policy.Document, facts, []);
            Assert.Equal(Disposition.ReadyToPrepare, evaluated.Disposition);
            await store.AppendPolicyDecisionAsync(policy, PolicyDecisionRecord.Create(policy, job.JobId, "assessment-handover-fixture",
                evaluated, facts.Select(fact => fact.Metadata.EvidenceId), clock.GetUtcNow(), jobEvidenceCount: facts.Length));

            var stored = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId));
            var catalog = CreateReviewedCatalog();
            var profile = CreateReviewedProfile(catalog, policy.Document);
            var proposal = PlanResolver.Resolve(stored, policy.Document, profile, catalog, facts, [], clock.GetUtcNow());
            Assert.Null(proposal.BlockedReason);
            proposal = proposal.WithSchedule(proposal.Actions.Select(action =>
                new ScheduledAction(action.Request.ActionId, MaintenancePhases.PackagePhase, [])), fullMaintenance: true);
            var plan = JournalPlanSnapshot.Create(proposal, ImpactEstimator.Estimate(proposal));
            Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
            var (checkpoint, _) = await ReachColdBootBoundaryAsync(store, clock, "handover-complete", () => boot++, plan);
            var request = await store.RecordColdBootCheckpointAsync(checkpoint);
            boot++;
            clock.Advance(TimeSpan.FromMinutes(1));
            observation = new ColdBootObservation(boot, clock.GetUtcNow(), ColdBootKind.OrderlyPowerOff);
            var measured = await CreateColdBootProfileRecordAsync(store, clock, checkpoint, plan, request.Id);
            var raw = measured.Result;
            var verified = new ProfileVerificationResult(raw.StartedAtUtc, raw.FinishedAtUtc, false, raw.ProfileSha256,
                policy.Document.ContentSha256, raw.Checks, raw.Capabilities.Select(capability =>
                    capability with { LaunchProbe = ProfileVerificationState.Passed }).ToImmutableArray(),
                [new(new("POL-OWNERSHIP-001"), PolicyRuleState.Met, PolicyRuleReason.ConditionMet, true)],
                raw.WindowsBuild, raw.FreeBytes, raw.VolumeCapacityBytes, raw.UpdateOfferCount, raw.EligibleUpdateOfferCount,
                WindowsUpdateDisposition.Current, false);
            await store.SaveProfileVerificationAsync(measured with { Result = verified });
            Assert.True((await store.LoadProfileVerificationAsync(job.JobId))?.HasPassedPostBootVerification);

            var mode = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            await store.SaveHandoverModeAsync(job.JobId, mode, HandoverMode.Sealed, Approver);
            foreach (var rule in FinalizationChecklist.Rules.Where(rule => rule.AllowsOperatorConfirmation))
            {
                var checklist = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
                await store.SaveFinalizationConfirmationAsync(job.JobId, checklist, rule.Id, FinalizationOutcome.Passed, null, Approver);
            }

            var before = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId, verifier));
            if (ownership != "clear")
            {
                Assert.Contains(new HandoverBlocker(HandoverRequirement.Ownership, HandoverBlockReason.EvidenceUnavailable), before.Blockers);
                await Assert.ThrowsAsync<JobStoreConflictException>(() => store.CompleteHandoverAsync(before, Approver, verifier));
                return;
            }
            Assert.Equal(new[] { HandoverRequirement.WorkshopRecord, HandoverRequirement.RecipientGuide,
                HandoverRequirement.SupportBundleDecision }, before.Blockers.Select(blocker => blocker.Requirement));
            var workshop = new HandoverFileReceipt(new(HandoverOutputKind.WorkshopRecord, before.Scope, "report-v1", new string('1', 64)), "workshop-fixture.txt", 20);
            var guide = new HandoverFileReceipt(new(HandoverOutputKind.RecipientGuide, before.Scope, "guide-v1", new string('2', 64)), "guide-fixture.html", 30);
            verifier.Current = [workshop, guide];
            await store.SaveHandoverFileAsync(workshop, verifier);
            await store.SaveHandoverFileAsync(guide, verifier);
            await store.SaveHandoverSupportOmissionAsync(before.Scope, Approver, SupportBundleOmissionReason.NoSupportRequest);
            var ready = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId, verifier));
            Assert.Empty(ready.Blockers);
            verifier.Current = [workshop];
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.CompleteHandoverAsync(ready, Approver, verifier));
            verifier.Current = [workshop, guide];
            signed = await store.CompleteHandoverAsync(ready, Approver, verifier);
            Assert.Equal(1, signed.Sequence);
            Assert.Equal(signed.Sequence, (await store.CompleteHandoverAsync(ready, Approver, verifier)).Sequence);
        }

        await using var reopened = await Open();
        var current = Assert.IsType<SavedHandoverCompletion>(await reopened.ReadHandoverCompletionAsync(job.JobId, verifier));
        Assert.True(current.IsCurrent);
        Assert.Equal(signed.OperatorId, current.Latest.OperatorId);
        Assert.Equal(signed.Evidence.AsEnumerable(), current.Latest.Evidence);
        boot++;
        var stale = Assert.IsType<SavedHandoverCompletion>(await reopened.ReadHandoverCompletionAsync(job.JobId, verifier));
        Assert.False(stale.IsCurrent);
        Assert.Equal(signed.Scope, stale.Latest.Scope);

        Task<SqliteJobStore> Open() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => boot, (_, _) => observation);
    }

    private static PolicySnapshot HandoverFixturePolicy(string expectedOwnership)
    {
        var candidate = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy.json")))!.AsObject();
        candidate["status"] = "effective";
        candidate["synthetic_data"] = false;
        candidate["claim_scope"] = "handover_contract_fixture";
        candidate["effective"]!["status"] = "effective";
        candidate["effective"]!["effective_from_utc"] = IssueTime.AddHours(-1).ToString("O", CultureInfo.InvariantCulture).Replace("+00:00", "Z", StringComparison.Ordinal);
        var rule = candidate["rules"]!.AsArray().Single(rule => (string?)rule!["id"] == "POL-OWNERSHIP-001")!.DeepClone();
        rule["condition"]!["value"] = expectedOwnership;
        candidate["rules"] = new JsonArray(rule);
        return PolicySnapshot.Parse(Encoding.UTF8.GetBytes(candidate.ToJsonString()));
    }

    private sealed class HandoverFixtureFiles : IHandoverFileVerifier
    {
        public HandoverFileReceipt[] Current { get; set; } = [];

        public bool MatchesPublishedFile(HandoverFileReceipt receipt, CancellationToken token) => Current.Contains(receipt);
    }
}
