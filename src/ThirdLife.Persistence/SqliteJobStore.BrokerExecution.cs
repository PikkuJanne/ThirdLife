using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore
{
    public async Task<JournalWriteResult> RecordBrokerApprovalAsync(JournalApproval approval,
        ImmutableArray<JournalEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var first = RequireCorrelatedBrokerBatch(entries);
        if (first.Plan != approval.Plan || first.ApprovalId != approval.ApprovalId ||
            first.CorrelationId != approval.CorrelationId || entries.Any(entry => entry.Checkpoint != JournalCheckpoint.BrokerApproval))
        {
            throw new JobStoreConflictException();
        }

        ThrowIfDisposed();
        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(first.Plan.JobId);
            var plan = await RequireJournalPlanAsync(connection, transaction, first.Plan, token).ConfigureAwait(false);
            RequireBrokerPlan(plan, entries, wholePlan: plan.Schedule is null);
            var recordedApproval = await ReadApprovalAsync(connection, transaction, first.Plan, approval.ApprovalId, token).ConfigureAwait(false);
            var recordedEntries = await AreBrokerEntriesRecordedAsync(connection, transaction, entries, token).ConfigureAwait(false);
            if (plan.Schedule is { } schedule)
            {
                var history = await ReadJournalHistoryAsync(transaction, plan, token).ConfigureAwait(false);
                var expected = recordedApproval is null
                    ? schedule.Steps.Select(step => step.ActionId).Where(id => JournalStateMachine.CanApproveBrokerAction(
                        history.Entries.LastOrDefault(entry => entry.ActionId == id)))
                    : history.Entries.Where(entry => entry.ApprovalId == approval.ApprovalId &&
                        entry.Checkpoint == JournalCheckpoint.BrokerApproval).Select(entry => entry.ActionId);
                if (!expected.SequenceEqual(entries.Select(entry => entry.ActionId)))
                {
                    throw new JobStoreConflictException();
                }
            }

            if (recordedApproval is not null || recordedEntries)
            {
                if (recordedApproval != approval || !recordedEntries)
                {
                    throw new JobStoreConflictException();
                }

                return;
            }

            await InsertJournalApprovalAsync(connection, transaction, plan, approval, token).ConfigureAwait(false);
            await InsertBrokerEntriesAsync(connection, transaction, plan, approval, entries, isOutcome: false, token).ConfigureAwait(false);
            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<JournalWriteResult> AppendBrokerBatchAsync(ImmutableArray<JournalEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var first = RequireCorrelatedBrokerBatch(entries);
        var isDispatch = first.Checkpoint == JournalCheckpoint.BrokerDispatchIntent;
        if (isDispatch ? entries.Any(entry => entry.Checkpoint != JournalCheckpoint.BrokerDispatchIntent) :
            entries.Any(entry => !IsBrokerOutcome(entry)))
        {
            throw new JobStoreConflictException();
        }

        ThrowIfDisposed();
        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(first.Plan.JobId);
            var plan = await RequireJournalPlanAsync(connection, transaction, first.Plan, token).ConfigureAwait(false);
            RequireBrokerPlan(plan, entries, wholePlan: isDispatch && plan.Schedule is null);
            if (plan.Schedule is not null && entries.Length != 1)
            {
                throw new JobStoreConflictException();
            }

            if (await AreBrokerEntriesRecordedAsync(connection, transaction, entries, token).ConfigureAwait(false))
            {
                return;
            }

            var approval = await ReadApprovalAsync(connection, transaction, first.Plan,
                isDispatch ? null : first.ApprovalId, token).ConfigureAwait(false);
            if (approval is null || approval.Plan != first.Plan || approval.ApprovalId != first.ApprovalId)
            {
                throw new JobStoreConflictException();
            }

            if (isDispatch)
            {
                await RequireCurrentJournalPlanAsync(connection, transaction, plan, token).ConfigureAwait(false);
                if (await HasUnreconciledJournalActionAsync(connection, transaction, first.Plan, token).ConfigureAwait(false))
                {
                    throw new JobStoreConflictException();
                }

                if (plan.Schedule is { } schedule)
                {
                    var history = await ReadJournalHistoryAsync(transaction, plan, token).ConfigureAwait(false);
                    if (schedule.Project(history).NextForReview != first.ActionId || history.Entries.Any(entry =>
                        entry.Checkpoint == JournalCheckpoint.BrokerDispatchIntent && entry.CorrelationId == first.CorrelationId))
                    {
                        throw new JobStoreConflictException();
                    }
                }
            }

            await InsertBrokerEntriesAsync(connection, transaction, plan, approval, entries, isOutcome: !isDispatch, token).ConfigureAwait(false);
            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private static JournalEntry RequireCorrelatedBrokerBatch(ImmutableArray<JournalEntry> entries)
    {
        if (entries.IsDefaultOrEmpty || entries.Length > IBrokerExecutionJournal.MaximumBatchActions ||
            entries[0] is not { ApprovalId: not null } first ||
            entries.Any(entry => entry is null || entry.Plan != first.Plan || entry.ApprovalId != first.ApprovalId ||
                entry.CorrelationId != first.CorrelationId) ||
            entries.Select(entry => entry.ActionId).Distinct().Count() != entries.Length ||
            entries.Select(entry => entry.EntryId).Distinct().Count() != entries.Length)
        {
            throw new JobStoreConflictException();
        }

        return first;
    }

    private static bool IsBrokerOutcome(JournalEntry entry) =>
        entry.PriorState is JournalState.Started or JournalState.Applied or JournalState.RequiresReview &&
        entry.State is JournalState.Applied or JournalState.Verified or JournalState.Failed or JournalState.RequiresReview &&
        entry.Checkpoint is JournalCheckpoint.None or JournalCheckpoint.Reconcile or JournalCheckpoint.BrokerPreDispatchFailure;

    private static void RequireBrokerPlan(JournalPlanSnapshot plan, ImmutableArray<JournalEntry> entries, bool wholePlan)
    {
        if (plan.Actions.Length > IBrokerExecutionJournal.MaximumBatchActions ||
            plan.Actions.Any(action => action.Request is not NoOpActionRequest || action.Definition.Availability != ActionAvailability.BrokerNoOp) ||
            (plan.Schedule is not null && (!plan.SyntheticData ||
                plan.Actions.Any(action => action.Definition.Verification != ActionVerification.NoMachineChange))) ||
            (wholePlan && !plan.Actions.Select(action => action.ActionId).SequenceEqual(entries.Select(entry => entry.ActionId))))
        {
            throw new JobStoreConflictException();
        }
    }

    private async Task<bool> AreBrokerEntriesRecordedAsync(SqliteConnection connection, SqliteTransaction transaction,
        ImmutableArray<JournalEntry> entries, CancellationToken token)
    {
        var recorded = 0;
        foreach (var entry in entries)
        {
            if (await ReadJournalEntryByIdAsync(connection, transaction, entry, token).ConfigureAwait(false) is { } existing)
            {
                if (existing != entry)
                {
                    throw new JobStoreConflictException();
                }

                recorded++;
            }
        }

        if (recorded != 0 && recorded != entries.Length)
        {
            throw new JobStoreConflictException();
        }

        return recorded != 0;
    }

    private async Task InsertBrokerEntriesAsync(SqliteConnection connection, SqliteTransaction transaction, JournalPlanSnapshot plan,
        JournalApproval approval, ImmutableArray<JournalEntry> entries, bool isOutcome, CancellationToken token)
    {
        // Leave room for a separate apply, verification or recovery record after every committed start.
        var requiredSlots = entries[0].Checkpoint == JournalCheckpoint.BrokerDispatchIntent ? entries.Length * 4 : entries.Length;
        await RequireJournalCapacityAsync(connection, transaction, plan.Key,
            "SELECT count(*) FROM action_journal_entries WHERE job_id = $job_id;", JournalHistory.MaximumEntriesPerJob,
            requiredSlots, token).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            var previous = await ReadLatestActionEntryAsync(connection, transaction, entry, token).ConfigureAwait(false);
            if (!JournalStateMachine.CanAppend(plan, approval, previous, entry) ||
                (isOutcome && !await HasMatchingBrokerDispatchAsync(connection, transaction, entry, previous, token).ConfigureAwait(false)))
            {
                throw new JobStoreConflictException();
            }

            await InsertJournalEntryAsync(connection, transaction, entry, token).ConfigureAwait(false);
        }
    }

    private async Task<bool> HasMatchingBrokerDispatchAsync(SqliteConnection connection, SqliteTransaction transaction,
        JournalEntry entry, JournalEntry? previous, CancellationToken token)
    {
        if (previous is null)
        {
            return false;
        }

        var started = previous.State == JournalState.Started ? previous : await ReadEntryAsync(connection, transaction, entry.Plan,
            EntryProjection + "WHERE " + JournalPlanWhere + " AND action_id = $id AND state = " + (int)JournalState.Started +
            " ORDER BY entry_sequence DESC LIMIT 1;", entry.ActionId, token).ConfigureAwait(false);
        return started is { Checkpoint: JournalCheckpoint.BrokerDispatchIntent } && started.ApprovalId == entry.ApprovalId &&
            started.CorrelationId == entry.CorrelationId;
    }
}
