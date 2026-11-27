using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;

namespace ThirdLife.Policy.Versioning;

public sealed record StoredPolicyDecision(PolicySnapshot Snapshot, PolicyDecisionRecord Decision)
{
    public bool HasNewSecurityReview(StoredJob job)
    {
        if (job.Job.JobId != Decision.JobId)
        {
            throw new ArgumentException("The review and saved decision must belong to the same job.", nameof(job));
        }

        var progress = ManualTestService.GetProgress(job, Decision.AssessmentId);
        foreach (var item in progress.Items)
        {
            if (!ManualTestCatalog.IsSecurityReview(item.Case.Id) || item.Latest is not { } latest) { continue; }
            var key = item.Case.Id == ManualTestCatalog.AdministratorReview ? "security.administrator_review" : "security.remote_access_review";
            if (Snapshot.Document.Rules.Any(rule => rule.Requirement.EvidenceKey.Value == key) &&
                !Decision.EvidenceIds.Contains(latest.Metadata.EvidenceId))
            {
                return true;
            }
        }

        return false;
    }
}

public interface IPolicyHistoryStore
{
    Task AppendPolicyDecisionAsync(
        PolicySnapshot snapshot, PolicyDecisionRecord decision, CancellationToken cancellationToken = default);

    Task<StoredPolicyDecision?> LoadPolicyDecisionAsync(
        JobId jobId, string decisionId, CancellationToken cancellationToken = default);

    Task<StoredPolicyDecision?> LoadLatestPolicyDecisionAsync(
        JobId jobId, string assessmentId, CancellationToken cancellationToken = default);
}
