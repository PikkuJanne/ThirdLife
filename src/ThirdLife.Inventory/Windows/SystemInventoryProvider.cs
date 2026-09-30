using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

public sealed class SystemInventoryProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-system-inventory";
    internal const int MaximumLogicalProcessorCount = 65_536;
    internal const long MaximumInstalledMemoryBytes = 1L << 50;

    private static readonly EvidenceKey DeviceManufacturerKey = new("device.manufacturer");
    private static readonly EvidenceKey DeviceModelKey = new("device.model");
    private static readonly EvidenceKey DeviceSerialNumberKey = new("device.serial_number");
    private static readonly EvidenceKey DeviceTypeKey = new("device.type");
    private static readonly EvidenceKey ProcessorManufacturerKey = new("processor.manufacturer");
    private static readonly EvidenceKey ProcessorModelKey = new("processor.model");
    private static readonly EvidenceKey ProcessorLogicalCountKey = new("processor.logical_count");
    private static readonly EvidenceKey SystemArchitectureKey = new("system.architecture");
    private static readonly EvidenceKey InstalledMemoryBytesKey = new("memory.installed_bytes");

    private const string Type1Source = "system_inventory.smbios.type1";
    private const string Type3Source = "system_inventory.smbios.type3";
    private const string Type4Source = "system_inventory.smbios.type4";
    private const string ArchitectureSource = "system_inventory.dotnet.os_architecture";
    private const string InstalledMemorySource = "system_inventory.kernel32.installed_memory";
    private const string LogicalProcessorSource = "system_inventory.kernel32.logical_processors";

    private readonly ISystemInventorySource _source;

    public SystemInventoryProvider()
        : this(new WindowsSystemInventorySource(), ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal SystemInventoryProvider(
        ISystemInventorySource source,
        ProviderEvidenceOrigin evidenceOrigin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Descriptor = CreateDescriptor(evidenceOrigin);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await Task.Run(
            () => _source.Read(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        using (snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(snapshot, cancellationToken);
        }
    }

    private static InventoryProviderDescriptor CreateDescriptor(ProviderEvidenceOrigin evidenceOrigin) =>
        new(
            new ProviderId(ProviderIdentity),
            ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(2),
            ProviderNetworkUse.None,
            evidenceOrigin,
            [ProviderOperatingSystem.Windows10, ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(
                new EvidenceKey("system_inventory.provider_status"),
                "system_inventory.provider.status"),
            [
                Definition(DeviceManufacturerKey, EvidenceValueKind.Text, Type1Source),
                Definition(DeviceModelKey, EvidenceValueKind.Text, Type1Source),
                Definition(DeviceSerialNumberKey, EvidenceValueKind.Text, Type1Source),
                Definition(DeviceTypeKey, EvidenceValueKind.Enum, Type3Source),
                Definition(
                    ProcessorManufacturerKey,
                    EvidenceValueKind.Text,
                    Type4Source,
                    maximumCardinality: SmbiosInventoryParser.MaximumProcessorRecords),
                Definition(
                    ProcessorModelKey,
                    EvidenceValueKind.Text,
                    Type4Source,
                    maximumCardinality: SmbiosInventoryParser.MaximumProcessorRecords),
                Definition(
                    ProcessorLogicalCountKey,
                    EvidenceValueKind.WholeNumber,
                    LogicalProcessorSource,
                    unit: "count"),
                Definition(SystemArchitectureKey, EvidenceValueKind.Enum, ArchitectureSource),
                Definition(
                    InstalledMemoryBytesKey,
                    EvidenceValueKind.WholeNumber,
                    InstalledMemorySource,
                    unit: "bytes"),
            ]);

    private static ProviderEvidenceDefinition Definition(
        EvidenceKey key,
        EvidenceValueKind kind,
        string source,
        string? unit = null,
        int maximumCardinality = 1) =>
        new(key, kind, unit, source, maximumCardinality);

    private static ProviderReadResult Normalize(
        SystemInventorySourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var evidence = new List<NormalizedEvidence>();
        var sourceStatuses = new[]
        {
            snapshot.FirmwareTable.Status,
            snapshot.NativeArchitecture.Status,
            snapshot.InstalledMemoryKilobytes.Status,
            snapshot.LogicalProcessorCount.Status,
        };

        if (snapshot.FirmwareTable.Status == SystemInventorySourceStatus.Available)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SmbiosInventoryParser.TryParse(
                    snapshot.FirmwareTable.GetRequiredValue(),
                    cancellationToken,
                    out var smbiosInventory))
            {
                sourceStatuses[0] = SystemInventorySourceStatus.InvalidData;
            }
            else
            {
                AddSmbiosEvidence(evidence, smbiosInventory!, cancellationToken);
            }
        }

        if (snapshot.NativeArchitecture.Status == SystemInventorySourceStatus.Available)
        {
            evidence.Add(NormalizeArchitecture(snapshot.NativeArchitecture.GetRequiredValue()));
        }

        if (snapshot.InstalledMemoryKilobytes.Status == SystemInventorySourceStatus.Available)
        {
            evidence.Add(NormalizeInstalledMemory(snapshot.InstalledMemoryKilobytes.GetRequiredValue()));
        }

        if (snapshot.LogicalProcessorCount.Status == SystemInventorySourceStatus.Available)
        {
            evidence.Add(NormalizeLogicalProcessorCount(snapshot.LogicalProcessorCount.GetRequiredValue()));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return FailureStatus(sourceStatuses) switch
        {
            null => ProviderReadResult.Collected(evidence),
            SystemInventorySourceStatus.AccessDenied => ProviderReadResult.AccessDenied(evidence),
            SystemInventorySourceStatus.InvalidData => ProviderReadResult.InvalidData(evidence),
            SystemInventorySourceStatus.Unavailable => ProviderReadResult.Unavailable(evidence),
            _ => throw new InvalidOperationException("The provider source failure is not defined."),
        };
    }

    private static void AddSmbiosEvidence(
        List<NormalizedEvidence> evidence,
        SmbiosInventory inventory,
        CancellationToken cancellationToken)
    {
        evidence.Add(NormalizeText(DeviceManufacturerKey, inventory.Manufacturer, Type1Source));
        evidence.Add(NormalizeText(DeviceModelKey, inventory.Model, Type1Source));
        evidence.Add(NormalizeText(DeviceSerialNumberKey, inventory.SerialNumber, Type1Source));
        evidence.Add(NormalizeDeviceType(inventory.ChassisTypes));

        if (inventory.Processors.Count == 0)
        {
            evidence.Add(NormalizedEvidence.NotAvailable(
                ProcessorManufacturerKey,
                ProviderLimitation.SourceValueMissing,
                Type4Source));
            evidence.Add(NormalizedEvidence.NotAvailable(
                ProcessorModelKey,
                ProviderLimitation.SourceValueMissing,
                Type4Source));
            return;
        }

        for (var index = 0; index < inventory.Processors.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Type4Source}.{index:D2}");
            evidence.Add(NormalizeText(
                ProcessorManufacturerKey,
                inventory.Processors[index].Manufacturer,
                source));
            evidence.Add(NormalizeText(
                ProcessorModelKey,
                inventory.Processors[index].Model,
                source));
        }
    }

    private static NormalizedEvidence NormalizeText(
        EvidenceKey key,
        SmbiosField<string> field,
        string source) =>
        field.Status switch
        {
            SmbiosFieldStatus.Available => NormalizedEvidence.Observed(
                key,
                EvidenceValue.FromString(field.GetRequiredValue()),
                sourceReference: source),
            SmbiosFieldStatus.Missing => NormalizedEvidence.NotAvailable(
                key,
                ProviderLimitation.SourceValueMissing,
                source),
            SmbiosFieldStatus.Malformed => NormalizedEvidence.NotAvailable(
                key,
                ProviderLimitation.SourceValueMalformed,
                source),
            SmbiosFieldStatus.Conflict => NormalizedEvidence.NotAvailable(
                key,
                ProviderLimitation.SourceValuesConflict,
                source),
            _ => throw new InvalidOperationException("The SMBIOS text status is not defined."),
        };

    private static NormalizedEvidence NormalizeDeviceType(
        IReadOnlyList<SmbiosField<byte>> chassisTypes)
    {
        if (chassisTypes.Count == 0)
        {
            return NormalizedEvidence.NotAvailable(
                DeviceTypeKey,
                ProviderLimitation.SourceValueMissing,
                Type3Source);
        }

        var mappedValues = new List<string>();
        var missingValueSeen = false;
        foreach (var chassisType in chassisTypes)
        {
            if (chassisType.Status == SmbiosFieldStatus.Malformed)
            {
                return NormalizedEvidence.NotAvailable(
                    DeviceTypeKey,
                    ProviderLimitation.SourceValueMalformed,
                    Type3Source);
            }

            if (chassisType.Status != SmbiosFieldStatus.Available)
            {
                missingValueSeen = true;
                continue;
            }

            var mapped = MapChassisType(chassisType.GetRequiredValue());
            if (mapped.Status == SmbiosFieldStatus.Malformed)
            {
                return NormalizedEvidence.NotAvailable(
                    DeviceTypeKey,
                    ProviderLimitation.SourceValueMalformed,
                    Type3Source);
            }

            if (mapped.Status == SmbiosFieldStatus.Missing)
            {
                missingValueSeen = true;
            }
            else
            {
                mappedValues.Add(mapped.GetRequiredValue());
            }
        }

        var distinctValues = mappedValues.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctValues.Length == 0)
        {
            return NormalizedEvidence.NotAvailable(
                DeviceTypeKey,
                ProviderLimitation.SourceValueMissing,
                Type3Source);
        }

        if (distinctValues.Length > 1)
        {
            return NormalizedEvidence.NotAvailable(
                DeviceTypeKey,
                ProviderLimitation.SourceValuesConflict,
                Type3Source);
        }

        return NormalizedEvidence.Inferred(
            DeviceTypeKey,
            EvidenceValue.FromEnum(distinctValues[0]),
            missingValueSeen ? ProviderLimitation.SourceValueMissing : null,
            Type3Source);
    }

    private static SmbiosField<string> MapChassisType(byte value) => value switch
    {
        1 => SmbiosField<string>.Available("other"),
        2 => SmbiosField<string>.Missing(),
        3 or 4 or 5 or 6 or 7 or 15 or 16 or 24 => SmbiosField<string>.Available("desktop"),
        8 or 9 or 10 or 14 => SmbiosField<string>.Available("laptop"),
        11 => SmbiosField<string>.Available("handheld"),
        12 => SmbiosField<string>.Available("docking_station"),
        13 => SmbiosField<string>.Available("all_in_one"),
        >= 17 and <= 23 or 28 or 29 => SmbiosField<string>.Available("server"),
        >= 25 and <= 27 => SmbiosField<string>.Available("other"),
        30 => SmbiosField<string>.Available("tablet"),
        31 => SmbiosField<string>.Available("convertible"),
        32 => SmbiosField<string>.Available("detachable"),
        33 or 34 => SmbiosField<string>.Available("embedded"),
        35 => SmbiosField<string>.Available("mini_pc"),
        36 => SmbiosField<string>.Available("stick_pc"),
        _ => SmbiosField<string>.Malformed(),
    };

    private static NormalizedEvidence NormalizeArchitecture(ushort nativeArchitecture)
    {
        var value = nativeArchitecture switch
        {
            0 => "x86",
            5 => "arm32",
            6 => "ia64",
            9 => "x64",
            12 => "arm64",
            _ => null,
        };
        return value is null
            ? NormalizedEvidence.NotAvailable(
                SystemArchitectureKey,
                ProviderLimitation.SourceValueMalformed,
                ArchitectureSource)
            : NormalizedEvidence.Observed(
                SystemArchitectureKey,
                EvidenceValue.FromEnum(value),
                sourceReference: ArchitectureSource);
    }

    private static NormalizedEvidence NormalizeInstalledMemory(ulong installedMemoryKilobytes)
    {
        if (installedMemoryKilobytes == 0 ||
            installedMemoryKilobytes > (ulong)(MaximumInstalledMemoryBytes / 1024))
        {
            return NormalizedEvidence.NotAvailable(
                InstalledMemoryBytesKey,
                ProviderLimitation.SourceValueMalformed,
                InstalledMemorySource);
        }

        var installedBytes = checked((long)installedMemoryKilobytes * 1024);
        return NormalizedEvidence.Observed(
            InstalledMemoryBytesKey,
            EvidenceValue.FromInteger(installedBytes),
            sourceReference: InstalledMemorySource);
    }

    private static NormalizedEvidence NormalizeLogicalProcessorCount(uint logicalProcessorCount) =>
        logicalProcessorCount == 0 || logicalProcessorCount > MaximumLogicalProcessorCount
            ? NormalizedEvidence.NotAvailable(
                ProcessorLogicalCountKey,
                ProviderLimitation.SourceValueMalformed,
                LogicalProcessorSource)
            : NormalizedEvidence.Observed(
                ProcessorLogicalCountKey,
                EvidenceValue.FromInteger(logicalProcessorCount),
                sourceReference: LogicalProcessorSource);

    private static SystemInventorySourceStatus? FailureStatus(
        IEnumerable<SystemInventorySourceStatus> sourceStatuses)
    {
        var statuses = sourceStatuses.ToArray();
        if (statuses.Contains(SystemInventorySourceStatus.AccessDenied))
        {
            return SystemInventorySourceStatus.AccessDenied;
        }

        if (statuses.Contains(SystemInventorySourceStatus.InvalidData))
        {
            return SystemInventorySourceStatus.InvalidData;
        }

        return statuses.Contains(SystemInventorySourceStatus.Unavailable)
            ? SystemInventorySourceStatus.Unavailable
            : null;
    }
}
