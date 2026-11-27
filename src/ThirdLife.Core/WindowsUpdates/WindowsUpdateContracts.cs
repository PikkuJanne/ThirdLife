namespace ThirdLife.Core.WindowsUpdates;

public enum WindowsUpdateClass
{
    Security = 1,
    Critical,
    Driver,
    Other,
    Restricted,
}

public enum WindowsUpdateStatus
{
    Succeeded = 1,
    Offline,
    ServiceUnavailable,
    AccessDenied,
    TimedOut,
    Cancelled,
    Failed,
    LimitExceeded,
    Partial,
    ApprovalChanged,
    Unavailable,
}

public sealed record WindowsUpdateIdentity
{
    public WindowsUpdateIdentity(Guid updateId, int revision)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(updateId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        UpdateId = updateId;
        Revision = revision;
    }

    public Guid UpdateId { get; }
    public int Revision { get; }
}

public sealed record WindowsUpdateCandidate
{
    public WindowsUpdateCandidate(WindowsUpdateIdentity identity, WindowsUpdateClass classification, long? maximumDownloadBytes = null)
    {
        if (maximumDownloadBytes is < 0 or > 9_007_199_254_740_991)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDownloadBytes));
        }

        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Classification = DomainValue.RequireDefined(classification, nameof(classification));
        MaximumDownloadBytes = maximumDownloadBytes;
    }

    public WindowsUpdateIdentity Identity { get; }
    public WindowsUpdateClass Classification { get; }
    public long? MaximumDownloadBytes { get; }
}

public sealed class WindowsUpdateScan
{
    public WindowsUpdateScan(WindowsUpdateStatus status, IReadOnlyList<WindowsUpdateCandidate> updates, bool? rebootRequired)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var count = updates.Count;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, WindowsUpdatePolicy.MaximumUpdates);
        var copy = new WindowsUpdateCandidate[count];
        var seen = new HashSet<Guid>();
        for (var index = 0; index < copy.Length; index++)
        {
            var candidate = updates[index];
            if (candidate is null || !seen.Add(candidate.Identity.UpdateId))
            {
                throw new ArgumentException("A scan requires one revision per update identity.", nameof(updates));
            }

            copy[index] = candidate;
        }

        Status = DomainValue.RequireDefined(status, nameof(status));
        Updates = Array.AsReadOnly(copy);
        RebootRequired = rebootRequired;
    }

    public WindowsUpdateStatus Status { get; }
    public IReadOnlyList<WindowsUpdateCandidate> Updates { get; }
    public bool? RebootRequired { get; }
}

public sealed record WindowsUpdateOperation
{
    public WindowsUpdateOperation(WindowsUpdateStatus status, bool? rebootRequired, int? progressPercent = null)
    {
        if (progressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progressPercent));
        }

        Status = DomainValue.RequireDefined(status, nameof(status));
        RebootRequired = rebootRequired;
        ProgressPercent = progressPercent;
    }

    public WindowsUpdateStatus Status { get; }
    public bool? RebootRequired { get; }
    public int? ProgressPercent { get; }
}

public interface IWindowsUpdateAgent
{
    Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken);
    Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken);
    Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken);
}
