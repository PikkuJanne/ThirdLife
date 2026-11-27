namespace ThirdLife.Core.WindowsUpdates;

/// <summary>A transient, read-only check of one offered revision and its servicing environment.</summary>
public sealed record WindowsUpdatePreparation(
    WindowsUpdateCandidate Exact, WindowsUpdateStatus Status, DateTimeOffset ObservedAtUtc,
    bool? RebootRequired = null, bool? ServicingSupported = null, bool? PowerReady = null,
    ulong? SystemAvailableBytes = null, ulong? CacheAvailableBytes = null, bool? CacheSharesSystemVolume = null,
    long? RecommendedInstallationBytes = null, WindowsUpdateEligibilityReason? EligibilityReason = null)
{
    // Compare the maximum transfer and the publisher's recommendation before installation.
    // Neither value specifies the space necessary for a successful rollback.
    public bool? HasRecommendedSpace
    {
        get
        {
            if (Status != WindowsUpdateStatus.Succeeded || Exact.MaximumDownloadBytes is not { } download ||
                RecommendedInstallationBytes is not > 0 || SystemAvailableBytes is not { } system ||
                CacheAvailableBytes is not { } cache || CacheSharesSystemVolume is not { } shared)
            {
                return null;
            }

            var install = (ulong)RecommendedInstallationBytes.Value;
            return shared ? Math.Min(system, cache) >= install + (ulong)download :
                system >= install && cache >= (ulong)download;
        }
    }
}
