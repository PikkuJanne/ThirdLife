using System.Globalization;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Packages;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private static readonly TimeSpan PackageApprovalLifetime = TimeSpan.FromMinutes(10);
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
    public bool ShowsPackageRecovery => _canInspectSavedPackage && _packageService is not null;
    public bool CanCheckPackageRecovery => CanCloseJob && ShowsPackageRecovery;
    public string PackageRecoveryStatus { get; private set; } = string.Empty;
    public bool CanEditPackageOperator => CanEditJob && PlanReviewDecision is null;
    public bool CanInstallPackage => CanEditJob && !_packageNeedsReview && _packageService is not null &&
        PlanReviewDecision is { Outcome: PlanReviewOutcome.Approved } && _packageApproval is not null &&
        _packageResolution?.Status == PackageResolutionStatus.Ready;

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

    public string PackageSourceStatus => _packageActivity ?? _packageResolution?.Status switch
    {
        PackageResolutionStatus.Ready => "The signed x64 installer matches the reviewed package. No conflicting installation was found.",
        PackageResolutionStatus.AlreadyInstalled => "The exact version is already installed for all users. No installation is needed.",
        PackageResolutionStatus.Conflict => "An existing installation conflicts with this plan. Review the machine before continuing.",
        PackageResolutionStatus.TrustMismatch => "The installer did not match the reviewed source, identity or signature.",
        PackageResolutionStatus.Cancelled => "The package check was cancelled.",
        PackageResolutionStatus.TimedOut => "The package source did not respond within the check time limit.",
        PackageResolutionStatus.NetworkUnavailable => "The installer download could not reach the package source. Check the connection, then build the plan again.",
        PackageResolutionStatus.Unavailable => "The package source or installed-state check is unavailable.",
        _ => "Build the plan to check the signed installer source and current installation.",
    };

    private bool CanApprovePackagePlan => IsPackageProfile && !_packageNeedsReview && _store is IActionJournalStore &&
        _packageResolution?.Status == PackageResolutionStatus.Ready && !string.IsNullOrWhiteSpace(_packageOperatorCode);

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

            var durable = await journal.LoadJournalAsync(approval.Plan);
            if (durable?.ActiveApproval != approval || durable.StateOf(request.ActionId) != JournalState.Approved ||
                !IsFreshPackageApproval(approval))
            {
                ClearPlanReview();
                _planReviewStatus = "The saved approval is no longer current. Build and approve a fresh plan.";
                return;
            }

            if (token.IsCancellationRequested)
            {
                _packageActivity = null;
                _planReviewStatus = "Installation was cancelled before dispatch.";
                return;
            }

            var correlation = Guid.NewGuid();
            if (await AppendPackageEntryAsync(journal, approval, request, correlation, JournalState.Approved,
                JournalState.Started, JournalActor.Coordinator, PackageCoordinator, JournalResult.None,
                JournalCheckpoint.PackageDispatchIntent) != JournalWriteResult.Recorded)
            {
                return;
            }

            ClearReport();
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
                    cancellation => CanDispatchPackageAsync(journal, approval, displayed, request, correlation, cancellation),
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
        });
    }

    private bool IsFreshPackageApproval(JournalApproval approval)
    {
        var age = _timeProvider.GetUtcNow() - approval.OccurredAtUtc;
        return age >= TimeSpan.Zero && age < PackageApprovalLifetime;
    }

    private async Task<bool> CanDispatchPackageAsync(
        IActionJournalStore journal, JournalApproval approval, PlanReviewSnapshot displayed, InstallPackageActionRequest request,
        Guid correlation, CancellationToken cancellationToken)
    {
        if (!IsFreshPackageApproval(approval))
        {
            return false;
        }

        var current = await ResolveCurrentPlanAsync(cancellationToken).ConfigureAwait(false);
        var durable = await journal.LoadJournalAsync(approval.Plan, cancellationToken).ConfigureAwait(false);
        return current is not null && PackageRequest(current) is not null &&
            !PlanDiff.Compare(displayed.Plan, displayed.Impact, current, ImpactEstimator.Estimate(current)).RequiresReapproval &&
            durable?.ActiveApproval == approval &&
            durable.Entries.LastOrDefault(entry => entry.ActionId == request.ActionId) is
            { Checkpoint: JournalCheckpoint.PackageDispatchIntent, State: JournalState.Started } started &&
            started.CorrelationId == correlation && started.ApprovalId == approval.ApprovalId &&
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
            _packageActivity = "Cancellation requested. Wait while ThirdLife records the outcome.";
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

    public Task CheckPackageRecoveryAsync()
    {
        if (!CanCheckPackageRecovery || _packageService is not { } service)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            PackageRecoveryStatus = "Checking current package registration.";
            Publish();
            var observation = await Task.Run(service.Inspect);
            PackageRecoveryStatus = observation.Status switch
            {
                PackageObservationStatus.Installed when observation.ExactVersion == PilotPackageService.Identity.ExactVersion =>
                    "The approved version is currently registered machine-wide. Review the saved attempt before proceeding.",
                PackageObservationStatus.Absent => "The approved version was not found. Windows Installer may still be running. Keep this job in review.",
                PackageObservationStatus.Conflict => "A conflicting installation was found. Keep this job in review.",
                _ => "Current package registration could not be checked. Keep this job in review.",
            };
        });
    }

    private async Task<WorkshopPackageOutcome> RecordPackageResultAsync(IActionJournalStore journal, JournalApproval approval,
        InstallPackageActionRequest request, Guid correlation, PackageInstallStatus result, PilotPackageService service,
        string installerSha256, CancellationToken cancellationToken)
    {
        var (state, reason, checkpoint) = result switch
        {
            PackageInstallStatus.Applied => (JournalState.Applied, JournalResult.BackendApplied, JournalCheckpoint.PackageLaunchPendingVerification),
            PackageInstallStatus.RestartRequired => (JournalState.Applied, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired),
            PackageInstallStatus.Cancelled => (JournalState.RequiresReview, JournalResult.Cancelled, JournalCheckpoint.Reconcile),
            PackageInstallStatus.CancelledBeforeDispatch =>
                (JournalState.Failed, JournalResult.Cancelled, JournalCheckpoint.PackagePreDispatchFailure),
            PackageInstallStatus.NetworkUnavailable =>
                (JournalState.Failed, JournalResult.NetworkUnavailable, JournalCheckpoint.PackagePreDispatchFailure),
            PackageInstallStatus.TimedOut => (JournalState.RequiresReview, JournalResult.TimedOut, JournalCheckpoint.Reconcile),
            PackageInstallStatus.ReviewRequired => (JournalState.RequiresReview, JournalResult.Interrupted, JournalCheckpoint.Reconcile),
            PackageInstallStatus.AuthorizationDeclined => (JournalState.Failed, JournalResult.AuthorizationDeclined, JournalCheckpoint.None),
            PackageInstallStatus.Unavailable => (JournalState.Failed, JournalResult.BackendUnavailable, JournalCheckpoint.None),
            PackageInstallStatus.TrustMismatch => (JournalState.Failed, JournalResult.TrustMismatch, JournalCheckpoint.None),
            PackageInstallStatus.AlreadyInstalled or PackageInstallStatus.Conflict or PackageInstallStatus.PreconditionChanged =>
                (JournalState.Failed, JournalResult.PreconditionChanged, JournalCheckpoint.None),
            _ => (JournalState.Failed, JournalResult.OperationFailed, JournalCheckpoint.None),
        };
        if (await AppendPackageEntryAsync(journal, approval, request, correlation, JournalState.Started,
            state, JournalActor.PackageBackend, InstallerBackend, reason, checkpoint) != JournalWriteResult.Recorded)
        {
            throw new InvalidOperationException("The package outcome was not freshly recorded.");
        }

        if (result != PackageInstallStatus.Applied)
        {
            return ReportOutcome(state, reason, checkpoint);
        }

        // Backend application is durable before the separate, cancellable verification runs.
        _packageAttemptProgress = null;
        _packageActivity = "Checking the installed version and testing application launch.";
        Publish();
        var status = await service.VerifyInstalledAsync(request, installerSha256, cancellationToken);
        var verificationResult = status switch
        {
            PackageVerificationStatus.Passed => JournalResult.PackageLaunchVerified,
            PackageVerificationStatus.ExecutableMissing or PackageVerificationStatus.ExecutableUntrusted or
                PackageVerificationStatus.LaunchFailed => JournalResult.PackageProbeUnavailable,
            PackageVerificationStatus.ExitedEarly => JournalResult.PackageProbeExitedEarly,
            PackageVerificationStatus.TimedOut => JournalResult.PackageProbeTimedOut,
            PackageVerificationStatus.Cancelled => JournalResult.PackageProbeCancelled,
            PackageVerificationStatus.CleanupUnconfirmed => JournalResult.PackageProbeCleanupUnconfirmed,
            _ => JournalResult.VerificationUnconfirmed,
        };
        var verified = verificationResult == JournalResult.PackageLaunchVerified;
        var verifiedState = verified ? JournalState.Verified : JournalState.RequiresReview;
        var verificationCheckpoint = verified ? JournalCheckpoint.None : JournalCheckpoint.Reconcile;
        if (await AppendPackageEntryAsync(journal, approval, request, correlation, JournalState.Applied,
            verifiedState, JournalActor.Verifier, PackageVerifier, verificationResult, verificationCheckpoint,
            verified ? ActionVerification.ExactPackageIdentity : null) != JournalWriteResult.Recorded)
        {
            throw new InvalidOperationException("The package verification was not freshly recorded.");
        }

        return ReportOutcome(verifiedState, verificationResult, verificationCheckpoint);
    }

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
        var packages = await ReadPackageRecordsAsync(jobId, CancellationToken.None);
        _packageNeedsReview = packages.Any(NeedsPackageReview);
        _canInspectSavedPackage = packages.Any(package => NeedsPackageReview(package) &&
            package.RequestedVersion == PilotPackageService.Identity.ExactVersion);
        if (_canInspectSavedPackage && PackageRecoveryStatus.Length == 0)
        {
            PackageRecoveryStatus = "A registration check shows the current installed state. The saved attempt stays in review.";
        }

        PackageHistoryText = string.Join(Environment.NewLine, packages.Select(package =>
            string.Create(CultureInfo.CurrentCulture,
                $"{package.PackageId} {package.RequestedVersion}, machine-wide. {WorkshopReportRenderer.DescribePackageOutcome(package.Outcome)}")));
    }

    private void ClearPackageHistory()
    {
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
        WorkshopPackageOutcome.LaunchVerificationPending;

    private async Task<IReadOnlyList<WorkshopPackageRecord>> ReadPackageRecordsAsync(JobId jobId, CancellationToken cancellationToken)
    {
        if (_store is not IActionJournalStore journal || IsFixtureScenario)
        {
            return [];
        }

        var histories = await Task.Run(() => journal.LoadJobJournalsAsync(jobId, cancellationToken), cancellationToken);
        var identity = PilotPackageService.Identity;
        var packages = new List<WorkshopPackageRecord>();
        foreach (var history in histories)
        {
            if (history.Plan.Actions is not [{ Request: InstallPackageActionRequest { Scope: ActionScope.Machine } request }] ||
                request.SourceId != identity.SourceId || request.PackageId != identity.PackageId ||
                request.Publisher != identity.Publisher ||
                !history.Entries.Any(entry => entry.Checkpoint == JournalCheckpoint.PackagePlanApproval) ||
                history.Entries.LastOrDefault() is not { } latest)
            {
                continue;
            }

            packages.Add(new WorkshopPackageRecord(request.PackageId, request.SourceId, request.ExactVersion,
                history.Plan.Key.PlanSha256, ReportOutcome(latest.State, latest.Result, latest.Checkpoint), latest.OccurredAtUtc));
        }

        return packages;
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
            (_, JournalResult.TimedOut, _) => WorkshopPackageOutcome.TimedOut,
            (_, JournalResult.AuthorizationDeclined, _) => WorkshopPackageOutcome.AuthorizationDeclined,
            (_, JournalResult.BackendUnavailable, _) => WorkshopPackageOutcome.BackendUnavailable,
            (_, JournalResult.TrustMismatch, _) => WorkshopPackageOutcome.TrustChanged,
            (_, JournalResult.PreconditionChanged, _) => WorkshopPackageOutcome.PreconditionChanged,
            (_, JournalResult.VerificationUnconfirmed, _) => WorkshopPackageOutcome.VerificationUnconfirmed,
            (_, JournalResult.PackageProbeUnavailable, _) => WorkshopPackageOutcome.ProbeUnavailable,
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
