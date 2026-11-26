using ThirdLife.Actions.Journal;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IActionDecisionJournal
{
    public async Task<JournalWriteResult> RecordActionDeclineAsync(JournalPlanSnapshot plan, JournalApproval? approval,
        Guid? actionId, OperatorId operatorId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operatorId);
        ArgumentOutOfRangeException.ThrowIfEqual(occurredAtUtc, default);
        if (actionId == Guid.Empty || (approval is null) != (actionId is null) ||
            (approval is not null && approval.Plan != plan.Key))
        {
            throw new ArgumentException("Choose a whole unapproved plan or one action from the exact approved plan.");
        }

        ThrowIfDisposed();
        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(plan.Key.JobId);
            await RequireCurrentJournalPlanAsync(connection, transaction, plan, token).ConfigureAwait(false);
            if (approval is null)
            {
                await SaveJournalPlanAsync(connection, transaction, plan, token).ConfigureAwait(false);
            }

            var stored = await RequireJournalPlanAsync(connection, transaction, plan.Key, token).ConfigureAwait(false);
            var history = await ReadJournalHistoryAsync(transaction, stored, token).ConfigureAwait(false);
            var targets = stored.Actions.Where(action => actionId is null || action.ActionId == actionId)
                .Select(action => (action.ActionId, Latest: history.Entries.LastOrDefault(entry => entry.ActionId == action.ActionId)))
                .ToArray();
            if (targets.Length == 0 || (approval is null ? !history.Approvals.IsEmpty :
                    stored.Schedule is null || history.ActiveApproval != approval) ||
                occurredAtUtc > _timeProvider.GetUtcNow())
            {
                throw new JobStoreConflictException();
            }

            if (targets.All(target => target.Latest is { State: JournalState.Skipped, Result: JournalResult.Declined } skipped &&
                    skipped.ApprovalId == approval?.ApprovalId && skipped.ActorId == operatorId &&
                    skipped.CorrelationId == targets[0].Latest?.CorrelationId))
            {
                return;
            }

            if (targets.Any(target => approval is null ? target.Latest is not null :
                    !JournalStateMachine.CanSkipApprovedAction(target.Latest, approval)) ||
                await HasUnreconciledJournalActionAsync(connection, transaction, plan.Key, token).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }

            await RequireJournalCapacityAsync(connection, transaction, plan.Key,
                "SELECT count(*) FROM action_journal_entries WHERE job_id = $job_id;", JournalHistory.MaximumEntriesPerJob,
                targets.Length, token).ConfigureAwait(false);
            var correlation = Guid.NewGuid();
            foreach (var target in targets)
            {
                var entry = new JournalEntry(Guid.NewGuid(), plan.Key, target.ActionId, approval?.ApprovalId,
                    target.Latest?.State ?? JournalState.Planned, JournalState.Skipped, JournalActor.Operator,
                    operatorId, correlation, occurredAtUtc, JournalResult.Declined);
                if (!JournalStateMachine.CanAppend(stored, approval, target.Latest, entry))
                {
                    throw new JobStoreConflictException();
                }

                await InsertJournalEntryAsync(connection, transaction, entry, token).ConfigureAwait(false);
            }

            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }
}
