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
    private static readonly OperatorId PackageVerifier = new("windows-installer-registration");
    private readonly PilotPackageService? _packageService;
    private PackageResolution? _packageResolution;
    private JournalApproval? _packageApproval;
    private CancellationTokenSource? _packageCancellation;
    private string _packageOperatorCode = string.Empty;
    private string? _packageActivity;

    public bool IsPackageProfile => SelectedPlanProfile?.PackagePolicy is not null;
    public string PackageHistoryText { get; private set; } = string.Empty;
    public bool HasPackageHistory => PackageHistoryText.Length != 0;
    public bool CanCancelPackage => _packageCancellation is { IsCancellationRequested: false };
    public bool CanEditPackageOperator => CanEditJob && PlanReviewDecision is null;
    public bool CanInstallPackage => CanEditJob && _packageService is not null &&
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
        PackageResolutionStatus.Unavailable => "The package source or installed-state check is unavailable.",
        _ => "Build the plan to check the signed installer source and current installation.",
    };

    private bool CanApprovePackagePlan => IsPackageProfile && _store is IActionJournalStore &&
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
        if (prior != JournalState.Planned &&
            !(prior == JournalState.Approved && existing!.Entries.LastOrDefault()?.Checkpoint == JournalCheckpoint.PackagePlanApproval))
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
            _packageActivity = "Preparing the signed installer. Windows may ask to approve this machine-wide change.";
            Publish();
            try
            {
                var digest = current.Actions[0].CatalogEntry.Package.InstallerSha256!;
                var progress = new Progress<PackageInstallStage>(stage =>
                {
                    if (_packageActivity is not null && !token.IsCancellationRequested && stage == PackageInstallStage.Installing)
                    {
                        _packageActivity = "Windows Installer is applying the approved change. Wait for its result.";
                        Publish();
                    }
                });
                var result = await Task.Run(() => service.InstallAsync(request, digest,
                    cancellation => CanDispatchPackageAsync(journal, approval, displayed, request, correlation, cancellation),
                    progress, token));
                await RecordPackageResultAsync(journal, approval, request, correlation, result.Status, service);
                await LoadPackageHistoryAsync(approval.Plan.JobId);
                _planReviewStatus = "Installation attempt recorded. Review the saved setup journal below.";
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
        if (CanCancelPackage)
        {
            _cancellationRequest = _packageCancellation!.CancelAsync();
            _packageActivity = "Cancellation requested. Wait while ThirdLife records the outcome.";
            Publish();
        }
    }

    private async Task RecordPackageResultAsync(IActionJournalStore journal, JournalApproval approval,
        InstallPackageActionRequest request, Guid correlation, PackageInstallStatus result, PilotPackageService service)
    {
        var (state, reason, checkpoint) = result switch
        {
            PackageInstallStatus.Applied => (JournalState.Applied, JournalResult.BackendApplied, JournalCheckpoint.None),
            PackageInstallStatus.RestartRequired => (JournalState.Applied, JournalResult.BackendApplied, JournalCheckpoint.RestartRequired),
            PackageInstallStatus.Cancelled => (JournalState.RequiresReview, JournalResult.Cancelled, JournalCheckpoint.Reconcile),
            PackageInstallStatus.TimedOut or PackageInstallStatus.ReviewRequired =>
                (JournalState.RequiresReview, JournalResult.Interrupted, JournalCheckpoint.Reconcile),
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

        if (result == PackageInstallStatus.Applied)
        {
            var observation = await Task.Run(service.Inspect);
            var verified = observation.Status == PackageObservationStatus.Installed && observation.ExactVersion == request.ExactVersion;
            await AppendPackageEntryAsync(journal, approval, request, correlation, JournalState.Applied,
                verified ? JournalState.Verified : JournalState.RequiresReview, JournalActor.Verifier, PackageVerifier,
                verified ? JournalResult.VerificationPassed : JournalResult.VerificationUnconfirmed,
                verified ? JournalCheckpoint.None : JournalCheckpoint.Reconcile,
                verified ? ActionVerification.ExactPackageIdentity : null);
        }
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
        return !plan.SyntheticData && plan.BlockedReason is null && plan.Actions is [var action] &&
            action.Request is InstallPackageActionRequest { Scope: ActionScope.Machine, Architecture: ActionArchitecture.X64 } request &&
            request.SourceId == identity.SourceId && request.PackageId == identity.PackageId &&
            request.ExactVersion == identity.ExactVersion && request.Publisher == identity.Publisher &&
            action.CatalogEntry.Package.InstallerSha256 == identity.InstallerSha256 ? request : null;
    }

    private async Task LoadPackageHistoryAsync(JobId jobId)
    {
        var packages = await ReadPackageRecordsAsync(jobId, CancellationToken.None);
        PackageHistoryText = string.Join(Environment.NewLine, packages.Select(package =>
            string.Create(CultureInfo.CurrentCulture,
                $"{package.PackageId} {package.RequestedVersion}, machine-wide. {WorkshopReportRenderer.DescribePackageOutcome(package.Outcome)}")));
    }

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
                history.Plan.Key.PlanSha256, ReportOutcome(latest), latest.OccurredAtUtc));
        }

        return packages;
    }

    private static WorkshopPackageOutcome ReportOutcome(JournalEntry entry) => (entry.State, entry.Result, entry.Checkpoint) switch
    {
        (JournalState.Approved, _, _) => WorkshopPackageOutcome.Approved,
        (JournalState.Applied, _, JournalCheckpoint.RestartRequired) => WorkshopPackageOutcome.RestartRequired,
        (JournalState.Applied, _, _) => WorkshopPackageOutcome.Applied,
        (JournalState.Verified, _, _) => WorkshopPackageOutcome.Verified,
        (JournalState.RolledBack, _, _) => WorkshopPackageOutcome.RolledBack,
        (_, JournalResult.Cancelled, _) => WorkshopPackageOutcome.Cancelled,
        (_, JournalResult.AuthorizationDeclined, _) => WorkshopPackageOutcome.AuthorizationDeclined,
        (_, JournalResult.BackendUnavailable, _) => WorkshopPackageOutcome.BackendUnavailable,
        (_, JournalResult.TrustMismatch, _) => WorkshopPackageOutcome.TrustChanged,
        (_, JournalResult.PreconditionChanged, _) => WorkshopPackageOutcome.PreconditionChanged,
        (_, JournalResult.VerificationUnconfirmed, _) => WorkshopPackageOutcome.VerificationUnconfirmed,
        (JournalState.Failed, _, _) => WorkshopPackageOutcome.Failed,
        _ => WorkshopPackageOutcome.PendingReview,
    };
}
