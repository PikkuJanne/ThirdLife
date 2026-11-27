using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView : UserControl
{
    private AuditWorkspace? _workspace;
    private DispatcherOperation? _announcement;
    private string? _displayedJob;
    private string? _displayedReviewAssessment;
    private string? _lastStatus;
    private string? _lastOperationError;
    private string? _lastAssessmentStatus;
    private string? _lastReportStatus;
    private string? _lastFinalizationStatus;
    private bool _sanitizationDateInvalid;
    private ManualTestProgressItem? _displayedManualItem;
    private readonly ManualCheckOption[] _manualOptions;

    private ShellStage? _shownStage;
    private bool? _wideEvidence;
    private readonly HashSet<TextBlock> _pendingAnnouncements = [];

    internal event EventHandler<ShellStage?>? StageRequested;

    public AuditWorkspaceView()
    {
        _manualOptions = ManualTestCatalog.Cases.Select(static check => new ManualCheckOption(check)).ToArray();
        InitializeComponent();
        ManualCheck.ItemsSource = _manualOptions;
    }

    private AuditWorkspace Workspace =>
        _workspace ?? throw new InvalidOperationException("The audit workspace is unavailable.");

    internal void Bind(AuditWorkspace workspace)
    {
        _workspace = workspace;
        DataContext = workspace;
        _lastStatus = workspace.JobEntryStatus;
        _lastOperationError = workspace.OperationError;
        _lastAssessmentStatus = workspace.AssessmentStatus;
        _lastReportStatus = workspace.ReportStatus;
        _lastFinalizationStatus = workspace.FinalizationStatus;
        FixtureNotice.Visibility = VisibleWhen(workspace.IsFixtureScenario);
        FixtureManualNotice.Visibility = FixtureNotice.Visibility;
        ManualPassOption.IsEnabled = !workspace.IsFixtureScenario;
        ManualFailOption.IsEnabled = !workspace.IsFixtureScenario;
        if (workspace.IsFixtureScenario)
        {
            SanitizationInstructions.Text = "Select Use this synthetic intake record, then Save external result to enable assessment for this isolated job.";
            ExternalRecordConfirmation.Text = "Use this synthetic intake record";
            AutomationProperties.SetName(ExternalRecordConfirmed, ExternalRecordConfirmation.Text);
            AssessmentInstructions.Text = "Assessment reads the synthetic fixtures and records their findings in this job. It leaves device readiness unknown.";
        }

        workspace.PropertyChanged += WorkspaceChanged;
        RefreshWorkspace();
    }

    internal void Unbind()
    {
        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= WorkspaceChanged;
        }

        _workspace = null;
        _announcement?.Abort();
        _pendingAnnouncements.Clear();
    }

    internal void ShowStage(ShellStage? stage)
    {
        var enteringHandover = _shownStage != ShellStage.Handover && stage == ShellStage.Handover;
        _shownStage = stage;
        JobRegion.Visibility = VisibleWhen(stage is null);
        IntakeSurface.Visibility = VisibleWhen(stage == ShellStage.Intake);
        InspectSurface.Visibility = VisibleWhen(stage == ShellStage.Inspect);
        DecideSurface.Visibility = VisibleWhen(stage == ShellStage.Decide);
        PrepareSurface.Visibility = VisibleWhen(stage == ShellStage.Prepare);
        VerifySurface.Visibility = VisibleWhen(stage == ShellStage.Verify);
        HandoverSurface.Visibility = VisibleWhen(stage == ShellStage.Handover);
        if (stage != ShellStage.Handover) { ClearArtifactTargets(); }
        _workspace?.ShowFinalization(stage == ShellStage.Handover);
        if (enteringHandover)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (_shownStage == ShellStage.Handover && _workspace is { } workspace)
                {
                    _ = workspace.RefreshFinalizationAsync();
                }
            }));
        }
    }

    internal void FocusJobs() => FocusAndReveal(CreateJobButton.IsEnabled ? CreateJobButton : JobRegion);

    private async void CreateJob(object sender, RoutedEventArgs e)
    {
        string previousJob = Workspace.JobId;
        JobRegion.Focus();
        await Workspace.CreateJobAsync();
        if (_shownStage is null && Workspace.JobId.Length != 0 && Workspace.JobId != previousJob)
        {
            StageRequested?.Invoke(this, ShellStage.Intake);
            FocusSanitization();
        }
        else if (_shownStage is null)
        {
            FocusAndReveal(AuditStatus);
        }
    }

    private async void OpenJob(object sender, RoutedEventArgs e)
    {
        string requested = JobIdInput.Text.Trim();
        JobRegion.Focus();
        await Workspace.OpenJobAsync(requested);
        if (_shownStage is null && string.Equals(Workspace.JobId, requested, StringComparison.Ordinal) && requested.Length != 0)
        {
            var destination = Workspace.Assessment is not null || Workspace.CanAssess ? ShellStage.Inspect : ShellStage.Intake;
            StageRequested?.Invoke(this, destination);
            if (destination == ShellStage.Intake)
            {
                FocusSanitization();
            }
            else
            {
                FocusAndReveal(AssessmentMessage);
            }
        }
        else if (_shownStage is null)
        {
            FocusAndReveal(JobIdInput);
        }
    }

    private async void ChangeJobArchive(object sender, RoutedEventArgs e)
    {
        if (!Workspace.CanChangeJobArchive || Workspace.Job is not { } job) { return; }
        FocusAndReveal(AuditStatus);
        await Workspace.ChangeJobArchiveAsync(job.Job.JobId, archive: !job.IsArchived);
    }

    private void CloseJob(object sender, RoutedEventArgs e)
    {
        Workspace.CloseJob();
        StageRequested?.Invoke(this, null);
        FocusAndReveal(JobIdInput);
    }

    private void GoToSanitization(object sender, RoutedEventArgs e)
    {
        StageRequested?.Invoke(this, ShellStage.Intake);
        FocusSanitization();
    }

    private void GoToDisposition(object sender, RoutedEventArgs e)
    {
        StageRequested?.Invoke(this, ShellStage.Decide);
        FocusAndReveal(PolicyDispositionPanel);
    }

    private void FocusSanitization()
    {
        FrameworkElement target = OperatorIdInput;
        if (Workspace.CanCompleteSavedSanitization)
        {
            target = CompleteSavedSanitizationButton;
        }
        else if (Workspace.IsFixtureScenario && ExternalRecordConfirmed.IsVisible)
        {
            target = ExternalRecordConfirmed.IsChecked == true ? RecordSanitizationButton : ExternalRecordConfirmed;
        }

        FocusAndReveal(target.IsEnabled ? target : SanitizationRegion);
    }

    private static void FocusAndReveal(FrameworkElement target)
    {
        target.Focus();
        target.BringIntoView();
    }

    private void ResizeFindings(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 680;
        if (_wideEvidence == wide) { return; }
        _wideEvidence = wide;
        EvidenceColumn.Width = new GridLength(wide ? 288 : 0);
        Grid.SetColumn(EvidenceInspector, wide ? 1 : 0);
        Grid.SetRow(EvidenceInspector, wide ? 0 : 1);
        EvidenceInspector.Margin = wide ? new Thickness(24, 0, 0, 0) : new Thickness(0, 16, 0, 0);
    }

    private void FindingSelected(object sender, SelectionChangedEventArgs e)
    {
        var finding = AssessmentObservations.SelectedItem as AssessmentFinding;
        EvidenceInspector.Visibility = VisibleWhen(finding is not null);
        ShowEvidenceButton.IsEnabled = finding is not null;
        BatterySafety.Visibility = VisibleWhen(finding?.Provider.Contains("battery", StringComparison.Ordinal) == true);
    }

    private void CloseEvidence(object sender, RoutedEventArgs e)
    {
        if (AssessmentObservations.SelectedItem is { } selected)
        {
            AssessmentObservations.ScrollIntoView(selected);
            AssessmentObservations.UpdateLayout();
            if (AssessmentObservations.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
            {
                FocusAndReveal(item);
            }
        }

        EvidenceInspector.Visibility = Visibility.Collapsed;
    }

    private void ShowSelectedEvidence(object sender, RoutedEventArgs e)
    {
        if (AssessmentObservations.SelectedItem is AssessmentFinding)
        {
            EvidenceInspector.Visibility = Visibility.Visible;
            FocusAndReveal(BackToFindings);
        }
    }

    private async void GenerateWorkshopReport(object sender, RoutedEventArgs e)
    {
        FocusReportControl(ReportMessage);
        await Workspace.GenerateReportAsync();
        if (ReportMessage.IsKeyboardFocused && Workspace.CanOpenReport)
        {
            FocusReportControl(OpenReportButton);
        }
    }

    private async void OpenWorkshopReport(object sender, RoutedEventArgs e)
    {
        FocusReportControl(ReportMessage);
        await Workspace.OpenReportAsync();
        if (ReportMessage.IsKeyboardFocused && Workspace.ReportPreview.Length != 0)
        {
            FocusReportControl(WorkshopReportPreview);
        }
    }

    private void CancelReport(object sender, RoutedEventArgs e)
    {
        FocusReportControl(ReportMessage);
        Workspace.CancelReport();
    }

    private void FocusReportControl(FrameworkElement target)
    {
        if (WorkshopReportRegion.IsKeyboardFocusWithin || Keyboard.FocusedElement is null)
        {
            target.Focus();
            target.BringIntoView();
        }
    }

    private async void RecordSanitization(object sender, RoutedEventArgs e)
    {
        SetError(SanitizationInputError, "");
        if (SanitizationResult.SelectedValue is not SanitizationState state)
        {
            SetError(SanitizationInputError, "Choose the external outcome before saving.");
            FocusAndReveal(SanitizationResult);
            return;
        }

        bool needsExternalRecord = state != SanitizationState.Unknown;
        if (needsExternalRecord && string.IsNullOrWhiteSpace(OperatorIdInput.Text))
        {
            SetError(SanitizationInputError, "Enter a workshop operator code.");
            FocusAndReveal(OperatorIdInput);
            return;
        }

        if (needsExternalRecord && (_sanitizationDateInvalid || SanitizationDate.SelectedDate is null))
        {
            SetError(SanitizationInputError, "Choose a valid date for the external result.");
            FocusAndReveal(SanitizationDate);
            return;
        }

        if (needsExternalRecord && ExternalRecordConfirmed.IsChecked != true)
        {
            SetError(SanitizationInputError, Workspace.IsFixtureScenario
                ? "Select Use this synthetic intake record before saving."
                : "Confirm ownership and the external record before saving this outcome.");
            FocusAndReveal(ExternalRecordConfirmed);
            return;
        }

        if (UsesMethod(state) && SanitizationMethod.SelectedIndex < 0)
        {
            SetError(SanitizationInputError, "Choose the method shown in the external record.");
            FocusAndReveal(SanitizationMethod);
            return;
        }

        bool tracksMedia = state is SanitizationState.Verified or SanitizationState.ReplacementStorage or SanitizationState.Failed;
        var input = new SanitizationInput(
            state,
            needsExternalRecord ? OperatorIdInput.Text.Trim() : "",
            needsExternalRecord && SanitizationDate.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null,
            tracksMedia ? SanitizationMediaId.Text.Trim() : "",
            needsExternalRecord && ExternalRecordConfirmed.IsChecked == true,
            UsesMethod(state) ? SanitizationMethod.SelectedValue as string ?? "" : "");
        await SaveSanitization(() => Workspace.RecordSanitizationAsync(input));
    }

    private async void CompleteSavedSanitization(object sender, RoutedEventArgs e) =>
        await SaveSanitization(Workspace.CompleteSavedSanitizationAsync);

    private async Task SaveSanitization(Func<Task> save)
    {
        if (SanitizationRegion.IsKeyboardFocusWithin)
        {
            SanitizationRegion.Focus();
        }

        var previous = LatestSanitization()?.Metadata.EvidenceId;
        await save();
        bool committed = LatestSanitization()?.Metadata.EvidenceId != previous;
        if (committed)
        {
            ExternalRecordConfirmed.IsChecked = false;
        }

        if (_shownStage != ShellStage.Intake) { return; }
        if (SanitizationRegion.IsKeyboardFocused && Workspace.CanAssess)
        {
            StageRequested?.Invoke(this, ShellStage.Inspect);
            FocusAndReveal(StartAssessmentButton);
        }
        else if (Workspace.CanCompleteSavedSanitization)
        {
            FocusAndReveal(CompleteSavedSanitizationButton);
        }
        else if (!committed && !Workspace.CanAssess && Workspace.OperationError.Length == 0)
        {
            SetError(SanitizationInputError, Workspace.Status);
        }
    }

    private void SanitizationResultChanged(object sender, SelectionChangedEventArgs e)
    {
        var state = SanitizationResult.SelectedValue as SanitizationState?;
        SanitizationMethodFields.Visibility = VisibleWhen(state is { } result && UsesMethod(result));
        SanitizationEventFields.Visibility = VisibleWhen(state is not (null or SanitizationState.Unknown));
        SanitizationMediaFields.Visibility = VisibleWhen(state is not SanitizationState.NoDonorStorage);
        ExternalRecordConfirmed.IsChecked = false;
        SanitizationInputError.Text = "";
    }

    private void InvalidSanitizationDate(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        _sanitizationDateInvalid = true;
        e.ThrowException = false;
        SetError(SanitizationInputError, "Choose a valid date for the external result.");
    }

    private void SanitizationDateChanged(object sender, SelectionChangedEventArgs e)
    {
        _sanitizationDateInvalid = false;
        SanitizationInputError.Text = "";
    }

    private async void StartAssessment(object sender, RoutedEventArgs e)
    {
        Task assessment = Workspace.StartAssessmentAsync();
        if (Workspace.IsAssessmentRunning)
        {
            AssessmentMessage.Focus();
        }

        await assessment;
    }

    private void CancelAssessment(object sender, RoutedEventArgs e)
    {
        Workspace.CancelAssessment();
        AssessmentMessage.Focus();
    }

    private async void SaveManualResult(object sender, RoutedEventArgs e)
    {
        SetError(ManualInputError, "");
        if (ManualCheck.SelectedItem is not ManualCheckOption selected ||
            ManualOutcome.SelectedValue is not HumanTestResult outcome)
        {
            SetError(ManualInputError, "Choose a manual check and the result you observed.");
            return;
        }

        var reason = ManualUnavailableReason.SelectedValue as ManualTestUnavailableReason?;
        if (outcome == HumanTestResult.NotAvailable && reason is null)
        {
            SetError(ManualInputError, "Choose why this check is unavailable.");
            return;
        }

        var previous = SelectedManualProgress()?.Latest?.HumanTestId;
        await Workspace.RecordManualAsync(
            selected.Case.Id,
            outcome,
            OperatorIdInput.Text.Trim(),
            ManualNotes.Text.Trim(),
            reason);
        if (SelectedManualProgress()?.Latest is { } saved && saved.HumanTestId != previous)
        {
            ManualOutcome.SelectedIndex = -1;
            ManualNotes.Clear();
            Announce(ManualCurrentResult);
        }
    }

    private void SelectNextManualCheck(object sender, RoutedEventArgs e)
    {
        ManualCheck.SelectedValue = Workspace.ManualProgress?.NextCase?.Id;
        ManualCheck.Focus();
    }

    private void ManualCheckChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = (ManualCheck.SelectedItem as ManualCheckOption)?.Case;
        ManualInstructions.Visibility = VisibleWhen(selected is not null);
        ManualUnavailableReason.ItemsSource = selected?.UnavailableReasons.Select(static value =>
            new UnavailableChoice(value, UnavailableLabel(value))).ToArray();
        ManualOutcome.SelectedIndex = -1;
        ManualNotes.Clear();
        ManualInputError.Text = "";
        RefreshManualProgress();
    }

    private void ManualOutcomeChanged(object sender, SelectionChangedEventArgs e)
    {
        bool isUnavailable = ManualOutcome.SelectedValue is HumanTestResult.NotAvailable;
        ManualUnavailableFields.Visibility = VisibleWhen(isUnavailable);
        if (!isUnavailable)
        {
            ManualUnavailableReason.SelectedIndex = -1;
        }

        ManualInputError.Text = "";
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshWorkspace();

        var assessmentChanged = _lastAssessmentStatus != Workspace.AssessmentStatus;
        var statusChanged = _lastStatus != Workspace.JobEntryStatus;
        var errorChanged = _lastOperationError != Workspace.OperationError;
        var reportChanged = _lastReportStatus != Workspace.ReportStatus;
        var finalizationChanged = _lastFinalizationStatus != Workspace.FinalizationStatus;
        _lastAssessmentStatus = Workspace.AssessmentStatus;
        _lastStatus = Workspace.JobEntryStatus;
        _lastOperationError = Workspace.OperationError;
        _lastReportStatus = Workspace.ReportStatus;
        _lastFinalizationStatus = Workspace.FinalizationStatus;
        if (assessmentChanged) { Announce(AssessmentMessage); }
        if (statusChanged && Workspace.OperationError.Length == 0) { Announce(AuditStatus); }
        if (errorChanged) { Announce(OperationErrorMessage); }
        if (reportChanged) { Announce(ReportMessage); }
        if (finalizationChanged) { Announce(FinalizationMessage); }
    }

    private void RefreshWorkspace()
    {
        RefreshJob();
        RefreshActionAvailability();
        RefreshManualProgress();
        RefreshFinalizationChecklist();
        RefreshRecipientHandover();
        RefreshRecipientGuide();
        RefreshSupportExport();
        RefreshHandoverCompletion();
        RefreshToggleKeys();
        RefreshBackupReadiness();
        JobHeader.Visibility = VisibleWhen(Workspace.Job is not null);
        JobArchiveRegion.Visibility = VisibleWhen(Workspace.ShowsJobArchive);
        OperationErrorRegion.Visibility = VisibleWhen(Workspace.OperationError.Length != 0);
        AuditStatus.Visibility = VisibleWhen(Workspace.OperationError.Length == 0);
        JobIdentity.Content = Workspace.Assessment?.Observations.FirstOrDefault(observation =>
            observation.EvidenceKey.Value == "device.model" && observation.Value?.Kind == EvidenceValueKind.Text)
            ?.Value?.StringValue ?? "Workshop job";
        JobState.Text = Workspace.Job?.IsArchived == true ? "Archived job · read-only"
            : Workspace.IsBusy ? "Job operation in progress" : "Job opened";
        BrokerExecutionPanel.Visibility = VisibleWhen(Workspace.IncludeBrokerQualification && Workspace.ShowsBrokerExecution);
        ReportArtifactDetails.Text = Workspace.ReportArtifact is { } artifact
            ? $"Saved workshop artifact: {artifact.FileName}"
            : "No workshop report is available for preview in this session.";
        var snapshot = Workspace.Assessment;
        FindingsSummary.Text = snapshot is null
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"{snapshot.Observations.Count} saved observations · {snapshot.Providers.Count(provider => provider.Outcome is null)} providers not run");
        var saved = LatestSanitization();
        SavedSanitization.Text = saved is null ? "No external result is saved for this job." : DescribeSanitization(saved);
    }

    private SanitizationEvidence? LatestSanitization() =>
        Workspace.Job?.SanitizationEvidence is { Count: > 0 } entries ? entries[^1] : null;

    private void RefreshJob()
    {
        if (_displayedJob == Workspace.Job?.Job.JobId.Value)
        {
            return;
        }

        _displayedJob = Workspace.Job?.Job.JobId.Value;
        if (_displayedJob is not null)
        {
            JobIdInput.Text = _displayedJob;
        }

        InspectionTabs.SelectedItem = DeviceFindingsTab;
        AssessmentObservations.SelectedIndex = -1;
        SanitizationResult.SelectedIndex = -1;
        SanitizationMethod.SelectedIndex = -1;
        OperatorIdInput.Clear();
        SanitizationDate.SelectedDate = null;
        SanitizationMediaId.Clear();
        ExternalRecordConfirmed.IsChecked = false;
        _sanitizationDateInvalid = false;
        SanitizationInputError.Text = "";
        ManualInputError.Text = "";
        ManualCheck.SelectedValue = _displayedJob is null
            ? null
            : (Workspace.ManualProgress?.NextCase ?? ManualTestCatalog.Cases[0]).Id;
    }

    private ManualTestProgressItem? SelectedManualProgress() =>
        ManualCheck.SelectedItem is ManualCheckOption selected
            ? _workspace?.ManualProgress?.Items.FirstOrDefault(item => item.Case.Id == selected.Case.Id)
            : null;

    private void RefreshManualProgress()
    {
        var securityReview = ManualCheck.SelectedItem is ManualCheckOption selected &&
            ManualTestCatalog.IsSecurityReview(selected.Case.Id);
        ManualNotesFields.Visibility = VisibleWhen(!securityReview);
        SecurityReviewScope.Visibility = VisibleWhen(securityReview);
        var assessmentId = _workspace?.SecurityReviewAssessmentId;
        if (securityReview && _displayedReviewAssessment != assessmentId)
        {
            ManualOutcome.SelectedIndex = -1;
        }
        _displayedReviewAssessment = assessmentId;
        SecurityReviewScope.Text = assessmentId is not null
            ? "Security review results apply to this assessment. A new assessment requires a fresh review."
            : "Complete an assessment under the current intake before recording this review.";
        SaveManualResultButton.IsEnabled = !securityReview || assessmentId is not null;

        var progress = _workspace?.ManualProgress;
        foreach (var option in _manualOptions)
        {
            var latest = progress?.Items.FirstOrDefault(item => item.Case.Id == option.Case.Id)?.Latest;
            option.Update(latest?.TestResult);
        }

        string next = progress?.NextCase is { } candidate
            ? $"Next unfinished check: {candidate.Title}."
            : progress is null ? "Open a job to see saved progress." : "Each check has a recorded outcome.";
        ManualResumeStatus.Text = progress?.HasEssentialFailure == true
            ? $"An essential failure remains a blocker candidate. {next}"
            : next;
        NextManualCheckButton.IsEnabled = progress?.NextCase is not null;
        var item = SelectedManualProgress();
        var record = item?.Latest;
        ManualCurrentResult.Text = record is null
            ? "No result has been saved for this check."
            : $"Latest saved result: {DescribeManualResult(record)}";
        if (!ReferenceEquals(item, _displayedManualItem))
        {
            _displayedManualItem = item;
            ManualHistory.ItemsSource = item?.History.TakeLast(10).Select(DescribeManualResult).ToArray();
        }
    }

    private static string DescribeSanitization(SanitizationEvidence record)
    {
        var evidence = record.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture
            ? "Synthetic fixture"
            : EvidenceLabel(record.Metadata.EvidenceClassification);
        if (record.OccurredAtUtc is not { } occurred)
        {
            return $"Latest saved external result: unknown. {evidence}.";
        }

        var method = record.MethodCode.Replace('_', ' ');
        var verification = record.VerificationState == SanitizationVerificationState.Verified ? "verified" : "verification failed";
        var media = record.MediaIdentifier is { } mediaCode ? $" Workshop media code {mediaCode.Value}." : "";
        return string.Create(
            CultureInfo.CurrentCulture,
            $"Latest saved external result: {method}, {verification}. {evidence}. Operator {record.OperatorId?.Value}, {occurred.UtcDateTime:d}.{media}");
    }

    private static string DescribeManualResult(HumanTestEvidence record)
    {
        var evidence = record.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture
            ? "Synthetic fixture"
            : EvidenceLabel(record.Metadata.EvidenceClassification);
        var limitation = record.LimitationCode?.Replace('_', ' ');
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{OutcomeLabel(record.TestResult)}. {evidence}. Operator {record.OperatorId.Value}, {record.Metadata.CollectedAtUtc.ToLocalTime():g}. {limitation} {record.Details?.Notes}");
    }

    private void RefreshActionAvailability()
    {
        bool available = !Workspace.IsBusy;
        CreateJobButton.IsEnabled = available;
        JobIdInput.IsEnabled = available;
        OpenJobButton.IsEnabled = available;
        GoToSanitizationButton.Visibility = VisibleWhen(Workspace.CanEditJob && !Workspace.CanAssess);
        AssessmentGateMessage.Visibility = VisibleWhen(!Workspace.IsBusy && !Workspace.CanAssess);
        CancelAssessmentButton.Visibility = VisibleWhen(Workspace.IsAssessmentRunning);
        bool hasAssessment = Workspace.Assessment is not null;
        ReviewDispositionButton.Visibility = VisibleWhen(hasAssessment);
        SetPrimaryAction(StartAssessmentButton, !hasAssessment);
        SetPrimaryAction(ReviewDispositionButton, hasAssessment);
        SetPrimaryAction(GenerateReportButton, Workspace.ReportArtifact is null);
        SetPrimaryAction(OpenReportButton, Workspace.ReportArtifact is not null);
        ShowEvidenceButton.IsEnabled = AssessmentObservations.SelectedItem is not null;
        SanitizationFields.Visibility = VisibleWhen(Workspace.Job?.IsArchived != true &&
            !Workspace.CanCompleteSavedSanitization);
        CompleteSavedSanitizationButton.Visibility = VisibleWhen(Workspace.CanCompleteSavedSanitization);
        AuditAssessmentProgress.Visibility = VisibleWhen(Workspace.IsAssessmentRunning);
        CancelReportButton.Visibility = VisibleWhen(Workspace.CanCancelReport);
        WorkshopReportPreview.Visibility = VisibleWhen(Workspace.ReportPreview.Length != 0);
    }

    private static void SetPrimaryAction(Button button, bool primary)
    {
        var style = primary ? button.TryFindResource("Trace.PrimaryButton") : null;
        if (style is Style primaryStyle)
        {
            button.Style = primaryStyle;
        }
        else
        {
            button.ClearValue(StyleProperty);
        }
    }

    private void SetError(TextBlock region, string text)
    {
        region.Text = text;
        if (text.Length != 0)
        {
            Announce(region);
        }
    }

    private void Announce(TextBlock region)
    {
        if (!region.IsVisible) { return; }
        _pendingAnnouncements.Add(region);
        _announcement ??= Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _announcement = null;
            foreach (var pending in _pendingAnnouncements.Where(text => text.IsVisible))
            {
                var peer = UIElementAutomationPeer.FromElement(pending) ?? UIElementAutomationPeer.CreatePeerForElement(pending);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }

            _pendingAnnouncements.Clear();
        }));
    }

    private static bool UsesMethod(SanitizationState state) => state is SanitizationState.Verified or SanitizationState.Failed;

    private static Visibility VisibleWhen(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static string OutcomeLabel(HumanTestResult result) => result switch
    {
        HumanTestResult.Pass => "Pass",
        HumanTestResult.Fail => "Fail",
        HumanTestResult.NotRun => "Not run",
        _ => "Not available",
    };

    private static string EvidenceLabel(EvidenceClassification evidence) => evidence switch
    {
        EvidenceClassification.HumanConfirmed => "Human confirmed",
        EvidenceClassification.NotAvailable => "Not available",
        EvidenceClassification.Inferred => "Inferred",
        _ => "Observed",
    };

    private static string UnavailableLabel(ManualTestUnavailableReason reason) => reason switch
    {
        ManualTestUnavailableReason.CapabilityAbsent => "Capability absent",
        ManualTestUnavailableReason.EquipmentMissing => "Equipment missing",
        ManualTestUnavailableReason.ProviderUnavailable => "Provider unavailable",
        ManualTestUnavailableReason.UnsafeToRun => "Unsafe to run",
        ManualTestUnavailableReason.PermissionDenied => "Permission denied",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private sealed class ManualCheckOption(ManualTestCase testCase) : INotifyPropertyChanged
    {
        private HumanTestResult? _lastResult;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ManualTestCase Case { get; } = testCase;
        public string Label => $"{Case.Title} · {(_lastResult is { } result ? OutcomeLabel(result) : "No result saved")}";

        public void Update(HumanTestResult? result)
        {
            if (_lastResult == result) { return; }
            _lastResult = result;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    private sealed record UnavailableChoice(ManualTestUnavailableReason Value, string Label);
}
