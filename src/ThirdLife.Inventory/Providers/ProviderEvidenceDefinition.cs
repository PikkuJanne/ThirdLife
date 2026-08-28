using ThirdLife.Core.Evidence;

namespace ThirdLife.Inventory.Providers;

public sealed class ProviderEvidenceDefinition
{
    public const int MaximumAllowedCardinality = 32;

    public ProviderEvidenceDefinition(
        EvidenceKey evidenceKey,
        EvidenceValueKind valueKind,
        string? unit,
        string sourceReference,
        int maximumCardinality = 1)
    {
        EvidenceKey = evidenceKey ?? throw new ArgumentNullException(nameof(evidenceKey));
        if (!Enum.IsDefined(valueKind))
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind), valueKind, "The value kind is not defined.");
        }

        ValueKind = valueKind;
        Unit = RequireOptionalCode(unit, nameof(unit));
        SourceReference = new EvidenceProvenance(
            ProvenanceKind.ProviderObservation,
            sourceReference).SourceReference;
        if (maximumCardinality is < 1 or > MaximumAllowedCardinality)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCardinality),
                maximumCardinality,
                $"Evidence cardinality must be between 1 and {MaximumAllowedCardinality}.");
        }

        MaximumCardinality = maximumCardinality;
    }

    public EvidenceKey EvidenceKey { get; }

    public EvidenceValueKind ValueKind { get; }

    public string? Unit { get; }

    public string SourceReference { get; }

    public int MaximumCardinality { get; }

    private static string? RequireOptionalCode(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Length > 128)
        {
            throw new ArgumentException("The optional code is not a bounded normalized value.", parameterName);
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':')
            {
                throw new ArgumentException(
                    "Codes may contain only ASCII letters, digits, hyphens, underscores, periods, and colons.",
                    parameterName);
            }
        }

        return value;
    }
}
