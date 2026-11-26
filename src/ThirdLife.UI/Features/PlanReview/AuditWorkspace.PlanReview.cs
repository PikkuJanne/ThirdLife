using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
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
    private const int MaximumPlanReviewDecisions = 128;
    private readonly Func<PolicyDeviceClass, PolicySnapshot?>? _fixturePlanPolicy;
    private ImmutableArray<PlanReviewDecision> _planReviewHistory = [];
    private PlanProfileOption? _selectedPlanProfile;
    private string? _planReviewChanges;
    private string _planReviewStatus = "Open a current assessment to review a preparation plan.";

    public IReadOnlyList<PlanProfileOption> PlanProfiles { get; }
    public IReadOnlyList<PlanProfileOption> VisiblePlanProfiles { get; }
    public IReadOnlyList<PlanCapabilityChoice> PlanCapabilities { get; private set; } = [];
    public PlanReviewSnapshot? PlanReview { get; private set; }
    public PlanReviewDecision? PlanReviewDecision { get; private set; }
    public IReadOnlyList<PlanReviewDecision> PlanReviewHistory => _planReviewHistory;
    public bool HasPlanReview => PlanReview is not null;
    public string PlanReviewTitle => PlanReview?.Title ?? string.Empty;
    public string PlanReviewDetails => DescribePlanReview();
    public string PlanReviewChanges => _planReviewChanges ?? string.Empty;
    public bool HasMaterialPlanImpact => PlanReview?.MaterialImpact.Count > 0;
    public bool ShowsPackageInstall => IsPackageProfile && PlanReviewDecision is { Outcome: PlanReviewOutcome.Approved };
    public bool ShowsPlanDecision => HasPlanReview && PlanReviewDecision is null;
    public bool CanChoosePlanProfile => VisiblePlanProfiles.Count != 0 && CanReviewPlan;
    public string PlanReviewAccess => HasPlanReview || CanChoosePlanProfile ? string.Empty :
        IsBusy ? "Wait for the current job operation to finish." :
        Job is { IsArchived: true } ? "This job is archived. Its saved records are read-only." :
        Gate is not { AllowsAssessment: true } ? GateStatus :
        VisiblePlanProfiles.Count == 0 ? "No reviewed preparation profile is available in this build." :
        "Complete a current assessment before building a plan.";
    public string PlanReviewStatus => CanRecordPlanReviewDecision
        ? _planReviewStatus
        : _planReviewStatus + " The decision history for this job session is full. Review remains available.";

    public bool CanReviewPlan => CanEditJob && PlanProfiles.Count != 0 &&
        Assessment is { Outcome: AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview } assessment &&
        Gate is { AllowsAssessment: true, Decision: { } gate } && gate.DecisionId == assessment.GateDecisionId &&
        (CandidatePlanPolicy() is not null || PlanProfiles.Any(option => option.PackagePolicy?.Document.Target.DeviceClass ==
            (RepairDeviceClass ?? PolicyDeviceClass.Laptop)));
    public bool CanBuildPlanReview => CanReviewPlan && SelectedPlanProfile is not null && IncludedEssentials && PlanPolicy() is not null;
    public bool CanApprovePlanReview => CanBuildPlanReview && CanRecordPlanReviewDecision && PlanReviewDecision is null &&
        PlanReview?.Plan is { BlockedReason: null, Actions.IsEmpty: false } plan &&
        (plan.SyntheticData ? plan.Actions.All(action => action.Request is NoOpActionRequest) &&
            (_brokerExecution is null || CanApproveBrokerPlan) :
            CanApprovePackagePlan && PackageRequest(plan) is not null);
    public bool CanDeclinePlanReview => CanReviewPlan && CanRecordPlanReviewDecision && HasPlanReview && PlanReviewDecision is null;

    private bool CanRecordPlanReviewDecision => _planReviewHistory.Length < MaximumPlanReviewDecisions;

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
            var active = PlanReviewDecision is { Outcome: PlanReviewOutcome.Approved } approval &&
                (!IsPackageProfile || _packageApproval is not null) ? approval : null;
            var changes = _planReviewChanges;
            var savedApproval = _packageApproval;
            var savedBrokerPlan = _brokerPlan;
            var savedBrokerApproval = _brokerApproval;
            ClearPlanReview();
            var plan = await ResolveCurrentPlanAsync();
            if (plan is null)
            {
                _planReviewStatus = "The assessment changed. Reopen the job before building a plan.";
                return;
            }

            if (!plan.SyntheticData)
            {
                await CheckPackageAsync();
            }

            var current = PlanReviewPresentation.Describe(plan);
            if (active is not null)
            {
                var approved = active.Snapshot;
                var difference = PlanDiff.Compare(approved.Plan, approved.Impact, current.Plan, current.Impact);
                if (!difference.RequiresReapproval)
                {
                    PlanReview = approved;
                    PlanReviewDecision = active;
                    _packageApproval = savedApproval;
                    _brokerPlan = savedBrokerPlan;
                    _brokerApproval = savedBrokerApproval;
                    _planReviewChanges = changes;
                    _planReviewStatus = plan.SyntheticData
                        ? DescribeBrokerApproval()
                        : "The recorded approval still applies to this machine-wide plan.";
                    return;
                }

                PresentPlan(current, difference.PlainLanguage);
                return;
            }

            PresentPlan(current);
        });

    public Task ApprovePlanReviewAsync()
    {
        if (!CanApprovePlanReview || PlanReview is not { } displayed)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var plan = await ResolveCurrentPlanAsync();
            if (plan is null)
            {
                ClearPlanReview();
                _planReviewStatus = "The assessment changed. Reopen the job before reviewing a plan.";
                return;
            }

            var current = PlanReviewPresentation.Describe(plan);
            var difference = PlanDiff.Compare(displayed.Plan, displayed.Impact, current.Plan, current.Impact);
            if (difference.RequiresReapproval)
            {
                PresentPlan(current, difference.PlainLanguage);
                return;
            }

            if (!plan.SyntheticData && !await SavePackageApprovalAsync(displayed))
            {
                return;
            }

            if (plan.SyntheticData && _brokerExecution is not null)
            {
                await SaveBrokerApprovalAsync();
            }

            RecordPlanReviewDecision(PlanReviewOutcome.Approved, displayed);
            _planReviewStatus = plan.SyntheticData
                ? DescribeBrokerApproval()
                : "Approval saved for this exact plan. Select Install approved package to continue.";
        });
    }

    public Task DeclinePlanReviewAsync()
    {
        if (CanDeclinePlanReview && PlanReview is { } displayed)
        {
            RecordPlanReviewDecision(PlanReviewOutcome.Declined, displayed);
            _planReviewStatus = displayed.Plan.Schedule.IsEmpty || DisplayedBrokerHistory is not { } history ||
                !history.Entries.Any(entry => entry.State == JournalState.Started)
                ? "Plan declined. No actions were started."
                : "Plan declined. The saved outcomes for earlier actions remain available.";
            Publish();
        }

        return Task.CompletedTask;
    }

    private bool IncludedEssentials => PlanCapabilities.All(choice => !choice.IsEssential || choice.IsIncluded);

    private PolicySnapshot? PlanPolicy()
    {
        var deviceClass = RepairDeviceClass ?? PolicyDeviceClass.Laptop;
        if (SelectedPlanProfile?.PackagePolicy is { } package)
        {
            return package.Document.Target.DeviceClass == deviceClass ? package : null;
        }

        return CandidatePlanPolicy();
    }

    private PolicySnapshot? CandidatePlanPolicy()
    {
        var deviceClass = RepairDeviceClass ?? PolicyDeviceClass.Laptop;
        return _fixturePlanPolicy is null
            ? WorkshopPolicyCandidate.ForDeviceClass(deviceClass)
            : _fixturePlanPolicy(deviceClass);
    }

    private async Task<ProposedPlan?> ResolveCurrentPlanAsync(CancellationToken cancellationToken = default)
    {
        var jobId = RequireWritableJob().Job.JobId;
        var expectedAssessment = Assessment?.AssessmentId;
        if (expectedAssessment is null || SelectedPlanProfile is not { } chosen || PlanPolicy() is not { } policy)
        {
            return null;
        }

        var optional = PlanCapabilities.Where(choice => !choice.IsEssential && choice.IsIncluded)
            .Select(choice => choice.CapabilityId).ToArray();
        var job = await LoadJobAsync(jobId, cancellationToken);
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
            var plan = PlanResolver.Resolve(job, policy.Document, chosen.Profile, chosen.Catalog, evidence, optional, now);
            return PackageRequest(plan) is not null ||
                (_brokerExecution?.HasScheduledVerification == true && plan is { SyntheticData: true, Actions.Length: > 1 })
                ? plan.WithSchedule(plan.Actions.Select(action => new ScheduledAction(action.Request.ActionId, 1, [])))
                : plan;
        }, cancellationToken);
    }

    private void PresentPlan(PlanReviewSnapshot current, string? changes = null)
    {
        var previous = _planReviewHistory.LastOrDefault(decision => decision.Outcome == PlanReviewOutcome.Approved);
        if (changes is null && previous is { Snapshot: var approved })
        {
            var difference = PlanDiff.Compare(approved.Plan, approved.Impact, current.Plan, current.Impact);
            if (difference.RequiresReapproval)
            {
                changes = difference.PlainLanguage;
            }
        }

        PlanReview = current;
        PlanReviewDecision = null;
        SetBrokerPlan(current);
        RestoreScheduledApproval();
        _planReviewChanges = changes;
        _planReviewStatus = current.Plan.BlockedReason is not null
            ? "Preparation is blocked. Review the reason below."
            : changes is not null
                ? "The plan changed. Review the changes before approving."
                : _brokerApproval is not null ? DescribeBrokerApproval()
                    : "Review the included actions and impact before approving.";
    }

    private void RecordPlanReviewDecision(PlanReviewOutcome outcome, PlanReviewSnapshot displayed)
    {
        var decision = new PlanReviewDecision(outcome, displayed, _timeProvider.GetUtcNow());
        _planReviewHistory = _planReviewHistory.Add(decision);
        PlanReviewDecision = decision;
    }

    private string DescribePlanReview()
    {
        if (PlanReview is not { } displayed)
        {
            return string.Empty;
        }

        if (_planReviewChanges is null && _planReviewHistory.IsEmpty)
        {
            return displayed.Details;
        }

        var details = new StringBuilder(displayed.Details);
        if (_planReviewChanges is { } changes)
        {
            details.AppendLine().AppendLine("Plan changes").AppendLine(changes);
        }

        if (!_planReviewHistory.IsEmpty)
        {
            details.AppendLine().AppendLine("Decisions in this job session");
            foreach (var decision in _planReviewHistory)
            {
                details.AppendLine(CultureInfo.CurrentCulture,
                    $"{decision.Outcome} at {decision.DecidedAtUtc:u}. Plan SHA-256 {decision.PlanSha256}");
            }
        }

        return details.ToString();
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
        _packageApproval = null;
        _packageResolution = null;
        _packageDiagnosticUnavailable = false;
        ClearBrokerPlan(clearSelection);
        _planReviewChanges = null;
        if (clearSelection)
        {
            _selectedPlanProfile = null;
            _packageOperatorCode = string.Empty;
            PlanCapabilities = [];
            _planReviewHistory = [];
        }

        _planReviewStatus = Assessment is null || Job is not { IsArchived: false }
            ? "Open a current assessment to review a preparation plan."
            : SelectedPlanProfile is null
            ? "Select a profile to review a plan for the current assessment."
            : PlanPolicy() is null
                ? "The selected plan has no policy for this device class."
                : IncludedEssentials
                    ? "Build a plan to review this selection."
                    : "Expected readiness is not met because an essential action is skipped.";
    }
}
