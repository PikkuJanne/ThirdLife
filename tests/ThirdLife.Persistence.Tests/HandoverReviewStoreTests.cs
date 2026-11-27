using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Support;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class HandoverReviewStoreTests
{
    [Fact]
    public async Task ReopenedReviewBindsChecklistModeAndBootRevisions()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var job = PersistenceTestData.CreateJob();
        var actor = new OperatorId("operator-review-test");
        uint boot = 10;
        HandoverReviewScope priorScope;
        await using (var store = await Open())
        {
            await store.CreateJobAsync(job);
            var initial = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId));
            var checklist = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
            Assert.Contains(new HandoverBlocker(HandoverRequirement.Finalization,
                HandoverBlockReason.FinalizationIncomplete), initial.Blockers);
            await store.SaveFinalizationConfirmationAsync(job.JobId, checklist, FinalizationRuleId.WorkshopAccounts,
                FinalizationOutcome.Passed, null, actor);
            var confirmed = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId));
            Assert.NotEqual(initial.Scope, confirmed.Scope);
            Assert.Equal(checklist.EvidenceVersion, (await store.LoadFinalizationAsync(job.JobId))?.EvidenceVersion);

            var handover = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            await store.SaveHandoverModeAsync(job.JobId, handover, HandoverMode.Sealed, actor);
            var selected = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId));
            Assert.NotEqual(confirmed.Scope, selected.Scope);
            Assert.DoesNotContain(selected.Blockers, blocker => blocker.Requirement == HandoverRequirement.RecipientHandoverMode);
            Assert.Contains(selected.Blockers, blocker => blocker.Requirement == HandoverRequirement.FinalProfileVerification);
            priorScope = selected.Scope;
        }

        await using var reopened = await Open();
        Assert.Equal(priorScope, (await reopened.LoadHandoverReviewAsync(job.JobId))?.Scope);
        boot++;
        var afterBoot = Assert.IsType<HandoverEligibility>(await reopened.LoadHandoverReviewAsync(job.JobId));
        Assert.NotEqual(priorScope, afterBoot.Scope);
        Assert.False(afterBoot.CanRequestSignOff);
        Assert.Equal(1, (await reopened.LoadFinalizationAsync(job.JobId))?.History.Count);

        Task<SqliteJobStore> Open() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => boot);
    }

    [Fact]
    public async Task MissingArchivedAndUnverifiedJobsStayExplicit()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => 9);
        var job = PersistenceTestData.CreateJob();
        Assert.Null(await store.LoadHandoverReviewAsync(job.JobId));
        await store.CreateJobAsync(job);
        var open = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId));
        Assert.False(open.CanRequestSignOff);
        Assert.Contains(new HandoverBlocker(HandoverRequirement.Ownership, HandoverBlockReason.EvidenceMissing), open.Blockers);
        Assert.Contains(new HandoverBlocker(HandoverRequirement.ColdBootVerification, HandoverBlockReason.EvidenceMissing), open.Blockers);
        await store.SetArchiveStateAsync(job.JobId, true, clock.GetUtcNow());
        var archived = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId));
        Assert.Contains(new HandoverBlocker(HandoverRequirement.OpenJob, HandoverBlockReason.JobArchived), archived.Blockers);
    }
    [Fact]
    public async Task MigratedReviewKeepsExactFilesAndOmissionButCannotCompleteBlockedJob()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var job = PersistenceTestData.CreateJob();
        var actor = new OperatorId("operator-signoff-test");
        var verifier = new OutputVerifier();
        var initialDigest = new string('1', 64);
        HandoverFileReceipt report;
        HandoverReviewScope older;
        await using (var previous = await Open(18))
        {
            await previous.CreateJobAsync(job);
            var review = Assert.IsType<HandoverEligibility>(await previous.LoadHandoverReviewAsync(job.JobId, verifier));
            older = review.Scope;
            await Assert.ThrowsAsync<JobStoreConflictException>(() => previous.SaveHandoverSupportOmissionAsync(older, actor,
                SupportBundleOmissionReason.NoSupportRequest));
        }
        await using (var migrated = await Open())
        {
            var review = Assert.IsType<HandoverEligibility>(await migrated.LoadHandoverReviewAsync(job.JobId, verifier));
            Assert.Equal(older, review.Scope);
            report = new(new(HandoverOutputKind.WorkshopRecord, older, "workshop-report-1", initialDigest), "workshop-review.txt", 20);
            verifier.Accepted = [report];
            await migrated.SaveHandoverFileAsync(report, verifier);
            await migrated.SaveHandoverSupportOmissionAsync(older, actor, SupportBundleOmissionReason.PrivacyChoice);
            var readyInputs = Assert.IsType<HandoverEligibility>(await migrated.LoadHandoverReviewAsync(job.JobId, verifier));
            Assert.Contains(readyInputs.Evidence, entry => entry.Output == report.Output);
            var choice = Assert.Single(readyInputs.Evidence, entry => entry.Requirement == HandoverRequirement.SupportBundleDecision);
            Assert.Equal(actor, choice.SupportBundleOmission?.OperatorId);
            Assert.Equal(SupportBundleOmissionReason.PrivacyChoice, choice.SupportBundleOmission?.Reason);
            Assert.False(readyInputs.CanRequestSignOff);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.CompleteHandoverAsync(readyInputs, actor, verifier));
        }
        await using var reopened = await Open();
        Assert.Null(await reopened.ReadHandoverCompletionAsync(job.JobId, verifier));
        var reopenedReview = Assert.IsType<HandoverEligibility>(await reopened.LoadHandoverReviewAsync(job.JobId, verifier));
        Assert.Contains(reopenedReview.Evidence, entry => entry.Output == report.Output);
        verifier.Accepted = [];
        var unavailable = Assert.IsType<HandoverEligibility>(await reopened.LoadHandoverReviewAsync(job.JobId, verifier));
        Assert.Contains(new HandoverBlocker(HandoverRequirement.WorkshopRecord, HandoverBlockReason.EvidenceUnavailable), unavailable.Blockers);
        var handover = Assert.IsType<SavedRecipientHandover>(await reopened.LoadRecipientHandoverAsync(job.JobId));
        await reopened.SaveHandoverModeAsync(job.JobId, handover, HandoverMode.Sealed, actor);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.SaveHandoverFileAsync(report, verifier));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.SaveHandoverSupportOmissionAsync(older, actor,
            SupportBundleOmissionReason.PrivacyChoice));
        var updated = Assert.IsType<HandoverEligibility>(await reopened.LoadHandoverReviewAsync(job.JobId, verifier));
        Assert.Contains(new HandoverBlocker(HandoverRequirement.WorkshopRecord, HandoverBlockReason.EvidenceStale), updated.Blockers);

        Task<SqliteJobStore> Open(int? schema = null) => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            schema ?? SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => 10);
    }

    [Fact]
    public async Task ArchivedOutputReviewRequiresTheExactCheckpointAndKeepsTheJobPartition()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var verifier = new OutputVerifier();
        var actor = new OperatorId("operator-archive-test");
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => 10);

        var job = PersistenceTestData.CreateJob();
        var other = Job.Create(DeviceId.New(), clock.GetUtcNow());
        await store.CreateJobAsync(job);
        await store.CreateJobAsync(other);
        var review = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId));
        var outside = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(other.JobId));
        var report = new HandoverFileReceipt(new(HandoverOutputKind.WorkshopRecord, review.Scope, "report-1", new string('1', 64)),
            "workshop-report.txt", 20);
        var guide = new HandoverFileReceipt(new(HandoverOutputKind.RecipientGuide, review.Scope, "guide-1", new string('2', 64)),
            "recipient-guide.html", 30);
        var otherReport = new HandoverFileReceipt(new(HandoverOutputKind.WorkshopRecord, outside.Scope, "report-2", new string('3', 64)),
            "workshop-other.txt", 40);
        verifier.Accepted = [report, guide, otherReport];
        await store.SaveHandoverFileAsync(report, verifier);
        await store.SaveHandoverFileAsync(guide, verifier);
        await store.SaveHandoverFileAsync(otherReport, verifier);
        await store.SaveHandoverSupportOmissionAsync(review.Scope, actor, SupportBundleOmissionReason.NoSupportRequest);

        var active = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId));
        Assert.Null(await store.ReadArchivedHandoverFilesAsync(job.JobId, active.Checkpoints[^1].CheckpointId));
        await store.SetArchiveStateAsync(job.JobId, true, clock.GetUtcNow());
        var archived = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId));
        var checkpoint = archived.Checkpoints[^1].CheckpointId;
        Assert.Equal([report, guide], await store.ReadArchivedHandoverFilesAsync(job.JobId, checkpoint));
        Assert.Null(await store.ReadArchivedHandoverFilesAsync(other.JobId, checkpoint));

        await store.SetArchiveStateAsync(job.JobId, false, clock.GetUtcNow());
        await store.SetArchiveStateAsync(job.JobId, true, clock.GetUtcNow());
        var rearchived = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId));
        Assert.Equal(archived.ArchivedAtUtc, rearchived.ArchivedAtUtc);
        Assert.Null(await store.ReadArchivedHandoverFilesAsync(job.JobId, checkpoint));
        Assert.Equal([report, guide], await store.ReadArchivedHandoverFilesAsync(job.JobId, rearchived.Checkpoints[^1].CheckpointId));
    }

    [Fact]
    public async Task OnlyExactScopeBoundSupportPublicationSatisfiesTheSupportRequirement()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var verifier = new OutputVerifier();
        var actor = new OperatorId("operator-support-test");
        var job = PersistenceTestData.CreateJob();
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => 10);
        await store.CreateJobAsync(job);
        var current = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId, verifier));
        var digest = new string('2', 64);
        var id = $"SUP-{Guid.NewGuid():N}";
        await store.SaveHandoverSupportOmissionAsync(current.Scope, actor, SupportBundleOmissionReason.NoSupportRequest);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.StageSupportExportAsync(job.JobId,
            new(id, "1", "1", digest, clock.GetUtcNow(), actor, SupportExportState.Pending, "older-review")));
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.StageSupportExportAsync(job.JobId,
            new(id, "1", "1", digest, clock.GetUtcNow(), actor, SupportExportState.Pending, current.Scope.Version));
        var pending = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId, verifier));
        Assert.Contains(new HandoverBlocker(HandoverRequirement.SupportBundleDecision, HandoverBlockReason.EvidenceUnavailable), pending.Blockers);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.RecordSupportExportAsync(job.JobId,
            new(id, "1", "1", digest, clock.GetUtcNow(), actor, SupportExportState.Published)));
        await store.RecordSupportExportAsync(job.JobId,
            new(id, "1", "1", digest, clock.GetUtcNow(), actor, SupportExportState.Published, current.Scope.Version));
        var published = Assert.IsType<HandoverEligibility>(await store.LoadHandoverReviewAsync(job.JobId, verifier));
        var proof = Assert.Single(published.Evidence, entry => entry.Requirement == HandoverRequirement.SupportBundleDecision);
        Assert.Equal(digest, proof.Output?.Sha256);
        Assert.Null(proof.SupportBundleOmission);
        Assert.False(published.CanRequestSignOff);
    }

    private sealed class OutputVerifier : IHandoverFileVerifier
    {
        public HandoverFileReceipt[] Accepted { get; set; } = [];

        public bool MatchesPublishedFile(HandoverFileReceipt receipt, CancellationToken token) => Accepted.Contains(receipt);
    }
}
