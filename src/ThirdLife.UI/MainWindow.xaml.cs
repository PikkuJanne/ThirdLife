using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ThirdLife.Persistence;
using ThirdLife.UI.Shell;
using ThirdLife.UI.Themes;

namespace ThirdLife.UI;

public partial class MainWindow : Window
{
    private readonly AuditWorkspace _workspace;
    private readonly TraceAppearance _appearance;
    private readonly WheelScrolling _wheelScrolling = new();
    private DispatcherOperation? _announcement;
    private bool _closingWorkspace;
    private bool _workspaceClosed;
    private bool? _wide;

    internal MainWindow(AuditWorkspace workspace, bool persistAppearance = false)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height));
        _appearance = new TraceAppearance(this, persistPreference: persistAppearance);
        AppearanceChoice.SelectedIndex = _appearance.Preference == TraceTheme.Dark ? 0 : 1;
        AuditView.Bind(workspace);
        AuditView.StageRequested += StageRequested;
        _workspace.PropertyChanged += WorkspaceChanged;
        Loaded += FocusOnEntry;
        UpdateNavigation();
    }

    // Isolated component callers use the same production window and navigation.
    internal void ShowStage(ShellStage stage)
    {
        var item = stage switch
        {
            ShellStage.Intake => IntakeStage,
            ShellStage.Inspect => InspectStage,
            ShellStage.Decide => DecideStage,
            ShellStage.Prepare => PrepareStage,
            ShellStage.Verify => VerifyStage,
            ShellStage.Handover => HandoverStage,
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
        StageList.SelectedItem = item;
        AuditView.ShowStage(stage);
        // Navigation callers move keyboard focus to controls in the new stage.
        AuditView.UpdateLayout();
    }

    private void FocusOnEntry(object sender, RoutedEventArgs e)
    {
        Loaded -= FocusOnEntry;
        if (_workspace.Job is null)
        {
            AuditView.FocusJobs();
        }
        else
        {
            ShowStage(ShellStage.Intake);
        }
    }

    private void SelectStage(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, StageList) ||
            StageList.SelectedItem is not ListBoxItem { Tag: string tag } ||
            !Enum.TryParse<ShellStage>(tag, out var stage))
        {
            return;
        }

        AuditView.ShowStage(stage);
        ShellScroll.ScrollToTop();
        AnnounceNavigation($"{stage} opened.");
    }

    private void StageRequested(object? sender, ShellStage? stage)
    {
        if (stage is { } destination)
        {
            ShowStage(destination);
            return;
        }

        ShowJobs();
    }

    private void GoToJobs(object sender, RoutedEventArgs e)
    {
        ShowJobs();
        AuditView.FocusJobs();
    }

    private void ShowJobs()
    {
        StageList.SelectedItem = null;
        AuditView.ShowStage(null);
        ShellScroll.ScrollToTop();
        AnnounceNavigation("Jobs opened.");
    }

    private void ChangeAppearance(object sender, SelectionChangedEventArgs e)
    {
        if (_appearance is not null && AppearanceChoice.SelectedIndex >= 0)
        {
            _appearance.SetPreference(AppearanceChoice.SelectedIndex == 0 ? TraceTheme.Dark : TraceTheme.Light);
        }
    }

    private void ScrollFromWheel(object sender, MouseWheelEventArgs e) => _wheelScrolling.Scroll(ShellScroll, e);

    private void ResizeProductBar(object sender, SizeChangedEventArgs e) =>
        DockPanel.SetDock(ProductIdentity, e.NewSize.Width >= 520 ? Dock.Left : Dock.Top);

    private void ResizeWorkflow(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 750;
        if (wide == _wide) { return; }
        _wide = wide;
        NavigationColumn.Width = wide ? new GridLength(176) : new GridLength(1, GridUnitType.Star);
        WorkColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRowSpan(WorkflowRail, wide ? 2 : 1);
        Grid.SetRowSpan(WorkspaceColumn, wide ? 2 : 1);
        Grid.SetColumn(WorkspaceColumn, wide ? 1 : 0);
        Grid.SetRow(WorkspaceColumn, wide ? 0 : 1);
        WorkflowRail.BorderThickness = wide ? new Thickness(0, 0, 1, 0) : new Thickness(0, 0, 0, 1);
        StageList.ItemsPanel = (ItemsPanelTemplate)FindResource(wide ? "WideWorkflow" : "CompactWorkflow");
        LocalRecords.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigation();
    }

    private void WorkspaceChanged(object? sender, PropertyChangedEventArgs e) => UpdateNavigation();

    private void UpdateNavigation()
    {
        bool hasJob = _workspace.Job is not null;
        bool wide = _wide != false;
        SetNote(IntakeStage, IntakeNote, hasJob && _workspace.Job!.SanitizationEvidence.Count != 0 ? "Record saved" : "External record needed");
        SetNote(InspectStage, InspectNote, _workspace.IsAssessmentRunning ? "Assessment running"
            : _workspace.Assessment is not null ? "Assessment saved" : "No assessment");
        SetNote(DecideStage, DecideNote, _workspace.HasPolicyDecision ? "Decision saved" : "Candidate policy");
        SetNote(PrepareStage, PrepareNote, _workspace.PlanReviewDecision is not null ? "Decision recorded"
            : _workspace.HasPlanReview ? "Plan built" : "Plan review");
        SetNote(VerifyStage, VerifyNote, _workspace.HasPackageHistory ? "Package journal saved" : "Checks and recovery");
        SetNote(HandoverStage, HandoverNote, _workspace.ReportArtifact is not null ? "Report saved" : "Clearance pending");

        void SetNote(ListBoxItem item, TextBlock note, string text)
        {
            note.Text = hasJob ? text : "";
            note.Visibility = wide && hasJob ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetItemStatus(item, note.Text);
        }
    }

    private void AnnounceNavigation(string text)
    {
        NavigationStatus.Text = text;
        _announcement?.Abort();
        _announcement = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var peer = UIElementAutomationPeer.FromElement(NavigationStatus)
                ?? UIElementAutomationPeer.CreatePeerForElement(NavigationStatus);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _workspaceClosed) { return; }
        e.Cancel = true;
        if (_closingWorkspace) { return; }
        _closingWorkspace = true;
        _ = CloseWorkspaceAsync();
    }

    private async Task CloseWorkspaceAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            await _workspace.CloseAsync();
        }
        catch (Exception ex) when (ex is JobStoreException or InvalidOperationException)
        {
            TraceNotice.ShowMessage("Job recovery",
                "ThirdLife could not finish releasing the local job store. Reopen the job and check its saved records before continuing.",
                this);
        }
        finally
        {
            _workspaceClosed = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _workspace.PropertyChanged -= WorkspaceChanged;
        AuditView.StageRequested -= StageRequested;
        AuditView.Unbind();
        _announcement?.Abort();
        base.OnClosed(e);
    }
}
