using ThirdLife.Actions.Journal;

namespace ThirdLife.Actions.Recovery;

public sealed record PackageRebootCandidate(RebootResumeCandidate Candidate, bool CanVerify, bool IsExpired);

/// <summary>Records independent observations against one previously applied package attempt.</summary>
public interface IPackageRecoveryStore
{
    Task<PackageRebootCandidate?> LoadPackageRebootCheckpointAsync(
        JournalEntry restartEntry, CancellationToken cancellationToken = default);

    Task<bool> RecordPackageRebootVerificationAsync(
        JournalEntry verification, RebootResumeToken token, Guid checkpointId,
        CancellationToken cancellationToken = default);

    Task<bool> RecordPackageRecoveryVerificationAsync(
        JournalEntry prior, JournalEntry verification, CancellationToken cancellationToken = default);
}
