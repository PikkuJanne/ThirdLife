using System.Diagnostics;
using System.Runtime.InteropServices;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class NetworkDeviceSourceTests
{
    private static readonly string[] ClassificationProperties = ["HardwareInterface", "NdisPhysicalMedium"];
    private static readonly string[] HardwareProperty = ["HardwareInterface"];

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(9, true, false)]
    [InlineData(14, false, true)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(4, false, false)]
    [InlineData(5, false, false)]
    [InlineData(6, false, false)]
    [InlineData(7, false, false)]
    [InlineData(8, false, false)]
    [InlineData(10, false, false)]
    [InlineData(11, false, false)]
    [InlineData(12, false, false)]
    [InlineData(13, false, false)]
    [InlineData(15, false, false)]
    [InlineData(16, false, false)]
    [InlineData(17, false, false)]
    [InlineData(18, false, false)]
    public void PhysicalMediaClassifyWithoutConnectionOrIdentityProperties(int medium, bool wifi, bool ethernet)
    {
        foreach (var (deviceClass, expected) in new[] { (DeviceClass.Wifi, wifi), (DeviceClass.Ethernet, ethernet) })
        {
            var row = new NativeRow(true, medium);
            var result = Read(deviceClass, [(1, row, 1)]);

            Assert.Equal(DeviceEnumerationStatus.Available, result.Status);
            Assert.Equal(expected, result.Present);
            Assert.Null(result.ProblemCount);
            Assert.Equal(ClassificationProperties, row.PropertiesRead);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(null)]
    public void AmbiguousMediaCannotProveAbsence(int? medium)
    {
        foreach (var deviceClass in NetworkClasses())
        {
            var result = Read(deviceClass, [(1, new NativeRow(true, medium), 1)]);
            Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
            Assert.Null(result.Present);
        }
    }

    [Fact]
    public void MissingHardwareFlagCannotProvePhysicalPresenceOrAbsence()
    {
        foreach (var deviceClass in NetworkClasses())
        {
            var result = Read(deviceClass, [(1, new NativeRow(null, 9), 1)]);
            Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
            Assert.Null(result.Present);
        }
    }

    [Fact]
    public void NonHardwareRowsAreExcludedWithoutReadingTheirMedium()
    {
        var row = new NativeRow(false, "private network name");
        var result = Read(DeviceClass.Wifi, [(1, row, 1)]);

        Assert.Equal(DeviceEnumerationStatus.Available, result.Status);
        Assert.False(result.Present);
        Assert.Equal(HardwareProperty, row.PropertiesRead);
        Assert.DoesNotContain("private network name", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 20)]
    [InlineData(true, -1)]
    [InlineData(true, "192.0.2.1")]
    [InlineData(true, "00:00:00:00:00:00")]
    [InlineData("personal adapter", 9)]
    [InlineData(1, 9)]
    public void MalformedAndUnrecognizedPropertiesFailClosedWithoutEchoingValues(object hardware, object medium)
    {
        var result = Read(DeviceClass.Wifi, [(0, new NativeRow(true, 9), 1), (1, new NativeRow(hardware, medium), 1)]);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Null(result.NativeErrorCode);
        if (hardware is string hardwareText) { Assert.DoesNotContain(hardwareText, result.ToString(), StringComparison.Ordinal); }
        if (medium is string mediumText) { Assert.DoesNotContain(mediumText, result.ToString(), StringComparison.Ordinal); }
    }

    [Fact]
    public void DefinitePresenceWithAmbiguousAdditionalRowsIsOrderIndependent()
    {
        foreach (var (deviceClass, medium) in new[] { (DeviceClass.Wifi, 9), (DeviceClass.Ethernet, 14) })
        {
            var known = new NativeRow(true, medium);
            var unknown = new NativeRow(true, 0);
            foreach (var rows in new[] { new[] { known, unknown }, new[] { unknown, known } })
            {
                var result = Read(deviceClass, [(0, rows[0], 1), (1, rows[1], 1)]);
                Assert.Equal(DeviceEnumerationStatus.Available, result.Status);
                Assert.True(result.Present);
                Assert.Null(result.ProblemCount);
            }
        }
    }

    [Fact]
    public void CompleteEmptyEnumerationProvesClassAbsence()
    {
        foreach (var deviceClass in NetworkClasses())
        {
            var result = Read(deviceClass, [(1, null, 0)]);
            Assert.Equal(DeviceEnumerationStatus.Available, result.Status);
            Assert.False(result.Present);
            Assert.Null(result.ProblemCount);
        }
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(42, 1, true)]
    public void ContradictoryEnumerationMetadataFailsClosedAndReleasesRow(int status, uint count, bool hasRow)
    {
        var cleanup = true;
        var row = hasRow ? new NativeRow(true, 9) : null;
        var released = false;
        var result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, new NativeEnumerator([(status, row, count)]),
            Stopwatch.GetTimestamp(), ref cleanup, instance => { Assert.Same(row, instance); released = true; return true; },
            CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.True(released);
        if (row is not null) { Assert.Empty(row.PropertiesRead); }
    }

    [Fact]
    public void MaximumRowsSucceedsAndOneExtraRowFailsWithoutReadingOverflowProperties()
    {
        var last = new NativeRow(true, 9);
        var rows = Enumerable.Range(0, WindowsDeviceEnumerationSource.MaximumDevicesPerClass)
            .Select(static _ => (Status: 0, Row: (IStorageWbemObject?)new NativeRow(true, 9), Count: 1U)).ToArray();
        var accepted = Read(DeviceClass.Wifi, [.. rows, (1, null, 0)]);
        Assert.Equal(DeviceEnumerationStatus.Available, accepted.Status);
        Assert.True(accepted.Present);

        var cleanup = true;
        var released = 0;
        var result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, new NativeEnumerator([.. rows, (1, last, 1)]),
            Stopwatch.GetTimestamp(), ref cleanup, _ => { released++; return true; }, CancellationToken.None);

        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Null(result.Present);
        Assert.Equal(WindowsDeviceEnumerationSource.MaximumDevicesPerClass + 1, released);
        Assert.Empty(last.PropertiesRead);
    }

    [Fact]
    public void TimeoutCannotPublishPartialPresenceAndRepeatedTimeoutIsBounded()
    {
        var cleanup = true;
        var enumerator = new NativeEnumerator([(0, new NativeRow(true, 9), 1)], repeatTimeout: true);
        var result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, enumerator, 1, ref cleanup,
            static _ => true, CancellationToken.None, static () => 1);

        Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Null(result.Present);
        Assert.Equal(WindowsNetworkDeviceSource.MaximumEnumerationCalls, enumerator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsCrossingDeadlineReleaseRowAndCannotPublishPresence(bool expireDuringPropertyRead)
    {
        var timestamp = 1L;
        var expired = timestamp + checked(Stopwatch.Frequency * 3);
        var row = new NativeRow(true, 9, onGet: () => { if (expireDuringPropertyRead) { timestamp = expired; } });
        var enumerator = new NativeEnumerator([(1, row, 1)], onNext: () => { if (!expireDuringPropertyRead) { timestamp = expired; } });
        var cleanup = true;
        var released = false;
        var result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, enumerator, timestamp, ref cleanup,
            value => { Assert.Same(row, value); released = true; return true; }, CancellationToken.None, () => timestamp);

        Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Null(result.Present);
        Assert.True(released);
        Assert.Equal(expireDuringPropertyRead ? 2 : 0, row.PropertiesRead.Count);
    }

    [Fact]
    public void ExpiredCollectionAndWrongClassMakeNoEnumerationCall()
    {
        var cleanup = true;
        var enumerator = new NativeEnumerator([]);
        var result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, enumerator, 0, ref cleanup,
            static _ => true, CancellationToken.None, static () => Stopwatch.Frequency * 3);
        Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
        result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Camera, enumerator, 1, ref cleanup,
            static _ => true, CancellationToken.None);
        Assert.Equal(DeviceEnumerationStatus.InvalidData, result.Status);
        Assert.Equal(0, enumerator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationReleasesReturnedRowAndDoesNotReadAnotherProperty(bool cancelDuringProperty)
    {
        using var cancellation = new CancellationTokenSource();
        var cleanup = true;
        var row = new NativeRow(true, 9, onGet: () => { if (cancelDuringProperty) { cancellation.Cancel(); } });
        var enumerator = new NativeEnumerator([(0, row, 1)], onNext: () => { if (!cancelDuringProperty) { cancellation.Cancel(); } });
        var released = false;

        Assert.Throws<OperationCanceledException>(() => WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, enumerator,
            Stopwatch.GetTimestamp(), ref cleanup, _ => { released = true; return true; }, cancellation.Token));
        Assert.True(released);
        Assert.Equal(cancelDuringProperty ? 1 : 0, row.PropertiesRead.Count);
    }

    [Fact]
    public void FailedRowReleaseStopsEnumerationAndCompletionCarriesCleanupFailure()
    {
        var cleanup = true;
        var enumerator = new NativeEnumerator([(0, new NativeRow(true, 9), 1), (1, null, 0)]);
        var result = WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi, enumerator, Stopwatch.GetTimestamp(),
            ref cleanup, static _ => false, CancellationToken.None);

        Assert.False(cleanup);
        Assert.Equal(DeviceEnumerationStatus.Unavailable, result.Status);
        Assert.Equal(1, enumerator.Calls);
        Assert.False(WindowsNetworkDeviceSource.CompleteRead(result, false, cleanup, CancellationToken.None).CleanupComplete);
        Assert.Throws<DeviceEnumerationCleanupException>(() =>
            WindowsNetworkDeviceSource.CompleteRead(result, true, cleanup, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        var error = Assert.Throws<OperationCanceledException>(() =>
            WindowsNetworkDeviceSource.CompleteRead(result, true, true, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData(5U, (int)DeviceEnumerationStatus.AccessDenied)]
    [InlineData(1314U, (int)DeviceEnumerationStatus.AccessDenied)]
    [InlineData(0x80070005U, (int)DeviceEnumerationStatus.AccessDenied)]
    [InlineData(0x80041003U, (int)DeviceEnumerationStatus.AccessDenied)]
    [InlineData(0x80041002U, (int)DeviceEnumerationStatus.Unavailable)]
    [InlineData(0x8004100eU, (int)DeviceEnumerationStatus.Unavailable)]
    [InlineData(0x80041010U, (int)DeviceEnumerationStatus.Unavailable)]
    [InlineData(0x8004100cU, (int)DeviceEnumerationStatus.Unavailable)]
    [InlineData(0x800706baU, (int)DeviceEnumerationStatus.Unavailable)]
    [InlineData(0xffffffffU, (int)DeviceEnumerationStatus.InvalidData)]
    public void NativeFailuresKeepOnlySanitizedCodeAndRelevantClass(uint code, int expected)
    {
        var result = WindowsNetworkDeviceSource.Failure(DeviceClass.Ethernet, code);
        Assert.Equal(DeviceClass.Ethernet, result.Class);
        Assert.Equal((DeviceEnumerationStatus)expected, result.Status);
        Assert.Null(result.Present);
        Assert.Null(result.ProblemCount);
        Assert.Equal(code, result.NativeErrorCode);
    }

    [Fact]
    public void NativeEnumerationFailureReleasesEvenAnUnexpectedReturnedObject()
    {
        var cleanup = true;
        var row = new NativeRow(true, 9);
        var released = false;
        var error = Assert.Throws<FirmwareWmiNativeException>(() => WindowsNetworkDeviceSource.ReadRows(DeviceClass.Wifi,
            new NativeEnumerator([(unchecked((int)0x80041003), row, 1)]), Stopwatch.GetTimestamp(), ref cleanup,
            value => { Assert.Same(row, value); released = true; return true; }, CancellationToken.None));
        Assert.Equal(0x80041003U, error.NativeErrorCode);
        Assert.True(released);
        Assert.Empty(row.PropertiesRead);
    }

    [Fact]
    public void PropertyReadFailureReleasesRowAndKeepsOnlyNativeCode()
    {
        var cleanup = true;
        var row = new NativeRow(null, null, getResult: unchecked((int)0x80041003));
        var released = false;
        var error = Assert.Throws<FirmwareWmiNativeException>(() => WindowsNetworkDeviceSource.ReadRows(DeviceClass.Ethernet,
            new NativeEnumerator([(1, row, 1)]), Stopwatch.GetTimestamp(), ref cleanup,
            value => { Assert.Same(row, value); released = true; return true; }, CancellationToken.None));

        Assert.Equal(0x80041003U, error.NativeErrorCode);
        Assert.True(released);
        Assert.True(cleanup);
        Assert.Single(row.PropertiesRead);
        var result = WindowsNetworkDeviceSource.Failure(DeviceClass.Ethernet, error.NativeErrorCode);
        Assert.Equal(DeviceEnumerationStatus.AccessDenied, result.Status);
        Assert.Null(result.Present);
    }

    [Fact]
    public void ProjectionIsFixedAndContainsOnlyClassificationProperties()
    {
        Assert.Equal(@"root\StandardCimv2", WindowsNetworkDeviceSource.LocalNamespace);
        Assert.Equal("SELECT HardwareInterface, NdisPhysicalMedium FROM MSFT_NetAdapter", WindowsNetworkDeviceSource.QueryText);
    }

    private static DeviceClass[] NetworkClasses() => [DeviceClass.Wifi, DeviceClass.Ethernet];

    private static DeviceClassSnapshot Read(DeviceClass deviceClass, (int Status, IStorageWbemObject? Row, uint Count)[] rows)
    {
        var cleanup = true;
        return WindowsNetworkDeviceSource.ReadRows(deviceClass, new NativeEnumerator(rows), Stopwatch.GetTimestamp(),
            ref cleanup, static _ => true, CancellationToken.None);
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
            var entry = Calls >= values.Length && repeatTimeout
                ? (Status: 0x40004, Row: (IStorageWbemObject?)null, Count: 0U) : values[Calls];
            Calls++;
            onNext?.Invoke();
            value = entry.Row;
            returned = entry.Count;
            return entry.Status;
        }
    }

    private sealed class NativeRow(object? hardware, object? medium, Action? onGet = null, int getResult = 0) : IStorageWbemObject
    {
        internal List<string> PropertiesRead { get; } = [];
        public void Reserved01() => throw new NotSupportedException();
        public int Get(string name, int flags, nint variant, out int cimType, nint flavor)
        {
            Assert.Contains(name, ClassificationProperties);
            PropertiesRead.Add(name);
            onGet?.Invoke();
            cimType = name == "HardwareInterface" ? 11 : 19;
            Marshal.GetNativeVariantForObject(name == "HardwareInterface" ? hardware : medium, variant);
            return getResult;
        }
    }
}
