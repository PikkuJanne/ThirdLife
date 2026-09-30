namespace ThirdLife.Inventory.Windows;

internal enum StorageCollectionStatus
{
    Collected = 1,
    Unavailable,
    AccessDenied,
    InvalidData,
    Failed,
    TimedOut,
    CleanupIncomplete,
}

internal sealed record StorageCollectionInfo(StorageCollectionStatus Status, uint? NativeErrorCode = null);

internal sealed record StorageReliabilitySnapshot(
    StorageCollectionInfo CollectionInfo,
    SystemInventorySourceValue<byte> Wear,
    SystemInventorySourceValue<byte> Temperature,
    SystemInventorySourceValue<ulong> ReadErrorsTotal,
    SystemInventorySourceValue<ulong> ReadErrorsUncorrected,
    SystemInventorySourceValue<ulong> WriteErrorsTotal,
    SystemInventorySourceValue<ulong> WriteErrorsUncorrected,
    SystemInventorySourceValue<ushort> PowerOnHours);

internal sealed record StorageDiskSnapshot(
    int Ordinal,
    SystemInventorySourceValue<ulong> CapacityBytes,
    SystemInventorySourceValue<ushort> MediaType,
    SystemInventorySourceValue<ushort> BusType,
    SystemInventorySourceValue<ushort> HealthStatus,
    SystemInventorySourceValue<IReadOnlyList<ushort>> OperationalStatus,
    StorageReliabilitySnapshot Reliability);

internal sealed record StorageVolumeSnapshot(
    StorageCollectionInfo CollectionInfo,
    SystemInventorySourceValue<ulong> CallerTotalBytes,
    SystemInventorySourceValue<ulong> CallerAvailableBytes,
    SystemInventorySourceValue<ulong> TotalFreeBytes);

internal sealed record StorageSourceSnapshot(
    StorageCollectionInfo DiskEnumeration,
    IReadOnlyList<StorageDiskSnapshot> Disks,
    StorageVolumeSnapshot SystemVolume);

internal interface IStorageSource
{
    StorageSourceSnapshot Read(CancellationToken cancellationToken);
}
