using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

/// <summary>
/// Observes Windows activation status. This is not evidence of legal licence ownership.
/// </summary>
public sealed class ActivationProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-activation";
    private const string StatusSource = "activation.slc.windows_application_status";
    private static readonly EvidenceKey StateKey = new("activation.state");

    private readonly IActivationSource _source;
    private int _observationInProgress;

    public ActivationProvider()
        : this(new WindowsActivationSource(), ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal ActivationProvider(IActivationSource source, ProviderEvidenceOrigin evidenceOrigin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Descriptor = new InventoryProviderDescriptor(
            new ProviderId(ProviderIdentity),
            ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(5),
            ProviderNetworkUse.None,
            evidenceOrigin,
            [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("activation.provider_status"), "activation.provider.status"),
            [new ProviderEvidenceDefinition(StateKey, EvidenceValueKind.Enum, unit: null, StatusSource)]);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _observationInProgress, 1, 0) != 0)
        {
            return ProviderReadResult.Unavailable();
        }

        try
        {
            // SLC calls have no cancellation API. The runner bounds the caller's wait;
            // this guard remains held until the native call returns and cleanup finishes.
            var snapshot = await Task.Run(() => _source.Read(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!snapshot.CleanupComplete)
            {
                return ProviderReadResult.CleanupIncomplete();
            }

            return snapshot.Statuses.Status switch
            {
                SystemInventorySourceStatus.Available => ProviderReadResult.Collected(
                    [Normalize(snapshot.Statuses.GetRequiredValue(), cancellationToken)]),
                SystemInventorySourceStatus.Unavailable => ProviderReadResult.Unavailable(),
                SystemInventorySourceStatus.AccessDenied => ProviderReadResult.AccessDenied(),
                SystemInventorySourceStatus.InvalidData => ProviderReadResult.InvalidData(),
                _ => throw new InvalidOperationException("The activation source status is not defined."),
            };
        }
        finally
        {
            Volatile.Write(ref _observationInProgress, 0);
        }
    }

    private static NormalizedEvidence Normalize(int[] statuses, CancellationToken cancellationToken)
    {
        if (statuses.Length > WindowsActivationSource.MaximumStatusCount)
        {
            return Unknown(ProviderLimitation.SourceValueMalformed);
        }

        if (statuses.Length == 0)
        {
            return Unknown(ProviderLimitation.SourceValueMissing);
        }

        var distinctStatuses = new HashSet<int>();
        foreach (var status in statuses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (status is < 0 or > 3)
            {
                return Unknown(ProviderLimitation.SourceValueMalformed);
            }

            distinctStatuses.Add(status);
        }

        // SLC may return several Windows SKUs. Selecting the first, last, or any licensed
        // row could hide contradictory states. Only unanimous known statuses are reported.
        if (distinctStatuses.Count != 1)
        {
            return Unknown(ProviderLimitation.SourceValuesConflict);
        }

        var value = distinctStatuses.Single() switch
        {
            0 => "unactivated",
            1 => "activated",
            2 => "grace_period",
            3 => "notification",
            _ => throw new InvalidOperationException("The activation status is not defined."),
        };
        return NormalizedEvidence.Observed(StateKey, EvidenceValue.FromEnum(value), sourceReference: StatusSource);
    }

    private static NormalizedEvidence Unknown(ProviderLimitation limitation) =>
        NormalizedEvidence.NotAvailable(StateKey, limitation, StatusSource);
}
