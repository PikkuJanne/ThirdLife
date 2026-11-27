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
