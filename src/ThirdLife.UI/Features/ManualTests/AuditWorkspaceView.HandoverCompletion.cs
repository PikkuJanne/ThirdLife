using System.Windows;
using ThirdLife.Core.Handover;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private string? _lastHandoverCompletionStatus;

    private async void ReviewHandover(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(HandoverCompletionMessage);
        await Workspace.ReviewHandoverAsync();
    }

    private async void RecordSupportOmission(object sender, RoutedEventArgs e)
    {
        if (!CheckCompletionOperator()) { return; }
        if (SupportOmissionReasonChoice.SelectedValue is not SupportBundleOmissionReason reason)
        {
            SetError(HandoverCompletionInputError, "Choose why the support archive is omitted.");
            FocusAndReveal(SupportOmissionReasonChoice);
            return;
        }

        FocusAndReveal(HandoverCompletionMessage);
        await Workspace.RecordSupportOmissionAsync(HandoverCompletionOperator.Text, reason);
    }

    private async void CompleteHandover(object sender, RoutedEventArgs e)
    {
        if (!CheckCompletionOperator()) { return; }
        FocusAndReveal(HandoverCompletionMessage);
        await Workspace.SignOffHandoverAsync(HandoverCompletionOperator.Text, HandoverCompletionConfirmed.IsChecked == true);
    }

    private bool CheckCompletionOperator()
    {
        if (string.IsNullOrWhiteSpace(HandoverCompletionOperator.Text))
        {
            SetError(HandoverCompletionInputError, "Enter your workshop operator code.");
            FocusAndReveal(HandoverCompletionOperator);
            return false;
        }
        SetError(HandoverCompletionInputError, string.Empty);
        return true;
    }

    private void HandoverConfirmationChanged(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null)
        {
            UpdateHandoverCompletionButton();
        }
    }

    private void UpdateHandoverCompletionButton() =>
        CompleteHandoverButton.IsEnabled = Workspace.CanSignOffHandover && HandoverCompletionConfirmed.IsChecked == true;

    private void RefreshHandoverCompletion()
    {
        HandoverCompletionActions.Visibility = VisibleWhen(Workspace.CanRecordSupportOmission);
        if (!Workspace.CanSignOffHandover)
        {
            HandoverCompletionConfirmed.IsChecked = false;
        }
        if (Workspace.Job is null)
        {
            HandoverCompletionOperator.Clear();
            SupportOmissionReasonChoice.SelectedIndex = -1;
            SetError(HandoverCompletionInputError, string.Empty);
        }
        UpdateHandoverCompletionButton();
        if (_lastHandoverCompletionStatus != Workspace.HandoverCompletionStatus)
        {
            _lastHandoverCompletionStatus = Workspace.HandoverCompletionStatus;
            Announce(HandoverCompletionMessage);
        }
    }
}
