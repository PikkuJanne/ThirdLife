using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThirdLife.Core.Handover;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
using ThirdLife.Reports.Storage;
using ThirdLife.UI.Features.ManualTests;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditFinalizationTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditBackupReadinessTests
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public async Task PreparesAnIsolatedSampleOnlyAfterTheRecipientConfirmsTheCurrentReview()
    {
        var volume = Environment.GetEnvironmentVariable("THIRDLIFE_FILE_HISTORY_TEST_VOLUME");
        Assert.False(string.IsNullOrWhiteSpace(volume), "Specify the exact test volume root for this observation.");
        using var disk = new AuditTestStore();
        var original = Path.Combine(disk.Root, "original");
        var recovered = Path.Combine(disk.Root, "recovered");
        var time = TimeProvider.System;
        await using var workspace = new AuditWorkspace(
            async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, time, null, () => 180_000, () => 10, token).ConfigureAwait(false),
            [AuditTestProvider.Available("file-history-sample", ProviderEvidenceOrigin.ActiveMachine)], time,
            backupSampleProbe: new FileHistoryRestoreProbe(original, recovered));
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());

        InHandover(workspace, (window, element) =>
        {
            var view = Assert.IsType<AuditWorkspaceView>(element);
            var mode = Named<ComboBox>(view, "HandoverModeChoice");
            mode.SelectedValue = HandoverMode.Assisted;
            Named<TextBox>(view, "HandoverModeOperator").Text = "operator-backup-sample";
            Invoke(window, Named<Button>(view, "SaveHandoverModeButton"));
            PumpUntil(window, () => workspace.CanCheckBackupReadiness);
            Named<TextBox>(view, "BackupScopeMebibytes").Text = "1";
            Named<CheckBox>(view, "BackupRecoveryConfirmed").IsChecked = true;
            view.SelectBackupDriveForTesting = () => volume;
            Invoke(window, Named<Button>(view, "CheckBackupReadinessButton"));
            PumpUntil(window, () => workspace.CanPrepareBackupSample);
            var approval = Named<CheckBox>(view, "BackupSampleConfirmed");
            var prepare = Named<Button>(view, "PrepareBackupSampleButton");
            Assert.Contains("declared 1 MiB", workspace.BackupReadinessStatus, StringComparison.Ordinal);
            Invoke(window, prepare);
            Assert.Same(approval, Keyboard.FocusedElement);
            Assert.False(Directory.Exists(original));
            approval.IsChecked = true;
            var operatorCode = Named<TextBox>(view, "BackupSampleOperator");
            Invoke(window, prepare);
            Assert.Same(operatorCode, Keyboard.FocusedElement);
            Assert.False(Directory.Exists(original));
            operatorCode.Text = "operator-backup-sample";
            Invoke(window, prepare);
            PumpUntil(window, () => !workspace.IsBusy && workspace.CanCheckBackupSample);
            Assert.Contains("Backup remains pending", workspace.BackupReadinessStatus, StringComparison.Ordinal);
            var sample = Assert.Single(Directory.EnumerateFiles(original, "thirdlife-restore-*.txt"));
            Assert.StartsWith("ThirdLife File History restore sample", File.ReadAllText(sample), StringComparison.Ordinal);
            Assert.Contains(Path.GetFileName(sample), workspace.BackupSampleRecoveryInstructions, StringComparison.Ordinal);
            Assert.Contains("original in a library", workspace.BackupSampleRecoveryInstructions, StringComparison.Ordinal);
            Assert.Contains("choose Restore to", workspace.BackupSampleRecoveryInstructions, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(recovered));
            var checkCopy = Named<Button>(view, "CheckBackupSampleButton");
            Invoke(window, checkCopy);
            PumpUntil(window, () => !workspace.IsBusy && workspace.BackupReadinessStatus.Contains("unavailable", StringComparison.Ordinal));
            File.Copy(sample, Path.Combine(recovered, Path.GetFileName(sample)));
            Invoke(window, checkCopy);
            PumpUntil(window, () => !workspace.IsBusy && workspace.BackupReadinessStatus.StartsWith("The separate copy matches", StringComparison.Ordinal));
            Assert.Contains("Backup remains pending", workspace.BackupReadinessStatus, StringComparison.Ordinal);
            var current = Assert.IsType<ThirdLife.Core.Jobs.StoredJob>(workspace.Job);
            Invoke(window, Named<Button>(view, "CloseJobButton"));
            Assert.Null(workspace.Job);
            Named<TextBox>(view, "JobIdInput").Text = current.Job.JobId.Value;
            Invoke(window, Named<Button>(view, "OpenJobButton"));
            var stages = Named<ListBox>(window, "StageList");
            PumpUntil(window, () => !workspace.IsBusy && workspace.Job?.Job.JobId == current.Job.JobId &&
                stages.SelectedItem is not null, () => workspace.OperationError);
            Assert.IsType<MainWindow>(window).ShowStage(ShellStage.Handover);
            var savedMessage = Named<TextBlock>(view, "BackupSampleJournalMessage");
            PumpUntil(window, () => savedMessage.IsVisible && savedMessage.Text.Length != 0);
            Assert.Contains("This handover recorded a matching separate sample copy. Backup remains pending.", savedMessage.Text, StringComparison.Ordinal);
            Assert.False(workspace.CanCheckBackupSample);
            Named<TextBox>(view, "BackupScopeMebibytes").Text = "1";
            Named<CheckBox>(view, "BackupRecoveryConfirmed").IsChecked = true;
            Invoke(window, Named<Button>(view, "CheckBackupReadinessButton"));
            PumpUntil(window, () => workspace.CanReopenBackupSample);
            var resume = Named<Button>(view, "ReopenBackupSampleButton");
            Invoke(window, resume);
            Assert.Same(approval, Keyboard.FocusedElement);
            Assert.False(workspace.CanCheckBackupSample);
            approval.IsChecked = true;
            Invoke(window, resume);
            Assert.Same(operatorCode, Keyboard.FocusedElement);
            Assert.False(workspace.CanCheckBackupSample);
            operatorCode.Text = "operator-resumed-sample";
            Invoke(window, resume);
            PumpUntil(window, () => !workspace.IsBusy && workspace.CanCheckBackupSample);
            Assert.Contains("Reopened the saved harmless sample", workspace.BackupReadinessStatus, StringComparison.Ordinal);
            Invoke(window, checkCopy);
            PumpUntil(window, () => !workspace.IsBusy && workspace.BackupReadinessStatus.StartsWith("The separate copy matches", StringComparison.Ordinal));
            Assert.Equal(sample, Assert.Single(Directory.EnumerateFiles(original, "thirdlife-restore-*.txt")));
        });

        var job = Assert.IsType<ThirdLife.Core.Jobs.StoredJob>(workspace.Job);
        await using var reopened = await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
            SqliteJobStore.CurrentSchemaVersion, time, null, () => 180_000, () => 10);
        var saved = await reopened.LoadFileHistorySampleHistoryAsync(job.Job.JobId);
        Assert.Equal(new[] { FileHistorySampleAuditState.Preparing, FileHistorySampleAuditState.Prepared,
            FileHistorySampleAuditState.CopyUnavailable, FileHistorySampleAuditState.CopyMatches, FileHistorySampleAuditState.CopyMatches },
            saved.Select(entry => entry.State));
        Assert.Equal("operator-backup-sample", saved[3].OperatorId.Value);
        Assert.Equal("operator-resumed-sample", saved[4].OperatorId.Value);
        Assert.Single(saved.Select(entry => entry.SampleId).Distinct());
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task AssistedReadinessRequiresRecipientAndRejectsTheSystemVolume()
    {
        using var disk = new AuditTestStore();
        var time = TimeProvider.System;
        await using var workspace = new AuditWorkspace(
            async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, time, null, () => 180_000, () => 10, token).ConfigureAwait(false),
            [AuditTestProvider.Available("file-history-review", ProviderEvidenceOrigin.ActiveMachine)], time);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());

        InHandover(workspace, (window, element) =>
        {
            var view = Assert.IsType<AuditWorkspaceView>(element);
            Assert.False(Named<GroupBox>(view, "BackupReadinessRegion").IsVisible);
            var mode = Named<ComboBox>(view, "HandoverModeChoice");
            mode.SelectedValue = HandoverMode.Assisted;
            Named<TextBox>(view, "HandoverModeOperator").Text = "operator-backup-test";
            Invoke(window, Named<Button>(view, "SaveHandoverModeButton"));
            PumpUntil(window, () => workspace.CanCheckBackupReadiness);
            window.Width = 660;
            window.Height = 520;
            var scroller = Named<ScrollViewer>(window, "ShellScroll");
            Assert.IsAssignableFrom<FrameworkElement>(scroller.Content).LayoutTransform = new ScaleTransform(2, 2);
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/ThirdLife.UI;component/Themes/Trace.HighContrast.xaml", UriKind.Relative),
            });
            var button = Named<Button>(view, "CheckBackupReadinessButton");
            var result = Named<TextBlock>(view, "BackupReadinessMessage");
            var presence = Named<CheckBox>(view, "BackupRecoveryConfirmed");
            var size = Named<TextBox>(view, "BackupScopeMebibytes");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(result));
            Assert.Equal("Estimated backup size in MiB", new TextBoxAutomationPeer(size).GetName());
            const string longLabel = "Ein Laufwerk auswählen und die Voraussetzungen für den Empfänger prüfen";
            button.Content = longLabel;
            button.FontSize = 22;
            size.Text = "1";
            ShellTestHost.Drain(window);
            Assert.True(size.Focus());
            NextIs(window, presence);
            NextIs(window, button);
            button.BringIntoView();
            ShellTestHost.Drain(window);
            Assert.Equal(0, scroller.ScrollableWidth);
            Assert.Equal(longLabel, ShellTestHost.ButtonPeer(button).GetName());
            Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            var bounds = button.TransformToAncestor(scroller).TransformBounds(new Rect(button.RenderSize));
            Assert.InRange(bounds.Right, 0, scroller.ActualWidth + 0.001);
            Assert.InRange(bounds.Bottom, 0, scroller.ActualHeight + 0.001);
            var pickerOpened = false;
            view.SelectBackupDriveForTesting = () =>
            {
                pickerOpened = true;
                return Path.GetPathRoot(Environment.SystemDirectory);
            };
            Invoke(window, button);
            Assert.Same(presence, Keyboard.FocusedElement);
            Assert.False(pickerOpened);
            presence.IsChecked = true;
            Invoke(window, button);
            PumpUntil(window, () => !workspace.IsBusy && workspace.BackupReadinessStatus.Contains("system volume", StringComparison.Ordinal),
                () => workspace.BackupReadinessStatus);
            Assert.True(pickerOpened);
            Assert.Same(result, Keyboard.FocusedElement);
            size.Text = "2";
            ShellTestHost.Drain(window);
            Assert.StartsWith("Select a backup drive", result.Text, StringComparison.Ordinal);
            mode.SelectedValue = HandoverMode.Sealed;
            Invoke(window, Named<Button>(view, "SaveHandoverModeButton"));
            PumpUntil(window, () => !workspace.IsBusy && !workspace.ShowsBackupReadiness);
            Assert.False(Named<GroupBox>(view, "BackupReadinessRegion").IsVisible);
            Assert.Empty(size.Text);
            Assert.False(presence.IsChecked);
        });
    }
}
