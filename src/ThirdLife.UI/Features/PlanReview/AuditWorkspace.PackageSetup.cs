using System.Globalization;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private static readonly OperatorId PackageCoordinator = new("thirdlife-package-coordinator");
    private static readonly OperatorId InstallerBackend = new("windows-installer");
    private static readonly OperatorId PackageVerifier = new("thirdlife-package-verifier");
    private readonly PilotPackageService? _packageService;
    private PackageResolution? _packageResolution;
    private JournalApproval? _packageApproval;
    private CancellationTokenSource? _packageCancellation;
    private PackageAttemptProgress? _packageAttemptProgress;
    private bool _packageNeedsReview;
    private bool _canInspectSavedPackage;
    private string _packageOperatorCode = string.Empty;
    private string? _packageActivity;

    public bool IsPackageProfile => SelectedPlanProfile?.PackagePolicy is not null;
    public string PackageHistoryText { get; private set; } = string.Empty;
    public bool HasPackageHistory => PackageHistoryText.Length != 0;
    public string LatestPackageIdentity { get; private set; } = string.Empty;
    public string LatestPackageOutcome { get; private set; } = string.Empty;
    public IReadOnlyList<PackageJournalItem> PackageJournal { get; private set; } = [];
    public bool IsPackageOperationRunning => _packageCancellation is not null;
    public bool ShowsPackageOperationStatus => IsPackageProfile || IsPackageOperationRunning;
    public bool ShowsVerifyEmptyState => !HasPackageHistory && !HasRebootRecovery && !HasBrokerHistory && !HasPreparationDisposition;
    public bool ShowsPackageInstallActivity => IsPackageOperationRunning && !ShowsPackageRecoveryCancellation;
    public bool ShowsPackageVerificationPlanStatus => HasPackageHistory && IsPackageProfile && PlanReviewStatus != LatestPackageOutcome;
    public bool ShowsPackageVerificationActions => ShowsPackageRecovery || HasPackageRetryStatus ||
        ShowPackageRetry || IsPackageOperationRunning || ShowsPackageVerificationPlanStatus;
    public bool CanCancelPackage => _packageCancellation is { IsCancellationRequested: false } &&
        (_packageAttemptProgress?.AcceptsCancellation ?? true);
    public bool ShowsPackageRecovery => OffersPackageRecoveryCheck || PackageRecoveryStatus.Length != 0;
    public bool OffersPackageRecoveryCheck => _canInspectSavedPackage && CanUsePackageRecovery;
    public bool CanCheckPackageRecovery => CanCloseJob && OffersPackageRecoveryCheck;
    public string PackageRecoveryStatus { get; private set; } = string.Empty;
    public bool CanEditPackageOperator => CanEditJob && (PlanReviewDecision is null || ShowsActionSkipChoices);
    public bool CanInstallPackage => CanEditJob && !HoldsPackageDispatch && !_packageNeedsReview && _packageService is not null &&
        PlanReviewDecision is { Outcome: PlanReviewOutcome.Approved } && _packageApproval is not null &&
        _packageResolution?.Status == PackageResolutionStatus.Ready && CanStartPackagePlan;

    public string PackageOperatorCode
    {
        get => _packageOperatorCode;
        set
        {
            if (!CanEditPackageOperator || value == _packageOperatorCode)
            {
                return;
            }

            _packageOperatorCode = value;
            Publish();
        }
    }

    public string PackageSourceStatus => _packageActivity ?? (CurrentPackageSourceStatus() +
        (_packageDiagnosticUnavailable ? " Local diagnostic storage was not confirmed." : string.Empty));

    private string CurrentPackageSourceStatus() =>
        SavedPackageRetry.Status == PackageRetryStatus.Verified && _packageResolution is { Observation: var observation }
            ? observation switch
            {
                { Status: PackageObservationStatus.Absent } => "The previously verified package is no longer installed. Review the current device state.",
                { Status: PackageObservationStatus.Installed, ExactVersion: var version } when version == PilotPackageService.Identity.ExactVersion =>
                    DescribePackageSource(PackageResolutionStatus.AlreadyInstalled),
                { Status: PackageObservationStatus.Installed or PackageObservationStatus.Conflict } =>
                    "The current installation differs from the previously verified package. Review the current device state.",
                _ => "The saved verification is available, but current installed state could not be checked.",
            }
            : DescribePackageSource(_packageResolution?.Status);

    private static string DescribePackageSource(PackageResolutionStatus? status) => status switch
    {
        PackageResolutionStatus.Ready => "The signed x64 installer matches the reviewed package. No conflicting installation was found.",
        PackageResolutionStatus.AlreadyInstalled => "The exact version is already installed for all users. No installation is needed.",
        PackageResolutionStatus.Conflict => "An existing installation conflicts with this plan. Review the machine before continuing.",
        PackageResolutionStatus.TrustMismatch => "The installer did not match the reviewed source, identity or signature. Stop and review the approved source.",
        PackageResolutionStatus.Cancelled => "The package check was cancelled.",
        PackageResolutionStatus.TimedOut => "Preparing the installer reached its time limit. Check the connection, then build the plan again.",
        PackageResolutionStatus.NetworkUnavailable => "The installer download could not reach the package source. Check the connection, then build the plan again.",
        PackageResolutionStatus.DownloadUnavailable => "The reviewed installer could not be downloaded or staged. Check the approved source and available storage.",
        PackageResolutionStatus.Unavailable => "The installed-state check or package backend is unavailable. Check Windows Installer on a supported Windows device.",
        _ => "Build the plan to check the signed installer source and current installation.",
    };

    private bool CanApprovePackagePlan => IsPackageProfile && !_packageNeedsReview && _store is IPackageScheduleJournal &&
        _packageResolution?.Status == PackageResolutionStatus.Ready && !string.IsNullOrWhiteSpace(_packageOperatorCode) &&
        CanStartPackagePlan && _displayedActionDecisionHistory?.Entries.LastOrDefault()?.State != JournalState.Skipped;

    private async Task CheckPackageAsync(PlanReviewSnapshot displayed)
    {
        if (_packageService is not { } service)
        {
            return;
        }

        _packageCancellation = new CancellationTokenSource();
        var token = _packageCancellation.Token;
        _packageActivity = "Checking the reviewed installer and the current installed state.";
        Publish();
        try
        {
            if (SavedPackageRetry.Status == PackageRetryStatus.Verified)
            {
                var observed = await Task.Run(service.Inspect);
                _packageResolution = new PackageResolution(PackageResolutionStatus.Unavailable, observed);
                return;
            }

            if (PackageRequest(displayed.Plan) is null)
            {
                return;
            }

            _packagePreflight = await CheckPreparationResourcesAsync(displayed.Impact, false, token);
            if (!_packagePreflight.AllowsPreparation)
            {
                var observed = await Task.Run(service.Inspect, token);
                token.ThrowIfCancellationRequested();
                _packageResolution = observed.Status switch
                {
                    PackageObservationStatus.Installed when observed.ExactVersion == PilotPackageService.Identity.ExactVersion =>
                        new(PackageResolutionStatus.AlreadyInstalled, observed),
                    PackageObservationStatus.Installed or PackageObservationStatus.Conflict =>
                        new(PackageResolutionStatus.Conflict, observed),
                    _ => null,
                };
                return;
            }

            _packageResolution = await Task.Run(() => service.ResolveAsync(token));
            var diagnostic = _packageResolution.Status switch
            {
                PackageResolutionStatus.Conflict => DiagnosticResultCode.PackageResolutionConflict,
                PackageResolutionStatus.Unavailable => DiagnosticResultCode.PackageResolutionUnavailable,
                PackageResolutionStatus.TrustMismatch => DiagnosticResultCode.PackageTrustMismatch,
                PackageResolutionStatus.Cancelled => DiagnosticResultCode.PackageCancelled,
                PackageResolutionStatus.TimedOut => DiagnosticResultCode.PackageDownloadTimedOut,
                PackageResolutionStatus.NetworkUnavailable => DiagnosticResultCode.PackageDownloadNetworkUnavailable,
                PackageResolutionStatus.DownloadUnavailable => DiagnosticResultCode.PackageDownloadUnavailable,
                _ => (DiagnosticResultCode?)null,
            };
            await RecordPackageDiagnosticAsync(DiagnosticPhase.Plan, diagnostic, DiagnosticCorrelationId.CreateRandom());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _packagePreflight = null;
            _packageResolution = new(PackageResolutionStatus.Cancelled, new(PackageObservationStatus.Unavailable));
        }
        finally
        {
            _packageActivity = null;
        }
    }

    private async Task<bool> SavePackageApprovalAsync(PlanReviewSnapshot displayed)
    {
        if (_store is not IPackageScheduleJournal journal || PackageRequest(displayed.Plan) is not { } request)
        {
            return false;
        }

        var snapshot = JournalPlanSnapshot.Create(displayed.Plan, displayed.Impact);
        var histories = await LoadPackageJournalsAsync(snapshot.Key.JobId, CancellationToken.None);
        if (EvaluatePackageRetry(histories, request).Status is not (PackageRetryStatus.Initial or PackageRetryStatus.Ready))
        {
            await LoadPackageHistoryAsync(snapshot.Key.JobId);
            _planReviewStatus = "The saved attempts changed. Review the package history before approving.";
            return false;
        }

        var actor = new OperatorId(_packageOperatorCode.Trim());
        await journal.SaveJournalPlanAsync(snapshot);
        var existing = await journal.LoadJournalAsync(snapshot.Key);
        var previous = existing?.Entries.LastOrDefault(entry => entry.ActionId == request.ActionId);
        if (!JournalStateMachine.CanApprovePackageAction(previous))
        {
            _planReviewStatus = "This plan already has a saved transaction. Review the job history before taking further action.";
            await LoadPackageHistoryAsync(snapshot.Key.JobId);
            return false;
        }

        var approval = new JournalApproval(Guid.NewGuid(), snapshot.Key, actor, Guid.NewGuid(), _timeProvider.GetUtcNow());
        var approved = new JournalEntry(Guid.NewGuid(), snapshot.Key, request.ActionId, approval.ApprovalId,
            previous?.State ?? JournalState.Planned, JournalState.Approved, JournalActor.Operator,
            approval.ActorId, approval.CorrelationId, approval.OccurredAtUtc, JournalResult.None, JournalCheckpoint.PackagePlanApproval);
        await journal.RecordPackageScheduleApprovalAsync(approval, [approved]);
        _packageApproval = approval;
        ClearReport();
        await LoadPackageHistoryAsync(snapshot.Key.JobId);
        return true;
    }

    public Task InstallPackageAsync()
    {
        if (!CanInstallPackage || _packageApproval is not { } approval ||
            PlanReview is not { } displayed || _store is not IActionJournalStore journal ||
            _packageService is not { } service)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            _packageCancellation = new CancellationTokenSource();
            var token = _packageCancellation.Token;
            Publish();
            try
            {
                await RunApprovedPackagePlanAsync(journal, service, approval, displayed, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _planReviewStatus = "Cancellation finished. Check the saved outcome before continuing.";
                await LoadPackageHistoryAsync(approval.Plan.JobId);
            }
            finally
            {
                await RefreshActionDecisionsAsync(approval.Plan.JobId);
            }
        });
    }

    private async Task RunApprovedPackagePlanAsync(IActionJournalStore journal, PilotPackageService service,
        JournalApproval approval, PlanReviewSnapshot displayed, CancellationToken token)
    {
        var current = await ResolveCurrentPlanAsync(token);
        if (current is null || PackageRequest(current) is not { } request ||
            !MatchesApprovedPackagePlan(displayed, current, approval))
        {
            ClearPlanReview();
            _planReviewStatus = "The current evidence or plan changed. Build and approve a new plan before installing.";
            return;
        }

        var attempted = false;
        await new ScheduledPackageExecutor(journal).ExecuteAsync(approval, candidate => candidate.ActionId == request.ActionId,
            async (history, scheduled, attemptToken) =>
            {
                var histories = await LoadPackageJournalsAsync(approval.Plan.JobId, attemptToken);
                var eligibility = EvaluatePackageRetry(histories, scheduled);
                if (history.Entries.LastOrDefault(entry => entry.ActionId == scheduled.ActionId) is not
                    { State: JournalState.Approved, Checkpoint: JournalCheckpoint.PackagePlanApproval } previous ||
                    !IsFreshPackageApproval(approval) || eligibility.Status is not (PackageRetryStatus.Initial or PackageRetryStatus.Ready))
                {
                    return;
                }

                attempted = true;
                await StartPackageAttemptAsync(journal, service, approval, displayed, current, scheduled, previous,
                    eligibility, attemptToken);
            }, token);
        if (!attempted)
        {
            token.ThrowIfCancellationRequested();
            ClearPlanReview();
            _planReviewStatus = "The saved approval is no longer current. Build and approve a fresh plan.";
            await LoadPackageHistoryAsync(approval.Plan.JobId);
        }
    }

    private async Task StartPackageAttemptAsync(IActionJournalStore journal, PilotPackageService service,
        JournalApproval approval, PlanReviewSnapshot displayed, ProposedPlan current, InstallPackageActionRequest request,
        JournalEntry previous, PackageRetryEligibility eligibility, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            _planReviewStatus = "Installation was cancelled before dispatch.";
            return;
        }

        var impact = ImpactEstimator.Estimate(current);
        var isRetry = eligibility.Status == PackageRetryStatus.Ready;
        if (isRetry && JournalPlanSnapshot.Create(current, impact).Key != approval.Plan)
        {
            ClearPlanReview();
            _planReviewStatus = "The approved resolution changed. Build and approve the current plan before retrying.";
            return;
        }

        try
        {
            _packagePreflight = await CheckPreparationResourcesAsync(impact, false, token);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _planReviewStatus = "Installation was cancelled before dispatch.";
            return;
        }

        if (!_packagePreflight.AllowsPreparation)
        {
            _planReviewStatus = isRetry
                ? "The saved attempt can be retried after the resource requirements are met."
                : "The approval is saved. Request installation again after the resource requirements are met.";
            return;
        }

        if (!IsFreshPackageApproval(approval))
        {
            ClearPlanReview();
            _planReviewStatus = "The saved approval expired. Build and approve a fresh plan.";
            return;
        }

        _packageApproval = null;
        var correlation = Guid.NewGuid();
        var intent = new JournalEntry(Guid.NewGuid(), approval.Plan, request.ActionId, approval.ApprovalId,
            previous.State, JournalState.Started, JournalActor.Coordinator, PackageCoordinator, correlation,
            _timeProvider.GetUtcNow(), JournalResult.None,
            isRetry ? JournalCheckpoint.PackageRetryDispatchIntent : JournalCheckpoint.PackageDispatchIntent,
            priorAttemptEntryId: isRetry ? eligibility.LatestAttemptEntry?.EntryId : null);
        try
        {
            if (await journal.AppendJournalEntryAsync(intent, CancellationToken.None) != JournalWriteResult.Recorded)
            {
                return;
            }
        }
        catch (JobStoreConflictException)
        {
            await LoadPackageHistoryAsync(approval.Plan.JobId);
            await LoadWindowsUpdateHistoryAsync(approval.Plan.JobId);
            var saved = await journal.LoadJournalAsync(approval.Plan, CancellationToken.None);
            if (HoldsPackageDispatch && saved?.ActiveApproval == approval && IsFreshPackageApproval(approval) &&
                saved.Entries.LastOrDefault(entry => entry.ActionId == request.ActionId) == previous)
            {
                _packageApproval = approval;
            }

            _planReviewStatus = HoldsPackageDispatch ? MaintenancePhaseStatus :
                "The saved approval or attempt changed before dispatch. Review the current package history.";
            return;
        }

        ClearReport();
        _packageDiagnosticUnavailable = false;
        PackageRecoveryStatus = string.Empty;
        var display = new Progress<PackageInstallProgress>(update =>
        {
            if (_packageAttemptProgress is { } attempt && attempt.IsCurrent(update) && !token.IsCancellationRequested)
            {
                _packageActivity = update.Stage switch
                {
                    PackageInstallStage.Resolving => "Downloading and checking the approved installer.",
                    PackageInstallStage.Installing => "Windows Installer is applying the approved change. Use Windows Installer if it offers a cancel option.",
                    _ => _packageActivity,
                };
                Publish();
            }
        });
        var stage = new PackageAttemptProgress(request.ActionId, correlation, display);
        _packageAttemptProgress = stage;
        _packageActivity = "Preparing the approved package.";
        Publish();
        try
        {
            var digest = current.Actions.Single(action => action.Request.ActionId == request.ActionId).CatalogEntry.Package.InstallerSha256!;
            PreparationPreflightResult? dispatchPreflight = null;
            var result = await Task.Run(() => service.InstallAsync(request, digest, correlation,
                async cancellation =>
                {
                    var resources = await CheckPreparationResourcesAsync(impact, true, cancellation);
                    if (!await CanDispatchPackageAsync(journal, approval, displayed, request, intent, cancellation))
                    {
                        return false;
                    }

                    dispatchPreflight = resources;
                    return resources.AllowsPreparation;
                },
                stage, token));
            stage.Complete();
            var status = result.Status;
            if (status == PackageInstallStatus.AlreadyInstalled &&
                !await CanDispatchPackageAsync(journal, approval, displayed, request, intent, CancellationToken.None))
            {
                status = PackageInstallStatus.PreconditionChanged;
            }

            var resourceDeferred = status == PackageInstallStatus.PreconditionChanged &&
                dispatchPreflight is { AllowsPreparation: false };
            if (dispatchPreflight is not null && (resourceDeferred || dispatchPreflight.AllowsPreparation))
            {
                _packagePreflight = dispatchPreflight;
            }

            var outcome = await RecordPackageResultAsync(journal, approval, request, correlation, status, service, digest,
                resourceDeferred, token);
            await LoadPackageHistoryAsync(approval.Plan.JobId);
            _planReviewStatus = WorkshopReportRenderer.DescribePackageOutcome(outcome);
        }
        finally
        {
            _packageActivity = null;
            _packageResolution = null;
        }
    }

    private bool IsFreshPackageApproval(JournalApproval approval) =>
        PackageRetryPolicy.IsFreshApproval(approval, _timeProvider.GetUtcNow());

    private static bool MatchesApprovedPackagePlan(PlanReviewSnapshot displayed, ProposedPlan current, JournalApproval approval)
    {
        var impact = ImpactEstimator.Estimate(current);
        return !PlanDiff.Compare(displayed.Plan, displayed.Impact, current, impact).RequiresReapproval &&
            JournalPlanSnapshot.Create(current, impact).Key == approval.Plan;
    }

    private async Task<bool> CanDispatchPackageAsync(
        IActionJournalStore journal, JournalApproval approval, PlanReviewSnapshot displayed, InstallPackageActionRequest request,
        JournalEntry intent, CancellationToken cancellationToken)
    {
        if (!IsFreshPackageApproval(approval))
        {
            return false;
        }

        var current = await ResolveCurrentPlanAsync(cancellationToken).ConfigureAwait(false);
        var durable = await journal.LoadJournalAsync(approval.Plan, cancellationToken).ConfigureAwait(false);
        if (current is null || PackageRequest(current) is null)
        {
            return false;
        }

        return MatchesApprovedPackagePlan(displayed, current, approval) &&
            durable?.ActiveApproval == approval &&
            durable.Entries.LastOrDefault(entry => entry.ActionId == request.ActionId) == intent &&
            IsFreshPackageApproval(approval);
    }

    public void CancelPackageOperation()
    {
        if (_packageCancellation is not { IsCancellationRequested: false } cancellation)
        {
            return;
        }

        var request = _packageAttemptProgress is { } progress
            ? progress.RequestCancellation(cancellation)
            : cancellation.CancelAsync();
        if (request is not null)
        {
            _cancellationRequest = request;
            const string waiting = "Cancellation requested. Wait while ThirdLife records the outcome.";
            if (_packageRecoveryRunning)
            {
                PackageRecoveryStatus = waiting;
            }
            else
            {
                _packageActivity = waiting;
            }

            Publish();
        }
    }

    private void StopWaitingForPackageOnClose()
    {
        if (_packageCancellation is { IsCancellationRequested: false } cancellation)
        {
            _cancellationRequest = cancellation.CancelAsync();
        }
    }

    private async Task<WorkshopPackageOutcome> RecordPackageResultAsync(IActionJournalStore journal, JournalApproval approval,
        InstallPackageActionRequest request, Guid correlation, PackageInstallStatus result, PilotPackageService service,
        string installerSha256, bool resourceDeferred, CancellationToken cancellationToken)
    {
        if (result == PackageInstallStatus.AlreadyInstalled)
        {
            return await VerifyPackageAttemptAsync(journal, approval, request, correlation, JournalState.Started,
                service, installerSha256, cancellationToken);
        }

        var (state, reason, checkpoint, diagnostic) = result switch
        {
            PackageInstallStatus.Applied => (JournalState.Applied, JournalResult.BackendApplied, JournalCheckpoint.PackageLaunchPendingVerification, (DiagnosticResultCode?)null),
            PackageInstallStatus.RestartRequired => (JournalState.Applied, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired, DiagnosticResultCode.PackageRestartRequired),
            PackageInstallStatus.Cancelled => (JournalState.RequiresReview, JournalResult.Cancelled, JournalCheckpoint.Reconcile, DiagnosticResultCode.PackageInstallerInterrupted),
            PackageInstallStatus.CancelledBeforeDispatch =>
                (JournalState.Failed, JournalResult.Cancelled, JournalCheckpoint.PackagePreDispatchFailure, DiagnosticResultCode.PackageCancelled),
            PackageInstallStatus.NetworkUnavailable =>
                (JournalState.Failed, JournalResult.NetworkUnavailable, JournalCheckpoint.PackagePreDispatchFailure, DiagnosticResultCode.PackageDownloadNetworkUnavailable),
            PackageInstallStatus.DownloadUnavailable =>
                (JournalState.Failed, JournalResult.PackageDownloadUnavailable, JournalCheckpoint.None, DiagnosticResultCode.PackageDownloadUnavailable),
            PackageInstallStatus.DownloadTimedOut =>
                (JournalState.RequiresReview, JournalResult.PackageDownloadTimedOut, JournalCheckpoint.Reconcile, DiagnosticResultCode.PackageDownloadTimedOut),
            PackageInstallStatus.TimedOut => (JournalState.RequiresReview, JournalResult.TimedOut, JournalCheckpoint.Reconcile, DiagnosticResultCode.PackageInstallerTimedOut),
            PackageInstallStatus.ReviewRequired => (JournalState.RequiresReview, JournalResult.Interrupted, JournalCheckpoint.Reconcile, DiagnosticResultCode.PackageInstallerInterrupted),
            PackageInstallStatus.AuthorizationDeclined => (JournalState.Failed, JournalResult.AuthorizationDeclined, JournalCheckpoint.None, DiagnosticResultCode.PackageElevationDeclined),
            PackageInstallStatus.ElevationUnavailable =>
                (JournalState.Failed, JournalResult.PackageElevationUnavailable, JournalCheckpoint.None, DiagnosticResultCode.PackageElevationUnavailable),
            PackageInstallStatus.Unavailable => (JournalState.Failed, JournalResult.BackendUnavailable, JournalCheckpoint.PackagePreDispatchFailure,
                DiagnosticResultCode.PackageResolutionUnavailable),
            PackageInstallStatus.TrustMismatch => (JournalState.Failed, JournalResult.TrustMismatch, JournalCheckpoint.None, DiagnosticResultCode.PackageTrustMismatch),
            PackageInstallStatus.Conflict => (JournalState.Failed, JournalResult.PreconditionChanged, JournalCheckpoint.None, DiagnosticResultCode.PackageResolutionConflict),
            PackageInstallStatus.PreconditionChanged when resourceDeferred => (JournalState.Failed,
                JournalResult.PackageResourcePreflightDeferred, JournalCheckpoint.PackagePreDispatchFailure, null),
            PackageInstallStatus.PreconditionChanged => (JournalState.Failed, JournalResult.PackageDispatchAuthorizationChanged,
                JournalCheckpoint.PackagePreDispatchFailure, DiagnosticResultCode.PackagePreconditionChanged),
            _ => (JournalState.Failed, JournalResult.OperationFailed, JournalCheckpoint.None, DiagnosticResultCode.PackageInstallerFailed),
        };
        using var lease = result == PackageInstallStatus.Applied
            ? PackageVerificationLease.TryAcquire(correlation)
            : null;
        var backendEntry = new JournalEntry(Guid.NewGuid(), approval.Plan, request.ActionId, approval.ApprovalId,
            JournalState.Started, state, JournalActor.PackageBackend, InstallerBackend, correlation, _timeProvider.GetUtcNow(),
            reason, checkpoint);
        if (await journal.AppendJournalEntryAsync(backendEntry, CancellationToken.None) != JournalWriteResult.Recorded)
        {
            throw new InvalidOperationException("The package outcome was not freshly recorded.");
        }

        if (result == PackageInstallStatus.RestartRequired)
        {
            PackageRecoveryStatus = journal is IPackageRecoveryStore recovery &&
                await recovery.LoadPackageRebootCheckpointAsync(backendEntry, CancellationToken.None) is not null
                    ? "Save your work, use Restart in Windows, then promptly reopen this job for verification."
                    : "The saved attempt has no usable restart checkpoint. Keep this job in review.";
        }

        if (result != PackageInstallStatus.Applied || lease is null)
        {
            await RecordPackageDiagnosticAsync(DiagnosticPhase.Execute, diagnostic, DiagnosticCorrelationId.FromOperation(correlation));
            return ReportOutcome(state, reason, checkpoint);
        }

        // Backend application is durable before the separate, cancellable verification runs.
        return await VerifyPackageAttemptAsync(journal, approval, request, correlation, JournalState.Applied,
            service, installerSha256, cancellationToken);
    }

    private async Task<WorkshopPackageOutcome> VerifyPackageAttemptAsync(IActionJournalStore journal, JournalApproval approval,
        InstallPackageActionRequest request, Guid correlation, JournalState prior, PilotPackageService service,
        string installerSha256, CancellationToken cancellationToken)
    {
        _packageAttemptProgress = null;
        _packageActivity = "Checking the installed version and testing application launch.";
        Publish();
        var status = await service.VerifyInstalledAsync(request, installerSha256, cancellationToken);
        var (verificationResult, verificationDiagnostic) = DescribePackageVerification(status);
        var verified = verificationResult == JournalResult.PackageLaunchVerified;
        var verifiedState = verified ? JournalState.Verified : JournalState.RequiresReview;
        var verificationCheckpoint = prior == JournalState.Started
            ? JournalCheckpoint.PackageExistingTargetVerification
            : verified ? JournalCheckpoint.None : JournalCheckpoint.Reconcile;
        if (await AppendPackageEntryAsync(journal, approval, request, correlation, prior,
            verifiedState, JournalActor.Verifier, PackageVerifier, verificationResult, verificationCheckpoint,
            verified ? ActionVerification.ExactPackageIdentity : null) != JournalWriteResult.Recorded)
        {
            throw new InvalidOperationException("The package verification was not freshly recorded.");
        }

        await RecordPackageDiagnosticAsync(DiagnosticPhase.Verify, verificationDiagnostic, DiagnosticCorrelationId.FromOperation(correlation));
        return ReportOutcome(verifiedState, verificationResult, verificationCheckpoint);
    }

    private static (JournalResult Result, DiagnosticResultCode? Diagnostic) DescribePackageVerification(PackageVerificationStatus status) =>
        status switch
        {
            PackageVerificationStatus.Passed => (JournalResult.PackageLaunchVerified, null),
            PackageVerificationStatus.PackageAbsent => (JournalResult.PackageDetectionAbsent, DiagnosticResultCode.PackageDetectionAbsent),
            PackageVerificationStatus.PackageMismatch => (JournalResult.PackageDetectionMismatch, DiagnosticResultCode.PackageDetectionMismatch),
            PackageVerificationStatus.ExecutableMissing => (JournalResult.PackageProbeExecutableMissing, DiagnosticResultCode.PackageLaunchExecutableMissing),
            PackageVerificationStatus.ExecutableUntrusted => (JournalResult.PackageProbeExecutableUntrusted, DiagnosticResultCode.PackageLaunchExecutableUntrusted),
            PackageVerificationStatus.LaunchFailed => (JournalResult.PackageProbeLaunchFailed, DiagnosticResultCode.PackageLaunchFailed),
            PackageVerificationStatus.ExitedEarly => (JournalResult.PackageProbeExitedEarly, DiagnosticResultCode.PackageLaunchExitedEarly),
            PackageVerificationStatus.TimedOut => (JournalResult.PackageProbeTimedOut, DiagnosticResultCode.PackageLaunchTimedOut),
            PackageVerificationStatus.Cancelled => (JournalResult.PackageProbeCancelled, DiagnosticResultCode.PackageVerificationCancelled),
            PackageVerificationStatus.CleanupUnconfirmed => (JournalResult.PackageProbeCleanupUnconfirmed, DiagnosticResultCode.PackageLaunchCleanupUnconfirmed),
            _ => (JournalResult.PackageDetectionUnavailable, DiagnosticResultCode.PackageDetectionUnavailable),
        };

    private Task<JournalWriteResult> AppendPackageEntryAsync(IActionJournalStore journal, JournalApproval approval,
        InstallPackageActionRequest request, Guid correlation, JournalState prior, JournalState state, JournalActor actor,
        OperatorId actorId, JournalResult result, JournalCheckpoint checkpoint = JournalCheckpoint.None,
        ActionVerification? verification = null) =>
        journal.AppendJournalEntryAsync(new JournalEntry(Guid.NewGuid(), approval.Plan, request.ActionId, approval.ApprovalId,
            prior, state, actor, actorId, correlation, _timeProvider.GetUtcNow(), result, checkpoint, verification));

    private static InstallPackageActionRequest? PackageRequest(ProposedPlan plan)
    {
        var identity = PilotPackageService.Identity;
        return !plan.SyntheticData && plan.BlockedReason is null && WorkshopPackageProfile.IncludesLaunchProbe(plan) &&
            plan.Actions is [var action] &&
            action.Request is InstallPackageActionRequest { Scope: ActionScope.Machine, Architecture: ActionArchitecture.X64 } request &&
            request.SourceId == identity.SourceId && request.PackageId == identity.PackageId &&
            request.ExactVersion == identity.ExactVersion && request.Publisher == identity.Publisher &&
            action.CatalogEntry.Package.InstallerSha256 == identity.InstallerSha256 ? request : null;
    }

    private async Task LoadPackageHistoryAsync(JobId jobId)
    {
        var histories = await LoadPackageJournalsAsync(jobId, CancellationToken.None);
        _packagePhaseNeedsRecovery = MaintenancePhases.RequiresPackageRecovery(histories);
        RememberPackageRetry(histories);
        var latest = ReadPackageRecords(histories, includeEarlierAttempts: false);
        _packageNeedsReview = latest.Any(NeedsPackageReview);
        _canInspectSavedPackage = latest.Any(package => NeedsPackageReview(package) &&
            package.RequestedVersion == PilotPackageService.Identity.ExactVersion);
        if (OffersPackageRecoveryCheck && PackageRecoveryStatus.Length == 0)
        {
            PackageRecoveryStatus = "Reopen this job or check the installed package state to continue its saved verification.";
        }

        var attempts = ReadPackageRecords(histories, includeEarlierAttempts: true);
        PackageHistoryText = string.Join(Environment.NewLine, attempts.Select(package =>
            string.Create(CultureInfo.CurrentCulture,
                $"{package.PackageId} {package.RequestedVersion}, machine-wide. {WorkshopReportRenderer.DescribePackageOutcome(package.Outcome)}")));
        var last = attempts.LastOrDefault();
        LatestPackageIdentity = last is null ? string.Empty :
            $"{last.PackageId} · {last.RequestedVersion} · All users";
        LatestPackageOutcome = last is null ? string.Empty : WorkshopReportRenderer.DescribePackageOutcome(last.Outcome);
        PackageJournal = histories
            .Select(history => (History: history, Request: PackageHistoryRequest(history)))
            .Where(item => item.Request is not null)
            .SelectMany(item => item.History.Entries.Reverse().Where(entry => entry.ActionId == item.Request!.ActionId)
                .Select(entry => (Entry: entry, Plan: item.History.Plan.Key.PlanSha256)))
            .OrderByDescending(item => item.Entry.OccurredAtUtc)
            .Take(24)
            .Select(item => PresentPackageJournalEntry(item.Entry, item.Plan))
            .ToArray();
        RefreshPackageRetryAfterDelay();
    }

    private static PackageJournalItem PresentPackageJournalEntry(JournalEntry entry, string planHash)
    {
        var outcome = entry.State switch
        {
            JournalState.Approved => "An operator approved the exact plan at this time.",
            JournalState.Started => "The intent was recorded before requesting Windows Installer.",
            JournalState.Applied when entry.Checkpoint != JournalCheckpoint.RestartRequired => "Windows Installer reported success.",
            _ => WorkshopReportRenderer.DescribePackageOutcome(ReportOutcome(entry.State, entry.Result, entry.Checkpoint)),
        };
        var title = entry.State switch
        {
            JournalState.Approved => "Exact plan approval saved",
            JournalState.Started => "Dispatch intent saved",
            JournalState.Applied => "Installer result saved",
            JournalState.Verified => "Independent verification saved",
            JournalState.RequiresReview => "Review needed",
            JournalState.Failed when entry.Result == JournalResult.PackageResourcePreflightDeferred => "Preparation deferred",
            JournalState.Failed => "Operation failed",
            JournalState.Skipped => "Operation skipped",
            JournalState.RolledBack => "Rollback result saved",
            _ => "Package record saved",
        };
        return new PackageJournalItem(title, outcome,
            entry.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            FormattableString.Invariant(
                $"{entry.OccurredAtUtc:O}{Environment.NewLine}{entry.Checkpoint} · {entry.Result}{Environment.NewLine}Action {entry.ActionId:D}{Environment.NewLine}Plan SHA-256 {planHash}"));
    }

    private void ClearPackageHistory()
    {
        StopPackageRetryRefresh();
        _savedPackageRetry = default;
        _packagePhaseNeedsRecovery = false;
        _packageNeedsReview = false;
        _canInspectSavedPackage = false;
        PackageHistoryText = string.Empty;
        LatestPackageIdentity = string.Empty;
        LatestPackageOutcome = string.Empty;
        PackageJournal = [];
        PackageRecoveryStatus = string.Empty;
    }

    private static bool NeedsPackageReview(WorkshopPackageRecord package) => package.Outcome is
        WorkshopPackageOutcome.NoFinalResult or WorkshopPackageOutcome.PendingReview or WorkshopPackageOutcome.Cancelled or
        WorkshopPackageOutcome.TimedOut or WorkshopPackageOutcome.VerificationUnconfirmed or WorkshopPackageOutcome.Applied or
        WorkshopPackageOutcome.RestartRequired or WorkshopPackageOutcome.ProbeUnavailable or WorkshopPackageOutcome.ProbeExitedEarly or
        WorkshopPackageOutcome.ProbeTimedOut or WorkshopPackageOutcome.ProbeCancelled or WorkshopPackageOutcome.ProbeCleanupUnconfirmed or
        WorkshopPackageOutcome.LaunchVerificationPending or WorkshopPackageOutcome.DownloadTimedOut or
        WorkshopPackageOutcome.DetectionAbsent or WorkshopPackageOutcome.DetectionMismatch or WorkshopPackageOutcome.DetectionUnavailable or
        WorkshopPackageOutcome.ProbeExecutableMissing or WorkshopPackageOutcome.ProbeExecutableUntrusted or WorkshopPackageOutcome.ProbeLaunchFailed;

    private static WorkshopPackageRecord[] ReadPackageRecords(IReadOnlyList<JournalHistory> histories, bool includeEarlierAttempts)
    {
        var packages = new List<WorkshopPackageRecord>();
        foreach (var history in histories)
        {
            if (PackageHistoryRequest(history) is not { } request)
            {
                continue;
            }

            var entries = history.Entries.Where(entry => entry.ActionId == request.ActionId).ToArray();
            var latest = entries[^1];
            if (includeEarlierAttempts)
            {
                foreach (var attempt in entries.Where(entry => entry.State != JournalState.Approved)
                    .GroupBy(entry => (entry.ApprovalId, entry.CorrelationId)).Select(entries => entries.Last()))
                {
                    AddRecord(attempt);
                }
            }

            if (!includeEarlierAttempts || latest.State == JournalState.Approved)
            {
                AddRecord(latest);
            }

            void AddRecord(JournalEntry entry) => packages.Add(new WorkshopPackageRecord(request.PackageId, request.SourceId,
                request.ExactVersion, history.Plan.Key.PlanSha256, ReportOutcome(entry.State, entry.Result, entry.Checkpoint), entry.OccurredAtUtc));
        }

        return packages.OrderBy(package => package.OccurredAtUtc).ToArray();
    }

    private static InstallPackageActionRequest? PackageHistoryRequest(JournalHistory history)
    {
        if (history.Plan.SyntheticData || (history.Plan.Schedule is null && history.Plan.Actions.Length != 1))
        {
            return null;
        }

        var identity = PilotPackageService.Identity;
        return history.Plan.Actions.Select(action => action.Request).OfType<InstallPackageActionRequest>()
            .FirstOrDefault(request => request.Scope == ActionScope.Machine &&
                request.SourceId == identity.SourceId && request.PackageId == identity.PackageId && request.Publisher == identity.Publisher &&
                history.Entries.Any(entry => entry.ActionId == request.ActionId &&
                    (entry.Checkpoint == JournalCheckpoint.PackagePlanApproval || entry.State == JournalState.Skipped)));
    }

    private static WorkshopPackageOutcome ReportOutcome(JournalState state, JournalResult result, JournalCheckpoint checkpoint) =>
        (state, result, checkpoint) switch
        {
            (JournalState.Approved, _, _) => WorkshopPackageOutcome.Approved,
            (JournalState.Started, _, _) => WorkshopPackageOutcome.NoFinalResult,
            (JournalState.Applied, _, JournalCheckpoint.RestartRequired) => WorkshopPackageOutcome.RestartRequired,
            (JournalState.Applied, _, JournalCheckpoint.PackageLaunchPendingVerification) => WorkshopPackageOutcome.LaunchVerificationPending,
            (JournalState.Applied, _, _) => WorkshopPackageOutcome.Applied,
            (JournalState.Verified, JournalResult.PackageLaunchVerified, JournalCheckpoint.PackageExistingTargetVerification) =>
                WorkshopPackageOutcome.ExistingTargetVerified,
            (JournalState.Verified, JournalResult.PackageLaunchVerified, _) => WorkshopPackageOutcome.LaunchVerified,
            (JournalState.Verified, _, _) => WorkshopPackageOutcome.Verified,
            (JournalState.Skipped, _, _) => WorkshopPackageOutcome.Skipped,
            (JournalState.RolledBack, _, _) => WorkshopPackageOutcome.RolledBack,
            (_, JournalResult.Cancelled, JournalCheckpoint.PackagePreDispatchFailure) => WorkshopPackageOutcome.CancelledBeforeDispatch,
            (_, JournalResult.Cancelled, _) => WorkshopPackageOutcome.Cancelled,
            (_, JournalResult.NetworkUnavailable, _) => WorkshopPackageOutcome.NetworkUnavailable,
            (_, JournalResult.PackageDownloadUnavailable, _) => WorkshopPackageOutcome.DownloadUnavailable,
            (_, JournalResult.PackageDownloadTimedOut, _) => WorkshopPackageOutcome.DownloadTimedOut,
            (_, JournalResult.TimedOut, _) => WorkshopPackageOutcome.TimedOut,
            (_, JournalResult.AuthorizationDeclined, _) => WorkshopPackageOutcome.AuthorizationDeclined,
            (_, JournalResult.PackageElevationUnavailable, _) => WorkshopPackageOutcome.ElevationUnavailable,
            (_, JournalResult.BackendUnavailable, JournalCheckpoint.PackagePreDispatchFailure) => WorkshopPackageOutcome.BackendUnavailableBeforeDispatch,
            (_, JournalResult.BackendUnavailable, _) => WorkshopPackageOutcome.BackendUnavailable,
            (_, JournalResult.TrustMismatch, _) => WorkshopPackageOutcome.TrustChanged,
            (_, JournalResult.PackageResourcePreflightDeferred, _) => WorkshopPackageOutcome.ResourcePreflightDeferred,
            (_, JournalResult.PreconditionChanged or JournalResult.PackageDispatchAuthorizationChanged, _) => WorkshopPackageOutcome.PreconditionChanged,
            (_, JournalResult.VerificationUnconfirmed, _) => WorkshopPackageOutcome.VerificationUnconfirmed,
            (_, JournalResult.PackageDetectionAbsent, _) => WorkshopPackageOutcome.DetectionAbsent,
            (_, JournalResult.PackageDetectionMismatch, _) => WorkshopPackageOutcome.DetectionMismatch,
            (_, JournalResult.PackageDetectionUnavailable, _) => WorkshopPackageOutcome.DetectionUnavailable,
            (_, JournalResult.PackageProbeUnavailable, _) => WorkshopPackageOutcome.ProbeUnavailable,
            (_, JournalResult.PackageProbeExecutableMissing, _) => WorkshopPackageOutcome.ProbeExecutableMissing,
            (_, JournalResult.PackageProbeExecutableUntrusted, _) => WorkshopPackageOutcome.ProbeExecutableUntrusted,
            (_, JournalResult.PackageProbeLaunchFailed, _) => WorkshopPackageOutcome.ProbeLaunchFailed,
            (_, JournalResult.PackageProbeExitedEarly, _) => WorkshopPackageOutcome.ProbeExitedEarly,
            (_, JournalResult.PackageProbeTimedOut, _) => WorkshopPackageOutcome.ProbeTimedOut,
            (_, JournalResult.PackageProbeCancelled, _) => WorkshopPackageOutcome.ProbeCancelled,
            (_, JournalResult.PackageProbeCleanupUnconfirmed, _) => WorkshopPackageOutcome.ProbeCleanupUnconfirmed,
            (JournalState.Failed, _, _) => WorkshopPackageOutcome.Failed,
            _ => WorkshopPackageOutcome.PendingReview,
        };

    private sealed class PackageAttemptProgress(Guid actionId, Guid correlationId, IProgress<PackageInstallProgress> display)
        : IProgress<PackageInstallProgress>
    {
        private readonly object _gate = new();
        private bool _acceptsCancellation = true;
        private bool _completed;

        public bool AcceptsCancellation
        {
            get { lock (_gate) { return _acceptsCancellation; } }
        }

        public bool IsCurrent(PackageInstallProgress progress)
        {
            lock (_gate) { return !_completed && Matches(progress); }
        }

        private bool Matches(PackageInstallProgress progress) =>
            progress.ActionId == actionId && progress.CorrelationId == correlationId;

        public void Complete()
        {
            lock (_gate)
            {
                _completed = true;
                _acceptsCancellation = false;
            }
        }

        public Task? RequestCancellation(CancellationTokenSource cancellation)
        {
            lock (_gate)
            {
                return _acceptsCancellation ? cancellation.CancelAsync() : null;
            }
        }

        public void Report(PackageInstallProgress progress)
        {
            if (!Matches(progress))
            {
                return;
            }

            // Close the cancellation window synchronously before ApplyAsync, even while
            // WPF is waiting to display the stage change.
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                if (progress.Stage == PackageInstallStage.Installing)
                {
                    _acceptsCancellation = false;
                }
            }

            display.Report(progress);
        }
    }
}
