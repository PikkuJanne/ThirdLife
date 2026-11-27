namespace ThirdLife.Inventory.Windows;

internal enum StorageWmiQuery
{
    PhysicalDisks,
    ReliabilityCounters,
    PhysicalDiskCounterAssociations,
    SystemPartition,
    SystemDisk,
}

internal enum StorageWmiValueKind
{
    RoutingString,
    Reference,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    UInt16Array,
}

internal sealed record StorageWmiField(string Name, StorageWmiValueKind Kind);

internal sealed record StorageWmiRow(IReadOnlyDictionary<string, object?> Fields);

internal sealed record StorageWmiQueryResult(StorageCollectionInfo CollectionInfo, IReadOnlyList<StorageWmiRow> Rows);

internal sealed record StorageWmiSnapshot(
    StorageWmiQueryResult PhysicalDisks,
    StorageWmiQueryResult ReliabilityCounters,
    StorageWmiQueryResult Associations,
    bool CleanupComplete = true,
    SystemInventorySourceValue<ushort>? SystemDiskHealth = null);

internal interface IStorageWmiSource
{
    StorageWmiSnapshot Read(CancellationToken cancellationToken);
}

internal interface IStorageVolumeSource
{
    StorageVolumeSnapshot Read(CancellationToken cancellationToken);
}

internal sealed class StorageInvalidValue
{
    public static StorageInvalidValue Instance { get; } = new();

    private StorageInvalidValue()
    {
    }
}

internal static class StorageSourceFailures
{
    internal static readonly StorageCollectionInfo Collected = new(StorageCollectionStatus.Collected);
    internal static readonly StorageCollectionInfo Unavailable = new(StorageCollectionStatus.Unavailable);
    internal static readonly StorageCollectionInfo InvalidData = new(StorageCollectionStatus.InvalidData);

    internal static StorageCollectionInfo FromNativeError(uint code) => new(code switch
    {
        5 or 0x80070005 or 0x80041003 => StorageCollectionStatus.AccessDenied,
        1 or 2 or 3 or 50 or 120 or 1168 or 0x8004100e or 0x80041010 or 0x8004100c or 0x80040154 =>
            StorageCollectionStatus.Unavailable,
        _ => StorageCollectionStatus.Failed,
    }, code);

    internal static SystemInventorySourceValue<T> Field<T>(StorageCollectionInfo info)
        where T : notnull => info.Status switch
        {
            StorageCollectionStatus.AccessDenied => SystemInventorySourceValue<T>.AccessDenied(),
            StorageCollectionStatus.InvalidData => SystemInventorySourceValue<T>.InvalidData(),
            _ => SystemInventorySourceValue<T>.Unavailable(),
        };

    internal static StorageReliabilitySnapshot Reliability(StorageCollectionInfo info) => new(
        info,
        Field<byte>(info),
        Field<byte>(info),
        Field<ulong>(info),
        Field<ulong>(info),
        Field<ulong>(info),
        Field<ulong>(info),
        Field<ushort>(info));

    internal static StorageVolumeSnapshot Volume(StorageCollectionInfo info) => new(
        info, Field<ulong>(info), Field<ulong>(info), Field<ulong>(info));
}
