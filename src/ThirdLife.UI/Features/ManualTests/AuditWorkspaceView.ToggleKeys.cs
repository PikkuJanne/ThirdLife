using System.Windows;
using ThirdLife.Core.Handover;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private ToggleKeysChange? _shownToggleKeysChange;
    private string? _lastToggleKeysStatus;

    private async void PreviewToggleKeysOn(object sender, RoutedEventArgs e) => await PreviewToggleKeysAsync(requested: true);

    private async void PreviewToggleKeysOff(object sender, RoutedEventArgs e) => await PreviewToggleKeysAsync(requested: false);

    private async Task PreviewToggleKeysAsync(bool requested)
    {
        FocusAndReveal(ToggleKeysMessage);
        await Workspace.PreviewToggleKeysAsync(requested);
    }

    private async void PreviewToggleKeysRestoration(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(ToggleKeysMessage);
        await Workspace.PreviewToggleKeysRestorationAsync();
    }

    private async void KeepToggleKeysUnconfirmed(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(ToggleKeysMessage);
        await Workspace.KeepToggleKeysAttemptUnconfirmedAsync();
    }

    private async void ApplyToggleKeys(object sender, RoutedEventArgs e)
    {
        if (Workspace.ToggleKeysChangeReview is not { } change || ToggleKeysConfirmed.IsChecked != true) { return; }
        if (string.IsNullOrWhiteSpace(ToggleKeysOperator.Text))
        {
            SetError(ToggleKeysError, "Enter your workshop operator code.");
            FocusAndReveal(ToggleKeysOperator);
            return;
        }
        FocusAndReveal(ToggleKeysMessage);
        await Workspace.ApplyToggleKeysAsync(change, recipientConfirmed: true, ToggleKeysOperator.Text);
    }

    private void ToggleKeysConfirmationChanged(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null) { UpdateToggleKeysApplyButton(); }
    }

    private void UpdateToggleKeysApplyButton() =>
        ApplyToggleKeysButton.IsEnabled = Workspace.CanApplyToggleKeys && ToggleKeysConfirmed.IsChecked == true;

    private void RefreshToggleKeys()
    {
        var preview = Workspace.ToggleKeysChangeReview;
        if (!ReferenceEquals(_shownToggleKeysChange, preview))
        {
            _shownToggleKeysChange = preview;
            ToggleKeysConfirmed.IsChecked = false;
            ToggleKeysOperator.Clear();
            SetError(ToggleKeysError, string.Empty);
        }
        ToggleKeysRegion.Visibility = VisibleWhen(Workspace.ShowsToggleKeys);
        ToggleKeysReviewRegion.Visibility = VisibleWhen(preview is not null);
        KeepToggleKeysUnconfirmedButton.Visibility = VisibleWhen(Workspace.CanKeepToggleKeysAttemptUnconfirmed);
        UpdateToggleKeysApplyButton();
        if (_lastToggleKeysStatus != Workspace.ToggleKeysStatus)
        {
            _lastToggleKeysStatus = Workspace.ToggleKeysStatus;
            Announce(ToggleKeysMessage);
        }
    }
}
