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

    public PolicyDispositionView() => InitializeComponent();

    private async void EvaluatePolicy(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanEvaluatePolicy)
        {
            var hadKeyboardFocus = EvaluatePolicyButton.IsKeyboardFocused;
            await workspace.EvaluatePolicyAsync();
            FocusNewResult(workspace, hadKeyboardFocus);
        }
    }

    private async void ReevaluatePolicy(object sender, RoutedEventArgs args)
    {
        if (DataContext is AuditWorkspace workspace && workspace.CanReevaluatePolicy)
        {
            var hadKeyboardFocus = ReevaluatePolicyButton.IsKeyboardFocused;
            await workspace.ReevaluatePolicyAsync();
            FocusNewResult(workspace, hadKeyboardFocus);
        }
    }

    private void FocusNewResult(AuditWorkspace workspace, bool hadKeyboardFocus)
    {
        if (hadKeyboardFocus && workspace.HasPolicyDecision &&
            (IsKeyboardFocusWithin || Keyboard.FocusedElement is null))
        {
            PolicyResultTitle.Focus();
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

    private void AnnounceStatus(object sender, DataTransferEventArgs args)
    {
        if (PolicyStatusMessage is { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message &&
            !string.Equals(_announcedStatus, message.Text, StringComparison.Ordinal))
        {
            _announcedStatus = message.Text;
            var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
