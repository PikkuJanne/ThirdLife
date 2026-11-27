using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsStorageWmiSource : IStorageWmiSource
{
    internal const string LocalNamespace = "root\\Microsoft\\Windows\\Storage";
    internal const int EnumerationWaitMilliseconds = 100;
    internal const int MaximumEnumerationCalls = 128;
    internal static readonly TimeSpan CollectionDeadline = TimeSpan.FromSeconds(6);

    private const uint RpcPacketPrivacy = 6;
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

    private static readonly StorageWmiField[] SystemPartitionFields = [new("DiskNumber", StorageWmiValueKind.UInt32)];
    private static readonly StorageWmiField[] SystemDiskFields =
    [
        new("Number", StorageWmiValueKind.UInt32),
        new("HealthStatus", StorageWmiValueKind.UInt16),
    ];

    public StorageWmiSnapshot Read(CancellationToken cancellationToken) => Read(null, cancellationToken);

    internal static SystemInventorySourceValue<ushort> ReadSystemDiskHealth(char? driveLetter, CancellationToken cancellationToken)
    {
        if (driveLetter is not (>= 'A' and <= 'Z'))
        {
            return SystemInventorySourceValue<ushort>.Unavailable();
        }

        var snapshot = Read(driveLetter, cancellationToken);
        return snapshot.CleanupComplete
            ? snapshot.SystemDiskHealth ?? StorageSourceFailures.Field<ushort>(snapshot.PhysicalDisks.CollectionInfo)
            : SystemInventorySourceValue<ushort>.Unavailable();
    }

    private static StorageWmiSnapshot Read(char? systemDriveLetter, CancellationToken cancellationToken)
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

            if (systemDriveLetter.HasValue)
            {
                ThrowOnFailure(StorageWmiNativeMethods.SetServicesProxyBlanket(services, 10, 0, 0, RpcPacketPrivacy, 3, 0, 0));
            }
            else
            {
                SetReadProxySecurity(services);
            }
            snapshot = systemDriveLetter is { } driveLetter
                ? FailureSnapshot(StorageSourceFailures.Unavailable) with
                {
                    SystemDiskHealth = ReadSystemDiskHealth(services, driveLetter, started, cancellationToken, ref cleanupComplete),
                }
                : ReadPhysicalStorage(services, started, cancellationToken, ref cleanupComplete);
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

    private static StorageWmiSnapshot ReadPhysicalStorage(IStorageWbemServices services, long started,
        CancellationToken cancellationToken, ref bool cleanupComplete)
    {
        var physical = Query(services, StorageWmiQuery.PhysicalDisks, started, cancellationToken, ref cleanupComplete);
        var counters = physical.CollectionInfo.Status == StorageCollectionStatus.Collected
            ? Query(services, StorageWmiQuery.ReliabilityCounters, started, cancellationToken, ref cleanupComplete)
            : Empty(StorageSourceFailures.Unavailable);
        var associations = physical.CollectionInfo.Status == StorageCollectionStatus.Collected &&
            counters.CollectionInfo.Status == StorageCollectionStatus.Collected
            ? Query(services, StorageWmiQuery.PhysicalDiskCounterAssociations, started, cancellationToken, ref cleanupComplete)
            : Empty(StorageSourceFailures.Unavailable);
        return new(physical, counters, associations);
    }

    internal static string QueryText(StorageWmiQuery query, char? driveLetter = null, uint? diskNumber = null) => query switch
    {
        // ObjectId is projected only so WMI can form the transient routing paths.
        // No ObjectId value or other identifying/display property is read into a snapshot.
        StorageWmiQuery.PhysicalDisks =>
            "SELECT ObjectId,Size,MediaType,BusType,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk",
        StorageWmiQuery.ReliabilityCounters =>
            "SELECT ObjectId,Wear,Temperature,ReadErrorsTotal,ReadErrorsUncorrected,WriteErrorsTotal,WriteErrorsUncorrected,PowerOnHours FROM MSFT_StorageReliabilityCounter",
        StorageWmiQuery.PhysicalDiskCounterAssociations =>
            "SELECT PhysicalDisk,StorageReliabilityCounter FROM MSFT_PhysicalDiskToStorageReliabilityCounter",
        StorageWmiQuery.SystemPartition when driveLetter is >= 'A' and <= 'Z' =>
            $"SELECT DiskNumber FROM MSFT_Partition WHERE IsBoot = TRUE AND IsOffline = FALSE AND DriveLetter = '{driveLetter}'",
        StorageWmiQuery.SystemDisk when diskNumber.HasValue => string.Create(CultureInfo.InvariantCulture,
            $"SELECT Number,HealthStatus FROM MSFT_Disk WHERE Number = {diskNumber.Value} AND IsOffline = FALSE AND IsBoot = TRUE"),
        _ => throw new ArgumentOutOfRangeException(nameof(query)),
    };

    private static SystemInventorySourceValue<ushort> ReadSystemDiskHealth(IStorageWbemServices services,
        char driveLetter, long started, CancellationToken cancellationToken, ref bool cleanupComplete)
    {
        var partition = Query(services, StorageWmiQuery.SystemPartition, started, cancellationToken,
            ref cleanupComplete, driveLetter);
        var partitionDisk = ResolveSystemPartitionDisk(partition);
        if (partitionDisk.Status != SystemInventorySourceStatus.Available || !cleanupComplete)
        {
            return partitionDisk.Status switch
            {
                SystemInventorySourceStatus.AccessDenied => SystemInventorySourceValue<ushort>.AccessDenied(),
                SystemInventorySourceStatus.InvalidData => SystemInventorySourceValue<ushort>.InvalidData(),
                _ => SystemInventorySourceValue<ushort>.Unavailable(),
            };
        }

        var diskNumber = partitionDisk.GetRequiredValue();
        var disk = Query(services, StorageWmiQuery.SystemDisk, started, cancellationToken,
            ref cleanupComplete, diskNumber: diskNumber);
        return ResolveSystemDiskHealth(disk, diskNumber);
    }

    internal static SystemInventorySourceValue<uint> ResolveSystemPartitionDisk(StorageWmiQueryResult partition)
    {
        if (partition.CollectionInfo.Status != StorageCollectionStatus.Collected || partition.Rows.Count == 0)
        {
            return StorageSourceFailures.Field<uint>(partition.CollectionInfo);
        }

        return partition.Rows is [var selected] && selected.Fields.GetValueOrDefault("DiskNumber") is uint diskNumber
            ? SystemInventorySourceValue<uint>.Available(diskNumber)
            : SystemInventorySourceValue<uint>.InvalidData();
    }

    internal static SystemInventorySourceValue<ushort> ResolveSystemDiskHealth(StorageWmiQueryResult disk, uint diskNumber)
    {
        if (disk.CollectionInfo.Status != StorageCollectionStatus.Collected || disk.Rows.Count == 0)
        {
            return StorageSourceFailures.Field<ushort>(disk.CollectionInfo);
        }

        if (disk.Rows is not [var selected] || !selected.Fields.TryGetValue("Number", out var number) ||
            number is not uint observedNumber || observedNumber != diskNumber)
        {
            return SystemInventorySourceValue<ushort>.InvalidData();
        }

        return selected.Fields.GetValueOrDefault("HealthStatus") switch
        {
            ushort health when health <= 2 => SystemInventorySourceValue<ushort>.Available(health),
            null or ushort => SystemInventorySourceValue<ushort>.Unavailable(),
            _ => SystemInventorySourceValue<ushort>.InvalidData(),
        };
    }

    private static StorageWmiQueryResult Query(
        IStorageWbemServices services,
        StorageWmiQuery query,
        long started,
        CancellationToken cancellationToken,
        ref bool cleanupComplete,
        char? driveLetter = null,
        uint? diskNumber = null)
    {
        IStorageWbemEnumerator? enumerator = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= CollectionDeadline)
            {
                return Empty(new(StorageCollectionStatus.TimedOut));
            }

            // RETURN_IMMEDIATELY | FORWARD_ONLY; existing associations also need ENSURE_LOCATABLE.
            var flags = query is StorageWmiQuery.SystemPartition or StorageWmiQuery.SystemDisk ? 0x30 : 0x130;
            ThrowOnFailure(services.ExecQuery("WQL", QueryText(query, driveLetter, diskNumber), flags, 0, out enumerator));
            if (enumerator is null)
            {
                return Empty(StorageSourceFailures.InvalidData);
            }

            if (query is StorageWmiQuery.SystemPartition or StorageWmiQuery.SystemDisk)
            {
                ThrowOnFailure(StorageWmiNativeMethods.SetEnumeratorProxyBlanket(enumerator, 10, 0, 0, RpcPacketPrivacy, 3, 0, 0));
            }
            else
            {
                SetReadProxySecurity(enumerator);
            }
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
        var maximum = query switch
        {
            StorageWmiQuery.PhysicalDisks => WindowsStorageSource.MaximumDisks,
            StorageWmiQuery.SystemPartition or StorageWmiQuery.SystemDisk => 1,
            _ => WindowsStorageSource.MaximumRelatedRows,
        };
        var fields = query switch
        {
            StorageWmiQuery.PhysicalDisks => PhysicalFields,
            StorageWmiQuery.ReliabilityCounters => CounterFields,
            StorageWmiQuery.PhysicalDiskCounterAssociations => AssociationFields,
            StorageWmiQuery.SystemPartition => SystemPartitionFields,
            StorageWmiQuery.SystemDisk => SystemDiskFields,
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
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
