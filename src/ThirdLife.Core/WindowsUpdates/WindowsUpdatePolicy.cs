namespace ThirdLife.Core.WindowsUpdates;

public enum WindowsUpdateDisposition
{
    NotCurrent = 1,
    RestartRequired,
    ReviewRequired,
    ApprovalRequired,
    Current,
    PassLimitReached,
}

public static class WindowsUpdatePolicy
{
    public const int MaximumUpdates = 64;
    public const int MaximumPasses = 3;

    public static bool IsSelectable(WindowsUpdateCandidate candidate) =>
        candidate is { MaximumDownloadBytes: not null, Classification: WindowsUpdateClass.Security or WindowsUpdateClass.Critical };

    public static bool Matches(WindowsUpdateCandidate exact, WindowsUpdateScan fresh) =>
        IsSelectable(exact) && fresh is { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false } &&
        fresh.Updates.Contains(exact);

    public static WindowsUpdateDisposition Evaluate(WindowsUpdateScan fresh, int completedPasses)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentOutOfRangeException.ThrowIfNegative(completedPasses);
        if (fresh.Status != WindowsUpdateStatus.Succeeded || fresh.RebootRequired is null)
        {
            return WindowsUpdateDisposition.NotCurrent;
        }

        if (fresh.RebootRequired == true)
        {
            return WindowsUpdateDisposition.RestartRequired;
        }

        if (fresh.Updates.Count == 0)
        {
            return WindowsUpdateDisposition.Current;
        }

        if (!fresh.Updates.Any(IsSelectable))
        {
            return WindowsUpdateDisposition.ReviewRequired;
        }

        return completedPasses >= MaximumPasses
            ? WindowsUpdateDisposition.PassLimitReached
            : WindowsUpdateDisposition.ApprovalRequired;
    }
}
