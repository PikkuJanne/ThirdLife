using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThirdLife.Core.Evidence;

[JsonConverter(typeof(StableStringEnumConverter<EvidenceValueKind>))]
public enum EvidenceValueKind
{
    [JsonStringEnumMemberName("boolean")]
    Boolean = 1,

    [JsonStringEnumMemberName("integer")]
    WholeNumber,

    [JsonStringEnumMemberName("decimal")]
    DecimalNumber,

    [JsonStringEnumMemberName("string")]
    Text,

    [JsonStringEnumMemberName("enum")]
    Enum,
}

[JsonConverter(typeof(EvidenceValueJsonConverter))]
public sealed record EvidenceValue
{
    private EvidenceValue(
        EvidenceValueKind kind,
        bool? booleanValue,
        long? integerValue,
        decimal? decimalValue,
        string? stringValue)
    {
        Kind = kind;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        DecimalValue = decimalValue;
        StringValue = stringValue;
    }

    public EvidenceValueKind Kind { get; }

    public bool? BooleanValue { get; }

    public long? IntegerValue { get; }

    public decimal? DecimalValue { get; }

    public string? StringValue { get; }

    public static EvidenceValue FromBoolean(bool value) =>
        new(EvidenceValueKind.Boolean, value, integerValue: null, decimalValue: null, stringValue: null);

    public static EvidenceValue FromInteger(long value) =>
        new(EvidenceValueKind.WholeNumber, booleanValue: null, value, decimalValue: null, stringValue: null);

    public static EvidenceValue FromDecimal(decimal value) =>
        new(EvidenceValueKind.DecimalNumber, booleanValue: null, integerValue: null, value, stringValue: null);

    public static EvidenceValue FromString(string value) =>
        new(
            EvidenceValueKind.Text,
            booleanValue: null,
            integerValue: null,
            decimalValue: null,
            DomainValue.RequireText(value, nameof(value)));

    public static EvidenceValue FromEnum(string value) =>
        new(
            EvidenceValueKind.Enum,
            booleanValue: null,
            integerValue: null,
            decimalValue: null,
            DomainValue.RequireCode(value, nameof(value)));

    public override string ToString() => "[evidence_value]";
}

internal sealed class EvidenceValueJsonConverter : JsonConverter<EvidenceValue>
{
    public override EvidenceValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An evidence value must be a JSON object.");
        }

        JsonElement typeProperty = default;
        JsonElement dataProperty = default;
        var hasType = false;
        var hasData = false;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type" when !hasType:
                    typeProperty = property.Value;
                    hasType = true;
                    break;
                case "data" when !hasData:
                    dataProperty = property.Value;
                    hasData = true;
                    break;
                default:
                    throw new JsonException($"Unknown or duplicate evidence-value property '{property.Name}'.");
            }
        }

        if (!hasType || typeProperty.ValueKind != JsonValueKind.String || !hasData)
        {
            throw new JsonException("Evidence values require exactly one string 'type' and one 'data' property.");
        }

        return typeProperty.GetString() switch
        {
            "boolean" when dataProperty.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                EvidenceValue.FromBoolean(dataProperty.GetBoolean()),
            "integer" when dataProperty.TryGetInt64(out var integerValue) =>
                EvidenceValue.FromInteger(integerValue),
            "decimal" when dataProperty.TryGetDecimal(out var decimalValue) =>
                EvidenceValue.FromDecimal(decimalValue),
            "string" when dataProperty.ValueKind == JsonValueKind.String =>
                CreateStringValue(dataProperty, isEnum: false),
            "enum" when dataProperty.ValueKind == JsonValueKind.String =>
                CreateStringValue(dataProperty, isEnum: true),
            _ => throw new JsonException("Evidence-value type and data do not form a supported bounded scalar."),
        };
    }

    public override void Write(Utf8JsonWriter writer, EvidenceValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("type", GetWireName(value.Kind));
        writer.WritePropertyName("data");

        switch (value.Kind)
        {
            case EvidenceValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue!.Value);
                break;
            case EvidenceValueKind.WholeNumber:
                writer.WriteNumberValue(value.IntegerValue!.Value);
                break;
            case EvidenceValueKind.DecimalNumber:
                writer.WriteNumberValue(value.DecimalValue!.Value);
                break;
            case EvidenceValueKind.Text:
            case EvidenceValueKind.Enum:
                writer.WriteStringValue(value.StringValue);
                break;
            default:
                throw new JsonException("The evidence-value kind is not defined.");
        }

        writer.WriteEndObject();
    }

    private static EvidenceValue CreateStringValue(JsonElement dataProperty, bool isEnum)
    {
        var value = dataProperty.GetString();
        if (value is null)
        {
            throw new JsonException("String evidence data must not be null.");
        }

        try
        {
            return isEnum ? EvidenceValue.FromEnum(value) : EvidenceValue.FromString(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("String evidence data is invalid.", exception);
        }
    }

    private static string GetWireName(EvidenceValueKind kind) => kind switch
    {
        EvidenceValueKind.Boolean => "boolean",
        EvidenceValueKind.WholeNumber => "integer",
        EvidenceValueKind.DecimalNumber => "decimal",
        EvidenceValueKind.Text => "string",
        EvidenceValueKind.Enum => "enum",
        _ => throw new JsonException("The evidence-value kind is not defined."),
    };
}
