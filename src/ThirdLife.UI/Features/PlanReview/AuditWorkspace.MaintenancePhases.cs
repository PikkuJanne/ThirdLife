using System.Collections.Immutable;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Inventory;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private bool _packagePhaseNeedsRecovery;
    private UpdateRecovery _updateRecovery;
    private MaintenanceCheckpoint? _maintenance;
    private JournalHistory? _maintenanceJournal;
    private bool _maintenanceUnavailable;
    private bool _maintenanceCurrent;
    private bool _includeFullMaintenance;
    private string _maintenanceFeedback = string.Empty;

    private bool HasActiveMaintenance => _maintenance is { Phase: not MaintenancePhase.FinalVerification };

    private bool AllowsMaintenanceUpdates => !_maintenanceUnavailable &&
        (!HasActiveMaintenance || (_maintenanceCurrent && !MaintenanceRestartLimitReached && _maintenance?.Phase == MaintenancePhase.WindowsUpdates));

    private bool MaintenanceRestartLimitReached => _maintenance is { Phase: not (MaintenancePhase.FinalRestart or MaintenancePhase.FinalVerification) } checkpoint &&
        _updateHistory.LastOrDefault()?.BootSession is { } boot && !MaintenancePhases.WithinRestartLimit(checkpoint, boot);

    private bool OutsideFinalRestartLimit => _maintenance is { Phase: MaintenancePhase.FinalRestart } checkpoint &&
        _updateHistory.LastOrDefault()?.BootSession is { } boot && boot != checkpoint.BootSession &&
        !MaintenancePhases.ConfirmsFinalRestart(checkpoint, boot);

    private bool HoldsPackageDispatch => _updateHistoryUnavailable || _updateRecovery != UpdateRecovery.None ||
        _maintenanceUnavailable || (HasActiveMaintenance &&
        (!_maintenanceCurrent || _maintenance?.Phase != MaintenancePhase.Packages || MaintenanceRestartLimitReached ||
         WindowsUpdateHistory.Disposition(_updateHistory) is not (WindowsUpdateDisposition.Current or WindowsUpdateDisposition.NoEligibleOffers)));

    public bool ShowsMaintenanceChoice => IsPackageProfile && ShowsWindowsUpdates && _store is IMaintenancePhaseStore;
    public bool CanChooseMaintenance => ShowsMaintenanceChoice && CanChoosePlanProfile && !HasActiveMaintenance && !_maintenanceUnavailable;
    public bool IncludeFullMaintenance
    {
        get => IsPackageProfile && (HasActiveMaintenance || _includeFullMaintenance);
        set
        {
            if (!CanChooseMaintenance || _includeFullMaintenance == value) { return; }
            _includeFullMaintenance = value;
            ChangePlanSelection();
        }
    }

    public string ReviewedMaintenanceSchedule => PlanReview?.Plan.FullMaintenance == true ? PlanReviewPresentation.FullMaintenanceReview : string.Empty;
    public bool ShowsMaintenanceContinuation => HasActiveMaintenance;
    public bool IsCheckingMaintenance { get; private set; }
    public bool CanContinueMaintenance => ShowsMaintenanceContinuation && CanScanWindowsUpdates && _maintenanceCurrent &&
        !_maintenanceUnavailable && SavedServicingApproval is null;
    public bool HasMaintenancePhaseStatus => _maintenance is not null || _maintenanceUnavailable ||
        (ShowsWindowsUpdates && (HoldsPackageDispatch || _packagePhaseNeedsRecovery));

    public string MaintenancePhaseStatus
    {
        get
        {
            if (_maintenanceUnavailable) { return "The maintenance checkpoint could not be read. Reopen the job to check the saved schedule."; }
            var progress = _maintenance?.Phase switch
            {
                null => string.Empty,
                _ when !_maintenanceCurrent && _maintenanceJournal?.Entries.Any(entry => entry.State == JournalState.Verified) == true =>
                    "The intake or assessment changed after package verification. Review the saved package result in Verify. The current evidence requires a new readiness review.",
                _ when !_maintenanceCurrent => "The saved maintenance plan no longer matches the current intake or assessment. Build and review the current full maintenance plan in Prepare.",
                _ when MaintenanceRestartLimitReached => "Windows reported a boot sequence outside this schedule's three-restart limit before the final restart. Stop and review the saved maintenance history.",
                _ when OutsideFinalRestartLimit => "Windows reported a boot sequence outside the final restart checkpoint's four-restart limit. The job needs workshop review before final verification.",
                MaintenancePhase.WindowsUpdates => "Windows Update phase. Review any eligible revision separately in Prepare. Check and continue after servicing and any required restarts.",
                MaintenancePhase.Packages => "Package phase. In Prepare, select the reviewed package profile and build its plan to check the current approval. Install and verify the package, then check and continue.",
                MaintenancePhase.PostPackageChecks => "Post-package checks. Check and continue to compare the installed version with the approved action and save the final restart checkpoint.",
                MaintenancePhase.FinalRestart => "Final restart required. Save your work, use Restart in Windows and reopen this job. Check and continue to confirm the new boot and recheck Windows and the installed package.",
                MaintenancePhase.FinalVerification => "Final restart confirmed. The job is awaiting independent final verification. Whole-device readiness is still unverified.",
                _ => throw new InvalidOperationException(),
            };
            var lastCheck = _maintenance?.LastCheckFailure?.Issue switch
            {
                MaintenanceCheckIssue.PackageMismatch => "The last saved check found that the installed package did not match the approved version. Review its installed state and check again.",
                MaintenanceCheckIssue.PackageUnavailable => "The last saved check could not read the installed package version. Check Windows Installer access and try again.",
                _ => string.Empty,
            };
            return string.Join(Environment.NewLine, new[] { progress, RecoveryGuidance, lastCheck, _maintenanceFeedback }
                .Where(line => line.Length != 0));
        }
    }

    private string RecoveryGuidance => _updateHistoryUnavailable
        ? "Package installation is paused because the saved update journal could not be read. Reopen the job to review it."
        : _updateRecovery switch
        {
            UpdateRecovery.Restart => "Package installation is paused for a Windows Update restart. Save your work, restart Windows, reopen this job and scan again in Prepare.",
            UpdateRecovery.VerifyInstallation => "Package installation is paused until Windows Update verifies the installed revision. Scan again in Prepare.",
            UpdateRecovery.ReviewResult => "Package installation is paused. Review the saved update operation in Verify. A fresh scan can reconcile an interrupted preparation. An unconfirmed handoff needs workshop review.",
            UpdateRecovery.Servicing => "Windows Update is between servicing steps. If servicing was interrupted, review the saved result and run a fresh scan before starting packages.",
            _ when _packagePhaseNeedsRecovery => "Windows servicing is paused. Open Verify and reconcile the saved package using its installed state and any required restart.",
            _ => string.Empty,
        };

    private void ClearMaintenanceCheckpoint()
    {
        _maintenance = null;
        _maintenanceJournal = null;
        _maintenanceCurrent = false;
        _maintenanceUnavailable = false;
        _maintenanceFeedback = string.Empty;
    }

    private async Task LoadMaintenanceCheckpointAsync(JobId jobId)
    {
        if (_store is not IMaintenancePhaseStore store || _store is not IActionJournalStore journal ||
            _hasSeparateEvidenceOrigin || IsFixtureScenario)
        {
            ClearMaintenanceCheckpoint();
            return;
        }
        try
        {
            var checkpoint = await Task.Run(() => store.LoadMaintenanceAsync(jobId));
            if (checkpoint != _maintenance) { _maintenanceFeedback = string.Empty; }
            _maintenance = checkpoint;
            _maintenanceJournal = checkpoint is null ? null : await Task.Run(() => journal.LoadJournalAsync(checkpoint.Plan));
            _maintenanceCurrent = false;
            if (_maintenanceJournal is { Plan.FullMaintenance: true } history && history.ActiveApproval?.ApprovalId == checkpoint?.ApprovalId)
            {
                var current = await LoadJobAsync(jobId);
                _maintenanceCurrent = await Task.Run(() => !IsSeparateEvidenceOrigin(current) &&
                    IsCurrentPreparationPlan(current, AssessmentOrchestrator.ReadLatest(current), history.Plan, _timeProvider.GetUtcNow()));
            }
            _maintenanceUnavailable = false;
        }
        catch (JobStoreException)
        {
            _maintenanceUnavailable = true;
            _maintenanceCurrent = false;
        }
    }

    private bool RestoreMaintenancePackageApproval(PlanReviewSnapshot displayed)
    {
        if (!displayed.Plan.FullMaintenance || !_maintenanceCurrent || _maintenance is not { } checkpoint ||
            _displayedActionDecisionHistory is not { ActiveApproval: { } approval } history ||
            checkpoint.Plan != history.Plan.Key || approval.ApprovalId != checkpoint.ApprovalId ||
            !IsFreshPackageApproval(approval) || history.Entries.LastOrDefault()?.State != JournalState.Approved)
        {
            return false;
        }

        _packageApproval = approval;
        PlanReviewDecision = new PlanReviewDecision(PlanReviewOutcome.Approved, displayed, approval.OccurredAtUtc);
        _planReviewStatus = "The saved full maintenance approval matches this package plan.";
        return true;
    }

    public Task ContinueMaintenanceAsync()
    {
        if (!CanContinueMaintenance || _maintenance is not { } expected || UpdateAgent is not { } agent ||
            _store is not IMaintenancePhaseStore store || _store is not IWindowsUpdateJournal updates ||
            Gate?.Decision is not { } gate)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var jobId = expected.Plan.JobId;
            IsCheckingMaintenance = true;
            _updateCancellation = new CancellationTokenSource();
            var token = _updateCancellation.Token;
            _maintenanceFeedback = "Checking Windows Update and the current maintenance phase.";
            Publish();
            try
            {
                var workflow = new WindowsUpdateWorkflow(updates, agent, _timeProvider);
                var scan = await Task.Run(() => workflow.ScanAsync(jobId, gate.DecisionId, token));
                await LoadWindowsUpdateHistoryAsync(jobId);
                await LoadMaintenanceCheckpointAsync(jobId);
                token.ThrowIfCancellationRequested();
                if (expected != _maintenance || !_maintenanceCurrent || _maintenanceUnavailable)
                {
                    _maintenanceFeedback = "The saved job or maintenance schedule changed during the check. Reopen it before continuing.";
                    return;
                }
                var disposition = WindowsUpdateHistory.Disposition(_updateHistory);
                if (disposition is not (WindowsUpdateDisposition.Current or WindowsUpdateDisposition.NoEligibleOffers))
                {
                    if (disposition == WindowsUpdateDisposition.ApprovalRequired && expected.Phase != MaintenancePhase.WindowsUpdates)
                    {
                        await store.AdvanceMaintenanceAsync(expected, scan.Id, token: token);
                        ClearReport();
                    }
                    _maintenanceFeedback = _updateHistory.IsEmpty ? "The current update scan could not be read." :
                        DescribeSavedWindowsUpdate(_updateHistory);
                    return;
                }

                ImmutableArray<MaintenancePackageObservation> observed = [];
                if (expected.Phase is MaintenancePhase.PostPackageChecks or MaintenancePhase.FinalRestart &&
                    _maintenanceJournal is { } history && PackageHistoryRequest(history) is { } request &&
                    history.StateOf(request.ActionId) == JournalState.Verified)
                {
                    var registration = _packageService is { } service ? await Task.Run(service.Inspect, token) : null;
                    if (registration is not { Status: PackageObservationStatus.Installed, ExactVersion: { } version } ||
                        version != request.ExactVersion)
                    {
                        var issue = registration is null or { Status: PackageObservationStatus.Unavailable }
                            ? MaintenanceCheckIssue.PackageUnavailable : MaintenanceCheckIssue.PackageMismatch;
                        await store.RecordMaintenanceCheckFailureAsync(expected, scan.Id, new(request.ActionId, issue), token);
                        ClearReport();
                        return;
                    }
                    observed = [new(request.ActionId, version)];
                }

                await store.AdvanceMaintenanceAsync(expected, scan.Id, observed, token);
                ClearReport();
                _maintenanceFeedback = string.Empty;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _maintenanceFeedback = "Check cancelled. Reopen the saved job or check the phase again.";
            }
            catch (JobStoreConflictException)
            {
                _maintenanceFeedback = expected.Phase switch
                {
                    MaintenancePhase.FinalRestart => "The saved checks do not yet confirm a later Windows boot, a clear restart state and the approved installed package.",
                    MaintenancePhase.Packages => "Review the approved package and its latest verification. After an unexpected restart, check Windows again before installation.",
                    _ => "Check the current scan, approval and package results. Review is required after three pre-final restarts.",
                };
            }
            finally
            {
                IsCheckingMaintenance = false;
                await LoadMaintenanceCheckpointAsync(jobId);
                await RefreshPreparationDispositionAsync(jobId, invalidateReport: false);
            }
        });
    }
}
