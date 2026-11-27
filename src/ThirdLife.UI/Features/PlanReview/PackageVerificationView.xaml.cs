using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.PlanReview;

public partial class PackageVerificationView : UserControl
{
    private readonly Dictionary<TextBlock, string> _announcedMessages = [];

    public PackageVerificationView() => InitializeComponent();

    private async void RetryPackage(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanRetryPackage)
        {
            var hadFocus = RetryPackageButton.IsKeyboardFocused;
            await workspace.RetryPackageAsync();
            RestoreFocus(workspace.HasPackagePreflightBlocker ? RetryPreflightMessage :
                RetryPlanStatus.IsVisible ? RetryPlanStatus : PackageRetryMessage, hadFocus);
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

    private void CancelSavedPackageOperation(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCancelPackage)
        {
            var hadFocus = CancelSavedPackageOperationButton.IsKeyboardFocused;
            workspace.CancelPackageOperation();
            RestoreFocus(PackageVerificationActivity, hadFocus);
        }
    }

    private void RestoreFocus(FrameworkElement preferred, bool hadFocus)
    {
        var focused = Keyboard.FocusedElement;
        if (!hadFocus || !IsVisible || !(IsKeyboardFocusWithin || focused is null ||
            focused is ScrollViewer ancestor && ancestor.IsAncestorOf(this)))
        {
            return;
        }

        var target = preferred.IsVisible ? preferred : LatestResult.IsVisible ? LatestResult : VerificationHeading;
        target.Focus();
        target.BringIntoView();
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
