using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

internal enum SecurityCheck
{
    FirewallHealth,
    AntivirusHealth,
    UacHealth,
    UacEnabled,
    DomainJoined,
    MdmRegistered,
    CloudJoin,
    RemoteDesktopConnections,
    NtpClientEnabled,
    DefenderHealth,
    SystemVolumeProtection,
}

public sealed class SecurityBaselineProvider : IInventoryProvider
{
    internal const string ProviderIdentity = "windows-security-baseline";
    private static readonly (SecurityCheck Check, string Key, string Source)[] Checks =
    [
        (SecurityCheck.FirewallHealth, "security.firewall_health", "security.wsc.firewall"),
        (SecurityCheck.AntivirusHealth, "security.antivirus_health", "security.wsc.antivirus"),
        (SecurityCheck.UacHealth, "security.uac_health", "security.wsc.uac"),
        (SecurityCheck.UacEnabled, "security.uac_configured", "security.registry.enable_lua"),
        (SecurityCheck.DomainJoined, "ownership.domain_join", "security.netapi.domain_join"),
        (SecurityCheck.MdmRegistered, "ownership.mdm_registration", "security.mdm.registration"),
        (SecurityCheck.CloudJoin, "ownership.cloud_join", "security.netapi.cloud_join"),
        (SecurityCheck.RemoteDesktopConnections, "security.remote_desktop_configured", "security.registry.rdp_deny"),
        (SecurityCheck.NtpClientEnabled, "security.ntp_client_configured", "security.registry.ntp_client"),
        (SecurityCheck.DefenderHealth, "security.defender_health", "security.defender.computer_status"),
        (SecurityCheck.SystemVolumeProtection, "security.system_volume_protection", "security.bitlocker.system_volume"),
    ];
    private const string OwnershipSource = "security.management_indicators";
    private const string UacSource = "security.uac_configuration_and_health";
    private static readonly EvidenceKey OwnershipKey = new("ownership.lock_state");
    private static readonly EvidenceKey UacKey = new("security.uac_state");

    private readonly Func<SecurityCheck, CancellationToken, SystemInventorySourceValue<string>> _read;
    private int _active;
    private bool _cleanupFailed;

    public SecurityBaselineProvider() : this(WindowsSecurityBaselineSource.Read, ProviderEvidenceOrigin.ActiveMachine) { }

    internal SecurityBaselineProvider(Func<SecurityCheck, CancellationToken, SystemInventorySourceValue<string>> read, ProviderEvidenceOrigin origin)
    {
        _read = read;
        var fields = Checks.Select(item => new ProviderEvidenceDefinition(
            new EvidenceKey(item.Key), EvidenceValueKind.Enum, null, item.Source));
        Descriptor = new InventoryProviderDescriptor(new ProviderId(ProviderIdentity),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10),
            ProviderNetworkUse.None, origin, [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new("security.baseline_provider_status"), "security.baseline_status"),
            [.. fields, new(OwnershipKey, EvidenceValueKind.Enum, null, OwnershipSource),
             new(UacKey, EvidenceValueKind.Enum, null, UacSource)]);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) { return ProviderReadResult.Unavailable(); }
        try
        {
            if (_cleanupFailed) { return ProviderReadResult.CleanupIncomplete(); }
            return await Task.Run(() => Collect(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (SecuritySourceCleanupException)
        {
            _cleanupFailed = true;
            return ProviderReadResult.CleanupIncomplete();
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }

    private ProviderReadResult Collect(CancellationToken cancellationToken)
    {
        List<NormalizedEvidence> findings = [];
        Dictionary<SecurityCheck, SystemInventorySourceValue<string>> values = [];
        foreach (var (check, key, source) in Checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = _read(check, cancellationToken);
            values.Add(check, value);
            findings.Add(value.Status == SystemInventorySourceStatus.Available
                ? NormalizedEvidence.Observed(new(key), EvidenceValue.FromEnum(value.GetRequiredValue()), sourceReference: source)
                : NormalizedEvidence.NotAvailable(new(key), value.Status == SystemInventorySourceStatus.InvalidData
                    ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing, source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool managementIndicated = values[SecurityCheck.DomainJoined].Value == "domain_joined" ||
            values[SecurityCheck.MdmRegistered].Value == "registered" ||
            values[SecurityCheck.CloudJoin].Value is "device_joined" or "workplace_joined";
        findings.Add(managementIndicated
            ? NormalizedEvidence.Inferred(OwnershipKey, EvidenceValue.FromEnum("management_indicated"), sourceReference: OwnershipSource)
            : NormalizedEvidence.NotAvailable(OwnershipKey, ProviderLimitation.SourceValueMissing, OwnershipSource));

        var uac = (values[SecurityCheck.UacHealth].Value, values[SecurityCheck.UacEnabled].Value) switch
        {
            ("good", "enabled") => NormalizedEvidence.Inferred(UacKey, EvidenceValue.FromEnum("enabled"), sourceReference: UacSource),
            ("good", "disabled") => NormalizedEvidence.NotAvailable(UacKey, ProviderLimitation.SourceValuesConflict, UacSource),
            (_, "disabled") or ("poor" or "snoozed", _) => NormalizedEvidence.Inferred(UacKey,
                EvidenceValue.FromEnum("requires_review"), sourceReference: UacSource),
            _ => NormalizedEvidence.NotAvailable(UacKey, ProviderLimitation.SourceValueMissing, UacSource),
        };
        findings.Add(uac);

        var statuses = values.Values.Select(item => item.Status);
        if (statuses.Contains(SystemInventorySourceStatus.InvalidData)) { return ProviderReadResult.InvalidData(findings); }
        if (statuses.Contains(SystemInventorySourceStatus.AccessDenied)) { return ProviderReadResult.AccessDenied(findings); }
        return statuses.Contains(SystemInventorySourceStatus.Unavailable)
            ? ProviderReadResult.Unavailable(findings) : ProviderReadResult.Collected(findings);
    }
}
