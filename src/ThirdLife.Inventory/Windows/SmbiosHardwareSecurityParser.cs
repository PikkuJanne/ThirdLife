using System.Buffers.Binary;

namespace ThirdLife.Inventory.Windows;

internal static class SmbiosHardwareSecurityParser
{
    internal const int MaximumStructures = 1024;

    internal static SystemInventorySourceValue<byte> Parse(byte[] rawTable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawTable);
        cancellationToken.ThrowIfCancellationRequested();
        if (rawTable.Length < SmbiosInventoryParser.RawHeaderBytes ||
            rawTable.Length > WindowsSystemInventorySource.MaximumFirmwareTableBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(rawTable.AsSpan(4, 4)) != rawTable.Length - SmbiosInventoryParser.RawHeaderBytes)
        {
            return SystemInventorySourceValue<byte>.InvalidData();
        }

        var table = rawTable.AsSpan(SmbiosInventoryParser.RawHeaderBytes);
        var handles = new HashSet<ushort>();
        byte? settings = null;
        var offset = 0;
        var structures = 0;
        while (offset < table.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++structures > MaximumStructures || table.Length - offset < 4)
            {
                return SystemInventorySourceValue<byte>.InvalidData();
            }

            var type = table[offset];
            var length = table[offset + 1];
            var handle = BinaryPrimitives.ReadUInt16LittleEndian(table.Slice(offset + 2, 2));
            if (length < 4 || length > table.Length - offset || !handles.Add(handle))
            {
                return SystemInventorySourceValue<byte>.InvalidData();
            }

            var strings = offset + length;
            var terminator = FindTerminator(table, strings);
            if (terminator < 0 || (strings < terminator && table[strings] == 0))
            {
                return SystemInventorySourceValue<byte>.InvalidData();
            }

            if (type == 24)
            {
                // DSP0134 Table 93: header + one settings byte, no string fields.
                // Reject duplicates even when equal; no enumeration-order selection.
                if (settings is not null || length != 5 || strings != terminator)
                {
                    return SystemInventorySourceValue<byte>.InvalidData();
                }

                settings = table[offset + 4];
            }
            else if (type == 127)
            {
                if (length != 4 || strings != terminator || terminator + 2 != table.Length)
                {
                    return SystemInventorySourceValue<byte>.InvalidData();
                }

                return settings is { } value
                    ? SystemInventorySourceValue<byte>.Available(value)
                    : SystemInventorySourceValue<byte>.Unavailable();
            }

            offset = terminator + 2;
        }

        return SystemInventorySourceValue<byte>.InvalidData();
    }

    private static int FindTerminator(ReadOnlySpan<byte> table, int start)
    {
        // Scan bounded raw bytes without decoding SMBIOS identifiers or string pools.
        for (var index = start; index < table.Length - 1; index++)
        {
            if (table[index] == 0 && table[index + 1] == 0) { return index; }
        }

        return -1;
    }
}
