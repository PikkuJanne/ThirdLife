using System.ComponentModel;
using System.Globalization;
using System.IO;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Repairs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using ThirdLife.Packages;
using ThirdLife.Packages.WindowsUpdates;
using ThirdLife.Persistence;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.Reports.RecipientGuide;
using ThirdLife.Reports.Storage;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.BrokerClient;
using ThirdLife.UI.Features.Finalization;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed record SanitizationInput(
    SanitizationState State,
    string OperatorCode,
    DateOnly? OccurredOn,
    string MediaIdentifier,
    bool ConfirmedExternalRecord,
    string MethodCode = "");

internal sealed record AssessmentFinding(
    string Name,
    string Value,
    string Evidence,
    string Provider,
    string DisplayName,
    string CollectedAt,
    string TechnicalDetails);

/// <summary>Owns one window's open job and serializes commands while the shell stays navigable.</summary>
internal sealed partial class AuditWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    private const string NoAssessment = "No assessment is recorded for this job.";
    private static readonly ProviderId IntakeProvider = new("workshop-intake");
    private readonly Func<CancellationToken, Task<IJobStore>> _openStore;
    private readonly IInventoryProvider[] _providers;
    private readonly TimeProvider _timeProvider;
    private readonly WorkshopReportStore? _reportStore;
    private IJobStore? _store;
    private JobService? _jobs;
    private ManualTestService? _manualTests;
    private AssessmentOrchestrator? _orchestrator;
    private CancellationTokenSource? _assessmentCancellation;
    private CancellationTokenSource? _reportCancellation;
    private Task _cancellationRequest = Task.CompletedTask;
    private Task _active = Task.CompletedTask;
    private bool _closing;
    private bool _hasSeparateEvidenceOrigin;

    public AuditWorkspace(
        Func<CancellationToken, Task<IJobStore>> openStore,
        IReadOnlyList<IInventoryProvider> providers,
        TimeProvider timeProvider,
        WorkshopReportStore? reportStore = null,
        Func<PolicyDeviceClass, PolicySnapshot?>? fixturePlanPolicy = null,
        PilotPackageService? packageService = null,
        Func<IBrokerExecutionJournal, ExecutionCoordinator>? fixtureBrokerCoordinator = null,
        Func<StructuredDiagnosticEvent, CancellationToken, Task>? appendPackageDiagnostic = null,
        Func<DateTimeOffset, PreparationResourceSnapshot>? observePreparationResources = null,
        IWindowsUpdateAgent? fixtureWindowsUpdates = null,
        RecipientGuideStore? recipientGuideStore = null, IRecipientToggleKeys? fixtureToggleKeys = null,
        FileHistoryRestoreProbe? backupSampleProbe = null)
    {
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (providers.Count is < 1 or > AssessmentOrchestrator.MaximumProviders)
        {
            throw new ArgumentException("A bounded assessment roster is required.", nameof(providers));
        }

        _openStore = openStore;
        _providers = providers.ToArray();
        _timeProvider = timeProvider;
        _reportStore = reportStore;
        _recipientGuideStore = recipientGuideStore;
        _backupSampleProbe = backupSampleProbe;
        IsFixtureScenario = _providers.All(static provider =>
            provider.Descriptor.EvidenceOrigin == ProviderEvidenceOrigin.SyntheticFixture);
        if (!IsFixtureScenario && _providers.Any(static provider =>
            provider.Descriptor.EvidenceOrigin == ProviderEvidenceOrigin.SyntheticFixture))
        {
            throw new ArgumentException("Fixture and live providers require separate workspaces.", nameof(providers));
        }

        if (fixturePlanPolicy is not null && !IsFixtureScenario)
        {
            throw new ArgumentException("A fixture plan policy requires a fixture workspace.", nameof(fixturePlanPolicy));
        }

        _fixturePlanPolicy = fixturePlanPolicy;
        if (fixtureWindowsUpdates is not null && (!IsFixtureScenario || fixtureWindowsUpdates is WindowsUpdateAgent))
        {
            throw new ArgumentException("The fixture update agent requires a synthetic workspace.", nameof(fixtureWindowsUpdates));
        }

        if (fixtureToggleKeys is not null && (!IsFixtureScenario || fixtureToggleKeys is WindowsToggleKeys))
        {
            throw new ArgumentException("The fixture settings adapter requires a synthetic workspace.", nameof(fixtureToggleKeys));
        }
        _toggleKeys = fixtureToggleKeys;
        UpdateAgent = fixtureWindowsUpdates;
        UpdateBackend = fixtureWindowsUpdates as IWindowsUpdateDispatchBackend;
        if (fixtureBrokerCoordinator is not null && !IsFixtureScenario)
        {
            throw new ArgumentException("A fixture coordinator requires a fixture workspace.", nameof(fixtureBrokerCoordinator));
        }

        _fixtureBrokerCoordinator = fixtureBrokerCoordinator;
        if (packageService is not null && IsFixtureScenario)
        {
            throw new ArgumentException("Live package execution requires a live workspace.", nameof(packageService));
        }

        _packageService = packageService;
        _observePreparationResources = observePreparationResources ?? ObserveWindowsPreparationResources;
        _appendPackageDiagnostic = appendPackageDiagnostic;
        PlanProfiles = packageService is not null && WorkshopPackageProfile.Load(timeProvider.GetUtcNow()) is { } package
            ? [.. WorkshopPlanCandidate.Options, package]
            : WorkshopPlanCandidate.Options;
        VisiblePlanProfiles = IsFixtureScenario || IncludeBrokerQualification
            ? PlanProfiles
            : PlanProfiles.Where(option => option.PackagePolicy is not null).ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StoredJob? Job { get; private set; }
    public string JobId => Job?.Job.JobId.Value ?? string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string OperationError { get; private set; } = string.Empty;
    public string JobEntryStatus => IsBusy ? "A job operation is in progress." : Status;
    public string AssessmentStatus { get; private set; } = NoAssessment;
    public AssessmentSnapshot? Assessment { get; private set; }
    public IReadOnlyList<AssessmentFinding> Findings { get; private set; } = [];
    public ManualTestProgress? ManualProgress { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsAssessmentRunning => _assessmentCancellation is not null;
    public bool CanCancel => IsAssessmentRunning && !_assessmentCancellation!.IsCancellationRequested;
    public bool CanCloseJob => Job is not null && !IsBusy && !_closing;
    public bool CanEditJob => CanCloseJob && Job is { IsArchived: false } && !_hasSeparateEvidenceOrigin;
    public bool ShowsJobArchive => Job is not null && !IsFixtureScenario && !_hasSeparateEvidenceOrigin;
    public bool CanChangeJobArchive => ShowsJobArchive && CanCloseJob && !JobDeletionPending;
    public string JobArchiveAction => Job?.IsArchived == true ? "Restore archived job" : "Archive job";
    public bool IsFixtureScenario { get; }
    public bool IncludeBrokerQualification => _fixtureBrokerCoordinator is not null;
    private HandoverFileReceipt? _recordedWorkshopReport;
    public WorkshopReportArtifact? ReportArtifact { get; private set; }
    public string? SavedReportFileName => ReportArtifact?.FileName ?? _recordedWorkshopReport?.FileName;
    public string ReportPreview { get; private set; } = string.Empty;
    public string ReportStatus { get; private set; } =
        "Open a job to generate its restricted workshop report.";
    public bool CanGenerateReport => CanCloseJob && _reportStore is not null && !_hasSeparateEvidenceOrigin &&
        !JobDeletionPending && _store is IJobDataLifecycleStore;
    public bool CanOpenReport => CanCloseJob && _reportStore is not null && !_hasSeparateEvidenceOrigin && Job is { } job &&
        (ReportArtifact?.JobId == job.Job.JobId || _recordedWorkshopReport?.Output.Scope.JobId == job.Job.JobId);
    public bool CanCancelReport => _reportCancellation is { IsCancellationRequested: false };

    private SanitizationGateStatus? Gate => Job is null ? null : SanitizationGate.Inspect(Job);

    internal string? SecurityReviewAssessmentId => CurrentReviewAssessment(Job, Assessment);

    private static string? CurrentReviewAssessment(StoredJob? job, AssessmentSnapshot? assessment) =>
        job is not null && assessment is { Outcome: AssessmentOutcome.Completed or AssessmentOutcome.RequiresReview } &&
        SanitizationGate.Inspect(job) is { AllowsAssessment: true, Decision: { } gate } &&
        assessment.JobId == job.Job.JobId && assessment.GateDecisionId == gate.DecisionId
            ? assessment.AssessmentId.Value : null;

    public bool CanAssess => CanEditJob && Gate is { AllowsAssessment: true };

    public bool CanCompleteSavedSanitization => CanEditJob &&
        Gate?.Reason is SanitizationGateReason.GateDecisionMissing or SanitizationGateReason.GateDecisionStale;

    public string GateStatus => _hasSeparateEvidenceOrigin
        ? "The saved evidence belongs to a different test or live scenario. Assessment is blocked."
        : Gate?.Reason switch
        {
            null => "Open a job to record external sanitization evidence.",
            SanitizationGateReason.SanitizationEvidenceMissing => "Save the external sanitization result before starting assessment.",
            SanitizationGateReason.GateDecisionMissing or SanitizationGateReason.GateDecisionStale =>
                "The external record is saved. Check the saved record to complete its gate decision.",
            SanitizationGateReason.SanitizationUnknown => "Sanitization is unknown. Review the external record before assessment.",
            SanitizationGateReason.SanitizationFailed => "Sanitization failed. Assessment is blocked pending verified external evidence.",
            SanitizationGateReason.JobArchived => "This job is archived. Its saved records are read-only.",
            _ => "Sanitization permits read-only assessment. Device readiness is unknown.",
        };

    internal static AuditWorkspace CreateLive(Func<CancellationToken, Task<IJobStore>>? openStore = null) => new(
        openStore ?? (async cancellationToken =>
            await SqliteJobStore.OpenWithColdBootAsync(WindowsColdBootEvidence.Read, cancellationToken).ConfigureAwait(false)),
        [
            new ActivationProvider(),
            new BatteryReportProvider(),
            new DeviceEnumerationProvider(),
            new FirmwareSecurityProvider(),
            new OperatingSystemProvider(),
            new PendingRebootProvider(),
            new SecurityBaselineProvider(),
            new StorageProvider(),
            new SystemDiskHealthProvider(),
            new SystemInventoryProvider(),
        ],
        TimeProvider.System,
        RegisteredReportStore(),
        packageService: new PilotPackageService(),
        appendPackageDiagnostic: AppendLocalPackageDiagnosticAsync,
        recipientGuideStore: RegisteredRecipientGuideStore())
    {
        UpdateAgent = new WindowsUpdateAgent(),
        UpdateBackend = new ProtectedWindowsUpdateBackend(),
    };

    private static WorkshopReportStore? RegisteredReportStore()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.IsPathFullyQualified(localData)
            ? new WorkshopReportStore(Path.Combine(localData, "ThirdLife", "SetupCore", "WorkshopReports"))
            : null;
    }

    public Task CreateJobAsync() => RunAsync(async () =>
    {
        await OpenStoreAsync();
        var created = await Task.Run(() => _jobs!.CreateAsync(DeviceId.New()));
        await ApplyJobAsync(await LoadJobAsync(created.JobId));
        Status = "Job created. Save the external sanitization result before starting assessment.";
    });

    public Task OpenJobAsync(string jobId) => RunAsync(async () =>
    {
        var id = new JobId(jobId.Trim());
        await OpenStoreAsync();
        var stored = await Task.Run(() => _jobs!.ReopenAsync(id));
        if (stored is null)
        {
            Status = "No job was found for this code. Check the code and try again.";
            return;
        }

        var alreadyOpen = Job?.Job.JobId == id;
        _profileVerificationFeedback = string.Empty;
        await ApplyJobAsync(stored, refreshPreparation: false);
        if (alreadyOpen)
        {
            await LoadWindowsUpdateHistoryAsync(id);
            var previousPackageHistory = PackageHistoryText;
            await LoadPackageHistoryAsync(id);
            if (!string.Equals(previousPackageHistory, PackageHistoryText, StringComparison.Ordinal))
            {
                ClearReport();
            }

            await LoadBrokerHistoryAsync(id, reconcile: true);
        }

        await ReconcileSavedPackageAsync(id);
        await RefreshPreparationDispositionAsync(id, invalidateReport: false);
        await LoadRecordedWorkshopReportAsync(id);
        Status = await LoadPendingJobDeletionAsync(stored)
            ? "This archived job has an approved deletion waiting to finish. Open Jobs to review and continue."
            : "Job opened. The committed records are shown below.";
    });

    public Task ChangeJobArchiveAsync(JobId jobId, bool archive)
    {
        if (!CanChangeJobArchive || Job is not { } current || current.Job.JobId != jobId ||
            current.IsArchived == archive)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            await Task.Run(() => archive ? _jobs!.ArchiveAsync(jobId) : _jobs!.RestoreAsync(jobId));
            var stored = await LoadJobAsync(jobId);
            ClearJobDeletionReview();
            ClearFinalization();
            ClearSupportExport();
            ClearReport();
            await ApplyJobAsync(stored);
            await LoadRecordedWorkshopReportAsync(jobId);
            Status = stored.IsArchived
                ? "The job is archived and its saved records remain available to review."
                : "The archived job was restored. You can resume work from its saved records.";
        });
    }

    public void CloseJob()
    {
        if (IsBusy || _closing) { return; }
        ClearOpenJob();
        Status = "Job closed. Reopen it by its workshop job ID.";
        Publish();
    }

    private void ClearOpenJob()
    {
        Job = null;
        OperationError = string.Empty;
        _hasSeparateEvidenceOrigin = false;
        ManualProgress = null;
        ApplyAssessment(null);
        ClearPlanReview(clearSelection: true);
        ClearPackageHistory();
        ClearWindowsUpdateHistory();
        ClearBrokerHistory();
        ClearPreparationDisposition();
        ClearProfileVerification();
        ClearRebootRecovery();
        ClearRepairs();
        ClearPolicyView();
        ClearReport();
        ClearFinalization();
        ClearSupportExport();
        ClearHandoverReview();
        ClearJobDeletionReview();
    }

    public Task RecordSanitizationAsync(SanitizationInput input) => RunAsync(async () =>
    {
        var job = RequireWritableJob();
        var evidence = CreateSanitizationEvidence(input);
        await Task.Run(() => _store!.AppendEvidenceAsync(new JobEvidenceBatch(
            job.Job.JobId, sanitizationEvidence: [evidence])));
        await ApplyJobAsync(await LoadJobAsync(job.Job.JobId));
        await CompleteGateAsync(job.Job.JobId, evidence.Metadata.EvidenceId);
    });

    public Task CompleteSavedSanitizationAsync()
    {
        if (!CanCompleteSavedSanitization) { return Task.CompletedTask; }
        return RunAsync(() => CompleteGateAsync(Job!.Job.JobId, Gate!.EvidenceId!));
    }

    public Task StartAssessmentAsync() => StartAssessmentAsync(linkedRepairId: null);

    private Task StartAssessmentAsync(EvidenceId? linkedRepairId)
    {
        if (IsBusy || _closing) { return Task.CompletedTask; }
        if (!CanAssess)
        {
            Status = GateStatus;
            Publish();
            return Task.CompletedTask;
        }

        var jobId = Job!.Job.JobId;
        if (linkedRepairId is not null && !RepairJournal.CanRetest(Job, linkedRepairId))
        {
            Status = "This repair needs verified current sanitization evidence before a linked retest.";
            Publish();
            return Task.CompletedTask;
        }

        _assessmentCancellation = new CancellationTokenSource();
        var cancellationToken = _assessmentCancellation.Token;
        Assessment = null;
        ClearPlanReview();
        ClearPolicyView();
        AssessmentStatus = "Read-only assessment is running. Device readiness is unknown.";
        Findings = [];
        RepairCandidates = [];
        return RunAsync(async () =>
        {
            try
            {
                var snapshot = await Task.Run(() => linkedRepairId is null
                    ? _orchestrator!.AssessAsync(jobId, cancellationToken)
                    : _orchestrator!.RetestAsync(jobId, linkedRepairId, cancellationToken));
                await ApplyJobAsync(await LoadJobAsync(jobId), snapshot);
                if (linkedRepairId is not null) { ClearReport(); }
                Status = AssessmentStatus;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ApplyJobAsync(await LoadJobAsync(jobId));
                Status = "Assessment was cancelled before a new run was saved.";
            }
            catch (AssessmentException exception)
            {
                await ApplyJobAsync(await LoadJobAsync(jobId));
                Status = exception.ResultCode switch
                {
                    "assessment_busy" => "The previous assessment is still releasing its resources. Try again after it finishes.",
                    "assessment_cleanup_incomplete" => "Assessment cleanup needs review. Close and reopen the application before retrying.",
                    "assessment_gate_blocked" or "assessment_fixture_gate_blocked" => GateStatus,
                    _ => "Assessment stopped. Review the committed results before retrying.",
                };
            }
        });
    }

    public void CancelAssessment()
    {
        if (!CanCancel) { return; }
        _cancellationRequest = _assessmentCancellation!.CancelAsync();
        AssessmentStatus = "Cancellation requested. Waiting for the assessment to stop and save its result.";
        Status = AssessmentStatus;
        Publish();
    }

    public Task RecordManualAsync(string caseId, HumanTestResult outcome, string operatorCode,
        string notes, ManualTestUnavailableReason? reason) => RunAsync(async () =>
    {
        var job = RequireWritableJob();
        if (IsFixtureScenario && job.SanitizationEvidence.Count == 0)
        {
            throw new InvalidOperationException("A fixture job requires a labelled fixture intake record.");
        }

        var assessmentId = ManualTestCatalog.IsSecurityReview(caseId)
            ? SecurityReviewAssessmentId ?? throw new InvalidOperationException("Complete the current assessment before recording a security review.")
            : null;
        await Task.Run(() => _manualTests!.RecordAsync(job.Job.JobId, caseId, outcome,
            new OperatorId(operatorCode.Trim()), notes.Trim(), reason, assessmentId: assessmentId));
        await ApplyJobAsync(await LoadJobAsync(job.Job.JobId));
        ClearReport();
        Status = "Manual result saved. You can close and reopen this job to continue.";
    });

    public async Task CloseAsync()
    {
        _closing = true;
        CancelAssessment();
        CancelWindowsUpdate();
        CancelProfileVerification();
        CancelReport();
        CancelArtifactScan();
        CancelSupport();
        StopPackageRetryRefresh();
        StopWaitingForPackageOnClose();
        CancelBrokerExecution();
        try
        {
            await Task.WhenAll(_active, _finalizationRead);
        }
        finally
        {
            if (_store is { } store)
            {
                _store = null;
                await store.DisposeAsync();
            }
        }
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    public Task GenerateReportAsync()
    {
        if (!CanGenerateReport) { return Task.CompletedTask; }
        var jobId = Job!.Job.JobId;
        return RunReportAsync("Generating the workshop report from saved evidence.", async cancellationToken =>
        {
            ClearHandoverReview();
            var reviewScope = await ReadPublicationScopeAsync(jobId, cancellationToken);
            var finalization = await ReadFinalizationForReportAsync(jobId, cancellationToken);
            var stored = await LoadJobAsync(jobId, cancellationToken);
            if (IsSeparateEvidenceOrigin(stored))
            {
                throw new InvalidOperationException("The job belongs to a separate evidence scenario.");
            }

            var backupSample = await ReadBackupSampleForReportAsync(stored, cancellationToken);
            var assessment = await Task.Run(() => AssessmentOrchestrator.ReadLatest(stored), cancellationToken);
            var summary = SummarizeAssessment(assessment);
            var policy = await FindSavedPolicyAsync(jobId, assessment, cancellationToken);
            var journals = await ReadPreparationJournalsAsync(jobId, cancellationToken);
            var checkpoint = await ReadMaintenanceForReportAsync(jobId, cancellationToken);
            var verification = await ReadProfileVerificationForReportAsync(jobId, cancellationToken);
            var coldBoot = await ReadColdBootVerificationAsync(jobId, cancellationToken);
            await ConfirmReportEvidenceAsync(stored, finalization, backupSample, cancellationToken);
            var now = _timeProvider.GetUtcNow();
            var artifact = await Task.Run(() =>
            {
                var packages = ReadPackageRecords(journals, includeEarlierAttempts: true);
                var preparation = ProjectPreparationRecords(stored, assessment, journals, checkpoint, verification, now, cancellationToken);
                var content = WorkshopReportRenderer.RenderTechnicalRecord(stored, summary, policy, packages,
                    preparation, ProjectProfileVerification(verification, coldBoot),
                    ProjectWorkshopRecordDetails(jobId, journals, finalization, backupSample),
                    cancellationToken);
                return _reportStore!.WriteWithOwnershipAsync(jobId, content,
                    (output, publish, token) => PublishOwnedJobFileAsync(JobLocalFileKind.WorkshopReport, output, publish, token),
                    cancellationToken);
            }, cancellationToken);
            ReportArtifact = artifact;
            ReportPreview = string.Empty;
            var recorded = await RecordPublishedHandoverFileAsync(reviewScope, HandoverOutputKind.WorkshopRecord, artifact, cancellationToken);
            ReportStatus = recorded ? "Workshop report saved. Open it here to review its findings and limits."
                : "Workshop report saved. The handover evidence changed before the receipt was recorded. Generate a current copy before sign-off.";
        });
    }

    public Task OpenReportAsync()
    {
        if (!CanOpenReport) { return Task.CompletedTask; }
        var jobId = Job!.Job.JobId;
        var artifact = ReportArtifact?.JobId == jobId ? ReportArtifact : null;
        var saved = _recordedWorkshopReport;
        ReportPreview = string.Empty;
        return RunReportAsync("Opening the saved workshop report.", async cancellationToken =>
        {
            ReportPreview = await Task.Run(() => artifact is null
                ? _reportStore!.ReadSavedReport(jobId, saved!, cancellationToken)
                : _reportStore!.Read(artifact, cancellationToken), cancellationToken);
            ReportStatus = artifact is null
                ? "Saved workshop report opened. This copy reflects the evidence recorded when it was generated."
                : "Restricted workshop report opened. Review the saved findings and limits.";
        });
    }

    private async Task LoadRecordedWorkshopReportAsync(JobId jobId)
    {
        if (IsFixtureScenario || _hasSeparateEvidenceOrigin || _reportStore is null ||
            ReportArtifact is not null || _store is not IHandoverCompletionStore store)
        {
            return;
        }

        _recordedWorkshopReport = null;
        ReportPreview = string.Empty;
        try
        {
            _recordedWorkshopReport = await Task.Run(() => store.ReadLatestWorkshopReportReceiptAsync(jobId));
            ReportStatus = _recordedWorkshopReport is null
                ? "Generate a restricted workshop report from the saved job."
                : "A saved workshop report is available for this job.";
        }
        catch (JobStoreException)
        {
            ReportStatus = "The saved report receipt could not be read. Generate a current report to continue.";
        }
    }

    public void CancelReport()
    {
        if (!CanCancelReport) { return; }
        _cancellationRequest = _reportCancellation!.CancelAsync();
        ReportStatus = "Report operation cancellation requested.";
        Publish();
    }

    private Task RunReportAsync(string activity, Func<CancellationToken, Task> command)
    {
        _reportCancellation = new CancellationTokenSource();
        var cancellationToken = _reportCancellation.Token;
        ReportStatus = activity;
        return RunAsync(async () =>
        {
            try
            {
                await command(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReportStatus = "Report operation cancelled. Previously saved reports are preserved.";
            }
            catch (WorkshopReportStoreException exception)
            {
                ReportStatus = exception.Failure switch
                {
                    WorkshopReportFailure.NameCollision =>
                        "A report already uses that filename. Existing output was preserved. Generate a new report to try again.",
                    WorkshopReportFailure.DestinationUnavailable =>
                        "The report destination is unavailable. Try again after access is restored.",
                    WorkshopReportFailure.ArtifactChanged =>
                        "The saved report has changed. Generate a fresh report from the job.",
                    WorkshopReportFailure.ReadFailed =>
                        "The saved report could not be opened. Generate a new report from the job.",
                    WorkshopReportFailure.ContentTooLarge or WorkshopReportFailure.CapacityExceeded =>
                        "The report could not be saved within the local storage limits. Existing reports were preserved.",
                    _ => "The workshop report could not be saved. Previously saved reports were preserved.",
                };
            }
            catch (Exception exception) when (exception is AssessmentException or JobStoreException or JobLifecycleException
                or ArgumentException or InvalidOperationException)
            {
                ReportStatus = "The workshop report could not be generated from this job. Reopen the job and review its saved records.";
            }
        }, refreshFinalization: false);
    }

    private Task RunAsync(Func<Task> command, bool refreshFinalization = true)
    {
        if (IsBusy || _closing) { return Task.CompletedTask; }
        if (refreshFinalization)
        {
            _finalizationReadVersion++;
            ClearHandoverReview();
        }

        IsBusy = true;
        OperationError = string.Empty;
        Publish();
        _active = ExecuteAsync(command, refreshFinalization);
        return _active;
    }

    private async Task ExecuteAsync(Func<Task> command, bool refreshFinalization)
    {
        try
        {
            await command();
        }
        catch (ArgumentException)
        {
            Status = OperationError = "Check the required fields. Use a workshop identifier and an allowed outcome, date or reason.";
        }
        catch (JobLifecycleException)
        {
            Status = OperationError = "The job state changed. Reopen it to review the saved records before retrying.";
        }
        catch (AssessmentException)
        {
            Status = OperationError = "The saved assessment could not be read safely. Review the job before retrying.";
        }
        catch (JobStoreException)
        {
            Status = OperationError = "The local job store could not complete this operation. Reopen the job to check what was saved.";
        }
        catch (InvalidOperationException)
        {
            Status = OperationError = "The operation is unavailable for this job. Check its current state and try again.";
        }
        finally
        {
            try { await _cancellationRequest; }
            catch (AggregateException) { /* The orchestrator records the provider's actual outcome. */ }
            _cancellationRequest = Task.CompletedTask;
            _assessmentCancellation?.Dispose();
            _assessmentCancellation = null;
            _updateCancellation?.Dispose();
            _updateCancellation = null;
            _profileVerificationCancellation?.Dispose();
            _profileVerificationCancellation = null;
            _postBootVerificationRunning = false;
            _reportCancellation?.Dispose();
            _reportCancellation = null;
            _supportCancellation?.Dispose();
            _supportCancellation = null;
            _packageCancellation?.Dispose();
            _packageCancellation = null;
            _packageAttemptProgress = null;
            _packageRecoveryRunning = false;
            _packageActivity = null;
            _brokerCancellation?.Dispose();
            _brokerCancellation = null;
            try
            {
                if (_showFinalization && (refreshFinalization || Finalization is null))
                {
                    await LoadFinalizationAsync();
                }
            }
            finally
            {
                IsBusy = false;
                Publish();
            }
        }
    }

    private async Task OpenStoreAsync()
    {
        if (_store is not null) { return; }
        var store = await Task.Run(() => _openStore(CancellationToken.None));
        try
        {
            _orchestrator = new AssessmentOrchestrator(store, _providers, _timeProvider);
            _jobs = new JobService(store, _timeProvider);
            _manualTests = new ManualTestService(store, _timeProvider);
            if (store is IBrokerExecutionJournal brokerJournal)
            {
                // Only a fixture can supply a backend until the ordinary dispatch candidate has privilege authorization.
                _brokerExecution = IsFixtureScenario ? _fixtureBrokerCoordinator?.Invoke(brokerJournal)
                    : new ExecutionCoordinator(brokerJournal, timeProvider: _timeProvider);
            }

            _store = store;
        }
        catch
        {
            await store.DisposeAsync();
            throw;
        }
    }

    private Task<StoredJob> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default) => Task.Run(async () =>
        await _jobs!.ReopenAsync(jobId, cancellationToken).ConfigureAwait(false) ??
        throw new InvalidOperationException("The job is unavailable."), cancellationToken);

    private async Task CompleteGateAsync(JobId jobId, EvidenceId evidenceId)
    {
        await Task.Run(() => _jobs!.CompleteIntakeAsync(jobId, evidenceId));
        await ApplyJobAsync(await LoadJobAsync(jobId));
        Status = GateStatus;
    }

    private StoredJob RequireWritableJob() => !_hasSeparateEvidenceOrigin && Job is { IsArchived: false } job
        ? job
        : throw new InvalidOperationException("An open, writable job is required.");

    private SanitizationEvidence CreateSanitizationEvidence(SanitizationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var known = input.State != SanitizationState.Unknown;
        var now = _timeProvider.GetUtcNow();
        var method = input.State switch
        {
            SanitizationState.Verified or SanitizationState.Failed
                when input.MethodCode is "clear" or "purge" or "destroy" => input.MethodCode,
            SanitizationState.ReplacementStorage => "replacement_storage",
            SanitizationState.NoDonorStorage => "no_donor_storage",
            SanitizationState.Unknown => "unknown",
            _ => throw new ArgumentException("Select the recorded sanitization method.", nameof(input)),
        };
        if (known && (!input.ConfirmedExternalRecord || input.OccurredOn is null ||
            input.OccurredOn > DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime)))
        {
            throw new ArgumentException("Confirm the external record and provide a non-future event date.", nameof(input));
        }

        var needsMedia = input.State is SanitizationState.Verified or SanitizationState.ReplacementStorage or SanitizationState.Failed;
        var media = input.MediaIdentifier.Trim();
        if (needsMedia && (media.Length is 0 or > 128 ||
            media.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
        {
            throw new ArgumentException("Enter a bounded workshop media identifier.", nameof(input));
        }

        var classification = !known ? EvidenceClassification.NotAvailable :
            IsFixtureScenario ? EvidenceClassification.Observed : EvidenceClassification.HumanConfirmed;
        var provenance = IsFixtureScenario ? ProvenanceKind.SyntheticFixture :
            known ? ProvenanceKind.HumanConfirmation : ProvenanceKind.SystemGenerated;
        var verification = input.State switch
        {
            SanitizationState.Unknown => SanitizationVerificationState.NotAvailable,
            SanitizationState.Failed => SanitizationVerificationState.Failed,
            _ => SanitizationVerificationState.Verified,
        };
        var metadata = new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
            classification, IntakeProvider, now, new EvidenceProvenance(provenance, "m1-intake"),
            known ? ValueAvailability.Available : ValueAvailability.Unknown);
        return new SanitizationEvidence(metadata, input.State, method,
            known ? new OperatorId(input.OperatorCode.Trim()) : null,
            known ? new DateTimeOffset(input.OccurredOn!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            needsMedia ? new MediaIdentifier(media) : null,
            verification, SanitizationGate.WorkshopIntakePolicyVersion);
    }

    private async Task ApplyJobAsync(StoredJob job, AssessmentSnapshot? currentRun = null, bool refreshPreparation = true)
    {
        var (manual, snapshot, separateOrigin, repairHistory) = await Task.Run(() =>
        {
            var assessment = currentRun ?? AssessmentOrchestrator.ReadLatest(job);
            return (ManualTestService.GetProgress(job, CurrentReviewAssessment(job, assessment)), assessment,
                IsSeparateEvidenceOrigin(job), RepairJournal.GetHistory(job));
        });
        var jobChanged = Job?.Job.JobId != job.Job.JobId;
        var previousGate = Gate?.Decision?.DecisionId;
        Job = job;
        if (jobChanged)
        {
            ClearJobDeletionReview();
            ClearFinalization();
            ClearSupportExport();
            ClearHandoverReview();
            ClearBrokerHistory();
            ClearPackageHistory();
            ClearWindowsUpdateHistory();
            ClearPreparationDisposition();
            ClearProfileVerification();
            ClearReport();
            ClearRepairs();
            ClearRebootRecovery();
        }
        else if (job.IsArchived || previousGate != Gate?.Decision?.DecisionId)
        {
            ClearWindowsUpdateOffers();
        }

        _hasSeparateEvidenceOrigin = separateOrigin;
        ManualProgress = manual;
        ApplyAssessment(snapshot);
        ClearPlanReview(clearSelection: jobChanged);
        ApplyRepairs(repairHistory.Repairs, repairHistory.Retests);
        await LoadPolicyViewAsync(job.Job.JobId, snapshot);
        await LoadRebootRecoveryAsync(job.Job.JobId);
        if (jobChanged)
        {
            await LoadWindowsUpdateHistoryAsync(job.Job.JobId);
            await LoadPackageHistoryAsync(job.Job.JobId);
            await LoadBrokerHistoryAsync(job.Job.JobId, reconcile: true);
        }

        if (!jobChanged && refreshPreparation) { await LoadMaintenanceCheckpointAsync(job.Job.JobId); }

        if (refreshPreparation)
        {
            await LoadPreparationDispositionAsync(job, snapshot);
        }
    }

    private bool IsSeparateEvidenceOrigin(StoredJob job) =>
        job.SanitizationEvidence.Any(entry =>
            (entry.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) != IsFixtureScenario) ||
        job.Observations.Any(observation => IsFixtureScenario
            ? observation.Metadata.Provenance.Kind is ProvenanceKind.ProviderObservation or ProvenanceKind.ImportedRecord
            : observation.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ||
        job.HumanTests.Any(attempt =>
            (attempt.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) != IsFixtureScenario);

    private void ClearReport()
    {
        _recordedWorkshopReport = null;
        ReportArtifact = null;
        ReportPreview = string.Empty;
        ReportStatus = _reportStore is null
            ? "Workshop report storage is unavailable."
            : Job is null
                ? "Open a job to generate its restricted workshop report."
                : "Generate a restricted workshop report from the saved job.";
    }

    private static WorkshopAssessmentSummary? SummarizeAssessment(AssessmentSnapshot? snapshot)
    {
        if (snapshot is null) { return null; }
        var outcome = snapshot.Outcome switch
        {
            AssessmentOutcome.Incomplete => WorkshopAssessmentOutcome.Incomplete,
            AssessmentOutcome.Completed => WorkshopAssessmentOutcome.Completed,
            AssessmentOutcome.Cancelled => WorkshopAssessmentOutcome.Cancelled,
            _ => WorkshopAssessmentOutcome.RequiresReview,
        };
        return new WorkshopAssessmentSummary(
            snapshot.JobId, snapshot.GateDecisionId, snapshot.StartedAtUtc, snapshot.FinishedAtUtc,
            outcome, snapshot.Providers.Where(provider => provider.Outcome is null)
                .Select(provider => provider.ProviderId).ToArray(), snapshot.AssessmentId.Value);
    }

    private void ApplyAssessment(AssessmentSnapshot? snapshot)
    {
        Assessment = snapshot;
        var summary = snapshot?.Outcome switch
        {
            null => NoAssessment,
            AssessmentOutcome.Completed => "Assessment complete. Review the saved findings and any unavailable checks. Device readiness is unknown.",
            AssessmentOutcome.Cancelled => "Assessment cancelled. Committed partial results are shown.",
            AssessmentOutcome.Incomplete => "An assessment was interrupted. Committed partial results are shown.",
            _ => "Assessment needs review. Check failed, unavailable and unfinished findings.",
        };
        AssessmentStatus = snapshot is not null && Gate?.Decision?.DecisionId != snapshot.GateDecisionId
            ? $"Historical assessment under an earlier sanitization decision. {summary}"
            : summary;
        Findings = snapshot?.Providers.SelectMany(provider => ProjectFindings(snapshot, provider)).ToArray() ?? [];
    }

    private static IEnumerable<AssessmentFinding> ProjectFindings(
        AssessmentSnapshot snapshot, AssessmentProviderSnapshot provider)
    {
        var providerName = provider.ProviderId.Value;
        yield return new AssessmentFinding("Provider collection", provider.Outcome switch
        {
            null => "Not run",
            ProviderRunOutcome.Completed => "Collected",
            ProviderRunOutcome.AccessDenied => "Access denied",
            ProviderRunOutcome.TimedOut => "Timed out",
            ProviderRunOutcome.InvalidData or ProviderRunOutcome.ContractInvalid => "Data needs review",
            ProviderRunOutcome.CleanupIncomplete => "Cleanup needs review",
            ProviderRunOutcome.Unavailable => "Not available",
            ProviderRunOutcome.Cancelled => "Cancelled",
            _ => "Failed",
        }, "Provider collection", providerName, $"Collection · {providerName.Replace('-', ' ')}", "",
            $"Assessment {snapshot.AssessmentId.Value}");

        foreach (var observation in provider.Observations)
        {
            var value = observation.Value;
            var displayValue = value?.Kind switch
            {
                EvidenceValueKind.Boolean => value.BooleanValue!.Value ? "Yes" : "No",
                EvidenceValueKind.WholeNumber => value.IntegerValue!.Value.ToString(CultureInfo.CurrentCulture),
                EvidenceValueKind.DecimalNumber => value.DecimalValue!.Value.ToString(CultureInfo.CurrentCulture),
                EvidenceValueKind.Text or EvidenceValueKind.Enum => value.StringValue!,
                _ => observation.Metadata.ValueAvailability == ValueAvailability.NotApplicable
                    ? "Not applicable" : "Not available",
            };
            var metadata = observation.Metadata;
            var detail = metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture
                ? "Synthetic fixture" : metadata.EvidenceClassification switch
                {
                    EvidenceClassification.NotAvailable => "Not available",
                    EvidenceClassification.HumanConfirmed => "Human confirmed",
                    _ => metadata.EvidenceClassification.ToString(),
                };
            if (observation.LimitationCode is { } limitation)
            {
                detail = $"{detail} ({limitation.Replace('_', ' ')})";
            }

            string key = observation.EvidenceKey.Value;
            string displayName = key switch
            {
                "security.firewall_health" => "Firewall health reported by Windows Security Center",
                "security.antivirus_health" => "Antivirus health reported by Windows Security Center",
                "security.defender_health" => "Microsoft Defender real-time protection and signature health",
                "security.system_volume_protection" => "BitLocker protection on the Windows volume",
                "security.uac_health" => "User Account Control health reported by Windows Security Center",
                "security.uac_configured" => "User Account Control setting",
                "security.uac_state" => "User Account Control setting and reported health",
                "security.remote_desktop_configured" => "Windows Remote Desktop connection setting",
                "security.ntp_client_configured" => "Windows network time client setting",
                "security.last_successful_sync_utc" => "Last successful Windows time synchronization (UTC)",
                "ownership.domain_join" => "Windows domain membership",
                "ownership.mdm_registration" => "Device management registration",
                "ownership.cloud_join" => "Device or current-user work account registration",
                "ownership.lock_state" => "Ownership review",
                "memory.installed_bytes" => "Installed memory (bytes)",
                "device.model" => "Device model",
                "device.manufacturer" => "Device manufacturer",
                "os.family" => "Windows family",
                "system.architecture" => "System architecture",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(key.Replace('.', ' ').Replace('_', ' ')),
            };
            if (providerName == "windows-security-baseline")
            {
                if (key == "security.last_successful_sync_utc" && value is not null)
                {
                    displayValue = WorkshopReportRenderer.WorkshopSyncTime(observation) ?? "Not available";
                }

                detail += key switch
                {
                    "security.firewall_health" => ". Category health covers registered firewall protection. Individual firewall profiles need separate inspection.",
                    "security.antivirus_health" => ". Category health only. Product approval needs separate evidence.",
                    "security.defender_health" => ". Windows reports Defender service, antivirus, real-time protection and signature status.",
                    "security.system_volume_protection" => ". This read-only check covers the Windows volume.",
                    "security.remote_desktop_configured" => ". Other remote support tools and administrator accounts need separate review.",
                    "security.ntp_client_configured" => ". Windows Time supplies the last successful synchronization separately.",
                    "security.last_successful_sync_utc" => ". The candidate policy checks the age of this Windows Time observation at evaluation.",
                    "ownership.lock_state" => ". Unresolved management or ownership blocks handover.",
                    _ => string.Empty,
                };
            }
            string technicalDetails = $"Assessment {snapshot.AssessmentId.Value}\nEvidence {metadata.EvidenceId.Value}\nField {key}\nProvenance {metadata.Provenance.SourceReference}";
            yield return new AssessmentFinding(key, displayValue, detail, providerName, displayName,
                metadata.CollectedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), technicalDetails);
        }
    }

    private void Publish() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
