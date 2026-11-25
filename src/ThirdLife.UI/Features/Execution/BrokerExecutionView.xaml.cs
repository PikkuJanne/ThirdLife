using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Execution;

public partial class BrokerExecutionView : UserControl
{
    private string? _announcedStatus;

    public BrokerExecutionView() => InitializeComponent();

    private async void RunBroker(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanRunBrokerExecution)
        {
            var hadFocus = RunBrokerButton.IsKeyboardFocused;
            var execution = workspace.RunBrokerExecutionAsync();
            RestoreFocus(hadFocus);
            await execution;
            RestoreFocus(hadFocus);
        }
    }

    private void CancelBroker(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCancelBrokerExecution)
        {
            var hadFocus = CancelBrokerButton.IsKeyboardFocused;
            workspace.CancelBrokerExecution();
            RestoreFocus(hadFocus);
        }
    }

    private async void CheckRecovery(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanCheckBrokerRecovery)
        {
            var hadFocus = CheckRecoveryButton.IsKeyboardFocused;
            await workspace.CheckBrokerRecoveryAsync();
            RestoreFocus(hadFocus);
        }
    }

    private void RestoreFocus(bool hadFocus)
    {
        if (hadFocus && ExecutionStatus.IsVisible && (IsKeyboardFocusWithin || Keyboard.FocusedElement is null))
        {
            ExecutionStatus.Focus();
            ExecutionStatus.BringIntoView();
        }
    }

    private void AnnounceStatus(object sender, DataTransferEventArgs args)
    {
        if (ExecutionStatus is { IsLoaded: true, IsVisible: true, Text.Length: > 0 } &&
            !string.Equals(_announcedStatus, ExecutionStatus.Text, StringComparison.Ordinal))
        {
            _announcedStatus = ExecutionStatus.Text;
            var peer = UIElementAutomationPeer.FromElement(ExecutionStatus) ?? UIElementAutomationPeer.CreatePeerForElement(ExecutionStatus);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
