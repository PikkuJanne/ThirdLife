using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed record ActionSkipChoice(Guid ActionId, string Label, string Consequence);

internal sealed partial class AuditWorkspace
{
    private JournalHistory? _displayedActionDecisionHistory;

    private bool CanRecordDisplayedActionDecision => _store is IActionDecisionJournal &&
        PlanReview?.Plan is { BlockedReason: null, Actions.IsEmpty: false };

    private bool HasActionDecisionOperator => !string.IsNullOrWhiteSpace(IsPackageProfile ? _packageOperatorCode : _brokerOperatorCode);

    public bool ShowsBrokerOperator => HasBrokerPlan || (!IsPackageProfile && CanRecordDisplayedActionDecision);
    public bool ShowsActionSkipChoices => ActionSkipChoices.Count != 0;
    public bool CanSkipApprovedAction => CanReviewPlan && ShowsActionSkipChoices && HasActionDecisionOperator;

    public IReadOnlyList<ActionSkipChoice> ActionSkipChoices
    {
        get
        {
            if (!CanRecordDisplayedActionDecision || _displayedActionDecisionHistory is not
                { ActiveApproval: { } approval, Plan.Schedule: not null } history)
            {
                return [];
            }

            return history.Plan.Actions.Where(action => JournalStateMachine.CanSkipApprovedAction(
                    history.Entries.LastOrDefault(entry => entry.ActionId == action.ActionId), approval))
                .Select(action => new ActionSkipChoice(action.ActionId,
                    $"Skip {PlanReviewPresentation.CapabilityName(action.CapabilityId)}",
                    action.Intent == CapabilityIntent.Essential
                        ? "Essential. Skipping this action requires a readiness review."
                        : "Optional. Any action that depends on it will wait."))
                .ToArray();
        }
    }

    public Task SkipApprovedActionAsync(Guid actionId) => CanSkipApprovedAction && PlanReview is { } displayed &&
        _displayedActionDecisionHistory?.ActiveApproval is { } approval && ActionSkipChoices.Any(choice => choice.ActionId == actionId)
        ? RecordActionDeclineAsync(displayed, approval, actionId)
        : Task.CompletedTask;

    private Task RecordActionDeclineAsync(PlanReviewSnapshot displayed, JournalApproval? approval, Guid? actionId) =>
        RunAsync(async () =>
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
                await LoadDisplayedActionDecisionAsync(current);
                PresentPlan(current, difference.PlainLanguage);
                return;
            }

            var snapshot = JournalPlanSnapshot.Create(displayed.Plan, displayed.Impact);
            var actor = new OperatorId((IsPackageProfile ? _packageOperatorCode : _brokerOperatorCode).Trim());
            await ((IActionDecisionJournal)_store!).RecordActionDeclineAsync(snapshot, approval, actionId, actor, _timeProvider.GetUtcNow());
            ClearReport();
            await RefreshActionDecisionsAsync(snapshot.Key.JobId);
            if (actionId is not { } skipped)
            {
                RecordPlanReviewDecision(PlanReviewOutcome.Declined, displayed);
                _planReviewStatus = "Plan decline saved. None of its actions started.";
                return;
            }

            var action = snapshot.Actions.Single(item => item.ActionId == skipped);
            var name = PlanReviewPresentation.CapabilityName(action.CapabilityId);
            _planReviewStatus = action.Intent == CapabilityIntent.Essential
                ? $"Skip saved for {name}. This essential action requires a readiness review."
                : $"Skip saved for {name}. Any dependent action still requires verification of {name}.";
            if (IsPackageProfile)
            {
                _packageApproval = null;
                await LoadPackageHistoryAsync(snapshot.Key.JobId);
            }
            else
            {
                await LoadBrokerHistoryAsync(snapshot.Key.JobId, reconcile: false);
            }
        });

    private async Task LoadDisplayedActionDecisionAsync(PlanReviewSnapshot displayed)
    {
        _displayedActionDecisionHistory = _store is IActionDecisionJournal journal &&
            displayed.Plan is { BlockedReason: null, Actions.IsEmpty: false }
            ? await Task.Run(() => journal.LoadJournalAsync(JournalPlanSnapshot.Create(displayed.Plan, displayed.Impact).Key))
            : null;
    }

    private async Task RefreshActionDecisionsAsync(JobId jobId)
    {
        if (PlanReview is { } displayed)
        {
            await LoadDisplayedActionDecisionAsync(displayed);
        }

        await RefreshPreparationDispositionAsync(jobId);
    }

    private bool RestorePlanDecline(PlanReviewSnapshot displayed)
    {
        if (_displayedActionDecisionHistory is not { Approvals.IsEmpty: true, Entries.IsEmpty: false } history ||
            history.Plan.Actions.Any(action => history.StateOf(action.ActionId) != JournalState.Skipped))
        {
            return false;
        }

        PlanReviewDecision = new PlanReviewDecision(PlanReviewOutcome.Declined, displayed, history.Entries[^1].OccurredAtUtc);
        _planReviewStatus = "Plan decline saved. None of its actions started.";
        return true;
    }
}
