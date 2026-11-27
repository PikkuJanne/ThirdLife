using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Windows;
using ThirdLife.Persistence;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private readonly WorkshopArtifactDetector _artifactDetector = new();
    private CancellationTokenSource? _artifactScanCancellation;
    private string _artifactScanFeedback = string.Empty;

    internal Func<string[], string[], CancellationToken, Task<IReadOnlyList<WorkshopArtifactFinding>>>? FixtureArtifactScan { get; init; }

    public bool CanScanArtifacts => CanAssess && _showFinalization && !_readingFinalization &&
        _completedFinalizationReadVersion == _finalizationReadVersion && Finalization is { CanScanArtifacts: true } &&
        (!IsFixtureScenario || FixtureArtifactScan is not null && Job is { SanitizationEvidence.Count: > 0 });

    public bool CanCancelArtifactScan => _artifactScanCancellation is { IsCancellationRequested: false };

    public string ArtifactScanStatus
    {
        get
        {
            if (_artifactScanFeedback.Length != 0) { return _artifactScanFeedback; }
            if (Finalization is not { } checklist) { return "Open a workshop job to review its scans."; }
            if (checklist.ArtifactScans.Count >= WorkshopArtifactScan.MaximumScansPerJob)
            {
                return "The scan history has reached its limit. Saved findings remain available for review.";
            }

            if (checklist.ArtifactScans.Count == 0)
            {
                return Gate is { AllowsAssessment: true } ? "No workshop artifact scan is saved for this job."
                    : "Complete the sanitization record before scanning this device.";
            }
            var latest = checklist.ArtifactScans[^1];
            var state = latest.EvidenceVersion == checklist.EvidenceVersion ? "Current job context" : "Earlier job context";
            return string.Create(CultureInfo.InvariantCulture, $"{state} · {latest.RecordedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        }
    }

    public string ArtifactScanFindings => Finalization is not { ArtifactScans.Count: > 0 } checklist ? string.Empty :
        string.Join(Environment.NewLine, checklist.ArtifactScans[^1].Findings.Select(finding =>
            $"{ArtifactSourceLabel(finding.Kind)} · {ArtifactStatusLabel(finding.Status)}"));

    public Task ScanArtifactsAsync(string[] accountNames, string[] shareNames)
    {
        if (!CanScanArtifacts || Finalization is not { } expected || _store is not IFinalizationStore store ||
            Gate?.Decision?.DecisionId is not { } gate)
        {
            return Task.CompletedTask;
        }

        var jobStore = _store;

        return RunAsync(async () =>
        {
            var job = RequireWritableJob();
            using var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            _artifactScanCancellation = cancellation;
            _artifactScanFeedback = "Checking selected local workshop artifacts.";
            Publish();
            try
            {
                await Task.Run(() => jobStore.RequireSanitizationGateAsync(job.Job.JobId, gate, token), token);
                var findings = IsFixtureScenario && FixtureArtifactScan is { } fixture
                    ? await fixture(accountNames, shareNames, token)
                    : await _artifactDetector.DetectAsync(accountNames, shareNames, token);
                var origin = IsFixtureScenario ? ProvenanceKind.SyntheticFixture : ProvenanceKind.ProviderObservation;
                await Task.Run(() => store.SaveFinalizationArtifactScanAsync(job.Job.JobId, expected, gate, findings, origin, token), token);
                _artifactScanFeedback = string.Empty;
                ClearReport();
            }
            catch (ArgumentException)
            {
                _artifactScanFeedback = "Use up to 16 distinct exact local names per list. Enter an account name of up to 20 characters or a share name of up to 80 characters on each line.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _artifactScanFeedback = "The scan was cancelled. Review the saved results before retrying.";
            }
            catch (JobStoreConflictException)
            {
                _artifactScanFeedback = "The saved job changed during the scan. Review the current checklist and scan again.";
            }
            catch (JobLifecycleException)
            {
                _artifactScanFeedback = "The saved sanitization decision changed. Review intake before scanning the device.";
            }
            finally
            {
                _artifactScanCancellation = null;
            }
        });
    }

    public void CancelArtifactScan()
    {
        if (!CanCancelArtifactScan) { return; }
        _cancellationRequest = _artifactScanCancellation!.CancelAsync();
        Publish();
    }

    public void ReportOversizedArtifactTargets()
    {
        _artifactScanFeedback = "The target list is too long. Enter up to 16 exact workshop names per list.";
        Publish();
    }

    internal static string ArtifactSourceLabel(WorkshopArtifactKind kind) => kind switch
    {
        WorkshopArtifactKind.BatteryReportResidue => "Temporary battery-report files",
        WorkshopArtifactKind.WorkshopReportCopies => "Protected workshop report copies",
        WorkshopArtifactKind.PackageStagingResidue => "Package staging folders",
        WorkshopArtifactKind.ConfiguredLocalAccounts => "Specified local accounts",
        WorkshopArtifactKind.ConfiguredSmbShares => "Specified local SMB shares",
        WorkshopArtifactKind.RemoteDesktopSetting => "Local Remote Desktop setting",
        WorkshopArtifactKind.SolicitedRemoteAssistancePolicy => "Solicited Remote Assistance policy",
        WorkshopArtifactKind.OfferRemoteAssistancePolicy => "Offer Remote Assistance policy",
        _ => "Workshop artifact source",
    };

    private static string ArtifactStatusLabel(WorkshopArtifactStatus status) => status switch
    {
        WorkshopArtifactStatus.Present => "Indication found. Review in the relevant workshop check.",
        WorkshopArtifactStatus.AbsentInCheckedScope => "No indication in the checked scope.",
        WorkshopArtifactStatus.TargetsNotConfigured => "No exact names entered. Review manually.",
        WorkshopArtifactStatus.AccessDenied => "Access denied. Review manually.",
        WorkshopArtifactStatus.UnsafePathOrEntry => "The expected local path or entry could not be verified. Review manually.",
        WorkshopArtifactStatus.ScanLimitReached => "The search reached its resource limit. Review manually.",
        WorkshopArtifactStatus.TimedOut => "The search reached its time limit. Review manually.",
        _ => "The source was unavailable. Review manually.",
    };
}
