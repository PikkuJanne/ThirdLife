using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;

namespace ThirdLife.Actions.Execution;

/// <summary>Coordinates eligible package attempts using the exact saved plan and its approval.</summary>
public sealed class ScheduledPackageExecutor(IActionJournalStore journal)
{
    private readonly IActionJournalStore _journal = journal ?? throw new ArgumentNullException(nameof(journal));

    /// <summary>Runs one eligible application-owned attempt at a time.</summary>
    /// <remarks>The attempt must reserve its durable intent before dispatch and independently verify the result.</remarks>
    public async Task ExecuteAsync(JournalApproval approval, Func<InstallPackageActionRequest, bool> hasQualifiedRoute,
        Func<JournalHistory, InstallPackageActionRequest, CancellationToken, Task> attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(hasQualifiedRoute);
        ArgumentNullException.ThrowIfNull(attempt);
        var history = await _journal.LoadJournalAsync(approval.Plan, cancellationToken).ConfigureAwait(false);
        if (history?.ActiveApproval != approval || !JournalStateMachine.IsScheduledPackagePlan(history.Plan) ||
            history.Plan.Schedule is not { } schedule)
        {
            return;
        }

        foreach (var _ in schedule.Steps)
        {
            if (cancellationToken.IsCancellationRequested || history?.ActiveApproval != approval ||
                schedule.Project(history).NextForReview is not { } candidate ||
                history.Plan.Actions.Single(action => action.ActionId == candidate).Request is not InstallPackageActionRequest request ||
                !hasQualifiedRoute(request))
            {
                return;
            }

            await attempt(history, request, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            history = await _journal.LoadJournalAsync(approval.Plan, cancellationToken).ConfigureAwait(false);
            var latest = history?.Entries.LastOrDefault(entry => entry.ActionId == candidate);
            if (latest is not { State: JournalState.Verified or JournalState.Failed } ||
                latest.Result is JournalResult.AuthorizationDeclined or JournalResult.PackageDispatchAuthorizationChanged)
            {
                return;
            }
        }
    }
}
