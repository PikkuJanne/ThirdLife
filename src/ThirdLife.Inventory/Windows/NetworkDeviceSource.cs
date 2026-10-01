using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

// This source identifies physical adapter classes only. It never queries connection
// state, addresses, networks, names, identities, or any configuration method.
internal sealed class WindowsNetworkDeviceSource : IDeviceClassSource
{
    internal const string LocalNamespace = @"root\StandardCimv2";
    internal const string QueryText = "SELECT HardwareInterface, NdisPhysicalMedium FROM MSFT_NetAdapter";
    internal const int EnumerationWaitMilliseconds = 100;
    internal const int MaximumEnumerationCalls = WindowsDeviceEnumerationSource.MaximumDevicesPerClass + 32;
    internal static readonly TimeSpan CollectionDeadline = TimeSpan.FromSeconds(3);
    private static readonly Guid LocatorClassId = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorInterfaceId = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    public DeviceClassSnapshot Read(DeviceClass deviceClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (deviceClass is not (DeviceClass.Wifi or DeviceClass.Ethernet)) { return Invalid(deviceClass); }
        if (!OperatingSystem.IsWindows()) { return Unavailable(deviceClass); }
        IStorageWbemLocator? locator = null;
        IStorageWbemServices? services = null;
        IStorageWbemEnumerator? enumerator = null;
        var initialized = false;
        var cleanupComplete = true;
        var cancelled = false;
        var started = Stopwatch.GetTimestamp();
        DeviceClassSnapshot snapshot;
        try
        {
            ThrowOnFailure(StorageWmiNativeMethods.CoInitializeEx(0, 0));
            initialized = true;
            ThrowOnFailure(StorageWmiNativeMethods.CoCreateInstance(in LocatorClassId, 0, 1, in LocatorInterfaceId, out locator));
            if (locator is null) { throw new InvalidDataException(); }
            cancellationToken.ThrowIfCancellationRequested();
            ThrowOnFailure(locator.ConnectServer(LocalNamespace, 0, 0, 0, 0x80, 0, 0, out services));
            if (services is null) { throw new InvalidDataException(); }
            SetReadProxySecurity(services);
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started, null)) { throw new NetworkDeviceDeadlineException(); }
            ThrowOnFailure(services.ExecQuery("WQL", QueryText, 0x30, 0, out enumerator));
            if (enumerator is null) { throw new InvalidDataException(); }
            SetReadProxySecurity(enumerator);
            snapshot = ReadRows(deviceClass, enumerator, started, ref cleanupComplete, Release, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            snapshot = Unavailable(deviceClass);
        }
        catch (NetworkDeviceDeadlineException) { snapshot = Unavailable(deviceClass); }
        catch (FirmwareWmiCleanupException error)
        {
            cleanupComplete = false;
            snapshot = new(deviceClass, DeviceEnumerationStatus.Unavailable, NativeErrorCode: error.NativeErrorCode);
        }
        catch (FirmwareWmiNativeException error) { snapshot = Failure(deviceClass, error.NativeErrorCode); }
        catch (COMException error) { snapshot = Failure(deviceClass, unchecked((uint)error.HResult)); }
        catch (UnauthorizedAccessException) { snapshot = Failure(deviceClass, 5); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            snapshot = Unavailable(deviceClass);
        }
        catch (Exception error) when (error is not OperationCanceledException) { snapshot = Invalid(deviceClass); }
        finally
        {
            cleanupComplete &= Release(enumerator);
            cleanupComplete &= Release(services);
            cleanupComplete &= Release(locator);
            if (initialized) { StorageWmiNativeMethods.CoUninitialize(); }
        }

        return CompleteRead(snapshot, cancelled, cleanupComplete, cancellationToken);
    }

    internal static DeviceClassSnapshot CompleteRead(DeviceClassSnapshot snapshot, bool cancelled,
        bool cleanupComplete, CancellationToken cancellationToken)
    {
        if (cancelled)
        {
            if (!cleanupComplete) { throw new DeviceEnumerationCleanupException(); }
            throw new OperationCanceledException(cancellationToken);
        }

        return snapshot with { CleanupComplete = cleanupComplete };
    }

    internal static DeviceClassSnapshot ReadRows(DeviceClass deviceClass, IStorageWbemEnumerator enumerator,
        long started, ref bool cleanupComplete, Func<object?, bool> release, CancellationToken cancellationToken,
        Func<long>? getTimestamp = null)
    {
        if (deviceClass is not (DeviceClass.Wifi or DeviceClass.Ethernet)) { return Invalid(deviceClass); }
        var present = false;
        var ambiguous = false;
        var rowCount = 0;
        for (var call = 0; call < MaximumEnumerationCalls; call++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started, getTimestamp)) { return Unavailable(deviceClass); }
            IStorageWbemObject? row = null;
            var ended = false;
            try
            {
                var status = enumerator.Next(EnumerationWaitMilliseconds, 1, out row, out var count);
                ThrowOnFailure(status);
                cancellationToken.ThrowIfCancellationRequested();
                if (Expired(started, getTimestamp)) { return Unavailable(deviceClass); }
                if (count > 1 || (count == 1) != (row is not null) || status is not (0 or 1 or 0x40004) ||
                    (status == 0 && count == 0))
                {
                    return Invalid(deviceClass);
                }

                if (row is not null)
                {
                    if (++rowCount > WindowsDeviceEnumerationSource.MaximumDevicesPerClass) { return Invalid(deviceClass); }
                    var classification = ClassifyRow(deviceClass, row, ref cleanupComplete, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Expired(started, getTimestamp)) { return Unavailable(deviceClass); }
                    if (classification.Status == DeviceEnumerationStatus.InvalidData) { return classification; }
                    ambiguous |= classification.Status == DeviceEnumerationStatus.Unavailable;
                    present |= classification.Present == true;
                }

                ended = status == 1;
            }
            finally { cleanupComplete &= release(row); }

            if (!cleanupComplete) { return Unavailable(deviceClass); }
            if (ended)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A known physical adapter proves presence. Unknown additional rows
                // prevent absence; they do not undo the observed adapter's existence.
                return present ? new(deviceClass, DeviceEnumerationStatus.Available, true) :
                    ambiguous ? Unavailable(deviceClass) : new(deviceClass, DeviceEnumerationStatus.Available, false);
            }
        }

        return Unavailable(deviceClass);
    }

    private static DeviceClassSnapshot ClassifyRow(DeviceClass deviceClass, IStorageWbemObject row,
        ref bool cleanupComplete, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hardware = FirmwareWmiValueReader.ReadBoolean(row, "HardwareInterface", ref cleanupComplete);
        cancellationToken.ThrowIfCancellationRequested();
        if (hardware.Status != SystemInventorySourceStatus.Available)
        {
            return hardware.Status == SystemInventorySourceStatus.InvalidData ? Invalid(deviceClass) : Unavailable(deviceClass);
        }
        if (!hardware.GetRequiredValue()) { return new(deviceClass, DeviceEnumerationStatus.Available, false); }

        var medium = FirmwareWmiValueReader.ReadUInt32(row, "NdisPhysicalMedium", ref cleanupComplete);
        cancellationToken.ThrowIfCancellationRequested();
        if (medium.Status != SystemInventorySourceStatus.Available)
        {
            return medium.Status == SystemInventorySourceStatus.InvalidData ? Invalid(deviceClass) : Unavailable(deviceClass);
        }

        var code = medium.GetRequiredValue();
        if (code > 19) { return Invalid(deviceClass); }
        if (code is 0 or 19) { return Unavailable(deviceClass); }
        // MSFT_NetAdapter's documented NdisPhysicalMedium values. In particular,
        // NdisMedium 802.3 emulation is not evidence of Ethernet hardware.
        var matches = deviceClass == DeviceClass.Wifi ? code is 1 or 9 : code == 14;
        return new(deviceClass, DeviceEnumerationStatus.Available, matches);
    }

    internal static DeviceClassSnapshot Failure(DeviceClass deviceClass, uint code) => new(deviceClass, code switch
    {
        5 or 1314 or 0x80070005 or 0x80041003 => DeviceEnumerationStatus.AccessDenied,
        0x80041002 or 0x8004100e or 0x80041010 or 0x8004100c or 0x800706ba => DeviceEnumerationStatus.Unavailable,
        _ => DeviceEnumerationStatus.InvalidData,
    }, NativeErrorCode: code);

    private static DeviceClassSnapshot Invalid(DeviceClass deviceClass) => new(deviceClass, DeviceEnumerationStatus.InvalidData);
    private static DeviceClassSnapshot Unavailable(DeviceClass deviceClass) => new(deviceClass, DeviceEnumerationStatus.Unavailable);
    private static bool Expired(long started, Func<long>? getTimestamp) =>
        Stopwatch.GetElapsedTime(started, getTimestamp?.Invoke() ?? Stopwatch.GetTimestamp()) >= CollectionDeadline;
    private static void SetReadProxySecurity(object proxy) => ThrowOnFailure(
        StorageWmiNativeMethods.CoSetProxyBlanket(proxy, 10, 0, 0, 3, 3, 0, 0));

    private static void ThrowOnFailure(int result)
    {
        if (result < 0) { throw new FirmwareWmiNativeException(unchecked((uint)result)); }
    }

    private static bool Release(object? instance)
    {
        if (instance is null) { return true; }
        try { _ = Marshal.FinalReleaseComObject(instance); return true; }
        catch (Exception) { return false; }
    }

    private sealed class NetworkDeviceDeadlineException : Exception;
}
