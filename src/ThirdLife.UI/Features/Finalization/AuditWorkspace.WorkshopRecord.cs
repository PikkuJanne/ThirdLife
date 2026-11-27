using System.Collections.Immutable;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Finalization;
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

    private async Task ConfirmReportEvidenceAsync(JobId jobId, FinalizationChecklist? expected, CancellationToken token)
    {
        var current = await ReadFinalizationForReportAsync(jobId, token);
        if ((expected is null) != (current is null) || expected?.EvidenceVersion != current?.EvidenceVersion ||
            expected?.History.Count != current?.History.Count || expected?.ArtifactScans.Count != current?.ArtifactScans.Count)
        {
            throw new JobStoreConflictException();
        }
    }

    private static WorkshopRecordDetails ProjectWorkshopRecordDetails(JobId jobId, ImmutableArray<JournalHistory> journals,
        FinalizationChecklist? finalization)
    {
        return new(journals.Select(history =>
        {
            var plan = history.Plan;
            return new WorkshopPlanProvenance(plan.Key.JobId, plan.Key.PlanSha256, plan.Key.ImpactSha256,
                plan.CatalogSha256, plan.ProfileSha256, plan.PolicySha256, plan.EvidenceSha256, plan.SyntheticData,
                history.Approvals.Select(approval => new WorkshopPlanApproval(approval.ApprovalId, approval.ActorId,
                    approval.OccurredAtUtc)).ToArray());
        }).ToArray(), finalization is null ? null : new WorkshopFinalizationReview(jobId, finalization));
    }
}
