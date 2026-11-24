using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
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
    private string? _lastStatus;
    private string? _lastAssessmentStatus;
    private bool _sanitizationDateInvalid;
    private ManualTestProgressItem? _displayedManualItem;

    internal event EventHandler? ReturnToStagesRequested;

    public AuditWorkspaceView()
    {
        InitializeComponent();
        ManualCheck.ItemsSource = ManualTestCatalog.Cases;
    }

    private AuditWorkspace Workspace =>
        _workspace ?? throw new InvalidOperationException("The audit workspace is unavailable.");

    internal void Bind(AuditWorkspace workspace)
    {
        _workspace = workspace;
        DataContext = workspace;
        _lastStatus = workspace.Status;
        _lastAssessmentStatus = workspace.AssessmentStatus;
        FixtureNotice.Visibility = VisibleWhen(workspace.IsFixtureScenario);
        FixtureManualNotice.Visibility = FixtureNotice.Visibility;
        ManualPassOption.IsEnabled = !workspace.IsFixtureScenario;
        ManualFailOption.IsEnabled = !workspace.IsFixtureScenario;
        workspace.PropertyChanged += WorkspaceChanged;
        RefreshWorkspace();
    }

    internal void Unbind()
    {
        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= WorkspaceChanged;
        }

        _announcement?.Abort();
    }

    internal void FocusWorkspace()
    {
        FrameworkElement target = Workspace.IsAssessmentRunning
            ? Workspace.CanCancel ? CancelAssessmentButton : AssessmentMessage
            : JobRegion;
        target.Focus();
        target.BringIntoView();
    }

    private async void CreateJob(object sender, RoutedEventArgs e) => await Workspace.CreateJobAsync();

    private async void OpenJob(object sender, RoutedEventArgs e) => await Workspace.OpenJobAsync(JobIdInput.Text.Trim());

    private void CloseJob(object sender, RoutedEventArgs e)
    {
        Workspace.CloseJob();
        JobIdInput.Focus();
    }

    private void RequestStageReturn(object sender, RoutedEventArgs e) => ReturnToStagesRequested?.Invoke(this, EventArgs.Empty);

    private async void RecordSanitization(object sender, RoutedEventArgs e)
    {
        SetError(SanitizationInputError, "");
        if (SanitizationResult.SelectedValue is not SanitizationState state)
        {
            SetError(SanitizationInputError, "Choose the external outcome before saving.");
            return;
        }

        bool needsExternalRecord = state != SanitizationState.Unknown;
        if (needsExternalRecord && (_sanitizationDateInvalid || SanitizationDate.SelectedDate is null))
        {
            SetError(SanitizationInputError, "Choose a valid date for the external result.");
            return;
        }

        if (needsExternalRecord && ExternalRecordConfirmed.IsChecked != true)
        {
            SetError(SanitizationInputError, "Confirm ownership and the external record before saving this outcome.");
            return;
        }

        if (UsesMethod(state) && SanitizationMethod.SelectedIndex < 0)
        {
            SetError(SanitizationInputError, "Choose the method shown in the external record.");
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
        ExternalRecordConfirmed.IsChecked = false;
        await Workspace.RecordSanitizationAsync(input);
        SanitizationInputError.Text = Workspace.Status;
    }

    private async void CompleteSavedSanitization(object sender, RoutedEventArgs e)
    {
        await Workspace.CompleteSavedSanitizationAsync();
        SanitizationInputError.Text = Workspace.Status;
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
        if (ManualCheck.SelectedItem is not ManualTestCase selected ||
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
            selected.Id,
            outcome,
            OperatorIdInput.Text.Trim(),
            ManualNotes.Text.Trim(),
            reason);
        if (SelectedManualProgress()?.Latest is { } saved && saved.HumanTestId != previous)
        {
            ManualOutcome.SelectedIndex = -1;
            ManualNotes.Clear();
        }

        ManualInputError.Text = Workspace.Status;
    }

    private void SelectNextManualCheck(object sender, RoutedEventArgs e)
    {
        ManualCheck.SelectedItem = Workspace.ManualProgress?.NextCase;
        ManualCheck.Focus();
    }

    private void ManualCheckChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ManualCheck.SelectedItem as ManualTestCase;
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
        var statusChanged = _lastStatus != Workspace.Status;
        _lastAssessmentStatus = Workspace.AssessmentStatus;
        _lastStatus = Workspace.Status;
        if (assessmentChanged && (!statusChanged || Workspace.Status == Workspace.AssessmentStatus || Workspace.IsAssessmentRunning))
        {
            Announce(AssessmentMessage);
        }
        else if (statusChanged)
        {
            Announce(AuditStatus);
        }
    }

    private void RefreshWorkspace()
    {
        RefreshJob();
        RefreshActionAvailability();
        RefreshManualProgress();
        var saved = Workspace.Job?.SanitizationEvidence is { Count: > 0 } entries ? entries[^1] : null;
        SavedSanitization.Text = saved is null ? "No external result is saved for this job." : DescribeSanitization(saved);
    }

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

        SanitizationResult.SelectedIndex = -1;
        SanitizationMethod.SelectedIndex = -1;
        SanitizationDate.SelectedDate = null;
        SanitizationMediaId.Clear();
        ExternalRecordConfirmed.IsChecked = false;
        _sanitizationDateInvalid = false;
        SanitizationInputError.Text = "";
        ManualInputError.Text = "";
        ManualCheck.SelectedItem = _displayedJob is null
            ? null
            : Workspace.ManualProgress?.NextCase ?? ManualTestCatalog.Cases[0];
    }

    private ManualTestProgressItem? SelectedManualProgress() =>
        ManualCheck.SelectedItem is ManualTestCase selected
            ? _workspace?.ManualProgress?.Items.FirstOrDefault(item => item.Case.Id == selected.Id)
            : null;

    private void RefreshManualProgress()
    {
        var progress = _workspace?.ManualProgress;
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
        CompleteSavedSanitizationButton.Visibility = VisibleWhen(Workspace.CanCompleteSavedSanitization);
        AuditAssessmentProgress.Visibility = VisibleWhen(Workspace.IsAssessmentRunning);
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
        _announcement?.Abort();
        _announcement = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var peer = UIElementAutomationPeer.FromElement(region) ?? UIElementAutomationPeer.CreatePeerForElement(region);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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

    private sealed record UnavailableChoice(ManualTestUnavailableReason Value, string Label);
}
