using System.Reflection;
using System.Text;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Repairs;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Reports.WorkshopReport;

public sealed record WorkshopPlanApproval(Guid ApprovalId, OperatorId OperatorId, DateTimeOffset OccurredAtUtc);

public sealed record WorkshopPlanProvenance(
    JobId JobId,
    string PlanSha256,
    string ImpactSha256,
    string CatalogSha256,
    string ProfileSha256,
    string PolicySha256,
    string EvidenceSha256,
    bool SyntheticData,
    IReadOnlyList<WorkshopPlanApproval> Approvals);

public sealed record WorkshopFinalizationReview(JobId JobId, FinalizationChecklist Checklist);

public sealed record WorkshopBackupSampleReview(JobId JobId, FileHistorySampleAuditState? State, bool EarlierHandover);

public sealed record WorkshopRecordDetails(IReadOnlyList<WorkshopPlanProvenance> Plans,
    WorkshopFinalizationReview? Finalization = null,
    WorkshopBackupSampleReview? BackupSample = null);

public static partial class WorkshopReportRenderer
{
    private static readonly string RendererVersion = ReadRendererVersion();

    public static string RenderTechnicalRecord(StoredJob job, WorkshopAssessmentSummary? assessment,
        StoredPolicyDecision? savedDecision, IReadOnlyList<WorkshopPackageRecord> packages,
        IReadOnlyList<WorkshopPreparationPlan>? preparation, WorkshopProfileVerification? profileVerification,
        WorkshopRecordDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ValidatePackageRecords(packages, savedDecision, assessment);
        return RenderCore(job, assessment, savedDecision, packages, preparation, profileVerification, details, cancellationToken);
    }

    private static string ReadRendererVersion()
    {
        var assembly = typeof(WorkshopReportRenderer).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString();
        return version is { Length: > 0 and <= 128 } &&
            version.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+')
                ? version : "Unavailable";
    }

    private static void WriteRecordProvenance(StringBuilder report, JobId jobId,
        IReadOnlyList<WorkshopPlanProvenance> plans, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plans);
        if (plans.Count > 128 || plans.Sum(plan => (long)plan.Approvals.Count) > 128 ||
            plans.Any(plan => plan.JobId != jobId || !IsRecordDigest(plan.PlanSha256) || !IsRecordDigest(plan.ImpactSha256) ||
                !IsRecordDigest(plan.PolicySha256) || !IsRecordDigest(plan.CatalogSha256) ||
                !IsRecordDigest(plan.ProfileSha256) || !IsRecordDigest(plan.EvidenceSha256)))
        {
            throw new ArgumentException("The workshop record requires bounded, job-bound plan provenance.", nameof(plans));
        }

        Line(report, "");
        Line(report, "Record provenance");
        Line(report, $"Renderer build {RendererVersion}.");
        Line(report, "Handover eligibility and sign-off require a separate review of current evidence.");
        if (plans.Count == 0)
        {
            Line(report, "No plan is saved for this job.");
            return;
        }

        foreach (var plan in plans.OrderBy(plan => plan.PlanSha256, StringComparer.Ordinal)
            .ThenBy(plan => plan.ImpactSha256, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Line(report, $"- Saved plan SHA-256 {plan.PlanSha256}{(plan.SyntheticData ? " (synthetic test)" : "")}.");
            Line(report, $"  Impact SHA-256 {plan.ImpactSha256}.");
            Line(report, $"  Catalog SHA-256 {plan.CatalogSha256}.");
            Line(report, $"  Profile SHA-256 {plan.ProfileSha256}.");
            Line(report, $"  Policy SHA-256 {plan.PolicySha256}.");
            Line(report, $"  Reviewed evidence SHA-256 {plan.EvidenceSha256}.");
            if (plan.Approvals.Count == 0)
            {
                Line(report, "  No operator approval is saved for this plan.");
            }

            foreach (var approval in plan.Approvals.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.ApprovalId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Line(report, $"  Operator approval {approval.ApprovalId:D} at {Timestamp(approval.OccurredAtUtc)}.");
                Line(report, $"  Workshop operator code {SafeCode(approval.OperatorId.Value)}.");
            }
        }
    }

    private static bool IsRecordDigest(string value) => value is { Length: 64 } &&
        value.All(static character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static void WriteBackupSampleReview(StringBuilder report, JobId jobId, WorkshopBackupSampleReview? review)
    {
        if (review is null) { return; }
        if (review.JobId != jobId)
        {
            throw new ArgumentException("The backup sample belongs to a different job.", nameof(review));
        }

        Line(report, "");
        Line(report, "Backup sample");
        if (review.State.HasValue)
        {
            Line(report, review.EarlierHandover ? "Sample evidence is from an earlier handover." : "Sample evidence is for this handover.");
        }
        Line(report, review.State switch
        {
            FileHistorySampleAuditState.Preparing => "Sample preparation remains unconfirmed.",
            FileHistorySampleAuditState.Prepared => "A protected harmless sample was prepared.",
            FileHistorySampleAuditState.PreparationUnavailable => "Sample preparation was unavailable.",
            FileHistorySampleAuditState.CopyUnavailable => "The protected recovered copy was unavailable.",
            FileHistorySampleAuditState.CopyMismatch => "The recovered copy did not match the prepared sample.",
            FileHistorySampleAuditState.CopyMatches => "The separate recovered copy matched the prepared sample.",
            _ => "Saved sample evidence is unavailable.",
        });
        Line(report, "Backup setup remains pending. Windows File History restoration has no verified record.");
    }

    private static void WriteFinalizationReview(StringBuilder report, JobId jobId, WorkshopFinalizationReview? review,
        CancellationToken cancellationToken)
    {
        Line(report, "");
        Line(report, "Workshop finalization");
        if (review is null)
        {
            Line(report, "No checklist result is available. Required workshop checks remain unverified.");
            return;
        }

        if (review.JobId != jobId)
        {
            throw new ArgumentException("The finalization review belongs to a different job.", nameof(review));
        }

        var checklist = review.Checklist;
        Line(report, $"Rules {FinalizationChecklist.RuleRevision}.");
        Line(report, checklist.EvidenceVersion is { } version ? $"Current evidence version {SafeCode(version)}."
            : "Current finalization evidence is unavailable.");
        Line(report, checklist.HasBlockingChecks ? "Required finalization checks remain open."
            : "The checklist records no open required finalization checks.");
        foreach (var row in checklist.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requirement = row.IsBlocking ? "Required, blocks handover." : row.Rule.IsRequired ? "Required." : "Advisory.";
            var source = row.NeedsRecheck ? "Needs recheck after a change to the reviewed evidence."
                : row.Source switch
                {
                    FinalizationSource.OperatorConfirmation => "Operator confirmation.",
                    FinalizationSource.AutomatedObservation => "Automated observation.",
                    _ => "No current result recorded.",
                };
            Line(report, $"- {DescribeFinalizationRule(row.Rule.Id)}. {requirement} {source} {DescribeFinalizationOutcome(row.Outcome)}");
            if (row.HasBlockingArtifact)
            {
                Line(report, "  A saved workshop artifact indication still needs resolution.");
            }
        }

        Line(report, "");
        Line(report, "Saved operator confirmations");
        if (checklist.History.Count == 0)
        {
            Line(report, "No operator confirmation is saved.");
        }

        var currentConfirmations = checklist.Rows
            .Where(row => row.Source == FinalizationSource.OperatorConfirmation && row.Confirmation is not null)
            .Select(row => row.Confirmation!.Sequence).ToHashSet();
        foreach (var record in checklist.History)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = currentConfirmations.Contains(record.Sequence) ? "Current" : "Earlier or superseded";
            Line(report, $"- {scope} confirmation for {DescribeFinalizationRule(record.RuleId)}. {DescribeFinalizationOutcome(record.Outcome)}");
            if (record.Reason is { } reason)
            {
                Line(report, $"  Reason {DescribeFinalizationReason(reason)}.");
            }

            Line(report, $"  Workshop operator code {SafeCode(record.OperatorId.Value)} at {Timestamp(record.RecordedAtUtc)}.");
            Line(report, $"  Evidence version {SafeCode(record.EvidenceVersion)}.");
        }

        WriteArtifactScanHistory(report, checklist, cancellationToken);
    }

    private static void WriteArtifactScanHistory(StringBuilder report, FinalizationChecklist checklist,
        CancellationToken cancellationToken)
    {
        if (checklist.ArtifactScans.Count == 0) { return; }

        Line(report, "");
        Line(report, "Workshop artifact scans");
        Line(report, "Each absence applies to its checked source and scope. Required workshop confirmations remain separate.");
        var latestSequence = checklist.ArtifactScans[^1].Sequence;
        foreach (var scan in checklist.ArtifactScans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool current = scan.Sequence == latestSequence && scan.EvidenceVersion == checklist.EvidenceVersion &&
                scan.DetectorRevision == WorkshopArtifactScan.CurrentDetectorRevision;
            var context = current ? "Current" : "Earlier or superseded";
            var origin = scan.Origin == ProvenanceKind.SyntheticFixture ? "Synthetic fixture" : "Automated local observation";
            Line(report, $"- {context} scan at {Timestamp(scan.RecordedAtUtc)}. {origin}. Detector {SafeCode(scan.DetectorRevision)}.");
            foreach (var finding in scan.Findings.OrderBy(finding => finding.Kind))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Line(report, $"  {DescribeWorkshopArtifactKind(finding.Kind)}. {DescribeWorkshopArtifactStatus(finding.Status)}");
            }
        }
    }

    public static string DescribeWorkshopArtifactKind(WorkshopArtifactKind kind) => kind switch
    {
        WorkshopArtifactKind.BatteryReportResidue => "Temporary battery-report files",
        WorkshopArtifactKind.WorkshopReportCopies => "Workshop report copies",
        WorkshopArtifactKind.PackageStagingResidue => "Package staging folders",
        WorkshopArtifactKind.ConfiguredLocalAccounts => "Specified local accounts",
        WorkshopArtifactKind.ConfiguredSmbShares => "Specified local SMB shares",
        WorkshopArtifactKind.RemoteDesktopSetting => "Local Remote Desktop setting",
        WorkshopArtifactKind.SolicitedRemoteAssistancePolicy => "Solicited Remote Assistance policy",
        WorkshopArtifactKind.OfferRemoteAssistancePolicy => "Offer Remote Assistance policy",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string DescribeWorkshopArtifactStatus(WorkshopArtifactStatus status) => status switch
    {
        WorkshopArtifactStatus.Present => "Indication found.",
        WorkshopArtifactStatus.AbsentInCheckedScope => "No indication in the checked scope.",
        WorkshopArtifactStatus.TargetsNotConfigured => "No exact names entered.",
        WorkshopArtifactStatus.AccessDenied => "Access denied.",
        WorkshopArtifactStatus.UnsafePathOrEntry => "The expected local path or entry could not be verified.",
        WorkshopArtifactStatus.ScanLimitReached => "The search reached its resource limit.",
        WorkshopArtifactStatus.TimedOut => "The search reached its time limit.",
        WorkshopArtifactStatus.Unavailable => "The source was unavailable.",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string DescribeFinalizationRule(FinalizationRuleId rule) => rule switch
    {
        FinalizationRuleId.WorkshopAccounts => "Workshop accounts",
        FinalizationRuleId.BrowserSessions => "Browser sessions",
        FinalizationRuleId.WiFiProfiles => "Wi-Fi profiles",
        FinalizationRuleId.Credentials => "Workshop credentials",
        FinalizationRuleId.Shares => "Workshop shares",
        FinalizationRuleId.TestMedia => "Test media",
        FinalizationRuleId.Diagnostics => "Diagnostic exports",
        FinalizationRuleId.Downloads => "Downloads and package residue",
        FinalizationRuleId.RemoteSupport => "VPN and remote support",
        FinalizationRuleId.ClipboardKeyMaterial => "Clipboard and key material",
        FinalizationRuleId.PendingRestart => "Pending restart",
        _ => throw new ArgumentOutOfRangeException(nameof(rule)),
    };

    private static string DescribeFinalizationOutcome(FinalizationOutcome outcome) => outcome switch
    {
        FinalizationOutcome.Passed => "Checked and clear.",
        FinalizationOutcome.Failed => "Problem found.",
        FinalizationOutcome.NotApplicable => "Not applicable.",
        FinalizationOutcome.Unknown => "Unconfirmed.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    public static string DescribeFinalizationReason(FinalizationReason reason) => reason switch
    {
        FinalizationReason.NoWorkshopUse => "The workshop did not use this",
        FinalizationReason.CapabilityAbsent => "The capability is absent",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static void WriteRepairHistory(StringBuilder report, StoredJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (repairs, retests) = RepairJournal.GetHistory(job);
        var retestsByRepair = retests.ToLookup(retest => retest.RepairId);
        Line(report, "Repair and retest records");
        if (repairs.Count == 0)
        {
            Line(report, "No repair record is saved.");
        }

        foreach (var repair in repairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = repair.Component == RepairComponent.Battery ? "battery" : "system storage";
            var action = repair.Action == RepairAction.Replaced ? "replacement" : "repair";
            Line(report, $"- Operator recorded {component} {action} at {Timestamp(repair.RecordedAtUtc)}.");
            Line(report, $"  Workshop operator code {SafeCode(repair.OperatorId.Value)}. Repair record {SafeCode(repair.RepairId.Value)}.");
            Line(report, $"  Earlier assessment {SafeCode(repair.PriorAssessmentId)}. Failed evidence {SafeCode(repair.FailedEvidenceId.Value)}.");
            if (repair.SanitizationInvalidationEvidenceId is { } invalidation)
            {
                Line(report, $"  Storage replacement reopened the sanitization gate. Evidence ID {SafeCode(invalidation.Value)}.");
            }

            foreach (var retest in retestsByRepair[repair.RepairId])
            {
                cancellationToken.ThrowIfCancellationRequested();
                Line(report, $"  Retest assessment {SafeCode(retest.AssessmentId)} started at {Timestamp(retest.StartedAtUtc)}.");
            }

            if (!retestsByRepair.Contains(repair.RepairId))
            {
                Line(report, "  No linked retest is saved.");
            }
        }

        Line(report, "");
    }
}
