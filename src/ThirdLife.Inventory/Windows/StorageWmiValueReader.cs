using System.Globalization;
using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal static class StorageWmiValueReader
{
    // VARIANT is 24 bytes on x64 and 16 bytes on x86. This caller-owned allocation
    // is large enough for both. Variable BSTR/SAFEARRAY data stays native until bounded.
    internal const int VariantBytes = 24;

    internal static object? Read(nint variant, int cimType, StorageWmiValueKind expected)
    {
        var variantType = unchecked((ushort)Marshal.ReadInt16(variant));
        if (variantType is 0 or 1)
        {
            return null;
        }

        switch (expected)
        {
            case StorageWmiValueKind.RoutingString when cimType == 8 && variantType == 8:
            case StorageWmiValueKind.Reference when cimType == 102 && variantType == 8:
                return ReadBoundedString(Marshal.ReadIntPtr(variant, 8), WindowsStorageSource.MaximumRoutingCharacters)
                    ?? (object)StorageInvalidValue.Instance;
            case StorageWmiValueKind.UInt8 when cimType == 17 && variantType == 17:
                return Marshal.ReadByte(variant, 8);
            case StorageWmiValueKind.UInt16 when cimType == 18 && variantType == 3:
                var number = Marshal.ReadInt32(variant, 8);
                return number is >= ushort.MinValue and <= ushort.MaxValue
                    ? (ushort)number
                    : StorageInvalidValue.Instance;
            case StorageWmiValueKind.UInt32 when cimType == 19 && variantType == 3:
                return unchecked((uint)Marshal.ReadInt32(variant, 8));
            case StorageWmiValueKind.UInt64 when cimType == 21 && variantType == 8:
                var text = ReadBoundedString(Marshal.ReadIntPtr(variant, 8), 20);
                return TryUInt64(text, out var value) ? value : StorageInvalidValue.Instance;
            case StorageWmiValueKind.UInt16Array when cimType == (18 | 0x2000) && variantType == (3 | 0x2000):
                return ReadUInt16Array(Marshal.ReadIntPtr(variant, 8));
            default:
                return StorageInvalidValue.Instance;
        }
    }

    private static string? ReadBoundedString(nint value, int maximumCharacters)
    {
        if (value == 0)
        {
            return null;
        }

        var length = StorageWmiNativeMethods.SysStringLen(value);
        return length > maximumCharacters ? null : Marshal.PtrToStringUni(value, checked((int)length));
    }

    internal static bool TryUInt64(string? text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 20)
        {
            return false;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return text.Length is >= 3 and <= 18 &&
                ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static object ReadUInt16Array(nint array)
    {
        if (array == 0 || StorageWmiNativeMethods.SafeArrayGetDim(array) != 1 ||
            StorageWmiNativeMethods.SafeArrayGetVartype(array, out var variantType) != 0 || variantType != 3 ||
            StorageWmiNativeMethods.SafeArrayGetElemsize(array) != sizeof(int) ||
            StorageWmiNativeMethods.SafeArrayGetLBound(array, 1, out var lower) != 0 ||
            StorageWmiNativeMethods.SafeArrayGetUBound(array, 1, out var upper) != 0)
        {
            return StorageInvalidValue.Instance;
        }

        var count = (long)upper - lower + 1;
        if (count is < 0 or > WindowsStorageSource.MaximumOperationalStatusCount)
        {
            return StorageInvalidValue.Instance;
        }

        var values = new ushort[(int)count];
        for (var index = 0; index < values.Length; index++)
        {
            var nativeIndex = checked(lower + index);
            if (StorageWmiNativeMethods.SafeArrayGetElement(array, in nativeIndex, out var element) != 0 ||
                element is < ushort.MinValue or > ushort.MaxValue)
            {
                return StorageInvalidValue.Instance;
            }

            values[index] = (ushort)element;
        }

        return Array.AsReadOnly(values);
    }
}
