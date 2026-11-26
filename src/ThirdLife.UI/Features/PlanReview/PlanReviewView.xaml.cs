using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.PlanReview;

public partial class PlanReviewView : UserControl
{
    private IInputElement? _detailsHeaderFocus;
    private PlanReviewSnapshot? _detailsPlan;
    private string? _announcedStatus;
    private string? _announcedPackageStatus;

    public PlanReviewView() => InitializeComponent();

    private void ResizeMaterialImpact(object sender, SizeChangedEventArgs args) =>
        MaterialImpactItems.Tag = args.NewSize.Width >= 560 ? 2 : 1;

    private async void BuildPlanReview(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanBuildPlanReview)
        {
            var hadFocus = BuildPlanReviewButton.IsKeyboardFocused;
            await workspace.BuildPlanReviewAsync();
            RestoreFocus(workspace.HasPlanReview ? PlanResultTitle : PlanStatusMessage, hadFocus);
        }
    }

    private async void ApprovePlanReview(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanApprovePlanReview)
        {
            var hadFocus = ApprovePlanReviewButton.IsKeyboardFocused;
            await workspace.ApprovePlanReviewAsync();
            RestoreFocus(InstallPackageButton.IsVisible && InstallPackageButton.IsEnabled
                ? InstallPackageButton : PlanStatusMessage, hadFocus);
        }
    }

    private async void DeclinePlanReview(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanDeclinePlanReview)
        {
            var hadFocus = DeclinePlanReviewButton.IsKeyboardFocused;
            await workspace.DeclinePlanReviewAsync();
            RestoreFocus(PlanStatusMessage, hadFocus);
        }
    }

    private async void InstallPackage(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanInstallPackage)
        {
            var hadFocus = InstallPackageButton.IsKeyboardFocused;
            await workspace.InstallPackageAsync();
            RestoreFocus(PlanStatusMessage, hadFocus);
        }
    }

    private void CancelPackageOperation(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCancelPackage)
        {
            var hadFocus = CancelPackageButton.IsKeyboardFocused;
            workspace.CancelPackageOperation();
            RestoreFocus(PackageSourceMessage, hadFocus);
        }
    }

    private void RestoreFocus(FrameworkElement target, bool hadFocus)
    {
        var focused = Keyboard.FocusedElement;
        if (hadFocus && target.IsVisible && (IsKeyboardFocusWithin || focused is null ||
            focused is ScrollViewer ancestor && ancestor.IsAncestorOf(this)))
        {
            target.Focus();
            target.BringIntoView();
        }
    }

    private void ExpandPlanDetails(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, PlanDetailsExpander) ||
            !PlanDetailsExpander.IsKeyboardFocusWithin || PlanDetailsText.IsKeyboardFocusWithin)
        {
            return;
        }

        var header = Keyboard.FocusedElement;
        _detailsHeaderFocus = header;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (PlanDetailsExpander.IsExpanded && PlanDetailsText.IsVisible &&
                ReferenceEquals(header, Keyboard.FocusedElement))
            {
                PlanDetailsText.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void ClosePlanDetailsOnEscape(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !PlanDetailsExpander.IsExpanded)
        {
            return;
        }

        PlanDetailsExpander.IsExpanded = false;
        if (_detailsHeaderFocus is UIElement { IsVisible: true, IsEnabled: true } header)
        {
            header.Focus();
        }
        else
        {
            PlanDetailsExpander.Focus();
        }

        args.Handled = true;
    }

    private void UpdateDetailsVisibility(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Expander { IsVisible: true } expander && DataContext is AuditWorkspace workspace &&
            !ReferenceEquals(_detailsPlan, workspace.PlanReview))
        {
            _detailsHeaderFocus = null;
            _detailsPlan = workspace.PlanReview;
            expander.IsExpanded = !workspace.HasMaterialPlanImpact;
        }
    }

    private void AnnounceStatus(object sender, DataTransferEventArgs args)
    {
        if (args.TargetObject is not TextBlock { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message)
        {
            return;
        }

        ref var announced = ref (ReferenceEquals(message, PackageSourceMessage) ? ref _announcedPackageStatus : ref _announcedStatus);
        if (string.Equals(announced, message.Text, StringComparison.Ordinal))
        {
            return;
        }

        announced = message.Text;
        var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
