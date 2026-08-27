using System.Globalization;
using System.Text.Json.Serialization;

namespace ThirdLife.Core.Jobs;

[JsonConverter(typeof(JobIdJsonConverter))]
public sealed record JobId
{
    public JobId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static JobId New() =>
        new(string.Concat("job-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

[JsonConverter(typeof(DeviceIdJsonConverter))]
public sealed record DeviceId
{
    public DeviceId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static DeviceId New() =>
        new(string.Concat("device-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

[JsonConverter(typeof(ActionIdJsonConverter))]
public sealed record ActionId
{
    public ActionId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static ActionId New() =>
        new(string.Concat("action-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

internal sealed class JobIdJsonConverter : StringValueJsonConverter<JobId>
{
    protected override JobId Create(string value) => new(value);

    protected override string GetValue(JobId value) => value.Value;
}

internal sealed class DeviceIdJsonConverter : StringValueJsonConverter<DeviceId>
{
    protected override DeviceId Create(string value) => new(value);

    protected override string GetValue(DeviceId value) => value.Value;
}

internal sealed class ActionIdJsonConverter : StringValueJsonConverter<ActionId>
{
    protected override ActionId Create(string value) => new(value);

    protected override string GetValue(ActionId value) => value.Value;
}
