using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Core.Repairs;
using ThirdLife.Policy.Schema;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Disposition;

public partial class RepairWorkflowView : UserControl
{
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
        if (DataContext is not AuditWorkspace workspace)
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
        await workspace.RecordRepairAsync(finding, action.Value, RepairOperatorInput.Text);
    }

    private async void StartRetest(object sender, RoutedEventArgs args)
    {
        if (DataContext is not AuditWorkspace workspace)
        {
            return;
        }

        if (SavedRepairInput.SelectedItem is not SavedRepairChoice repair)
        {
            ShowMessage("Select the saved repair to retest.", SavedRepairInput);
            return;
        }

        RepairInputMessage.Text = string.Empty;
        await workspace.StartRetestAsync(repair.RepairId);
    }

    private void ShowMessage(string message, Control input)
    {
        RepairInputMessage.Text = message;
        input.Focus();
        var peer = UIElementAutomationPeer.FromElement(RepairInputMessage)
            ?? UIElementAutomationPeer.CreatePeerForElement(RepairInputMessage);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed record Choice<T>(string Caption, T Value);
}
