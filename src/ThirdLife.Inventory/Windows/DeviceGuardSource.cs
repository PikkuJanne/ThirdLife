using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed record DeviceGuardSourceSnapshot
{
    internal DeviceGuardSourceSnapshot(SystemInventorySourceValue<IReadOnlyList<uint>> AvailableSecurityProperties,
        uint? NativeErrorCode = null, bool CleanupComplete = true)
    {
        ArgumentNullException.ThrowIfNull(AvailableSecurityProperties);
        if (AvailableSecurityProperties.Status == SystemInventorySourceStatus.Available)
        {
            var values = AvailableSecurityProperties.GetRequiredValue().Take(WindowsDeviceGuardSource.MaximumProperties + 1).ToArray();
            AvailableSecurityProperties = values.Length > WindowsDeviceGuardSource.MaximumProperties
                ? SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData()
                : SystemInventorySourceValue<IReadOnlyList<uint>>.Available(Array.AsReadOnly(values));
        }

        this.AvailableSecurityProperties = AvailableSecurityProperties;
        this.NativeErrorCode = NativeErrorCode;
        this.CleanupComplete = CleanupComplete;
    }

    public SystemInventorySourceValue<IReadOnlyList<uint>> AvailableSecurityProperties { get; }
    public uint? NativeErrorCode { get; }
    public bool CleanupComplete { get; init; }
}

internal interface IDeviceGuardSource
{
    DeviceGuardSourceSnapshot Read(CancellationToken cancellationToken);
}

internal sealed class WindowsDeviceGuardSource : IDeviceGuardSource
{
    internal const string LocalNamespace = @"root\Microsoft\Windows\DeviceGuard";
    internal const string QueryText = "SELECT AvailableSecurityProperties FROM Win32_DeviceGuard";
    internal const int MaximumProperties = 16;
    internal const int EnumerationWaitMilliseconds = 100;
    internal const int MaximumEnumerationCalls = 32;
    internal static readonly TimeSpan CollectionDeadline = TimeSpan.FromSeconds(3);
    private static readonly Guid LocatorClassId = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorInterfaceId = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    public DeviceGuardSourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) { return Unavailable(); }
        IStorageWbemLocator? locator = null;
        IStorageWbemServices? services = null;
        IStorageWbemEnumerator? enumerator = null;
        var initialized = false;
        var cleanupComplete = true;
        var cancelled = false;
        var started = Stopwatch.GetTimestamp();
        DeviceGuardSourceSnapshot snapshot;
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
            if (Stopwatch.GetElapsedTime(started) >= CollectionDeadline) { throw new DeviceGuardDeadlineException(); }
            // Semisynchronous and forward-only. No identity/location projection is needed.
            ThrowOnFailure(services.ExecQuery("WQL", QueryText, 0x30, 0, out enumerator));
            if (enumerator is null) { throw new InvalidDataException(); }
            SetReadProxySecurity(enumerator);
            snapshot = ReadRows(enumerator, started, ref cleanupComplete, Release, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            snapshot = Unavailable();
        }
        catch (DeviceGuardDeadlineException)
        {
            snapshot = Unavailable();
        }
        catch (FirmwareWmiCleanupException error)
        {
            cleanupComplete = false;
            snapshot = new(SystemInventorySourceValue<IReadOnlyList<uint>>.Unavailable(), error.NativeErrorCode);
        }
        catch (FirmwareWmiNativeException error)
        {
            snapshot = Failure(error.NativeErrorCode);
        }
        catch (COMException error)
        {
            snapshot = Failure(unchecked((uint)error.HResult));
        }
        catch (UnauthorizedAccessException)
        {
            snapshot = Failure(5);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            snapshot = Unavailable();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            snapshot = Invalid();
        }
        finally
        {
            cleanupComplete &= Release(enumerator);
            cleanupComplete &= Release(services);
            cleanupComplete &= Release(locator);
            if (initialized) { StorageWmiNativeMethods.CoUninitialize(); }
        }

        return CompleteRead(snapshot, cancelled, cleanupComplete, cancellationToken);
    }

    internal static DeviceGuardSourceSnapshot CompleteRead(DeviceGuardSourceSnapshot snapshot, bool cancelled,
        bool cleanupComplete, CancellationToken cancellationToken)
    {
        if (cancelled)
        {
            if (!cleanupComplete) { throw new FirmwareSecurityCleanupException(); }
            throw new OperationCanceledException(cancellationToken);
        }

        return snapshot with { CleanupComplete = cleanupComplete };
    }

    internal static DeviceGuardSourceSnapshot ReadRows(IStorageWbemEnumerator enumerator, long started,
        ref bool cleanupComplete, Func<object?, bool> release, CancellationToken cancellationToken,
        Func<long>? getTimestamp = null)
    {
        SystemInventorySourceValue<IReadOnlyList<uint>>? values = null;
        for (var call = 0; call < MaximumEnumerationCalls; call++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Expired(started, getTimestamp)) { return Unavailable(); }
            IStorageWbemObject? row = null;
            try
            {
                var status = enumerator.Next(EnumerationWaitMilliseconds, 1, out row, out var count);
                ThrowOnFailure(status);
                cancellationToken.ThrowIfCancellationRequested();
                if (Expired(started, getTimestamp)) { return Unavailable(); }
                if (count > 1 || (count == 1) != (row is not null) || status is not (0 or 1 or 0x40004) ||
                    (status == 0 && count == 0))
                {
                    return Invalid();
                }

                if (row is not null)
                {
                    if (values is not null) { return Invalid(); }
                    cancellationToken.ThrowIfCancellationRequested();
                    values = FirmwareWmiValueReader.ReadUInt32Array(row, "AvailableSecurityProperties", ref cleanupComplete, MaximumProperties);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Expired(started, getTimestamp)) { return Unavailable(); }
                }

                if (status == 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return values is null ? Unavailable() : new(values);
                }
            }
            finally
            {
                cleanupComplete &= release(row);
            }

            if (!cleanupComplete) { return Unavailable(); }
        }

        return Unavailable();
    }

    internal static DeviceGuardSourceSnapshot Failure(uint code) => new(code switch
    {
        5 or 1314 or 0x80070005 or 0x80041003 => SystemInventorySourceValue<IReadOnlyList<uint>>.AccessDenied(),
        0x80041002 or 0x8004100e or 0x80041010 or 0x8004100c or 0x800706ba => SystemInventorySourceValue<IReadOnlyList<uint>>.Unavailable(),
        _ => SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData(),
    }, code);

    private static DeviceGuardSourceSnapshot Invalid() => new(SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData());
    private static DeviceGuardSourceSnapshot Unavailable() => new(SystemInventorySourceValue<IReadOnlyList<uint>>.Unavailable());
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

    private sealed class DeviceGuardDeadlineException : Exception;
}
