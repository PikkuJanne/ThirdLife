using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Finalization;

public interface IFinalizationStore
{
    Task<FinalizationChecklist?> LoadFinalizationAsync(JobId jobId, CancellationToken token = default);

    Task SaveFinalizationConfirmationAsync(JobId jobId, FinalizationChecklist expected,
        FinalizationRuleId ruleId, FinalizationOutcome outcome, FinalizationReason? reason,
        OperatorId operatorId, CancellationToken token = default);

    Task SaveFinalizationArtifactScanAsync(JobId jobId, FinalizationChecklist expected,
        SanitizationGateDecisionId expectedGate, IReadOnlyList<WorkshopArtifactFinding> findings,
        ProvenanceKind origin, CancellationToken token = default);
}

// Values and names are persisted. Keep the meaning of historical values stable.
[JsonConverter(typeof(StableStringEnumConverter<FinalizationRuleId>))]
public enum FinalizationRuleId
{
    [JsonStringEnumMemberName("workshop_accounts")]
    WorkshopAccounts = 1,
    [JsonStringEnumMemberName("browser_sessions")]
    BrowserSessions,
    [JsonStringEnumMemberName("wifi_profiles")]
    WiFiProfiles,
    [JsonStringEnumMemberName("credentials")]
    Credentials,
    [JsonStringEnumMemberName("shares")]
    Shares,
    [JsonStringEnumMemberName("test_media")]
    TestMedia,
    [JsonStringEnumMemberName("diagnostics")]
    Diagnostics,
    [JsonStringEnumMemberName("downloads")]
    Downloads,
    [JsonStringEnumMemberName("remote_support")]
    RemoteSupport,
    [JsonStringEnumMemberName("clipboard_key_material")]
    ClipboardKeyMaterial,
    [JsonStringEnumMemberName("pending_restart")]
    PendingRestart,
}

[JsonConverter(typeof(StableStringEnumConverter<FinalizationOutcome>))]
public enum FinalizationOutcome
{
    [JsonStringEnumMemberName("unknown")]
    Unknown = 1,
    [JsonStringEnumMemberName("passed")]
    Passed,
    [JsonStringEnumMemberName("failed")]
    Failed,
    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable,
}

[JsonConverter(typeof(StableStringEnumConverter<FinalizationReason>))]
public enum FinalizationReason
{
    [JsonStringEnumMemberName("no_workshop_use")]
    NoWorkshopUse = 1,
    [JsonStringEnumMemberName("capability_absent")]
    CapabilityAbsent,
}

public enum FinalizationSource
{
    None,
    OperatorConfirmation,
    AutomatedObservation,
}

public sealed class FinalizationRule
{
    internal FinalizationRule(FinalizationRuleId id, bool isRequired, bool allowsOperatorConfirmation,
        params FinalizationReason[] allowedReasons)
    {
        Id = id;
        IsRequired = isRequired;
        AllowsOperatorConfirmation = allowsOperatorConfirmation;
        AllowedReasons = Array.AsReadOnly(allowedReasons);
    }

    public FinalizationRuleId Id { get; }
    public bool IsRequired { get; }
    public bool AllowsOperatorConfirmation { get; }
    public bool AllowsNotApplicable => AllowedReasons.Count != 0;
    public IReadOnlyList<FinalizationReason> AllowedReasons { get; }
}

public sealed record FinalizationRecord
{
    public FinalizationRecord(long sequence, FinalizationRuleId ruleId, FinalizationOutcome outcome,
        FinalizationReason? reason, OperatorId operatorId, DateTimeOffset recordedAtUtc, string evidenceVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        var rule = FinalizationChecklist.Rules.SingleOrDefault(item => item.Id == ruleId);
        if (rule is not { AllowsOperatorConfirmation: true })
        {
            throw new ArgumentException("This rule does not accept operator confirmation.", nameof(ruleId));
        }

        Outcome = DomainValue.RequireDefined(outcome, nameof(outcome));
        if (outcome == FinalizationOutcome.NotApplicable
            ? reason is null || !rule.AllowedReasons.Contains(reason.Value)
            : reason is not null)
        {
            throw new ArgumentException("The selected result requires a reason permitted by the rule.", nameof(reason));
        }

        Sequence = sequence;
        RuleId = ruleId;
        Reason = reason;
        OperatorId = operatorId ?? throw new ArgumentNullException(nameof(operatorId));
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
        EvidenceVersion = DomainValue.RequireVersion(evidenceVersion, nameof(evidenceVersion));
    }

    public long Sequence { get; }
    public FinalizationRuleId RuleId { get; }
    public FinalizationOutcome Outcome { get; }
    public FinalizationReason? Reason { get; }
    public OperatorId OperatorId { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public string EvidenceVersion { get; }
}

public sealed record FinalizationRow(FinalizationRule Rule, FinalizationOutcome Outcome, FinalizationSource Source,
    FinalizationRecord? Confirmation, bool NeedsRecheck, bool HasBlockingArtifact = false)
{
    public bool IsBlocking => Rule.IsRequired && (HasBlockingArtifact || Outcome is FinalizationOutcome.Unknown or FinalizationOutcome.Failed);
}

public sealed class FinalizationChecklist
{
    public const string RuleRevision = "M6-finalization-1";
    public const int MaximumHistoryPerJob = 512;

    public static IReadOnlyList<FinalizationRule> Rules { get; } = Array.AsReadOnly<FinalizationRule>(
    [
        new(FinalizationRuleId.WorkshopAccounts, true, true),
        new(FinalizationRuleId.BrowserSessions, true, true, FinalizationReason.NoWorkshopUse),
        new(FinalizationRuleId.WiFiProfiles, true, true, FinalizationReason.NoWorkshopUse, FinalizationReason.CapabilityAbsent),
        new(FinalizationRuleId.Credentials, true, true),
        new(FinalizationRuleId.Shares, true, true, FinalizationReason.NoWorkshopUse),
        new(FinalizationRuleId.TestMedia, true, true, FinalizationReason.NoWorkshopUse),
        new(FinalizationRuleId.Diagnostics, true, true),
        new(FinalizationRuleId.Downloads, true, true),
        new(FinalizationRuleId.RemoteSupport, true, true, FinalizationReason.NoWorkshopUse),
        new(FinalizationRuleId.ClipboardKeyMaterial, true, true),
        new(FinalizationRuleId.PendingRestart, true, false),
    ]);

    private FinalizationChecklist(string? evidenceVersion, ReadOnlyCollection<FinalizationRecord> history,
        ReadOnlyCollection<FinalizationRow> rows, ReadOnlyCollection<WorkshopArtifactScan> artifactScans)
    {
        EvidenceVersion = evidenceVersion;
        History = history;
        Rows = rows;
        ArtifactScans = artifactScans;
    }

    public string? EvidenceVersion { get; }
    public IReadOnlyList<FinalizationRecord> History { get; }
    public IReadOnlyList<FinalizationRow> Rows { get; }
    public IReadOnlyList<WorkshopArtifactScan> ArtifactScans { get; }
    public bool HasBlockingChecks => Rows.Any(row => row.IsBlocking);
    public bool CanRecord => EvidenceVersion is not null && History.Count < MaximumHistoryPerJob;
    public bool CanScanArtifacts => EvidenceVersion is not null && ArtifactScans.Count < WorkshopArtifactScan.MaximumScansPerJob;

    public string? EvidenceVersionForConfirmation(FinalizationRuleId rule) =>
        EvidenceVersionForConfirmation(EvidenceVersion, rule, ArtifactScans.Count);

    public static FinalizationChecklist Evaluate(string? evidenceVersion, IReadOnlyList<FinalizationRecord> history,
        FinalizationOutcome restartOutcome = FinalizationOutcome.Unknown,
        IReadOnlyList<WorkshopArtifactScan>? artifactScans = null)
    {
        if (evidenceVersion is not null)
        {
            DomainValue.RequireVersion(evidenceVersion, nameof(evidenceVersion));
        }

        if (restartOutcome is not (FinalizationOutcome.Unknown or FinalizationOutcome.Passed or FinalizationOutcome.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(restartOutcome));
        }

        ArgumentNullException.ThrowIfNull(history);
        if (history.Count > MaximumHistoryPerJob)
        {
            throw new ArgumentOutOfRangeException(nameof(history));
        }

        var records = history.ToArray();
        var scans = CopyArtifactScans(artifactScans);
        var openIndications = ReadOpenArtifactIndications(scans);
        var latest = new Dictionary<FinalizationRuleId, FinalizationRecord>();
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (record?.Sequence != index + 1)
            {
                throw new ArgumentException("Finalization history must be contiguous and in sequence order.", nameof(history));
            }

            latest[record.RuleId] = record;
        }

        var rows = new List<FinalizationRow>(Rules.Count);
        foreach (var rule in Rules)
        {
            if (rule.Id == FinalizationRuleId.PendingRestart)
            {
                var observed = evidenceVersion is not null && restartOutcome != FinalizationOutcome.Unknown;
                rows.Add(new(rule, observed ? restartOutcome : FinalizationOutcome.Unknown,
                    observed ? FinalizationSource.AutomatedObservation : FinalizationSource.None, null, false));
                continue;
            }

            latest.TryGetValue(rule.Id, out var confirmation);
            var current = confirmation is not null &&
                confirmation.EvidenceVersion == EvidenceVersionForConfirmation(evidenceVersion, rule.Id, scans.Length);
            var outcome = current ? confirmation!.Outcome : FinalizationOutcome.Unknown;
            var blockingArtifact = openIndications.Any(kind => ArtifactRule(kind) == rule.Id &&
                (IsFixedCleanupSource(kind) || outcome == FinalizationOutcome.NotApplicable));
            rows.Add(new(rule, outcome,
                current ? FinalizationSource.OperatorConfirmation : FinalizationSource.None,
                confirmation, confirmation is not null && !current, blockingArtifact));
        }

        return new(evidenceVersion, Array.AsReadOnly(records), rows.AsReadOnly(), Array.AsReadOnly(scans));
    }

    private static string? EvidenceVersionForConfirmation(string? evidenceVersion, FinalizationRuleId rule, int scanCount)
    {
        if (evidenceVersion is null || scanCount == 0 || rule is not (FinalizationRuleId.WorkshopAccounts or
            FinalizationRuleId.Shares or FinalizationRuleId.Diagnostics or FinalizationRuleId.Downloads or FinalizationRuleId.RemoteSupport))
        {
            return evidenceVersion;
        }

        var context = string.Create(CultureInfo.InvariantCulture, $"{evidenceVersion}:workshop-artifact-scan:{scanCount}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
    }

    private static WorkshopArtifactScan[] CopyArtifactScans(IReadOnlyList<WorkshopArtifactScan>? history)
    {
        if (history is null || history.Count == 0) { return []; }
        if (history.Count > WorkshopArtifactScan.MaximumScansPerJob)
        {
            throw new ArgumentOutOfRangeException(nameof(history));
        }

        var scans = history.ToArray();
        for (var index = 0; index < scans.Length; index++)
        {
            if (scans[index]?.Sequence != index + 1 || scans[index].Origin != scans[0].Origin)
            {
                throw new ArgumentException("Artifact scan history must be contiguous and attributed to one evidence origin.", nameof(history));
            }
        }

        return scans;
    }

    private static HashSet<WorkshopArtifactKind> ReadOpenArtifactIndications(WorkshopArtifactScan[] scans)
    {
        var open = new HashSet<(WorkshopArtifactKind Kind, string Revision)>();
        foreach (var scan in scans)
        {
            foreach (var finding in scan.Findings)
            {
                var source = (finding.Kind, scan.DetectorRevision);
                if (finding.Status == WorkshopArtifactStatus.Present)
                {
                    open.Add(source);
                }
                else if (finding.Status == WorkshopArtifactStatus.AbsentInCheckedScope &&
                    IsFixedCleanupSource(finding.Kind))
                {
                    // Only unchanged fixed local path detectors have comparable scopes across scans.
                    open.Remove(source);
                }
            }
        }

        return open.Select(source => source.Kind).ToHashSet();
    }

    private static bool IsFixedCleanupSource(WorkshopArtifactKind kind) =>
        kind is WorkshopArtifactKind.BatteryReportResidue or WorkshopArtifactKind.PackageStagingResidue;

    private static FinalizationRuleId ArtifactRule(WorkshopArtifactKind kind) => kind switch
    {
        WorkshopArtifactKind.BatteryReportResidue or WorkshopArtifactKind.WorkshopReportCopies => FinalizationRuleId.Diagnostics,
        WorkshopArtifactKind.PackageStagingResidue => FinalizationRuleId.Downloads,
        WorkshopArtifactKind.ConfiguredLocalAccounts => FinalizationRuleId.WorkshopAccounts,
        WorkshopArtifactKind.ConfiguredSmbShares => FinalizationRuleId.Shares,
        _ => FinalizationRuleId.RemoteSupport,
    };
}
