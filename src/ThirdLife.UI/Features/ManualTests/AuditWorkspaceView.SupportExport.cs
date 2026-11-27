using System.Windows;
using Microsoft.Win32;
using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private SupportArchivePreview? _displayedSupportPreview;
    private string? _lastSupportStatus;
    private string? _lastSupportHistory;

    internal Func<string?>? SelectSupportFolderForTesting { get; set; }

    private async void PreviewSupport(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(SupportExportMessage);
        await Workspace.CreateSupportPreviewAsync();
        if (SupportExportMessage.IsKeyboardFocused && Workspace.SupportPreview is not null)
        {
            FocusAndReveal(SupportFiles);
        }
    }

    private async void ExportSupport(object sender, RoutedEventArgs e)
    {
        if (SupportExportApproved.IsChecked != true || !Workspace.CanExportSupport ||
            Workspace.SupportPreview is not { } preview)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SupportOperatorCode.Text))
        {
            SetError(SupportInputError, "Enter your workshop operator code.");
            FocusAndReveal(SupportOperatorCode);
            return;
        }

        var destination = ChooseSupportFolder();
        if (destination is null) { return; }
        FocusAndReveal(SupportExportMessage);
        await Workspace.ExportSupportAsync(preview, SupportExportApproved.IsChecked == true,
            SupportOperatorCode.Text, destination);
    }

    private string? ChooseSupportFolder()
    {
        if (SelectSupportFolderForTesting is { } select) { return select(); }
        var dialog = new OpenFolderDialog { Title = "Choose a local location for the protected support folder" };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
    }

    private void CancelSupport(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(SupportExportMessage);
        Workspace.CancelSupport();
    }

    private async void RetrySupportAudit(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(SupportExportMessage);
        await Workspace.RetrySupportAuditAsync();
    }

    private void SupportApprovalChanged(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null)
        {
            ExportSupportButton.IsEnabled = Workspace.CanExportSupport && SupportExportApproved.IsChecked == true;
        }
    }

    private void RefreshSupportExport()
    {
        var preview = Workspace.SupportPreview;
        if (!ReferenceEquals(_displayedSupportPreview, preview))
        {
            _displayedSupportPreview = preview;
            SupportExportApproved.IsChecked = false;
            SupportOperatorCode.Clear();
            SupportFiles.SelectedIndex = 0;
            SetError(SupportInputError, string.Empty);
        }

        SupportPreviewRegion.Visibility = VisibleWhen(preview is not null);
        CancelSupportButton.Visibility = VisibleWhen(Workspace.CanCancelSupport);
        RetrySupportAuditButton.Visibility = VisibleWhen(Workspace.CanRetrySupportAudit);
        ExportSupportButton.IsEnabled = Workspace.CanExportSupport && SupportExportApproved.IsChecked == true;
        if (_lastSupportStatus != Workspace.SupportExportStatus)
        {
            _lastSupportStatus = Workspace.SupportExportStatus;
            Announce(SupportExportMessage);
        }

        if (_lastSupportHistory != Workspace.SupportHistoryStatus)
        {
            _lastSupportHistory = Workspace.SupportHistoryStatus;
            Announce(SupportHistoryMessage);
        }
    }
}
