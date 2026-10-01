using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Inventory.Tests;

internal enum AssessmentStoreFault
{
    None,
    BeforeCommit,
    AfterCommit,
    MismatchedCommit,
}

/// <summary>A transactional fixture store; a failed batch never exposes an inserted prefix.</summary>
internal sealed class AssessmentTestStore : IJobStore
{
    internal static readonly DateTimeOffset Timestamp = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly object _gate = new();
    private readonly Dictionary<JobId, MutableJob> _jobs = [];
    private readonly List<JobEvidenceBatch> _committed = [];
    private int _gateChecks;

    public AssessmentTestStore(SanitizationState state = SanitizationState.Verified, bool recordDecision = true)
    {
        JobId = new("job-assessment-synthetic");
        var job = new MutableJob(new(JobId, new("device-assessment-synthetic"), Timestamp));
        var evidence = CreateSanitization(state);
        job.Sanitization.Add(evidence);
        if (recordDecision) { job.Decisions.Add(SanitizationGate.Evaluate(JobId, evidence, Timestamp)); }
        _jobs.Add(JobId, job);
    }

    public JobId JobId { get; }
    public Action<JobEvidenceBatch>? BeforeAppend { get; set; }
    public Action<JobEvidenceBatch>? AfterCommit { get; set; }
    public Func<JobEvidenceBatch, Task>? AfterCommitAsync { get; set; }
    public Func<int, Task>? BeforeGateCheckAsync { get; set; }
    public Func<JobEvidenceBatch, bool>? FaultSelector { get; set; }
    public AssessmentStoreFault Fault { get; set; }
    public IReadOnlyList<JobEvidenceBatch> Committed { get { lock (_gate) { return _committed.ToArray(); } } }

    public Task CreateJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { _jobs.Add(job.JobId, new(job)); }
        return Task.CompletedTask;
    }

    public async Task AppendEvidenceAsync(JobEvidenceBatch batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fault = AssessmentStoreFault.None;
        lock (_gate)
        {
            BeforeAppend?.Invoke(batch);
            cancellationToken.ThrowIfCancellationRequested();
            var job = _jobs[batch.JobId];
            if (batch.ExpectedSanitizationGateDecisionId is { } expected)
            {
                var access = SanitizationGate.Inspect(Snapshot(job));
                if (!access.AllowsAssessment || access.Decision!.DecisionId != expected)
                { throw new InvalidOperationException("SYNTHETIC-PRIVATE-GATE-CHANGED"); }
                Assert.Empty(batch.SanitizationEvidence);
                Assert.Empty(batch.HumanTests);
            }

            var ids = job.Observations.Select(static value => value.Metadata.EvidenceId).ToHashSet();
            Assert.All(batch.Observations, observation => Assert.DoesNotContain(observation.Metadata.EvidenceId, ids));
            if (Fault != AssessmentStoreFault.None && FaultSelector?.Invoke(batch) == true)
            {
                fault = Fault;
                Fault = AssessmentStoreFault.None;
            }
            if (fault == AssessmentStoreFault.BeforeCommit)
            { throw new InvalidOperationException("SYNTHETIC-PRIVATE-STORE-BEFORE-COMMIT"); }

            var observations = batch.Observations.ToArray();
            if (fault == AssessmentStoreFault.MismatchedCommit)
            {
                var index = Array.FindIndex(observations, static value => value.Value?.BooleanValue.HasValue == true);
                Assert.True(index >= 0);
                var original = observations[index];
                observations[index] = new(original.Metadata, original.EvidenceKey,
                    EvidenceValue.FromBoolean(!original.Value!.BooleanValue!.Value), original.Unit, original.LimitationCode);
            }

            job.Observations.AddRange(observations);
            job.Sanitization.AddRange(batch.SanitizationEvidence);
            job.HumanTests.AddRange(batch.HumanTests);
            _committed.Add(batch);
        }

        AfterCommit?.Invoke(batch);
        if (AfterCommitAsync is { } afterCommit) { await afterCommit(batch); }
        if (fault is AssessmentStoreFault.AfterCommit or AssessmentStoreFault.MismatchedCommit)
        { throw new InvalidOperationException("SYNTHETIC-PRIVATE-STORE-AFTER-COMMIT"); }
    }

    public Task SetArchiveStateAsync(JobId jobId, bool isArchived, DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var job = _jobs[jobId];
            job.IsArchived = isArchived;
            job.ArchivedAtUtc = isArchived ? changedAtUtc : null;
        }
        return Task.CompletedTask;
    }

    public Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(SanitizationGateDecision candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var job = _jobs[candidate.JobId];
            Assert.False(job.IsArchived);
            Assert.Equal(job.Sanitization[^1].Metadata.EvidenceId, candidate.EvidenceId);
            job.Decisions.Add(candidate);
        }
        return Task.FromResult(candidate);
    }

    public Task<StoredJob?> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? Snapshot(job) : null);
        }
    }

    public async Task RequireSanitizationGateAsync(JobId jobId, SanitizationGateDecisionId expectedDecisionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var check = Interlocked.Increment(ref _gateChecks);
        if (BeforeGateCheckAsync is { } beforeCheck) { await beforeCheck(check); }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var access = SanitizationGate.Inspect(Snapshot(_jobs[jobId]));
            if (!access.AllowsAssessment || access.Decision!.DecisionId != expectedDecisionId)
            { throw new InvalidOperationException("SYNTHETIC-PRIVATE-GATE-CHANGED"); }
        }
    }

    public void ChangeSanitization(SanitizationState state, bool recordDecision)
    {
        lock (_gate)
        {
            var job = _jobs[JobId];
            var evidence = CreateSanitization(state);
            job.Sanitization.Add(evidence);
            if (recordDecision) { job.Decisions.Add(SanitizationGate.Evaluate(JobId, evidence, Timestamp)); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { _jobs.Clear(); _committed.Clear(); }
        return ValueTask.CompletedTask;
    }

    private static StoredJob Snapshot(MutableJob job) => new(job.Job, job.IsArchived, job.ArchivedAtUtc,
        job.Observations, job.Sanitization, job.HumanTests, job.Decisions, []);

    private static SanitizationEvidence CreateSanitization(SanitizationState state)
    {
        var unknown = state == SanitizationState.Unknown;
        return new(new(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
                unknown ? EvidenceClassification.NotAvailable : EvidenceClassification.Observed,
                new("provider-synthetic-sanitization"), Timestamp,
                new(ProvenanceKind.SyntheticFixture, "source.synthetic.sanitization"),
                unknown ? ValueAvailability.Unknown : ValueAvailability.Available),
            state, "external_sanitization", unknown ? null : new("operator-synthetic"),
            unknown ? null : Timestamp,
            state is SanitizationState.Verified or SanitizationState.ReplacementStorage or SanitizationState.Failed
                ? new("SYNTHETIC-MEDIA") : null,
            state switch
            {
                SanitizationState.Unknown => SanitizationVerificationState.NotAvailable,
                SanitizationState.Failed => SanitizationVerificationState.Failed,
                _ => SanitizationVerificationState.Verified,
            }, "community-policy@1.0.0");
    }

    private sealed class MutableJob(Job job)
    {
        public Job Job { get; } = job;
        public bool IsArchived { get; set; }
        public DateTimeOffset? ArchivedAtUtc { get; set; }
        public List<Observation> Observations { get; } = [];
        public List<SanitizationEvidence> Sanitization { get; } = [];
        public List<HumanTestEvidence> HumanTests { get; } = [];
        public List<SanitizationGateDecision> Decisions { get; } = [];
    }
}
