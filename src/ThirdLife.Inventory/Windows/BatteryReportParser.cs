using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ThirdLife.Inventory.Windows;

internal enum BatteryReportParseStatus
{
    Collected = 1,
    NoBattery,
    InvalidData,
}

internal sealed record BatteryReportEntry(
    int Ordinal,
    SystemInventorySourceValue<uint> DesignCapacityMwh,
    SystemInventorySourceValue<uint> FullChargeCapacityMwh,
    SystemInventorySourceValue<uint> CycleCount);

internal sealed class BatteryReportParseResult
{
    internal BatteryReportParseResult(BatteryReportParseStatus status, IEnumerable<BatteryReportEntry> batteries)
    {
        ArgumentNullException.ThrowIfNull(batteries);
        var entries = batteries.Take(BatteryReportParser.MaximumBatteries + 1).ToArray();
        if (!Enum.IsDefined(status) || entries.Length > BatteryReportParser.MaximumBatteries ||
            entries.Any(static entry => entry is null) ||
            (status == BatteryReportParseStatus.Collected) != (entries.Length > 0))
        {
            throw new ArgumentException("The battery parse result is not a bounded valid state.", nameof(batteries));
        }

        Status = status;
        Batteries = Array.AsReadOnly(entries);
    }

    internal BatteryReportParseStatus Status { get; }

    internal ReadOnlyCollection<BatteryReportEntry> Batteries { get; }
}

internal static class BatteryReportParser
{
    internal const int MaximumReportBytes = 4 * 1024 * 1024;
    internal const int MaximumBatteries = 4;
    internal const int MaximumDepth = 16;
    internal const int MaximumNodes = 100_000;
    // The reviewed active Windows emitter uses 18 attributes in an ignored
    // history element. Bound that wider shape while scalar attributes remain
    // restricted separately to namespace declarations.
    internal const int MaximumAttributes = 64;
    internal const string ReportNamespace = "http://schemas.microsoft.com/battery/2012";
    internal const string ReportVersion = "1.1";

    internal static BatteryReportParseResult Parse(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead)
        {
            return Invalid();
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumReportBytes,
            MaxCharactersFromEntities = 1024,
            ConformanceLevel = ConformanceLevel.Document,
            CloseInput = false,
        };
        using var bounded = new BoundedReadStream(stream, cancellationToken);
        try
        {
            using var reader = XmlReader.Create(bounded, settings);
            return ParseDocument(reader, cancellationToken);
        }
        catch (XmlException)
        {
            // XML exception messages may contain report data. Retain only the typed state.
            return Invalid();
        }
    }

    private static BatteryReportParseResult ParseDocument(XmlReader reader, CancellationToken cancellationToken)
    {
        var contexts = new ElementContext[MaximumDepth + 1];
        var entries = new List<BatteryReportEntry>();
        var version = new ScalarField();
        BatteryFields? battery = null;
        var sawRoot = false;
        var sawInformation = false;
        var sawBatteries = false;
        var nodes = 0;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++nodes > MaximumNodes || reader.Depth > MaximumDepth || reader.AttributeCount > MaximumAttributes ||
                reader.LocalName.Length > 128 || reader.NamespaceURI.Length > 512)
            {
                return Invalid();
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                var parent = reader.Depth == 0 ? ElementContext.None : contexts[reader.Depth - 1];
                var context = ElementContext.Ignored;
                var reportNamespace = reader.NamespaceURI == ReportNamespace;
                if (reader.Depth == 0)
                {
                    if (sawRoot || reader.LocalName != "BatteryReport" || !reportNamespace)
                    {
                        return Invalid();
                    }

                    sawRoot = true;
                    context = ElementContext.Root;
                }
                else if (parent == ElementContext.Root && reader.LocalName == "ReportInformation")
                {
                    if (sawInformation || !reportNamespace)
                    {
                        return Invalid();
                    }

                    sawInformation = true;
                    context = ElementContext.Information;
                }
                else if (parent == ElementContext.Root && reader.LocalName == "Batteries")
                {
                    if (sawBatteries || !reportNamespace)
                    {
                        return Invalid();
                    }

                    sawBatteries = true;
                    context = ElementContext.Batteries;
                }
                else if (parent == ElementContext.Information && reader.LocalName == "ReportVersion")
                {
                    if (version.Seen || !reportNamespace)
                    {
                        return Invalid();
                    }

                    context = ElementContext.Version;
                    version.Begin(HasOnlyNamespaceAttributes(reader));
                }
                else if (parent == ElementContext.Batteries)
                {
                    if (reader.LocalName != "Battery" || !reportNamespace || entries.Count >= MaximumBatteries)
                    {
                        return Invalid();
                    }

                    battery = new BatteryFields();
                    context = ElementContext.Battery;
                }
                else if (parent == ElementContext.Battery)
                {
                    context = reader.LocalName switch
                    {
                        "RelativeCapacity" => ElementContext.RelativeCapacity,
                        "DesignCapacity" => ElementContext.DesignCapacity,
                        "FullChargeCapacity" => ElementContext.FullChargeCapacity,
                        "CycleCount" => ElementContext.CycleCount,
                        _ => ElementContext.Ignored,
                    };
                    battery!.Field(context)?.Begin(reportNamespace && HasOnlyNamespaceAttributes(reader));
                }
                else if (parent == ElementContext.Version)
                {
                    version.Invalidate();
                }
                else
                {
                    battery?.Field(parent)?.Invalidate();
                }

                contexts[reader.Depth] = context;
                if (reader.IsEmptyElement && context == ElementContext.Battery)
                {
                    entries.Add(battery!.CreateEntry(entries.Count));
                    battery = null;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && contexts[reader.Depth] == ElementContext.Battery)
            {
                entries.Add(battery!.CreateEntry(entries.Count));
                battery = null;
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                var parent = reader.Depth == 0 ? ElementContext.None : contexts[reader.Depth - 1];
                if (parent == ElementContext.Version)
                {
                    version.Append(reader.Value);
                }
                else if (battery?.Field(parent) is { } field)
                {
                    field.Append(reader.Value);
                }
                else if (parent is ElementContext.Root or ElementContext.Information or ElementContext.Batteries or ElementContext.Battery &&
                    !IsXmlWhitespace(reader.Value))
                {
                    return Invalid();
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!sawRoot || !sawInformation || !sawBatteries || version.ReadText() != ReportVersion)
        {
            return Invalid();
        }

        return new BatteryReportParseResult(
            entries.Count == 0 ? BatteryReportParseStatus.NoBattery : BatteryReportParseStatus.Collected,
            entries);
    }

    private static bool HasOnlyNamespaceAttributes(XmlReader reader)
    {
        if (!reader.HasAttributes)
        {
            return true;
        }

        var valid = true;
        while (reader.MoveToNextAttribute())
        {
            if (reader.NamespaceURI != "http://www.w3.org/2000/xmlns/")
            {
                valid = false;
            }
        }

        reader.MoveToElement();
        return valid;
    }

    private static bool IsXmlWhitespace(string value) => value.All(static character => character is ' ' or '\t' or '\r' or '\n');

    private static BatteryReportParseResult Invalid() => new(BatteryReportParseStatus.InvalidData, []);

    private enum ElementContext
    {
        None,
        Ignored,
        Root,
        Information,
        Version,
        Batteries,
        Battery,
        RelativeCapacity,
        DesignCapacity,
        FullChargeCapacity,
        CycleCount,
    }

    private sealed class BatteryFields
    {
        private readonly ScalarField _relative = new();
        private readonly ScalarField _design = new();
        private readonly ScalarField _full = new();
        private readonly ScalarField _cycles = new();

        internal ScalarField? Field(ElementContext context) => context switch
        {
            ElementContext.RelativeCapacity => _relative,
            ElementContext.DesignCapacity => _design,
            ElementContext.FullChargeCapacity => _full,
            ElementContext.CycleCount => _cycles,
            _ => null,
        };

        internal BatteryReportEntry CreateEntry(int ordinal)
        {
            var relative = _relative.ReadText();
            var design = _design.ReadNumber();
            var full = _full.ReadNumber();
            if (_relative.Malformed || relative is not null and not "0" and not "1" and not "false" and not "true")
            {
                design = SystemInventorySourceValue<uint>.InvalidData();
                full = SystemInventorySourceValue<uint>.InvalidData();
            }
            else if (relative is not "0" and not "false")
            {
                // Relative values have undefined energy units. Missing flags cannot
                // establish mWh either; retain independent cycle information only.
                if (design.Status != SystemInventorySourceStatus.InvalidData)
                {
                    design = SystemInventorySourceValue<uint>.Unavailable();
                }

                if (full.Status != SystemInventorySourceStatus.InvalidData)
                {
                    full = SystemInventorySourceValue<uint>.Unavailable();
                }
            }

            return new BatteryReportEntry(ordinal, design, full, _cycles.ReadNumber());
        }
    }

    private sealed class ScalarField
    {
        private readonly StringBuilder _text = new();

        internal bool Seen { get; private set; }

        internal bool Malformed { get; private set; }

        internal void Begin(bool validShape)
        {
            Malformed |= Seen || !validShape;
            Seen = true;
        }

        internal void Invalidate() => Malformed = true;

        internal void Append(string value)
        {
            if (Malformed || _text.Length + value.Length > 64)
            {
                Malformed = true;
                return;
            }

            _text.Append(value);
        }

        internal string? ReadText()
        {
            if (!Seen || Malformed)
            {
                return null;
            }

            var value = _text.ToString().Trim(' ', '\t', '\r', '\n');
            return value.Length == 0 ? null : value;
        }

        internal SystemInventorySourceValue<uint> ReadNumber()
        {
            if (Malformed)
            {
                return SystemInventorySourceValue<uint>.InvalidData();
            }

            var text = ReadText();
            if (text is null)
            {
                return SystemInventorySourceValue<uint>.Unavailable();
            }

            if (!text.All(char.IsAsciiDigit) || !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return SystemInventorySourceValue<uint>.InvalidData();
            }

            // Zero can mean an unsupported counter/capacity. The unsigned maximum
            // is the firmware unknown sentinel, not a measured battery property.
            return value is 0 or uint.MaxValue
                ? SystemInventorySourceValue<uint>.Unavailable()
                : SystemInventorySourceValue<uint>.Available(value);
        }
    }

    private sealed class BoundedReadStream(Stream inner, CancellationToken cancellationToken) : Stream
    {
        private long _readBytes;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = checked((int)(MaximumReportBytes - _readBytes));
            var read = inner.Read(buffer[..Math.Min(buffer.Length, remaining + 1)]);
            _readBytes += read;
            if (_readBytes > MaximumReportBytes)
            {
                throw new XmlException("The battery report exceeds its byte bound.");
            }

            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
