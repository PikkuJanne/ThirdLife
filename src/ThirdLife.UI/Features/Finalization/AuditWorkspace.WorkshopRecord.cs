using System.Collections.Immutable;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private async Task<FinalizationChecklist?> ReadFinalizationForReportAsync(JobId jobId, CancellationToken token)
    {
        var checklist = _store is IFinalizationStore store
            ? await Task.Run(() => store.LoadFinalizationAsync(jobId, token), token)
            : null;
        return IsSeparateArtifactOrigin(checklist) ? throw new JobStoreConflictException() : checklist;
    }

    private async Task ConfirmReportEvidenceAsync(StoredJob job, FinalizationChecklist? expected,
        BackupSampleReportEvidence expectedSample, CancellationToken token)
    {
        var current = await ReadFinalizationForReportAsync(job.Job.JobId, token);
        var currentSample = await ReadBackupSampleForReportAsync(job, token);
        if ((expected is null) != (current is null) || expected?.EvidenceVersion != current?.EvidenceVersion ||
            expected?.History.Count != current?.History.Count || expected?.ArtifactScans.Count != current?.ArtifactScans.Count ||
            expectedSample != currentSample)
        {
            throw new JobStoreConflictException();
        }
    }

    private async Task<BackupSampleReportEvidence> ReadBackupSampleForReportAsync(StoredJob job, CancellationToken token)
    {
        var audit = await ReadBackupSampleAuditAsync(job.Job.JobId, token);
        if (audit.Audit is null)
        {
            return new BackupSampleReportEvidence(null, null, audit.Unavailable);
        }
        var handover = await ReadRecipientHandoverAsync(job);
        if (handover is null) { return new BackupSampleReportEvidence(null, null, true); }
        return new BackupSampleReportEvidence(handover is { IsArchived: false } ? handover.Sequence : null,
            audit.Audit, false);
    }

    private sealed record BackupSampleReportEvidence(long? CurrentSequence, FileHistorySampleAudit? Sample, bool Unavailable);

    private static WorkshopRecordDetails ProjectWorkshopRecordDetails(JobId jobId, ImmutableArray<JournalHistory> journals,
        FinalizationChecklist? finalization, BackupSampleReportEvidence backupSample)
    {
        var sampleReview = backupSample.Sample is { } sample
            ? new WorkshopBackupSampleReview(jobId, sample.State, sample.HandoverSequence != backupSample.CurrentSequence)
            : backupSample.Unavailable ? new WorkshopBackupSampleReview(jobId, null, false) : null;
        return new(journals.Select(history =>
        {
            var plan = history.Plan;
            return new WorkshopPlanProvenance(plan.Key.JobId, plan.Key.PlanSha256, plan.Key.ImpactSha256,
                plan.CatalogSha256, plan.ProfileSha256, plan.PolicySha256, plan.EvidenceSha256, plan.SyntheticData,
                history.Approvals.Select(approval => new WorkshopPlanApproval(approval.ApprovalId, approval.ActorId,
                    approval.OccurredAtUtc)).ToArray());
        }).ToArray(), finalization is null ? null : new WorkshopFinalizationReview(jobId, finalization), sampleReview);
    }
}
