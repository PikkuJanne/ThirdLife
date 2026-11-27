using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Core.Handover;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
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
            var button = Named<Button>(view, "CheckBackupReadinessButton");
            var result = Named<TextBlock>(view, "BackupReadinessMessage");
            var presence = Named<CheckBox>(view, "BackupRecoveryConfirmed");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(result));
            Named<TextBox>(view, "BackupScopeMebibytes").Text = "1";
            var pickerOpened = false;
            view.SelectBackupDriveForTesting = () =>
            {
                pickerOpened = true;
                return Path.GetPathRoot(Environment.SystemDirectory);
            };
            Invoke(window, button);
            Assert.False(pickerOpened);
            presence.IsChecked = true;
            Invoke(window, button);
            PumpUntil(window, () => !workspace.IsBusy && workspace.BackupReadinessStatus.Contains("system volume", StringComparison.Ordinal),
                () => workspace.BackupReadinessStatus);
            Assert.True(pickerOpened);
            mode.SelectedValue = HandoverMode.Sealed;
            Invoke(window, Named<Button>(view, "SaveHandoverModeButton"));
            PumpUntil(window, () => !workspace.IsBusy && !workspace.ShowsBackupReadiness);
            Assert.False(Named<GroupBox>(view, "BackupReadinessRegion").IsVisible);
        });
    }
}
