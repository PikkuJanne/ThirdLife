using System.Buffers.Binary;
using System.Text;

namespace ThirdLife.Inventory.Windows;

internal enum SmbiosFieldStatus
{
    Available = 1,
    Missing,
    Malformed,
    Conflict,
}

internal sealed class SmbiosField<T>
    where T : notnull
{
    private SmbiosField(SmbiosFieldStatus status, T? value, bool hasValue)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The SMBIOS field status is not defined.");
        }

        if ((status == SmbiosFieldStatus.Available) != hasValue)
        {
            throw new ArgumentException("Only an available SMBIOS field may carry data.", nameof(value));
        }

        Status = status;
        Value = value;
    }

    public SmbiosFieldStatus Status { get; }

    public T? Value { get; }

    public static SmbiosField<T> Available(T value) =>
        new(
            SmbiosFieldStatus.Available,
            value ?? throw new ArgumentNullException(nameof(value)),
            hasValue: true);

    public static SmbiosField<T> Missing() =>
        new(SmbiosFieldStatus.Missing, value: default, hasValue: false);

    public static SmbiosField<T> Malformed() =>
        new(SmbiosFieldStatus.Malformed, value: default, hasValue: false);

    public static SmbiosField<T> Conflict() =>
        new(SmbiosFieldStatus.Conflict, value: default, hasValue: false);

    public T GetRequiredValue() =>
        Status == SmbiosFieldStatus.Available
            ? Value!
            : throw new InvalidOperationException("The SMBIOS field is not available.");
}

internal sealed class SmbiosProcessorInformation
{
    public SmbiosProcessorInformation(
        ushort handle,
        SmbiosField<string> manufacturer,
        SmbiosField<string> model)
    {
        Handle = handle;
        Manufacturer = manufacturer ?? throw new ArgumentNullException(nameof(manufacturer));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public ushort Handle { get; }

    public SmbiosField<string> Manufacturer { get; }

    public SmbiosField<string> Model { get; }
}

internal sealed class SmbiosInventory
{
    public SmbiosInventory(
        SmbiosField<string> manufacturer,
        SmbiosField<string> model,
        SmbiosField<string> serialNumber,
        IReadOnlyList<SmbiosField<byte>> chassisTypes,
        IReadOnlyList<SmbiosProcessorInformation> processors)
    {
        Manufacturer = manufacturer ?? throw new ArgumentNullException(nameof(manufacturer));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        SerialNumber = serialNumber ?? throw new ArgumentNullException(nameof(serialNumber));
        ChassisTypes = chassisTypes ?? throw new ArgumentNullException(nameof(chassisTypes));
        Processors = processors ?? throw new ArgumentNullException(nameof(processors));
    }

    public SmbiosField<string> Manufacturer { get; }

    public SmbiosField<string> Model { get; }

    public SmbiosField<string> SerialNumber { get; }

    public IReadOnlyList<SmbiosField<byte>> ChassisTypes { get; }

    public IReadOnlyList<SmbiosProcessorInformation> Processors { get; }
}

internal static class SmbiosInventoryParser
{
    internal const int RawHeaderBytes = 8;
    internal const int MaximumProcessorRecords = 8;
    internal const int MaximumTextBytes = 256;
    internal const int MaximumTextCharacters = 256;
    internal const int MaximumSerialBytes = 128;
    internal const int MaximumSerialCharacters = 128;

    private const int MaximumStructureCount = 1024;
    private const int SmbiosStructureHeaderBytes = 4;
    private const byte SystemInformationType = 1;
    private const byte SystemEnclosureType = 3;
    private const byte ProcessorInformationType = 4;
    private const byte EndOfTableType = 127;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "default string",
        "none",
        "not applicable",
        "not available",
        "not specified",
        "system manufacturer",
        "system product name",
        "system serial number",
        "to be filled by o.e.m.",
        "to be filled by oem",
        "unknown",
    };

    public static bool TryParse(
        byte[] rawFirmwareTable,
        CancellationToken cancellationToken,
        out SmbiosInventory? inventory)
    {
        ArgumentNullException.ThrowIfNull(rawFirmwareTable);
        inventory = null;
        cancellationToken.ThrowIfCancellationRequested();

        if (rawFirmwareTable.Length < RawHeaderBytes ||
            rawFirmwareTable.Length > WindowsSystemInventorySource.MaximumFirmwareTableBytes)
        {
            return false;
        }

        var declaredTableBytes = BinaryPrimitives.ReadUInt32LittleEndian(
            rawFirmwareTable.AsSpan(4, sizeof(uint)));
        if (declaredTableBytes == 0 || declaredTableBytes != rawFirmwareTable.Length - RawHeaderBytes)
        {
            return false;
        }

        var table = rawFirmwareTable.AsSpan(RawHeaderBytes);
        var handles = new HashSet<ushort>();
        var chassisTypes = new List<SmbiosField<byte>>();
        var processors = new List<SmbiosProcessorInformation>();
        var manufacturer = SmbiosField<string>.Missing();
        var model = SmbiosField<string>.Missing();
        var serialNumber = SmbiosField<string>.Missing();
        var systemInformationSeen = false;
        var endOfTableSeen = false;
        var offset = 0;
        var structureCount = 0;

        while (offset < table.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            structureCount++;
            if (structureCount > MaximumStructureCount ||
                table.Length - offset < SmbiosStructureHeaderBytes)
            {
                return false;
            }

            var type = table[offset];
            var formattedLength = table[offset + 1];
            if (formattedLength < SmbiosStructureHeaderBytes ||
                formattedLength > table.Length - offset)
            {
                return false;
            }

            var handle = BinaryPrimitives.ReadUInt16LittleEndian(table.Slice(offset + 2, sizeof(ushort)));
            if (!handles.Add(handle))
            {
                return false;
            }

            var stringSetStart = offset + formattedLength;
            if (!TryFindStringSetEnd(table, stringSetStart, out var stringSetEnd) ||
                !ValidateStringSet(table, stringSetStart, stringSetEnd))
            {
                return false;
            }

            var formatted = table.Slice(offset, formattedLength);
            switch (type)
            {
                case SystemInformationType:
                    if (systemInformationSeen || formattedLength < 8)
                    {
                        return false;
                    }

                    systemInformationSeen = true;
                    manufacturer = ReadTextField(
                        table,
                        stringSetStart,
                        stringSetEnd,
                        formatted[4],
                        TextFieldKind.General);
                    model = ReadTextField(
                        table,
                        stringSetStart,
                        stringSetEnd,
                        formatted[5],
                        TextFieldKind.General);
                    serialNumber = ReadTextField(
                        table,
                        stringSetStart,
                        stringSetEnd,
                        formatted[7],
                        TextFieldKind.Serial);
                    break;

                case SystemEnclosureType:
                    chassisTypes.Add(
                        formattedLength > 5
                            ? SmbiosField<byte>.Available((byte)(formatted[5] & 0x7f))
                            : SmbiosField<byte>.Malformed());
                    break;

                case ProcessorInformationType:
                    const byte centralProcessorType = 0x03;
                    if (formattedLength <= 5)
                    {
                        return false;
                    }

                    if (formatted[5] != centralProcessorType)
                    {
                        break;
                    }

                    if (formattedLength <= 24)
                    {
                        if (processors.Count >= MaximumProcessorRecords)
                        {
                            return false;
                        }

                        processors.Add(new SmbiosProcessorInformation(
                            handle,
                            SmbiosField<string>.Malformed(),
                            SmbiosField<string>.Malformed()));
                        break;
                    }

                    const byte populatedSocketMask = 0x40;
                    if ((formatted[24] & populatedSocketMask) == 0)
                    {
                        break;
                    }

                    if (processors.Count >= MaximumProcessorRecords)
                    {
                        return false;
                    }

                    processors.Add(new SmbiosProcessorInformation(
                        handle,
                        ReadTextField(
                            table,
                            stringSetStart,
                            stringSetEnd,
                            formatted[7],
                            TextFieldKind.General),
                        ReadTextField(
                            table,
                            stringSetStart,
                            stringSetEnd,
                            formatted[16],
                            TextFieldKind.Processor)));
                    break;

                case EndOfTableType:
                    if (formattedLength != SmbiosStructureHeaderBytes ||
                        stringSetStart != stringSetEnd ||
                        stringSetEnd + 2 != table.Length)
                    {
                        return false;
                    }

                    endOfTableSeen = true;
                    offset = table.Length;
                    continue;
            }

            offset = stringSetEnd + 2;
        }

        if (!endOfTableSeen)
        {
            return false;
        }

        inventory = new SmbiosInventory(
            manufacturer,
            model,
            serialNumber,
            chassisTypes.AsReadOnly(),
            processors.OrderBy(static value => value.Handle).ToArray());
        return true;
    }

    private static bool TryFindStringSetEnd(ReadOnlySpan<byte> table, int start, out int end)
    {
        end = -1;
        if (start < 0 || start > table.Length - 2)
        {
            return false;
        }

        for (var index = start; index < table.Length - 1; index++)
        {
            if (table[index] == 0 && table[index + 1] == 0)
            {
                end = index;
                return true;
            }
        }

        return false;
    }

    private static bool ValidateStringSet(ReadOnlySpan<byte> table, int start, int end)
    {
        if (start == end)
        {
            return true;
        }

        var cursor = start;
        while (cursor < end)
        {
            var terminator = table.Slice(cursor, end - cursor + 1).IndexOf((byte)0);
            if (terminator <= 0)
            {
                return false;
            }

            cursor += terminator + 1;
        }

        return cursor == end + 1;
    }

    private static SmbiosField<string> ReadTextField(
        ReadOnlySpan<byte> table,
        int stringSetStart,
        int stringSetEnd,
        byte oneBasedIndex,
        TextFieldKind fieldKind)
    {
        if (oneBasedIndex == 0)
        {
            return SmbiosField<string>.Missing();
        }

        var currentIndex = 1;
        var cursor = stringSetStart;
        while (cursor < stringSetEnd)
        {
            var terminator = table.Slice(cursor, stringSetEnd - cursor + 1).IndexOf((byte)0);
            if (terminator <= 0)
            {
                return SmbiosField<string>.Malformed();
            }

            if (currentIndex == oneBasedIndex)
            {
                return NormalizeText(table.Slice(cursor, terminator), fieldKind);
            }

            currentIndex++;
            cursor += terminator + 1;
        }

        return SmbiosField<string>.Malformed();
    }

    private static SmbiosField<string> NormalizeText(ReadOnlySpan<byte> bytes, TextFieldKind fieldKind)
    {
        var maximumBytes = fieldKind == TextFieldKind.Serial ? MaximumSerialBytes : MaximumTextBytes;
        var maximumCharacters = fieldKind == TextFieldKind.Serial
            ? MaximumSerialCharacters
            : MaximumTextCharacters;
        if (bytes.IsEmpty || bytes.Length > maximumBytes)
        {
            return bytes.IsEmpty ? SmbiosField<string>.Missing() : SmbiosField<string>.Malformed();
        }

        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SmbiosField<string>.Malformed();
        }

        if (decoded.Length > maximumCharacters || decoded.Any(char.IsControl))
        {
            return SmbiosField<string>.Malformed();
        }

        var normalized = fieldKind == TextFieldKind.Serial
            ? decoded.Trim(' ')
            : string.Join(' ', decoded.Trim(' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0 || IsPlaceholder(normalized, fieldKind))
        {
            return SmbiosField<string>.Missing();
        }

        if (!IsAllowlistedText(normalized, fieldKind))
        {
            return SmbiosField<string>.Malformed();
        }

        return SmbiosField<string>.Available(normalized);
    }

    private static bool IsPlaceholder(string value, TextFieldKind fieldKind)
    {
        if (PlaceholderValues.Contains(value))
        {
            return true;
        }

        if (fieldKind != TextFieldKind.Serial)
        {
            return false;
        }

        var significantCharacters = value.Where(static character =>
            character is not ' ' and not '-' and not '_' and not '.' and not '/' and not '#').ToArray();
        return significantCharacters.Length == 0 ||
            significantCharacters.All(static character => character == '0') ||
            significantCharacters.All(static character => character is 'f' or 'F');
    }

    private static bool IsAllowlistedText(string value, TextFieldKind fieldKind)
    {
        if (value[0] is '=' or '+' or '-' or '@' ||
            value.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character == ' ')
            {
                continue;
            }

            var allowed = fieldKind switch
            {
                TextFieldKind.Serial => character is '-' or '_' or '.' or '/' or '#',
                TextFieldKind.General => character is '-' or '_' or '.' or '(' or ')' or '/' or '+' or
                    '#' or '&' or '\'' or ',' or ':',
                TextFieldKind.Processor => character is '-' or '_' or '.' or '(' or ')' or '/' or '+' or
                    '@' or '#' or '&' or '\'' or ',' or ':',
                _ => false,
            };
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private enum TextFieldKind
    {
        General = 1,
        Serial,
        Processor,
    }
}
