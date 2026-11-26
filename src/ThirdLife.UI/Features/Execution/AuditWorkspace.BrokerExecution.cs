using System.Collections.Immutable;
using System.Globalization;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private readonly Func<IBrokerExecutionJournal, ExecutionCoordinator>? _fixtureBrokerCoordinator;
    private ExecutionCoordinator? _brokerExecution;
    private ImmutableArray<JournalHistory> _brokerHistories = [];
    private JournalPlanSnapshot? _brokerPlan;
    private JournalApproval? _brokerApproval;
    private CancellationTokenSource? _brokerCancellation;
    private string _brokerOperatorCode = string.Empty;
    private string? _brokerActivity;

    public bool HasBrokerPlan => _brokerPlan is not null;
    public bool HasBrokerHistory => !_brokerHistories.IsEmpty;
    public bool ShowsBrokerExecution => HasBrokerPlan || HasBrokerHistory;
    public bool CanEditBrokerOperator => CanEditJob && ShowsBrokerOperator && (PlanReviewDecision is null || ShowsActionSkipChoices);
    public bool CanRunBrokerExecution => CanEditJob && CanDispatchBrokerPlan &&
        _brokerApproval is not null && PlanReviewDecision is { Outcome: PlanReviewOutcome.Approved } &&
        (_brokerPlan?.Schedule is null || ApprovedScheduleProgress is { } projection && CanResumeScheduledPlan(projection));
    public bool CanCancelBrokerExecution => _brokerCancellation is { IsCancellationRequested: false };
    public bool CanCheckBrokerRecovery => CanEditJob && _brokerHistories.Any(history =>
        history.Plan.Actions.Any(action => history.StateOf(action.ActionId) == JournalState.Started));

    private bool CanDispatchBrokerPlan => _brokerPlan is { } plan && _brokerExecution?.CanDispatchPlan(plan) == true;

    private JournalHistory? DisplayedBrokerHistory => _brokerPlan is { } plan
        ? _brokerHistories.FirstOrDefault(history => history.Plan.Key == plan.Key) : null;

    private ActionScheduleProjection? ApprovedScheduleProgress => DisplayedBrokerHistory is
    { ActiveApproval: not null, Plan.Schedule: { } schedule } history ? schedule.Project(history) : null;

    private bool CanApproveBrokerPlan => _brokerPlan is { } plan && !string.IsNullOrWhiteSpace(_brokerOperatorCode) &&
        (DisplayedBrokerHistory is not { } saved ||
            (plan.Schedule is { } schedule
                ? !schedule.Project(saved).RequiresRecovery && saved.Plan.Actions.Any(action => CanReapprove(saved, action.ActionId))
                : saved.Plan.Actions.All(action => CanReapprove(saved, action.ActionId))));

    public string BrokerOperatorCode
    {
        get => _brokerOperatorCode;
        set
        {
            if (CanEditBrokerOperator && value != _brokerOperatorCode)
            {
                _brokerOperatorCode = value;
                Publish();
            }
        }
    }

    public string BrokerExecutionStatus => _brokerActivity ?? (CanDispatchBrokerPlan
        ? _brokerPlan?.Schedule is not null
            ? "Each scheduled test action may request Windows consent. The no-ops make no machine changes."
            : "Running the approved no-op requests Windows consent. The test actions make no machine changes."
        : "The no-op broker is unavailable in this build. Saved approvals and execution history remain accessible.");

    public string BrokerHistoryText { get; private set; } = string.Empty;

    public Task RunBrokerExecutionAsync()
    {
        if (!CanRunBrokerExecution || _brokerExecution is not { } execution ||
            _brokerApproval is not { } approval || PlanReview is not { } displayed)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var cancellation = new CancellationTokenSource();
            _brokerCancellation = cancellation;
            _brokerActivity = "Checking the approved no-op plan before requesting Windows consent.";
            _planReviewStatus = "Review the saved no-op outcome below.";
            Publish();

            async Task<bool> MatchesCurrentPlanAsync(CancellationToken token)
            {
                var current = await ResolveCurrentPlanAsync(token);
                return current is not null &&
                    !PlanDiff.Compare(displayed.Plan, displayed.Impact, current, ImpactEstimator.Estimate(current)).RequiresReapproval;
            }

            try
            {
                if (!await MatchesCurrentPlanAsync(cancellation.Token))
                {
                    ClearPlanReview();
                    _planReviewStatus = "The current evidence or plan changed. Build and approve a fresh no-op plan.";
                    return;
                }

                _brokerActivity = "Waiting for Windows consent and the no-op broker.";
                Publish();
                var progress = new Progress<NoOpExecutionProgress>(update =>
                {
                    if (_brokerCancellation == cancellation && !cancellation.IsCancellationRequested)
                    {
                        var action = displayed.Plan.Actions.FirstOrDefault(item => item.Request.ActionId == update.ActionId);
                        if (action is not null)
                        {
                            var name = PlanReviewPresentation.CapabilityName(action.CapabilityId);
                            _brokerActivity = update.Stage == NoOpProgressStage.Running
                                ? $"No-op in progress for {name}." : $"Broker accepted the no-op for {name}.";
                            Publish();
                        }
                    }
                });

                var outcome = await execution.ExecuteAsync(approval.Plan, approval.ApprovalId, progress,
                    validateCurrentPlan: MatchesCurrentPlanAsync, cancellationToken: cancellation.Token);
                _brokerActivity = DescribeBrokerOutcome(outcome, displayed.Plan.Schedule.Length > 0);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                _brokerActivity = "Cancellation finished. Check the saved outcome for each action.";
            }
            finally
            {
                _brokerApproval = null;
                PlanReviewDecision = null;
                await LoadBrokerHistoryAsync(approval.Plan.JobId, reconcile: false);
                RestoreScheduledApproval();
                await RefreshActionDecisionsAsync(approval.Plan.JobId);
            }
        });
    }

    public void CancelBrokerExecution()
    {
        if (CanCancelBrokerExecution)
        {
            _cancellationRequest = _brokerCancellation!.CancelAsync();
            _brokerActivity = "Cancellation requested. Waiting for the saved broker outcome.";
            Publish();
        }
    }

    public Task CheckBrokerRecoveryAsync() => !CanCheckBrokerRecovery ? Task.CompletedTask : RunAsync(async () =>
    {
        await LoadBrokerHistoryAsync(Job!.Job.JobId, reconcile: true);
        ClearReport();
        await RefreshActionDecisionsAsync(Job.Job.JobId);
        _brokerActivity = _brokerHistories.Any(history => history.Plan.Actions.Any(action =>
            history.StateOf(action.ActionId) == JournalState.Started))
            ? "The broker could still be active. Check recovery again after its dispatch window expires."
            : "Saved no-op execution history refreshed.";
    });

    private void SetBrokerPlan(PlanReviewSnapshot review)
    {
        _brokerPlan = _brokerExecution is not null && review.Plan.SyntheticData && review.Plan.BlockedReason is null &&
            review.Plan.Actions.Length is > 0 and <= IBrokerExecutionJournal.MaximumBatchActions
            ? JournalPlanSnapshot.Create(review.Plan, review.Impact) : null;
    }

    private void ClearBrokerPlan(bool clearSelection)
    {
        _brokerPlan = null;
        _brokerApproval = null;
        _brokerActivity = null;
        if (clearSelection)
        {
            _brokerOperatorCode = string.Empty;
        }
    }

    private void ClearBrokerHistory()
    {
        _brokerHistories = [];
        BrokerHistoryText = string.Empty;
    }

    private async Task SaveBrokerApprovalAsync()
    {
        var plan = _brokerPlan!;
        var actor = new OperatorId(_brokerOperatorCode.Trim());
        _brokerApproval = await _brokerExecution!.ApproveAsync(plan, actor);
        _brokerActivity = null;
        await LoadBrokerHistoryAsync(plan.Key.JobId, reconcile: false);
    }

    private void RestoreScheduledApproval()
    {
        if (PlanReview is not { } displayed || DisplayedBrokerHistory?.ActiveApproval is not { } approval ||
            ApprovedScheduleProgress is not { } projection)
        {
            return;
        }

        if (!CanResumeScheduledPlan(projection) &&
            !projection.Actions.All(action => action.State is JournalState.Verified or JournalState.Skipped))
        {
            return;
        }

        _brokerApproval = approval;
        PlanReviewDecision = new PlanReviewDecision(PlanReviewOutcome.Approved, displayed, approval.OccurredAtUtc);
    }

    private static bool CanResumeScheduledPlan(ActionScheduleProjection projection) => !projection.RequiresFreshApproval &&
        (projection.NextForReview is not null || projection.Actions.Any(action => action.State == JournalState.Applied));

    private string DescribeBrokerApproval() => _brokerApproval is null
        ? "Approval recorded for this synthetic plan. Device readiness still requires verification."
        : ApprovedScheduleProgress is { } projection && projection.Actions.All(action => action.State == JournalState.Verified)
            ? "All scheduled actions have saved independent verification."
            : ApprovedScheduleProgress is { } settled && settled.Actions.All(action => action.State is JournalState.Verified or JournalState.Skipped)
            ? "The scheduled actions have saved verification or skip outcomes. Review those outcomes in Verify."
            : CanDispatchBrokerPlan
            ? "Approval saved for this exact test plan. Select Run approved no-op to continue."
            : "Approval saved for this exact test plan. Broker execution is unavailable in this build.";

    private async Task LoadBrokerHistoryAsync(JobId jobId, bool reconcile)
    {
        if (_brokerExecution is not { } execution || _store is not IBrokerExecutionJournal journal || _hasSeparateEvidenceOrigin)
        {
            return;
        }

        var mayReconcile = reconcile && Job is { IsArchived: false };
        (_brokerHistories, BrokerHistoryText) = await Task.Run(async () =>
        {
            var histories = await journal.LoadJobJournalsAsync(jobId).ConfigureAwait(false);
            if (mayReconcile)
            {
                var started = histories.Where(history => HasBrokerEntries(history) && history.Plan.Actions.Any(action =>
                    history.StateOf(action.ActionId) == JournalState.Started)).ToArray();
                foreach (var history in started)
                {
                    await execution.ReconcileAsync(history.Plan.Key).ConfigureAwait(false);
                }

                if (started.Length != 0)
                {
                    histories = await journal.LoadJobJournalsAsync(jobId).ConfigureAwait(false);
                }
            }

            var brokerHistories = histories.Where(HasBrokerEntries).ToImmutableArray();
            return (brokerHistories, string.Join(Environment.NewLine + Environment.NewLine,
                brokerHistories.Reverse().Select(DescribeBrokerHistory)));
        });
    }

    private static bool HasBrokerEntries(JournalHistory history) =>
        history.Plan.Actions.All(action => action.Request is NoOpActionRequest) &&
        history.Entries.Any(entry => entry.Checkpoint == JournalCheckpoint.BrokerApproval);

    private static bool CanReapprove(JournalHistory history, Guid actionId) =>
        JournalStateMachine.CanApproveBrokerAction(history.Entries.LastOrDefault(entry => entry.ActionId == actionId));

    private static string DescribeBrokerHistory(JournalHistory history)
    {
        var projection = history.Plan.Schedule?.Project(history);
        var ids = projection is null ? history.Plan.Actions.Select(action => action.ActionId) :
            projection.Actions.Select(action => action.ActionId);
        var actions = ids.Select((id, index) =>
        {
            var latest = history.Entries.LastOrDefault(entry => entry.ActionId == id);
            var scheduled = projection?.Actions[index];
            var outcome = (latest?.State, latest?.Result) switch
            {
                (JournalState.Approved, _) when scheduled is { EarlierPhasePending: true } =>
                    "Approved. Waiting for verification of the earlier phase.",
                (JournalState.Approved, _) when scheduled is { PendingDependencies.IsEmpty: false } =>
                    "Approved. Waiting for verification of prerequisite actions.",
                (JournalState.Approved, _) => "Approved. No action started.",
                (JournalState.Started, _) => "Started. Waiting for a final result or recovery.",
                (JournalState.Applied, _) => "Broker reported completion. Independent verification is pending.",
                (JournalState.Verified, _) => "Independent verification recorded.",
                (JournalState.Skipped, _) => "Operator chose to skip this action.",
                (JournalState.Failed, JournalResult.AuthorizationDeclined) => "Windows consent declined. No action was sent. Review and approve again to retry.",
                (JournalState.Failed, JournalResult.BackendUnavailable) => "Broker unavailable. No action was sent.",
                (JournalState.Failed, JournalResult.Cancelled) => "Cancelled before dispatch. No action was sent.",
                (JournalState.Failed, JournalResult.NoChangeConfirmed) => "Recovery confirmed no change.",
                (JournalState.RequiresReview, JournalResult.Cancelled) => "Cancellation after dispatch needs review. No automatic retry.",
                (JournalState.RequiresReview, _) => "Execution needs review. No automatic retry.",
                (JournalState.Failed, _) => "Broker reported failure.",
                _ => "No action started.",
            };
            return string.Create(CultureInfo.CurrentCulture, $"Action {index + 1}: {outcome}");
        });
        return $"Plan SHA-256 {history.Plan.Key.PlanSha256}{Environment.NewLine}{string.Join(Environment.NewLine, actions)}";
    }

    private static string DescribeBrokerOutcome(NoOpExecutionStatus outcome, bool scheduled) => outcome switch
    {
        NoOpExecutionStatus.ApprovalRequired => "The saved approval or plan changed. Review the current plan before retrying.",
        NoOpExecutionStatus.AlreadyStarted => "A saved no-op action has started. Check the saved history for its result or recovery.",
        NoOpExecutionStatus.Declined when scheduled =>
            "Windows consent was declined for the current action. Review the saved results and approve again to continue.",
        NoOpExecutionStatus.Declined => "Windows consent was declined. No action was sent. Review and approve again to retry.",
        NoOpExecutionStatus.Cancelled => "Cancellation finished. Check the saved outcome for each action.",
        NoOpExecutionStatus.Completed when scheduled => "All scheduled actions have saved independent verification.",
        NoOpExecutionStatus.Completed => "The broker returned results. Check the saved outcome for each action.",
        NoOpExecutionStatus.CompletedWithOmissions => "Essential actions have saved verification. Review the recorded optional outcomes.",
        NoOpExecutionStatus.Failed => "The broker reported a failure. Check the saved outcome for each action.",
        NoOpExecutionStatus.RequiresReview when scheduled => "Check the saved actions for pending verification or recovery.",
        NoOpExecutionStatus.RequiresReview => "The broker stopped before confirming the outcome. Check the saved execution history.",
        _ => "The broker is unavailable. Review the saved execution history.",
    };
}
