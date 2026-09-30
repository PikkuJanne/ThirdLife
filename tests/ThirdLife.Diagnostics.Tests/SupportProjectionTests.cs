using System.Globalization;
using System.Text;
using System.Text.Json;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Tests;

public sealed class SupportProjectionTests
{
    private static readonly RedactionFixtureSet Fixtures = RedactionFixtureSet.Load();

    [Fact]
    public void ExactTwentyFiveFieldProjectionIsDeterministicAndScalarOnly()
    {
        var fields = CreateAllFixtureFields();
        var first = SupportProjection.Create(fields);
        var second = SupportProjection.Create(fields.Reverse());

        Assert.Equal(first.GetUtf8Json(), second.GetUtf8Json());
        Assert.Equal(first.ContentDigestSha256, second.ContentDigestSha256);
        using var document = JsonDocument.Parse(first.GetUtf8Json());
        var properties = document.RootElement.EnumerateObject().ToArray();
        Assert.Equal(25, properties.Length);
        Assert.Equal(Fixtures.SupportAllowlist, properties.Select(static property => property.Name));
        var expectedValues = Fixtures.Cases
            .Where(static fixture => fixture.Action == RedactionAction.PreserveAllowlisted)
            .ToDictionary(static fixture => fixture.InputField, static fixture => fixture.ExportedForm);
        foreach (var property in properties)
        {
            object? actual = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetInt64(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidOperationException("The support projection contains a non-scalar value."),
            };
            Assert.Equal(expectedValues[property.Name], actual);
        }

        Assert.All(
            properties,
            static property => Assert.Contains(
                property.Value.ValueKind,
                new[] { JsonValueKind.String, JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False }));
        Assert.False(document.RootElement.TryGetProperty("full_serial_number", out _));
        Assert.False(document.RootElement.TryGetProperty("username", out _));
        Assert.False(document.RootElement.TryGetProperty("raw_command_output", out _));
    }

    [Fact]
    public void RepresentativeProjectionContainsNoProhibitedFixtureSeed()
    {
        var projection = SupportProjection.Create(CreateAllFixtureFields());
        var json = Encoding.UTF8.GetString(projection.GetUtf8Json());
        var prohibited = Fixtures.Cases.Where(
            static fixture => fixture.Action is not RedactionAction.PreserveAllowlisted and
                not RedactionAction.SuppressTelemetry);

        foreach (var fixture in prohibited)
        {
            Assert.DoesNotContain(fixture.InputValue.ToString()!, json, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.InputField, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProjectionRejectsDuplicatesWrongTypesAndUnboundedEnumeration()
    {
        var duplicate = new SupportField(
            SupportFieldName.SchemaVersion,
            SupportValue.Version("thirdlife.support.synthetic.v1"));
        Assert.Equal(
            "support_field_duplicate",
            Assert.Throws<DiagnosticContractException>(() =>
                SupportProjection.Create([duplicate, duplicate])).ResultCode);

        Assert.Equal(
            "support_field_type_invalid",
            Assert.Throws<DiagnosticContractException>(() =>
                new SupportField(SupportFieldName.Retryable, SupportValue.Code("false"))).ResultCode);

        Assert.Equal(
            "support_field_value_unregistered",
            Assert.Throws<DiagnosticContractException>(() =>
                new SupportField(SupportFieldName.ApplicationVersion, SupportValue.Version("192.0.2.44"))).ResultCode);

        var enumerated = 0;
        IEnumerable<SupportField> Unbounded()
        {
            while (true)
            {
                enumerated++;
                yield return duplicate;
            }
        }

        Assert.Equal(
            "support_field_count_exceeded",
            Assert.Throws<DiagnosticContractException>(() => SupportProjection.Create(Unbounded())).ResultCode);
        Assert.Equal(26, enumerated);
    }

    [Fact]
    public void ActualProjectionPathRejectsEveryProhibitedFixtureSeedAndPlausibleOverlap()
    {
        var prohibitedSeeds = Fixtures.Cases
            .Where(static fixture => fixture.Action is not RedactionAction.PreserveAllowlisted and
                not RedactionAction.SuppressTelemetry)
            .Select(static fixture => fixture.InputValue.ToString()!)
            .Concat(
            [
                "SUP-SYNTHETIC-SERIAL-000000",
                "0.0.0-SyntheticPerson",
                "synthetic-build-SyntheticPerson",
                "10.0.192.0.2.44",
                new string('a', 64),
            ])
            .ToArray();

        foreach (var field in SupportFieldCatalog.AllFields)
        {
            foreach (var seed in prohibitedSeeds)
            {
                var exception = Record.Exception(() =>
                {
                    var supportField = new SupportField(field, CreateCandidate(field, seed));
                    _ = SupportProjection.Create([supportField]);
                });
                Assert.True(
                    exception is ArgumentException or DiagnosticContractException,
                    $"Field {field} accepted a prohibited relabeled value: {seed}");
            }
        }
    }

    [Fact]
    public async Task DecodedPersistedScalarsContainNoProhibitedFixtureValue()
    {
        var timestamp = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var temporary = new TemporaryDirectory();
        await using var store = SanitizedLogStore.OpenForTesting(
            Path.Combine(temporary.Path, "logs"),
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(timestamp));
        await store.AppendAsync(DiagnosticEventFactory.Completed(timestamp));
        var persistedEvent = Assert.Single(await store.ReadOwnedRecordsForTestingAsync());
        var projection = SupportProjection.Create(CreateAllFixtureFields()).GetUtf8Json();
        var prohibited = Fixtures.Cases
            .Where(static fixture => fixture.Action is not RedactionAction.PreserveAllowlisted and
                not RedactionAction.SuppressTelemetry &&
                fixture.InputValue is string)
            .Select(static fixture => (Value: (string)fixture.InputValue, fixture.InputField))
            .ToArray();

        foreach (var bytes in new[] { persistedEvent, projection })
        {
            using var document = JsonDocument.Parse(bytes);
            var properties = document.RootElement.EnumerateObject().ToArray();
            var decodedStrings = properties
                .Where(static property => property.Value.ValueKind == JsonValueKind.String)
                .Select(static property => property.Value.GetString() ?? string.Empty)
                .ToArray();
            foreach (var candidate in prohibited)
            {
                Assert.DoesNotContain(
                    decodedStrings,
                    value => value.Contains(candidate.Value, StringComparison.Ordinal));
                Assert.DoesNotContain(
                    properties,
                    property => string.Equals(property.Name, candidate.InputField, StringComparison.Ordinal));
            }
        }
    }

    private static SupportField[] CreateAllFixtureFields()
    {
        return Fixtures.Cases
            .Where(static fixture => fixture.Action == RedactionAction.PreserveAllowlisted)
            .Select(CreateField)
            .ToArray();
    }

    private static SupportField CreateField(RedactionFixtureCase fixture)
    {
        var redactionField = RedactionFieldCatalog.Parse(fixture.InputField);
        Assert.True(SupportFieldCatalog.TryMap(redactionField, out var supportField));
        var value = supportField switch
        {
            SupportFieldName.SchemaVersion or
            SupportFieldName.ManifestVersion or
            SupportFieldName.ApplicationVersion or
            SupportFieldName.BuildVersion or
            SupportFieldName.OsVersion => SupportValue.Version((string)fixture.InputValue),
            SupportFieldName.InternalSupportId => SupportValue.OpaqueIdentifier((string)fixture.InputValue),
            SupportFieldName.EventTimeUtc or
            SupportFieldName.ExportCreatedAtUtc => SupportValue.Timestamp(
                DateTimeOffset.Parse(
                    (string)fixture.InputValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)),
            SupportFieldName.Retryable => SupportValue.Boolean((bool)fixture.InputValue),
            SupportFieldName.DurationMs or
            SupportFieldName.BoundedCount => SupportValue.NonNegativeInteger((long)fixture.InputValue),
            SupportFieldName.MemoryBucket => SupportValue.ResourceBucket((string)fixture.InputValue),
            SupportFieldName.PreviewContentDigestSha256 or
            SupportFieldName.ExportContentDigestSha256 => SupportValue.Sha256Digest((string)fixture.InputValue),
            _ => SupportValue.Code((string)fixture.InputValue),
        };

        return new SupportField(supportField, value);
    }

    private static SupportValue CreateCandidate(SupportFieldName field, string seed) => field switch
    {
        SupportFieldName.SchemaVersion or
        SupportFieldName.ManifestVersion or
        SupportFieldName.ApplicationVersion or
        SupportFieldName.BuildVersion or
        SupportFieldName.OsVersion => SupportValue.Version(seed),
        SupportFieldName.InternalSupportId => SupportValue.OpaqueIdentifier(seed),
        SupportFieldName.MemoryBucket => SupportValue.ResourceBucket(seed),
        SupportFieldName.PreviewContentDigestSha256 or
        SupportFieldName.ExportContentDigestSha256 when seed.Length == 64 => SupportValue.Sha256Digest(seed),
        _ => SupportValue.Code(seed),
    };
}
