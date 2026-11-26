using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ThirdLife.Core.Repairs;
using ThirdLife.Policy.Schema;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Disposition;

public partial class RepairWorkflowView : UserControl
{
    private string? _messageJobId;
    private string? _announcedAssessment;

    private static readonly Choice<PolicyDeviceClass>[] DeviceClasses =
    [
        new("Mobile (laptop)", PolicyDeviceClass.Laptop),
        new("Stationary (desktop)", PolicyDeviceClass.Desktop),
    ];
    private static readonly Choice<RepairAction>[] Actions =
    [
        new("Component repaired", RepairAction.Repaired),
        new("Component replaced", RepairAction.Replaced),
    ];

    public RepairWorkflowView()
    {
        InitializeComponent();
        DeviceClassInput.ItemsSource = DeviceClasses;
        RepairActionInput.ItemsSource = Actions;
    }

    private async void RecordRepair(object sender, RoutedEventArgs args)
    {
        if (DataContext is not AuditWorkspace { CanRecordRepair: true } workspace)
        {
            return;
        }

        if (RepairFindingInput.SelectedItem is not RepairSuggestion finding)
        {
            ShowMessage("Select a saved finding for the completed work.", RepairFindingInput);
            return;
        }

        if (RepairActionInput.SelectedItem is not Choice<RepairAction> action)
        {
            ShowMessage("Select the work that was completed.", RepairActionInput);
            return;
        }

        if (string.IsNullOrWhiteSpace(RepairOperatorInput.Text))
        {
            ShowMessage("Enter the workshop operator code.", RepairOperatorInput);
            return;
        }

        RepairInputMessage.Text = string.Empty;
        RepairResultMessage.Text = string.Empty;
        var hadKeyboardFocus = RecordRepairButton.IsKeyboardFocused;
        await workspace.RecordRepairAsync(finding, action.Value, RepairOperatorInput.Text);
        ShowResult(workspace.Status, hadKeyboardFocus);
    }

    private async void StartRetest(object sender, RoutedEventArgs args)
    {
        if (DataContext is not AuditWorkspace { CanStartRetest: true } workspace)
        {
            return;
        }

        if (SavedRepairInput.SelectedItem is not SavedRepairChoice repair)
        {
            ShowMessage("Select the saved repair to retest.", SavedRepairInput);
            return;
        }

        RepairInputMessage.Text = string.Empty;
        RepairResultMessage.Text = string.Empty;
        var hadKeyboardFocus = StartRetestButton.IsKeyboardFocused;
        var retest = workspace.StartRetestAsync(repair.RepairId);
        if (hadKeyboardFocus && RetestProgressMessage.IsVisible) { RetestProgressMessage.Focus(); }
        await retest;
        ShowResult(workspace.Status, hadKeyboardFocus);
    }

    private void CancelRetest(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace { CanCancel: true } workspace)
        {
            RetestProgressMessage.Focus();
            workspace.CancelAssessment();
        }
    }

    private void ShowMessage(string message, Control input)
    {
        RepairInputMessage.Text = message;
        input.Focus();
        Announce(RepairInputMessage);
    }

    private void ShowResult(string message, bool hadKeyboardFocus)
    {
        RepairResultMessage.Text = message;
        Announce(RepairResultMessage);
        if (hadKeyboardFocus && IsVisible && (IsKeyboardFocusWithin || Keyboard.FocusedElement is null))
        {
            RepairResultMessage.Focus();
            RepairResultMessage.BringIntoView();
        }
    }

    private void ResetForJob(object sender, DataTransferEventArgs args)
    {
        if (ReferenceEquals(args.TargetObject, this) && Tag is string jobId && _messageJobId != jobId)
        {
            _messageJobId = jobId;
            _announcedAssessment = null;
            RepairActionInput.SelectedIndex = -1;
            RepairOperatorInput.Clear();
            RepairInputMessage.Text = string.Empty;
            RepairResultMessage.Text = string.Empty;
        }
    }

    private void RetestProgressUpdated(object sender, DataTransferEventArgs args) => AnnounceRetestProgress();

    private void RetestProgressVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => AnnounceRetestProgress();

    private void AnnounceRetestProgress()
    {
        if (RetestProgressMessage is { IsLoaded: true, IsVisible: true } message &&
            !string.Equals(_announcedAssessment, message.Text, StringComparison.Ordinal))
        {
            _announcedAssessment = message.Text;
            Announce(message);
        }
    }

    private static void Announce(UIElement message)
    {
        if (message.IsVisible)
        {
            var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private sealed record Choice<T>(string Caption, T Value);
}
