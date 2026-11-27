using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Diagnostics.Redaction;
using ThirdLife.Inventory;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.Verification;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private async Task<SupportInputs> ReadSupportInputsAsync(JobId jobId, CancellationToken token)
    {
        var finalization = await ReadSupportFinalizationAsync(jobId, token);
        var job = await LoadJobAsync(jobId, token);
        if (IsSeparateEvidenceOrigin(job))
        {
            throw new InvalidOperationException("The saved job belongs to a different evidence scenario.");
        }

        var assessment = await Task.Run(() => AssessmentOrchestrator.ReadLatest(job), token);
        var profile = await ReadProfileVerificationForReportAsync(jobId, token);
        var journals = await ReadPreparationJournalsAsync(jobId, token);
        var confirmed = await ReadSupportFinalizationAsync(jobId, token);
        if (finalization?.EvidenceVersion != confirmed?.EvidenceVersion ||
            finalization?.History.Count != confirmed?.History.Count)
        {
            throw new JobStoreConflictException();
        }

        token.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var gate = SanitizationGate.Inspect(job);
        var currentAssessment = finalization?.EvidenceVersion is not null && gate.AllowsAssessment &&
            assessment is { Outcome: AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview, FinishedAtUtc: { } finished } &&
            finished <= now && assessment.GateDecisionId == gate.Decision!.DecisionId &&
            assessment.StartedAtUtc >= gate.Decision.EvaluatedAtUtc ? assessment : null;
        var policyFacts = currentAssessment is null ? [] : AuditPolicyEvidence.Create(job, currentAssessment, now);
        var profileResult = profile is { IsCurrent: true } ? profile.Record.Result : null;
        var facts = ProjectSupportFacts(currentAssessment?.Observations ?? [], policyFacts, profileResult);
        var checks = ProjectSupportChecks(policyFacts, profileResult, finalization);
        var outcomes = ReadPackageRecords(journals, includeEarlierAttempts: true).Select(ProjectSupportPackage)
            .OfType<StructuredDiagnosticEvent>().TakeLast(SupportArchivePreview.MaximumRecords - checks.Length).ToArray();
        var checkpoint = job.Checkpoints.Count == 0 ? null : job.Checkpoints[^1].CheckpointId;
        return new(new(checkpoint, finalization?.EvidenceVersion,
            finalization?.History.Count ?? 0), facts, checks, outcomes);
    }

    private Task<FinalizationChecklist?> ReadSupportFinalizationAsync(JobId jobId, CancellationToken token) =>
        _store is IFinalizationStore store
            ? Task.Run(() => store.LoadFinalizationAsync(jobId, token), token)
            : Task.FromResult<FinalizationChecklist?>(null);

    private static SupportMachineFacts ProjectSupportFacts(IReadOnlyList<Observation> raw,
        IReadOnlyList<Observation> policy, ProfileVerificationResult? verification)
    {
        var build = ReadSupportFact(raw, "os.build", "windows-operating-system", "os.dotnet.environment.version")?.IntegerValue;
        var memory = ReadSupportFact(policy, "memory.installed_bytes")?.IntegerValue;
        var architecture = ReadSupportFact(policy, "system.architecture")?.StringValue == "x64";
        var storage = ProjectSupportStorage(raw);
        return new(verification?.WindowsBuild ?? (build is >= 22_000 and <= 99_999 ? (int?)build : null),
            architecture, memory, storage);
    }

    private static SupportStorageKind? ProjectSupportStorage(IReadOnlyList<Observation> observations)
    {
        const string provider = "windows-storage";
        const string disk = "storage.msft.physical_disk";
        if (ReadSupportFact(observations, "storage.physical_disk_count", provider, disk)?.IntegerValue != 1)
        {
            return null;
        }

        var bus = ReadSupportFact(observations, "storage.physical_disk.bus_type", provider, disk + ".00")?.StringValue;
        var media = ReadSupportFact(observations, "storage.physical_disk.media_type", provider, disk + ".00")?.StringValue;
        return (bus, media) switch
        {
            ("nvme", _) => SupportStorageKind.Nvme,
            ("mmc", "ssd") => SupportStorageKind.Emmc,
            (_, "ssd") => SupportStorageKind.Ssd,
            (_, "hdd") => SupportStorageKind.Hdd,
            _ => null,
        };
    }

    private static EvidenceValue? ReadSupportFact(IReadOnlyList<Observation> facts, string key,
        string? provider = null, string? source = null)
    {
        var matches = facts.Where(fact => fact.EvidenceKey.Value == key).Take(2).ToArray();
        if (matches is not [{ LimitationCode: null } observed] || (provider is not null &&
            (observed.Metadata.ProviderId.Value != provider || observed.Metadata.Provenance.SourceReference != source ||
            observed.Metadata.Provenance.Kind is not (ProvenanceKind.ProviderObservation or ProvenanceKind.SyntheticFixture))))
        {
            return null;
        }

        return observed.Metadata.ValueAvailability == ValueAvailability.Available ? observed.Value : null;
    }

    private static SupportCheck[] ProjectSupportChecks(IReadOnlyList<Observation> policy,
        ProfileVerificationResult? verification, FinalizationChecklist? finalization)
    {
        var os = ReadSupportFact(policy, "operating_system.support_state")?.StringValue switch
        {
            "supported" => SupportCheckOutcome.Passed,
            "unsupported" or "audit_only" => SupportCheckOutcome.Failed,
            _ => SupportCheckOutcome.NotAvailable,
        };
        var activation = ReadSupportFact(policy, "activation.state")?.StringValue switch
        {
            "activated" => SupportCheckOutcome.Passed,
            "unactivated" or "grace_period" or "notification" => SupportCheckOutcome.Failed,
            _ => SupportCheckOutcome.NotAvailable,
        };
        var pendingRestart = ReadSupportFact(policy, "reboot.pending_status")?.StringValue == "pending_indicated"
            ? SupportCheckOutcome.Failed : SupportCheckOutcome.NotAvailable;
        var workshop = finalization?.EvidenceVersion is null ? SupportCheckOutcome.NotAvailable :
            finalization.Rows.Any(row => row.IsBlocking && row.Outcome == FinalizationOutcome.Failed) ? SupportCheckOutcome.Failed :
            finalization.HasBlockingChecks ? SupportCheckOutcome.Unknown : SupportCheckOutcome.Passed;
        return
        [
            new(SupportCheckId.OperatingSystem, ProfileCheck(ProfileCheckKind.OperatingSystemSupport, os)),
            new(SupportCheckId.Activation, ProfileCheck(ProfileCheckKind.Activation, activation)),
            new(SupportCheckId.WindowsUpdate, ProfileCheck(ProfileCheckKind.WindowsUpdate)),
            new(SupportCheckId.PendingRestart, ProfileCheck(ProfileCheckKind.PendingRestart, pendingRestart)),
            new(SupportCheckId.SystemDiskHealth, ProfileCheck(ProfileCheckKind.SystemStorageHealth)),
            new(SupportCheckId.SecurityBaseline, Aggregate(
                ProfileCheck(ProfileCheckKind.Firewall), ProfileCheck(ProfileCheckKind.Antivirus),
                ProfileCheck(ProfileCheckKind.Defender), ProfileCheck(ProfileCheckKind.UserAccountControl),
                ProfileCheck(ProfileCheckKind.TimeSynchronization))),
            new(SupportCheckId.EssentialCapabilities, Aggregate(verification?.Capabilities.Where(item => item.Essential)
                .Select(item => Map(item.State)).ToArray() ?? [])),
            new(SupportCheckId.WorkshopFinalization, workshop),
        ];

        SupportCheckOutcome ProfileCheck(ProfileCheckKind kind, SupportCheckOutcome fallback = SupportCheckOutcome.NotAvailable) =>
            verification?.Checks.FirstOrDefault(check => check.Kind == kind) is { } result ? Map(result.State) : fallback;

        static SupportCheckOutcome Map(ProfileVerificationState state) => state switch
        {
            ProfileVerificationState.Passed => SupportCheckOutcome.Passed,
            ProfileVerificationState.Failed => SupportCheckOutcome.Failed,
            ProfileVerificationState.Stale => SupportCheckOutcome.Unknown,
            _ => SupportCheckOutcome.NotAvailable,
        };

        static SupportCheckOutcome Aggregate(params SupportCheckOutcome[] outcomes) =>
            outcomes.Contains(SupportCheckOutcome.Failed) ? SupportCheckOutcome.Failed :
            outcomes.Length > 0 && outcomes.All(outcome => outcome == SupportCheckOutcome.Passed) ? SupportCheckOutcome.Passed :
            outcomes.Any(outcome => outcome != SupportCheckOutcome.NotAvailable) ? SupportCheckOutcome.Unknown :
            SupportCheckOutcome.NotAvailable;
    }

    private static StructuredDiagnosticEvent? ProjectSupportPackage(WorkshopPackageRecord package)
    {
        (DiagnosticPhase Phase, DiagnosticResultCode? Result) outcome = package.Outcome switch
        {
            WorkshopPackageOutcome.Failed => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerFailed),
            WorkshopPackageOutcome.CancelledBeforeDispatch => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageCancelled),
            WorkshopPackageOutcome.NetworkUnavailable => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageDownloadNetworkUnavailable),
            WorkshopPackageOutcome.TimedOut => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerTimedOut),
            WorkshopPackageOutcome.NoFinalResult or WorkshopPackageOutcome.Cancelled =>
                (DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerInterrupted),
            WorkshopPackageOutcome.RestartRequired => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageRestartRequired),
            WorkshopPackageOutcome.TrustChanged => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageTrustMismatch),
            WorkshopPackageOutcome.AuthorizationDeclined => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageElevationDeclined),
            WorkshopPackageOutcome.ElevationUnavailable => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageElevationUnavailable),
            WorkshopPackageOutcome.PreconditionChanged => (DiagnosticPhase.Execute, DiagnosticResultCode.PackagePreconditionChanged),
            WorkshopPackageOutcome.DownloadUnavailable => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageDownloadUnavailable),
            WorkshopPackageOutcome.DownloadTimedOut => (DiagnosticPhase.Execute, DiagnosticResultCode.PackageDownloadTimedOut),
            WorkshopPackageOutcome.DetectionAbsent => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionAbsent),
            WorkshopPackageOutcome.DetectionMismatch => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionMismatch),
            WorkshopPackageOutcome.DetectionUnavailable => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageDetectionUnavailable),
            WorkshopPackageOutcome.ProbeExecutableMissing => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExecutableMissing),
            WorkshopPackageOutcome.ProbeExecutableUntrusted => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExecutableUntrusted),
            WorkshopPackageOutcome.ProbeLaunchFailed => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchFailed),
            WorkshopPackageOutcome.ProbeExitedEarly => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchExitedEarly),
            WorkshopPackageOutcome.ProbeTimedOut => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchTimedOut),
            WorkshopPackageOutcome.ProbeCancelled => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageVerificationCancelled),
            WorkshopPackageOutcome.ProbeCleanupUnconfirmed => (DiagnosticPhase.Verify, DiagnosticResultCode.PackageLaunchCleanupUnconfirmed),
            _ => (default, null),
        };
        return outcome.Result is { } code ? PackageDiagnostic.Create(outcome.Phase, code, DiagnosticCorrelationId.CreateRandom()) : null;
    }

    private sealed record SupportInputVersion(CheckpointId? LastJobCheckpoint, string? EvidenceVersion, int Confirmations);

    private sealed record SupportInputs(SupportInputVersion Version, SupportMachineFacts Facts, IReadOnlyList<SupportCheck> Checks,
        IReadOnlyList<StructuredDiagnosticEvent> PackageOutcomes);
}
