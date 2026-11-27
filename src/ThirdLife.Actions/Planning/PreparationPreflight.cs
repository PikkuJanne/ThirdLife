using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Catalog.Schema;

namespace ThirdLife.Actions.Planning;

public enum PreparationPlatform
{
    Unknown,
    Windows11X64,
    UnsupportedOperatingSystem,
    UnsupportedArchitecture,
}

public enum PreparationPower
{
    Unknown,
    NoBattery,
    ExternalPower,
    OnBattery,
    CriticallyLowBattery,
}

public enum PreparationPreflightIssue
{
    PlatformUnavailable,
    UnsupportedOperatingSystem,
    UnsupportedArchitecture,
    DownloadEstimateUnavailable,
    InstallationEstimateUnavailable,
    StorageLayoutUnavailable,
    SystemSpaceUnavailable,
    StagingSpaceUnavailable,
    InsufficientSystemSpace,
    InsufficientStagingSpace,
    InsufficientSharedSpace,
    PowerUnavailable,
    ExternalPowerRequired,
    BatteryCriticallyLow,
}

public sealed record PreparationResourceSnapshot(
    DateTimeOffset ObservedAtUtc,
    PreparationPlatform Platform,
    PreparationPower Power,
    ulong? SystemAvailableBytes,
    ulong? StagingAvailableBytes,
    bool? SharedVolume);

public sealed record PreparationPreflightResult(
    PreparationResourceSnapshot Observation,
    ImmutableArray<PreparationPreflightIssue> Issues,
    ulong? SystemRequiredBytes,
    ulong? StagingRequiredBytes,
    ulong? SharedRequiredBytes,
    long? ReviewedInstalledFootprintBytes)
{
    public DateTimeOffset ObservedAtUtc => Observation.ObservedAtUtc;

    public bool AllowsPreparation => Issues.IsEmpty;
}

public static class PreparationPreflight
{
    public const ulong SystemReserveBytes = 1_024 * 1_024 * 1_024;
    public const ulong InstallerOverheadBytes = 128 * 1_024 * 1_024;
    public const ulong StagingReserveBytes = 128 * 1_024 * 1_024;

    /// <summary>Checks the approved estimates against a fresh, read-only resource observation.</summary>
    /// <param name="impact">The immutable plan impact.</param>
    /// <param name="snapshot">Current observations for the affected platform, volumes and power source.</param>
    /// <param name="downloadAlreadyStaged">Whether the exact single installer already occupies the observed staging volume.</param>
    /// <param name="reviewedInstalledFootprint">A separately reviewed base footprint for the exact single installer when the catalog estimate is unknown.</param>
    public static PreparationPreflightResult Evaluate(
        PlanImpact impact,
        PreparationResourceSnapshot snapshot,
        bool downloadAlreadyStaged = false,
        long? reviewedInstalledFootprint = null)
    {
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(snapshot);

        var packages = impact.Actions.Where(row => row.Action.Request is InstallPackageActionRequest).ToArray();
        if (packages.Length == 0)
        {
            return new(snapshot, [], null, null, null, null);
        }

        var issues = ImmutableArray.CreateBuilder<PreparationPreflightIssue>();
        CheckPlatform(snapshot.Platform, packages, issues);
        var download = impact.Download;
        if (download.UnknownActionCount > 0)
        {
            issues.Add(PreparationPreflightIssue.DownloadEstimateUnavailable);
        }

        var installation = impact.Installation;
        var reviewedFootprint = packages.Length == 1 && installation.UnknownActionCount == 1 &&
            reviewedInstalledFootprint is > 0 ? reviewedInstalledFootprint : null;
        if (installation.UnknownActionCount > 0 && reviewedFootprint is null)
        {
            issues.Add(PreparationPreflightIssue.InstallationEstimateUnavailable);
        }

        // A serial schedule can reuse workspace headroom. Count all declared package bytes conservatively.
        // These minima still describe the known footprint when an estimate is incomplete.
        var remainingDownload = downloadAlreadyStaged && packages.Length == 1 && download.TotalBytes.HasValue
            ? 0UL
            : (ulong)download.KnownBytes;
        var systemRequired = checked(SystemReserveBytes + InstallerOverheadBytes +
            (ulong)(reviewedFootprint ?? installation.KnownBytes));
        var stagingRequired = checked(StagingReserveBytes + remainingDownload);
        var sharedRequired = snapshot.SharedVolume == true ? checked(systemRequired + stagingRequired) : (ulong?)null;
        CheckSpace(snapshot, systemRequired, stagingRequired, sharedRequired, issues);
        CheckPower(snapshot.Power, issues);

        return new(snapshot, issues.ToImmutable(), systemRequired, stagingRequired, sharedRequired, reviewedFootprint);
    }

    private static void CheckPlatform(PreparationPlatform platform, ActionImpact[] packages,
        ImmutableArray<PreparationPreflightIssue>.Builder issues)
    {
        if (platform is not (PreparationPlatform.Windows11X64 or PreparationPlatform.UnsupportedOperatingSystem or
            PreparationPlatform.UnsupportedArchitecture))
        {
            issues.Add(PreparationPreflightIssue.PlatformUnavailable);
        }

        if (platform == PreparationPlatform.UnsupportedOperatingSystem ||
            packages.Any(row => row.Action.CatalogEntry.Package.MinimumOs != "windows_11"))
        {
            issues.Add(PreparationPreflightIssue.UnsupportedOperatingSystem);
        }

        if (platform == PreparationPlatform.UnsupportedArchitecture ||
            packages.Any(row => row.Action.Request is InstallPackageActionRequest { Architecture: not ActionArchitecture.X64 } ||
                !row.Action.CatalogEntry.Package.Architectures.Contains(CatalogArchitecture.X64)))
        {
            issues.Add(PreparationPreflightIssue.UnsupportedArchitecture);
        }
    }

    private static void CheckSpace(PreparationResourceSnapshot snapshot, ulong systemRequired, ulong stagingRequired,
        ulong? sharedRequired, ImmutableArray<PreparationPreflightIssue>.Builder issues)
    {
        if (snapshot.SharedVolume is null)
        {
            issues.Add(PreparationPreflightIssue.StorageLayoutUnavailable);
        }

        if (snapshot.SystemAvailableBytes is not { } systemAvailable)
        {
            issues.Add(PreparationPreflightIssue.SystemSpaceUnavailable);
        }
        else if (snapshot.SharedVolume != true && systemAvailable < systemRequired)
        {
            issues.Add(PreparationPreflightIssue.InsufficientSystemSpace);
        }

        if (snapshot.StagingAvailableBytes is not { } stagingAvailable)
        {
            issues.Add(PreparationPreflightIssue.StagingSpaceUnavailable);
        }
        else if (snapshot.SharedVolume != true && stagingAvailable < stagingRequired)
        {
            issues.Add(PreparationPreflightIssue.InsufficientStagingSpace);
        }

        if (sharedRequired is { } combined &&
            (snapshot.SystemAvailableBytes < combined || snapshot.StagingAvailableBytes < combined))
        {
            issues.Add(PreparationPreflightIssue.InsufficientSharedSpace);
        }
    }

    private static void CheckPower(PreparationPower power, ImmutableArray<PreparationPreflightIssue>.Builder issues)
    {
        switch (power)
        {
            case PreparationPower.NoBattery:
            case PreparationPower.ExternalPower:
                break;
            case PreparationPower.OnBattery:
                issues.Add(PreparationPreflightIssue.ExternalPowerRequired);
                break;
            case PreparationPower.CriticallyLowBattery:
                issues.Add(PreparationPreflightIssue.BatteryCriticallyLow);
                break;
            default:
                issues.Add(PreparationPreflightIssue.PowerUnavailable);
                break;
        }
    }
}
