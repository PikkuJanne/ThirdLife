using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThirdLife.Core;

internal static class DomainValue
{
    private const int MaximumIdentifierLength = 128;
    private const int MaximumCodeLength = 128;
    private const int MaximumTextLength = 4096;

    public static string RequireIdentifier(string? value, string parameterName)
    {
        var normalized = RequireText(value, parameterName, MaximumIdentifierLength);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                throw new ArgumentException(
                    "Opaque identifiers may contain only ASCII letters, digits, hyphens, and underscores.",
                    parameterName);
            }
        }

        if (IsReservedWindowsDeviceName(normalized))
        {
            throw new ArgumentException("Opaque identifiers must not use a reserved Windows device name.", parameterName);
        }

        return normalized;
    }

    public static string RequireCode(string? value, string parameterName)
    {
        var normalized = RequireText(value, parameterName, MaximumCodeLength);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':')
            {
                throw new ArgumentException(
                    "Codes may contain only ASCII letters, digits, hyphens, underscores, periods, and colons.",
                    parameterName);
            }
        }

        return normalized;
    }

    public static string RequireVersion(string? value, string parameterName)
    {
        var normalized = RequireText(value, parameterName, MaximumCodeLength);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':' and not '@' and not '+')
            {
                throw new ArgumentException(
                    "Versions may contain only ASCII letters, digits, hyphens, underscores, periods, colons, at signs, and plus signs.",
                    parameterName);
            }
        }

        return normalized;
    }

    public static string RequireText(string? value, string parameterName, int maximumLength = MaximumTextLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("The value must not contain leading or trailing whitespace.", parameterName);
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The value must be no longer than {maximumLength} characters."));
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsControl(character))
            {
                throw new ArgumentException("The value must not contain control characters.", parameterName);
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw new ArgumentException("The value must contain well-formed Unicode text.", parameterName);
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                throw new ArgumentException("The value must contain well-formed Unicode text.", parameterName);
            }
        }

        return value;
    }

    public static string? OptionalCode(string? value, string parameterName) =>
        value is null ? null : RequireCode(value, parameterName);

    public static DateTimeOffset RequireTimestamp(DateTimeOffset value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException("A non-default timestamp is required.", parameterName);
        }

        return value.ToUniversalTime();
    }

    public static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The enum value is not defined.");
        }

        return value;
    }

    private static bool IsReservedWindowsDeviceName(string value) =>
        value.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
        IsNumberedWindowsDeviceName(value, "COM") ||
        IsNumberedWindowsDeviceName(value, "LPT");

    private static bool IsNumberedWindowsDeviceName(string value, string prefix) =>
        value.Length == 4 &&
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        value[3] is >= '1' and <= '9';
}

internal abstract class StringValueJsonConverter<TValue> : JsonConverter<TValue>
    where TValue : class
{
    public sealed override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{typeToConvert.Name} must be encoded as a JSON string.");
        }

        var value = reader.GetString()!;

        try
        {
            return Create(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException($"Invalid {typeToConvert.Name} value.", exception);
        }
    }

    public sealed override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(GetValue(value));
    }

    protected abstract TValue Create(string value);

    protected abstract string GetValue(TValue value);
}

internal sealed class StableStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ValuesByWireName;
    private static readonly Dictionary<TEnum, string> WireNamesByValue;

    static StableStringEnumConverter()
    {
        var valuesByWireName = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        var wireNamesByValue = new Dictionary<TEnum, string>();

        foreach (var value in Enum.GetValues<TEnum>())
        {
            var memberName = Enum.GetName(value)
                ?? throw new InvalidOperationException($"{typeof(TEnum).Name} contains an unnamed value.");
            var member = typeof(TEnum).GetField(memberName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"{typeof(TEnum).Name}.{memberName} is unavailable.");
            var attribute = member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()
                ?? throw new InvalidOperationException(
                    $"{typeof(TEnum).Name}.{memberName} must declare an explicit JSON wire name.");

            if (!valuesByWireName.TryAdd(attribute.Name, value) ||
                !wireNamesByValue.TryAdd(value, attribute.Name))
            {
                throw new InvalidOperationException($"{typeof(TEnum).Name} contains a duplicate JSON wire name or value.");
            }
        }

        ValuesByWireName = valuesByWireName;
        WireNamesByValue = wireNamesByValue;
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{typeToConvert.Name} must be encoded as a JSON string.");
        }

        var wireName = reader.GetString();
        if (wireName is null || !ValuesByWireName.TryGetValue(wireName, out var value))
        {
            throw new JsonException($"The JSON value is not a defined {typeToConvert.Name} wire name.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!WireNamesByValue.TryGetValue(value, out var wireName))
        {
            throw new JsonException($"The {typeof(TEnum).Name} value is not defined.");
        }

        writer.WriteStringValue(wireName);
    }
}
