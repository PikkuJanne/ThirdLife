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

/// <summary>Reads current platform, power, and caller-available storage at the package route's fixed locations.</summary>
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
        var power = WindowsSystemPowerSource.Read() is { } status
            ? ClassifyPower(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent)
            : WindowsPreparationPower.Unknown;
        return new(platform, power, systemInstallCapacity, stagingAvailable, sharedVolume);
    }

    // The reviewed x64 package places its payload in the native Program Files tree.
    // This route's single system budget covers both Windows Installer and that payload.
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
