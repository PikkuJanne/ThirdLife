using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class FirmwarePlatformSourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SecureBootDwordRetainsOnlyTheDocumentedEnabledValue(uint value)
    {
        var result = WindowsFirmwarePlatformNativeApi.MapRegistryValue(0, 4, 4, value);

        Assert.Equal(value, result.Value.GetRequiredValue());
        Assert.Null(result.NativeErrorCode);
    }

    [Theory]
    [InlineData(0, 1, 4, 1)]
    [InlineData(0, 4, 0, 1)]
    [InlineData(0, 4, 8, 1)]
    [InlineData(0, 4, 4, 2)]
    [InlineData(234, 4, 8, 0)]
    public void MalformedRegistryTypeLengthValueAndOverflowRemainInvalid(int status, uint type, uint bytes, uint value)
    {
        Assert.Equal(SystemInventorySourceStatus.InvalidData,
            WindowsFirmwarePlatformNativeApi.MapRegistryValue(status, type, bytes, value).Value.Status);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(1314, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(50, false)]
    [InlineData(120, false)]
    public void DenialAndUnavailableNativeApisDoNotProduceFalseValues(uint error, bool denied)
    {
        var result = WindowsFirmwarePlatformNativeApi.Failure(error);

        Assert.Equal(denied ? SystemInventorySourceStatus.AccessDenied : SystemInventorySourceStatus.Unavailable, result.Value.Status);
        Assert.Equal(error, result.NativeErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x40)]
    [InlineData(0x10)]
    [InlineData(0x04)]
    [InlineData(0x01)]
    [InlineData(0x55)]
    [InlineData(0xaa)]
    [InlineData(0xff)]
    public void HardwareSecurityRawByteIsPreservedWithoutOwnershipInference(byte settings)
    {
        var result = SmbiosHardwareSecurityParser.Parse(Table(Security(settings), End()));

        Assert.Equal(settings, result.GetRequiredValue());
    }

    [Fact]
    public void MissingOptionalTypeIsUnavailableWhileAllUnknownRawByteRemainsObserved()
    {
        Assert.Equal(SystemInventorySourceStatus.Unavailable, SmbiosHardwareSecurityParser.Parse(Table(End())).Status);
        Assert.Equal(255, SmbiosHardwareSecurityParser.Parse(Table(Security(255), End())).GetRequiredValue());
    }

    [Fact]
    public void DuplicateRecordsAndHandlesFailClosedRegardlessOfOrderOrEqualValues()
    {
        foreach (var pair in new[] { (First: (byte)0, Second: (byte)4), (First: (byte)4, Second: (byte)0), (First: (byte)4, Second: (byte)4) })
        {
            AssertInvalid(Table(Security(pair.First, 1), Security(pair.Second, 2), End()));
        }

        AssertInvalid(Table(Security(4, 1), [42, 4, 1, 0, 0, 0], End()));
    }

    [Fact]
    public void TruncatedUnexpectedLengthStringPoolAndMissingEndAreInvalid()
    {
        AssertInvalid([]);
        AssertInvalid(new byte[8]);
        AssertInvalid(Table([24, 4, 1, 0, 0, 0], End()));
        AssertInvalid(Table([24, 6, 1, 0, 4, 0, 0, 0], End()));
        AssertInvalid(Table([24, 5, 1, 0, 4, (byte)'x', 0, 0], End()));
        AssertInvalid(Table(Security(4)));
        AssertInvalid(Table(Security(4), [127, 4, 255, 255, 0]));
        AssertInvalid(Table(Security(4), End(), [0]));
        AssertInvalid(Table([42, 3, 2, 0, 0, 0], End()));
        AssertInvalid(Table([42, 40, 2, 0, 0, 0], End()));
        AssertInvalid(Table([42, 4, 2, 0, 0, (byte)'x', 0, 0], End()));
        var mismatched = Table(Security(4), End());
        mismatched[4]++;
        AssertInvalid(mismatched);
    }

    [Fact]
    public void TableAndStructureBoundsIncludeUnrelatedRecords()
    {
        AssertInvalid(new byte[WindowsSystemInventorySource.MaximumFirmwareTableBytes + 1]);
        var structures = Enumerable.Range(1, SmbiosHardwareSecurityParser.MaximumStructures)
            .Select(static index => new byte[] { 42, 4, (byte)index, (byte)(index >> 8), 0, 0 }).ToList();
        structures.Add(End());
        AssertInvalid(Table(structures.ToArray()));
        structures.RemoveAt(0);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, SmbiosHardwareSecurityParser.Parse(Table(structures.ToArray())).Status);
    }

    [Fact]
    public void SourceNeverDecodesPrivateStringsAndClearsTheOwnedRawBuffer()
    {
        const string canary = "SYNTHETIC-SERIAL-USERNAME-COMPUTER";
        var bytes = Table([42, 4, 3, 0, .. Encoding.UTF8.GetBytes(canary), 0, 0], Security(0x55), End());
        var native = new NativeFixture { FirmwareTable = () => SystemInventorySourceValue<byte[]>.Available(bytes) };

        var result = new WindowsFirmwarePlatformSource(native).Read(CancellationToken.None);

        Assert.Equal(2U, result.FirmwareType.GetRequiredValue());
        Assert.Equal(1U, result.SecureBootRegistryValue.GetRequiredValue());
        Assert.Equal(0x55, result.HardwareSecuritySettings.GetRequiredValue());
        Assert.All(bytes, static value => Assert.Equal(0, value));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void IndependentFactsRemainAvailableWhenHardwareSecurityReadIsDenied()
    {
        var native = new NativeFixture { FirmwareTable = static () => SystemInventorySourceValue<byte[]>.AccessDenied() };
        var result = new WindowsFirmwarePlatformSource(native).Read(CancellationToken.None);

        Assert.Equal(SystemInventorySourceStatus.Available, result.FirmwareType.Status);
        Assert.Equal(SystemInventorySourceStatus.Available, result.SecureBootRegistryValue.Status);
        Assert.Equal(SystemInventorySourceStatus.AccessDenied, result.HardwareSecuritySettings.Status);
    }

    [Fact]
    public void MalformedOrCancelledParsingStillClearsRawBytes()
    {
        var malformed = Table(Security(4));
        var invalid = WindowsFirmwarePlatformSource.ReadHardwareSecurityAndClear(
            SystemInventorySourceValue<byte[]>.Available(malformed), CancellationToken.None);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, invalid.Status);
        Assert.All(malformed, static value => Assert.Equal(0, value));
        var bytes = Table(Security(4), End());
        using var cancellation = new CancellationTokenSource();
        var native = new NativeFixture
        {
            FirmwareTable = () => { cancellation.Cancel(); return SystemInventorySourceValue<byte[]>.Available(bytes); },
        };
        Assert.Throws<OperationCanceledException>(() => new WindowsFirmwarePlatformSource(native).Read(cancellation.Token));
        Assert.All(bytes, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void PreCancellationStartsNoNativeRead()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var native = new NativeFixture();
        Assert.Throws<OperationCanceledException>(() => new WindowsFirmwarePlatformSource(native).Read(cancellation.Token));
        Assert.Equal(0, native.Calls);
    }

    private static void AssertInvalid(byte[] bytes) =>
        Assert.Equal(SystemInventorySourceStatus.InvalidData, SmbiosHardwareSecurityParser.Parse(bytes).Status);

    private static byte[] Security(byte settings, ushort handle = 1) => [24, 5, (byte)handle, (byte)(handle >> 8), settings, 0, 0];
    private static byte[] End() => [127, 4, 255, 255, 0, 0];

    private static byte[] Table(params byte[][] structures)
    {
        var contents = structures.SelectMany(static value => value).ToArray();
        var bytes = new byte[contents.Length + 8];
        bytes[1] = 3;
        bytes[2] = 7;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), checked((uint)contents.Length));
        contents.CopyTo(bytes, 8);
        return bytes;
    }

    private sealed class NativeFixture : IFirmwarePlatformNativeApi
    {
        internal Func<SystemInventorySourceValue<byte[]>> FirmwareTable { get; init; } = static () => SystemInventorySourceValue<byte[]>.Unavailable();
        internal int Calls { get; private set; }
        public FirmwarePlatformNativeValue ReadFirmwareType() { Calls++; return new(SystemInventorySourceValue<uint>.Available(2)); }
        public FirmwarePlatformNativeValue ReadSecureBootRegistryValue() { Calls++; return new(SystemInventorySourceValue<uint>.Available(1)); }
        public SystemInventorySourceValue<byte[]> ReadFirmwareTable() { Calls++; return FirmwareTable(); }
    }
}
