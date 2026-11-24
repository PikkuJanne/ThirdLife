using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _model;
    private DispatcherOperation? _announcement;
    private bool _statusChanged;
    private bool _activityChanged;

    public MainWindow() : this(new ShellViewModel()) { }

    public MainWindow(ShellViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += ModelChanged;
        UpdateNavigation();
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= ModelChanged;
        _announcement?.Abort();
        base.OnClosed(e);
    }

    private void ShowIntake(object sender, RoutedEventArgs e) => _model.Navigate(ShellStage.Intake);
    private void ShowInspect(object sender, RoutedEventArgs e) => _model.Navigate(ShellStage.Inspect);

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
        _activityChanged |= e.PropertyName == nameof(ShellViewModel.ActivityText);
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
