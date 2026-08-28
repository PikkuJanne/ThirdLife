using ThirdLife.Core.Evidence;

namespace ThirdLife.Inventory.Providers;

public sealed class ProviderFailureDefinition
{
    public ProviderFailureDefinition(EvidenceKey evidenceKey, string sourceReference)
    {
        EvidenceKey = evidenceKey ?? throw new ArgumentNullException(nameof(evidenceKey));
        SourceReference = new EvidenceProvenance(
            ProvenanceKind.ProviderObservation,
            sourceReference).SourceReference;
    }

    public EvidenceKey EvidenceKey { get; }

    public string SourceReference { get; }
}
