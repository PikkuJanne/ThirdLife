using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Jobs;
using ThirdLife.Packages;
using ThirdLife.Policy.Schema;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private PackageRetryEligibility _savedPackageRetry;
    private CancellationTokenSource? _packageRetryRefresh;

    public bool ShowPackageRetry => CanUsePackageRecovery && RetryProfile is not null &&
        SavedPackageRetry.Status is PackageRetryStatus.Ready or PackageRetryStatus.CoolingDown;

    public bool CanRetryPackage => CanReviewPlan && ShowPackageRetry && !_packageNeedsReview &&
        SavedPackageRetry.Status == PackageRetryStatus.Ready;

    public string PackageRetryStatusText => SavedPackageRetry.Status switch
    {
        PackageRetryStatus.CoolingDown when ShowPackageRetry => "Wait briefly before retrying the saved package attempt.",
        PackageRetryStatus.Ready when CanRetryPackage => "You can retry the saved package. ThirdLife will check the current plan and approval first.",
        PackageRetryStatus.Exhausted => "This saved job has reached its package attempt limit. Review its history.",
        PackageRetryStatus.Verified => "The saved plan already has an independently verified result.",
        _ => string.Empty,
    };

    public bool HasPackageRetryStatus => PackageRetryStatusText.Length != 0;

    private PlanProfileOption? RetryProfile => PlanProfiles.SingleOrDefault(option =>
        option.PackagePolicy?.Document.Target.DeviceClass == (RepairDeviceClass ?? PolicyDeviceClass.Laptop));

    private PackageRetryEligibility SavedPackageRetry => _savedPackageRetry.RetryAtUtc is { } readyAt
        ? _savedPackageRetry with
        {
            Status = _timeProvider.GetUtcNow() < readyAt ? PackageRetryStatus.CoolingDown : PackageRetryStatus.Ready,
        }
        : _savedPackageRetry;

    private void RememberPackageRetry(ImmutableArray<JournalHistory> histories)
    {
        var target = histories.Select(PackageHistoryRequest).FirstOrDefault(request =>
            request is { Architecture: ActionArchitecture.X64 } && request.ExactVersion == PilotPackageService.Identity.ExactVersion);
        _savedPackageRetry = target is null ? default : EvaluatePackageRetry(histories, target);
    }

    private PackageRetryEligibility EvaluatePackageRetry(IReadOnlyList<JournalHistory> histories, InstallPackageActionRequest request) =>
        PackageRetryPolicy.Evaluate(histories, request, _timeProvider.GetUtcNow());

    private bool CanStartPackagePlan => SavedPackageRetry.Status is PackageRetryStatus.Initial or PackageRetryStatus.Ready;

    private Task<ImmutableArray<JournalHistory>> LoadPackageJournalsAsync(JobId jobId, CancellationToken cancellationToken) =>
        _store is IActionJournalStore journal && !IsFixtureScenario
            ? Task.Run(() => journal.LoadJobJournalsAsync(jobId, cancellationToken), cancellationToken)
            : Task.FromResult(ImmutableArray<JournalHistory>.Empty);

    public Task RetryPackageAsync()
    {
        if (!CanRetryPackage || RetryProfile is not { } profile ||
            SavedPackageRetry.LatestAttemptEntry is not { } expected ||
            _store is not IActionJournalStore journal || _packageService is not { } service)
        {
            return Task.CompletedTask;
        }

        var declined = IsPackageProfile && PlanReviewDecision?.Outcome == PlanReviewOutcome.Declined;
        SelectedPlanProfile = profile;
        return RunAsync(async () =>
        {
            var current = await ResolveCurrentPlanAsync();
            if (current is null || PackageRequest(current) is not { } request)
            {
                ClearPlanReview();
                _planReviewStatus = "The current assessment or package plan is unavailable. Review this job before continuing.";
                return;
            }

            var histories = await LoadPackageJournalsAsync(expected.Plan.JobId, CancellationToken.None);
            var eligibility = EvaluatePackageRetry(histories, request);
            if (eligibility.Status != PackageRetryStatus.Ready || eligibility.LatestAttemptEntry != expected)
            {
                await LoadPackageHistoryAsync(expected.Plan.JobId);
                _planReviewStatus = "The saved package attempt changed. Review its current history.";
                return;
            }

            var displayed = PlanReviewPresentation.Describe(current);
            var key = JournalPlanSnapshot.Create(current, displayed.Impact).Key;
            var saved = histories.SingleOrDefault(history => history.Plan.Key == key);
            var approval = saved?.ActiveApproval;
            var previous = saved?.Entries.LastOrDefault();
            if (declined || approval is null || !IsFreshPackageApproval(approval) || previous?.ApprovalId != approval.ApprovalId ||
                (previous.State != JournalState.Approved &&
                    (previous != expected || expected.Result == JournalResult.PackageDispatchAuthorizationChanged)))
            {
                ClearPlanReview();
                await CheckPackageAsync();
                PresentPlan(displayed);
                _planReviewStatus = key == expected.Plan
                    ? "Review and approve the package plan again before retrying."
                    : "The approved resolution changed. Review and approve the current package plan before retrying.";
                return;
            }

            _packageApproval = null;
            _packageCancellation = new CancellationTokenSource();
            await StartPackageAttemptAsync(journal, service, approval, displayed, current, request, previous, eligibility,
                _packageCancellation.Token);
        });
    }

    private void RefreshPackageRetryAfterDelay()
    {
        StopPackageRetryRefresh();
        if (!_closing && CanUsePackageRecovery && SavedPackageRetry is
            { Status: PackageRetryStatus.CoolingDown, RetryAtUtc: { } retryAt })
        {
            var delay = retryAt - _timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(15))
            {
                _packageRetryRefresh = new CancellationTokenSource();
                _ = PublishPackageRetryAfterDelayAsync(delay, _packageRetryRefresh.Token);
            }
        }
    }

    private async Task PublishPackageRetryAfterDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, token);
            if (!token.IsCancellationRequested)
            {
                Publish();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void StopPackageRetryRefresh()
    {
        _packageRetryRefresh?.Cancel();
        _packageRetryRefresh?.Dispose();
        _packageRetryRefresh = null;
    }
}
