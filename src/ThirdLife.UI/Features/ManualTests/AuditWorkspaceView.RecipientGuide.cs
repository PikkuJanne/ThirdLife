using System.Windows;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private string? _lastRecipientGuideStatus;

    private async void PreviewRecipientGuide(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(RecipientGuideMessage);
        await Workspace.PreviewRecipientGuideAsync();
        if (RecipientGuideMessage.IsKeyboardFocused && Workspace.RecipientGuidePreview.Length > 0)
        {
            FocusAndReveal(RecipientGuidePreviewBox);
        }
    }

    private async void SaveRecipientGuide(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(RecipientGuideMessage);
        await Workspace.SavePrintableRecipientGuideAsync();
    }

    private void RefreshRecipientGuide()
    {
        RecipientGuidePreviewRegion.Visibility = VisibleWhen(Workspace.RecipientGuidePreview.Length > 0);
        if (_lastRecipientGuideStatus != Workspace.RecipientGuideStatus)
        {
            _lastRecipientGuideStatus = Workspace.RecipientGuideStatus;
            Announce(RecipientGuideMessage);
        }
    }
}
