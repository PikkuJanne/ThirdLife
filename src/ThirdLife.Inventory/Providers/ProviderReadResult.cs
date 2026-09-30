using System.Collections.ObjectModel;
using ThirdLife.Inventory.Normalization;

namespace ThirdLife.Inventory.Providers;

public enum ProviderReadStatus
{
    Collected = 1,
    Unavailable,
    AccessDenied,
    InvalidData,
    CleanupIncomplete,
    Failed,
}

public sealed class ProviderReadResult
{
    private ProviderReadResult(ProviderReadStatus status, IEnumerable<NormalizedEvidence>? evidence)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The provider-read status is not defined.");
        }

        Status = status;
        var copiedEvidence = evidence?.Take(InventoryProviderDescriptor.MaximumEvidenceKeyCount + 1).ToArray()
            ?? [];
        if (copiedEvidence.Length > InventoryProviderDescriptor.MaximumEvidenceKeyCount ||
            copiedEvidence.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Provider evidence must be non-null and remain within its declared bound.",
                nameof(evidence));
        }

        if (Status == ProviderReadStatus.Collected && copiedEvidence.Length == 0)
        {
            throw new ArgumentException(
                "Collected results require evidence.",
                nameof(evidence));
        }

        Evidence = Array.AsReadOnly(copiedEvidence);
    }

    public ProviderReadStatus Status { get; }

    public ReadOnlyCollection<NormalizedEvidence> Evidence { get; }

    public static ProviderReadResult Collected(IEnumerable<NormalizedEvidence> evidence) =>
        new(ProviderReadStatus.Collected, evidence ?? throw new ArgumentNullException(nameof(evidence)));

    public static ProviderReadResult Unavailable(IEnumerable<NormalizedEvidence>? retainedEvidence = null) =>
        new(ProviderReadStatus.Unavailable, retainedEvidence);

    public static ProviderReadResult AccessDenied(IEnumerable<NormalizedEvidence>? retainedEvidence = null) =>
        new(ProviderReadStatus.AccessDenied, retainedEvidence);

    public static ProviderReadResult InvalidData(IEnumerable<NormalizedEvidence>? retainedEvidence = null) =>
        new(ProviderReadStatus.InvalidData, retainedEvidence);

    public static ProviderReadResult CleanupIncomplete(IEnumerable<NormalizedEvidence>? retainedEvidence = null) =>
        new(ProviderReadStatus.CleanupIncomplete, retainedEvidence);

    public static ProviderReadResult Failed(IEnumerable<NormalizedEvidence>? retainedEvidence = null) =>
        new(ProviderReadStatus.Failed, retainedEvidence);
}
