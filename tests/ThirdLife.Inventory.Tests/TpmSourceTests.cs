using System.Diagnostics;
using System.Text.Json;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class TpmSourceTests
{
    [Fact]
    public void DeviceInfoPreservesRawCodesWithoutInterpretingReservedFields()
    {
        var value = new TpmDeviceInfo(1, 2, 0xfedcba98, uint.MaxValue);
        var result = WindowsTpmSource.DeviceResult(0, value);
        Assert.Equal(FirmwareCollectionStatus.Collected, result.CollectionInfo.Status);
        Assert.Equal(0U, result.ReturnCode);
        Assert.Equal(value, result.Value);
    }

    [Theory]
    [InlineData(0x8028400FU, (int)FirmwareCollectionStatus.NotFound)]
    [InlineData(0x80284012U, (int)FirmwareCollectionStatus.AccessDenied)]
    [InlineData(0x80070005U, (int)FirmwareCollectionStatus.AccessDenied)]
    [InlineData(0x80284010U, (int)FirmwareCollectionStatus.Unavailable)]
    [InlineData(0xffffffffU, (int)FirmwareCollectionStatus.Failed)]
    public void DeviceFailureNeverPublishesOutputBuffer(uint code, int expected)
    {
        var result = WindowsTpmSource.DeviceResult(code, new(1, 2, 3, 4));
        Assert.Equal((FirmwareCollectionStatus)expected, result.CollectionInfo.Status);
        Assert.Equal(code, result.ReturnCode);
        Assert.Equal(code, result.CollectionInfo.NativeErrorCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TbsAndWmiRemainSeparateWhenTheirPresenceFactsConflict()
    {
        var wmi = ReadRows(new NativeEnumerator([(1, StandardRow(), 1)]));
        var source = new WindowsTpmSource(() => WindowsTpmSource.DeviceResult(0x8028400F, new(0, 0, 0, 0)), _ => wmi);
        var result = source.Read(CancellationToken.None);
        Assert.Equal(FirmwareCollectionStatus.NotFound, result.Device.CollectionInfo.Status);
        Assert.True(result.Wmi.InstancePresent);
        Assert.Equal("2.0, 0, 1.59", result.Wmi.SpecVersion.GetRequiredValue());
    }

    [Fact]
    public void UnexpectedErrorsCannotLeakMessagesOrSuppressIndependentReads()
    {
        const string canary = "private-tpm-path-owner-canary";
        var source = new WindowsTpmSource(() => throw new InvalidOperationException(canary),
            _ => throw new UnauthorizedAccessException(canary));
        var result = source.Read(CancellationToken.None);
        Assert.Equal(FirmwareCollectionStatus.Failed, result.Device.CollectionInfo.Status);
        Assert.Equal(FirmwareCollectionStatus.AccessDenied, result.Wmi.CollectionInfo.Status);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void PreCancelledReadCallsNeitherApi()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new WindowsTpmSource(() => throw new InvalidOperationException("must not run"),
            _ => throw new InvalidOperationException("must not run"));
        Assert.Throws<OperationCanceledException>(() => source.Read(cancellation.Token));
    }

    [Fact]
    public void CleanupFailureWinsOverConcurrentCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new WindowsTpmSource(() => WindowsTpmSource.DeviceResult(0, new(1, 2, 3, 4)), _ =>
        {
            cancellation.Cancel();
            return WindowsTpmSource.EmptyWmi(new(FirmwareCollectionStatus.CleanupIncomplete)) with { CleanupComplete = false };
        });
        Assert.Throws<FirmwareSecurityCleanupException>(() => source.Read(cancellation.Token));
    }

    [Fact]
    public void ExactlyOneRowRetainsInitialValuesWithoutInferringReadiness()
    {
        var result = ReadRows(new NativeEnumerator([(1, StandardRow(), 1)]));
        Assert.Equal(FirmwareCollectionStatus.Collected, result.CollectionInfo.Status);
        Assert.True(result.InstancePresent);
        Assert.True(result.EnabledInitial.GetRequiredValue());
        Assert.False(result.ActivatedInitial.GetRequiredValue());
        Assert.True(result.OwnedInitial.GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.Readiness.IsReady.Status);
    }

    [Fact]
    public void EmptyEnumerationOnlyDescribesTheWmiSurface()
    {
        var result = ReadRows(new NativeEnumerator([(1, null, 0)]));
        Assert.Equal(FirmwareCollectionStatus.NotFound, result.CollectionInfo.Status);
        Assert.False(result.InstancePresent);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.SpecVersion.Status);
    }

    [Fact]
    public void DuplicateSingletonRowsFailClosedWithoutChoosingAnOrder()
    {
        var result = ReadRows(new NativeEnumerator([(0, StandardRow(), 1), (1, StandardRow(), 1)]));
        Assert.Equal(FirmwareCollectionStatus.InvalidData, result.CollectionInfo.Status);
        Assert.Null(result.InstancePresent);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.SpecVersion.Status);
    }

    [Fact]
    public void MalformedFieldDoesNotDiscardIndependentInitialFacts()
    {
        var row = new FirmwareTestWbemObject(new Dictionary<string, (int, object?)>
        {
            ["SpecVersion"] = (8, "private-identity-canary"),
            ["ManufacturerId"] = (19, 0),
            ["IsEnabled_InitialValue"] = (11, true),
            ["IsActivated_InitialValue"] = (11, "true"),
        });
        var result = ReadRows(new NativeEnumerator([(1, row, 1)]));
        Assert.Equal(SystemInventorySourceStatus.InvalidData, result.SpecVersion.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.ManufacturerId.Status);
        Assert.True(result.EnabledInitial.GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.InvalidData, result.ActivatedInitial.Status);
        Assert.DoesNotContain("private-identity-canary", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0U, false)]
    [InlineData(1, 2U, true)]
    [InlineData(0x40005, 0U, false)]
    public void IncoherentEnumerationMetadataIsInvalid(int status, uint count, bool rowPresent)
    {
        var result = ReadRows(new NativeEnumerator([(status, rowPresent ? StandardRow() : null, count)]));
        Assert.Equal(FirmwareCollectionStatus.InvalidData, result.CollectionInfo.Status);
    }

    [Fact]
    public void RepeatedFiniteWaitsStopAtTheCallBound()
    {
        var enumerator = new NativeEnumerator([], repeatTimeout: true);
        var result = ReadRows(enumerator);
        Assert.Equal(FirmwareCollectionStatus.TimedOut, result.CollectionInfo.Status);
        Assert.Equal(WindowsTpmSource.MaximumEnumerationCalls, enumerator.CallCount);
    }

    [Fact]
    public void FailedRowReleaseStopsFurtherNativeReads()
    {
        var enumerator = new NativeEnumerator([(0, StandardRow(), 1)]);
        var cleanup = true;
        var result = WindowsTpmSource.ReadRows(enumerator, Stopwatch.GetTimestamp(), ref cleanup,
            static _ => false, CancellationToken.None);
        Assert.False(cleanup);
        Assert.Equal(FirmwareCollectionStatus.CleanupIncomplete, result.CollectionInfo.Status);
        Assert.Equal(1, enumerator.CallCount);
    }

    [Fact]
    public void ReadinessKeepsRawBitmaskAndMethodResult()
    {
        var row = new FirmwareTestWbemObject(new Dictionary<string, (int, object?)>
        {
            ["ReturnValue"] = (19, 0),
            ["Information"] = (19, unchecked((int)0x80000008)),
            ["IsReady"] = (11, false),
        });
        var cleanup = true;
        var result = WindowsTpmSource.ReadReadinessResult(row, ref cleanup);
        Assert.Equal(FirmwareCollectionStatus.Collected, result.CollectionInfo.Status);
        Assert.Equal(0x80000008U, result.Information.GetRequiredValue());
        Assert.False(result.IsReady.GetRequiredValue());
        Assert.Equal(0U, result.ReturnValue.GetRequiredValue());
    }

    [Fact]
    public void FailedMethodDoesNotReadItsUntrustedOutputParameters()
    {
        var row = new FirmwareTestWbemObject(new Dictionary<string, (int, object?)>
        {
            ["ReturnValue"] = (19, unchecked((int)0x80284012)),
            ["Information"] = (19, 0),
            ["IsReady"] = (11, true),
        });
        var cleanup = true;
        var result = WindowsTpmSource.ReadReadinessResult(row, ref cleanup);
        Assert.Equal(FirmwareCollectionStatus.AccessDenied, result.CollectionInfo.Status);
        Assert.Equal(0x80284012U, result.ReturnValue.GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.IsReady.Status);
        Assert.Equal(["ReturnValue"], row.ReadProperties);
    }

    [Fact]
    public void MissingMethodReturnCodeCannotClaimReadiness()
    {
        var row = new FirmwareTestWbemObject(new Dictionary<string, (int, object?)> { ["IsReady"] = (11, true) });
        var cleanup = true;
        var result = WindowsTpmSource.ReadReadinessResult(row, ref cleanup);
        Assert.Equal(FirmwareCollectionStatus.InvalidData, result.CollectionInfo.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, result.IsReady.Status);
    }

    [Fact]
    public void ReadinessCancellationBetweenFieldsStopsTheNextGet()
    {
        using var cancellation = new CancellationTokenSource();
        var row = new FirmwareTestWbemObject(new Dictionary<string, (int, object?)>
        {
            ["ReturnValue"] = (19, 0),
            ["Information"] = (19, 0),
            ["IsReady"] = (11, true),
        })
        { ReadAction = _ => cancellation.Cancel() };
        var cleanup = true;
        Assert.Throws<OperationCanceledException>(() => WindowsTpmSource.ReadReadinessResult(row,
            Stopwatch.GetTimestamp(), ref cleanup, cancellation.Token));
        Assert.True(cleanup);
        Assert.Equal(["ReturnValue"], row.ReadProperties);
    }

    private static TpmWmiSnapshot ReadRows(NativeEnumerator enumerator)
    {
        var cleanup = true;
        var result = WindowsTpmSource.ReadRows(enumerator, Stopwatch.GetTimestamp(), ref cleanup, static _ => true, CancellationToken.None);
        Assert.True(cleanup);
        return result;
    }

    private static FirmwareTestWbemObject StandardRow() => new(new Dictionary<string, (int, object?)>
    {
        ["SpecVersion"] = (8, "2.0, 0, 1.59"),
        ["ManufacturerVersion"] = (8, "7.85.4555.0"),
        ["ManufacturerId"] = (19, 0x12345678),
        ["IsEnabled_InitialValue"] = (11, true),
        ["IsActivated_InitialValue"] = (11, false),
        ["IsOwned_InitialValue"] = (11, true),
    });

    private sealed class NativeEnumerator(IReadOnlyList<(int Status, IStorageWbemObject? Row, uint Count)> values,
        bool repeatTimeout = false) : IStorageWbemEnumerator
    {
        internal int CallCount { get; private set; }
        public void Reserved01() => throw new NotSupportedException();
        public int Next(int timeoutMilliseconds, uint count, out IStorageWbemObject? value, out uint returned)
        {
            Assert.Equal(100, timeoutMilliseconds);
            Assert.Equal(1U, count);
            var item = repeatTimeout ? (Status: 0x40004, Row: (IStorageWbemObject?)null, Count: 0U) : values[CallCount];
            CallCount++;
            value = item.Row;
            returned = item.Count;
            return item.Status;
        }
    }
}
