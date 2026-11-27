using System.Collections.Immutable;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.Persistence;
using ThirdLife.Policy.Schema;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    public string PreparationDispositionStatus { get; private set; } = string.Empty;

    public string PreparationOutcomes { get; private set; } = string.Empty;

    public bool HasPreparationDisposition => PreparationDispositionStatus.Length > 0;
    public bool HasPreparationOutcomes => PreparationOutcomes.Length > 0;

    private Task<ImmutableArray<JournalHistory>> ReadPreparationJournalsAsync(JobId jobId, CancellationToken cancellationToken) =>
        _store is IActionJournalStore journal
            ? Task.Run(() => journal.LoadJobJournalsAsync(jobId, cancellationToken), cancellationToken)
            : Task.FromResult(ImmutableArray<JournalHistory>.Empty);

    private async Task RefreshPreparationDispositionAsync(JobId jobId, bool invalidateReport = true)
    {
        if (invalidateReport)
        {
            ClearReport();
        }

        var stored = await LoadJobAsync(jobId);
        var assessment = await Task.Run(() => AssessmentOrchestrator.ReadLatest(stored));
        await LoadPreparationDispositionAsync(stored, assessment);
    }

    private async Task LoadPreparationDispositionAsync(StoredJob job, AssessmentSnapshot? assessment)
    {
        if (IsSeparateEvidenceOrigin(job))
        {
            ClearPreparationDisposition();
            ClearProfileVerification();
            return;
        }

        try
        {
            var histories = await ReadPreparationJournalsAsync(job.Job.JobId, CancellationToken.None);
            var checkpoint = await ReadMaintenanceForReportAsync(job.Job.JobId, CancellationToken.None);
            await LoadProfileVerificationAsync(job.Job.JobId);
            var now = _timeProvider.GetUtcNow();
            var verification = _profileVerification;
            var preparation = await Task.Run(() => ProjectPreparationRecords(job, assessment, histories, checkpoint, verification, now, CancellationToken.None));
            var current = preparation.SingleOrDefault(plan => plan.CurrentDisposition is not null);
            var status = preparation.Length == 0 ? string.Empty : WorkshopReportRenderer.DescribePreparationSummary(current);
            var outcomes = current is null ? string.Empty : string.Join(Environment.NewLine,
                current.Actions.Where(action => action.LatestForAction).Select(action =>
                    $"{PlanReviewPresentation.CapabilityName(action.CapabilityId)} ({(action.Essential ? "essential" : "optional")}). " +
                    WorkshopReportRenderer.DescribePreparationAction(action)));
            if (PreparationDispositionStatus != status || PreparationOutcomes != outcomes)
            {
                ClearReport();
            }

            PreparationDispositionStatus = status;
            PreparationOutcomes = outcomes;
        }
        catch (JobStoreException)
        {
            ClearReport();
            PreparationDispositionStatus = "The saved preparation action history needs review.";
            PreparationOutcomes = string.Empty;
        }
    }

    private void ClearPreparationDisposition()
    {
        PreparationDispositionStatus = string.Empty;
        PreparationOutcomes = string.Empty;
    }

    private Task<MaintenanceCheckpoint?> ReadMaintenanceForReportAsync(JobId jobId, CancellationToken token) =>
        _store is IMaintenancePhaseStore store && !IsFixtureScenario
            ? Task.Run(() => store.LoadMaintenanceAsync(jobId, token), token)
            : Task.FromResult<MaintenanceCheckpoint?>(null);

    private static WorkshopMaintenanceCheckpoint? ProjectMaintenance(JournalHistory history, MaintenanceCheckpoint? saved,
        SavedProfileVerification? verification)
    {
        if (!history.Plan.FullMaintenance) { return null; }
        if (saved?.Plan != history.Plan.Key || saved.ApprovalId != history.ActiveApproval?.ApprovalId)
        {
            return new(WorkshopMaintenancePhase.Unconfirmed, null);
        }

        var phase = saved.Phase switch
        {
            MaintenancePhase.WindowsUpdates => WorkshopMaintenancePhase.WindowsUpdates,
            MaintenancePhase.Packages => WorkshopMaintenancePhase.Packages,
            MaintenancePhase.PostPackageChecks => WorkshopMaintenancePhase.PostPackageChecks,
            MaintenancePhase.FinalRestart => WorkshopMaintenancePhase.FinalRestart,
            MaintenancePhase.FinalVerification => WorkshopMaintenancePhase.FinalVerification,
            _ => WorkshopMaintenancePhase.Unconfirmed,
        };
        return new(phase, saved.LastCheckFailure?.Issue switch
        {
            MaintenanceCheckIssue.PackageMismatch => WorkshopMaintenanceIssue.PackageMismatch,
            MaintenanceCheckIssue.PackageUnavailable => WorkshopMaintenanceIssue.PackageUnavailable,
            _ => null,
        }, verification is { IsCurrent: true } && verification.Record.Checkpoint == saved);
    }

    private WorkshopPreparationPlan[] ProjectPreparationRecords(StoredJob job, AssessmentSnapshot? assessment,
        ImmutableArray<JournalHistory> journals, MaintenanceCheckpoint? checkpoint, SavedProfileVerification? verification,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reviewed = journals.Where(history => !history.Entries.IsEmpty).ToArray();
        var latest = reviewed.OrderBy(LatestReviewTime).LastOrDefault();
        var result = new List<WorkshopPreparationPlan>();
        foreach (var history in reviewed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isCurrent = history == latest && IsCurrentPreparationPlan(job, assessment, history.Plan, now);
            var projection = PreparationDisposition.Evaluate(history);
            var outcomes = isCurrent
                ? projection.Actions.Concat(projection.History.Where(item => !item.IsCurrent))
                : projection.History.Concat(projection.Actions.Where(item =>
                    item.State is JournalState.Started or JournalState.Applied or JournalState.RequiresReview));
            var rows = outcomes.Select(ProjectPreparationAction).ToArray();
            result.Add(new WorkshopPreparationPlan(history.Plan.Key.JobId, history.Plan.Key.PlanSha256, history.Plan.SyntheticData,
                isCurrent ? projection.Disposition : null, rows, ProjectMaintenance(history, checkpoint, verification)));
        }

        return result.ToArray();
    }

    private static DateTimeOffset LatestReviewTime(JournalHistory history) => history.Approvals
        .Select(approval => approval.OccurredAtUtc)
        .Concat(history.Entries.Where(entry => entry is { State: JournalState.Skipped, PriorState: JournalState.Planned })
            .Select(entry => entry.OccurredAtUtc))
        .DefaultIfEmpty()
        .Max();

    private bool IsCurrentPreparationPlan(StoredJob job, AssessmentSnapshot? assessment, JournalPlanSnapshot plan, DateTimeOffset now)
    {
        var gate = SanitizationGate.Inspect(job);
        if (!plan.IsCurrentPolicyPlan || plan.SyntheticData != IsFixtureScenario || !gate.AllowsAssessment ||
            gate.Decision?.DecisionId != plan.GateDecisionId ||
            (plan.FullMaintenance && plan.ReviewedEvidenceCount != job.SanitizationEvidence.Count + job.Observations.Count + job.HumanTests.Count) ||
            assessment is not { Outcome: AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview } ||
            assessment.GateDecisionId != plan.GateDecisionId || assessment.FinishedAtUtc > now)
        {
            return false;
        }

        var profile = VisiblePlanProfiles.FirstOrDefault(item => item.Profile.ContentSha256 == plan.ProfileSha256 &&
            item.Catalog.ContentSha256 == plan.CatalogSha256);
        if (profile is null)
        {
            return false;
        }

        var policy = profile.PackagePolicy ?? Enum.GetValues<PolicyDeviceClass>()
            .Select(deviceClass => _fixturePlanPolicy is null ? WorkshopPolicyCandidate.ForDeviceClass(deviceClass) : _fixturePlanPolicy(deviceClass))
            .FirstOrDefault(policy => policy?.Document.ContentSha256 == plan.PolicySha256);
        return policy?.Document.ContentSha256 == plan.PolicySha256 &&
            (plan.SyntheticData || (policy.Document.Effective.FromUtc <= now &&
                (policy.Document.Effective.UntilUtc is not { } until || now < until))) &&
            PlanResolver.MatchesEvidence(plan, AuditPolicyEvidence.Create(job, assessment, now), now);
    }

    private static WorkshopPreparationAction ProjectPreparationAction(PreparationActionOutcome action)
    {
        var state = action.State switch
        {
            JournalState.Verified => WorkshopPreparationState.Verified,
            JournalState.Skipped => WorkshopPreparationState.Skipped,
            JournalState.Failed when action.Entry?.Result == JournalResult.AuthorizationDeclined => WorkshopPreparationState.ConsentDeclined,
            JournalState.Failed => WorkshopPreparationState.Failed,
            JournalState.RolledBack => WorkshopPreparationState.RolledBack,
            _ => WorkshopPreparationState.Unverified,
        };
        var effect = action.Effect switch
        {
            PreparationActionEffect.Verified => WorkshopPreparationEffect.Verified,
            PreparationActionEffect.OptionalOmission => WorkshopPreparationEffect.OptionalOmission,
            PreparationActionEffect.ReviewRequired => WorkshopPreparationEffect.ReviewRequired,
            PreparationActionEffect.AuthorizedException => WorkshopPreparationEffect.AuthorizedException,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        return new WorkshopPreparationAction(action.Action.CapabilityId, action.Action.Intent == CapabilityIntent.Essential,
            state, effect, action.IsCurrent, action.Entry?.Result.ToString(), action.Entry?.OccurredAtUtc,
            state == WorkshopPreparationState.Skipped ? action.Entry?.ActorId : null, action.Exception);
    }
}
