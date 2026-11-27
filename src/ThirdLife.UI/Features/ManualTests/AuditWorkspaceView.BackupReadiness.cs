using System.Globalization;
using System.Windows;
using Microsoft.Win32;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private string? _lastBackupReadiness;
    internal Func<string?>? SelectBackupDriveForTesting { get; set; }

    private async void CheckBackupReadiness(object sender, RoutedEventArgs e)
    {
        if (!Workspace.CanCheckBackupReadiness) { return; }
        if (BackupRecoveryConfirmed.IsChecked != true)
        {
            SetError(BackupReadinessError, "Confirm the recipient's presence and recovery ownership.");
            FocusAndReveal(BackupRecoveryConfirmed);
            return;
        }
        const long bytesPerMebibyte = 1024L * 1024;
        if (!long.TryParse(BackupScopeMebibytes.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var mebibytes) ||
            mebibytes is < 1 or > long.MaxValue / bytesPerMebibyte)
        {
            SetError(BackupReadinessError, "Enter the estimated backup size in whole MiB.");
            FocusAndReveal(BackupScopeMebibytes);
            return;
        }
        var selected = ChooseBackupDrive();
        if (selected is null) { return; }
        SetError(BackupReadinessError, string.Empty);
        FocusAndReveal(BackupReadinessMessage);
        await Workspace.CheckBackupReadinessAsync(selected, mebibytes * bytesPerMebibyte,
            BackupRecoveryConfirmed.IsChecked == true);
    }

    private string? ChooseBackupDrive()
    {
        if (SelectBackupDriveForTesting is { } choose) { return choose(); }
        var picker = new OpenFolderDialog { Title = "Choose the recipient's local backup drive root" };
        return picker.ShowDialog(Window.GetWindow(this)) == true ? picker.FolderName : null;
    }

    private void RefreshBackupReadiness()
    {
        BackupReadinessRegion.Visibility = VisibleWhen(Workspace.ShowsBackupReadiness);
        if (!Workspace.ShowsBackupReadiness)
        {
            BackupScopeMebibytes.Clear();
            BackupRecoveryConfirmed.IsChecked = false;
            SetError(BackupReadinessError, string.Empty);
        }
        if (_lastBackupReadiness != Workspace.BackupReadinessStatus)
        {
            _lastBackupReadiness = Workspace.BackupReadinessStatus;
            Announce(BackupReadinessMessage);
        }
    }
}
