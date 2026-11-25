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
        if (PlanStatusMessage is { IsLoaded: true, IsVisible: true, Text.Length: > 0 } message &&
            !string.Equals(_announcedStatus, message.Text, StringComparison.Ordinal))
        {
            _announcedStatus = message.Text;
            var peer = UIElementAutomationPeer.FromElement(message) ?? UIElementAutomationPeer.CreatePeerForElement(message);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
