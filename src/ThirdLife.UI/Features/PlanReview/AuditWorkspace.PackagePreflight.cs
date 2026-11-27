using System.Globalization;
using ThirdLife.Actions.Planning;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    // This exact route pins the MSI reviewed in ADR 0009. The embedded files
    // provide a payload estimate. Installer caching and rollback use separate headroom.
    private const string ReviewedMsiSha256 = "e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80";
    private const long ReviewedMsiEmbeddedFileBytes = 28_130_419;
    private readonly Func<DateTimeOffset, PreparationResourceSnapshot> _observePreparationResources;
    private PreparationPreflightResult? _packagePreflight;

    public bool HasPackagePreflightStatus => _packagePreflight is not null;
    internal bool HasPackagePreflightBlocker => _packagePreflight is { AllowsPreparation: false };

    public string PackagePreflightStatus => _packagePreflight is { } result
        ? DescribePreparationResources(result)
        : string.Empty;

    private static PreparationResourceSnapshot ObserveWindowsPreparationResources(DateTimeOffset now)
    {
        var resources = WindowsPreparationResources.Read();
        var platform = resources.Platform switch
        {
            WindowsPlatformEligibility.Supported => PreparationPlatform.Windows11X64,
            WindowsPlatformEligibility.UnsupportedOperatingSystem => PreparationPlatform.UnsupportedOperatingSystem,
            WindowsPlatformEligibility.UnsupportedArchitecture => PreparationPlatform.UnsupportedArchitecture,
            _ => PreparationPlatform.Unknown,
        };
        var power = resources.Power switch
        {
            WindowsPreparationPower.NoBattery => PreparationPower.NoBattery,
            WindowsPreparationPower.ExternalPower => PreparationPower.ExternalPower,
            WindowsPreparationPower.OnBattery => PreparationPower.OnBattery,
            WindowsPreparationPower.CriticallyLowBattery => PreparationPower.CriticallyLowBattery,
            _ => PreparationPower.Unknown,
        };
        return new(now, platform, power, resources.SystemAvailableBytes, resources.StagingAvailableBytes,
            resources.SharedVolume);
    }

    // Callers first match the exact reviewed x64 package route and pinned catalogue.
    private Task<PreparationPreflightResult> CheckPreparationResourcesAsync(
        PlanImpact impact, bool downloadAlreadyStaged, CancellationToken token)
    {
        long? footprint = impact.Actions is [var row] && row.Action.CatalogEntry.Package.InstallerSha256 == ReviewedMsiSha256
            ? ReviewedMsiEmbeddedFileBytes : null;
        return Task.Run(() =>
        {
            var observed = _observePreparationResources(_timeProvider.GetUtcNow());
            token.ThrowIfCancellationRequested();
            return PreparationPreflight.Evaluate(impact, observed, downloadAlreadyStaged, footprint);
        }, token);
    }

    private static string DescribePreparationResources(PreparationPreflightResult result)
    {
        var details = new List<string>
        {
            result.AllowsPreparation ? "Preparation requirements met." : "Preparation is paused.",
            $"Checked at {result.ObservedAtUtc:u}.",
        };
        details.AddRange(result.Issues.Select(DescribePreflightIssue));
        var observation = result.Observation;
        if (result.SharedRequiredBytes is { } shared)
        {
            ulong? available = observation.SystemAvailableBytes is { } system &&
                observation.StagingAvailableBytes is { } staging ? Math.Min(system, staging) : null;
            details.Add(DescribePreflightCapacity("Windows and download storage", available, shared));
        }
        else if (observation.SharedVolume == false)
        {
            details.Add(DescribePreflightCapacity("Windows storage", observation.SystemAvailableBytes, result.SystemRequiredBytes));
            details.Add(DescribePreflightCapacity("Download storage", observation.StagingAvailableBytes, result.StagingRequiredBytes));
        }

        if (result.ReviewedInstalledFootprintBytes is { } footprint)
        {
            details.Add(string.Create(CultureInfo.CurrentCulture,
                $"Installed size is uncertain. The space check uses {footprint:N0} bytes of reviewed installer payload plus extra space for temporary files and rollback."));
        }

        return string.Join(' ', details);
    }

    private static string DescribePreflightCapacity(string location, ulong? available, ulong? required) =>
        (available, required) switch
        {
            ({ } free, { } needed) => string.Create(CultureInfo.CurrentCulture,
                $"{location} has {free:N0} bytes available and needs at least {needed:N0} bytes, including reserve."),
            ({ } free, null) => string.Create(CultureInfo.CurrentCulture,
                $"{location} has {free:N0} bytes available. The requirement is unknown."),
            (null, { } needed) => string.Create(CultureInfo.CurrentCulture,
                $"{location} needs at least {needed:N0} bytes, including reserve. Available space is unknown."),
            _ => $"{location} capacity and required space are unknown.",
        };

    private static string DescribePreflightIssue(PreparationPreflightIssue issue) => issue switch
    {
        PreparationPreflightIssue.PlatformUnavailable => "Windows version or architecture could not be confirmed.",
        PreparationPreflightIssue.UnsupportedOperatingSystem => "This plan needs a supported Windows 11 client.",
        PreparationPreflightIssue.UnsupportedArchitecture => "This plan needs native x64 Windows and an x64 installer.",
        PreparationPreflightIssue.DownloadEstimateUnavailable => "The download size is unknown.",
        PreparationPreflightIssue.InstallationEstimateUnavailable => "The installed size is unknown and no reviewed footprint is available.",
        PreparationPreflightIssue.StorageLayoutUnavailable => "ThirdLife could not confirm where Windows and the installer download use disk space.",
        PreparationPreflightIssue.SystemSpaceUnavailable => "ThirdLife could not check space on the Windows storage.",
        PreparationPreflightIssue.StagingSpaceUnavailable => "ThirdLife could not check space on the download storage.",
        PreparationPreflightIssue.InsufficientSystemSpace => "Free up space on the Windows storage and check again.",
        PreparationPreflightIssue.InsufficientStagingSpace => "Free up space on the download storage and check again.",
        PreparationPreflightIssue.InsufficientSharedSpace => "Free up space on the shared Windows and download storage and check again.",
        PreparationPreflightIssue.PowerUnavailable => "ThirdLife could not confirm a safe power state. Connect reliable external power and check again.",
        PreparationPreflightIssue.ExternalPowerRequired => "Connect reliable external power before continuing.",
        PreparationPreflightIssue.BatteryCriticallyLow => "Connect reliable external power and let the battery charge before continuing.",
        _ => "A preparation requirement could not be confirmed.",
    };
}
