using System.Text.Json.Serialization;

namespace ThirdLife.Core.Jobs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Device
{
    [JsonConstructor]
    public Device(DeviceId deviceId)
    {
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
    }

    [JsonPropertyName("device_id")]
    public DeviceId DeviceId { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Job
{
    [JsonConstructor]
    public Job(JobId jobId, DeviceId deviceId, DateTimeOffset createdAtUtc)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        CreatedAtUtc = DomainValue.RequireTimestamp(createdAtUtc, nameof(createdAtUtc));
    }

    [JsonPropertyName("job_id")]
    public JobId JobId { get; }

    [JsonPropertyName("device_id")]
    public DeviceId DeviceId { get; }

    [JsonPropertyName("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; }

    public static Job Create(DeviceId deviceId, DateTimeOffset createdAtUtc) =>
        new(JobId.New(), deviceId, createdAtUtc);
}
