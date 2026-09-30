using System.Globalization;
using System.Text.Json.Serialization;

namespace ThirdLife.Core.Evidence;

[JsonConverter(typeof(EvidenceIdJsonConverter))]
public sealed record EvidenceId
{
    public EvidenceId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static EvidenceId New() =>
        new(string.Concat("evidence-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

[JsonConverter(typeof(ProviderIdJsonConverter))]
public sealed record ProviderId
{
    public ProviderId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

[JsonConverter(typeof(OperatorIdJsonConverter))]
public sealed record OperatorId
{
    public OperatorId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static OperatorId New() =>
        new(string.Concat("operator-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

[JsonConverter(typeof(HumanTestIdJsonConverter))]
public sealed record HumanTestId
{
    public HumanTestId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static HumanTestId New() =>
        new(string.Concat("human-test-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

[JsonConverter(typeof(RequirementIdJsonConverter))]
public sealed record RequirementId
{
    public RequirementId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

[JsonConverter(typeof(EvidenceKeyJsonConverter))]
public sealed record EvidenceKey
{
    public EvidenceKey(string value)
    {
        Value = DomainValue.RequireCode(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

internal sealed class EvidenceIdJsonConverter : StringValueJsonConverter<EvidenceId>
{
    protected override EvidenceId Create(string value) => new(value);

    protected override string GetValue(EvidenceId value) => value.Value;
}

internal sealed class ProviderIdJsonConverter : StringValueJsonConverter<ProviderId>
{
    protected override ProviderId Create(string value) => new(value);

    protected override string GetValue(ProviderId value) => value.Value;
}

internal sealed class OperatorIdJsonConverter : StringValueJsonConverter<OperatorId>
{
    protected override OperatorId Create(string value) => new(value);

    protected override string GetValue(OperatorId value) => value.Value;
}

internal sealed class HumanTestIdJsonConverter : StringValueJsonConverter<HumanTestId>
{
    protected override HumanTestId Create(string value) => new(value);

    protected override string GetValue(HumanTestId value) => value.Value;
}

internal sealed class RequirementIdJsonConverter : StringValueJsonConverter<RequirementId>
{
    protected override RequirementId Create(string value) => new(value);

    protected override string GetValue(RequirementId value) => value.Value;
}

internal sealed class EvidenceKeyJsonConverter : StringValueJsonConverter<EvidenceKey>
{
    protected override EvidenceKey Create(string value) => new(value);

    protected override string GetValue(EvidenceKey value) => value.Value;
}
