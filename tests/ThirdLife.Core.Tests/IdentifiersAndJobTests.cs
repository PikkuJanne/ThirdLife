using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Tests;

public sealed class IdentifiersAndJobTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("recipient name")]
    [InlineData("job/escape")]
    [InlineData("job\\escape")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("C:")]
    [InlineData("CON")]
    [InlineData("com1")]
    public void JobIdentifierRejectsInvalidValues(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new JobId(value!));
    }

    [Fact]
    public void EvidenceKeysRemainCodesRatherThanPathIdentifiers()
    {
        var key = new EvidenceKey("battery.present");

        Assert.Equal("battery.present", key.Value);
        Assert.Throws<ArgumentException>(() => new JobId("battery.present"));
    }

    [Fact]
    public void GeneratedIdentifiersAreOpaqueUniqueValues()
    {
        var firstJob = JobId.New();
        var secondJob = JobId.New();
        var device = DeviceId.New();
        var action = ActionId.New();
        var evidence = EvidenceId.New();

        Assert.StartsWith("job-", firstJob.Value, StringComparison.Ordinal);
        Assert.NotEqual(firstJob, secondJob);
        Assert.StartsWith("device-", device.Value, StringComparison.Ordinal);
        Assert.StartsWith("action-", action.Value, StringComparison.Ordinal);
        Assert.StartsWith("evidence-", evidence.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void StrongIdentifierSerializesAsStableScalarString()
    {
        var identifier = new JobId("SYNTHETIC-JOB-001");

        var json = JsonSerializer.Serialize(identifier);
        var roundTrip = JsonSerializer.Deserialize<JobId>(json);

        Assert.Equal("\"SYNTHETIC-JOB-001\"", json);
        Assert.Equal(identifier, roundTrip);
        Assert.Null(JsonSerializer.Deserialize<JobId>("null"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JobId>("{\"value\":\"job-1\"}"));
    }

    [Fact]
    public void JobRoundTripUsesExplicitNamesAndUtcTimestamp()
    {
        var job = new Job(
            new JobId("job-001"),
            new DeviceId("device-001"),
            new DateTimeOffset(2030, 1, 1, 10, 30, 0, TimeSpan.FromHours(2)));

        var json = JsonSerializer.Serialize(job);
        var roundTrip = JsonSerializer.Deserialize<Job>(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("job-001", root.GetProperty("job_id").GetString());
        Assert.Equal("device-001", root.GetProperty("device_id").GetString());
        Assert.Equal("2030-01-01T08:30:00+00:00", root.GetProperty("created_at_utc").GetString());
        Assert.False(root.TryGetProperty("JobId", out _));
        Assert.Equal(job, roundTrip);
    }

    [Fact]
    public void JobRejectsMissingIdentifiersAndDefaultTimestamp()
    {
        var jobId = new JobId("job-001");
        var deviceId = new DeviceId("device-001");
        var timestamp = DateTimeOffset.Parse("2030-01-01T00:00:00Z", provider: null);

        Assert.Throws<ArgumentNullException>(() => new Job(null!, deviceId, timestamp));
        Assert.Throws<ArgumentNullException>(() => new Job(jobId, null!, timestamp));
        Assert.Throws<ArgumentException>(() => new Job(jobId, deviceId, default));
    }

    [Fact]
    public void IdentifierValidationRejectsMalformedUnicodeWithoutLossyRoundTrip()
    {
        const string malformed = "\uD800";

        Assert.Throws<ArgumentException>(() => new JobId(malformed));
        Assert.Throws<ArgumentException>(() => EvidenceValue.FromString(malformed));
    }
}
