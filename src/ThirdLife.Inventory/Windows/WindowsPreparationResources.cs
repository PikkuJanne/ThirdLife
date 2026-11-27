using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Inventory.Windows;

public enum WindowsPreparationPower
{
    Unknown,
    NoBattery,
    ExternalPower,
    OnBattery,
    CriticallyLowBattery,
}

public sealed class WindowsPreparationResourceSnapshot
{
    internal WindowsPreparationResourceSnapshot(WindowsPlatformEligibility platform, WindowsPreparationPower power,
        ulong? systemAvailableBytes, ulong? stagingAvailableBytes, bool? sharedVolume)
    {
        Platform = platform;
        Power = power;
        SystemAvailableBytes = systemAvailableBytes;
        StagingAvailableBytes = stagingAvailableBytes;
        SharedVolume = sharedVolume;
    }

    public WindowsPlatformEligibility Platform { get; }
    public WindowsPreparationPower Power { get; }
    public ulong? SystemAvailableBytes { get; }
    public ulong? StagingAvailableBytes { get; }
    public bool? SharedVolume { get; }
}

public sealed class WindowsServicingResources(bool? servicingSupported, WindowsPreparationPower power,
    ulong? systemAvailableBytes, ulong? cacheAvailableBytes, bool? cacheSharesSystemVolume, WindowsUpdateServicingTarget? target = null)
{
    public bool? ServicingSupported { get; } = servicingSupported;
    public WindowsPreparationPower Power { get; } = power;
    public ulong? SystemAvailableBytes { get; } = systemAvailableBytes;
    public ulong? CacheAvailableBytes { get; } = cacheAvailableBytes;
    public bool? CacheSharesSystemVolume { get; } = cacheSharesSystemVolume;
    public WindowsUpdateServicingTarget? Target { get; } = target;
}

/// <summary>Reads platform, power and caller-available storage at each preparation route's fixed locations.</summary>
public static class WindowsPreparationResources
{
    private const byte MinimumPortableBatteryPercent = 10;

    public static WindowsPreparationResourceSnapshot Read()
    {
        var platform = WindowsPlatform.CheckCurrent();
        if (platform != WindowsPlatformEligibility.Supported)
        {
            return new(platform, WindowsPreparationPower.Unknown, null, null, null);
        }

        var (systemAvailable, systemMount) = WindowsStorageVolumeSource.ReadPreparationVolume(Environment.SystemDirectory);
        var (installAvailable, installMount) = WindowsStorageVolumeSource.ReadPreparationVolume(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify));
        var (stagingAvailable, stagingMount) = WindowsStorageVolumeSource.ReadPreparationVolume(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        var systemInstallCapacity = GetSystemInstallCapacity(systemAvailable, installAvailable,
            WindowsStorageVolumeSource.ShareVolume(systemMount, installMount));
        var sharedVolume = WindowsStorageVolumeSource.ShareVolume(systemMount, stagingMount);
        return new(platform, ReadPower(), systemInstallCapacity, stagingAvailable, sharedVolume);
    }

    /// <summary>Reads the Windows servicing store and update download cache without following redirects.</summary>
    public static WindowsServicingResources ReadForWindowsUpdate(DateTimeOffset now, CancellationToken token)
    {
        var os = new WindowsOperatingSystemSource().Read(token);
        var servicing = EvaluateServicing(os, DateOnly.FromDateTime(now.UtcDateTime));
        if (WindowsPlatform.Evaluate(os).Eligibility != WindowsPlatformEligibility.Supported)
        {
            return new(servicing, WindowsPreparationPower.Unknown, null, null, null);
        }

        var system = Environment.SystemDirectory;
        var windows = Path.GetDirectoryName(system);
        if (windows is null || !WindowsStorageVolumeSource.IsLocalAbsolutePath(windows))
        {
            return new(servicing, ReadPower(), null, null, null);
        }

        var (systemFree, systemMount) = WindowsStorageVolumeSource.ReadPreparationVolume(system);
        var (storeFree, storeMount) = WindowsStorageVolumeSource.ReadPreparationVolume(Path.Combine(windows, "WinSxS"));
        token.ThrowIfCancellationRequested();
        var (cacheFree, cacheMount) = WindowsStorageVolumeSource.ReadPreparationVolume(
            Path.Combine(windows, "SoftwareDistribution", "Download"));
        var servicingFree = GetSystemInstallCapacity(systemFree, storeFree, WindowsStorageVolumeSource.ShareVolume(systemMount, storeMount));
        var target = os.Edition.Status == SystemInventorySourceStatus.Available && WindowsOperatingSystemSource.ReadBuildRevision() is { } revision
            ? new WindowsUpdateServicingTarget(os.Version.GetRequiredValue().Build, revision, os.Edition.GetRequiredValue()) : null;
        return new(servicing, ReadPower(), servicingFree, cacheFree, WindowsStorageVolumeSource.ShareVolume(systemMount, cacheMount), target);
    }

    internal static bool? EvaluateServicing(OperatingSystemSnapshot os, DateOnly today)
    {
        var platform = WindowsPlatform.Evaluate(os).Eligibility;
        if (platform != WindowsPlatformEligibility.Supported)
        {
            return platform == WindowsPlatformEligibility.UnverifiedIdentity ? null : false;
        }

        if (os.Edition.Status != SystemInventorySourceStatus.Available) { return null; }
        var lifecycle = WindowsLifecycleCatalog.Current.Evaluate((uint)os.Version.GetRequiredValue().Build,
            os.Edition.GetRequiredValue(), WindowsPlatform.GetArchitecture(os.Architecture), today);
        return lifecycle.Status switch
        {
            WindowsLifecycleStatus.Supported => true,
            WindowsLifecycleStatus.Unsupported => false,
            _ => null,
        };
    }

    private static WindowsPreparationPower ReadPower() => WindowsSystemPowerSource.Read() is { } status
        ? ClassifyPower(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent)
        : WindowsPreparationPower.Unknown;

    // The system budget covers both Windows and the route's installation directory.
    internal static ulong? GetSystemInstallCapacity(ulong? systemAvailable, ulong? installAvailable, bool? installSharesSystem) =>
        installSharesSystem is true && systemAvailable is { } system && installAvailable is { } install
            ? Math.Min(system, install) : null;

    internal static WindowsPreparationPower ClassifyPower(byte acLineStatus, byte batteryFlag, byte batteryPercent)
    {
        const byte offline = 0;
        const byte online = 1;
        const byte unknown = 255;
        const byte high = 1;
        const byte low = 2;
        const byte critical = 4;
        const byte charging = 8;
        const byte noBattery = 128;
        if (acLineStatus is not (offline or online or unknown) || batteryPercent is > 100 and not unknown)
        {
            return WindowsPreparationPower.Unknown;
        }

        if (batteryFlag == noBattery)
        {
            return acLineStatus != offline && batteryPercent == unknown
                ? WindowsPreparationPower.NoBattery : WindowsPreparationPower.Unknown;
        }

        if ((batteryFlag & ~(high | low | critical | charging)) != 0 ||
            ((batteryFlag & high) != 0 && (batteryFlag & (low | critical)) != 0) ||
            (acLineStatus == offline && (batteryFlag & charging) != 0))
        {
            return WindowsPreparationPower.Unknown;
        }

        if ((batteryFlag & critical) != 0 || batteryPercent < MinimumPortableBatteryPercent)
        {
            return WindowsPreparationPower.CriticallyLowBattery;
        }

        return acLineStatus switch
        {
            offline => WindowsPreparationPower.OnBattery,
            online when batteryPercent != unknown => WindowsPreparationPower.ExternalPower,
            _ => WindowsPreparationPower.Unknown,
        };
    }
}
