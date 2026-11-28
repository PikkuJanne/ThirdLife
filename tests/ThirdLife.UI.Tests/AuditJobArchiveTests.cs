using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
using ThirdLife.Reports.Storage;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditJobArchiveTests
{
    [Fact]
    public async Task ArchivesAndRestoresTheSameJobThroughTheJobsSurface()
    {
        using var disk = new AuditTestStore();
        var storeRoot = Path.Combine(disk.Root, "store");
        var clock = TimeProvider.System;
        await using var workspace = new AuditWorkspace(
            async token => await SqliteJobStore.OpenForTestingAsync(storeRoot, SqliteJobStore.CurrentSchemaVersion, clock,
                null, () => 180_000, () => 10, token).ConfigureAwait(false),
            [AuditTestProvider.Available("job-archive", ProviderEvidenceOrigin.ActiveMachine)], clock);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        var original = Assert.IsType<StoredJob>(workspace.Job);
        var jobId = original.Job.JobId;

        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                var view = Named<FrameworkElement>(window, "AuditView");
                var jobs = Named<Button>(window, "JobsButton");
                var archive = Named<Button>(view, "JobArchiveButton");
                Assert.False(archive.IsVisible);
                Invoke(window, jobs);
                PumpUntil(window, () => archive.IsVisible && archive.IsEnabled);
                Assert.Equal("Archive job", new ButtonAutomationPeer(archive).GetName());

                Invoke(window, archive);
                PumpUntil(window, () => !workspace.IsBusy && workspace.Job is { IsArchived: true },
                    () => workspace.OperationError);
                Assert.Equal("Restore archived job", new ButtonAutomationPeer(archive).GetName());
                Assert.False(workspace.CanEditJob);
                Assert.False(workspace.CanAssess);

                Invoke(window, Named<Button>(view, "CloseJobButton"));
                Assert.Null(workspace.Job);
                Assert.False(archive.IsVisible);
                Named<TextBox>(view, "JobIdInput").Text = jobId.Value;
                Invoke(window, Named<Button>(view, "OpenJobButton"));
                var stages = Named<ListBox>(window, "StageList");
                PumpUntil(window, () => !workspace.IsBusy && workspace.Job?.Job.JobId == jobId &&
                    stages.SelectedItem is not null, () => workspace.OperationError);
                Assert.True(Assert.IsType<StoredJob>(workspace.Job).IsArchived);
                Assert.False(workspace.CanEditJob);

                Invoke(window, jobs);
                PumpUntil(window, () => archive.IsVisible && archive.IsEnabled);
                Invoke(window, archive);
                PumpUntil(window, () => !workspace.IsBusy && workspace.Job is { IsArchived: false },
                    () => workspace.OperationError);
                Assert.True(workspace.CanEditJob);
                Assert.True(workspace.CanAssess);
                Assert.Equal("Archive job", new ButtonAutomationPeer(archive).GetName());
            }
            finally
            {
                CloseWindow(window);
            }
        });

        await using var reopened = await SqliteJobStore.OpenForTestingAsync(storeRoot, SqliteJobStore.CurrentSchemaVersion,
            clock, null, () => 180_000, () => 10);
        var restored = Assert.IsType<StoredJob>(await reopened.LoadJobAsync(jobId));
        Assert.False(restored.IsArchived);
        Assert.Equal(Assert.Single(original.SanitizationEvidence).Metadata.EvidenceId,
            Assert.Single(restored.SanitizationEvidence).Metadata.EvidenceId);
        Assert.Equal(new[] { JobCheckpointKind.Archived, JobCheckpointKind.Restored },
            restored.Checkpoints.Where(item => item.Kind is JobCheckpointKind.Archived or JobCheckpointKind.Restored)
                .Select(item => item.Kind));
    }

    [Fact]
    public async Task ReviewsTheRecordedOriginalSampleWithoutReadingRecoveredOrUnrecordedCopies()
    {
        using var disk = new AuditTestStore();
        var original = Path.Combine(disk.Root, "original");
        var recovered = Path.Combine(disk.Root, "recovered");
        var probe = new FileHistoryRestoreProbe(original, recovered);
        var job = Job.Create(DeviceId.New(), DateTimeOffset.UtcNow);
        FileHistoryRestoreSample published;
        FileHistoryRestoreSample unrecorded;
        await using (var store = await disk.OpenSqliteAsync())
        {
            await store.CreateJobAsync(job);
            var actor = new OperatorId("operator-sample-review");
            var initial = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            await store.SaveHandoverModeAsync(job.JobId, initial, HandoverMode.Assisted, actor);
            var handover = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            var destination = new FileHistoryDestinationObservation(Guid.NewGuid(), true, 50_000);
            var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(handover, true, destination, 1, true).Review);
            var approval = review.Approve(review.ReviewId, true);
            published = approval.CreateRestoreSample(handover, true, destination);
            unrecorded = approval.CreateRestoreSample(handover, true, destination);
            _ = await store.StageFileHistorySampleAsync(approval, published, actor);
            Assert.Equal(FileHistorySamplePreparation.Prepared, probe.Prepare(published));
            Assert.Equal(FileHistorySamplePreparation.Prepared, probe.Prepare(unrecorded));
            await store.SetArchiveStateAsync(job.JobId, true, DateTimeOffset.UtcNow);
        }

        var sourcePath = Path.Combine(original, published.FileName);
        var recoveredPath = Path.Combine(recovered, published.FileName);
        File.Copy(sourcePath, recoveredPath);
        await using var workspace = new AuditWorkspace(
            async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [AuditTestProvider.Available("archived-sample", ProviderEvidenceOrigin.ActiveMachine)], TimeProvider.System,
            backupSampleProbe: probe);
        await workspace.OpenJobAsync(job.JobId.Value);
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                ShellTestHost.Invoke(window, "JobsButton");
                var view = Named<FrameworkElement>(window, "AuditView");
                var review = Named<Button>(view, "ReviewArchivedOutputsButton");
                var status = Named<TextBlock>(view, "AuditStatus");
                Assert.Equal("Check saved copies and original samples", new ButtonAutomationPeer(review).GetName());
                using (var locked = File.Open(recoveredPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Invoke(window, review);
                    PumpUntil(window, () => !workspace.IsBusy && workspace.Status.Contains("records: 1", StringComparison.Ordinal),
                        () => workspace.Status);
                    Assert.Contains("Original samples that could not be verified: 0", workspace.Status, StringComparison.Ordinal);
                    Assert.Same(status, Keyboard.FocusedElement);
                    var announcement = new TextBlockAutomationPeer(status);
                    Assert.Equal(AutomationLiveSetting.Polite, announcement.GetLiveSetting());
                    Assert.Equal(workspace.Status, announcement.GetName());
                }

                var changed = File.ReadAllBytes(sourcePath);
                changed[0] ^= 1;
                File.WriteAllBytes(sourcePath, changed);
                Invoke(window, review);
                PumpUntil(window, () => !workspace.IsBusy && workspace.Status.Contains("records: 0", StringComparison.Ordinal),
                    () => workspace.Status);
                Assert.Contains("Original samples that could not be verified: 1", workspace.Status, StringComparison.Ordinal);
                Assert.Equal(changed, File.ReadAllBytes(sourcePath));
                Assert.Equal(published.CopyContent(), File.ReadAllBytes(recoveredPath));
                Assert.Equal(unrecorded.CopyContent(), File.ReadAllBytes(Path.Combine(original, unrecorded.FileName)));
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }
}
