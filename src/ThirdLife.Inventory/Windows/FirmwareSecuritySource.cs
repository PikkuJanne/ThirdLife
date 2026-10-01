namespace ThirdLife.Inventory.Windows;

internal enum FirmwareCollectionStatus
{
    Collected = 1,
    NotFound,
    Unavailable,
    AccessDenied,
    InvalidData,
    Failed,
    TimedOut,
    CleanupIncomplete,
}

internal sealed record FirmwareCollectionInfo(FirmwareCollectionStatus Status, uint? NativeErrorCode = null);

internal sealed record FirmwareSecuritySourceSnapshot(
    FirmwarePlatformSourceSnapshot Platform,
    DeviceGuardSourceSnapshot DeviceGuard,
    TpmSourceSnapshot Tpm);

internal interface IFirmwareSecuritySource
{
    FirmwareSecuritySourceSnapshot Read(CancellationToken cancellationToken);
}

internal sealed class WindowsFirmwareSecuritySource : IFirmwareSecuritySource
{
    private readonly IFirmwarePlatformSource _platform;
    private readonly IDeviceGuardSource _deviceGuard;
    private readonly ITpmSource _tpm;

    public WindowsFirmwareSecuritySource()
        : this(new WindowsFirmwarePlatformSource(), new WindowsDeviceGuardSource(), new WindowsTpmSource())
    {
    }

    internal WindowsFirmwareSecuritySource(
        IFirmwarePlatformSource platform, IDeviceGuardSource deviceGuard, ITpmSource tpm)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _deviceGuard = deviceGuard ?? throw new ArgumentNullException(nameof(deviceGuard));
        _tpm = tpm ?? throw new ArgumentNullException(nameof(tpm));
    }

    public FirmwareSecuritySourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var platform = _platform.Read(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var deviceGuard = _deviceGuard.Read(cancellationToken);
        if (!deviceGuard.CleanupComplete)
        {
            throw new FirmwareSecurityCleanupException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var tpm = _tpm.Read(cancellationToken);
        if (!tpm.Wmi.CleanupComplete || tpm.Wmi.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete ||
            tpm.Wmi.Readiness.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete)
        {
            throw new FirmwareSecurityCleanupException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(platform, deviceGuard, tpm);
    }
}

internal sealed class FirmwareSecurityCleanupException : Exception
{
}
