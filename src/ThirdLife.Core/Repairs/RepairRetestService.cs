using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Repairs;

public sealed class RepairRetestService
{
    private readonly IJobStore _store;
    private readonly TimeProvider _timeProvider;

    public RepairRetestService(IJobStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RepairRecord> RecordAsync(
        JobId jobId,
        string priorAssessmentId,
        EvidenceId failedEvidenceId,
        RepairComponent component,
        RepairAction action,
        OperatorId operatorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        _ = DomainValue.RequireIdentifier(priorAssessmentId, nameof(priorAssessmentId));
        ArgumentNullException.ThrowIfNull(failedEvidenceId);
        _ = DomainValue.RequireDefined(component, nameof(component));
        _ = DomainValue.RequireDefined(action, nameof(action));
        ArgumentNullException.ThrowIfNull(operatorId);

        var job = await _store.LoadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job?.Job.JobId != jobId)
        {
            throw new JobLifecycleException("job_not_found", "The requested job does not exist.");
        }

        if (job.IsArchived)
        {
            throw new JobLifecycleException("job_archived", "Restore the job before recording a repair.");
        }

        _ = RepairJournal.GetRepairs(job);
        var recordedAt = DomainValue.RequireTimestamp(_timeProvider.GetUtcNow(), "recordedAtUtc");
        var matches = job.Observations.Where(observation => observation.Metadata.EvidenceId == failedEvidenceId).Take(2).ToArray();
        if (matches.Length != 1 || !RepairJournal.IsRepairEvidence(matches[0], component) ||
            matches[0].Metadata.CollectedAtUtc < job.Job.CreatedAtUtc || matches[0].Metadata.CollectedAtUtc > recordedAt)
        {
            throw new JobLifecycleException("repair_evidence_unavailable", "Select a saved finding for the component and review its evidence.");
        }

        var source = matches[0];
        var gate = SanitizationGate.Inspect(job).Decision;
        if (gate?.EvaluatedAtUtc > recordedAt ||
            (job.SanitizationEvidence.Count > 0 && job.SanitizationEvidence[^1].Metadata.CollectedAtUtc > recordedAt))
        {
            throw new JobLifecycleException("repair_evidence_unavailable", "The repair would precede its saved job records.");
        }

        var isFixture = source.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture;
        if (job.SanitizationEvidence.Any(item =>
                (item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) != isFixture))
        {
            throw new JobLifecycleException("repair_evidence_unavailable", "The finding belongs to a different evidence scenario.");
        }

        var invalidationId = RepairJournal.IsStorageReplacement(component, action) ? EvidenceId.New() : null;
        var repair = new RepairRecord(EvidenceId.New(), jobId, priorAssessmentId, failedEvidenceId, source.EvidenceKey,
            component, action, operatorId, recordedAt, gate?.DecisionId, invalidationId);
        var marker = RepairJournal.CreateRepairMarker(repair, isFixture);
        var invalidation = invalidationId is null ? null : RepairJournal.CreateSanitizationInvalidation(invalidationId, recordedAt, isFixture);
        await _store.AppendEvidenceAsync(new JobEvidenceBatch(jobId,
            observations: [marker],
            sanitizationEvidence: invalidation is null ? null : [invalidation],
            requireUnarchivedJob: true), cancellationToken).ConfigureAwait(false);
        return repair;
    }
}
