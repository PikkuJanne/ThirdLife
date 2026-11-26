using System.Collections.Immutable;

namespace ThirdLife.Actions.Journal;

/// <summary>Atomically records the approval and eligible action entries for an exact saved package schedule.</summary>
public interface IPackageScheduleJournal : IActionJournalStore
{
    Task<JournalWriteResult> RecordPackageScheduleApprovalAsync(JournalApproval approval, ImmutableArray<JournalEntry> entries,
        CancellationToken cancellationToken = default);
}
