using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
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
}
