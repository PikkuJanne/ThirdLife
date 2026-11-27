using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThirdLife.Catalog.Schema;

internal static partial class DeclarativeJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static JsonDocument Open(ReadOnlyMemory<byte> utf8Json, int maximumBytes, out string contentSha256,
        out ImmutableArray<byte> bytes)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > maximumBytes)
        {
            throw new JsonException();
        }

        // Validate and hash the same bounded byte snapshot.
        bytes = ImmutableArray.Create(utf8Json.Span);
        _ = StrictUtf8.GetCharCount(bytes.AsSpan());
        contentSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan()));
        return JsonDocument.Parse(bytes.AsMemory(), new JsonDocumentOptions
        {
            AllowDuplicateProperties = false,
            MaxDepth = 8,
        });
    }

    public static void RequireFields(JsonElement value, ReadOnlySpan<string> expected)
    {
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name))
            {
                throw new JsonException();
            }

            count++;
        }

        if (count != expected.Length)
        {
            throw new JsonException();
        }
    }

    public static void RequireValue(JsonElement element, string name, string expected)
    {
        if (!element.GetProperty(name).ValueEquals(expected))
        {
            throw new JsonException();
        }
    }

    public static string ReadToken(JsonElement element, string property, Regex pattern, int maximumLength)
    {
        var value = element.GetProperty(property).GetString();
        return value is not null && value.Length <= maximumLength && pattern.IsMatch(value) ? value : throw new JsonException();
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    public static partial Regex LowerKebabPattern();

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?\z", RegexOptions.NonBacktracking)]
    public static partial Regex SemanticVersionPattern();
}
