using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Policy.Schema;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.Verification;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private CancellationTokenSource? _profileVerificationCancellation;
    private SavedProfileVerification? _profileVerification;
    private string _profileVerificationFeedback = string.Empty;

    public bool ShowsProfileVerification => !IsFixtureScenario && !_hasSeparateEvidenceOrigin &&
        (_maintenance?.Phase == MaintenancePhase.FinalVerification || _profileVerification is not null);

    public bool CanRunProfileVerification => ShowsProfileVerification && CanScanWindowsUpdates &&
        _maintenance is { Phase: MaintenancePhase.FinalVerification } && _maintenanceCurrent && !_maintenanceUnavailable &&
        !_packagePhaseNeedsRecovery && SavedServicingApproval is null && _store is IProfileVerificationStore;

    public bool IsProfileVerificationRunning => _profileVerificationCancellation is not null;
    public bool CanCancelProfileVerification => _profileVerificationCancellation is { IsCancellationRequested: false };

    public string ProfileVerificationStatus => _profileVerificationFeedback.Length != 0 ? _profileVerificationFeedback :
        ProjectProfileVerification(_profileVerification) is { } saved
            ? WorkshopReportRenderer.DescribeProfileVerification(saved)
            : "Run final profile verification to check the current machine. The check queries the configured Windows Update service.";

    public string ProfileVerificationDetails => ProjectProfileVerification(_profileVerification) is { } saved
        ? WorkshopReportRenderer.DescribeProfileVerificationDetails(saved) : string.Empty;

    public bool HasProfileVerificationDetails => ProfileVerificationDetails.Length != 0;

    public Task RunProfileVerificationAsync()
    {
        if (!CanRunProfileVerification || _maintenance is not { } checkpoint ||
            _maintenanceJournal is not { } history || history.Entries.LastOrDefault() is not { } packageBoundary ||
            Assessment is not { } original || UpdateAgent is not { } agent ||
            _store is not IProfileVerificationStore verificationStore || _store is not IWindowsUpdateJournal updates ||
            VisiblePlanProfiles.SingleOrDefault(option => option.Profile.ContentSha256 == history.Plan.ProfileSha256 &&
                option.Catalog.ContentSha256 == history.Plan.CatalogSha256) is not { } option ||
            WorkshopPolicyCandidate.ForDeviceClass(PolicyDeviceClass.Laptop) is not { } baseline)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var jobId = checkpoint.Plan.JobId;
            _profileVerificationCancellation = new CancellationTokenSource();
            var token = _profileVerificationCancellation.Token;
            _profileVerificationFeedback = "Reading current Windows, update, package and device evidence.";
            Publish();
            try
            {
                await Task.Run(async () =>
                {
                    var journals = await ReadPreparationJournalsAsync(jobId, token);
                    var entryCount = journals.Sum(item => item.Entries.Length);
                    var started = _timeProvider.GetUtcNow();
                    var workflow = new WindowsUpdateWorkflow(updates, agent, _timeProvider);
                    var scan = await workflow.ScanAsync(jobId, original.GateDecisionId, token);
                    token.ThrowIfCancellationRequested();
                    var updateHistory = await updates.LoadWindowsUpdatesAsync(jobId, token);
                    var collected = await _orchestrator!.VerifyAsync(jobId, original.GateDecisionId, token);
                    var packageChecks = ReadProfilePackages(history, option, token);
                    var job = await LoadJobAsync(jobId, token);
                    var finished = _timeProvider.GetUtcNow();
                    var evidence = AuditPolicyEvidence.CreateForFinalVerification(job, collected, original, finished);
                    var update = scan.Scan is { } observed ? new ProfileUpdateCheck(observed, scan.OccurredAtUtc,
                        WindowsUpdateHistory.RecoveryBeforePackages(updateHistory) != UpdateRecovery.None) : null;
                    var optional = history.Plan.Actions.Where(action => action.Intent == CapabilityIntent.Optional)
                        .Select(action => action.CapabilityId).ToArray();
                    var result = ProfileVerification.Evaluate(started, finished, option.Profile, collected.Observations,
                        evidence, baseline.Document, update, packageChecks, optional);
                    await verificationStore.SaveProfileVerificationAsync(new(checkpoint, scan.Id, packageBoundary.EntryId, entryCount, result), token);
                }, token);
                _profileVerificationFeedback = string.Empty;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _profileVerificationFeedback = "Final verification was cancelled. Run it again to collect a complete result.";
            }
            catch (JobStoreConflictException)
            {
                _profileVerificationFeedback = "The job, Windows boot or approved plan changed during verification. Reopen the job and review the saved records.";
            }
            finally
            {
                await LoadWindowsUpdateHistoryAsync(jobId);
                await RefreshPreparationDispositionAsync(jobId, invalidateReport: false);
            }
        });
    }

    public void CancelProfileVerification()
    {
        if (!CanCancelProfileVerification) { return; }
        _cancellationRequest = _profileVerificationCancellation!.CancelAsync();
        _profileVerificationFeedback = "Stopping final verification.";
        Publish();
    }

    private ProfilePackageCheck[] ReadProfilePackages(JournalHistory history, PlanProfileOption option, CancellationToken token)
    {
        var result = new List<ProfilePackageCheck>();
        foreach (var action in history.Plan.Actions)
        {
            token.ThrowIfCancellationRequested();
            if (action.Request is not InstallPackageActionRequest request) { continue; }
            var entry = option.Catalog.Applications.SingleOrDefault(item => item.Package.SourceId == request.SourceId &&
                item.Package.PackageId == request.PackageId && item.Package.ExactVersion == request.ExactVersion);
            var last = history.Entries.LastOrDefault(item => item.ActionId == action.ActionId);
            var observation = last is { State: JournalState.Verified } && last.ApprovalId == history.ActiveApproval?.ApprovalId &&
                entry?.Package.InstallerSha256 is { } digest
                ? _packageService?.InspectApproved(request, digest) : null;
            var state = observation?.Status switch
            {
                PackageObservationStatus.Installed when observation.ExactVersion == request.ExactVersion => ProfilePackageStatus.ExactVersion,
                PackageObservationStatus.Installed or PackageObservationStatus.Conflict => ProfilePackageStatus.Mismatch,
                PackageObservationStatus.Absent => ProfilePackageStatus.Absent,
                _ => ProfilePackageStatus.Unavailable,
            };
            result.Add(new(action.CapabilityId, request.ExactVersion, observation?.ExactVersion,
                state, _timeProvider.GetUtcNow(), action.ActionId));
        }
        return result.ToArray();
    }

    private async Task<SavedProfileVerification?> ReadProfileVerificationForReportAsync(JobId jobId, CancellationToken token)
    {
        if (IsFixtureScenario || _hasSeparateEvidenceOrigin || _store is not IProfileVerificationStore verification) { return null; }
        var saved = await Task.Run(() => verification.LoadProfileVerificationAsync(jobId, token), token);
        if (saved is { IsCurrent: true } &&
            (WorkshopPolicyCandidate.ForDeviceClass(PolicyDeviceClass.Laptop)?.Document.ContentSha256 != saved.Record.Result.PolicySha256 ||
             !VisiblePlanProfiles.Any(option => option.Profile.ContentSha256 == saved.Record.Result.ProfileSha256)))
        {
            return saved with { IsCurrent = false };
        }
        return saved;
    }

    private static WorkshopProfileVerification? ProjectProfileVerification(SavedProfileVerification? saved) =>
        saved is null ? null : new(saved.Record.Checkpoint.Plan.JobId, saved.Record.Result, saved.IsCurrent);

    private async Task LoadProfileVerificationAsync(JobId jobId)
    {
        try
        {
            var saved = await ReadProfileVerificationForReportAsync(jobId, CancellationToken.None);
            if (_profileVerification?.Record.ScanId != saved?.Record.ScanId || _profileVerification?.IsCurrent != saved?.IsCurrent)
            {
                ClearReport();
            }
            _profileVerification = saved;
        }
        catch (JobStoreException)
        {
            _profileVerification = null;
            ClearReport();
            _profileVerificationFeedback = "The saved profile verification could not be read. Reopen the job and review its history.";
        }
    }

    private void ClearProfileVerification()
    {
        _profileVerification = null;
        _profileVerificationFeedback = string.Empty;
    }
}
