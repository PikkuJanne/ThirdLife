using System.Windows;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private void RefreshJobDeletion()
    {
        JobDeletionRegion.Visibility = VisibleWhen(Workspace.ShowsJobDeletion);
        JobDeletionReviewPanel.Visibility = VisibleWhen(Workspace.ShowsJobDeletionReview);
        JobDeletionApprovalPanel.Visibility = VisibleWhen(!Workspace.JobDeletionPending);
        ResumeJobDeletionButton.Visibility = VisibleWhen(Workspace.JobDeletionPending);
        ReviewJobDeletionButton.Content = Workspace.JobDeletionPending
            ? "Review pending job-data deletion" : "Review job-data deletion";
        if (!Workspace.ShowsJobDeletionReview || Workspace.JobDeletionPending)
        {
            ResetJobDeletionConfirmation();
        }
        UpdateJobDeletionConfirmation();
    }

    private void ResetJobDeletionConfirmation()
    {
        JobDeletionConfirmed.IsChecked = false;
        JobDeletionOperator.Clear();
    }

    private void UpdateJobDeletionConfirmation(object? sender = null, RoutedEventArgs? e = null)
    {
        if (ApproveJobDeletionButton is not null && _workspace is { } workspace)
        {
            ApproveJobDeletionButton.IsEnabled = workspace.CanApproveJobDeletion &&
                JobDeletionConfirmed.IsChecked == true && !string.IsNullOrWhiteSpace(JobDeletionOperator.Text);
        }
    }

    private async void ReviewJobDeletion(object sender, RoutedEventArgs e)
    {
        ResetJobDeletionConfirmation();
        FocusAndReveal(AuditStatus);
        await Workspace.ReviewJobDeletionAsync();
        if (Workspace.ShowsJobDeletionReview && AuditStatus.IsKeyboardFocused)
        {
            FocusAndReveal(JobDeletionScope);
        }
    }

    private async void ApproveJobDeletion(object sender, RoutedEventArgs e)
    {
        if (!ApproveJobDeletionButton.IsEnabled) { return; }
        var operatorCode = JobDeletionOperator.Text;
        var confirmed = JobDeletionConfirmed.IsChecked == true;
        ResetJobDeletionConfirmation();
        FocusAndReveal(AuditStatus);
        await Workspace.ApproveJobDeletionAsync(operatorCode, confirmed);
    }

    private async void ResumeJobDeletion(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(AuditStatus);
        await Workspace.ResumeJobDeletionAsync();
    }
}
