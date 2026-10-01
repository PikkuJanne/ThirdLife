using System.Runtime.InteropServices;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class FirmwareWmiValueReaderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BooleanUsesTheDocumentedVariantBoolean(bool value)
    {
        using var variant = new FirmwareTestVariant(value);
        Assert.Equal(value, FirmwareWmiValueReader.ParseBoolean(variant.Pointer, 11).GetRequiredValue());
        Marshal.WriteInt16(variant.Pointer, 8, 1);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, FirmwareWmiValueReader.ParseBoolean(variant.Pointer, 11).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    public void UInt32PreservesAllDocumentedSignedVariantBits(int raw)
    {
        using var variant = new FirmwareTestVariant(raw);
        Assert.Equal(unchecked((uint)raw), FirmwareWmiValueReader.ParseUInt32(variant.Pointer, 19).GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.InvalidData, FirmwareWmiValueReader.ParseUInt32(variant.Pointer, 3).Status);
    }

    [Theory]
    [InlineData("2.0, 0, 1.59")]
    [InlineData("1.2, A, 0")]
    [InlineData("7.85.4555.0")]
    public void VersionRetainsOnlyBoundedVersionNotation(string value)
    {
        using var variant = new FirmwareTestVariant(value);
        Assert.Equal(value, FirmwareWmiValueReader.ParseVersion(variant.Pointer, 8).GetRequiredValue());
    }

    [Theory]
    [InlineData("Not Supported", (int)SystemInventorySourceStatus.Unavailable)]
    [InlineData("", (int)SystemInventorySourceStatus.Unavailable)]
    [InlineData("private-owner-canary", (int)SystemInventorySourceStatus.InvalidData)]
    [InlineData("2.0\0private", (int)SystemInventorySourceStatus.InvalidData)]
    [InlineData("2.0\n", (int)SystemInventorySourceStatus.InvalidData)]
    [InlineData("２.０", (int)SystemInventorySourceStatus.InvalidData)]
    public void MissingOrUnsafeVersionCannotEscape(string value, int expected)
    {
        using var variant = new FirmwareTestVariant(value);
        var result = FirmwareWmiValueReader.ParseVersion(variant.Pointer, 8);
        Assert.Equal((SystemInventorySourceStatus)expected, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void OversizedVersionIsRejectedBeforeManagedCopy()
    {
        using var variant = new FirmwareTestVariant(new string('1', FirmwareWmiValueReader.MaximumVersionCharacters + 1));
        Assert.Equal(SystemInventorySourceStatus.InvalidData, FirmwareWmiValueReader.ParseVersion(variant.Pointer, 8).Status);
    }

    [Fact]
    public void NullValuesRemainUnavailableForEveryType()
    {
        using var variant = new FirmwareTestVariant(null);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, FirmwareWmiValueReader.ParseBoolean(variant.Pointer, 11).Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, FirmwareWmiValueReader.ParseUInt32(variant.Pointer, 19).Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, FirmwareWmiValueReader.ParseVersion(variant.Pointer, 8).Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, FirmwareWmiValueReader.ParseUInt32Array(variant.Pointer, 19 | 0x2000, 16).Status);
    }

    [Fact]
    public void UInt32ArrayPreservesOrderAndFullBits()
    {
        int[] values = [2, -1, 0, int.MinValue];
        using var variant = new FirmwareTestVariant(values);
        var result = FirmwareWmiValueReader.ParseUInt32Array(variant.Pointer, 19 | 0x2000, 16);
        Assert.Equal([2U, uint.MaxValue, 0U, 0x80000000U], result.GetRequiredValue());
    }

    [Theory]
    [InlineData(0, (int)SystemInventorySourceStatus.Available)]
    [InlineData(16, (int)SystemInventorySourceStatus.Available)]
    [InlineData(17, (int)SystemInventorySourceStatus.InvalidData)]
    public void UInt32ArrayHasAFixedElementBound(int length, int expected)
    {
        using var variant = new FirmwareTestVariant(new int[length]);
        Assert.Equal((SystemInventorySourceStatus)expected, FirmwareWmiValueReader.ParseUInt32Array(variant.Pointer, 19 | 0x2000, 16).Status);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, FirmwareWmiValueReader.ParseUInt32Array(variant.Pointer, 19 | 0x2000, 17).Status);
    }

    [Fact]
    public void ArrayTagCannotHideADifferentNativeElementWidth()
    {
        long[] values = [1, 2];
        using var variant = new FirmwareTestVariant(values);
        Marshal.WriteInt16(variant.Pointer, 0, 3 | 0x2000);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, FirmwareWmiValueReader.ParseUInt32Array(variant.Pointer, 19 | 0x2000, 16).Status);
    }

    [Fact]
    public void MultipleArrayDimensionsAreRejected()
    {
        using var variant = new FirmwareTestVariant(new int[2, 2]);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, FirmwareWmiValueReader.ParseUInt32Array(variant.Pointer, 19 | 0x2000, 16).Status);
    }

    [Fact]
    public void RowReaderClearsOwnedVariantAndPreservesNativeErrorCode()
    {
        var row = new FirmwareTestWbemObject(new Dictionary<string, (int, object?)>
        {
            ["Version"] = (8, "2.0"),
        });
        var cleanup = true;
        Assert.Equal("2.0", FirmwareWmiValueReader.ReadVersion(row, "Version", ref cleanup).GetRequiredValue());
        Assert.Equal(SystemInventorySourceStatus.Unavailable, FirmwareWmiValueReader.ReadVersion(row, "Missing", ref cleanup).Status);
        row.Error = unchecked((int)0x80041003);
        var error = Assert.Throws<FirmwareWmiNativeException>(() => FirmwareWmiValueReader.ReadVersion(row, "Version", ref cleanup));
        Assert.Equal(0x80041003U, error.NativeErrorCode);
        Assert.True(cleanup);
    }

    [Fact]
    public void CleanupErrorPreservesItsCodeWhenTheReadAlsoFails()
    {
        var row = new FirmwareTestWbemObject([])
        {
            Error = unchecked((int)0x80041003),
            InvalidVariantOnError = true,
        };
        var cleanup = true;
        var error = Assert.Throws<FirmwareWmiCleanupException>(() => FirmwareWmiValueReader.ReadUInt32(row, "Value", ref cleanup));
        Assert.False(cleanup);
        Assert.Equal(0x80020008U, error.NativeErrorCode);
    }
}

internal sealed class FirmwareTestVariant : IDisposable
{
    private readonly short _originalType;
    internal FirmwareTestVariant(object? value)
    {
        Pointer = Marshal.AllocHGlobal(StorageWmiValueReader.VariantBytes);
        for (var offset = 0; offset < StorageWmiValueReader.VariantBytes; offset += 8) { Marshal.WriteInt64(Pointer, offset, 0); }
        Marshal.GetNativeVariantForObject(value, Pointer);
        _originalType = Marshal.ReadInt16(Pointer);
    }
    internal nint Pointer { get; }
    public void Dispose()
    {
        Marshal.WriteInt16(Pointer, _originalType);
        _ = StorageWmiNativeMethods.VariantClear(Pointer);
        Marshal.FreeHGlobal(Pointer);
    }
}

internal sealed class FirmwareTestWbemObject(Dictionary<string, (int CimType, object? Value)> fields) : IStorageWbemObject
{
    internal int Error { get; set; }
    internal bool InvalidVariantOnError { get; set; }
    internal Action<string>? ReadAction { get; set; }
    internal List<string> ReadProperties { get; } = [];
    public void Reserved01() => throw new NotSupportedException();
    public int Get(string name, int flags, nint variant, out int cimType, nint flavor)
    {
        ReadProperties.Add(name);
        cimType = 0;
        if (Error != 0)
        {
            if (InvalidVariantOnError) { Marshal.WriteInt16(variant, 0, 0x0fff); }
            return Error;
        }
        if (!fields.TryGetValue(name, out var field)) { return unchecked((int)0x80041002); }
        cimType = field.CimType;
        Marshal.GetNativeVariantForObject(field.Value, variant);
        ReadAction?.Invoke(name);
        return 0;
    }
}
