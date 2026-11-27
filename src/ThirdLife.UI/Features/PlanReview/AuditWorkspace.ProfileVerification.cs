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
    private SavedColdBootVerification? _coldBootVerification;
    private bool _postBootVerificationRunning;
    private string _profileVerificationFeedback = string.Empty;
    private string _coldBootVerificationFeedback = string.Empty;

    public bool ShowsProfileVerification => !IsFixtureScenario && !_hasSeparateEvidenceOrigin &&
        (_maintenance?.Phase == MaintenancePhase.FinalVerification || _profileVerification is not null);

    private bool CanVerifyProfile => ShowsProfileVerification && CanScanWindowsUpdates &&
        _maintenance is { Phase: MaintenancePhase.FinalVerification } && _maintenanceCurrent && !_maintenanceUnavailable &&
        !_packagePhaseNeedsRecovery && SavedServicingApproval is null && _store is IProfileVerificationStore;

    public bool CanRunProfileVerification => CanVerifyProfile && _coldBootVerification is null;
    public bool ShowsColdBootVerification => ShowsProfileVerification || _coldBootVerification is not null;
    public bool CanRecordColdBootCheckpoint => CanVerifyProfile && (_coldBootVerification?.State is
        null or ColdBootVerificationState.AwaitingEvidence or ColdBootVerificationState.Historical);
    public bool CanRunPostBootVerification => CanVerifyProfile &&
        _coldBootVerification is { State: ColdBootVerificationState.ReadyToVerify };
    public bool IsProfileVerificationRunning => _profileVerificationCancellation is not null && !_postBootVerificationRunning;
    public bool IsPostBootVerificationRunning => _profileVerificationCancellation is not null && _postBootVerificationRunning;
    public bool CanCancelProfileVerification => _profileVerificationCancellation is { IsCancellationRequested: false };

    public string ProfileVerificationStatus => _profileVerificationFeedback.Length != 0 ? _profileVerificationFeedback :
        ProjectProfileVerification(_profileVerification, _coldBootVerification) is { Result: not null } saved
            ? WorkshopReportRenderer.DescribeProfileVerification(saved)
            : "Run final profile checks before shutdown or save the cold-boot checkpoint. Verification queries the configured Windows Update service.";

    public string ProfileVerificationDetails => ProjectProfileVerification(_profileVerification, _coldBootVerification) is { } saved
        ? WorkshopReportRenderer.DescribeProfileVerificationDetails(saved) : string.Empty;

    public bool HasProfileVerificationDetails => ProfileVerificationDetails.Length != 0;

    public string ColdBootVerificationStatus => _coldBootVerificationFeedback.Length != 0 ? _coldBootVerificationFeedback :
        ProjectProfileVerification(_profileVerification, _coldBootVerification) is { } saved
            ? WorkshopReportRenderer.DescribeColdBootVerification(saved)
            : "Save a cold-boot checkpoint on this job before shutting down Windows.";

    public string ColdBootVerificationDetails => ProjectProfileVerification(_profileVerification, _coldBootVerification) is { } saved
        ? WorkshopReportRenderer.DescribeColdBootVerificationDetails(saved) : string.Empty;

    public bool HasColdBootVerificationDetails => ColdBootVerificationDetails.Length != 0;

    public Task RecordColdBootCheckpointAsync()
    {
        if (!CanRecordColdBootCheckpoint || _maintenance is not { } checkpoint || _store is not IProfileVerificationStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            _coldBootVerificationFeedback = string.Empty;
            try
            {
                await Task.Run(() => store.RecordColdBootCheckpointAsync(checkpoint));
            }
            catch (JobStoreConflictException)
            {
                _coldBootVerificationFeedback = "The cold-boot checkpoint could not be saved. Reopen the job and review the approved plan and Windows session.";
            }
            finally
            {
                await LoadProfileVerificationAsync(checkpoint.Plan.JobId);
            }
        });
    }

    public Task RunProfileVerificationAsync() => CanRunProfileVerification ? RunProfileVerificationAsync(null) : Task.CompletedTask;

    public Task RunPostBootVerificationAsync() => CanRunPostBootVerification && _coldBootVerification is { } saved
        ? RunProfileVerificationAsync(saved.Checkpoint.Id) : Task.CompletedTask;

    private Task RunProfileVerificationAsync(Guid? coldBootCheckpointId)
    {
        if (_maintenance is not { } checkpoint ||
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
            _postBootVerificationRunning = coldBootCheckpointId.HasValue;
            var token = _profileVerificationCancellation.Token;
            _profileVerificationFeedback = string.Empty;
            _coldBootVerificationFeedback = string.Empty;
            SetVerificationFeedback(_postBootVerificationRunning
                ? "Checking Windows, updates, devices and approved package launches after cold boot."
                : "Reading current Windows, update, package and device evidence.");
            Publish();
            try
            {
                await Task.Run(async () =>
                {
                    if (coldBootCheckpointId is { } id &&
                        (await verificationStore.LoadColdBootVerificationAsync(jobId, token) is not
                        { State: ColdBootVerificationState.ReadyToVerify } pending || pending.Checkpoint.Id != id))
                    {
                        throw new JobStoreConflictException();
                    }
                    var journals = await ReadPreparationJournalsAsync(jobId, token);
                    var entryCount = journals.Sum(item => item.Entries.Length);
                    var started = _timeProvider.GetUtcNow();
                    var workflow = new WindowsUpdateWorkflow(updates, agent, _timeProvider);
                    var scan = await workflow.ScanAsync(jobId, original.GateDecisionId, token);
                    token.ThrowIfCancellationRequested();
                    var updateHistory = await updates.LoadWindowsUpdatesAsync(jobId, token);
                    var collected = await _orchestrator!.VerifyAsync(jobId, original.GateDecisionId, token);
                    var packageChecks = await ReadProfilePackagesAsync(history, option, coldBootCheckpointId.HasValue, token);
                    var job = await LoadJobAsync(jobId, token);
                    var finished = _timeProvider.GetUtcNow();
                    var evidence = AuditPolicyEvidence.CreateForFinalVerification(job, collected, original, finished);
                    var update = scan.Scan is { } observed ? new ProfileUpdateCheck(observed, scan.OccurredAtUtc,
                        WindowsUpdateHistory.RecoveryBeforePackages(updateHistory) != UpdateRecovery.None) : null;
                    var optional = history.Plan.Actions.Where(action => action.Intent == CapabilityIntent.Optional)
                        .Select(action => action.CapabilityId).ToArray();
                    var result = ProfileVerification.Evaluate(started, finished, option.Profile, collected.Observations,
                        evidence, baseline.Document, update, packageChecks, optional);
                    await verificationStore.SaveProfileVerificationAsync(new(checkpoint, scan.Id, packageBoundary.EntryId, entryCount,
                        result, coldBootCheckpointId), token);
                }, token);
                SetVerificationFeedback(string.Empty);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SetVerificationFeedback("Verification was cancelled. Run it again to collect a complete result.");
            }
            catch (JobStoreConflictException)
            {
                SetVerificationFeedback("The job, Windows boot or approved plan changed during verification. Reopen the job and review the saved records.");
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
        SetVerificationFeedback("Stopping verification.");
        Publish();
    }

    private void SetVerificationFeedback(string message)
    {
        if (_postBootVerificationRunning) { _coldBootVerificationFeedback = message; }
        else { _profileVerificationFeedback = message; }
    }

    private async Task<ProfilePackageCheck[]> ReadProfilePackagesAsync(JournalHistory history, PlanProfileOption option,
        bool checkLaunch, CancellationToken token)
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
            ProfileVerificationState? launch = null;
            if (checkLaunch)
            {
                launch = ProfileVerificationState.Unavailable;
                if (state == ProfilePackageStatus.ExactVersion && WorkshopPackageProfile.IncludesLaunchProbe(history.Plan) &&
                    _packageService is { } service && entry?.Package.InstallerSha256 is { } approvedDigest)
                {
                    var probe = await service.VerifyInstalledAsync(request, approvedDigest, token);
                    token.ThrowIfCancellationRequested();
                    launch = probe switch
                    {
                        PackageVerificationStatus.Passed => ProfileVerificationState.Passed,
                        PackageVerificationStatus.ExecutableMissing or PackageVerificationStatus.ExecutableUntrusted or
                            PackageVerificationStatus.LaunchFailed or PackageVerificationStatus.ExitedEarly or
                            PackageVerificationStatus.CleanupUnconfirmed => ProfileVerificationState.Failed,
                        _ => ProfileVerificationState.Unavailable,
                    };
                }
            }
            result.Add(new(action.CapabilityId, request.ExactVersion, observation?.ExactVersion,
                state, _timeProvider.GetUtcNow(), action.ActionId, launch));
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

    private Task<SavedColdBootVerification?> ReadColdBootVerificationAsync(JobId jobId, CancellationToken token) =>
        IsFixtureScenario || _hasSeparateEvidenceOrigin || _store is not IProfileVerificationStore store
            ? Task.FromResult<SavedColdBootVerification?>(null)
            : Task.Run(() => store.LoadColdBootVerificationAsync(jobId, token), token);

    private WorkshopProfileVerification? ProjectProfileVerification(SavedProfileVerification? saved, SavedColdBootVerification? coldBoot)
    {
        var jobId = saved?.Record.Checkpoint.Plan.JobId ?? Job?.Job.JobId;
        if (jobId is null || saved is null && coldBoot is null) { return null; }
        var postBoot = saved?.Record.ColdBootObservation;
        var observation = saved?.Record.ColdBootCheckpointId == coldBoot?.Checkpoint.Id && postBoot is not null
            ? postBoot : coldBoot?.Observation;
        return new(jobId, saved?.Record.Result, saved?.IsCurrent ?? coldBoot?.State != ColdBootVerificationState.Historical,
            coldBoot?.Checkpoint, observation, postBoot is not null, coldBoot?.State);
    }

    private async Task LoadProfileVerificationAsync(JobId jobId)
    {
        try
        {
            var saved = await ReadProfileVerificationForReportAsync(jobId, CancellationToken.None);
            var coldBoot = await ReadColdBootVerificationAsync(jobId, CancellationToken.None);
            if (_profileVerification?.Record.ScanId != saved?.Record.ScanId || _profileVerification?.IsCurrent != saved?.IsCurrent ||
                _coldBootVerification != coldBoot)
            {
                ClearReport();
            }
            _profileVerification = saved;
            _coldBootVerification = coldBoot;
        }
        catch (JobStoreException)
        {
            _profileVerification = null;
            _coldBootVerification = null;
            ClearReport();
            _profileVerificationFeedback = "The saved profile verification could not be read. Reopen the job and review its history.";
        }
    }

    private void ClearProfileVerification()
    {
        _profileVerification = null;
        _coldBootVerification = null;
        _profileVerificationFeedback = string.Empty;
        _coldBootVerificationFeedback = string.Empty;
    }
}
