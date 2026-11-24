using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Repairs;
using ThirdLife.Core.Sanitization;
using FixedTimeProvider = ThirdLife.Core.Tests.JobLifecycleTests.FixedTimeProvider;
using InMemoryJobStore = ThirdLife.Core.Tests.JobLifecycleTests.InMemoryJobStore;

namespace ThirdLife.Core.Tests;

public sealed class RepairRetestTests
{
    private const string PriorAssessmentId = "assessment-11111111111111111111111111111111";
    private const string RetestAssessmentId = "assessment-22222222222222222222222222222222";
    private static readonly DateTimeOffset StartedAtUtc = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RepairedAtUtc = StartedAtUtc.AddMinutes(20);
    private static readonly JobId Job = new("synthetic-repair-job");
    private static readonly OperatorId Operator = new("synthetic-workshop-operator");

    [Fact]
    public async Task StorageReplacementInvalidatesTheOldGateAndLinksNewEvidenceWithoutErasingTheFailure()
    {
        await using var store = new RecordingStore();
        var failedStorage = Observation("synthetic-storage-failed", "storage.system.health_state",
            EvidenceValue.FromEnum("unhealthy"), StartedAtUtc.AddMinutes(5));
        var oldGate = await SeedAsync(store, failedStorage);
        var clock = new FixedTimeProvider(RepairedAtUtc);
        var repairs = new RepairRetestService(store, clock);

        var before = await LoadAsync(store);
        var repair = await repairs.RecordAsync(
            Job, PriorAssessmentId, failedStorage.Metadata.EvidenceId, RepairComponent.SystemStorage, RepairAction.Replaced, Operator);
        var replaced = await LoadAsync(store);

        Assert.Equal(Job, repair.JobId);
        Assert.Equal(PriorAssessmentId, repair.PriorAssessmentId);
        Assert.Equal(failedStorage.Metadata.EvidenceId, repair.FailedEvidenceId);
        Assert.Equal(failedStorage.EvidenceKey, repair.EvidenceKey);
        Assert.Equal(RepairComponent.SystemStorage, repair.Component);
        Assert.Equal(RepairAction.Replaced, repair.Action);
        Assert.Equal(Operator, repair.OperatorId);
        Assert.Equal(RepairedAtUtc, repair.RecordedAtUtc);
        Assert.Equal(oldGate.DecisionId, repair.GateDecisionIdAtRecording);
        Assert.Empty(RepairJournal.GetRepairs(before));
        Assert.Equal(repair, Assert.Single(RepairJournal.GetRepairs(replaced)));
        Assert.Contains(failedStorage, replaced.Observations);
        var batch = Assert.IsType<JobEvidenceBatch>(store.LastBatch);
        Assert.True(batch.RequireUnarchivedJob);
        Assert.Equal(repair.RepairId, Assert.Single(batch.Observations).Metadata.EvidenceId);
        var invalidation = Assert.Single(batch.SanitizationEvidence);
        Assert.Equal(repair.SanitizationInvalidationEvidenceId, invalidation.Metadata.EvidenceId);
        Assert.Equal(SanitizationState.Unknown, invalidation.SanitizationState);
        Assert.Equal(RepairedAtUtc, invalidation.Metadata.CollectedAtUtc);
        Assert.Null(invalidation.OperatorId);
        Assert.Equal(invalidation, replaced.SanitizationEvidence[^1]);
        Assert.False(SanitizationGate.Inspect(replaced).AllowsAssessment);
        Assert.False(RepairJournal.CanRetest(replaced, repair.RepairId));
        Assert.Throws<JobLifecycleException>(() => RepairJournal.CreateRetestMarker(
            replaced, repair.RepairId, RetestAssessmentId, oldGate.DecisionId, RepairedAtUtc));

        var replacementEvidence = Sanitization("replacement", SanitizationState.ReplacementStorage, RepairedAtUtc.AddMinutes(5));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job, sanitizationEvidence: [replacementEvidence]));
        Assert.False(RepairJournal.CanRetest(await LoadAsync(store), repair.RepairId));
        clock.Advance(TimeSpan.FromMinutes(6));
        var replacementGate = await new JobService(store, clock).CompleteIntakeAsync(Job, replacementEvidence.Metadata.EvidenceId);
        var withFreshGate = await LoadAsync(store);

        Assert.Equal(SanitizationGateReason.ReplacementStorageVerified, replacementGate.Reason);
        Assert.True(RepairJournal.CanRetest(withFreshGate, repair.RepairId));
        Assert.Throws<JobLifecycleException>(() => RepairJournal.CreateRetestMarker(
            withFreshGate, repair.RepairId, RetestAssessmentId, oldGate.DecisionId, clock.GetUtcNow()));
        var marker = RepairJournal.CreateRetestMarker(
            withFreshGate, repair.RepairId, RetestAssessmentId, replacementGate.DecisionId, clock.GetUtcNow());
        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            Job, observations: [marker], expectedSanitizationGateDecisionId: replacementGate.DecisionId));
        var healthyStorage = Observation("synthetic-storage-retest", "storage.system.health_state",
            EvidenceValue.FromEnum("healthy"), clock.GetUtcNow().AddMinutes(1));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            Job, observations: [healthyStorage], expectedSanitizationGateDecisionId: replacementGate.DecisionId));
        var retested = await LoadAsync(store);
        var link = Assert.Single(RepairJournal.GetRetests(retested));

        Assert.Equal(marker.Metadata.EvidenceId, link.RetestId);
        Assert.Equal(repair.RepairId, link.RepairId);
        Assert.Equal(failedStorage.Metadata.EvidenceId, link.PriorFailureEvidenceId);
        Assert.Equal(RetestAssessmentId, link.AssessmentId);
        Assert.Equal(replacementGate.DecisionId, link.GateDecisionId);
        Assert.Equal(clock.GetUtcNow(), link.StartedAtUtc);
        Assert.NotEqual(failedStorage.Metadata.EvidenceId, healthyStorage.Metadata.EvidenceId);
        var history = retested.Observations.Where(item => item.EvidenceKey == failedStorage.EvidenceKey).ToArray();
        Assert.Collection(history, observation => Assert.Equal(failedStorage, observation),
            observation => Assert.Equal(healthyStorage, observation));
        Assert.True(RepairJournal.CanRetest(retested, repair.RepairId));
        Assert.Throws<JobLifecycleException>(() => RepairJournal.CreateRetestMarker(
            retested, repair.RepairId, RetestAssessmentId, replacementGate.DecisionId, clock.GetUtcNow()));
        var retryMarker = RepairJournal.CreateRetestMarker(
            retested, repair.RepairId, "assessment-33333333333333333333333333333333", replacementGate.DecisionId,
            clock.GetUtcNow().AddMinutes(2));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            Job, observations: [retryMarker], expectedSanitizationGateDecisionId: replacementGate.DecisionId));
        var attempts = RepairJournal.GetRetests(await LoadAsync(store));
        Assert.Collection(attempts, prior => Assert.Equal(link, prior), retry =>
        {
            Assert.Equal(retryMarker.Metadata.EvidenceId, retry.RetestId);
            Assert.Equal(failedStorage.Metadata.EvidenceId, retry.PriorFailureEvidenceId);
        });
    }

    [Fact]
    public async Task NewlyVerifiedGeneralSanitizationDoesNotSubstituteForReplacementStorageVerification()
    {
        await using var store = new InMemoryJobStore();
        var failedStorage = Observation("synthetic-storage-failed", "storage.system.health_state",
            EvidenceValue.FromEnum("unhealthy"), StartedAtUtc.AddMinutes(5));
        await SeedAsync(store, failedStorage);
        var clock = new FixedTimeProvider(RepairedAtUtc);
        var repair = await new RepairRetestService(store, clock).RecordAsync(
            Job, PriorAssessmentId, failedStorage.Metadata.EvidenceId, RepairComponent.SystemStorage, RepairAction.Replaced, Operator);
        clock.Advance(TimeSpan.FromMinutes(5));
        var generalSanitization = Sanitization("new-verified", SanitizationState.Verified, clock.GetUtcNow());
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job, sanitizationEvidence: [generalSanitization]));
        var verified = await new JobService(store, clock).CompleteIntakeAsync(Job, generalSanitization.Metadata.EvidenceId);
        var saved = await LoadAsync(store);

        Assert.True(SanitizationGate.Inspect(saved).AllowsAssessment);
        Assert.False(RepairJournal.CanRetest(saved, repair.RepairId));
        Assert.Throws<JobLifecycleException>(() => RepairJournal.CreateRetestMarker(
            saved, repair.RepairId, RetestAssessmentId, verified.DecisionId, clock.GetUtcNow()));
    }

    [Fact]
    public async Task BatteryRepairUsesTheCurrentGateAndCannotReuseTheFailedAssessmentIdentity()
    {
        await using var store = new InMemoryJobStore();
        var failedBattery = Observation("synthetic-battery-failed", "battery.present",
            EvidenceValue.FromBoolean(false), StartedAtUtc.AddMinutes(5));
        var existingGate = await SeedAsync(store, failedBattery);
        var repair = await new RepairRetestService(store, new FixedTimeProvider(RepairedAtUtc)).RecordAsync(
            Job, PriorAssessmentId, failedBattery.Metadata.EvidenceId, RepairComponent.Battery, RepairAction.Repaired, Operator);
        var after = await LoadAsync(store);

        Assert.Null(repair.SanitizationInvalidationEvidenceId);
        Assert.Single(after.SanitizationEvidence);
        Assert.Equal(existingGate.DecisionId, SanitizationGate.Inspect(after).Decision!.DecisionId);
        Assert.True(RepairJournal.CanRetest(after, repair.RepairId));
        Assert.Throws<JobLifecycleException>(() => RepairJournal.CreateRetestMarker(
            after, repair.RepairId, PriorAssessmentId, existingGate.DecisionId, RepairedAtUtc));
        Assert.Throws<JobLifecycleException>(() => RepairJournal.CreateRetestMarker(
            after, repair.RepairId, RetestAssessmentId, existingGate.DecisionId, RepairedAtUtc.AddSeconds(-1)));
    }

    [Fact]
    public async Task MissingMisclassifiedAndArchivedRepairInputsAreRejectedBeforeWriting()
    {
        await using var store = new RecordingStore();
        var failedBattery = Observation("synthetic-battery-failed", "battery.present",
            EvidenceValue.FromBoolean(false), StartedAtUtc.AddMinutes(5));
        var gate = await SeedAsync(store, failedBattery);
        var healthyStorage = Observation("synthetic-storage-healthy", "storage.system.health_state",
            EvidenceValue.FromEnum("healthy"), StartedAtUtc.AddMinutes(5));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            Job, observations: [healthyStorage], expectedSanitizationGateDecisionId: gate.DecisionId));
        var originalBatch = store.LastBatch;
        var service = new RepairRetestService(store, new FixedTimeProvider(RepairedAtUtc));

        await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
            Job, PriorAssessmentId, new EvidenceId("synthetic-evidence-missing"), RepairComponent.Battery, RepairAction.Repaired, Operator));
        await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
            Job, PriorAssessmentId, failedBattery.Metadata.EvidenceId, RepairComponent.SystemStorage, RepairAction.Replaced, Operator));
        await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
            Job, PriorAssessmentId, healthyStorage.Metadata.EvidenceId, RepairComponent.SystemStorage, RepairAction.Replaced, Operator));
        await store.SetArchiveStateAsync(Job, isArchived: true, RepairedAtUtc);
        await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
            Job, PriorAssessmentId, failedBattery.Metadata.EvidenceId, RepairComponent.Battery, RepairAction.Repaired, Operator));
        Assert.Same(originalBatch, store.LastBatch);
        var saved = await LoadAsync(store);
        Assert.Empty(RepairJournal.GetRepairs(saved));
        Assert.Contains(failedBattery, saved.Observations);
    }

    private static async Task<SanitizationGateDecision> SeedAsync(IJobStore store, Observation priorFailure)
    {
        await store.CreateJobAsync(new Job(Job, new DeviceId("synthetic-repair-device"), StartedAtUtc));
        var sanitization = Sanitization("initial", SanitizationState.Verified, StartedAtUtc);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job, sanitizationEvidence: [sanitization]));
        var clock = new FixedTimeProvider(StartedAtUtc.AddMinutes(1));
        var gate = await new JobService(store, clock).CompleteIntakeAsync(Job, sanitization.Metadata.EvidenceId);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            Job, observations: [priorFailure], expectedSanitizationGateDecisionId: gate.DecisionId));
        return gate;
    }

    private static async Task<StoredJob> LoadAsync(IJobStore store) => Assert.IsType<StoredJob>(await store.LoadJobAsync(Job));

    private static Observation Observation(string id, string key, EvidenceValue value, DateTimeOffset timestamp) =>
        new(Metadata(id, timestamp), new EvidenceKey(key), value, unit: null, limitationCode: null);

    private static SanitizationEvidence Sanitization(string id, SanitizationState state, DateTimeOffset timestamp) =>
        new(Metadata("synthetic-sanitization-" + id, timestamp),
            state, "synthetic_external_verification", Operator, timestamp, new MediaIdentifier("SYNTHETIC-MEDIA-" + id),
            SanitizationVerificationState.Verified, "synthetic-policy@0.1.0");

    private static EvidenceMetadata Metadata(string id, DateTimeOffset timestamp) =>
        new(new EvidenceId(id), PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            new ProviderId("synthetic-repair-fixture"), timestamp,
            new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "synthetic-repair-fixture"), ValueAvailability.Available);

    private sealed class RecordingStore : IJobStore
    {
        private readonly InMemoryJobStore _store = new();

        public JobEvidenceBatch? LastBatch { get; private set; }

        public async Task AppendEvidenceAsync(JobEvidenceBatch batch, CancellationToken cancellationToken = default)
        {
            await _store.AppendEvidenceAsync(batch, cancellationToken);
            LastBatch = batch;
        }

        public Task CreateJobAsync(Job job, CancellationToken cancellationToken = default) =>
            _store.CreateJobAsync(job, cancellationToken);

        public Task SetArchiveStateAsync(JobId jobId, bool isArchived, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) =>
            _store.SetArchiveStateAsync(jobId, isArchived, changedAtUtc, cancellationToken);

        public Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(
            SanitizationGateDecision candidate, CancellationToken cancellationToken = default) =>
            _store.RecordSanitizationGateDecisionAsync(candidate, cancellationToken);

        public Task<StoredJob?> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default) =>
            _store.LoadJobAsync(jobId, cancellationToken);

        public ValueTask DisposeAsync() => _store.DisposeAsync();
    }
}
