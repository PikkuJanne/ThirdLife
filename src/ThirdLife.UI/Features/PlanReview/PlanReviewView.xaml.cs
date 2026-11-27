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
    private readonly Dictionary<TextBlock, string> _announcedMessages = [];

    public PlanReviewView() => InitializeComponent();

    private void ResizeMaterialImpact(object sender, SizeChangedEventArgs args) =>
        MaterialImpactItems.Tag = args.NewSize.Width >= 560 ? 2 : 1;

    private async void BuildPlanReview(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanBuildPlanReview)
        {
            var hadFocus = BuildPlanReviewButton.IsKeyboardFocused;
            await workspace.BuildPlanReviewAsync();
            RestoreFocus(workspace.HasPackagePreflightBlocker ? PackagePreflightMessage :
                workspace.HasPlanReview ? PlanResultTitle : PlanStatusMessage, hadFocus);
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

    private async void SkipApprovedAction(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && sender is Button { DataContext: ActionSkipChoice choice } button)
        {
            var hadFocus = button.IsKeyboardFocused;
            await workspace.SkipApprovedActionAsync(choice.ActionId);
            RestoreFocus(PlanStatusMessage, hadFocus);
        }
    }

    private async void InstallPackage(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanInstallPackage)
        {
            var hadFocus = InstallPackageButton.IsKeyboardFocused;
            await workspace.InstallPackageAsync();
            RestoreFocus(workspace.HasPackagePreflightBlocker ? PackagePreflightMessage : PlanStatusMessage, hadFocus);
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

    private async void ScanWindowsUpdates(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanScanWindowsUpdates)
        {
            var hadFocus = ScanWindowsUpdatesButton.IsKeyboardFocused;
            await workspace.ScanWindowsUpdatesAsync();
            RestoreFocus(WindowsUpdateMessage, hadFocus);
        }
    }

    private async void CheckUpdateOffer(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCheckUpdateOffer)
        {
            var hadFocus = CheckUpdateOfferButton.IsKeyboardFocused;
            await workspace.CheckUpdateOfferAsync();
            RestoreFocus(UpdateOfferPreview, hadFocus);
        }
    }

    private async void EvaluateUpdatePolicy(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanEvaluateUpdatePolicy)
        {
            var hadFocus = EvaluateUpdatePolicyButton.IsKeyboardFocused;
            await workspace.EvaluateUpdatePolicyAsync();
            RestoreFocus(UpdateOfferPreview, hadFocus);
        }
    }

    private async void ApproveUpdate(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanApproveWindowsUpdate)
        {
            var hadFocus = ApproveUpdateButton.IsKeyboardFocused;
            await workspace.ApproveWindowsUpdateAsync();
            RestoreFocus(workspace.HasWindowsUpdatePreview ? UpdateOfferPreview : WindowsUpdateMessage, hadFocus);
        }
    }

    private async void RunApprovedUpdate(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanRunApprovedUpdate)
        {
            var hadFocus = RunUpdateButton.IsKeyboardFocused;
            await workspace.RunApprovedUpdateAsync();
            RestoreFocus(WindowsUpdateMessage, hadFocus);
        }
    }

    private void CancelWindowsUpdate(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCancelWindowsUpdate)
        {
            var hadFocus = CancelWindowsUpdateButton.IsKeyboardFocused;
            workspace.CancelWindowsUpdate();
            RestoreFocus(WindowsUpdateMessage, hadFocus);
        }
    }

    private async void ContinueMaintenance(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanContinueMaintenance)
        {
            var hadFocus = ContinueMaintenanceButton.IsKeyboardFocused;
            await workspace.ContinueMaintenanceAsync();
            RestoreFocus(MaintenancePauseMessage, hadFocus);
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

    private void AnnounceStatus(object sender, DataTransferEventArgs args) => AnnounceMessage(args.TargetObject);

    private void AnnouncePreflightVisibility(object sender, DependencyPropertyChangedEventArgs args) => AnnounceMessage(sender);

    private void AnnounceMessage(object target)
    {
        if (target is not TextBlock { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message ||
            _announcedMessages.TryGetValue(message, out var last) && string.Equals(last, message.Text, StringComparison.Ordinal))
        {
            return;
        }

        _announcedMessages[message] = message.Text;
        var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
