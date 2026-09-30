using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class StorageSourceTests
{
    [Fact]
    public void AssociationLinksDetermineCountersAndSortingDeterminesOnlyLocalOrdinals()
    {
        var first = Physical("A", 100);
        var second = Physical("B", 200);
        var lowWear = Counter("unrelated-2", 3);
        var highWear = Counter("unrelated-1", 80);
        var raw = Snapshot([second, first], [highWear, lowWear],
            [Link(second, highWear, true), Link(first, lowWear)]);
        var actual = Read(raw);
        Assert.Equal(StorageCollectionStatus.Collected, actual.DiskEnumeration.Status);
        Assert.Equal([0, 1], actual.Disks.Select(static row => row.Ordinal));
        Assert.Equal([100UL, 200UL], actual.Disks.Select(static row => row.CapacityBytes.GetRequiredValue()));
        Assert.Equal([3, 80], actual.Disks.Select(static row => (int)row.Reliability.Wear.GetRequiredValue()));
        Assert.Equal(actual, Read(raw with
        {
            PhysicalDisks = Collected([first, second]),
            ReliabilityCounters = Collected([lowWear, highWear]),
            Associations = Collected([Link(first, lowWear), Link(second, highWear, true)]),
        }), new SnapshotComparer());
    }

    [Fact]
    public void TransientRoutingDataIsAbsentFromTypedSnapshot()
    {
        var disk = Physical("fixture-private-routing-identifier", 100);
        var counter = Counter("fixture-private-counter-identifier", 5);
        var text = JsonSerializer.Serialize(Read(Snapshot([disk], [counter], [Link(disk, counter)])));
        Assert.DoesNotContain("fixture-private", text, StringComparison.Ordinal);
        Assert.DoesNotContain("__PATH", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectId", text, StringComparison.Ordinal);
        Assert.False(text.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingAssociationDoesNotInferReliabilityFromMatchingRowOrderOrNumericId()
    {
        var actual = Read(Snapshot([Physical("0", 100)], [Counter("0", 0)], []));
        var disk = Assert.Single(actual.Disks);
        Assert.Equal(StorageCollectionStatus.Unavailable, disk.Reliability.CollectionInfo.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, disk.Reliability.Wear.Status);
        Assert.Equal(100UL, disk.CapacityBytes.GetRequiredValue());
        Assert.Equal(500UL, actual.SystemVolume.CallerTotalBytes.GetRequiredValue());
    }

    [Theory]
    [InlineData("duplicate_link")]
    [InlineData("shared_counter")]
    [InlineData("orphan_disk")]
    [InlineData("orphan_counter")]
    [InlineData("foreign_reference")]
    [InlineData("duplicate_counter")]
    [InlineData("too_many_counters")]
    [InlineData("too_many_links")]
    public void AmbiguousOrUnboundedReliabilityNeverChoosesAWinningRow(string condition)
    {
        var disk = Physical("A", 100);
        var other = Physical("B", 200);
        var counter = Counter("counter", 0);
        IReadOnlyList<StorageWmiRow> counters = [counter];
        IReadOnlyList<StorageWmiRow> links = [Link(disk, counter)];
        switch (condition)
        {
            case "duplicate_link": links = [Link(disk, counter), Link(disk, counter, true)]; break;
            case "shared_counter": links = [Link(disk, counter), Link(other, counter)]; break;
            case "orphan_disk": links = [Link(Physical("absent", 10), counter)]; break;
            case "orphan_counter": links = [Link(disk, Counter("absent", 0))]; break;
            case "foreign_reference":
                links = [new(new Dictionary<string, object?>
                { ["PhysicalDisk"] = "\\\\foreign-host\\root\\Microsoft\\Windows\\Storage:MSFT_PhysicalDisk.ObjectId=\"A\"",
                  ["StorageReliabilityCounter"] = counter.Fields["__RELPATH"] })]; break;
            case "duplicate_counter": counters = [counter, counter]; break;
            case "too_many_counters": counters = Enumerable.Range(0, 9).Select(i => Counter(i.ToString(System.Globalization.CultureInfo.InvariantCulture), 0)).ToArray(); break;
            case "too_many_links": links = Enumerable.Repeat(Link(disk, counter), 9).ToArray(); break;
        }

        var actual = Read(Snapshot([disk, other], counters, links));
        Assert.Equal(StorageCollectionStatus.Collected, actual.DiskEnumeration.Status);
        Assert.Equal(2, actual.Disks.Count);
        Assert.All(actual.Disks, static value =>
        {
            Assert.Equal(StorageCollectionStatus.InvalidData, value.Reliability.CollectionInfo.Status);
            Assert.Equal(SystemInventorySourceStatus.InvalidData, value.Reliability.Wear.Status);
            Assert.Equal(SystemInventorySourceStatus.Available, value.CapacityBytes.Status);
        });
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("too_many")]
    [InlineData("missing_path")]
    [InlineData("foreign_host")]
    [InlineData("wrong_class")]
    [InlineData("path_mismatch")]
    [InlineData("long_path")]
    [InlineData("control_path")]
    public void InvalidPhysicalEnumerationDiscardsAllPhysicalRowsAndPreservesVolume(string condition)
    {
        var disk = Physical("A", 100);
        IReadOnlyList<StorageWmiRow> rows = [disk];
        var fields = new Dictionary<string, object?>(disk.Fields);
        switch (condition)
        {
            case "duplicate": rows = [disk, disk]; break;
            case "too_many": rows = Enumerable.Range(0, 5).Select(i => Physical(i.ToString(System.Globalization.CultureInfo.InvariantCulture), 10)).ToArray(); break;
            case "missing_path": fields.Remove("__PATH"); rows = [new(fields)]; break;
            case "foreign_host": fields["__PATH"] = "\\\\foreign-host\\root\\Microsoft\\Windows\\Storage:" + fields["__RELPATH"]; rows = [new(fields)]; break;
            case "wrong_class": rows = [Counter("A", 0)]; break;
            case "path_mismatch": fields["__PATH"] = fields["__PATH"] + "other"; rows = [new(fields)]; break;
            case "long_path": fields["__RELPATH"] = new string('x', 2049); rows = [new(fields)]; break;
            case "control_path": fields["__PATH"] = fields["__PATH"] + "\n"; rows = [new(fields)]; break;
        }

        var actual = Read(Snapshot(rows, [], []));
        Assert.Equal(StorageCollectionStatus.InvalidData, actual.DiskEnumeration.Status);
        Assert.Empty(actual.Disks);
        Assert.Equal(500UL, actual.SystemVolume.CallerTotalBytes.GetRequiredValue());
    }

    [Fact]
    public void MissingAndMalformedPropertiesRemainDistinctAndOperationalCodesAreCopied()
    {
        ushort[] status = [2, 65535, 2];
        var disk = Physical("A", 100);
        var fields = new Dictionary<string, object?>(disk.Fields)
        {
            ["Size"] = null,
            ["MediaType"] = "4",
            ["HealthStatus"] = StorageInvalidValue.Instance,
            ["OperationalStatus"] = status,
        };
        var actual = Assert.Single(Read(Snapshot([new(fields)], [], [])).Disks);
        status[0] = 0;
        Assert.Equal(SystemInventorySourceStatus.Unavailable, actual.CapacityBytes.Status);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, actual.MediaType.Status);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, actual.HealthStatus.Status);
        Assert.Equal([2, 65535, 2], actual.OperationalStatus.GetRequiredValue().Select(static value => (int)value));
        fields["OperationalStatus"] = new ushort[17];
        Assert.Equal(SystemInventorySourceStatus.InvalidData,
            Assert.Single(Read(Snapshot([new(fields)], [], [])).Disks).OperationalStatus.Status);
    }

    [Theory]
    [InlineData((int)StorageCollectionStatus.AccessDenied, 0x80041003U)]
    [InlineData((int)StorageCollectionStatus.Unavailable, 0x80041010U)]
    [InlineData((int)StorageCollectionStatus.Failed, 0x80041001U)]
    [InlineData((int)StorageCollectionStatus.TimedOut, null)]
    public void CollectionFailuresPreserveExactNumericReasonAndOtherScopes(int rawStatus, uint? code)
    {
        var status = (StorageCollectionStatus)rawStatus;
        var failed = new StorageWmiQueryResult(new(status, code), []);
        var raw = Snapshot([Physical("A", 100)], [], []) with { ReliabilityCounters = failed };
        var actual = Read(raw);
        Assert.Equal(new(status, code), Assert.Single(actual.Disks).Reliability.CollectionInfo);
        Assert.Equal(StorageCollectionStatus.Collected, actual.DiskEnumeration.Status);
        actual = Read(raw with { PhysicalDisks = failed });
        Assert.Equal(new(status, code), actual.DiskEnumeration);
        Assert.Empty(actual.Disks);
        Assert.Equal(500UL, actual.SystemVolume.CallerTotalBytes.GetRequiredValue());
        actual = Read(Snapshot([Physical("A", 100)], [], []) with { Associations = failed });
        Assert.Equal(new(status, code), Assert.Single(actual.Disks).Reliability.CollectionInfo);
    }

    [Fact]
    public void CleanupFailureWithholdsAllDiskValuesAndPreservesOriginalNativeCode()
    {
        var raw = Snapshot([Physical("A", 100)], [], []) with
        {
            PhysicalDisks = new(new(StorageCollectionStatus.AccessDenied, 0x80041003), []),
            CleanupComplete = false,
        };
        var actual = Read(raw);
        Assert.Equal(new(StorageCollectionStatus.CleanupIncomplete, 0x80041003), actual.DiskEnumeration);
        Assert.Empty(actual.Disks);
        Assert.Equal(StorageCollectionStatus.Collected, actual.SystemVolume.CollectionInfo.Status);
        actual = Read(raw with
        {
            PhysicalDisks = Collected([Physical("A", 100)]),
            ReliabilityCounters = new(new(StorageCollectionStatus.CleanupIncomplete, 0x8002000D), []),
        });
        Assert.Equal(0x8002000DU, actual.DiskEnumeration.NativeErrorCode);
    }

    [Fact]
    public void EmptyEnumerationIsCollectedWithZeroDisks()
    {
        var actual = Read(Snapshot([], [], []));
        Assert.Equal(StorageCollectionStatus.Collected, actual.DiskEnumeration.Status);
        Assert.Empty(actual.Disks);
    }

    [Fact]
    public void PreCancellationDoesNotReadAnySource()
    {
        var volume = new FixtureVolume();
        var wmi = new FixtureWmi(Snapshot([], [], []));
        var source = new WindowsStorageSource(wmi, volume);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.Read(cancellation.Token));
        Assert.Equal(0, volume.ReadCount);
        Assert.Equal(0, wmi.ReadCount);
    }

    [Theory]
    [InlineData(5U, (int)StorageCollectionStatus.AccessDenied)]
    [InlineData(0x80070005U, (int)StorageCollectionStatus.AccessDenied)]
    [InlineData(0x80041003U, (int)StorageCollectionStatus.AccessDenied)]
    [InlineData(50U, (int)StorageCollectionStatus.Unavailable)]
    [InlineData(0x8004100EU, (int)StorageCollectionStatus.Unavailable)]
    [InlineData(0x80041010U, (int)StorageCollectionStatus.Unavailable)]
    [InlineData(0x80040154U, (int)StorageCollectionStatus.Unavailable)]
    [InlineData(0xDEADBEEFU, (int)StorageCollectionStatus.Failed)]
    public void NativeFailureCodesAreExactAndNotConvertedToMessages(uint code, int rawStatus)
    {
        var status = (StorageCollectionStatus)rawStatus;
        Assert.Equal(new(status, code), StorageSourceFailures.FromNativeError(code));
    }

    [Theory]
    [InlineData("0", 0UL)]
    [InlineData("010", 10UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("0xffffffffffffffff", ulong.MaxValue)]
    [InlineData("0X10", 16UL)]
    public void UInt64WmiStringsUseDocumentedInvariantDecimalOrExplicitHex(string text, ulong expected)
    {
        Assert.True(StorageWmiValueReader.TryUInt64(text, out var actual));
        Assert.Equal(expected, actual);
        using var variant = new NativeVariant(text);
        Assert.Equal(expected, Assert.IsType<ulong>(StorageWmiValueReader.Read(variant.Pointer, 21, StorageWmiValueKind.UInt64)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("1,000")]
    [InlineData("1.0")]
    [InlineData("18446744073709551616")]
    [InlineData("0x10000000000000000")]
    [InlineData("0x")]
    public void InvalidUnsignedTextIsNotCoerced(string text)
    {
        Assert.False(StorageWmiValueReader.TryUInt64(text, out _));
    }

    [Fact]
    public void NativeScalarParserRequiresExactCimAndVariantKinds()
    {
        using var number = new NativeVariant(65535);
        Assert.Equal(ushort.MaxValue, StorageWmiValueReader.Read(number.Pointer, 18, StorageWmiValueKind.UInt16));
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(number.Pointer, 17, StorageWmiValueKind.UInt8));
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(number.Pointer, 21, StorageWmiValueKind.UInt64));
        using var tooLarge = new NativeVariant(65536);
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(tooLarge.Pointer, 18, StorageWmiValueKind.UInt16));
        using var negative = new NativeVariant(-1);
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(negative.Pointer, 18, StorageWmiValueKind.UInt16));
        using var octet = new NativeVariant((byte)255);
        Assert.Equal((byte)255, StorageWmiValueReader.Read(octet.Pointer, 17, StorageWmiValueKind.UInt8));
        using var missing = new NativeVariant(null);
        Assert.Null(StorageWmiValueReader.Read(missing.Pointer, 18, StorageWmiValueKind.UInt16));
    }

    [Fact]
    public void NativeStringsAreLengthBoundedBeforeManagedCopyAndReferencesRequireCimReference()
    {
        using var value = new NativeVariant(new string('x', WindowsStorageSource.MaximumRoutingCharacters + 1));
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(value.Pointer, 8, StorageWmiValueKind.RoutingString));
        using var reference = new NativeVariant("synthetic-reference");
        Assert.Equal("synthetic-reference", StorageWmiValueReader.Read(reference.Pointer, 102, StorageWmiValueKind.Reference));
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(reference.Pointer, 8, StorageWmiValueKind.Reference));
    }

    [Fact]
    public void NativeOperationalStatusArrayIsBoundedTypedAndReadOnly()
    {
        int[] values = [2, 65535, 2];
        using var variant = new NativeVariant(values);
        var actual = Assert.IsAssignableFrom<IReadOnlyList<ushort>>(
            StorageWmiValueReader.Read(variant.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
        Assert.Equal([2, 65535, 2], actual.Select(static value => (int)value));
        Assert.Throws<NotSupportedException>(() => ((IList<ushort>)actual)[0] = 1);
        using var oversized = new NativeVariant(new int[17]);
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(oversized.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
        int[] invalidValues = [-1];
        using var invalid = new NativeVariant(invalidValues);
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(invalid.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
        using var multiDimension = new NativeVariant(new int[1, 1]);
        Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(multiDimension.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
    }

    [Fact]
    public void NativeArrayTagCannotHideMismatchedElementTypeOrSize()
    {
        long[] differentSize = [1];
        using var variant = new NativeVariant(differentSize);
        var tag = Marshal.ReadInt16(variant.Pointer);
        Marshal.WriteInt16(variant.Pointer, 0x2003);
        try
        {
            Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(variant.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
        }
        finally
        {
            Marshal.WriteInt16(variant.Pointer, tag);
        }

        uint[] differentType = [1];
        using var sameSizeDifferentType = new NativeVariant(differentType);
        tag = Marshal.ReadInt16(sameSizeDifferentType.Pointer);
        Marshal.WriteInt16(sameSizeDifferentType.Pointer, 0x2003);
        try
        {
            Assert.Same(StorageInvalidValue.Instance, StorageWmiValueReader.Read(sameSizeDifferentType.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
        }
        finally
        {
            Marshal.WriteInt16(sameSizeDifferentType.Pointer, tag);
        }
    }

    [Fact]
    public void NativeArrayElementSizeIsCheckedEvenWhenItsActualTypeIsInt32()
    {
        int[] values = [1];
        using var variant = new NativeVariant(values);
        var array = Marshal.ReadIntPtr(variant.Pointer, 8);
        // SAFEARRAY.cbElements follows its two UInt16 fields on both x86 and x64.
        var originalSize = Marshal.ReadInt32(array, 4);
        Assert.Equal(sizeof(int), originalSize);
        Marshal.WriteInt32(array, 4, sizeof(long));
        try
        {
            Assert.Same(StorageInvalidValue.Instance,
                StorageWmiValueReader.Read(variant.Pointer, 0x2012, StorageWmiValueKind.UInt16Array));
        }
        finally
        {
            // Restore the descriptor before the owning VARIANT releases the native array.
            Marshal.WriteInt32(array, 4, originalSize);
        }
    }

    [Fact]
    public void EnumerationReadsOneRowAtATimeAndUsesUnreadSentinelRatherThanTruncation()
    {
        var rows = Enumerable.Range(0, 5).Select(_ => new NativeRow()).ToArray();
        var enumerator = new NativeEnumerator(rows.Select(static row => (Status: 0, Row: (IStorageWbemObject?)row, Count: 1U)).ToArray());
        var cleanup = true;
        var released = new List<object>();
        var actual = WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp(), ref cleanup, value => { if (value is not null) { released.Add(value); } return true; }, CancellationToken.None);
        Assert.Equal(StorageCollectionStatus.InvalidData, actual.CollectionInfo.Status);
        Assert.Empty(actual.Rows);
        Assert.Equal(5, enumerator.CallCount);
        Assert.Equal(5, released.Count);
        Assert.Equal(0, rows[4].GetCount);
        Assert.True(cleanup);
    }

    [Fact]
    public void EnumerationProcessesPartialFinalAndTimedOutRowsWithoutLosingThem()
    {
        var first = new NativeRow();
        var second = new NativeRow();
        var enumerator = new NativeEnumerator([(0x40004, first, 1), (1, second, 1)]);
        var cleanup = true;
        var actual = WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp(), ref cleanup, static _ => true, CancellationToken.None);
        Assert.Equal(StorageCollectionStatus.Collected, actual.CollectionInfo.Status);
        Assert.Equal(2, actual.Rows.Count);
        Assert.Equal(2, enumerator.CallCount);
    }

    [Fact]
    public void RepeatedImmediateWmiTimeoutsHaveAFiniteCallBound()
    {
        var enumerator = new NativeEnumerator([], repeatTimeout: true);
        var cleanup = true;
        var actual = WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp(), ref cleanup, static _ => true, CancellationToken.None);
        Assert.Equal(StorageCollectionStatus.TimedOut, actual.CollectionInfo.Status);
        Assert.Equal(WindowsStorageWmiSource.MaximumEnumerationCalls, enumerator.CallCount);
    }

    [Fact]
    public void EnumerationDeadlineAndCancellationPreventAnotherNativeCall()
    {
        var enumerator = new NativeEnumerator([]);
        var cleanup = true;
        var actual = WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 10), ref cleanup, static _ => true, CancellationToken.None);
        Assert.Equal(StorageCollectionStatus.TimedOut, actual.CollectionInfo.Status);
        Assert.Equal(0, enumerator.CallCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp(), ref cleanup, static _ => true, cancellation.Token));
        Assert.Equal(0, enumerator.CallCount);
    }

    [Theory]
    [InlineData(0, 0U)]
    [InlineData(2, 0U)]
    [InlineData(0, 2U)]
    public void InconsistentNativeEnumerationResultsFailClosed(int status, uint count)
    {
        var enumerator = new NativeEnumerator([(status, null, count)]);
        var cleanup = true;
        var actual = WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp(), ref cleanup, static _ => true, CancellationToken.None);
        Assert.Equal(StorageCollectionStatus.InvalidData, actual.CollectionInfo.Status);
        Assert.Empty(actual.Rows);
    }

    [Fact]
    public void RowReleaseFailureIsRetainedInCleanupState()
    {
        var enumerator = new NativeEnumerator([(1, new NativeRow(), 1)]);
        var cleanup = true;
        _ = WindowsStorageWmiSource.ReadRows(enumerator, StorageWmiQuery.PhysicalDisks,
            Stopwatch.GetTimestamp(), ref cleanup, static _ => false, CancellationToken.None);
        Assert.False(cleanup);
    }

    [Theory]
    [InlineData("C:\\", true)]
    [InlineData("D:\\Windows", true)]
    [InlineData("\\\\server\\share", false)]
    [InlineData("C:Windows", false)]
    [InlineData("Windows", false)]
    [InlineData("\\\\?\\C:\\Windows", false)]
    [InlineData("C:\\Windows\n", false)]
    public void SystemVolumeInputMustBeABoundedLocalDrivePath(string path, bool expected)
    {
        Assert.Equal(expected, WindowsStorageVolumeSource.IsLocalAbsolutePath(path));
        Assert.False(WindowsStorageVolumeSource.IsLocalAbsolutePath("C:\\" + new string('x', 32768)));
    }

    private static StorageSourceSnapshot Read(StorageWmiSnapshot snapshot) =>
        new WindowsStorageSource(new FixtureWmi(snapshot), new FixtureVolume()).Read(CancellationToken.None);

    private static StorageWmiSnapshot Snapshot(IReadOnlyList<StorageWmiRow> disks,
        IReadOnlyList<StorageWmiRow> counters, IReadOnlyList<StorageWmiRow> associations) =>
        new(Collected(disks), Collected(counters), Collected(associations));

    private static StorageWmiQueryResult Collected(IReadOnlyList<StorageWmiRow> rows) => new(StorageSourceFailures.Collected, rows);

    private static StorageWmiRow Physical(string key, ulong capacity)
    {
        var fields = Routing("MSFT_PhysicalDisk", key);
        fields.Add("Size", capacity);
        fields.Add("MediaType", (ushort)4);
        fields.Add("BusType", (ushort)17);
        fields.Add("HealthStatus", (ushort)0);
        fields.Add("OperationalStatus", Array.AsReadOnly<ushort>([2]));
        return new(fields);
    }

    private static StorageWmiRow Counter(string key, byte wear)
    {
        var fields = Routing("MSFT_StorageReliabilityCounter", key);
        fields.Add("Wear", wear);
        fields.Add("Temperature", (byte)35);
        fields.Add("ReadErrorsTotal", 0UL);
        fields.Add("ReadErrorsUncorrected", 0UL);
        fields.Add("WriteErrorsTotal", 1UL);
        fields.Add("WriteErrorsUncorrected", 0UL);
        fields.Add("PowerOnHours", (ushort)123);
        return new(fields);
    }

    private static Dictionary<string, object?> Routing(string type, string key)
    {
        var relative = string.Concat(type, ".ObjectId=\"", key, "\"");
        return new(StringComparer.Ordinal)
        {
            ["__RELPATH"] = relative,
            ["__PATH"] = string.Concat("\\\\", Environment.MachineName, "\\root\\Microsoft\\Windows\\Storage:", relative),
        };
    }

    private static StorageWmiRow Link(StorageWmiRow disk, StorageWmiRow counter, bool fullPath = false) => new(
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PhysicalDisk"] = disk.Fields[fullPath ? "__PATH" : "__RELPATH"],
            ["StorageReliabilityCounter"] = counter.Fields[fullPath ? "__PATH" : "__RELPATH"],
        });

    private sealed class FixtureWmi(StorageWmiSnapshot snapshot) : IStorageWmiSource
    {
        internal int ReadCount { get; private set; }
        public StorageWmiSnapshot Read(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return snapshot;
        }
    }

    private sealed class FixtureVolume : IStorageVolumeSource
    {
        internal int ReadCount { get; private set; }
        public StorageVolumeSnapshot Read(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return new(StorageSourceFailures.Collected, SystemInventorySourceValue<ulong>.Available(500),
                SystemInventorySourceValue<ulong>.Available(100), SystemInventorySourceValue<ulong>.Available(110));
        }
    }

    private sealed class SnapshotComparer : IEqualityComparer<StorageSourceSnapshot>
    {
        public bool Equals(StorageSourceSnapshot? x, StorageSourceSnapshot? y) => JsonSerializer.Serialize(x) == JsonSerializer.Serialize(y);
        public int GetHashCode(StorageSourceSnapshot obj) => throw new NotSupportedException();
    }

    private sealed class NativeVariant : IDisposable
    {
        internal NativeVariant(object? value)
        {
            Pointer = Marshal.AllocHGlobal(StorageWmiValueReader.VariantBytes);
            for (var offset = 0; offset < StorageWmiValueReader.VariantBytes; offset += 8)
            {
                Marshal.WriteInt64(Pointer, offset, 0);
            }

            Marshal.GetNativeVariantForObject(value, Pointer);
        }

        internal nint Pointer { get; }

        public void Dispose()
        {
            _ = StorageWmiNativeMethods.VariantClear(Pointer);
            Marshal.FreeHGlobal(Pointer);
        }
    }

    private sealed class NativeEnumerator(
        IReadOnlyList<(int Status, IStorageWbemObject? Row, uint Count)> results,
        bool repeatTimeout = false) : IStorageWbemEnumerator
    {
        internal int CallCount { get; private set; }
        public void Reserved01() => throw new NotSupportedException();

        public int Next(int timeoutMilliseconds, uint count, out IStorageWbemObject? value, out uint returned)
        {
            Assert.Equal(100, timeoutMilliseconds);
            Assert.Equal(1U, count);
            var result = repeatTimeout ? (Status: 0x40004, Row: (IStorageWbemObject?)null, Count: 0U) : results[CallCount];
            CallCount++;
            value = result.Row;
            returned = result.Count;
            return result.Status;
        }
    }

    private sealed class NativeRow : IStorageWbemObject
    {
        internal int GetCount { get; private set; }
        public void Reserved01() => throw new NotSupportedException();

        public int Get(string name, int flags, nint variant, out int cimType, nint flavor)
        {
            GetCount++;
            cimType = 0;
            return unchecked((int)0x80041002);
        }
    }
}
