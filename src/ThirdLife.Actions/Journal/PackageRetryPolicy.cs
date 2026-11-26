using ThirdLife.Actions.Contracts;

namespace ThirdLife.Actions.Journal;

public enum PackageRetryStatus
{
    Initial,
    Ready,
    CoolingDown,
    Exhausted,
    RequiresReview,
    Verified,
    Blocked,
}

public readonly record struct PackageRetryEligibility(
    PackageRetryStatus Status,
    int AttemptCount,
    JournalEntry? LatestAttemptEntry = null,
    DateTimeOffset? RetryAtUtc = null);

/// <summary>Evaluates package starts and their outcomes in one saved job.</summary>
public static class PackageRetryPolicy
{
    public const int MaximumAttempts = 3;
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(10);

    public static bool IsFreshApproval(JournalApproval approval, DateTimeOffset now)
    {
        var age = now - approval.OccurredAtUtc;
        return age >= TimeSpan.Zero && age < ApprovalLifetime;
    }

    public static bool IsDispatchIntent(JournalCheckpoint checkpoint) =>
        checkpoint is JournalCheckpoint.PackageDispatchIntent or JournalCheckpoint.PackageRetryDispatchIntent;

    public static bool IsRetryableFailure(JournalEntry? entry) => entry is
    {
        State: JournalState.Failed,
        Actor: JournalActor.PackageBackend,
        Checkpoint: JournalCheckpoint.PackagePreDispatchFailure,
        Result: JournalResult.NetworkUnavailable or JournalResult.Cancelled or JournalResult.BackendUnavailable or
            JournalResult.PackageDispatchAuthorizationChanged,
    };

    public static PackageRetryEligibility Evaluate(IReadOnlyList<JournalHistory> histories,
        InstallPackageActionRequest target, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(histories);
        ArgumentNullException.ThrowIfNull(target);

        var attemptCount = 0;
        JournalEntry? latestStart = null;
        JournalEntry? latestResult = null;
        JournalEntry? unresolved = null;
        JournalEntry? verified = null;
        var ambiguousOrder = false;
        foreach (var history in histories)
        {
            if (history.Plan is not { SyntheticData: false, Actions: [{ Request: InstallPackageActionRequest package }] } ||
                !MatchesTarget(package, target))
            {
                continue;
            }

            JournalEntry? start = null;
            JournalEntry? result = null;
            foreach (var entry in history.Entries)
            {
                if (IsDispatchIntent(entry.Checkpoint))
                {
                    attemptCount++;
                    start = result = entry;
                }
                else if (start is not null && entry.CorrelationId == start.CorrelationId && entry.State != JournalState.Approved)
                {
                    result = entry;
                }
            }

            if (start is null || result is null)
            {
                continue;
            }

            if (result.State is JournalState.Started or JournalState.Applied or JournalState.RequiresReview)
            {
                unresolved = result;
            }
            else if (result.State == JournalState.Verified)
            {
                verified = result;
            }

            if (latestStart is null || start.OccurredAtUtc > latestStart.OccurredAtUtc)
            {
                latestStart = start;
                latestResult = result;
                ambiguousOrder = false;
            }
            else if (start.OccurredAtUtc == latestStart.OccurredAtUtc)
            {
                ambiguousOrder = true;
            }
        }

        if (unresolved is not null)
        {
            return new(PackageRetryStatus.RequiresReview, attemptCount, unresolved);
        }

        if (verified is not null)
        {
            return new(PackageRetryStatus.Verified, attemptCount, verified);
        }

        if (latestResult is null)
        {
            return new(PackageRetryStatus.Initial, attemptCount);
        }

        if (ambiguousOrder || !IsRetryableFailure(latestResult))
        {
            return new(PackageRetryStatus.Blocked, attemptCount, latestResult);
        }

        if (attemptCount >= MaximumAttempts)
        {
            return new(PackageRetryStatus.Exhausted, attemptCount, latestResult);
        }

        var delay = TimeSpan.FromSeconds(attemptCount == 1 ? 5 : 15);
        if (latestResult.OccurredAtUtc > DateTimeOffset.MaxValue - delay)
        {
            return new(PackageRetryStatus.Blocked, attemptCount, latestResult);
        }

        var retryAt = latestResult.OccurredAtUtc + delay;
        return new(now < retryAt ? PackageRetryStatus.CoolingDown : PackageRetryStatus.Ready,
            attemptCount, latestResult, retryAt);
    }

    private static bool MatchesTarget(InstallPackageActionRequest package, InstallPackageActionRequest target) =>
        package.Scope == target.Scope && package.Architecture == target.Architecture &&
        package.PackageId == target.PackageId && package.ExactVersion == target.ExactVersion &&
        package.SourceId == target.SourceId && package.Publisher == target.Publisher;
}
