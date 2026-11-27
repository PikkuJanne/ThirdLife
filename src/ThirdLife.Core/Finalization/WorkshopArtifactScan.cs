using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Core.Finalization;

[JsonConverter(typeof(StableStringEnumConverter<WorkshopArtifactKind>))]
public enum WorkshopArtifactKind
{
    [JsonStringEnumMemberName("battery_report_residue")]
    BatteryReportResidue = 1,
    [JsonStringEnumMemberName("workshop_report_copies")]
    WorkshopReportCopies,
    [JsonStringEnumMemberName("package_staging_residue")]
    PackageStagingResidue,
    [JsonStringEnumMemberName("configured_local_accounts")]
    ConfiguredLocalAccounts,
    [JsonStringEnumMemberName("configured_smb_shares")]
    ConfiguredSmbShares,
    [JsonStringEnumMemberName("remote_desktop_setting")]
    RemoteDesktopSetting,
    [JsonStringEnumMemberName("solicited_remote_assistance_policy")]
    SolicitedRemoteAssistancePolicy,
    [JsonStringEnumMemberName("offer_remote_assistance_policy")]
    OfferRemoteAssistancePolicy,
}

[JsonConverter(typeof(StableStringEnumConverter<WorkshopArtifactStatus>))]
public enum WorkshopArtifactStatus
{
    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
    [JsonStringEnumMemberName("present")]
    Present,
    [JsonStringEnumMemberName("absent_in_checked_scope")]
    AbsentInCheckedScope,
    [JsonStringEnumMemberName("targets_not_configured")]
    TargetsNotConfigured,
    [JsonStringEnumMemberName("access_denied")]
    AccessDenied,
    [JsonStringEnumMemberName("unsafe_path_or_entry")]
    UnsafePathOrEntry,
    [JsonStringEnumMemberName("scan_limit_reached")]
    ScanLimitReached,
    [JsonStringEnumMemberName("timed_out")]
    TimedOut,
}

public sealed record WorkshopArtifactFinding
{
    public WorkshopArtifactFinding(WorkshopArtifactKind kind, WorkshopArtifactStatus status)
    {
        Kind = DomainValue.RequireDefined(kind, nameof(kind));
        Status = DomainValue.RequireDefined(status, nameof(status));
        if (status == WorkshopArtifactStatus.TargetsNotConfigured &&
            kind is not (WorkshopArtifactKind.ConfiguredLocalAccounts or WorkshopArtifactKind.ConfiguredSmbShares))
        {
            throw new ArgumentException("Only explicit local account or share targets can be unconfigured.", nameof(status));
        }
    }

    public WorkshopArtifactKind Kind { get; }
    public WorkshopArtifactStatus Status { get; }
}

public sealed record WorkshopArtifactScan
{
    public const string CurrentDetectorRevision = "workshop-artifact-detector-1";
    public const int MaximumScansPerJob = 128;
    private static readonly WorkshopArtifactKind[] Kinds = Enum.GetValues<WorkshopArtifactKind>();

    public WorkshopArtifactScan(long sequence, DateTimeOffset recordedAtUtc, string evidenceVersion,
        string detectorRevision, ProvenanceKind origin, IReadOnlyList<WorkshopArtifactFinding> findings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaximumScansPerJob);
        ArgumentNullException.ThrowIfNull(findings);
        if (origin is not (ProvenanceKind.ProviderObservation or ProvenanceKind.SyntheticFixture))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        var copy = findings.Take(Kinds.Length + 1).ToArray();
        if (copy.Length != Kinds.Length || copy.Any(finding => finding is null) ||
            !copy.Select(finding => finding.Kind).Order().SequenceEqual(Kinds))
        {
            throw new ArgumentException("A scan requires exactly one typed result for each registered source.", nameof(findings));
        }

        Sequence = sequence;
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
        EvidenceVersion = DomainValue.RequireVersion(evidenceVersion, nameof(evidenceVersion));
        DetectorRevision = DomainValue.RequireVersion(detectorRevision, nameof(detectorRevision));
        Origin = origin;
        Findings = Array.AsReadOnly(copy);
    }

    public long Sequence { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public string EvidenceVersion { get; }
    public string DetectorRevision { get; }
    public ProvenanceKind Origin { get; }
    public IReadOnlyList<WorkshopArtifactFinding> Findings { get; }
}
