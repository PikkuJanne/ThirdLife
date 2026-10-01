using System.Globalization;
using System.Text;
using System.Text.Json;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class BatteryReportParserTests
{
    [Fact]
    public void ReviewedReportExtractsOnlyBatteryNumbersAndKeepsInputOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Document(Battery())));

        var result = BatteryReportParser.Parse(stream);

        Assert.True(stream.CanRead);
        Assert.Equal(BatteryReportParseStatus.Collected, result.Status);
        var battery = Assert.Single(result.Batteries);
        Assert.Equal(0, battery.Ordinal);
        Assert.Equal(50_000U, battery.DesignCapacityMwh.GetRequiredValue());
        Assert.Equal(40_000U, battery.FullChargeCapacityMwh.GetRequiredValue());
        Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
    }

    [Theory]
    [InlineData("fi-FI")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void ParsingDoesNotDependOnCurrentCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var battery = Assert.Single(Parse(Document(Battery(" \t50000\r\n", "40000", "00123"))).Batteries);

            Assert.Equal(50_000U, battery.DesignCapacityMwh.GetRequiredValue());
            Assert.Equal(40_000U, battery.FullChargeCapacityMwh.GetRequiredValue());
            Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("50,000")]
    [InlineData("50.000")]
    [InlineData("5e4")]
    [InlineData("+50000")]
    [InlineData("-50000")]
    [InlineData("٥٠٠٠٠")]
    [InlineData("50000 mWh")]
    [InlineData("4294967296")]
    [InlineData("-1")]
    [InlineData("\u00a050000\u00a0")]
    public void NonInvariantOrOutOfRangeValuesInvalidateOnlyTheirOwnField(string value)
    {
        var result = Parse(Document(Battery(design: value)));

        Assert.Equal(BatteryReportParseStatus.Collected, result.Status);
        var battery = Assert.Single(result.Batteries);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.DesignCapacityMwh.Status);
        Assert.Equal(40_000U, battery.FullChargeCapacityMwh.GetRequiredValue());
        Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("0")]
    [InlineData("4294967295")]
    public void MissingAndUnknownSentinelsNeverBecomeCapacityOrCycleMeasurements(string value)
    {
        var battery = Assert.Single(Parse(Document(Battery(value, value, value))).Batteries);

        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.DesignCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.FullChargeCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.CycleCount.Status);
    }

    [Fact]
    public void ZeroCyclesRemainUnavailableBecauseUnderlyingCounterCanReportZeroWhenUnsupported()
    {
        var battery = Assert.Single(Parse(Document(Battery(cycles: "0"))).Batteries);

        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.CycleCount.Status);
        Assert.Equal(SystemInventorySourceStatus.Available, battery.DesignCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.Available, battery.FullChargeCapacityMwh.Status);
    }

    [Fact]
    public void FullCapacityAboveDesignIsPreservedWithoutClampingOrHealthInference()
    {
        var battery = Assert.Single(Parse(Document(Battery("40000", "42000", "7"))).Batteries);

        Assert.Equal(40_000U, battery.DesignCapacityMwh.GetRequiredValue());
        Assert.Equal(42_000U, battery.FullChargeCapacityMwh.GetRequiredValue());
        Assert.Equal(7U, battery.CycleCount.GetRequiredValue());
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("false", true)]
    [InlineData("1", false)]
    [InlineData("true", false)]
    [InlineData("", false)]
    public void RelativeCapacityFlagMustEstablishAbsoluteEnergyUnits(string relative, bool absolute)
    {
        var battery = Assert.Single(Parse(Document(Battery(relative: relative))).Batteries);

        var expected = absolute ? SystemInventorySourceStatus.Available : SystemInventorySourceStatus.Unavailable;
        Assert.Equal(expected, battery.DesignCapacityMwh.Status);
        Assert.Equal(expected, battery.FullChargeCapacityMwh.Status);
        Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
    }

    [Theory]
    [InlineData("2")]
    [InlineData("FALSE")]
    [InlineData("-1")]
    [InlineData("0.0")]
    public void UnrecognizedRelativeFlagMakesUnitsInvalid(string relative)
    {
        var battery = Assert.Single(Parse(Document(Battery(relative: relative))).Batteries);

        Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.DesignCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.FullChargeCapacityMwh.Status);
        Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
    }

    [Fact]
    public void MissingRelativeFlagDoesNotMislabelNumbersAsMilliwattHours()
    {
        var battery = Assert.Single(Parse(Document("<Battery><DesignCapacity>50000</DesignCapacity><FullChargeCapacity>40000</FullChargeCapacity><CycleCount>3</CycleCount></Battery>")).Batteries);

        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.DesignCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.FullChargeCapacityMwh.Status);
        Assert.Equal(3U, battery.CycleCount.GetRequiredValue());
    }

    [Fact]
    public void EmptyBatteryRecordIsPresentWithMissingFields()
    {
        var result = Parse(Document("<Battery/>"));

        Assert.Equal(BatteryReportParseStatus.Collected, result.Status);
        var battery = Assert.Single(result.Batteries);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.DesignCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.FullChargeCapacityMwh.Status);
        Assert.Equal(SystemInventorySourceStatus.Unavailable, battery.CycleCount.Status);
    }

    [Fact]
    public void OnlyAValidExplicitEmptyBatteriesContainerMeansNoBattery()
    {
        var result = Parse(Document(" \n<!-- no battery records -->"));

        Assert.Equal(BatteryReportParseStatus.NoBattery, result.Status);
        Assert.Empty(result.Batteries);
        Assert.Equal(BatteryReportParseStatus.InvalidData, Parse(Document("").Replace("<Batteries></Batteries>", "", StringComparison.Ordinal)).Status);
        Assert.Equal(BatteryReportParseStatus.InvalidData, Parse("<BatteryReport/>").Status);
        Assert.Equal(BatteryReportParseStatus.InvalidData, Parse("").Status);
    }

    [Fact]
    public void FourIndependentRecordsUseSnapshotOrdinalsWithoutInventingDeviceIdentity()
    {
        var rows = Battery("50000", "40000", "10") + Battery("50000", "40000", "10") +
            Battery("20000", "18000", "20") + Battery("10000", "9000", "30");
        var result = Parse(Document(rows));

        Assert.Equal(BatteryReportParseStatus.Collected, result.Status);
        Assert.Equal([0, 1, 2, 3], result.Batteries.Select(static entry => entry.Ordinal));
        Assert.Equal([10U, 10U, 20U, 30U], result.Batteries.Select(static entry => entry.CycleCount.GetRequiredValue()));
        Assert.Equal(BatteryReportParseStatus.InvalidData, Parse(Document(rows + Battery())).Status);
    }

    [Theory]
    [InlineData("DesignCapacity", "50000")]
    [InlineData("FullChargeCapacity", "40000")]
    [InlineData("CycleCount", "123")]
    [InlineData("RelativeCapacity", "0")]
    public void DuplicateScalarFieldsNeverChooseFirstOrLastValue(string name, string original)
    {
        foreach (var duplicate in new[] { original, "9" })
        {
            var xml = Document(Battery(extra: $"<{name}>{duplicate}</{name}>"));
            var battery = Assert.Single(Parse(xml).Batteries);
            var affected = name switch
            {
                "DesignCapacity" or "RelativeCapacity" => battery.DesignCapacityMwh,
                "FullChargeCapacity" => battery.FullChargeCapacityMwh,
                _ => battery.CycleCount,
            };

            Assert.Equal(SystemInventorySourceStatus.InvalidData, affected.Status);
            if (name == "RelativeCapacity")
            {
                Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.FullChargeCapacityMwh.Status);
            }
        }
    }

    [Fact]
    public void DuplicateStructuralContainersInvalidateWholeReport()
    {
        AssertInvalid(Document(Battery(), "<Batteries/>"));
        AssertInvalid(Document(Battery(), "<ReportInformation><ReportVersion>1.1</ReportVersion></ReportInformation>"));
        AssertInvalid(Document(Battery()).Replace("</ReportVersion>", "</ReportVersion><ReportVersion>1.1</ReportVersion>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "1.1")]
    [InlineData("urn:unreviewed:battery", "1.1")]
    [InlineData("http://schemas.microsoft.com/battery/2012", "1.0")]
    [InlineData("http://schemas.microsoft.com/battery/2012", "2.0")]
    [InlineData("http://schemas.microsoft.com/battery/2012", "")]
    public void UnknownNamespaceOrVersionFailsClosed(string reportNamespace, string version)
    {
        AssertInvalid(Document(Battery(), version: version, reportNamespace: reportNamespace));
    }

    [Fact]
    public void NamespacePrefixesAndUtf16EncodingAreSupportedByExpandedNames()
    {
        const string prefixed = "<b:BatteryReport xmlns:b=\"http://schemas.microsoft.com/battery/2012\"><b:ReportInformation><b:ReportVersion>1.1</b:ReportVersion></b:ReportInformation><b:Batteries><b:Battery><b:RelativeCapacity>false</b:RelativeCapacity><b:DesignCapacity>50000</b:DesignCapacity><b:FullChargeCapacity>45000</b:FullChargeCapacity><b:CycleCount>8</b:CycleCount></b:Battery></b:Batteries></b:BatteryReport>";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(prefixed)).ToArray();
        using var stream = new FragmentedStream(bytes, 3);

        var result = BatteryReportParser.Parse(stream);

        Assert.Equal(BatteryReportParseStatus.Collected, result.Status);
        Assert.Equal(45_000U, Assert.Single(result.Batteries).FullChargeCapacityMwh.GetRequiredValue());
    }

    [Fact]
    public void ForeignNamespaceOrUnexpectedBatteryContainerChildCannotMeanAbsence()
    {
        AssertInvalid(Document("<Battery xmlns=\"urn:foreign\"/>"));
        AssertInvalid(Document("<Unexpected/>"));
        AssertInvalid(Document("unexpected text"));
        AssertInvalid(Document("").Replace("<Batteries>", "<Batteries xmlns=\"urn:foreign\">", StringComparison.Ordinal));
    }

    [Fact]
    public void FieldNamespaceNestingAndUnitsAttributesCannotSmuggleAlternateValues()
    {
        var variants = new[]
        {
            Battery().Replace("<DesignCapacity>", "<DesignCapacity xmlns=\"urn:foreign\">", StringComparison.Ordinal),
            Battery(design: "<Nested>50000</Nested>"),
            Battery().Replace("<DesignCapacity>", "<DesignCapacity units=\"mAh\">", StringComparison.Ordinal),
        };
        foreach (var variant in variants)
        {
            var battery = Assert.Single(Parse(Document(variant)).Batteries);
            Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.DesignCapacityMwh.Status);
            Assert.Equal(40_000U, battery.FullChargeCapacityMwh.GetRequiredValue());
        }
    }

    [Fact]
    public void HistoryIdentifiersAndHostMetadataAreIgnoredWithoutRetention()
    {
        const string canary = "SYNTHETIC-PRIVATE-COMPUTER-NAME-SERIAL-PATH";
        var privateXml = Document(Battery(extra: $"<Id>{canary}</Id><Manufacturer>{canary}</Manufacturer><SerialNumber>{canary}</SerialNumber>"),
            $"<SystemInformation><ComputerName>{canary}</ComputerName></SystemInformation><History><Battery><RelativeCapacity>0</RelativeCapacity><DesignCapacity>1</DesignCapacity><FullChargeCapacity>9</FullChargeCapacity><CycleCount>99</CycleCount></Battery></History><RuntimeEstimates><DesignCapacity><Capacity>999</Capacity></DesignCapacity></RuntimeEstimates>");
        var parsed = Parse(privateXml);
        var sanitized = Fingerprint(parsed);

        Assert.Equal(Fingerprint(Parse(Document(Battery()))), sanitized);
        Assert.DoesNotContain(canary, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, parsed.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<!DOCTYPE BatteryReport [<!ENTITY secret SYSTEM 'file:///C:/SYNTHETIC-PRIVATE.txt'>]>")]
    [InlineData("<!DOCTYPE BatteryReport [<!ENTITY a '123'><!ENTITY b '&a;&a;&a;'>]>")]
    [InlineData("<!DOCTYPE BatteryReport SYSTEM 'https://example.invalid/battery.dtd'>")]
    public void DtdAndEntityInputsFailClosedBeforeAnyExternalResolution(string declaration)
    {
        AssertInvalid(declaration + Document(Battery(design: "&secret;")));
    }

    [Theory]
    [InlineData("<BatteryReport>")]
    [InlineData("<BatteryReport></Other>")]
    [InlineData("not XML")]
    [InlineData("<BatteryReport/>trailing text")]
    public void MalformedXmlReturnsOnlyTypedFailure(string xml)
    {
        AssertInvalid(xml);
    }

    [Fact]
    public void TruncatedReportDiscardsEarlierValidBatteryEntries()
    {
        var xml = Document(Battery());
        AssertInvalid(xml[..^10]);
        AssertInvalid(xml + "<OtherRoot/>");
    }

    [Fact]
    public void OversizedScalarInvalidatesOnlyThatField()
    {
        var battery = Assert.Single(Parse(Document(Battery(design: new string('1', 65)))).Batteries);

        Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.DesignCapacityMwh.Status);
        Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
    }

    [Fact]
    public void ByteBoundAcceptsExactLimitAndRejectsOneMoreWithoutReadingUnboundedInput()
    {
        var template = Document(Battery(), "<History></History>");
        var padding = BatteryReportParser.MaximumReportBytes - Encoding.UTF8.GetByteCount(template);
        var exact = template.Replace("<History>", "<History>" + new string('x', padding), StringComparison.Ordinal);
        using var exactStream = new FragmentedStream(Encoding.UTF8.GetBytes(exact));
        Assert.Equal(BatteryReportParseStatus.Collected, BatteryReportParser.Parse(exactStream).Status);
        using var excessiveStream = new FragmentedStream(Encoding.UTF8.GetBytes(exact + " "));

        var excessive = BatteryReportParser.Parse(excessiveStream);

        Assert.Equal(BatteryReportParseStatus.InvalidData, excessive.Status);
        Assert.Empty(excessive.Batteries);
        Assert.InRange(excessiveStream.BytesRead, 0, BatteryReportParser.MaximumReportBytes + 1);
    }

    [Fact]
    public void DepthNodeAndAttributeBoundsApplyAlsoToIgnoredPrivateSections()
    {
        var deep = string.Concat(Enumerable.Repeat("<Nested>", BatteryReportParser.MaximumDepth + 1)) +
            string.Concat(Enumerable.Repeat("</Nested>", BatteryReportParser.MaximumDepth + 1));
        AssertInvalid(Document(Battery(), "<History>" + deep + "</History>"));
        var manyNodes = string.Concat(Enumerable.Repeat("<Entry/>", BatteryReportParser.MaximumNodes));
        AssertInvalid(Document(Battery(), "<History>" + manyNodes + "</History>"));
        var attributes = string.Join(' ', Enumerable.Range(0, BatteryReportParser.MaximumAttributes + 1)
            .Select(static index => FormattableString.Invariant($"a{index}=\"x\"")));
        AssertInvalid(Document(Battery(), "<History " + attributes + "/>"));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(32)]
    [InlineData(64)]
    public void BoundedWideHistoryElementsDoNotRejectOrRetainPrivateData(int attributeCount)
    {
        const string canary = "SYNTHETIC-PRIVATE-HISTORY";
        var attributes = string.Join(' ', Enumerable.Range(0, attributeCount)
            .Select(index => FormattableString.Invariant($"a{index}=\"{canary}\"")));
        var result = Parse(Document(Battery(), "<History><Entry " + attributes + "/></History>"));

        Assert.Equal(BatteryReportParseStatus.Collected, result.Status);
        Assert.Equal(Fingerprint(Parse(Document(Battery()))), Fingerprint(result));
        Assert.DoesNotContain(canary, Fingerprint(result), StringComparison.Ordinal);
    }

    [Fact]
    public void SixtyFiveHistoryAttributesFailClosedWithoutPartialBatteryFacts()
    {
        var attributes = string.Join(' ', Enumerable.Range(0, 65)
            .Select(static index => FormattableString.Invariant($"a{index}=\"x\"")));

        AssertInvalid(Document(Battery(), "<History><Entry " + attributes + "/></History>"));
    }

    [Fact]
    public void WiderHistoryBoundDoesNotAllowAttributesOnRecognizedScalarFields()
    {
        var attributes = string.Join(' ', Enumerable.Range(0, 64)
            .Select(static index => FormattableString.Invariant($"a{index}=\"x\"")));
        var xml = Document(Battery()).Replace("<DesignCapacity>", "<DesignCapacity " + attributes + ">", StringComparison.Ordinal);
        var battery = Assert.Single(Parse(xml).Batteries);

        Assert.Equal(SystemInventorySourceStatus.InvalidData, battery.DesignCapacityMwh.Status);
        Assert.Equal(40_000U, battery.FullChargeCapacityMwh.GetRequiredValue());
        Assert.Equal(123U, battery.CycleCount.GetRequiredValue());
    }

    [Fact]
    public void CancellationBeforeAndDuringReadPropagatesWithoutPublishingPartialFacts()
    {
        using var before = new CancellationTokenSource();
        before.Cancel();
        using var untouched = new FragmentedStream(Encoding.UTF8.GetBytes(Document(Battery())));
        Assert.Throws<OperationCanceledException>(() => BatteryReportParser.Parse(untouched, before.Token));
        Assert.Equal(0, untouched.BytesRead);

        using var during = new CancellationTokenSource();
        using var interrupted = new FragmentedStream(Encoding.UTF8.GetBytes(Document(Battery())), 3, () => during.Cancel());
        Assert.Throws<OperationCanceledException>(() => BatteryReportParser.Parse(interrupted, during.Token));
    }

    [Fact]
    public void ResultOwnsBoundedCopiedRowsAndRejectsInvalidStateCombinations()
    {
        var entry = Assert.Single(Parse(Document(Battery())).Batteries);
        var original = new[] { entry };
        var result = new BatteryReportParseResult(BatteryReportParseStatus.Collected, original);
        original[0] = entry with { Ordinal = 3 };

        Assert.Equal(0, Assert.Single(result.Batteries).Ordinal);
        Assert.Throws<ArgumentException>(() => new BatteryReportParseResult(BatteryReportParseStatus.NoBattery, [entry]));
        Assert.Throws<ArgumentException>(() => new BatteryReportParseResult(BatteryReportParseStatus.Collected, []));
        Assert.Throws<ArgumentException>(() => new BatteryReportParseResult(BatteryReportParseStatus.InvalidData, [entry]));
        Assert.Throws<ArgumentException>(() => new BatteryReportParseResult((BatteryReportParseStatus)999, []));
        Assert.Throws<ArgumentException>(() => new BatteryReportParseResult(BatteryReportParseStatus.Collected, Enumerable.Repeat(entry, 5)));
    }

    private static string Fingerprint(BatteryReportParseResult result) => JsonSerializer.Serialize(new
    {
        result.Status,
        Batteries = result.Batteries.Select(static entry => new { entry.Ordinal, entry.DesignCapacityMwh, entry.FullChargeCapacityMwh, entry.CycleCount }),
    });

    private static void AssertInvalid(string xml)
    {
        var result = Parse(xml);
        Assert.Equal(BatteryReportParseStatus.InvalidData, result.Status);
        Assert.Empty(result.Batteries);
    }

    private static BatteryReportParseResult Parse(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return BatteryReportParser.Parse(stream);
    }

    private static string Document(string batteries, string extra = "", string version = "1.1", string reportNamespace = BatteryReportParser.ReportNamespace) =>
        $"<BatteryReport xmlns=\"{reportNamespace}\"><ReportInformation><ReportVersion>{version}</ReportVersion></ReportInformation><Batteries>{batteries}</Batteries>{extra}</BatteryReport>";

    private static string Battery(string design = "50000", string full = "40000", string cycles = "123", string relative = "0", string extra = "") =>
        $"<Battery><RelativeCapacity>{relative}</RelativeCapacity><DesignCapacity>{design}</DesignCapacity><FullChargeCapacity>{full}</FullChargeCapacity><CycleCount>{cycles}</CycleCount>{extra}</Battery>";

    private sealed class FragmentedStream(byte[] data, int chunkSize = int.MaxValue, Action? beforeRead = null) : Stream
    {
        internal int BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            beforeRead?.Invoke();
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), data.Length - BytesRead);
            data.AsSpan(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return count;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
