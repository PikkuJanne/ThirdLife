using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Windows;

public sealed class OperatingSystemProvider : IInventoryProvider
{
    private const string VersionSource = "os.dotnet.environment.version";
    private const string EditionSource = "os.kernel32.product_info";
    private const string ArchitectureSource = "os.dotnet.os_architecture";
    private const string IdentitySource = "os.windows.identity";
    private const string LifecycleSource = "os.lifecycle.catalog";

    private readonly IOperatingSystemSource _source;
    private readonly WindowsLifecycleCatalog? _catalog;
    private readonly TimeProvider _timeProvider;
    private int _reading;

    public OperatingSystemProvider()
        : this(new WindowsOperatingSystemSource(), WindowsLifecycleCatalog.Current,
            TimeProvider.System, ProviderEvidenceOrigin.ActiveMachine)
    {
    }

    internal OperatingSystemProvider(
        IOperatingSystemSource source,
        WindowsLifecycleCatalog? catalog,
        TimeProvider timeProvider,
        ProviderEvidenceOrigin evidenceOrigin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _catalog = catalog;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Descriptor = new(
            new ProviderId("windows-operating-system"),
            ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2),
            ProviderNetworkUse.None, evidenceOrigin,
            [ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("os.provider_status"), "os.provider.status"),
            [
                Definition("os.version", EvidenceValueKind.Text, VersionSource),
                Definition("os.build", EvidenceValueKind.WholeNumber, VersionSource),
                Definition("os.edition_code", EvidenceValueKind.WholeNumber, EditionSource),
                Definition("os.edition", EvidenceValueKind.Enum, EditionSource),
                Definition("os.architecture", EvidenceValueKind.Enum, ArchitectureSource),
                Definition("os.support_target", EvidenceValueKind.Enum, LifecycleSource),
                Definition("os.lifecycle.source", EvidenceValueKind.Text, LifecycleSource),
                Definition("os.lifecycle.revision", EvidenceValueKind.Text, LifecycleSource),
                Definition("os.lifecycle.as_of", EvidenceValueKind.Text, LifecycleSource),
                Definition("os.lifecycle.fresh_through", EvidenceValueKind.Text, LifecycleSource),
                Definition("os.lifecycle.evaluated_on", EvidenceValueKind.Text, LifecycleSource),
                Definition("os.lifecycle.release", EvidenceValueKind.Text, LifecycleSource),
                Definition("os.family", EvidenceValueKind.Enum, IdentitySource),
                Definition("os.lifecycle.reason", EvidenceValueKind.Enum, LifecycleSource),
            ]);
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A timed-out synchronous OS call retains this slot until it actually returns.
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
        {
            return ProviderReadResult.Unavailable();
        }

        try
        {
            var snapshot = await Task.Run(() => _source.Read(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Normalize(snapshot);
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    private ProviderReadResult Normalize(OperatingSystemSnapshot snapshot)
    {
        var evidence = new List<NormalizedEvidence>();
        var version = snapshot.Version.Status == SystemInventorySourceStatus.Available
            ? snapshot.Version.GetRequiredValue() : null;
        var validVersion = WindowsPlatform.IsValidVersion(version);
        if (validVersion)
        {
            evidence.Add(Observed("os.version", EvidenceValue.FromString(
                string.Create(CultureInfo.InvariantCulture, $"{version!.Major}.{version.Minor}.{version.Build}")), VersionSource));
            evidence.Add(Observed("os.build", EvidenceValue.FromInteger(version.Build), VersionSource));
        }
        else
        {
            evidence.Add(Missing("os.version", VersionSource, SourceLimitation(snapshot.Version.Status)));
            evidence.Add(Missing("os.build", VersionSource, SourceLimitation(snapshot.Version.Status)));
        }

        uint? edition = snapshot.Edition.Status == SystemInventorySourceStatus.Available
            ? snapshot.Edition.GetRequiredValue() : null;
        var editionName = edition.HasValue ? WindowsLifecycleCatalog.GetEditionName(edition.Value) : null;
        evidence.Add(edition.HasValue
            ? Observed("os.edition_code", EvidenceValue.FromInteger(edition.Value), EditionSource)
            : Missing("os.edition_code", EditionSource, SourceLimitation(snapshot.Edition.Status)));
        evidence.Add(editionName is not null
            ? NormalizedEvidence.Inferred(new EvidenceKey("os.edition"), EvidenceValue.FromEnum(editionName), sourceReference: EditionSource)
            : Missing("os.edition", EditionSource, SourceLimitation(snapshot.Edition.Status)));

        var architecture = WindowsPlatform.GetArchitecture(snapshot.Architecture);
        evidence.Add(architecture is not null
            ? Observed("os.architecture", EvidenceValue.FromEnum(architecture), ArchitectureSource)
            : Missing("os.architecture", ArchitectureSource, SourceLimitation(snapshot.Architecture.Status)));

        var date = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var identity = WindowsPlatform.Evaluate(snapshot);
        var lifecycle = identity.Family == "windows_11"
            ? _catalog?.Evaluate((uint)version!.Build, edition ?? 0, architecture, date)
            : null;
        evidence.Add(identity.Family is { } family
            ? NormalizedEvidence.Inferred(new EvidenceKey("os.family"), EvidenceValue.FromEnum(family), sourceReference: IdentitySource)
            : Missing("os.family", IdentitySource, SourceLimitation(snapshot.ProductType.Status)));
        AddLifecycle(evidence, identity, lifecycle, date);

        var statuses = new[]
        {
            snapshot.Version.Status, snapshot.Edition.Status, snapshot.Architecture.Status, snapshot.ProductType.Status,
        };
        if (statuses.Contains(SystemInventorySourceStatus.AccessDenied))
        {
            return ProviderReadResult.AccessDenied(evidence);
        }

        if (statuses.Contains(SystemInventorySourceStatus.InvalidData) || (version is not null && !validVersion))
        {
            return ProviderReadResult.InvalidData(evidence);
        }

        return statuses.Contains(SystemInventorySourceStatus.Unavailable)
            ? ProviderReadResult.Unavailable(evidence)
            : ProviderReadResult.Collected(evidence);
    }

    private static void AddLifecycle(
        List<NormalizedEvidence> evidence, WindowsPlatformIdentity identity, WindowsLifecycleResult? lifecycle, DateOnly date)
    {
        var reason = identity.Eligibility switch
        {
            WindowsPlatformEligibility.UnsupportedOperatingSystem => WindowsLifecycleLimitation.UnsupportedOperatingSystem,
            WindowsPlatformEligibility.UnsupportedArchitecture => WindowsLifecycleLimitation.UnsupportedArchitecture,
            _ => lifecycle?.Limitation ?? (identity.Family == "windows_11"
                ? WindowsLifecycleLimitation.MissingData
                : identity.Limitation),
        };
        var limitation = reason switch
        {
            WindowsLifecycleLimitation.StaleData => ProviderLimitation.SourceValueStale,
            WindowsLifecycleLimitation.ContradictoryData => ProviderLimitation.SourceValuesConflict,
            WindowsLifecycleLimitation.MissingData => ProviderLimitation.SourceValueMissing,
            _ => ProviderLimitation.SourceValueMalformed,
        };
        var status = identity.Eligibility is WindowsPlatformEligibility.UnsupportedOperatingSystem or
            WindowsPlatformEligibility.UnsupportedArchitecture ? "unsupported" : lifecycle?.Status switch
            {
                WindowsLifecycleStatus.Supported => "supported",
                WindowsLifecycleStatus.Unsupported => "unsupported",
                _ => null,
            };
        evidence.Add(status is null
            ? Missing("os.support_target", LifecycleSource, limitation)
            : NormalizedEvidence.Inferred(new EvidenceKey("os.support_target"), EvidenceValue.FromEnum(status), sourceReference: LifecycleSource));
        AddText(evidence, "os.lifecycle.source", lifecycle?.SourceId, limitation);
        AddText(evidence, "os.lifecycle.revision", lifecycle?.Revision, limitation);
        AddText(evidence, "os.lifecycle.as_of", FormatDate(lifecycle?.AsOf), limitation);
        AddText(evidence, "os.lifecycle.fresh_through", FormatDate(lifecycle?.FreshThrough), limitation);
        AddText(evidence, "os.lifecycle.evaluated_on", FormatDate(date), limitation);
        AddText(evidence, "os.lifecycle.release", lifecycle?.Release, limitation);
        evidence.Add(NormalizedEvidence.Inferred(new EvidenceKey("os.lifecycle.reason"),
            EvidenceValue.FromEnum(reason.ToString()),
            sourceReference: LifecycleSource));
    }

    private static void AddText(List<NormalizedEvidence> evidence, string key, string? value, ProviderLimitation limitation) =>
        evidence.Add(value is null ? Missing(key, LifecycleSource, limitation)
            : NormalizedEvidence.Inferred(new EvidenceKey(key), EvidenceValue.FromString(value), sourceReference: LifecycleSource));

    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static ProviderLimitation SourceLimitation(SystemInventorySourceStatus status) =>
        status == SystemInventorySourceStatus.Available || status == SystemInventorySourceStatus.InvalidData
            ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing;

    private static NormalizedEvidence Observed(string key, EvidenceValue value, string source) =>
        NormalizedEvidence.Observed(new EvidenceKey(key), value, sourceReference: source);

    private static NormalizedEvidence Missing(string key, string source, ProviderLimitation limitation) =>
        NormalizedEvidence.NotAvailable(new EvidenceKey(key), limitation, source);

    private static ProviderEvidenceDefinition Definition(string key, EvidenceValueKind kind, string source) =>
        new(new EvidenceKey(key), kind, unit: null, source);
}
