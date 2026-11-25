using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory;
using ThirdLife.Persistence;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private const string NoCandidateDecision = "A saved assessment can be evaluated with the bundled laptop candidate.";
    private StoredPolicyDecision? _savedPolicy;
    private bool _policyHistoryUnavailable;

    public bool HasPolicyDecision => _savedPolicy is not null;

    public bool CanEvaluatePolicy => CanCapturePolicy && !HasPolicyDecision;

    public bool CanReevaluatePolicy => CanCapturePolicy && HasPolicyDecision;

    public string PolicyStatus { get; private set; } = NoCandidateDecision;

    public string PolicyDispositionTitle { get; private set; } = string.Empty;

    public string PolicyDispositionSummary { get; private set; } = string.Empty;

    public string PolicyExpertDetails { get; private set; } = string.Empty;

    private bool CanCapturePolicy => CanEditJob && !_policyHistoryUnavailable &&
        _store is IPolicyHistoryStore && Assessment is { Outcome: AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview } assessment &&
        Gate is { AllowsAssessment: true, Decision: { } gate } && gate.DecisionId == assessment.GateDecisionId &&
        CandidateForSelectedClass() is not null;

    public Task EvaluatePolicyAsync() => CanEvaluatePolicy ? CapturePolicyAsync() : Task.CompletedTask;

    public Task ReevaluatePolicyAsync() => CanReevaluatePolicy ? CapturePolicyAsync() : Task.CompletedTask;

    private PolicySnapshot? CandidateForSelectedClass() =>
        WorkshopPolicyCandidate.ForDeviceClass(RepairDeviceClass ?? PolicyDeviceClass.Laptop);

    private Task CapturePolicyAsync()
    {
        var assessmentId = Assessment!.AssessmentId;
        var expectedDecisionId = _savedPolicy?.Decision.DecisionId;
        return RunAsync(async () =>
        {
            var job = RequireWritableJob();
            var jobId = job.Job.JobId;
            var candidate = CandidateForSelectedClass() ?? throw new InvalidOperationException("A candidate policy is unavailable for this class.");
            var store = (IPolicyHistoryStore)_store!;
            var snapshot = await _orchestrator!.LoadAsync(jobId, assessmentId)
                ?? throw new InvalidOperationException("The selected assessment is unavailable.");
            var saved = await FindSavedPolicyAsync(jobId, snapshot);
            if (expectedDecisionId != saved?.Decision.DecisionId)
            {
                ApplySavedPolicy(saved);
                PolicyStatus = "The saved candidate decision changed. Review it before reevaluating.";
                Status = PolicyStatus;
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var storedJob = await LoadJobAsync(jobId);
            var decision = await Task.Run(() =>
            {
                var evidence = AuditPolicyEvidence.Create(storedJob, snapshot, now);
                // Capability choices are absent until a profile is selected and saved.
                var result = DispositionEngine.Evaluate(candidate.Document, evidence, saved?.Decision.ActiveCapabilityIds);
                return PolicyDecisionRecord.Create(candidate, jobId, snapshot.AssessmentId.Value, result,
                    evidence.Select(item => item.Metadata.EvidenceId), now, expectedDecisionId);
            });
            if (saved is not null && !saved.Decision.EvidenceIds.AsSpan().SequenceEqual(decision.EvidenceIds.AsSpan()))
            {
                PolicyStatus = "The evidence set changed. Run a new assessment to evaluate it and keep this saved decision.";
                Status = PolicyStatus;
                return;
            }

            await store.AppendPolicyDecisionAsync(candidate, decision);
            var committed = await store.LoadPolicyDecisionAsync(jobId, decision.DecisionId)
                ?? throw new InvalidOperationException("The saved candidate decision is unavailable.");
            ApplySavedPolicy(committed);
            ClearReport();
            Status = PolicyStatus;
        });
    }

    private async Task LoadPolicyViewAsync(JobId jobId, AssessmentSnapshot? assessment)
    {
        ClearPolicyView();
        if (_hasSeparateEvidenceOrigin || assessment is null)
        {
            return;
        }

        try
        {
            ApplySavedPolicy(await FindSavedPolicyAsync(jobId, assessment));
            if (!HasPolicyDecision && CandidateForSelectedClass() is null)
            {
                PolicyStatus = "A candidate policy is unavailable for this device class.";
            }
        }
        catch (JobStoreException)
        {
            _policyHistoryUnavailable = true;
            PolicyStatus = "The saved policy history needs review.";
        }
    }

    private Task<StoredPolicyDecision?> FindSavedPolicyAsync(
        JobId jobId, AssessmentSnapshot? assessment, CancellationToken cancellationToken = default) =>
        assessment is not null && _store is IPolicyHistoryStore history
            ? history.LoadLatestPolicyDecisionAsync(jobId, assessment.AssessmentId.Value, cancellationToken)
            : Task.FromResult<StoredPolicyDecision?>(null);

    private void ClearPolicyView()
    {
        _policyHistoryUnavailable = false;
        ApplySavedPolicy(null);
    }

    private void ApplySavedPolicy(StoredPolicyDecision? saved)
    {
        _savedPolicy = saved;
        if (saved is null)
        {
            PolicyStatus = NoCandidateDecision;
            PolicyDispositionTitle = string.Empty;
            PolicyDispositionSummary = string.Empty;
            PolicyExpertDetails = string.Empty;
            return;
        }

        var decision = saved.Decision;
        var historical = !saved.Snapshot.Document.IsCurrentSchema;
        var label = saved.Snapshot.Document.Status == ThirdLife.Policy.Schema.PolicyStatus.Candidate ? "Candidate" : "Saved";
        PolicyStatus = historical
            ? "Historical policy decision. Use the current policy for a new preparation decision."
            : $"{label} decision saved for workshop review.";
        PolicyDispositionTitle = WorkshopReportRenderer.SafePolicyPresentation(decision.Title);
        PolicyDispositionSummary = historical && decision.Disposition == Disposition.AlternativeOperatingSystemCandidate
            ? "This outcome was recorded under an earlier policy."
            : WorkshopReportRenderer.SafePolicyPresentation(decision.Summary);
        PolicyExpertDetails = WorkshopReportRenderer.SafePolicyPresentation(
            $"Policy {decision.PolicyId} version {decision.PolicyVersion}\nSHA-256 {decision.PolicySha256}\n\n" +
            (historical ? "Original recorded explanation\n" : string.Empty) +
            $"{decision.Explanation}\n\n{decision.RuleReport}\n\nExceptions\n" +
            (decision.ExceptionReport ?? "See the exception details captured with this saved policy."));
    }
}
