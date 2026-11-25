using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Actions.Recovery;

/// <summary>Records and consumes explicit decisions for read-only restart review.</summary>
public interface IRebootCheckpointStore
{
    Task<RebootResumeCandidate?> IssueRebootCheckpointAsync(
        JournalEntry restartEntry, CancellationToken cancellationToken = default);

    /// <summary>Loads an authenticated pending checkpoint, including an expired one available to abandon.</summary>
    Task<RebootResumeCandidate?> LoadRebootCheckpointAsync(
        JobId jobId, Guid checkpointId, CancellationToken cancellationToken = default);

    /// <summary>Lists pending checkpoints for the selected job and the current store user.</summary>
    Task<ImmutableArray<RebootResumeCandidate>> ListPendingRebootCheckpointsAsync(
        JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Loads the latest completed recovery decision for review of the selected job.</summary>
    Task<RebootRecoveryDecision?> LoadLatestRebootDecisionAsync(
        JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a current checkpoint or abandons an authenticated pending checkpoint once.</summary>
    Task<RebootCheckpoint?> DecideRebootCheckpointAsync(
        JobId jobId,
        Guid checkpointId,
        RebootResumeToken token,
        RebootDisposition disposition,
        CancellationToken cancellationToken = default);
}

public enum RebootDisposition
{
    Resume = 1,
    Abandon,
}

public sealed record RebootRecoveryDecision
{
    [JsonConstructor]
    public RebootRecoveryDecision(RebootCheckpoint checkpoint, RebootDisposition disposition, DateTimeOffset decidedAtUtc)
    {
        Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        if (!Enum.IsDefined(disposition) || decidedAtUtc < checkpoint.IssuedAtUtc ||
            (disposition == RebootDisposition.Resume && !checkpoint.IsCurrent(decidedAtUtc)))
        {
            throw new ArgumentException("The recovery decision is outside the checkpoint contract.");
        }

        Disposition = disposition;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
    }

    public RebootCheckpoint Checkpoint { get; }
    public RebootDisposition Disposition { get; }
    public DateTimeOffset DecidedAtUtc { get; }
}

/// <summary>A bounded, opaque proof of checkpoint authenticity.</summary>
public sealed class RebootResumeToken
{
    public const int MaximumBytes = 2_048;

    public RebootResumeToken(ReadOnlyMemory<byte> authenticityBlob)
    {
        if (authenticityBlob.Length is < 1 or > MaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(authenticityBlob), "The reboot authenticator is outside its size limit.");
        }

        AuthenticityBlob = ImmutableArray.Create(authenticityBlob.Span);
    }

    public ImmutableArray<byte> AuthenticityBlob { get; }

    public override string ToString() => "[reboot_resume_token]";
}

public sealed class RebootResumeCandidate
{
    public RebootResumeCandidate(RebootCheckpoint checkpoint, RebootResumeToken token)
    {
        Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        Token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public RebootCheckpoint Checkpoint { get; }
    public RebootResumeToken Token { get; }
}
