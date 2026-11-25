using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ThirdLife.Persistence;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _model;
    private readonly AuditWorkspace? _workspace;
    private readonly WheelScrolling _wheelScrolling = new();
    private DispatcherOperation? _announcement;
    private bool _statusChanged;
    private bool _activityChanged;
    private bool _closingWorkspace;
    private bool _workspaceClosed;

    public MainWindow() : this(new ShellViewModel()) { }

    public MainWindow(ShellViewModel model) : this(model, null) { }

    internal MainWindow(ShellViewModel model, AuditWorkspace? workspace)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _workspace = workspace;
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += ModelChanged;
        if (workspace is not null)
        {
            AuditView.Bind(workspace);
            AuditView.ReturnToStagesRequested += ReturnFromAudit;
            AuditView.Visibility = Visibility.Visible;
            OpenAuditWorkspaceButton.Visibility = Visibility.Visible;
            workspace.PropertyChanged += WorkspaceChanged;
            UpdateAuditPresentation();
        }

        UpdateNavigation();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _workspace is null || _workspaceClosed)
        {
            return;
        }

        e.Cancel = true;
        if (!_closingWorkspace)
        {
            _closingWorkspace = true;
            _ = CloseWorkspaceAsync(_workspace);
        }
    }

    private async Task CloseWorkspaceAsync(AuditWorkspace workspace)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            await workspace.CloseAsync();
        }
        catch (Exception ex) when (ex is JobStoreException or InvalidOperationException)
        {
            MessageBox.Show(this,
                "ThirdLife could not finish releasing the local job store. Reopen the job and check its saved records before continuing.",
                "Audit job recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _workspaceClosed = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= ModelChanged;
        if (_workspace is not null)
        {
            _workspace.PropertyChanged -= WorkspaceChanged;
            AuditView.ReturnToStagesRequested -= ReturnFromAudit;
            AuditView.Unbind();
        }

        _announcement?.Abort();
        base.OnClosed(e);
    }

    private void ShowIntake(object sender, RoutedEventArgs e) => _model.Navigate(ShellStage.Intake);
    private void ShowInspect(object sender, RoutedEventArgs e) => _model.Navigate(ShellStage.Inspect);

    private void OpenAuditWorkspace(object sender, RoutedEventArgs e) => AuditView.FocusWorkspace();

    private void OpenPlanReview(object sender, RoutedEventArgs e) => AuditView.FocusPlanReview();

    private void ScrollFromWheel(object sender, MouseWheelEventArgs e) => _wheelScrolling.Scroll(ShellScroll, e);

    private void ResizeWorkflow(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 720;
        NavigationColumn.Width = wide ? new GridLength(280) : new GridLength(1, GridUnitType.Star);
        ReviewColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(ReviewContent, wide ? 1 : 0);
        Grid.SetRow(ReviewContent, wide ? 0 : 1);
        ReviewContent.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0);
    }

    private void SkipToReview(object sender, RoutedEventArgs e)
    {
        StageReview.Focus();
        StageReview.BringIntoView();
    }

    private void ReturnToStages(object sender, RoutedEventArgs e)
        => FocusStage();

    private void ReturnFromAudit(object? sender, EventArgs e)
        => FocusStage();

    private void FocusStage()
    {
        var target = _model.SelectedStage == ShellStage.Intake ? IntakeButton : InspectButton;
        target.Focus();
        target.BringIntoView();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.StatusText) or nameof(ShellViewModel.ActivityText)))
        { return; }
        _statusChanged |= e.PropertyName == nameof(ShellViewModel.StatusText);
        _activityChanged |= _workspace is null && e.PropertyName == nameof(ShellViewModel.ActivityText);
        // Coalesce updates and let bindings settle before notifying assistive technology.
        _announcement?.Abort();
        _announcement = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            UpdateNavigation();
            if (_statusChanged) { Announce(StatusAnnouncement); }
            if (_activityChanged) { Announce(ActivityAnnouncement); }
            _statusChanged = false;
            _activityChanged = false;
        }));
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateAuditPresentation();
    }

    private void UpdateAuditPresentation()
    {
        if (_workspace is { } workspace)
        {
            _model.SetAuditContext(workspace.Job is not null, workspace.GateStatus, workspace.AssessmentStatus);
            OpenAuditWorkspaceButton.Content = workspace.IsAssessmentRunning
                ? "Go to assessment controls"
                : "Go to audit controls";
            PrepareButton.IsEnabled = workspace.CanReviewPlan;
            PrepareStageLabel.Text = workspace.CanReviewPlan ? "4. Prepare — review plan" : "4. Prepare — review unavailable";
            AutomationProperties.SetName(PrepareButton, workspace.CanReviewPlan
                ? "4. Prepare, review plan"
                : "4. Prepare, review unavailable");
            AutomationProperties.SetHelpText(WorkflowStages,
                "Use Page Down and Page Up to read stage requirements. A current assessment enables preparation plan review.");
            PrepareCriteria.Text = "A current assessment and approved intake gate enable plan review. Review the choices, exact package details and declared impact.";
            VerifyCriteria.Text = "Review package checks in the saved setup journal and workshop report. Full-device verification requires additional checks.";
        }
    }

    private void UpdateNavigation()
    {
        AutomationProperties.SetItemStatus(IntakeButton, _model.SelectedStage == ShellStage.Intake ? "Current review stage" : "Available for review");
        AutomationProperties.SetItemStatus(InspectButton, _model.SelectedStage == ShellStage.Intake ? "Available for review" : "Current review stage");
    }

    private static void Announce(TextBlock text)
    {
        var peer = UIElementAutomationPeer.FromElement(text) ?? UIElementAutomationPeer.CreatePeerForElement(text);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
