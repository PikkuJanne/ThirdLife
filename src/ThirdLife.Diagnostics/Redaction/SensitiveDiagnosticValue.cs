using System.Diagnostics;

namespace ThirdLife.Diagnostics.Redaction;

[DebuggerDisplay("[SENSITIVE:NOT-FOR-DIAGNOSTICS]")]
public sealed class SensitiveDiagnosticValue
{
    internal const int MaximumTransientCodeUnits = 64 * 1024;
    private readonly string _value;

    private SensitiveDiagnosticValue(RedactionField field, string value)
    {
        if (!IsCollectableWrapperField(field))
        {
            throw new ArgumentException(
                "The field is excluded from the public sensitive-value wrapper.",
                nameof(field));
        }

        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > MaximumTransientCodeUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"A sensitive transient value must not exceed {MaximumTransientCodeUnits} UTF-16 code units.");
        }

        Field = field;
        _value = value;
    }

    public RedactionField Field { get; }

    public static SensitiveDiagnosticValue Create(RedactionField field, string value) => new(field, value);

    public SafeDiagnosticMarker? ToSafeOrdinaryLogRepresentation()
    {
        var result = RedactionEngine.Transform(Field, DiagnosticContext.OrdinaryLog, _value);
        return result.Persistence == PersistenceDisposition.RedactedValueOnly &&
            result.RedactedForm is { Kind: RedactedScalarKind.String } redacted
            ? new SafeDiagnosticMarker(redacted.GetString())
            : null;
    }

    public override string ToString() => "[SENSITIVE:NOT-FOR-DIAGNOSTICS]";

    private static bool IsCollectableWrapperField(RedactionField field) => field is
        RedactionField.PersonName or
        RedactionField.Username or
        RedactionField.EmailAddress or
        RedactionField.WifiSsid or
        RedactionField.Ipv4Address or
        RedactionField.Ipv6Address or
        RedactionField.MacAddress or
        RedactionField.FullSerialNumber or
        RedactionField.FilePath or
        RedactionField.NetworkPath or
        RedactionField.PackageDownloadUrl or
        RedactionField.DeviceName or
        RedactionField.WindowsSid or
        RedactionField.AssetTag;
}

public sealed class SafeDiagnosticMarker
{
    internal SafeDiagnosticMarker(string value)
    {
        Value = DiagnosticText.RequireRedactionMarker(value, nameof(value));
    }

    internal string Value { get; }

    public override string ToString() => Value;
}
