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
    private string? _announcedStatus;
    private string? _announcedPackageStatus;
    private string? _announcedPackageRecovery;
    private string? _announcedPackageRetry;

    public PlanReviewView() => InitializeComponent();

    internal void FocusReview()
    {
        PlanReviewRegion.Focus();
        PlanReviewRegion.BringIntoView();
    }

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
            RestoreFocus(PlanStatusMessage, hadFocus);
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
            RestoreFocus(workspace.HasPackageHistory ? SavedPackageHistory : PlanStatusMessage, hadFocus);
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

    private async void RetryPackage(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanRetryPackage)
        {
            var hadFocus = RetryPackageButton.IsKeyboardFocused;
            await workspace.RetryPackageAsync();
            RestoreFocus(PlanStatusMessage, hadFocus);
        }
    }

    private async void CheckPackageRecovery(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCheckPackageRecovery)
        {
            var hadFocus = CheckPackageRecoveryButton.IsKeyboardFocused;
            await workspace.CheckPackageRecoveryAsync();
            RestoreFocus(PackageRecoveryMessage, hadFocus);
        }
    }

    private void CancelPackageRecovery(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCancelPackageRecovery)
        {
            var hadFocus = CancelPackageRecoveryButton.IsKeyboardFocused;
            workspace.CancelPackageOperation();
            RestoreFocus(PackageRecoveryMessage, hadFocus);
        }
    }

    private void RestoreFocus(FrameworkElement target, bool hadFocus)
    {
        if (hadFocus && target.IsVisible && (IsKeyboardFocusWithin || Keyboard.FocusedElement is null))
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
        if (sender is Expander expander)
        {
            _detailsHeaderFocus = null;
            expander.IsExpanded = expander.IsVisible;
        }
    }

    private void AnnounceStatus(object sender, DataTransferEventArgs args)
    {
        if (args.TargetObject is not TextBlock { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message)
        {
            return;
        }

        ref var announced = ref (ReferenceEquals(message, PackageSourceMessage) ? ref _announcedPackageStatus :
            ref (ReferenceEquals(message, PackageRecoveryMessage) ? ref _announcedPackageRecovery :
                ref (ReferenceEquals(message, PackageRetryMessage) ? ref _announcedPackageRetry : ref _announcedStatus)));
        if (string.Equals(announced, message.Text, StringComparison.Ordinal))
        {
            return;
        }

        announced = message.Text;
        var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
