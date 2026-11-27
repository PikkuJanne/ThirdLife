using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using ThirdLife.Core.Jobs;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private (JobId JobId, long Sequence)? _backupReadinessContext;
    private Guid? _shownBackupReadinessReview;
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

    private void BackupReadinessInputChanged(object sender, RoutedEventArgs e)
    {
        if (_workspace is not null) { Workspace.InvalidateBackupReadiness(); }
    }

    private async void PrepareBackupSample(object sender, RoutedEventArgs e)
    {
        if (Workspace.BackupReadinessReviewId is not { } reviewId) { return; }
        if (BackupSampleConfirmed.IsChecked != true)
        {
            SetError(BackupReadinessError, "Confirm the displayed drive and scope with the recipient.");
            FocusAndReveal(BackupSampleConfirmed);
            return;
        }
        if (string.IsNullOrWhiteSpace(BackupSampleOperator.Text))
        {
            SetError(BackupReadinessError, "Enter your workshop operator code for the sample record.");
            FocusAndReveal(BackupSampleOperator);
            return;
        }
        SetError(BackupReadinessError, string.Empty);
        FocusAndReveal(BackupReadinessMessage);
        await Workspace.PrepareBackupSampleAsync(reviewId, recipientConfirmed: true, BackupSampleOperator.Text);
    }

    private async void CheckBackupSample(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(BackupReadinessMessage);
        await Workspace.CheckBackupSampleAsync();
    }

    private void RefreshBackupReadiness()
    {
        BackupReadinessRegion.Visibility = VisibleWhen(Workspace.ShowsBackupReadiness);
        var review = Workspace.BackupReadinessReviewId;
        if (_shownBackupReadinessReview != review)
        {
            _shownBackupReadinessReview = review;
            BackupSampleConfirmed.IsChecked = false;
            BackupSampleOperator.Clear();
        }
        BackupSampleRegion.Visibility = VisibleWhen(review.HasValue);
        BackupPreparedRegion.Visibility = VisibleWhen(Workspace.ShowsBackupSampleCheck);
        BackupSampleJournalMessage.Visibility = VisibleWhen(Workspace.BackupSampleJournalStatus.Length != 0);
        (JobId, long)? context = Workspace.ShowsBackupReadiness && Workspace.RecipientHandover is { } handover
            ? (handover.JobId, handover.Sequence) : null;
        if (_backupReadinessContext != context)
        {
            _backupReadinessContext = context;
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
