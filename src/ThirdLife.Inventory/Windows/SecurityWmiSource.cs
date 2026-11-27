using System.Diagnostics;
using System.Runtime.InteropServices;
using SourceValue = ThirdLife.Inventory.Windows.SystemInventorySourceValue<string>;

namespace ThirdLife.Inventory.Windows;

internal static class SecurityWmiSource
{
    private static readonly Guid LocatorClass = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorInterface = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    internal static SourceValue Read(bool defender, CancellationToken cancellationToken)
    {
        bool clean = true;
        try
        {
            var observation = Collect(defender, cancellationToken, ref clean);
            return clean ? observation : throw new SecuritySourceCleanupException();
        }
        catch (Exception) when (!clean)
        {
            throw new SecuritySourceCleanupException();
        }
    }

    private static SourceValue Collect(bool defender, CancellationToken cancellationToken, ref bool clean)
    {
        string localNamespace = defender ? @"root\Microsoft\Windows\Defender" : @"root\CIMV2\Security\MicrosoftVolumeEncryption";
        string query = defender
            ? "SELECT AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,DefenderSignaturesOutOfDate FROM MSFT_MpComputerStatus"
            : "SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE VolumeType=0";
        var started = Stopwatch.GetTimestamp();
        bool initialized = false;
        IStorageWbemLocator? locator = null;
        IStorageWbemServices? services = null;
        IStorageWbemEnumerator? rows = null;
        try
        {
            Check(StorageWmiNativeMethods.CoInitializeEx(0, 0));
            initialized = true;
            Check(StorageWmiNativeMethods.CoCreateInstance(in LocatorClass, 0, 1, in LocatorInterface, out locator));
            cancellationToken.ThrowIfCancellationRequested();
            if (locator is null) { return SourceValue.InvalidData(); }
            Check(locator.ConnectServer(localNamespace, 0, 0, 0, 0x80, 0, 0, out services));
            if (services is null) { return SourceValue.InvalidData(); }
            Protect(services);
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started)) { return SourceValue.Unavailable(); }
            Check(services.ExecQuery("WQL", query, 0x30, 0, out rows));
            if (rows is null) { return SourceValue.InvalidData(); }
            Protect(rows);
            SourceValue? result = null;
            for (int call = 0; call < 32 && !Expired(started); call++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IStorageWbemObject? row = null;
                try
                {
                    int status = rows.Next(100, 1, out row, out var count);
                    Check(status);
                    if (status is not (0 or 1 or 0x40004) || count > 1 || (row is not null) != (count == 1) ||
                        (status == 0 && count == 0))
                    {
                        return SourceValue.InvalidData();
                    }
                    if (row is not null)
                    {
                        if (result is not null) { return SourceValue.InvalidData(); }
                        result = defender ? ReadDefender(row, ref clean) : ReadEncryption(row, ref clean);
                    }
                    if (status == 1) { return Expired(started) ? SourceValue.Unavailable() : result ?? SourceValue.Unavailable(); }
                }
                finally
                {
                    clean &= Release(row);
                }
                if (!clean) { break; }
            }
            return SourceValue.Unavailable();
        }
        catch (FirmwareWmiCleanupException)
        {
            clean = false;
            return SourceValue.Unavailable();
        }
        catch (FirmwareWmiNativeException error)
        {
            return MapError(error.NativeErrorCode);
        }
        catch (COMException error)
        {
            return MapError(unchecked((uint)error.HResult));
        }
        catch (UnauthorizedAccessException)
        {
            return SourceValue.AccessDenied();
        }
        finally
        {
            clean &= Release(rows);
            clean &= Release(services);
            clean &= Release(locator);
            if (initialized) { StorageWmiNativeMethods.CoUninitialize(); }
        }
    }

    private static SourceValue ReadEncryption(IStorageWbemObject row, ref bool clean)
    {
        var value = FirmwareWmiValueReader.ReadUInt32(row, "ProtectionStatus", ref clean);
        return value.Status != SystemInventorySourceStatus.Available ? Failure(value.Status) : value.Value switch
        {
            0 => SourceValue.Available("protection_off"),
            1 => SourceValue.Available("protection_on"),
            2 => SourceValue.Unavailable(),
            _ => SourceValue.InvalidData(),
        };
    }

    private static SourceValue ReadDefender(IStorageWbemObject row, ref bool clean)
    {
        var service = FirmwareWmiValueReader.ReadBoolean(row, "AMServiceEnabled", ref clean);
        var antivirus = FirmwareWmiValueReader.ReadBoolean(row, "AntivirusEnabled", ref clean);
        var realtime = FirmwareWmiValueReader.ReadBoolean(row, "RealTimeProtectionEnabled", ref clean);
        var stale = FirmwareWmiValueReader.ReadBoolean(row, "DefenderSignaturesOutOfDate", ref clean);
        return DefenderState(service, antivirus, realtime, stale);
    }

    internal static SourceValue DefenderState(SystemInventorySourceValue<bool> service, SystemInventorySourceValue<bool> antivirus,
        SystemInventorySourceValue<bool> realtime, SystemInventorySourceValue<bool> stale)
    {
        foreach (var check in new[] { service, antivirus, realtime, stale })
        {
            if (check.Status != SystemInventorySourceStatus.Available) { return Failure(check.Status); }
        }
        return SourceValue.Available(service.Value && antivirus.Value && realtime.Value && !stale.Value
            ? "real_time_active_signatures_current" : "requires_review");
    }

    private static bool Expired(long started) => Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(3);

    private static void Protect(object proxy) => Check(StorageWmiNativeMethods.CoSetProxyBlanket(proxy, 10, 0, 0, 3, 3, 0, 0));

    private static void Check(int status)
    {
        if (status < 0) { throw new FirmwareWmiNativeException(unchecked((uint)status)); }
    }

    private static SourceValue MapError(uint code) => code switch
    {
        5 or 0x80070005 or 0x80041003 => SourceValue.AccessDenied(),
        _ => SourceValue.Unavailable(),
    };

    private static SourceValue Failure(SystemInventorySourceStatus status) => status switch
    {
        SystemInventorySourceStatus.AccessDenied => SourceValue.AccessDenied(),
        SystemInventorySourceStatus.InvalidData => SourceValue.InvalidData(),
        _ => SourceValue.Unavailable(),
    };

    private static bool Release(object? value)
    {
        if (value is null) { return true; }
        try { _ = Marshal.FinalReleaseComObject(value); return true; }
        catch (Exception) { return false; }
    }
}
