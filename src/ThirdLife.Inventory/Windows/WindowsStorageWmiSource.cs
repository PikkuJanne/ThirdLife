using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsStorageWmiSource : IStorageWmiSource
{
    internal const string LocalNamespace = "root\\Microsoft\\Windows\\Storage";
    internal const int EnumerationWaitMilliseconds = 100;
    internal const int MaximumEnumerationCalls = 128;
    internal static readonly TimeSpan CollectionDeadline = TimeSpan.FromSeconds(6);

    private static readonly Guid LocatorClassId = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorInterfaceId = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    private static readonly StorageWmiField[] PhysicalFields =
    [
        new("__PATH", StorageWmiValueKind.RoutingString),
        new("__RELPATH", StorageWmiValueKind.RoutingString),
        new("Size", StorageWmiValueKind.UInt64),
        new("MediaType", StorageWmiValueKind.UInt16),
        new("BusType", StorageWmiValueKind.UInt16),
        new("HealthStatus", StorageWmiValueKind.UInt16),
        new("OperationalStatus", StorageWmiValueKind.UInt16Array),
    ];

    private static readonly StorageWmiField[] CounterFields =
    [
        new("__PATH", StorageWmiValueKind.RoutingString),
        new("__RELPATH", StorageWmiValueKind.RoutingString),
        new("Wear", StorageWmiValueKind.UInt8),
        new("Temperature", StorageWmiValueKind.UInt8),
        new("ReadErrorsTotal", StorageWmiValueKind.UInt64),
        new("ReadErrorsUncorrected", StorageWmiValueKind.UInt64),
        new("WriteErrorsTotal", StorageWmiValueKind.UInt64),
        new("WriteErrorsUncorrected", StorageWmiValueKind.UInt64),
        new("PowerOnHours", StorageWmiValueKind.UInt16),
    ];

    private static readonly StorageWmiField[] AssociationFields =
    [
        new("PhysicalDisk", StorageWmiValueKind.Reference),
        new("StorageReliabilityCounter", StorageWmiValueKind.Reference),
    ];

    public StorageWmiSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return FailureSnapshot(StorageSourceFailures.Unavailable);
        }

        IStorageWbemLocator? locator = null;
        IStorageWbemServices? services = null;
        var initialized = false;
        var cleanupComplete = true;
        var started = Stopwatch.GetTimestamp();
        StorageWmiSnapshot snapshot;
        try
        {
            var initialization = StorageWmiNativeMethods.CoInitializeEx(0, 0);
            ThrowOnFailure(initialization);
            initialized = true;
            ThrowOnFailure(StorageWmiNativeMethods.CoCreateInstance(
                in LocatorClassId, 0, 1, in LocatorInterfaceId, out locator));
            if (locator is null)
            {
                throw new StorageWmiInvalidDataException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            ThrowOnFailure(locator.ConnectServer(LocalNamespace, 0, 0, 0, 0x80, 0, 0, out services));
            if (services is null)
            {
                throw new StorageWmiInvalidDataException();
            }

            SetReadProxySecurity(services);
            var physical = Query(services, StorageWmiQuery.PhysicalDisks, started, cancellationToken, ref cleanupComplete);
            var counters = physical.CollectionInfo.Status == StorageCollectionStatus.Collected
                ? Query(services, StorageWmiQuery.ReliabilityCounters, started, cancellationToken, ref cleanupComplete)
                : Empty(StorageSourceFailures.Unavailable);
            var associations = physical.CollectionInfo.Status == StorageCollectionStatus.Collected &&
                counters.CollectionInfo.Status == StorageCollectionStatus.Collected
                ? Query(services, StorageWmiQuery.PhysicalDiskCounterAssociations, started, cancellationToken, ref cleanupComplete)
                : Empty(StorageSourceFailures.Unavailable);
            snapshot = new(physical, counters, associations);
        }
        catch (StorageWmiNativeException error)
        {
            snapshot = FailureSnapshot(StorageSourceFailures.FromNativeError(error.NativeError));
        }
        catch (COMException error)
        {
            snapshot = FailureSnapshot(StorageSourceFailures.FromNativeError(unchecked((uint)error.HResult)));
        }
        catch (UnauthorizedAccessException)
        {
            snapshot = FailureSnapshot(new(StorageCollectionStatus.AccessDenied));
        }
        catch (StorageWmiInvalidDataException)
        {
            snapshot = FailureSnapshot(StorageSourceFailures.InvalidData);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            snapshot = FailureSnapshot(StorageSourceFailures.Unavailable);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            snapshot = FailureSnapshot(new(StorageCollectionStatus.Failed));
        }
        finally
        {
            cleanupComplete &= Release(services);
            cleanupComplete &= Release(locator);
            if (initialized)
            {
                StorageWmiNativeMethods.CoUninitialize();
            }
        }

        return snapshot with { CleanupComplete = cleanupComplete };
    }

    internal static string QueryText(StorageWmiQuery query) => query switch
    {
        // ObjectId is projected only so WMI can form the transient routing paths.
        // No ObjectId value or other identifying/display property is read into a snapshot.
        StorageWmiQuery.PhysicalDisks =>
            "SELECT ObjectId,Size,MediaType,BusType,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk",
        StorageWmiQuery.ReliabilityCounters =>
            "SELECT ObjectId,Wear,Temperature,ReadErrorsTotal,ReadErrorsUncorrected,WriteErrorsTotal,WriteErrorsUncorrected,PowerOnHours FROM MSFT_StorageReliabilityCounter",
        StorageWmiQuery.PhysicalDiskCounterAssociations =>
            "SELECT PhysicalDisk,StorageReliabilityCounter FROM MSFT_PhysicalDiskToStorageReliabilityCounter",
        _ => throw new ArgumentOutOfRangeException(nameof(query)),
    };

    private static StorageWmiQueryResult Query(
        IStorageWbemServices services,
        StorageWmiQuery query,
        long started,
        CancellationToken cancellationToken,
        ref bool cleanupComplete)
    {
        IStorageWbemEnumerator? enumerator = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= CollectionDeadline)
            {
                return Empty(new(StorageCollectionStatus.TimedOut));
            }

            // RETURN_IMMEDIATELY | FORWARD_ONLY | ENSURE_LOCATABLE.
            ThrowOnFailure(services.ExecQuery("WQL", QueryText(query), 0x130, 0, out enumerator));
            if (enumerator is null)
            {
                return Empty(StorageSourceFailures.InvalidData);
            }

            SetReadProxySecurity(enumerator);
            return ReadRows(enumerator, query, started, ref cleanupComplete, Release, cancellationToken);
        }
        catch (StorageWmiNativeException error)
        {
            return Empty(StorageSourceFailures.FromNativeError(error.NativeError));
        }
        catch (COMException error)
        {
            return Empty(StorageSourceFailures.FromNativeError(unchecked((uint)error.HResult)));
        }
        catch (StorageWmiCleanupException error)
        {
            return Empty(new(StorageCollectionStatus.CleanupIncomplete, error.NativeError));
        }
        catch (StorageWmiTimeoutException)
        {
            return Empty(new(StorageCollectionStatus.TimedOut));
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or InvalidCastException)
        {
            return Empty(StorageSourceFailures.InvalidData);
        }
        finally
        {
            cleanupComplete &= Release(enumerator);
        }
    }

    internal static StorageWmiQueryResult ReadRows(
        IStorageWbemEnumerator enumerator,
        StorageWmiQuery query,
        long started,
        ref bool cleanupComplete,
        Func<object?, bool> release,
        CancellationToken cancellationToken)
    {
        var maximum = query == StorageWmiQuery.PhysicalDisks
            ? WindowsStorageSource.MaximumDisks
            : WindowsStorageSource.MaximumRelatedRows;
        var fields = query switch
        {
            StorageWmiQuery.PhysicalDisks => PhysicalFields,
            StorageWmiQuery.ReliabilityCounters => CounterFields,
            _ => AssociationFields,
        };
        var rows = new List<StorageWmiRow>(maximum);
        for (var call = 0; call < MaximumEnumerationCalls; call++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= CollectionDeadline)
            {
                return Empty(new(StorageCollectionStatus.TimedOut));
            }

            IStorageWbemObject? row = null;
            try
            {
                var status = enumerator.Next(EnumerationWaitMilliseconds, 1, out row, out var count);
                ThrowOnFailure(status);
                if (count > 1 || (count == 1) != (row is not null) || status is not (0 or 1 or 0x40004) ||
                    (status == 0 && count == 0))
                {
                    return Empty(StorageSourceFailures.InvalidData);
                }

                if (row is not null)
                {
                    if (rows.Count == maximum)
                    {
                        return Empty(StorageSourceFailures.InvalidData);
                    }

                    rows.Add(ReadRow(row, fields, started, cancellationToken, ref cleanupComplete));
                }

                if (status == 1)
                {
                    return new(StorageSourceFailures.Collected, rows.AsReadOnly());
                }
            }
            finally
            {
                cleanupComplete &= release(row);
            }
        }

        return Empty(new(StorageCollectionStatus.TimedOut));
    }

    private static StorageWmiRow ReadRow(
        IStorageWbemObject row,
        StorageWmiField[] fields,
        long started,
        CancellationToken cancellationToken,
        ref bool cleanupComplete)
    {
        var values = new Dictionary<string, object?>(fields.Length, StringComparer.Ordinal);
        var variant = Marshal.AllocHGlobal(StorageWmiValueReader.VariantBytes);
        try
        {
            foreach (var field in fields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started) >= CollectionDeadline)
                {
                    throw new StorageWmiTimeoutException();
                }

                for (var offset = 0; offset < StorageWmiValueReader.VariantBytes; offset += 8)
                {
                    Marshal.WriteInt64(variant, offset, 0);
                }

                var clearResult = 0;
                try
                {
                    var result = row.Get(field.Name, 0, variant, out var cimType, 0);
                    if (result == unchecked((int)0x80041002))
                    {
                        values.Add(field.Name, null);
                    }
                    else
                    {
                        ThrowOnFailure(result);
                        values.Add(field.Name, StorageWmiValueReader.Read(variant, cimType, field.Kind));
                    }
                }
                finally
                {
                    // This buffer was initialized even on Get failure. Native BSTR/SAFEARRAY
                    // allocations can precede validation; their initial allocation is WMI-owned.
                    clearResult = StorageWmiNativeMethods.VariantClear(variant);
                    if (clearResult != 0)
                    {
                        cleanupComplete = false;
                    }
                }

                if (clearResult != 0)
                {
                    throw new StorageWmiCleanupException(unchecked((uint)clearResult));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }

        return new(values);
    }

    private static void SetReadProxySecurity(object proxy) => ThrowOnFailure(
        StorageWmiNativeMethods.CoSetProxyBlanket(proxy, 10, 0, 0, 3, 3, 0, 0));

    private static void ThrowOnFailure(int result)
    {
        if (result < 0)
        {
            throw new StorageWmiNativeException(unchecked((uint)result));
        }
    }

    private static bool Release(object? instance)
    {
        if (instance is null)
        {
            return true;
        }

        try
        {
            _ = Marshal.FinalReleaseComObject(instance);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static StorageWmiQueryResult Empty(StorageCollectionInfo info) => new(info, []);

    private static StorageWmiSnapshot FailureSnapshot(StorageCollectionInfo info) =>
        new(Empty(info), Empty(StorageSourceFailures.Unavailable), Empty(StorageSourceFailures.Unavailable));

    private sealed class StorageWmiNativeException(uint error) : Exception("The storage WMI read failed.")
    {
        public uint NativeError { get; } = error;
    }

    private sealed class StorageWmiCleanupException(uint error) : Exception("The storage WMI cleanup failed.")
    {
        public uint NativeError { get; } = error;
    }

    private sealed class StorageWmiInvalidDataException : Exception;

    private sealed class StorageWmiTimeoutException : Exception;
}
