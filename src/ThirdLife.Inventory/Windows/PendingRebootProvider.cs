using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

public sealed class PendingRebootProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-pending-reboot";

    private const string ComponentServicingSource = "pending_reboot.registry64.cbs_key";
    private const string WindowsUpdateSource = "pending_reboot.registry64.windows_update_key";
    private const string FileRenameSource = "pending_reboot.registry64.rename_metadata";
    private const string AggregateSource = "pending_reboot.partial_indicator_union";
    private static readonly EvidenceKey PendingStatusKey = new("reboot.pending_status");
    private readonly IPendingRebootSource _source;
    private int _collectionInProgress;

    public PendingRebootProvider()
        : this(new WindowsPendingRebootSource(), ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal PendingRebootProvider(IPendingRebootSource source, ProviderEvidenceOrigin evidenceOrigin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Descriptor = CreateDescriptor(evidenceOrigin);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _collectionInProgress, 1, 0) != 0)
        {
            return ProviderReadResult.Unavailable();
        }

        try
        {
            var snapshot = await Task.Run(
                () => _source.Read(cancellationToken), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(snapshot);
        }
        finally
        {
            // A runner timeout cannot terminate a native registry call. Keep this guard
            // until that call and its handle cleanup return, so retries cannot add workers.
            Volatile.Write(ref _collectionInProgress, 0);
        }
    }

    private static InventoryProviderDescriptor CreateDescriptor(ProviderEvidenceOrigin evidenceOrigin)
    {
        var definitions = new List<ProviderEvidenceDefinition>
        {
            new(PendingStatusKey, EvidenceValueKind.Enum, null, AggregateSource),
        };
        AddDefinitions(definitions, "cbs", ComponentServicingSource);
        AddDefinitions(definitions, "windows_update", WindowsUpdateSource);
        AddDefinitions(definitions, "file_rename", FileRenameSource);
        return new InventoryProviderDescriptor(
            new ProviderId(ProviderIdentity),
            ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(2),
            ProviderNetworkUse.None,
            evidenceOrigin,
            [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("reboot.provider_status"), "pending_reboot.provider.status"),
            definitions);
    }

    private static void AddDefinitions(List<ProviderEvidenceDefinition> definitions, string sourceName, string source)
    {
        foreach (var field in new[] { "indicator", "confidence", "collection_status", "recovery" })
        {
            definitions.Add(new ProviderEvidenceDefinition(
                Key(sourceName, field), EvidenceValueKind.Enum, null, source));
        }
    }

    private static EvidenceKey Key(string sourceName, string field) => new($"reboot.{sourceName}.{field}");

    private static ProviderReadResult Normalize(PendingRebootSnapshot snapshot)
    {
        var evidence = new List<NormalizedEvidence>();
        AddSource(evidence, "cbs", ComponentServicingSource, snapshot.ComponentServicing);
        AddSource(evidence, "windows_update", WindowsUpdateSource, snapshot.WindowsUpdate);
        AddSource(evidence, "file_rename", FileRenameSource, snapshot.FileRename);
        var sources = new[] { snapshot.ComponentServicing, snapshot.WindowsUpdate, snapshot.FileRename };
        var hasPresentIndicator = sources.Any(static source =>
            source.Status == SystemInventorySourceStatus.Available && source.GetRequiredValue());
        evidence.Add(hasPresentIndicator
            ? NormalizedEvidence.Inferred(
                PendingStatusKey,
                EvidenceValue.FromEnum("pending_indicated"),
                sourceReference: AggregateSource)
            : NormalizedEvidence.NotAvailable(
                PendingStatusKey, ProviderLimitation.SourceValueMissing, AggregateSource));

        var statuses = sources.Select(static source => source.Status).ToArray();
        if (statuses.Contains(SystemInventorySourceStatus.AccessDenied))
        {
            return ProviderReadResult.AccessDenied(evidence);
        }

        if (statuses.Contains(SystemInventorySourceStatus.InvalidData))
        {
            return ProviderReadResult.InvalidData(evidence);
        }

        return statuses.Contains(SystemInventorySourceStatus.Unavailable)
            ? ProviderReadResult.Unavailable(evidence)
            : ProviderReadResult.Collected(evidence);
    }

    private static void AddSource(
        List<NormalizedEvidence> evidence,
        string sourceName,
        string provenance,
        SystemInventorySourceValue<bool> value)
    {
        var (status, recovery) = value.Status switch
        {
            SystemInventorySourceStatus.Available => ("available", "none"),
            SystemInventorySourceStatus.AccessDenied => ("access_denied", "review_access"),
            SystemInventorySourceStatus.Unavailable => ("unavailable", "retry_collection"),
            SystemInventorySourceStatus.InvalidData => ("invalid_data", "review_provider_data"),
            _ => throw new InvalidOperationException("The reboot source status is not defined."),
        };
        evidence.Add(NormalizedEvidence.Observed(
            Key(sourceName, "collection_status"), EvidenceValue.FromEnum(status), sourceReference: provenance));
        evidence.Add(NormalizedEvidence.Inferred(
            Key(sourceName, "recovery"), EvidenceValue.FromEnum(recovery), sourceReference: provenance));

        if (value.Status == SystemInventorySourceStatus.Available)
        {
            evidence.Add(NormalizedEvidence.Inferred(
                Key(sourceName, "indicator"),
                EvidenceValue.FromEnum(value.GetRequiredValue() ? "present" : "absent"),
                sourceReference: provenance));
            evidence.Add(NormalizedEvidence.Inferred(
                Key(sourceName, "confidence"), EvidenceValue.FromEnum("indicator_only"), sourceReference: provenance));
            return;
        }

        var limitation = value.Status == SystemInventorySourceStatus.InvalidData
            ? ProviderLimitation.SourceValueMalformed
            : ProviderLimitation.SourceValueMissing;
        evidence.Add(NormalizedEvidence.NotAvailable(Key(sourceName, "indicator"), limitation, provenance));
        evidence.Add(NormalizedEvidence.NotAvailable(Key(sourceName, "confidence"), limitation, provenance));
    }
}
