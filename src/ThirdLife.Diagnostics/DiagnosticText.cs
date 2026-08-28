using System.Globalization;
using System.Text;

namespace ThirdLife.Diagnostics;

internal static class DiagnosticText
{
    private static readonly char[] UnsafePrefixCharacters = ['=', '+', '-', '@'];

    public static string RequireCode(string? value, string parameterName, int maximumLength = 96)
    {
        var normalized = RequireAsciiText(value, parameterName, maximumLength);
        RequireLeadingAsciiLetterOrDigit(normalized, parameterName);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':')
            {
                throw new ArgumentException(
                    "Diagnostic codes may contain only ASCII letters, digits, hyphens, underscores, periods, and colons.",
                    parameterName);
            }
        }

        return normalized;
    }

    public static string RequireVersion(string? value, string parameterName, int maximumLength = 96)
    {
        var normalized = RequireAsciiText(value, parameterName, maximumLength);
        RequireLeadingAsciiLetterOrDigit(normalized, parameterName);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':' and not '@' and not '+')
            {
                throw new ArgumentException(
                    "Diagnostic versions contain an unsupported character.",
                    parameterName);
            }
        }

        return normalized;
    }

    public static string RequireOpaqueIdentifier(string? value, string parameterName, int maximumLength = 64)
    {
        var normalized = RequireAsciiText(value, parameterName, maximumLength);
        RequireLeadingAsciiLetterOrDigit(normalized, parameterName);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                throw new ArgumentException(
                    "Opaque diagnostic identifiers may contain only ASCII letters, digits, hyphens, and underscores.",
                    parameterName);
            }
        }

        return normalized;
    }

    public static string RequireDigest(string? value, string parameterName)
    {
        var normalized = RequireAsciiText(value, parameterName, 64);
        if (normalized.Length != 64 || normalized.Any(static character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("A lowercase 64-character SHA-256 digest is required.", parameterName);
        }

        if (!string.Equals(normalized, normalized.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A lowercase 64-character SHA-256 digest is required.", parameterName);
        }

        return normalized;
    }

    public static string RequireResourceBucket(string? value, string parameterName, int maximumLength = 32)
    {
        var normalized = RequireAsciiText(value, parameterName, maximumLength);
        RequireLeadingAsciiLetterOrDigit(normalized, parameterName);

        foreach (var character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ' ')
            {
                throw new ArgumentException("Resource buckets contain an unsupported character.", parameterName);
            }
        }

        return normalized;
    }

    public static string RequireRedactionMarker(string? value, string parameterName)
    {
        var normalized = RequireAsciiText(value, parameterName, 96);
        if (normalized[0] != '[' || normalized[^1] != ']' || normalized.Count(static character => character == ':') != 1)
        {
            throw new ArgumentException("A fixed diagnostic redaction marker is required.", parameterName);
        }

        foreach (var character in normalized.AsSpan(1, normalized.Length - 2))
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not ':')
            {
                throw new ArgumentException("The redaction marker contains an unsupported character.", parameterName);
            }
        }

        return normalized;
    }

    public static string RequireAsciiText(string? value, string parameterName, int maximumLength)
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

        if (value.Any(static character => !char.IsAscii(character) || char.IsControl(character)))
        {
            throw new ArgumentException("The value must contain printable ASCII characters only.", parameterName);
        }

        return value;
    }

    public static string? CanonicalizeFieldName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return null;
        }

        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        }
        catch (ArgumentException)
        {
            return null;
        }

        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;

        foreach (var character in normalized)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (character is '_' or '-' or '.' or ' ')
            {
                if (builder.Length > 0 && !previousWasSeparator)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }

                continue;
            }

            return null;
        }

        if (builder.Length > 0 && builder[^1] == '_')
        {
            builder.Length--;
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static void RequireLeadingAsciiLetterOrDigit(string value, string parameterName)
    {
        if (!char.IsAsciiLetterOrDigit(value[0]) || UnsafePrefixCharacters.Contains(value[0]))
        {
            throw new ArgumentException(
                "The value must begin with an ASCII letter or digit and must not begin with a formula marker.",
                parameterName);
        }
    }
}
