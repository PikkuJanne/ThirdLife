using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private readonly Func<PolicyDeviceClass, PolicySnapshot?>? _fixturePlanPolicy;
    private PlanProfileOption? _selectedPlanProfile;

    public IReadOnlyList<PlanProfileOption> PlanProfiles { get; } = WorkshopPlanCandidate.Options;
    public IReadOnlyList<PlanCapabilityChoice> PlanCapabilities { get; private set; } = [];
    public PlanReviewSnapshot? PlanReview { get; private set; }
    public PlanReviewDecision? PlanReviewDecision { get; private set; }
    public bool HasPlanReview => PlanReview is not null;
    public string PlanReviewTitle => PlanReview?.Title ?? string.Empty;
    public string PlanReviewDetails => PlanReview?.Details ?? string.Empty;
    public string PlanReviewStatus { get; private set; } = "Open a current assessment to review a synthetic plan.";

    public bool CanReviewPlan => CanEditJob && PlanProfiles.Count != 0 &&
        Assessment is { Outcome: AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview } assessment &&
        Gate is { AllowsAssessment: true, Decision: { } gate } && gate.DecisionId == assessment.GateDecisionId &&
        PlanPolicy() is not null;
    public bool CanBuildPlanReview => CanReviewPlan && SelectedPlanProfile is not null && IncludedEssentials;
    public bool CanApprovePlanReview => CanBuildPlanReview && PlanReviewDecision is null &&
        PlanReview?.Plan is { SyntheticData: true, BlockedReason: null, Actions.IsEmpty: false } plan &&
        plan.Actions.All(action => action.Request is NoOpActionRequest);
    public bool CanDeclinePlanReview => CanReviewPlan && HasPlanReview && PlanReviewDecision is null;

    public PlanProfileOption? SelectedPlanProfile
    {
        get => _selectedPlanProfile;
        set
        {
            if (ReferenceEquals(value, _selectedPlanProfile) || !CanReviewPlan ||
                (value is not null && !PlanProfiles.Contains(value)))
            {
                return;
            }

            _selectedPlanProfile = value;
            PlanCapabilities = value?.Profile.WorkshopCapabilities
                .Select(capability => new PlanCapabilityChoice(capability, () => CanReviewPlan, ChangePlanSelection))
                .ToArray() ?? [];
            ChangePlanSelection();
        }
    }

    public Task BuildPlanReviewAsync() => !CanBuildPlanReview
        ? Task.CompletedTask
        : RunAsync(async () =>
        {
            ClearPlanReview();
            var plan = await ResolveCurrentPlanAsync();
            if (plan is null)
            {
                PlanReviewStatus = "The assessment changed. Reopen the job before building a plan.";
                return;
            }

            PresentPlan(plan);
        });

    public Task ApprovePlanReviewAsync()
    {
        if (!CanApprovePlanReview || PlanReview is not { } displayed)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var current = await ResolveCurrentPlanAsync();
            if (current?.ContentSha256 != displayed.Plan.ContentSha256)
            {
                ClearPlanReview();
                if (current is not null)
                {
                    PresentPlan(current);
                }

                PlanReviewStatus = current is null
                    ? "The assessment changed. Reopen the job before reviewing a plan."
                    : "The plan changed. Review the updated plan before approving.";
                return;
            }

            PlanReviewDecision = new PlanReviewDecision(PlanReviewOutcome.Approved, displayed, _timeProvider.GetUtcNow());
            PlanReviewStatus = "Approval recorded for this synthetic plan. Device readiness still requires verification.";
        });
    }

    public Task DeclinePlanReviewAsync()
    {
        if (CanDeclinePlanReview && PlanReview is { } displayed)
        {
            PlanReviewDecision = new PlanReviewDecision(PlanReviewOutcome.Declined, displayed, _timeProvider.GetUtcNow());
            PlanReviewStatus = "Plan declined. No actions were started.";
            Publish();
        }

        return Task.CompletedTask;
    }

    private bool IncludedEssentials => PlanCapabilities.All(choice => !choice.IsEssential || choice.IsIncluded);

    private PolicySnapshot? PlanPolicy()
    {
        var deviceClass = RepairDeviceClass ?? PolicyDeviceClass.Laptop;
        return _fixturePlanPolicy is null
            ? WorkshopPolicyCandidate.ForDeviceClass(deviceClass)
            : _fixturePlanPolicy(deviceClass);
    }

    private async Task<ProposedPlan?> ResolveCurrentPlanAsync()
    {
        var jobId = RequireWritableJob().Job.JobId;
        var expectedAssessment = Assessment?.AssessmentId;
        if (expectedAssessment is null || SelectedPlanProfile is not { } chosen || PlanPolicy() is not { } policy)
        {
            return null;
        }

        var optional = PlanCapabilities.Where(choice => !choice.IsEssential && choice.IsIncluded)
            .Select(choice => choice.CapabilityId).ToArray();
        var job = await LoadJobAsync(jobId);
        var now = _timeProvider.GetUtcNow();
        return await Task.Run(() =>
        {
            var assessment = AssessmentOrchestrator.ReadLatest(job);
            var gate = SanitizationGate.Inspect(job);
            if (assessment is null || assessment.AssessmentId != expectedAssessment ||
                !gate.AllowsAssessment || gate.Decision?.DecisionId != assessment.GateDecisionId ||
                IsSeparateEvidenceOrigin(job) ||
                assessment.Outcome is not (AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview))
            {
                return null;
            }

            var evidence = AuditPolicyEvidence.Create(job, assessment, now);
            return PlanResolver.Resolve(job, policy.Document, chosen.Profile, chosen.Catalog, evidence, optional, now);
        });
    }

    private void PresentPlan(ProposedPlan plan)
    {
        PlanReview = PlanReviewPresentation.Describe(plan);
        PlanReviewStatus = plan.BlockedReason is null
            ? "Review the included actions and impact before approving."
            : "Preparation is blocked. Review the reason below.";
    }

    private void ChangePlanSelection()
    {
        ClearPlanReview();
        Publish();
    }

    private void ClearPlanReview(bool clearSelection = false)
    {
        PlanReview = null;
        PlanReviewDecision = null;
        if (clearSelection)
        {
            _selectedPlanProfile = null;
            PlanCapabilities = [];
        }

        PlanReviewStatus = Assessment is null || Job is not { IsArchived: false }
            ? "Open a current assessment to review a synthetic plan."
            : SelectedPlanProfile is null
            ? "Select a profile to review a synthetic plan for the current assessment."
            : IncludedEssentials
                ? "Build a plan to review this selection."
                : "Expected readiness is not met because an essential action is skipped.";
    }
}
