namespace ThirdLife.Inventory.Windows;

internal interface ITpmSource
{
    TpmSourceSnapshot Read(CancellationToken cancellationToken);
}

internal sealed record TpmSourceSnapshot(TpmDeviceInfoSnapshot Device, TpmWmiSnapshot Wmi);

internal sealed record TpmDeviceInfo(uint StructVersion, uint TpmVersion, uint InterfaceType, uint ImplementationRevision);

internal sealed record TpmDeviceInfoSnapshot(
    FirmwareCollectionInfo CollectionInfo,
    uint? ReturnCode = null,
    TpmDeviceInfo? Value = null);

internal sealed record TpmWmiSnapshot(
    FirmwareCollectionInfo CollectionInfo,
    bool? InstancePresent,
    SystemInventorySourceValue<string> SpecVersion,
    SystemInventorySourceValue<string> ManufacturerVersion,
    SystemInventorySourceValue<uint> ManufacturerId,
    SystemInventorySourceValue<bool> EnabledInitial,
    SystemInventorySourceValue<bool> ActivatedInitial,
    SystemInventorySourceValue<bool> OwnedInitial,
    TpmReadinessSnapshot Readiness,
    bool CleanupComplete = true);

internal sealed record TpmReadinessSnapshot(
    FirmwareCollectionInfo CollectionInfo,
    SystemInventorySourceValue<uint> ReturnValue,
    SystemInventorySourceValue<uint> Information,
    SystemInventorySourceValue<bool> IsReady);
