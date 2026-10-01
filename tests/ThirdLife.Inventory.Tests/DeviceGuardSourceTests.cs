using System.Diagnostics;
using System.Runtime.InteropServices;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class DeviceGuardSourceTests
{
    private static readonly int[] SecureBootAvailable = [2];
    private static readonly int[] NoPropertiesAvailable = [0];

    [Fact]
    public void SnapshotCopiesBoundedRawCodesIncludingUnknownAndDuplicateValues()
    {
        uint[] values = [2, 0, uint.MaxValue, 2];
        var snapshot = new DeviceGuardSourceSnapshot(SystemInventorySourceValue<IReadOnlyList<uint>>.Available(values));
        values[0] = 99;

        Assert.Equal(new uint[] { 2, 0, uint.MaxValue, 2 }, snapshot.AvailableSecurityProperties.GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.InvalidData, new DeviceGuardSourceSnapshot(
            SystemInventorySourceValue<IReadOnlyList<uint>>.Available(new uint[17])).AvailableSecurityProperties.Status);
    }

    [Fact]
    public void SingleRowCopiesOnlyFixedPropertyAndReleasesEveryReturnedObject()
    {
        var row = new NativeRow(new int[] { 1, 2, -1 });
        var enumerator = new NativeEnumerator([(0, row, 1), (1, null, 0)]);
        var released = new List<object?>();
        var cleanup = true;

        var result = WindowsDeviceGuardSource.ReadRows(enumerator, Stopwatch.GetTimestamp(), ref cleanup,
            instance => { released.Add(instance); return true; }, CancellationToken.None);

        Assert.Equal(new uint[] { 1, 2, uint.MaxValue }, result.AvailableSecurityProperties.GetRequiredValue());
        Assert.Equal(1, row.ReadCount);
        Assert.Equal(new object?[] { row, null }, released);
        Assert.True(cleanup);
    }

    [Fact]
    public void EmptyEnumerationAndNullPropertyRemainUnavailable()
    {
        foreach (var rows in new (int, IStorageWbemObject?, uint)[][] { [(1, null, 0)], [(1, new NativeRow(null), 1)] })
        {
            var cleanup = true;
            var result = WindowsDeviceGuardSource.ReadRows(new NativeEnumerator(rows), Stopwatch.GetTimestamp(), ref cleanup,
                static _ => true, CancellationToken.None);
            Assert.Equal(SystemInventorySourceStatus.Unavailable, result.AvailableSecurityProperties.Status);
        }
    }

    [Fact]
    public void DuplicateRowsFailClosedAndBothAreReleased()
    {
        var first = new NativeRow(SecureBootAvailable);
        var second = new NativeRow(NoPropertiesAvailable);
        foreach (var rows in new[] { new[] { first, second }, new[] { second, first }, new[] { first, first } })
        {
            var cleanup = true;
            var released = 0;
            var result = WindowsDeviceGuardSource.ReadRows(new NativeEnumerator([(0, rows[0], 1), (1, rows[1], 1)]),
                Stopwatch.GetTimestamp(), ref cleanup, _ => { released++; return true; }, CancellationToken.None);
            Assert.Equal(SystemInventorySourceStatus.InvalidData, result.AvailableSecurityProperties.Status);
            Assert.Equal(2, released);
        }
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(42, 1, true)]
    public void ContradictoryEnumerationMetadataIsInvalid(int status, uint count, bool hasRow)
    {
        var cleanup = true;
        var row = hasRow ? new NativeRow(SecureBootAvailable) : null;
        var released = false;
        var result = WindowsDeviceGuardSource.ReadRows(new NativeEnumerator([(status, row, count)]), Stopwatch.GetTimestamp(),
            ref cleanup, _ => { released = true; return true; }, CancellationToken.None);

        Assert.Equal(SystemInventorySourceStatus.InvalidData, result.AvailableSecurityProperties.Status);
        Assert.True(released);
    }

    [Fact]
    public void MalformedAndOversizedPropertiesRemainInvalid()
    {
        foreach (var value in new object[] { 2, new int[17] })
        {
            var cleanup = true;
            var result = WindowsDeviceGuardSource.ReadRows(new NativeEnumerator([(1, new NativeRow(value), 1)]),
                Stopwatch.GetTimestamp(), ref cleanup, static _ => true, CancellationToken.None);
            Assert.Equal(SystemInventorySourceStatus.InvalidData, result.AvailableSecurityProperties.Status);
        }
    }

    [Fact]
    public void RepeatedTimeoutAndExpiredDeadlineAreBoundedAndUnavailable()
    {
        var cleanup = true;
        var enumerator = new NativeEnumerator([], repeatTimeout: true);
        var result = WindowsDeviceGuardSource.ReadRows(enumerator, Stopwatch.GetTimestamp(), ref cleanup,
            static _ => true, CancellationToken.None);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.AvailableSecurityProperties.Status);
        Assert.Equal(WindowsDeviceGuardSource.MaximumEnumerationCalls, enumerator.Calls);
        var expired = new NativeEnumerator([]);
        result = WindowsDeviceGuardSource.ReadRows(expired, 0, ref cleanup, static _ => true, CancellationToken.None);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.AvailableSecurityProperties.Status);
        Assert.Equal(0, expired.Calls);
    }

    [Fact]
    public void CancellationAfterNextReleasesRowBeforePropagating()
    {
        using var cancellation = new CancellationTokenSource();
        var cleanup = true;
        var row = new NativeRow(SecureBootAvailable);
        var enumerator = new NativeEnumerator([(0, row, 1)], onNext: cancellation.Cancel);
        var released = false;

        Assert.Throws<OperationCanceledException>(() => WindowsDeviceGuardSource.ReadRows(enumerator, Stopwatch.GetTimestamp(),
            ref cleanup, _ => { released = true; return true; }, cancellation.Token));
        Assert.True(released);
        Assert.Equal(0, row.ReadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancelledCollectionReportsCleanupFailureBeforeCancellation(bool releaseSucceeded)
    {
        using var cancellation = new CancellationTokenSource();
        var cleanup = true;
        var row = new NativeRow(SecureBootAvailable);
        var enumerator = new NativeEnumerator([(0, row, 1)], onNext: cancellation.Cancel);
        var cancelled = false;
        var snapshot = new DeviceGuardSourceSnapshot(SystemInventorySourceValue<IReadOnlyList<uint>>.Unavailable());
        try
        {
            snapshot = WindowsDeviceGuardSource.ReadRows(enumerator, Stopwatch.GetTimestamp(), ref cleanup,
                _ => releaseSucceeded, cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }

        Assert.True(cancelled);
        Assert.Equal(releaseSucceeded, cleanup);
        Assert.Equal(0, row.ReadCount);
        if (releaseSucceeded)
        {
            var error = Assert.Throws<OperationCanceledException>(() =>
                WindowsDeviceGuardSource.CompleteRead(snapshot, cancelled, cleanup, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            Assert.Throws<FirmwareSecurityCleanupException>(() =>
                WindowsDeviceGuardSource.CompleteRead(snapshot, cancelled, cleanup, cancellation.Token));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsCrossingDeadlineCannotPublishValues(bool expireDuringPropertyRead)
    {
        var timestamp = 1L;
        var expired = timestamp + checked(Stopwatch.Frequency * 3);
        var row = new NativeRow(SecureBootAvailable, onGet: () =>
        {
            if (expireDuringPropertyRead) { timestamp = expired; }
        });
        var enumerator = new NativeEnumerator([(1, row, 1)], onNext: () =>
        {
            if (!expireDuringPropertyRead) { timestamp = expired; }
        });
        var released = false;
        var cleanup = true;

        var result = WindowsDeviceGuardSource.ReadRows(enumerator, timestamp, ref cleanup,
            value => { Assert.Same(row, value); released = true; return true; }, CancellationToken.None, () => timestamp);

        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.AvailableSecurityProperties.Status);
        Assert.Equal(expireDuringPropertyRead ? 1 : 0, row.ReadCount);
        Assert.Equal(1, enumerator.Calls);
        Assert.True(released);
        Assert.True(cleanup);
    }

    [Fact]
    public void FailedRowReleaseIsExplicitAndStopsFurtherReads()
    {
        var cleanup = true;
        var enumerator = new NativeEnumerator([(0, new NativeRow(SecureBootAvailable), 1), (1, null, 0)]);
        var result = WindowsDeviceGuardSource.ReadRows(enumerator, Stopwatch.GetTimestamp(), ref cleanup,
            static _ => false, CancellationToken.None);

        Assert.False(cleanup);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.AvailableSecurityProperties.Status);
        Assert.Equal(1, enumerator.Calls);
    }

    [Theory]
    [InlineData(0x80070005U, true)]
    [InlineData(0x80041003U, true)]
    [InlineData(0x8004100eU, false)]
    [InlineData(0x80041010U, false)]
    [InlineData(0x8004100cU, false)]
    public void UnsupportedAndDeniedWmiKeepSanitizedCodes(uint code, bool denied)
    {
        var result = WindowsDeviceGuardSource.Failure(code);
        Assert.Equal(code, result.NativeErrorCode);
        Assert.Equal(denied ? SystemInventorySourceStatus.AccessDenied : SystemInventorySourceStatus.Unavailable,
            result.AvailableSecurityProperties.Status);
    }

    private sealed class NativeEnumerator((int Status, IStorageWbemObject? Row, uint Count)[] values,
        bool repeatTimeout = false, Action? onNext = null) : IStorageWbemEnumerator
    {
        internal int Calls { get; private set; }
        public void Reserved01() => throw new NotSupportedException();
        public int Next(int timeoutMilliseconds, uint count, out IStorageWbemObject? value, out uint returned)
        {
            Assert.Equal(100, timeoutMilliseconds);
            Assert.Equal(1U, count);
            var entry = repeatTimeout ? (Status: 0x40004, Row: (IStorageWbemObject?)null, Count: 0U) : values[Calls];
            Calls++;
            onNext?.Invoke();
            value = entry.Row;
            returned = entry.Count;
            return entry.Status;
        }
    }

    private sealed class NativeRow(object? value, Action? onGet = null) : IStorageWbemObject
    {
        internal int ReadCount { get; private set; }
        public void Reserved01() => throw new NotSupportedException();
        public int Get(string name, int flags, nint variant, out int cimType, nint flavor)
        {
            Assert.Equal("AvailableSecurityProperties", name);
            ReadCount++;
            onGet?.Invoke();
            cimType = 19 | 0x2000;
            Marshal.GetNativeVariantForObject(value, variant);
            return 0;
        }
    }
}
