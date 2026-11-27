using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

public sealed class SystemDiskHealthProvider : IInventoryProvider
{
    private const string Source = "storage.msft.boot_partition_disk";
    private static readonly EvidenceKey Health = new("storage.system.health_state");
    private readonly Func<CancellationToken, SystemInventorySourceValue<ushort>> _read;
    private int _collecting;

    public SystemDiskHealthProvider() : this(ReadSystemDiskHealth, ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal SystemDiskHealthProvider(Func<CancellationToken, SystemInventorySourceValue<ushort>> read,
        ProviderEvidenceOrigin origin)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        Descriptor = new InventoryProviderDescriptor(new ProviderId("windows-system-disk-health"),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5),
            ProviderNetworkUse.None, origin, [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("storage.system.health_provider_status"), "storage.system.health_provider.status"),
            [new ProviderEvidenceDefinition(Health, EvidenceValueKind.Enum, null, Source)]);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _collecting, 1, 0) != 0)
        {
            return ProviderReadResult.Unavailable();
        }

        try
        {
            var value = await Task.Run(() => _read(cancellationToken), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var unknown = NormalizedEvidence.NotAvailable(Health, ProviderLimitation.SourceValueMissing, Source);
            var malformed = NormalizedEvidence.NotAvailable(Health, ProviderLimitation.SourceValueMalformed, Source);
            return value.Status switch
            {
                SystemInventorySourceStatus.AccessDenied => ProviderReadResult.AccessDenied([unknown]),
                SystemInventorySourceStatus.InvalidData => ProviderReadResult.InvalidData([malformed]),
                SystemInventorySourceStatus.Available => value.GetRequiredValue() switch
                {
                    0 => Collected("healthy"),
                    1 => Collected("warning"),
                    2 => Collected("unhealthy"),
                    _ => ProviderReadResult.Unavailable([unknown]),
                },
                _ => ProviderReadResult.Unavailable([unknown]),
            };
        }
        finally
        {
            // A timed-out caller cannot launch more native reads while this one is draining.
            Volatile.Write(ref _collecting, 0);
        }
    }

    private static ProviderReadResult Collected(string status) => ProviderReadResult.Collected(
        [NormalizedEvidence.Inferred(Health, EvidenceValue.FromEnum(status), sourceReference: Source)]);

    private static SystemInventorySourceValue<ushort> ReadSystemDiskHealth(CancellationToken token)
    {
        var driveLetter = WindowsStorageVolumeSource.ReadSystemDriveLetter(token);
        return driveLetter.Status switch
        {
            SystemInventorySourceStatus.Available => WindowsStorageWmiSource.ReadSystemDiskHealth(driveLetter.GetRequiredValue(), token),
            SystemInventorySourceStatus.AccessDenied => SystemInventorySourceValue<ushort>.AccessDenied(),
            SystemInventorySourceStatus.InvalidData => SystemInventorySourceValue<ushort>.InvalidData(),
            _ => SystemInventorySourceValue<ushort>.Unavailable(),
        };
    }
}
