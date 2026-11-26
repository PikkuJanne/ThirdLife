using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThirdLife.Actions.Recovery;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Recovery;

public partial class RebootRecoveryView : UserControl
{
    private string? _announcedStatus;

    public RebootRecoveryView() => InitializeComponent();

    private async void Resume(object sender, RoutedEventArgs args) =>
        await DecideAsync(ResumeButton, RebootDisposition.Resume);

    private async void Abandon(object sender, RoutedEventArgs args) =>
        await DecideAsync(AbandonButton, RebootDisposition.Abandon);

    private async Task DecideAsync(Button button, RebootDisposition disposition)
    {
        if (!button.IsEnabled || DataContext is not AuditWorkspace workspace) { return; }

        var hadFocus = button.IsKeyboardFocused;
        await workspace.DecideRebootRecoveryAsync(disposition);
        var focused = Keyboard.FocusedElement;
        if (hadFocus && DecisionStatus.IsVisible && (IsKeyboardFocusWithin || focused is null ||
            focused is ScrollViewer ancestor && ancestor.IsAncestorOf(this)))
        {
            DecisionStatus.Focus();
            DecisionStatus.BringIntoView();
        }
    }

    private void AnnounceStatus(object sender, DataTransferEventArgs args)
    {
        if (DecisionStatus is { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message &&
            !string.Equals(_announcedStatus, message.Text, StringComparison.Ordinal))
        {
            _announcedStatus = message.Text;
            var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
