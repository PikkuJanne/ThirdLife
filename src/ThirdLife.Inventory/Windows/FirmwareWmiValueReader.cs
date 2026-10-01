using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal static class FirmwareWmiValueReader
{
    internal const int MaximumVersionCharacters = 128;

    internal static SystemInventorySourceValue<uint> ReadUInt32(IStorageWbemObject row, string property,
        ref bool cleanupComplete) => Read(row, property, ParseUInt32, ref cleanupComplete);

    internal static SystemInventorySourceValue<bool> ReadBoolean(IStorageWbemObject row, string property,
        ref bool cleanupComplete) => Read(row, property, ParseBoolean, ref cleanupComplete);

    internal static SystemInventorySourceValue<string> ReadVersion(IStorageWbemObject row, string property,
        ref bool cleanupComplete) => Read(row, property, ParseVersion, ref cleanupComplete);

    internal static SystemInventorySourceValue<IReadOnlyList<uint>> ReadUInt32Array(IStorageWbemObject row, string property,
        ref bool cleanupComplete, int maximumCount = 16)
    {
        if (maximumCount is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return Read(row, property, (variant, type) => ParseUInt32Array(variant, type, maximumCount), ref cleanupComplete);
    }

    private static SystemInventorySourceValue<T> Read<T>(IStorageWbemObject row, string property,
        Func<nint, int, SystemInventorySourceValue<T>> parse, ref bool cleanupComplete) where T : notnull
    {
        var variant = Marshal.AllocHGlobal(StorageWmiValueReader.VariantBytes);
        try
        {
            for (var offset = 0; offset < StorageWmiValueReader.VariantBytes; offset += sizeof(long))
            {
                Marshal.WriteInt64(variant, offset, 0);
            }

            SystemInventorySourceValue<T>? result = null;
            Exception? readError = null;
            var clear = 0;
            try
            {
                var status = row.Get(property, 0, variant, out var cimType, 0);
                if (status == unchecked((int)0x80041002))
                {
                    result = SystemInventorySourceValue<T>.Unavailable();
                }
                else
                {
                    if (status < 0)
                    {
                        throw new FirmwareWmiNativeException(unchecked((uint)status));
                    }

                    result = parse(variant, cimType);
                }
            }
            catch (Exception error) { readError = error; }
            finally
            {
                clear = StorageWmiNativeMethods.VariantClear(variant);
                if (clear != 0)
                {
                    cleanupComplete = false;
                }
            }

            if (clear != 0)
            {
                throw new FirmwareWmiCleanupException(unchecked((uint)clear));
            }
            if (readError is not null) { ExceptionDispatchInfo.Throw(readError); }
            return result!;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }
    }

    internal static SystemInventorySourceValue<uint> ParseUInt32(nint variant, int cimType)
    {
        var type = VariantType(variant);
        return type is 0 or 1 ? SystemInventorySourceValue<uint>.Unavailable() :
            cimType == 19 && type == 3 ? SystemInventorySourceValue<uint>.Available(unchecked((uint)Marshal.ReadInt32(variant, 8))) :
            SystemInventorySourceValue<uint>.InvalidData();
    }

    internal static SystemInventorySourceValue<bool> ParseBoolean(nint variant, int cimType)
    {
        var type = VariantType(variant);
        if (type is 0 or 1) { return SystemInventorySourceValue<bool>.Unavailable(); }
        if (cimType != 11 || type != 11) { return SystemInventorySourceValue<bool>.InvalidData(); }
        return Marshal.ReadInt16(variant, 8) switch
        {
            -1 => SystemInventorySourceValue<bool>.Available(true),
            0 => SystemInventorySourceValue<bool>.Available(false),
            _ => SystemInventorySourceValue<bool>.InvalidData(),
        };
    }

    internal static SystemInventorySourceValue<string> ParseVersion(nint variant, int cimType)
    {
        var type = VariantType(variant);
        if (type is 0 or 1) { return SystemInventorySourceValue<string>.Unavailable(); }
        if (cimType != 8 || type != 8) { return SystemInventorySourceValue<string>.InvalidData(); }
        var pointer = Marshal.ReadIntPtr(variant, 8);
        if (pointer == 0) { return SystemInventorySourceValue<string>.Unavailable(); }
        var length = StorageWmiNativeMethods.SysStringLen(pointer);
        if (length > MaximumVersionCharacters) { return SystemInventorySourceValue<string>.InvalidData(); }
        var value = Marshal.PtrToStringUni(pointer, checked((int)length));
        if (value.Length == 0 || value.Equals("Not Supported", StringComparison.Ordinal))
        {
            return SystemInventorySourceValue<string>.Unavailable();
        }

        // Preserve the reported bounded version notation without admitting arbitrary
        // manufacturer text, embedded nulls, identifiers, controls, or localized messages.
        return value.Any(char.IsAsciiDigit) && value.All(static character => char.IsAsciiHexDigit(character) || character is '.' or ',' or ' ')
            ? SystemInventorySourceValue<string>.Available(value)
            : SystemInventorySourceValue<string>.InvalidData();
    }

    internal static SystemInventorySourceValue<IReadOnlyList<uint>> ParseUInt32Array(nint variant, int cimType, int maximumCount)
    {
        if (maximumCount is < 0 or > 16) { return SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData(); }
        var type = VariantType(variant);
        if (type is 0 or 1) { return SystemInventorySourceValue<IReadOnlyList<uint>>.Unavailable(); }
        if (cimType != (19 | 0x2000) || type != (3 | 0x2000)) { return SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData(); }
        var array = Marshal.ReadIntPtr(variant, 8);
        if (array == 0 || StorageWmiNativeMethods.SafeArrayGetDim(array) != 1 ||
            StorageWmiNativeMethods.SafeArrayGetVartype(array, out var elementType) != 0 || elementType != 3 ||
            StorageWmiNativeMethods.SafeArrayGetElemsize(array) != sizeof(int) ||
            StorageWmiNativeMethods.SafeArrayGetLBound(array, 1, out var lower) != 0 ||
            StorageWmiNativeMethods.SafeArrayGetUBound(array, 1, out var upper) != 0)
        {
            return SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData();
        }

        var count = (long)upper - lower + 1;
        if (count < 0 || count > maximumCount) { return SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData(); }
        var values = new uint[(int)count];
        for (var index = 0; index < values.Length; index++)
        {
            var nativeIndex = checked(lower + index);
            if (StorageWmiNativeMethods.SafeArrayGetElement(array, in nativeIndex, out var value) != 0)
            {
                return SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData();
            }

            values[index] = unchecked((uint)value);
        }

        return SystemInventorySourceValue<IReadOnlyList<uint>>.Available(Array.AsReadOnly(values));
    }

    private static ushort VariantType(nint variant) => unchecked((ushort)Marshal.ReadInt16(variant));
}

internal sealed class FirmwareWmiNativeException(uint nativeErrorCode) : Exception("The firmware WMI read failed.")
{
    internal uint NativeErrorCode { get; } = nativeErrorCode;
}

internal sealed class FirmwareWmiCleanupException(uint nativeErrorCode) : Exception("The firmware WMI cleanup failed.")
{
    internal uint NativeErrorCode { get; } = nativeErrorCode;
}
