using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsTpmSource : ITpmSource
{
    internal const string LocalNamespace = "root\\CIMV2\\Security\\MicrosoftTpm";
    internal const string QueryText = "SELECT SpecVersion,ManufacturerVersion,ManufacturerId,IsEnabled_InitialValue,IsActivated_InitialValue,IsOwned_InitialValue FROM Win32_Tpm";
    internal const int EnumerationWaitMilliseconds = 100;
    internal const int MaximumEnumerationCalls = 64;
    internal static readonly TimeSpan CollectionDeadline = TimeSpan.FromSeconds(6);
    private static readonly Guid LocatorClassId = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorInterfaceId = new("DC12A687-737F-11CF-884D-00AA004B2E24");
    private readonly Func<TpmDeviceInfoSnapshot> _deviceRead;
    private readonly Func<CancellationToken, TpmWmiSnapshot> _wmiRead;

    public WindowsTpmSource() : this(ReadNativeDevice, ReadNativeWmi) { }

    internal WindowsTpmSource(Func<TpmDeviceInfoSnapshot> deviceRead, Func<CancellationToken, TpmWmiSnapshot> wmiRead)
    {
        _deviceRead = deviceRead ?? throw new ArgumentNullException(nameof(deviceRead));
        _wmiRead = wmiRead ?? throw new ArgumentNullException(nameof(wmiRead));
    }

    public TpmSourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TpmDeviceInfoSnapshot device;
        try { device = _deviceRead(); }
        catch (Exception error) when (error is not OperationCanceledException) { device = new(ExceptionInfo(error)); }
        cancellationToken.ThrowIfCancellationRequested();
        TpmWmiSnapshot wmi;
        try { wmi = _wmiRead(cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException && error is not FirmwareSecurityCleanupException)
        {
            wmi = EmptyWmi(ExceptionInfo(error));
        }
        if (!wmi.CleanupComplete || wmi.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete ||
            wmi.Readiness.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete)
        {
            throw new FirmwareSecurityCleanupException();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(device, wmi);
    }

    private static TpmDeviceInfoSnapshot ReadNativeDevice()
    {
        if (!OperatingSystem.IsWindows()) { return new(new(FirmwareCollectionStatus.Unavailable)); }
        var code = NativeMethods.Tbsi_GetDeviceInfo(16, out var value);
        return DeviceResult(code, new(value.StructVersion, value.TpmVersion, value.InterfaceType, value.ImplementationRevision));
    }

    internal static TpmDeviceInfoSnapshot DeviceResult(uint code, TpmDeviceInfo value) =>
        code == 0 ? new(new(FirmwareCollectionStatus.Collected), code, value) :
        new(code == 0x8028400F ? new(FirmwareCollectionStatus.NotFound, code) : NativeInfo(code), code);

    private static TpmWmiSnapshot ReadNativeWmi(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) { return EmptyWmi(new(FirmwareCollectionStatus.Unavailable)); }
        IStorageWbemLocator? locator = null;
        IStorageWbemServices? services = null;
        IStorageWbemEnumerator? enumerator = null;
        var initialized = false;
        var cleanupComplete = true;
        var cancelled = false;
        var started = Stopwatch.GetTimestamp();
        TpmWmiSnapshot snapshot;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(StorageWmiNativeMethods.CoInitializeEx(0, 0));
            initialized = true;
            Check(StorageWmiNativeMethods.CoCreateInstance(in LocatorClassId, 0, 1, in LocatorInterfaceId, out locator));
            if (locator is null) { throw new TpmInvalidDataException(); }
            Check(locator.ConnectServer(LocalNamespace, 0, 0, 0, 0x80, 0, 0, out services));
            if (services is null) { throw new TpmInvalidDataException(); }
            SetReadSecurity(services);
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started)) { throw new TpmTimeoutException(); }
            // Fixed projection, RETURN_IMMEDIATELY | FORWARD_ONLY. No instance paths,
            // owner authorization, keys, certificates, logs, or identifying fields.
            Check(services.ExecQuery("WQL", QueryText, 0x30, 0, out enumerator));
            if (enumerator is null) { throw new TpmInvalidDataException(); }
            SetReadSecurity(enumerator);
            snapshot = ReadRows(enumerator, started, ref cleanupComplete, Release, cancellationToken);
            if (cleanupComplete && snapshot.CollectionInfo.Status == FirmwareCollectionStatus.Collected && snapshot.InstancePresent == true)
            {
                snapshot = snapshot with { Readiness = ReadReadiness(services, started, ref cleanupComplete, cancellationToken) };
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            snapshot = EmptyWmi(new(FirmwareCollectionStatus.Unavailable));
        }
        catch (Exception error) { snapshot = EmptyWmi(ExceptionInfo(error)); }
        finally
        {
            cleanupComplete &= Release(enumerator);
            cleanupComplete &= Release(services);
            cleanupComplete &= Release(locator);
            if (initialized) { StorageWmiNativeMethods.CoUninitialize(); }
        }

        if (cancelled)
        {
            if (!cleanupComplete) { throw new FirmwareSecurityCleanupException(); }
            throw new OperationCanceledException(cancellationToken);
        }

        return cleanupComplete ? snapshot : EmptyWmi(new(FirmwareCollectionStatus.CleanupIncomplete,
            snapshot.Readiness.CollectionInfo.Status == FirmwareCollectionStatus.CleanupIncomplete
                ? snapshot.Readiness.CollectionInfo.NativeErrorCode : snapshot.CollectionInfo.NativeErrorCode)) with
        { CleanupComplete = false };
    }

    internal static TpmWmiSnapshot ReadRows(IStorageWbemEnumerator enumerator, long started,
        ref bool cleanupComplete, Func<object?, bool> release, CancellationToken cancellationToken)
    {
        TpmWmiSnapshot? snapshot = null;
        for (var call = 0; call < MaximumEnumerationCalls; call++)
        {
            if (!cleanupComplete) { return EmptyWmi(new(FirmwareCollectionStatus.CleanupIncomplete)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started)) { return EmptyWmi(new(FirmwareCollectionStatus.TimedOut)); }
            IStorageWbemObject? row = null;
            try
            {
                var status = enumerator.Next(EnumerationWaitMilliseconds, 1, out row, out var count);
                Check(status);
                if (count > 1 || (count == 1) != (row is not null) || status is not (0 or 1 or 0x40004) ||
                    (status == 0 && count == 0))
                {
                    return EmptyWmi(new(FirmwareCollectionStatus.InvalidData));
                }

                if (row is not null)
                {
                    if (snapshot is not null) { return EmptyWmi(new(FirmwareCollectionStatus.InvalidData)); }
                    snapshot = ReadRow(row, started, ref cleanupComplete, cancellationToken);
                }
                if (status == 1)
                {
                    return snapshot ?? EmptyWmi(new(FirmwareCollectionStatus.NotFound)) with { InstancePresent = false };
                }
            }
            finally { cleanupComplete &= release(row); }
        }

        return EmptyWmi(new(FirmwareCollectionStatus.TimedOut));
    }

    private static TpmWmiSnapshot ReadRow(IStorageWbemObject row, long started, ref bool cleanupComplete,
        CancellationToken cancellationToken)
    {
        CheckDeadline(started, cancellationToken);
        var spec = FirmwareWmiValueReader.ReadVersion(row, "SpecVersion", ref cleanupComplete);
        CheckDeadline(started, cancellationToken);
        var manufacturerVersion = FirmwareWmiValueReader.ReadVersion(row, "ManufacturerVersion", ref cleanupComplete);
        CheckDeadline(started, cancellationToken);
        var manufacturer = FirmwareWmiValueReader.ReadUInt32(row, "ManufacturerId", ref cleanupComplete);
        if (manufacturer.Status == SystemInventorySourceStatus.Available && manufacturer.GetRequiredValue() == 0)
        {
            manufacturer = SystemInventorySourceValue<uint>.Unavailable();
        }
        CheckDeadline(started, cancellationToken);
        var enabled = FirmwareWmiValueReader.ReadBoolean(row, "IsEnabled_InitialValue", ref cleanupComplete);
        CheckDeadline(started, cancellationToken);
        var activated = FirmwareWmiValueReader.ReadBoolean(row, "IsActivated_InitialValue", ref cleanupComplete);
        CheckDeadline(started, cancellationToken);
        var owned = FirmwareWmiValueReader.ReadBoolean(row, "IsOwned_InitialValue", ref cleanupComplete);
        return new(new(FirmwareCollectionStatus.Collected), true, spec, manufacturerVersion, manufacturer,
            enabled, activated, owned, EmptyReadiness(new(FirmwareCollectionStatus.Unavailable)));
    }

    private static TpmReadinessSnapshot ReadReadiness(IStorageWbemServices services, long started,
        ref bool cleanupComplete, CancellationToken cancellationToken)
    {
        IStorageWbemObject? result = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started)) { return EmptyReadiness(new(FirmwareCollectionStatus.TimedOut)); }
            // The synchronous fixed read remains on the provider's retained worker.
            // Caller cancellation cannot abort this native call; no retry worker is started.
            Check(((ITpmWbemReadinessServices)services).ExecMethod("Win32_Tpm=@", "IsReadyInformation", 0, 0, 0, out result, 0));
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started)) { return EmptyReadiness(new(FirmwareCollectionStatus.TimedOut)); }
            if (result is null) { return EmptyReadiness(new(FirmwareCollectionStatus.InvalidData)); }
            return ReadReadinessResult(result, started, ref cleanupComplete, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException) { return EmptyReadiness(ExceptionInfo(error)); }
        finally { cleanupComplete &= Release(result); }
    }

    internal static TpmReadinessSnapshot ReadReadinessResult(IStorageWbemObject result, ref bool cleanupComplete)
        => ReadReadinessResult(result, Stopwatch.GetTimestamp(), ref cleanupComplete, CancellationToken.None);

    internal static TpmReadinessSnapshot ReadReadinessResult(IStorageWbemObject result, long started,
        ref bool cleanupComplete, CancellationToken cancellationToken)
    {
        CheckDeadline(started, cancellationToken);
        var returnValue = FirmwareWmiValueReader.ReadUInt32(result, "ReturnValue", ref cleanupComplete);
        if (returnValue.Status != SystemInventorySourceStatus.Available)
        {
            return EmptyReadiness(new(FirmwareCollectionStatus.InvalidData)) with { ReturnValue = returnValue };
        }
        if (returnValue.GetRequiredValue() != 0)
        {
            return EmptyReadiness(NativeInfo(returnValue.GetRequiredValue())) with { ReturnValue = returnValue };
        }
        CheckDeadline(started, cancellationToken);
        var ready = FirmwareWmiValueReader.ReadBoolean(result, "IsReady", ref cleanupComplete);
        CheckDeadline(started, cancellationToken);
        var information = FirmwareWmiValueReader.ReadUInt32(result, "Information", ref cleanupComplete);
        CheckDeadline(started, cancellationToken);
        return new(new(FirmwareCollectionStatus.Collected), returnValue, information, ready);
    }

    internal static TpmWmiSnapshot EmptyWmi(FirmwareCollectionInfo info) => new(info, null,
        SystemInventorySourceValue<string>.Unavailable(), SystemInventorySourceValue<string>.Unavailable(),
        SystemInventorySourceValue<uint>.Unavailable(), SystemInventorySourceValue<bool>.Unavailable(),
        SystemInventorySourceValue<bool>.Unavailable(), SystemInventorySourceValue<bool>.Unavailable(), EmptyReadiness(info));

    private static TpmReadinessSnapshot EmptyReadiness(FirmwareCollectionInfo info) => new(info,
        SystemInventorySourceValue<uint>.Unavailable(), SystemInventorySourceValue<uint>.Unavailable(),
        SystemInventorySourceValue<bool>.Unavailable());

    internal static FirmwareCollectionInfo NativeInfo(uint code) => new(code switch
    {
        5 or 0x80070005 or 0x80041003 or 0x80284012 => FirmwareCollectionStatus.AccessDenied,
        0x80041002 or 0x8004100C or 0x8004100E or 0x80041010 or 0x8004102E or
            0x80284008 or 0x8028400B or 0x80284010 => FirmwareCollectionStatus.Unavailable,
        _ => FirmwareCollectionStatus.Failed,
    }, code);

    private static FirmwareCollectionInfo ExceptionInfo(Exception error) => error switch
    {
        FirmwareWmiNativeException native => NativeInfo(native.NativeErrorCode),
        FirmwareWmiCleanupException cleanup => new(FirmwareCollectionStatus.CleanupIncomplete, cleanup.NativeErrorCode),
        COMException native => NativeInfo(unchecked((uint)native.HResult)),
        UnauthorizedAccessException => new(FirmwareCollectionStatus.AccessDenied),
        DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException => new(FirmwareCollectionStatus.Unavailable),
        TpmTimeoutException => new(FirmwareCollectionStatus.TimedOut),
        TpmInvalidDataException or ArgumentException or OverflowException or InvalidCastException => new(FirmwareCollectionStatus.InvalidData),
        _ => new(FirmwareCollectionStatus.Failed),
    };

    private static bool Expired(long started) => Stopwatch.GetElapsedTime(started) >= CollectionDeadline;
    private static void CheckDeadline(long started, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Expired(started)) { throw new TpmTimeoutException(); }
    }
    private static void Check(int result)
    {
        if (result < 0) { throw new FirmwareWmiNativeException(unchecked((uint)result)); }
    }

    private static void SetReadSecurity(object proxy) => Check(StorageWmiNativeMethods.CoSetProxyBlanket(proxy, 10, 0, 0, 3, 3, 0, 0));

    private static bool Release(object? value)
    {
        if (value is null) { return true; }
        try { _ = Marshal.FinalReleaseComObject(value); return true; }
        catch (Exception) { return false; }
    }

    private sealed class TpmInvalidDataException : Exception;
    private sealed class TpmTimeoutException : Exception;

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("tbs.dll", ExactSpelling = true)]
        internal static extern uint Tbsi_GetDeviceInfo(uint size, out NativeDeviceInfo information);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint InterfaceType;
        public uint ImplementationRevision;
    }
}
