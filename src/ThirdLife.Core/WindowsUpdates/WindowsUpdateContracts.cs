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
    ConsentDeclined,
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
    public const long DownloadSizeLimit = 9_007_199_254_740_991;

    public WindowsUpdateCandidate(WindowsUpdateIdentity identity, WindowsUpdateClass classification, long? maximumDownloadBytes = null)
    {
        if (maximumDownloadBytes is < 0 or > DownloadSizeLimit)
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

public sealed record WindowsUpdateInstalledObservation
{
    public WindowsUpdateInstalledObservation(WindowsUpdateStatus status, WindowsUpdateIdentity identity, bool? installed)
    {
        if (installed.HasValue && status != WindowsUpdateStatus.Succeeded)
        {
            throw new ArgumentException("An installed-state fact requires a successful lookup.", nameof(installed));
        }
        Status = DomainValue.RequireDefined(status, nameof(status));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Installed = installed;
    }

    public WindowsUpdateStatus Status { get; }
    public WindowsUpdateIdentity Identity { get; }
    public bool? Installed { get; }
}

public interface IWindowsUpdateAgent
{
    Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken);
    Task<WindowsUpdatePreparation> AssessPreparationAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
        Task.FromResult(new WindowsUpdatePreparation(exact,
            cancellationToken.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.Unavailable, DateTimeOffset.UtcNow));
    Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken);
    Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken);
    Task<WindowsUpdateInstalledObservation> ObserveInstalledAsync(WindowsUpdateIdentity exact, CancellationToken cancellationToken) =>
        Task.FromResult(new WindowsUpdateInstalledObservation(
            cancellationToken.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.Unavailable, exact, null));
}
