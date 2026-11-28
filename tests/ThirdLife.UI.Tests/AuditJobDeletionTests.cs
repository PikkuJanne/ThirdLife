using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Providers;
using ThirdLife.Reports.RecipientGuide;
using ThirdLife.Reports.Storage;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditJobDeletionTests
{
    [Fact]
    public async Task JobsRequiresSeparateApprovalAndReopensAnInterruptedDeletion()
    {
        using var disk = new AuditTestStore();
        var originalDirectory = Path.Combine(disk.Root, "original");
        var recoveredDirectory = Path.Combine(disk.Root, "recovered");
        var reportDirectory = Path.Combine(disk.Root, "reports");
        var guideDirectory = Path.Combine(disk.Root, "guides");
        var reportStore = new WorkshopReportStore(reportDirectory);
        var guideStore = new RecipientGuideStore(guideDirectory);
        var probe = new FileHistoryRestoreProbe(originalDirectory, recoveredDirectory);
        var job = Job.Create(DeviceId.New(), DateTimeOffset.UtcNow);
        var other = Job.Create(DeviceId.New(), DateTimeOffset.UtcNow);
        FileHistoryRestoreSample sample;
        WorkshopReportArtifact guide;
        await using (var store = await disk.OpenSqliteAsync())
        {
            await store.CreateJobAsync(job);
            await store.CreateJobAsync(other);
            var actor = new OperatorId("fixture-deletion");
            var initial = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            await store.SaveHandoverModeAsync(job.JobId, initial, HandoverMode.Assisted, actor);
            var handover = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            var renderedGuide = RecipientGuideRenderer.Render(new(handover.Handover!, null, [], SyntheticExample: true));
            guide = await guideStore.SaveArtifactWithOwnershipAsync(job.JobId, renderedGuide, (artifact, publish, token) =>
                store.PublishJobLocalFileAsync(job.JobId,
                    new(JobLocalFileKind.RecipientGuide, artifact.FileName, artifact.Receipt.ByteLength, artifact.Receipt.Sha256),
                    publish, token));
            var destination = new FileHistoryDestinationObservation(Guid.NewGuid(), true, 50_000);
            var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(handover, true, destination, 1, true).Review);
            var approval = review.Approve(review.ReviewId, true);
            sample = approval.CreateRestoreSample(handover, true, destination);
            var staged = await store.StageFileHistorySampleAsync(approval, sample, actor);
            var outcome = await store.PublishFileHistoryOriginalAsync(job.JobId, staged, token => probe.Prepare(sample, token));
            Assert.Equal(FileHistorySamplePreparation.Prepared, outcome);
            await store.RecordFileHistorySampleAsync(job.JobId, staged, FileHistorySampleAuditState.Prepared, actor);
            await store.SetArchiveStateAsync(job.JobId, true, DateTimeOffset.UtcNow);
        }

        var originalPath = Path.Combine(originalDirectory, sample.FileName);
        var recoveredPath = Path.Combine(recoveredDirectory, sample.FileName);
        var guidePath = Path.Combine(guideDirectory, guide.FileName);
        File.Copy(originalPath, recoveredPath);
        var otherReport = reportStore.Write(other.JobId, "Synthetic unrelated report");
        var otherPath = Path.Combine(reportDirectory, otherReport.FileName);
        var unrelatedBytes = File.ReadAllBytes(otherPath);
        await using var workspace = new AuditWorkspace(
            async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [AuditTestProvider.Available("job-deletion", ProviderEvidenceOrigin.ActiveMachine)], TimeProvider.System,
            reportStore, recipientGuideStore: guideStore, backupSampleProbe: probe);
        await workspace.OpenJobAsync(job.JobId.Value);
        await workspace.GenerateReportAsync();
        var report = Assert.IsType<WorkshopReportArtifact>(workspace.ReportArtifact);
        var reportPath = Path.Combine(reportDirectory, report.FileName);

        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                ShellTestHost.Invoke(window, "JobsButton");
                var view = Named<FrameworkElement>(window, "AuditView");
                var approve = Named<Button>(view, "ApproveJobDeletionButton");
                var resume = Named<Button>(view, "ResumeJobDeletionButton");
                var archive = Named<Button>(view, "JobArchiveButton");
                var status = Named<TextBlock>(view, "AuditStatus");
                Assert.False(approve.IsVisible);

                Invoke(window, Named<Button>(view, "ReviewJobDeletionButton"));
                PumpUntil(window, () => approve.IsVisible && !workspace.IsBusy, () => workspace.Status);
                Assert.Equal(job.JobId.Value, Named<TextBox>(view, "JobDeletionCode").Text);
                var listed = Named<TextBox>(view, "JobDeletionFiles");
                Assert.Contains(report.FileName, listed.Text, StringComparison.Ordinal);
                Assert.Contains(guide.FileName, listed.Text, StringComparison.Ordinal);
                Assert.Contains(sample.FileName, listed.Text, StringComparison.Ordinal);
                Assert.DoesNotContain(otherReport.FileName, listed.Text, StringComparison.Ordinal);
                Assert.False(approve.IsEnabled);
                Assert.False(resume.IsVisible);
                Assert.True(archive.IsEnabled);
                Assert.True(File.Exists(originalPath));

                var confirmed = Named<CheckBox>(view, "JobDeletionConfirmed");
                confirmed.IsChecked = true;
                Assert.False(approve.IsEnabled);
                Named<TextBox>(view, "JobDeletionOperator").Text = "fixture-deletion";
                Assert.True(approve.IsEnabled);
                using (File.Open(reportPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Invoke(window, approve);
                    PumpUntil(window, () => !workspace.IsBusy && resume.IsVisible && resume.IsEnabled,
                        () => workspace.Status + workspace.OperationError);
                    Assert.Equal(job.JobId, workspace.Job?.Job.JobId);
                    Assert.False(archive.IsEnabled);
                    Assert.False(workspace.CanGenerateReport);
                    Assert.Equal(report.FileName, listed.Text);
                    Assert.False(File.Exists(guidePath));
                    Assert.False(File.Exists(originalPath));
                    Assert.Contains("One recorded local file needs attention", workspace.Status, StringComparison.Ordinal);
                    Assert.Equal(AutomationLiveSetting.Polite, new TextBlockAutomationPeer(status).GetLiveSetting());
                }

                Invoke(window, Named<Button>(view, "CloseJobButton"));
                Assert.Null(workspace.Job);
                Named<TextBox>(view, "JobIdInput").Text = job.JobId.Value;
                Invoke(window, Named<Button>(view, "OpenJobButton"));
                PumpUntil(window, () => !workspace.IsBusy && resume.IsVisible && resume.IsEnabled,
                    () => workspace.Status + workspace.OperationError);
                Assert.True(File.Exists(reportPath));
                Assert.Equal("Continue approved job-data deletion", new ButtonAutomationPeer(resume).GetName());
                Invoke(window, resume);
                PumpUntil(window, () => !workspace.IsBusy && workspace.Job is null, () => workspace.Status + workspace.OperationError);
                Assert.Contains("recorded local files are absent", workspace.Status, StringComparison.Ordinal);
            }
            finally
            {
                CloseWindow(window);
            }
        });

        Assert.False(File.Exists(reportPath));
        Assert.Equal(sample.CopyContent(), File.ReadAllBytes(recoveredPath));
        Assert.Equal(unrelatedBytes, File.ReadAllBytes(otherPath));
        await using var reopened = await disk.OpenSqliteAsync();
        Assert.Null(await reopened.LoadJobAsync(job.JobId));
        Assert.Equal(other.JobId, Assert.IsType<StoredJob>(await reopened.LoadJobAsync(other.JobId)).Job.JobId);
    }
}
