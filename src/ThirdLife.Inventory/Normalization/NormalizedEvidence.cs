using ThirdLife.Core.Evidence;

namespace ThirdLife.Inventory.Normalization;

public enum ProviderLimitation
{
    ProviderUnavailable = 1,
    AccessDenied,
    CollectionCancelled,
    CollectionTimedOut,
    InvalidProviderData,
    ProviderContractInvalid,
    CleanupIncomplete,
    UnexpectedProviderFailure,
    SourceValueMissing,
    SourceValueMalformed,
    SourceValuesConflict,
    SourceValueStale,
    CapabilityNotPresent,
}

public sealed class NormalizedEvidence
{
    private NormalizedEvidence(
        EvidenceKey evidenceKey,
        EvidenceClassification evidenceClassification,
        ValueAvailability valueAvailability,
        EvidenceValue? value,
        ProviderLimitation? limitation,
        string? sourceReference)
    {
        EvidenceKey = evidenceKey ?? throw new ArgumentNullException(nameof(evidenceKey));
        EvidenceClassification = evidenceClassification;
        ValueAvailability = valueAvailability;
        Value = value;
        Limitation = limitation;
        SourceReference = sourceReference is null
            ? null
            : new EvidenceProvenance(ProvenanceKind.ProviderObservation, sourceReference).SourceReference;
    }

    public EvidenceKey EvidenceKey { get; }

    public EvidenceClassification EvidenceClassification { get; }

    public ValueAvailability ValueAvailability { get; }

    public EvidenceValue? Value { get; }

    public ProviderLimitation? Limitation { get; }

    public string? SourceReference { get; }

    public static NormalizedEvidence Observed(
        EvidenceKey evidenceKey,
        EvidenceValue value,
        ProviderLimitation? limitation = null,
        string? sourceReference = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new NormalizedEvidence(
            evidenceKey,
            EvidenceClassification.Observed,
            ValueAvailability.Available,
            value,
            RequireAvailableLimitation(limitation),
            sourceReference);
    }

    public static NormalizedEvidence Inferred(
        EvidenceKey evidenceKey,
        EvidenceValue value,
        ProviderLimitation? limitation = null,
        string? sourceReference = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new NormalizedEvidence(
            evidenceKey,
            EvidenceClassification.Inferred,
            ValueAvailability.Available,
            value,
            RequireAvailableLimitation(limitation),
            sourceReference);
    }

    public static NormalizedEvidence NotAvailable(
        EvidenceKey evidenceKey,
        ProviderLimitation limitation,
        string? sourceReference = null) =>
        new(
            evidenceKey,
            EvidenceClassification.NotAvailable,
            ValueAvailability.Unknown,
            value: null,
            RequireUnavailableLimitation(limitation),
            sourceReference);

    internal static NormalizedEvidence RunFailure(
        EvidenceKey evidenceKey,
        ProviderLimitation limitation) =>
        new(
            evidenceKey,
            EvidenceClassification.NotAvailable,
            ValueAvailability.Unknown,
            value: null,
            RequireDefined(limitation),
            sourceReference: null);

    public static NormalizedEvidence NotApplicable(
        EvidenceKey evidenceKey,
        ProviderLimitation limitation,
        string? sourceReference = null)
    {
        if (limitation != ProviderLimitation.CapabilityNotPresent)
        {
            throw new ArgumentException(
                "Not-applicable evidence is reserved for an observed capability absence.",
                nameof(limitation));
        }

        return new(
            evidenceKey,
            EvidenceClassification.Observed,
            ValueAvailability.NotApplicable,
            value: null,
            limitation,
            sourceReference);
    }

    private static ProviderLimitation RequireDefined(ProviderLimitation limitation)
    {
        if (!Enum.IsDefined(limitation))
        {
            throw new ArgumentOutOfRangeException(nameof(limitation), limitation, "The limitation is not defined.");
        }

        return limitation;
    }

    private static ProviderLimitation? RequireAvailableLimitation(ProviderLimitation? limitation)
    {
        if (limitation is null)
        {
            return null;
        }

        if (limitation is not ProviderLimitation.SourceValueMissing and
            not ProviderLimitation.SourceValuesConflict and
            not ProviderLimitation.SourceValueStale)
        {
            throw new ArgumentException(
                "Available evidence can retain only a missing-input, conflict, or stale-source limitation.",
                nameof(limitation));
        }

        return limitation;
    }

    private static ProviderLimitation RequireUnavailableLimitation(ProviderLimitation limitation)
    {
        if (limitation is not ProviderLimitation.SourceValueMissing and
            not ProviderLimitation.SourceValueMalformed and
            not ProviderLimitation.SourceValuesConflict and
            not ProviderLimitation.SourceValueStale)
        {
            throw new ArgumentException(
                "Provider-supplied unavailable evidence must describe a source-value limitation.",
                nameof(limitation));
        }

        return limitation;
    }
}
