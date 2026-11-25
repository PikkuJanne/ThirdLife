using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

public sealed class StorageProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-storage";
    internal const int MaximumPhysicalDisks = 4;
    internal const int MaximumOperationalStatusCodes = 16;

    private const string PhysicalSource = "storage.msft.physical_disk";
    private const string VolumeSource = "storage.kernel32.system_volume";
    private static readonly (string Field, EvidenceValueKind Kind, string? Unit)[] DiskFields =
    [
        ("capacity_bytes", EvidenceValueKind.WholeNumber, "bytes"),
        ("media_type", EvidenceValueKind.Enum, null),
        ("bus_type", EvidenceValueKind.Enum, null),
        ("reported_health", EvidenceValueKind.Enum, null),
        ("operational_status_codes", EvidenceValueKind.Text, null),
        ("wear", EvidenceValueKind.WholeNumber, "percent_consumed"),
        ("temperature", EvidenceValueKind.WholeNumber, "degrees_celsius"),
        ("read_errors_total", EvidenceValueKind.WholeNumber, "count"),
        ("read_errors_uncorrected", EvidenceValueKind.WholeNumber, "count"),
        ("write_errors_total", EvidenceValueKind.WholeNumber, "count"),
        ("write_errors_uncorrected", EvidenceValueKind.WholeNumber, "count"),
        ("power_on_hours", EvidenceValueKind.WholeNumber, "hours"),
        ("reliability_collection_status", EvidenceValueKind.Enum, null),
        ("reliability_native_error_code", EvidenceValueKind.WholeNumber, null),
    ];
    private readonly IStorageSource _source;
    private int _collectionInProgress;

    public StorageProvider()
        : this(new WindowsStorageSource(), ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal StorageProvider(IStorageSource source, ProviderEvidenceOrigin evidenceOrigin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Descriptor = CreateDescriptor(evidenceOrigin);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _collectionInProgress, 1, 0) != 0)
        {
            return ProviderReadResult.Unavailable();
        }

        try
        {
            var snapshot = await Task.Run(() => _source.Read(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(snapshot);
        }
        finally
        {
            // A runner timeout only stops waiting. Keep the guard until native work
            // and its cleanup finish, so retries cannot accumulate source workers.
            Volatile.Write(ref _collectionInProgress, 0);
        }
    }

    private static InventoryProviderDescriptor CreateDescriptor(ProviderEvidenceOrigin origin)
    {
        var definitions = DiskFields.Select(field => new ProviderEvidenceDefinition(
            DiskKey(field.Field), field.Kind, field.Unit, PhysicalSource, MaximumPhysicalDisks)).ToList();
        definitions.Add(new(new EvidenceKey("storage.physical_disk_count"), EvidenceValueKind.WholeNumber, "count", PhysicalSource));
        definitions.Add(new(new EvidenceKey("storage.physical_disk.collection_status"), EvidenceValueKind.Enum, null, PhysicalSource));
        definitions.Add(new(new EvidenceKey("storage.physical_disk.native_error_code"), EvidenceValueKind.WholeNumber, null, PhysicalSource));
        definitions.Add(new(new EvidenceKey("storage.system_volume.native_error_code"), EvidenceValueKind.WholeNumber, null, VolumeSource));
        foreach (var field in new[] { "caller_capacity_bytes", "caller_free_bytes", "total_free_bytes" })
        {
            definitions.Add(new(VolumeKey(field), EvidenceValueKind.WholeNumber, "bytes", VolumeSource));
        }

        return new InventoryProviderDescriptor(
            new ProviderId(ProviderIdentity),
            ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(5),
            ProviderNetworkUse.None,
            origin,
            [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("storage.provider_status"), "storage.provider.status"),
            definitions);
    }

    private static ProviderReadResult Normalize(StorageSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var evidence = new List<NormalizedEvidence>();
        var statuses = new List<StorageCollectionStatus>();
        var diskInfo = ValidInfo(snapshot.DiskEnumeration);
        var disks = snapshot.Disks?.Take(MaximumPhysicalDisks + 1).ToArray() ?? [];
        var ordinalsValid = snapshot.Disks is not null && disks.Length <= MaximumPhysicalDisks &&
            disks.All(static disk => disk is not null) &&
            disks.Select(static disk => disk.Ordinal).Order().SequenceEqual(Enumerable.Range(0, disks.Length));
        if (!ordinalsValid && diskInfo.Status == StorageCollectionStatus.Collected)
        {
            diskInfo = new(StorageCollectionStatus.InvalidData, diskInfo.NativeErrorCode);
        }

        statuses.Add(diskInfo.Status);
        var diskDataAvailable = diskInfo.Status == StorageCollectionStatus.Collected && ordinalsValid;
        evidence.Add(diskDataAvailable
            ? Observed("storage.physical_disk_count", disks.Length, PhysicalSource)
            : Missing(new EvidenceKey("storage.physical_disk_count"), Limitation(diskInfo.Status), PhysicalSource));
        if (diskDataAvailable && disks.Length > 0)
        {
            foreach (var disk in disks.OrderBy(static disk => disk.Ordinal))
            {
                AddDisk(evidence, statuses, disk);
            }
        }
        else
        {
            foreach (var field in DiskFields)
            {
                evidence.Add(Missing(DiskKey(field.Field), Limitation(diskInfo.Status), PhysicalSource));
            }
        }

        var volumeInfo = AddVolume(evidence, statuses, snapshot.SystemVolume);
        statuses.Add(volumeInfo.Status);
        evidence.Add(NormalizedEvidence.Observed(
            DiskKey("collection_status"), EvidenceValue.FromEnum(StatusCode(diskInfo.Status)), sourceReference: PhysicalSource));
        AddNativeError(evidence, DiskKey("native_error_code"), diskInfo, PhysicalSource);
        AddNativeError(evidence, VolumeKey("native_error_code"), volumeInfo, VolumeSource);
        if (evidence.Any(static item => item.Limitation is ProviderLimitation.SourceValueMalformed or ProviderLimitation.SourceValuesConflict))
        {
            statuses.Add(StorageCollectionStatus.InvalidData);
        }

        var status = AggregateStatus(statuses);

        return status switch
        {
            StorageCollectionStatus.Collected => ProviderReadResult.Collected(evidence),
            StorageCollectionStatus.AccessDenied => ProviderReadResult.AccessDenied(evidence),
            StorageCollectionStatus.InvalidData => ProviderReadResult.InvalidData(evidence),
            StorageCollectionStatus.CleanupIncomplete => ProviderReadResult.CleanupIncomplete(evidence),
            StorageCollectionStatus.Failed => ProviderReadResult.Failed(evidence),
            _ => ProviderReadResult.Unavailable(evidence),
        };
    }

    private static void AddDisk(List<NormalizedEvidence> evidence, List<StorageCollectionStatus> statuses, StorageDiskSnapshot disk)
    {
        var source = string.Create(CultureInfo.InvariantCulture, $"{PhysicalSource}.{disk.Ordinal:D2}");
        AddFieldStatus(statuses, disk.CapacityBytes?.Status, disk.MediaType?.Status, disk.BusType?.Status,
            disk.HealthStatus?.Status, disk.OperationalStatus?.Status);
        AddNumber(evidence, DiskKey("capacity_bytes"), disk.CapacityBytes, source);
        AddEnum(evidence, "media_type", disk.MediaType, source, static value => value switch
        {
            3 => "hdd",
            4 => "ssd",
            5 => "scm",
            _ => null,
        });
        AddEnum(evidence, "bus_type", disk.BusType, source, static value => value switch
        {
            1 => "scsi",
            2 => "atapi",
            3 => "ata",
            4 => "ieee1394",
            5 => "ssa",
            6 => "fibre_channel",
            7 => "usb",
            8 => "raid",
            9 => "iscsi",
            10 => "sas",
            11 => "sata",
            12 => "sd",
            13 => "mmc",
            15 => "file_backed_virtual",
            16 => "storage_spaces",
            17 => "nvme",
            _ => null,
        });
        AddEnum(evidence, "reported_health", disk.HealthStatus, source, static value => value switch
        {
            0 => "healthy",
            1 => "warning",
            2 => "unhealthy",
            _ => null,
        });
        AddOperationalStatus(evidence, disk.OperationalStatus, source);

        var reliability = disk.Reliability;
        var info = ValidInfo(reliability?.CollectionInfo);
        statuses.Add(info.Status);
        evidence.Add(NormalizedEvidence.Observed(
            DiskKey("reliability_collection_status"), EvidenceValue.FromEnum(StatusCode(info.Status)), sourceReference: source));
        AddNativeError(evidence, DiskKey("reliability_native_error_code"), info, source);
        if (info.Status != StorageCollectionStatus.Collected || reliability is null)
        {
            foreach (var field in DiskFields.Skip(5).Take(7))
            {
                evidence.Add(Missing(DiskKey(field.Field), Limitation(info.Status), source));
            }

            return;
        }

        AddFieldStatus(statuses, reliability.Wear?.Status, reliability.Temperature?.Status, reliability.ReadErrorsTotal?.Status,
            reliability.ReadErrorsUncorrected?.Status, reliability.WriteErrorsTotal?.Status, reliability.WriteErrorsUncorrected?.Status,
            reliability.PowerOnHours?.Status);
        AddNumber(evidence, DiskKey("wear"), reliability.Wear, source);
        AddNumber(evidence, DiskKey("temperature"), reliability.Temperature, source);
        AddNumber(evidence, DiskKey("read_errors_total"), reliability.ReadErrorsTotal, source);
        AddNumber(evidence, DiskKey("read_errors_uncorrected"), reliability.ReadErrorsUncorrected, source);
        AddNumber(evidence, DiskKey("write_errors_total"), reliability.WriteErrorsTotal, source);
        AddNumber(evidence, DiskKey("write_errors_uncorrected"), reliability.WriteErrorsUncorrected, source);
        AddNumber(evidence, DiskKey("power_on_hours"), reliability.PowerOnHours, source);
    }

    private static StorageCollectionInfo AddVolume(List<NormalizedEvidence> evidence, List<StorageCollectionStatus> statuses, StorageVolumeSnapshot? volume)
    {
        var info = ValidInfo(volume?.CollectionInfo);
        var fields = new[] { "caller_capacity_bytes", "caller_free_bytes", "total_free_bytes" };
        if (info.Status != StorageCollectionStatus.Collected || volume is null)
        {
            foreach (var field in fields)
            {
                evidence.Add(Missing(VolumeKey(field), Limitation(info.Status), VolumeSource));
            }

            return info;
        }

        var values = new[] { volume.CallerTotalBytes, volume.CallerAvailableBytes, volume.TotalFreeBytes };
        AddFieldStatus(statuses, values.Select(static value => value?.Status).ToArray());
        if (values[1]?.Status == SystemInventorySourceStatus.Available &&
            ((values[0]?.Status == SystemInventorySourceStatus.Available && values[1].GetRequiredValue() > values[0].GetRequiredValue()) ||
            (values[2]?.Status == SystemInventorySourceStatus.Available && values[1].GetRequiredValue() > values[2].GetRequiredValue())))
        {
            foreach (var field in fields)
            {
                evidence.Add(Missing(VolumeKey(field), ProviderLimitation.SourceValuesConflict, VolumeSource));
            }

            return new(StorageCollectionStatus.InvalidData, info.NativeErrorCode);
        }

        for (var index = 0; index < fields.Length; index++)
        {
            AddNumber(evidence, VolumeKey(fields[index]), values[index], VolumeSource);
        }

        return info;
    }

    private static void AddNumber<T>(List<NormalizedEvidence> evidence, EvidenceKey key, SystemInventorySourceValue<T>? value, string source)
        where T : struct, System.Numerics.IBinaryInteger<T>, System.Numerics.IUnsignedNumber<T>
    {
        if (value?.Status != SystemInventorySourceStatus.Available)
        {
            evidence.Add(Missing(key, FieldLimitation(value?.Status), source));
            return;
        }

        var number = ulong.CreateChecked(value.GetRequiredValue());
        evidence.Add(number <= long.MaxValue
            ? Observed(key, (long)number, source)
            : Missing(key, ProviderLimitation.SourceValueMalformed, source));
    }

    private static void AddEnum(List<NormalizedEvidence> evidence, string field, SystemInventorySourceValue<ushort>? value,
        string source, Func<ushort, string?> map)
    {
        var mapped = value?.Status == SystemInventorySourceStatus.Available ? map(value.GetRequiredValue()) : null;
        evidence.Add(mapped is null
            ? Missing(DiskKey(field), FieldLimitation(value?.Status), source)
            : NormalizedEvidence.Observed(DiskKey(field), EvidenceValue.FromEnum(mapped), sourceReference: source));
    }

    private static void AddOperationalStatus(List<NormalizedEvidence> evidence,
        SystemInventorySourceValue<IReadOnlyList<ushort>>? value, string source)
    {
        var key = DiskKey("operational_status_codes");
        if (value?.Status != SystemInventorySourceStatus.Available)
        {
            evidence.Add(Missing(key, FieldLimitation(value?.Status), source));
            return;
        }

        var codes = value.GetRequiredValue().Take(MaximumOperationalStatusCodes + 1).ToArray();
        if (codes.Length is 0 or > MaximumOperationalStatusCodes)
        {
            evidence.Add(Missing(key, ProviderLimitation.SourceValueMalformed, source));
            return;
        }

        // Preserve the provider's array order and every numeric code; no diagnosis
        // or healthy verdict is inferred from this uninterpreted status list.
        evidence.Add(NormalizedEvidence.Observed(key, EvidenceValue.FromString(
            string.Join(',', codes.Select(static code => code.ToString(CultureInfo.InvariantCulture)))), sourceReference: source));
    }

    private static StorageCollectionInfo ValidInfo(StorageCollectionInfo? info) =>
        info is not null && Enum.IsDefined(info.Status) ? info : new(StorageCollectionStatus.InvalidData, info?.NativeErrorCode);

    private static void AddNativeError(List<NormalizedEvidence> evidence, EvidenceKey key, StorageCollectionInfo info, string source) =>
        evidence.Add(info.NativeErrorCode is { } error
            ? Observed(key, error, source)
            : Missing(key, ProviderLimitation.SourceValueMissing, source));

    private static void AddFieldStatus(List<StorageCollectionStatus> statuses, params SystemInventorySourceStatus?[] values)
    {
        if (values.Contains(SystemInventorySourceStatus.AccessDenied))
        {
            statuses.Add(StorageCollectionStatus.AccessDenied);
        }
    }

    private static StorageCollectionStatus AggregateStatus(IEnumerable<StorageCollectionStatus> statuses) =>
        statuses.OrderByDescending(static status => status switch
        {
            StorageCollectionStatus.CleanupIncomplete => 6,
            StorageCollectionStatus.TimedOut => 5,
            StorageCollectionStatus.AccessDenied => 4,
            StorageCollectionStatus.InvalidData => 3,
            StorageCollectionStatus.Failed => 2,
            StorageCollectionStatus.Unavailable => 1,
            _ => 0,
        }).First();

    private static string StatusCode(StorageCollectionStatus status) => status switch
    {
        StorageCollectionStatus.Collected => "collected",
        StorageCollectionStatus.Unavailable => "unavailable",
        StorageCollectionStatus.AccessDenied => "access_denied",
        StorageCollectionStatus.InvalidData => "invalid_data",
        StorageCollectionStatus.Failed => "failed",
        StorageCollectionStatus.TimedOut => "timed_out",
        StorageCollectionStatus.CleanupIncomplete => "cleanup_incomplete",
        _ => "invalid_data",
    };

    private static ProviderLimitation Limitation(StorageCollectionStatus status) =>
        status == StorageCollectionStatus.InvalidData ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing;

    private static ProviderLimitation FieldLimitation(SystemInventorySourceStatus? status) =>
        status is null or SystemInventorySourceStatus.InvalidData ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing;

    private static NormalizedEvidence Missing(EvidenceKey key, ProviderLimitation limitation, string source) =>
        NormalizedEvidence.NotAvailable(key, limitation, source);

    private static NormalizedEvidence Observed(string key, long value, string source) => Observed(new EvidenceKey(key), value, source);

    private static NormalizedEvidence Observed(EvidenceKey key, long value, string source) =>
        NormalizedEvidence.Observed(key, EvidenceValue.FromInteger(value), sourceReference: source);

    private static EvidenceKey DiskKey(string field) => new($"storage.physical_disk.{field}");

    private static EvidenceKey VolumeKey(string field) => new($"storage.system_volume.{field}");
}
