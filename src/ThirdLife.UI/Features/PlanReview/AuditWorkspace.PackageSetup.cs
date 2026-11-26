using System.Globalization;
using ThirdLife.Actions.Contracts;
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
    public bool CanCancelPackage => _packageCancellation is { IsCancellationRequested: false } &&
        (_packageAttemptProgress?.AcceptsCancellation ?? true);
    public bool ShowsPackageRecovery => OffersPackageRecoveryCheck || PackageRecoveryStatus.Length != 0;
    public bool OffersPackageRecoveryCheck => _canInspectSavedPackage && CanUsePackageRecovery;
    public bool CanCheckPackageRecovery => CanCloseJob && OffersPackageRecoveryCheck;
    public string PackageRecoveryStatus { get; private set; } = string.Empty;
    public bool CanEditPackageOperator => CanEditJob && PlanReviewDecision is null;
    public bool CanInstallPackage => CanEditJob && !_packageNeedsReview && _packageService is not null &&
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

    public string PackageSourceStatus => _packageActivity ?? (DescribePackageSource(_packageResolution?.Status) +
        (_packageDiagnosticUnavailable ? " Local diagnostic storage was not confirmed." : string.Empty));

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

    private bool CanApprovePackagePlan => IsPackageProfile && !_packageNeedsReview && _store is IActionJournalStore &&
        _packageResolution?.Status == PackageResolutionStatus.Ready && !string.IsNullOrWhiteSpace(_packageOperatorCode) &&
        CanStartPackagePlan;

    private async Task CheckPackageAsync()
    {
        if (_packageService is not { } service)
        {
            return;
        }

        _packageCancellation = new CancellationTokenSource();
        _packageActivity = "Checking the reviewed installer and the current installed state.";
        Publish();
        try
        {
            var token = _packageCancellation.Token;
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
        finally
        {
            _packageActivity = null;
        }
    }

    private async Task<bool> SavePackageApprovalAsync(PlanReviewSnapshot displayed)
    {
        if (_store is not IActionJournalStore journal || PackageRequest(displayed.Plan) is not { } request)
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
        var prior = existing?.StateOf(request.ActionId);
        var checkpoint = existing?.Entries.LastOrDefault()?.Checkpoint;
        if (prior != JournalState.Planned &&
            (prior, checkpoint) is not ((JournalState.Approved, JournalCheckpoint.PackagePlanApproval) or
                (JournalState.Failed, JournalCheckpoint.PackagePreDispatchFailure)))
        {
            _planReviewStatus = "This plan already has a saved transaction. Review the job history before taking further action.";
            await LoadPackageHistoryAsync(snapshot.Key.JobId);
            return false;
        }

        var approval = new JournalApproval(Guid.NewGuid(), snapshot.Key, actor, Guid.NewGuid(), _timeProvider.GetUtcNow());
        await journal.AppendJournalApprovalAsync(approval);
        await AppendPackageEntryAsync(journal, approval, request, approval.CorrelationId, prior!.Value,
            JournalState.Approved, JournalActor.Operator, approval.ActorId, JournalResult.None, JournalCheckpoint.PackagePlanApproval);
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
            _packageApproval = null;
            _packageCancellation = new CancellationTokenSource();
            var token = _packageCancellation.Token;
            Publish();
            var current = await ResolveCurrentPlanAsync();
            if (current is null || PackageRequest(current) is not { } request ||
                PlanDiff.Compare(displayed.Plan, displayed.Impact, current, ImpactEstimator.Estimate(current)).RequiresReapproval)
            {
                ClearPlanReview();
                _planReviewStatus = "The current evidence or plan changed. Build and approve a new plan before installing.";
                return;
            }

            var histories = await LoadPackageJournalsAsync(approval.Plan.JobId, CancellationToken.None);
            var durable = histories.SingleOrDefault(history => history.Plan.Key == approval.Plan);
            var eligibility = EvaluatePackageRetry(histories, request);
            if (durable?.ActiveApproval != approval || durable.Entries.LastOrDefault() is not
                { State: JournalState.Approved, Checkpoint: JournalCheckpoint.PackagePlanApproval } previous ||
                previous.ApprovalId != approval.ApprovalId || !IsFreshPackageApproval(approval) ||
                eligibility.Status is not (PackageRetryStatus.Initial or PackageRetryStatus.Ready))
            {
                ClearPlanReview();
                _planReviewStatus = "The saved approval is no longer current. Build and approve a fresh plan.";
                await LoadPackageHistoryAsync(approval.Plan.JobId);
                return;
            }

            await StartPackageAttemptAsync(journal, service, approval, displayed, current, request, previous, eligibility, token);
        });
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

        var isRetry = eligibility.Status == PackageRetryStatus.Ready;
        if (isRetry && JournalPlanSnapshot.Create(current, ImpactEstimator.Estimate(current)).Key != approval.Plan)
        {
            ClearPlanReview();
            _planReviewStatus = "The approved resolution changed. Build and approve the current plan before retrying.";
            return;
        }

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
            _planReviewStatus = "The saved approval or attempt changed before dispatch. Review the current package history.";
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
            var digest = current.Actions[0].CatalogEntry.Package.InstallerSha256!;
            var result = await Task.Run(() => service.InstallAsync(request, digest, correlation,
                cancellation => CanDispatchPackageAsync(journal, approval, displayed, request, intent, cancellation),
                stage, token));
            stage.Complete();
            var outcome = await RecordPackageResultAsync(journal, approval, request, correlation, result.Status, service, digest, token);
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

        var impact = ImpactEstimator.Estimate(current);
        return !PlanDiff.Compare(displayed.Plan, displayed.Impact, current, impact).RequiresReapproval &&
            (intent.Checkpoint != JournalCheckpoint.PackageRetryDispatchIntent || JournalPlanSnapshot.Create(current, impact).Key == approval.Plan) &&
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
        string installerSha256, CancellationToken cancellationToken)
    {
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
            PackageInstallStatus.PreconditionChanged => (JournalState.Failed, JournalResult.PackageDispatchAuthorizationChanged,
                JournalCheckpoint.PackagePreDispatchFailure, DiagnosticResultCode.PackagePreconditionChanged),
            PackageInstallStatus.AlreadyInstalled =>
                (JournalState.Failed, JournalResult.PreconditionChanged, JournalCheckpoint.None, DiagnosticResultCode.PackagePreconditionChanged),
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
        _packageAttemptProgress = null;
        _packageActivity = "Checking the installed version and testing application launch.";
        Publish();
        var status = await service.VerifyInstalledAsync(request, installerSha256, cancellationToken);
        var (verificationResult, verificationDiagnostic) = DescribePackageVerification(status);
        var verified = verificationResult == JournalResult.PackageLaunchVerified;
        var verifiedState = verified ? JournalState.Verified : JournalState.RequiresReview;
        var verificationCheckpoint = verified ? JournalCheckpoint.None : JournalCheckpoint.Reconcile;
        if (await AppendPackageEntryAsync(journal, approval, request, correlation, JournalState.Applied,
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
        RefreshPackageRetryAfterDelay();
    }

    private void ClearPackageHistory()
    {
        StopPackageRetryRefresh();
        _savedPackageRetry = default;
        _packageNeedsReview = false;
        _canInspectSavedPackage = false;
        PackageHistoryText = string.Empty;
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

    private async Task<IReadOnlyList<WorkshopPackageRecord>> ReadPackageRecordsAsync(JobId jobId, CancellationToken cancellationToken) =>
        ReadPackageRecords(await LoadPackageJournalsAsync(jobId, cancellationToken), includeEarlierAttempts: true);

    private static WorkshopPackageRecord[] ReadPackageRecords(IReadOnlyList<JournalHistory> histories, bool includeEarlierAttempts)
    {
        var packages = new List<WorkshopPackageRecord>();
        foreach (var history in histories)
        {
            if (PackageHistoryRequest(history) is not { } request || history.Entries.LastOrDefault() is not { } latest)
            {
                continue;
            }

            if (includeEarlierAttempts)
            {
                foreach (var attempt in history.Entries.Where(entry => entry.State != JournalState.Approved)
                    .GroupBy(entry => entry.CorrelationId).Select(entries => entries.Last()))
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
        var identity = PilotPackageService.Identity;
        return history.Plan.Actions is [{ Request: InstallPackageActionRequest { Scope: ActionScope.Machine } request }] &&
            request.SourceId == identity.SourceId && request.PackageId == identity.PackageId &&
            request.Publisher == identity.Publisher && history.Entries.Any(entry => entry.Checkpoint == JournalCheckpoint.PackagePlanApproval)
            ? request : null;
    }

    private static WorkshopPackageOutcome ReportOutcome(JournalState state, JournalResult result, JournalCheckpoint checkpoint) =>
        (state, result, checkpoint) switch
        {
            (JournalState.Approved, _, _) => WorkshopPackageOutcome.Approved,
            (JournalState.Started, _, _) => WorkshopPackageOutcome.NoFinalResult,
            (JournalState.Applied, _, JournalCheckpoint.RestartRequired) => WorkshopPackageOutcome.RestartRequired,
            (JournalState.Applied, _, JournalCheckpoint.PackageLaunchPendingVerification) => WorkshopPackageOutcome.LaunchVerificationPending,
            (JournalState.Applied, _, _) => WorkshopPackageOutcome.Applied,
            (JournalState.Verified, JournalResult.PackageLaunchVerified, _) => WorkshopPackageOutcome.LaunchVerified,
            (JournalState.Verified, _, _) => WorkshopPackageOutcome.Verified,
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
