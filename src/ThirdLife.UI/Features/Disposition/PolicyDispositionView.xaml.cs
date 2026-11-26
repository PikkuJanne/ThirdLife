using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Disposition;

public partial class PolicyDispositionView : UserControl
{
    private IInputElement? _detailsHeaderFocus;
    private string? _announcedStatus;
    private string? _operationJobId;

    public PolicyDispositionView() => InitializeComponent();

    private async void EvaluatePolicy(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanEvaluatePolicy)
        {
            var hadKeyboardFocus = EvaluatePolicyButton.IsKeyboardFocused;
            BeginEvaluation(workspace);
            await workspace.EvaluatePolicyAsync();
            CompleteEvaluation(workspace);
            FocusAfterEvaluation(workspace, hadKeyboardFocus);
        }
    }

    private async void ReevaluatePolicy(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanReevaluatePolicy)
        {
            var hadKeyboardFocus = ReevaluatePolicyButton.IsKeyboardFocused;
            BeginEvaluation(workspace);
            await workspace.ReevaluatePolicyAsync();
            CompleteEvaluation(workspace);
            FocusAfterEvaluation(workspace, hadKeyboardFocus);
        }
    }

    private void FocusAfterEvaluation(AuditWorkspace workspace, bool hadKeyboardFocus)
    {
        if (!hadKeyboardFocus || !IsVisible || (!IsKeyboardFocusWithin && Keyboard.FocusedElement is not null))
        {
            return;
        }

        var destination = PolicyActionMessage.Text.Length > 0 ? PolicyActionMessage
            : workspace.HasPolicyDecision ? PolicyResultTitle : null;
        if (destination is not null)
        {
            destination.Focus();
            destination.BringIntoView();
        }
    }

    private void ExpandExpertDetails(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, PolicyExpertExpander) ||
            !PolicyExpertExpander.IsKeyboardFocusWithin || PolicyExpertText.IsKeyboardFocusWithin)
        {
            return;
        }

        _detailsHeaderFocus = Keyboard.FocusedElement;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (PolicyExpertExpander.IsExpanded && ReferenceEquals(_detailsHeaderFocus, Keyboard.FocusedElement))
            {
                PolicyExpertText.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void CloseExpertDetailsOnEscape(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !PolicyExpertExpander.IsExpanded)
        {
            return;
        }

        PolicyExpertExpander.IsExpanded = false;
        if (_detailsHeaderFocus is UIElement { IsVisible: true, IsEnabled: true } header)
        {
            header.Focus();
        }
        else
        {
            PolicyExpertExpander.Focus();
        }

        args.Handled = true;
    }

    private void ResetHiddenDetails(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Expander { IsVisible: false } expander)
        {
            expander.IsExpanded = false;
            _detailsHeaderFocus = null;
        }
    }

    private void BeginEvaluation(AuditWorkspace workspace)
    {
        _operationJobId = workspace.JobId;
        SetActionMessage("Evaluating candidate policy from saved evidence.");
    }

    private void CompleteEvaluation(AuditWorkspace workspace) =>
        SetActionMessage(workspace.Status == workspace.PolicyStatus ? string.Empty : workspace.Status);

    private void SetActionMessage(string message)
    {
        if (PolicyActionMessage.Text == message) { return; }
        PolicyActionMessage.Text = message;
        if (message.Length > 0)
        {
            Announce(PolicyActionMessage);
        }
    }

    private void AnnouncePolicyStatus(object sender, DataTransferEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && _operationJobId != workspace.JobId)
        {
            _operationJobId = workspace.JobId;
            SetActionMessage(string.Empty);
        }

        if (PolicyStatusMessage is { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message &&
            !string.Equals(_announcedStatus, message.Text, StringComparison.Ordinal))
        {
            _announcedStatus = message.Text;
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
}
