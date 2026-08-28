using ThirdLife.Core.Evidence;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Jobs;

public sealed class JobLifecycleException : Exception
{
    internal JobLifecycleException(string resultCode, string message)
        : base(message)
    {
        ResultCode = resultCode;
    }

    public string ResultCode { get; }
}

public sealed class JobService
{
    private readonly IJobStore _store;
    private readonly TimeProvider _timeProvider;

    public JobService(IJobStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<Job> CreateAsync(
        DeviceId deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        var job = Job.Create(deviceId, _timeProvider.GetUtcNow());
        await _store.CreateJobAsync(job, cancellationToken).ConfigureAwait(false);
        return job;
    }

    public Task<StoredJob?> ReopenAsync(
        JobId jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return _store.LoadJobAsync(jobId, cancellationToken);
    }

    public Task ArchiveAsync(
        JobId jobId,
        CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(jobId, isArchived: true, cancellationToken);

    public Task RestoreAsync(
        JobId jobId,
        CancellationToken cancellationToken = default) =>
        SetArchiveStateAsync(jobId, isArchived: false, cancellationToken);

    public async Task<SanitizationGateDecision> CompleteIntakeAsync(
        JobId jobId,
        EvidenceId evidenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(evidenceId);

        var storedJob = await RequireJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (storedJob.IsArchived)
        {
            throw new JobLifecycleException(
                "job_archived",
                "An archived job must be restored before its sanitization gate can be completed.");
        }

        var latestEvidence = storedJob.SanitizationEvidence.Count == 0
            ? null
            : storedJob.SanitizationEvidence[storedJob.SanitizationEvidence.Count - 1];
        if (latestEvidence is null)
        {
            throw new JobLifecycleException(
                "sanitization_evidence_missing",
                "Sanitization evidence is required before the gate can be completed.");
        }

        if (latestEvidence.Metadata.EvidenceId != evidenceId)
        {
            throw new JobLifecycleException(
                "sanitization_evidence_stale",
                "Only the newest committed sanitization evidence can complete the gate.");
        }

        var candidate = SanitizationGate.Evaluate(
            jobId,
            latestEvidence,
            _timeProvider.GetUtcNow());
        return await _store.RecordSanitizationGateDecisionAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SanitizationGateStatus> GetAssessmentAccessAsync(
        JobId jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        var storedJob = await RequireJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        return SanitizationGate.Inspect(storedJob);
    }

    private Task SetArchiveStateAsync(
        JobId jobId,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return _store.SetArchiveStateAsync(
            jobId,
            isArchived,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<StoredJob> RequireJobAsync(
        JobId jobId,
        CancellationToken cancellationToken)
    {
        var storedJob = await _store.LoadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        return storedJob ?? throw new JobLifecycleException(
            "job_not_found",
            "The requested job does not exist.");
    }
}
