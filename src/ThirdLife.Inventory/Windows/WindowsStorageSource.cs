namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsStorageSource : IStorageSource
{
    internal const int MaximumDisks = 4;
    internal const int MaximumRelatedRows = 8;
    internal const int MaximumOperationalStatusCount = 16;
    internal const int MaximumRoutingCharacters = 2048;

    private readonly IStorageWmiSource _wmi;
    private readonly IStorageVolumeSource _volume;

    public WindowsStorageSource()
        : this(new WindowsStorageWmiSource(), new WindowsStorageVolumeSource())
    {
    }

    internal WindowsStorageSource(IStorageWmiSource wmi, IStorageVolumeSource volume)
    {
        _wmi = wmi ?? throw new ArgumentNullException(nameof(wmi));
        _volume = volume ?? throw new ArgumentNullException(nameof(volume));
    }

    public StorageSourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var volume = _volume.Read(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var raw = _wmi.Read(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!raw.CleanupComplete)
        {
            var cleanupCode = raw.PhysicalDisks.CollectionInfo.Status == StorageCollectionStatus.CleanupIncomplete
                ? raw.PhysicalDisks.CollectionInfo.NativeErrorCode
                : raw.ReliabilityCounters.CollectionInfo.Status == StorageCollectionStatus.CleanupIncomplete
                    ? raw.ReliabilityCounters.CollectionInfo.NativeErrorCode
                    : raw.Associations.CollectionInfo.Status == StorageCollectionStatus.CleanupIncomplete
                        ? raw.Associations.CollectionInfo.NativeErrorCode
                        : raw.PhysicalDisks.CollectionInfo.NativeErrorCode ??
                            raw.ReliabilityCounters.CollectionInfo.NativeErrorCode ??
                            raw.Associations.CollectionInfo.NativeErrorCode;
            return new(new(StorageCollectionStatus.CleanupIncomplete, cleanupCode), [], volume);
        }

        if (raw.PhysicalDisks.CollectionInfo.Status != StorageCollectionStatus.Collected)
        {
            return new(raw.PhysicalDisks.CollectionInfo, [], volume);
        }

        if (!TryIndexRows(raw.PhysicalDisks.Rows, MaximumDisks, "MSFT_PhysicalDisk", out var disks, out var diskAliases))
        {
            return new(StorageSourceFailures.InvalidData, [], volume);
        }

        var reliability = ResolveReliability(raw, disks, diskAliases);
        var snapshots = new List<StorageDiskSnapshot>(disks.Count);
        for (var index = 0; index < disks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var disk = disks[index];
            snapshots.Add(new(
                index,
                Field<ulong>(disk.Row, "Size"),
                Field<ushort>(disk.Row, "MediaType"),
                Field<ushort>(disk.Row, "BusType"),
                Field<ushort>(disk.Row, "HealthStatus"),
                OperationalStatus(disk.Row),
                reliability[disk.RelativePath]));
        }

        return new(StorageSourceFailures.Collected, snapshots.AsReadOnly(), volume);
    }

    private static Dictionary<string, StorageReliabilitySnapshot> ResolveReliability(
        StorageWmiSnapshot raw,
        IReadOnlyList<IndexedRow> disks,
        Dictionary<string, IndexedRow> diskAliases)
    {
        var failure = raw.ReliabilityCounters.CollectionInfo.Status != StorageCollectionStatus.Collected
            ? raw.ReliabilityCounters.CollectionInfo
            : raw.Associations.CollectionInfo.Status != StorageCollectionStatus.Collected
                ? raw.Associations.CollectionInfo
                : null;
        if (failure is not null)
        {
            return UnavailableReliability(disks, failure);
        }

        if (!TryIndexRows(raw.ReliabilityCounters.Rows, MaximumRelatedRows, "MSFT_StorageReliabilityCounter", out _, out var counters) ||
            raw.Associations.Rows.Count > MaximumRelatedRows)
        {
            return UnavailableReliability(disks, StorageSourceFailures.InvalidData);
        }

        var links = new Dictionary<string, IndexedRow>(StringComparer.Ordinal);
        var usedCounters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var association in raw.Associations.Rows)
        {
            if (!TryRoutingValue(association, "PhysicalDisk", out var diskReference) ||
                !TryRoutingValue(association, "StorageReliabilityCounter", out var counterReference) ||
                !diskAliases.TryGetValue(diskReference!, out var disk) ||
                !counters.TryGetValue(counterReference!, out var counter) ||
                !links.TryAdd(disk.RelativePath, counter) ||
                !usedCounters.Add(counter.RelativePath))
            {
                // Ambiguous, duplicate, orphaned, or mismatched links never select a row.
                return UnavailableReliability(disks, StorageSourceFailures.InvalidData);
            }
        }

        var result = new Dictionary<string, StorageReliabilitySnapshot>(StringComparer.Ordinal);
        foreach (var disk in disks)
        {
            result.Add(disk.RelativePath, links.TryGetValue(disk.RelativePath, out var counter)
                ? new(
                    StorageSourceFailures.Collected,
                    Field<byte>(counter.Row, "Wear"),
                    Field<byte>(counter.Row, "Temperature"),
                    Field<ulong>(counter.Row, "ReadErrorsTotal"),
                    Field<ulong>(counter.Row, "ReadErrorsUncorrected"),
                    Field<ulong>(counter.Row, "WriteErrorsTotal"),
                    Field<ulong>(counter.Row, "WriteErrorsUncorrected"),
                    Field<ushort>(counter.Row, "PowerOnHours"))
                : StorageSourceFailures.Reliability(StorageSourceFailures.Unavailable));
        }

        return result;
    }

    private static Dictionary<string, StorageReliabilitySnapshot> UnavailableReliability(
        IReadOnlyList<IndexedRow> disks,
        StorageCollectionInfo failure) => disks.ToDictionary(
            static disk => disk.RelativePath,
            _ => StorageSourceFailures.Reliability(failure),
            StringComparer.Ordinal);

    private static bool TryIndexRows(
        IReadOnlyList<StorageWmiRow> rows,
        int maximumCount,
        string expectedClass,
        out List<IndexedRow> indexed,
        out Dictionary<string, IndexedRow> aliases)
    {
        indexed = [];
        aliases = new(StringComparer.Ordinal);
        if (rows.Count > maximumCount)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (!TryRoutingValue(row, "__PATH", out var path) ||
                !TryRoutingValue(row, "__RELPATH", out var relativePath) ||
                !IsLocalRowPath(path!, relativePath!, expectedClass))
            {
                return false;
            }

            var item = new IndexedRow(relativePath!, row);
            if (!aliases.TryAdd(path!, item) || !aliases.TryAdd(relativePath!, item))
            {
                return false;
            }

            indexed.Add(item);
        }

        indexed.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return true;
    }

    internal static bool IsLocalRowPath(string path, string relativePath, string expectedClass)
    {
        var prefix = string.Concat("\\\\", Environment.MachineName, "\\root\\Microsoft\\Windows\\Storage:");
        return relativePath.StartsWith(string.Concat(expectedClass, "."), StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            path.AsSpan(prefix.Length).SequenceEqual(relativePath);
    }

    private static bool TryRoutingValue(StorageWmiRow row, string property, out string? value)
    {
        value = row.Fields.TryGetValue(property, out var candidate) ? candidate as string : null;
        return value is { Length: > 0 and <= MaximumRoutingCharacters } &&
            !value.Any(char.IsControl);
    }

    private static SystemInventorySourceValue<T> Field<T>(StorageWmiRow row, string property)
        where T : notnull
    {
        if (!row.Fields.TryGetValue(property, out var value) || value is null)
        {
            return SystemInventorySourceValue<T>.Unavailable();
        }

        return value is T typed
            ? SystemInventorySourceValue<T>.Available(typed)
            : SystemInventorySourceValue<T>.InvalidData();
    }

    private static SystemInventorySourceValue<IReadOnlyList<ushort>> OperationalStatus(StorageWmiRow row)
    {
        var values = Field<IReadOnlyList<ushort>>(row, "OperationalStatus");
        if (values.Status != SystemInventorySourceStatus.Available)
        {
            return values;
        }

        var statuses = values.GetRequiredValue();
        return statuses.Count > MaximumOperationalStatusCount
            ? SystemInventorySourceValue<IReadOnlyList<ushort>>.InvalidData()
            : SystemInventorySourceValue<IReadOnlyList<ushort>>.Available(Array.AsReadOnly(statuses.ToArray()));
    }

    private sealed record IndexedRow(string RelativePath, StorageWmiRow Row);
}
