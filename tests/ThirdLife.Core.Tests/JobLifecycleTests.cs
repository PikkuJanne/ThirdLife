using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Tests;

public sealed class JobLifecycleTests
{
    public static TheoryData<SanitizationState, SanitizationGateOutcome, SanitizationGateReason> GateStates => new()
    {
        {
            SanitizationState.Verified,
            SanitizationGateOutcome.AllowAssessment,
            SanitizationGateReason.SanitizationVerified
        },
        {
            SanitizationState.ReplacementStorage,
            SanitizationGateOutcome.AllowAssessment,
            SanitizationGateReason.ReplacementStorageVerified
        },
        {
            SanitizationState.NoDonorStorage,
            SanitizationGateOutcome.AllowAssessment,
            SanitizationGateReason.NoDonorStorageVerified
        },
        {
            SanitizationState.Unknown,
            SanitizationGateOutcome.Blocked,
            SanitizationGateReason.SanitizationUnknown
        },
        {
            SanitizationState.Failed,
            SanitizationGateOutcome.Blocked,
            SanitizationGateReason.SanitizationFailed
        },
    };

    [Theory]
    [MemberData(nameof(GateStates))]
    public void EverySanitizationStateMapsToOneFailClosedGateDecision(
        SanitizationState state,
        SanitizationGateOutcome expectedOutcome,
        SanitizationGateReason expectedReason)
    {
        var evidence = CreateEvidence(state, "current");

        var decision = SanitizationGate.Evaluate(
            Job,
            evidence,
            Timestamp.AddMinutes(2),
            new SanitizationGateDecisionId("gate-current"));

        Assert.Equal(expectedOutcome, decision.Outcome);
        Assert.Equal(expectedReason, decision.Reason);
        Assert.Equal(evidence.Metadata.EvidenceId, decision.EvidenceId);
        Assert.Equal(evidence.PolicyVersion, decision.PolicyVersion);
        Assert.Equal(Job, decision.JobId);
    }

    [Fact]
    public void GovernedSyntheticFixturePreservesStateOnlyGateMapping()
    {
        var evidence = CreateEvidence(SanitizationState.Verified, "fixture");

        var decision = SanitizationGate.Evaluate(Job, evidence, Timestamp.AddMinutes(2));

        Assert.Equal(SanitizationGateOutcome.AllowAssessment, decision.Outcome);
        Assert.Equal(SanitizationGateReason.SanitizationVerified, decision.Reason);
    }

    [Fact]
    public void AllPreviousToLatestTransitionsRequireANewDecision()
    {
        foreach (var previousState in Enum.GetValues<SanitizationState>())
        {
            foreach (var latestState in Enum.GetValues<SanitizationState>())
            {
                var previous = CreateEvidence(previousState, "previous");
                var latest = CreateEvidence(latestState, "latest");
                var previousDecision = SanitizationGate.Evaluate(
                    Job,
                    previous,
                    Timestamp.AddMinutes(2));
                var stale = Stored(
                    sanitization: [previous, latest],
                    decisions: [previousDecision]);

                var staleStatus = SanitizationGate.Inspect(stale);

                Assert.False(staleStatus.AllowsAssessment);
                Assert.Equal(SanitizationGateReason.GateDecisionStale, staleStatus.Reason);
                Assert.Equal(latest.Metadata.EvidenceId, staleStatus.EvidenceId);

                var latestDecision = SanitizationGate.Evaluate(
                    Job,
                    latest,
                    Timestamp.AddMinutes(3));
                var currentStatus = SanitizationGate.Inspect(Stored(
                    sanitization: [previous, latest],
                    decisions: [previousDecision, latestDecision]));

                Assert.Equal(latestDecision.Outcome, currentStatus.Outcome);
                Assert.Equal(latestDecision.Reason, currentStatus.Reason);
                Assert.Equal(latestDecision.Outcome == SanitizationGateOutcome.AllowAssessment, currentStatus.AllowsAssessment);
            }
        }
    }

    [Fact]
    public void MissingEvidenceDecisionAndArchivedJobHaveDistinctBlockers()
    {
        var evidence = CreateEvidence(SanitizationState.Verified, "current");
        var missingEvidence = SanitizationGate.Inspect(Stored());
        var missingDecision = SanitizationGate.Inspect(Stored(sanitization: [evidence]));
        var archived = SanitizationGate.Inspect(Stored(
            isArchived: true,
            sanitization: [evidence]));

        Assert.Equal(SanitizationGateReason.SanitizationEvidenceMissing, missingEvidence.Reason);
        Assert.Equal(SanitizationGateReason.GateDecisionMissing, missingDecision.Reason);
        Assert.Equal(SanitizationGateReason.JobArchived, archived.Reason);
        Assert.False(missingEvidence.AllowsAssessment);
        Assert.False(missingDecision.AllowsAssessment);
        Assert.False(archived.AllowsAssessment);
    }

    [Fact]
    public void GateStatusRequiresDecisionEvidenceReferenceToMatch()
    {
        var evidence = CreateEvidence(SanitizationState.Verified, "status-match");
        var decision = SanitizationGate.Evaluate(Job, evidence, Timestamp.AddMinutes(2));

        Assert.Throws<ArgumentException>(() => new SanitizationGateStatus(
            decision.Outcome,
            decision.Reason,
            evidenceId: null,
            decision));
        Assert.Throws<ArgumentException>(() => new SanitizationGateStatus(
            decision.Outcome,
            decision.Reason,
            new EvidenceId("evidence-sanitization-other"),
            decision));
    }

    [Fact]
    public async Task CreateUsesRandomInternalIdWithoutRecipientInput()
    {
        await using var store = new InMemoryJobStore();
        var service = new JobService(store, new FixedTimeProvider(Timestamp));
        var deviceId = new DeviceId("device-synthetic-recipient-label");

        var first = await service.CreateAsync(deviceId);
        var second = await service.CreateAsync(deviceId);

        Assert.StartsWith("job-", first.JobId.Value, StringComparison.Ordinal);
        Assert.NotEqual(first.JobId, second.JobId);
        Assert.DoesNotContain("recipient", first.JobId.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(deviceId, first.DeviceId);
        Assert.Equal(Timestamp, first.CreatedAtUtc);
    }

    [Theory]
    [MemberData(nameof(GateStates))]
    public async Task CompleteGatePersistsExactPolicyEvidenceAndOutcome(
        SanitizationState state,
        SanitizationGateOutcome expectedOutcome,
        SanitizationGateReason expectedReason)
    {
        await using var store = new InMemoryJobStore();
        var service = new JobService(store, new FixedTimeProvider(Timestamp.AddMinutes(4)));
        await store.CreateJobAsync(new Job(Job, Device, Timestamp));
        var evidence = CreateEvidence(state, "current");
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job, sanitizationEvidence: [evidence]));

        var decision = await service.CompleteIntakeAsync(Job, evidence.Metadata.EvidenceId);
        var reopened = await service.ReopenAsync(Job);
        var status = await service.GetAssessmentAccessAsync(Job);

        Assert.NotNull(reopened);
        Assert.Equal(decision, Assert.Single(reopened.SanitizationGateDecisions));
        Assert.Equal(evidence.Metadata.EvidenceId, decision.EvidenceId);
        Assert.Equal(evidence.PolicyVersion, decision.PolicyVersion);
        Assert.Equal(expectedOutcome, decision.Outcome);
        Assert.Equal(expectedReason, decision.Reason);
        Assert.Equal(expectedOutcome == SanitizationGateOutcome.AllowAssessment, status.AllowsAssessment);
    }

    [Fact]
    public async Task ArchiveRestoreIsReversibleAndPreservesEvidenceAndDecision()
    {
        await using var store = new InMemoryJobStore();
        var time = new FixedTimeProvider(Timestamp.AddMinutes(4));
        var service = new JobService(store, time);
        await store.CreateJobAsync(new Job(Job, Device, Timestamp));
        var evidence = CreateEvidence(SanitizationState.Verified, "current");
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job, sanitizationEvidence: [evidence]));
        var decision = await service.CompleteIntakeAsync(Job, evidence.Metadata.EvidenceId);

        time.Advance(TimeSpan.FromMinutes(1));
        await service.ArchiveAsync(Job);
        Assert.Equal(SanitizationGateReason.JobArchived, (await service.GetAssessmentAccessAsync(Job)).Reason);

        time.Advance(TimeSpan.FromMinutes(1));
        await service.RestoreAsync(Job);
        var restored = await service.ReopenAsync(Job);
        var restoredStatus = await service.GetAssessmentAccessAsync(Job);

        Assert.NotNull(restored);
        Assert.False(restored.IsArchived);
        Assert.Equal(evidence, Assert.Single(restored.SanitizationEvidence));
        Assert.Equal(decision, Assert.Single(restored.SanitizationGateDecisions));
        Assert.True(restoredStatus.AllowsAssessment);
    }

    [Fact]
    public async Task MissingStaleAndArchivedEvidenceCannotCompleteGate()
    {
        await using var store = new InMemoryJobStore();
        var service = new JobService(store, new FixedTimeProvider(Timestamp.AddMinutes(4)));
        await store.CreateJobAsync(new Job(Job, Device, Timestamp));

        var missing = await Assert.ThrowsAsync<JobLifecycleException>(() =>
            service.CompleteIntakeAsync(Job, new EvidenceId("evidence-missing")));

        var first = CreateEvidence(SanitizationState.Verified, "first");
        var latest = CreateEvidence(SanitizationState.Failed, "latest");
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job, sanitizationEvidence: [first, latest]));
        var stale = await Assert.ThrowsAsync<JobLifecycleException>(() =>
            service.CompleteIntakeAsync(Job, first.Metadata.EvidenceId));

        await service.ArchiveAsync(Job);
        var archived = await Assert.ThrowsAsync<JobLifecycleException>(() =>
            service.CompleteIntakeAsync(Job, latest.Metadata.EvidenceId));

        Assert.Equal("sanitization_evidence_missing", missing.ResultCode);
        Assert.Equal("sanitization_evidence_stale", stale.ResultCode);
        Assert.Equal("job_archived", archived.ResultCode);
        Assert.Empty((await service.ReopenAsync(Job))!.SanitizationGateDecisions);
    }

    private static readonly DateTimeOffset Timestamp = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly JobId Job = new("job-synthetic-001");
    private static readonly DeviceId Device = new("device-synthetic-001");

    private static SanitizationEvidence CreateEvidence(
        SanitizationState state,
        string suffix,
        ProvenanceKind provenanceKind = ProvenanceKind.SyntheticFixture)
    {
        var unknown = state == SanitizationState.Unknown;
        var hasMedia = state is SanitizationState.Verified or SanitizationState.ReplacementStorage or SanitizationState.Failed;
        return new SanitizationEvidence(
            new EvidenceMetadata(
                new EvidenceId(string.Concat("evidence-sanitization-", suffix)),
                PrivacyClassification.WorkshopRestricted,
                unknown ? EvidenceClassification.NotAvailable : EvidenceClassification.Observed,
                new ProviderId("provider-sanitization"),
                Timestamp.AddMinutes(1),
                new EvidenceProvenance(provenanceKind, string.Concat("source-", suffix)),
                unknown ? ValueAvailability.Unknown : ValueAvailability.Available),
            state,
            unknown ? "not_available" : "external_sanitization",
            unknown ? null : new OperatorId("operator-synthetic"),
            unknown ? null : Timestamp,
            hasMedia ? new MediaIdentifier(string.Concat("SYNTHETIC-MEDIA-", suffix)) : null,
            state switch
            {
                SanitizationState.Unknown => SanitizationVerificationState.NotAvailable,
                SanitizationState.Failed => SanitizationVerificationState.Failed,
                _ => SanitizationVerificationState.Verified,
            },
            "community-policy@1.0.0");
    }

    private static StoredJob Stored(
        bool isArchived = false,
        IReadOnlyList<SanitizationEvidence>? sanitization = null,
        IReadOnlyList<SanitizationGateDecision>? decisions = null) =>
        new(
            new Job(Job, Device, Timestamp),
            isArchived,
            isArchived ? Timestamp.AddMinutes(5) : null,
            observations: [],
            sanitizationEvidence: sanitization ?? [],
            humanTests: [],
            sanitizationGateDecisions: decisions ?? [],
            checkpoints: []);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount)
        {
            _now = _now.Add(amount);
        }
    }

    private sealed class InMemoryJobStore : IJobStore
    {
        private readonly Dictionary<JobId, MutableJob> _jobs = [];

        public Task CreateJobAsync(Job job, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_jobs.TryAdd(job.JobId, new MutableJob(job)))
            {
                throw new InvalidOperationException("duplicate_job");
            }

            return Task.CompletedTask;
        }

        public Task AppendEvidenceAsync(
            JobEvidenceBatch batch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = Get(batch.JobId);
            job.Observations.AddRange(batch.Observations);
            job.Sanitization.AddRange(batch.SanitizationEvidence);
            job.HumanTests.AddRange(batch.HumanTests);
            return Task.CompletedTask;
        }

        public Task SetArchiveStateAsync(
            JobId jobId,
            bool isArchived,
            DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = Get(jobId);
            job.IsArchived = isArchived;
            job.ArchivedAtUtc = isArchived ? changedAtUtc : null;
            return Task.CompletedTask;
        }

        public Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(
            SanitizationGateDecision candidate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = Get(candidate.JobId);
            if (job.IsArchived || job.Sanitization.Count == 0)
            {
                throw new InvalidOperationException("gate_conflict");
            }

            var latest = job.Sanitization[job.Sanitization.Count - 1];
            if (latest.Metadata.EvidenceId != candidate.EvidenceId ||
                !SanitizationGate.IsConsistent(candidate, latest))
            {
                throw new InvalidOperationException("gate_conflict");
            }

            var existing = job.Decisions.Find(value => value.EvidenceId == candidate.EvidenceId);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            job.Decisions.Add(candidate);
            return Task.FromResult(candidate);
        }

        public Task<StoredJob?> LoadJobAsync(
            JobId jobId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                return Task.FromResult<StoredJob?>(null);
            }

            return Task.FromResult<StoredJob?>(new StoredJob(
                job.Job,
                job.IsArchived,
                job.ArchivedAtUtc,
                job.Observations,
                job.Sanitization,
                job.HumanTests,
                job.Decisions,
                checkpoints: []));
        }

        public ValueTask DisposeAsync()
        {
            _jobs.Clear();
            return ValueTask.CompletedTask;
        }

        private MutableJob Get(JobId jobId) =>
            _jobs.TryGetValue(jobId, out var job)
                ? job
                : throw new InvalidOperationException("job_not_found");

        private sealed class MutableJob
        {
            public MutableJob(Job job)
            {
                Job = job;
            }

            public Job Job { get; }

            public bool IsArchived { get; set; }

            public DateTimeOffset? ArchivedAtUtc { get; set; }

            public List<Observation> Observations { get; } = [];

            public List<SanitizationEvidence> Sanitization { get; } = [];

            public List<HumanTestEvidence> HumanTests { get; } = [];

            public List<SanitizationGateDecision> Decisions { get; } = [];
        }
    }
}
