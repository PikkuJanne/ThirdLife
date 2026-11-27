using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IPackageScheduleJournal
{
    public Task<JournalWriteResult> RecordPackageScheduleApprovalAsync(JournalApproval approval, ImmutableArray<JournalEntry> entries,
        CancellationToken cancellationToken = default) =>
        RecordPackageApprovalAsync(approval, entries, fullMaintenance: false, cancellationToken);

    public Task<JournalWriteResult> RecordMaintenanceApprovalAsync(JournalApproval approval, ImmutableArray<JournalEntry> entries,
        CancellationToken token = default) =>
        RecordPackageApprovalAsync(approval, entries, fullMaintenance: true, token);

    private async Task<JournalWriteResult> RecordPackageApprovalAsync(JournalApproval approval, ImmutableArray<JournalEntry> entries,
        bool fullMaintenance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ThrowIfDisposed();
        if (entries.IsDefaultOrEmpty)
        {
            throw new JobStoreConflictException();
        }

        var outcome = JournalWriteResult.AlreadyRecorded;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            var key = approval.Plan;
            _layout.ValidateJobDirectory(key.JobId);
            var plan = await RequireJournalPlanAsync(connection, transaction, key, token).ConfigureAwait(false);
            if (plan.FullMaintenance != fullMaintenance || plan.Schedule is not { } schedule || !JournalStateMachine.IsScheduledPackagePlan(plan) ||
                entries.Length > schedule.Steps.Length ||
                entries.Any(entry => entry is not { Checkpoint: JournalCheckpoint.PackagePlanApproval, State: JournalState.Approved } ||
                    entry.Plan != key || entry.ApprovalId != approval.ApprovalId) ||
                entries.Select(entry => entry.EntryId).Distinct().Count() != entries.Length)
            {
                throw new JobStoreConflictException();
            }

            var history = await ReadJournalHistoryAsync(transaction, plan, token).ConfigureAwait(false);
            if (await ReadApprovalAsync(connection, transaction, key, approval.ApprovalId, token).ConfigureAwait(false) is { } saved)
            {
                if (saved != approval || !history.Entries.Where(entry => entry.ApprovalId == approval.ApprovalId &&
                        entry.Checkpoint == JournalCheckpoint.PackagePlanApproval).SequenceEqual(entries))
                {
                    throw new JobStoreConflictException();
                }

                return;
            }

            var eligible = schedule.Steps.Select(step => (step.ActionId,
                    Previous: history.Entries.LastOrDefault(entry => entry.ActionId == step.ActionId)))
                .Where(action => JournalStateMachine.CanApprovePackageAction(action.Previous)).ToArray();
            if (!eligible.Select(action => action.ActionId).SequenceEqual(entries.Select(entry => entry.ActionId)))
            {
                throw new JobStoreConflictException();
            }

            await RequireJournalCapacityAsync(connection, transaction, key,
                "SELECT count(*) FROM action_journal_entries WHERE job_id = $job_id;", JournalHistory.MaximumEntriesPerJob,
                entries.Length, token).ConfigureAwait(false);

            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (!JournalStateMachine.CanAppend(plan, approval, eligible[index].Previous, entry) ||
                    await ReadJournalEntryByIdAsync(connection, transaction, entry, token).ConfigureAwait(false) is not null)
                {
                    throw new JobStoreConflictException();
                }
            }

            await InsertJournalApprovalAsync(connection, transaction, plan, approval, token).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                await InsertJournalEntryAsync(connection, transaction, entry, token).ConfigureAwait(false);
            }

            var maintenance = await ReadMaintenanceAsync(transaction, key.JobId, token).ConfigureAwait(false);
            if (fullMaintenance)
            {
                await StartMaintenanceAsync(transaction, plan, approval, maintenance, token).ConfigureAwait(false);
            }
            else if (maintenance is { Phase: not MaintenancePhase.FinalVerification })
            {
                throw new JobStoreConflictException();
            }
            outcome = JournalWriteResult.Recorded;
        }, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private static InstallPackageActionRequest? FindPackageJournalAction(JournalPlanSnapshot plan, Guid actionId)
    {
        if (plan.SyntheticData || (plan.Schedule is null ? plan.Actions.Length != 1 :
                !JournalStateMachine.IsScheduledPackagePlan(plan)))
        {
            return null;
        }

        return plan.Actions.FirstOrDefault(action => action.ActionId == actionId)?.Request is InstallPackageActionRequest
        { Scope: ActionScope.Machine, Architecture: ActionArchitecture.X64 } package ? package : null;
    }

    private async Task<bool> HasMatchingPackageDispatchAsync(SqliteConnection connection, SqliteTransaction transaction,
        JournalEntry outcome, JournalEntry? previous, CancellationToken token)
    {
        if (previous is null)
        {
            return false;
        }

        var started = previous.State == JournalState.Started ? previous : await ReadEntryAsync(connection, transaction, outcome.Plan,
            EntryProjection + "WHERE " + JournalPlanWhere + " AND action_id = $id AND state = " + (int)JournalState.Started +
            " ORDER BY entry_sequence DESC LIMIT 1;", outcome.ActionId, token).ConfigureAwait(false);
        return started is not null && PackageRetryPolicy.IsDispatchIntent(started.Checkpoint) &&
            started.ApprovalId == outcome.ApprovalId && started.CorrelationId == outcome.CorrelationId;
    }
}
