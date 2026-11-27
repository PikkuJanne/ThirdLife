using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Finalization;
using ThirdLife.Persistence;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditFinalizationTests
{
    [Fact]
    public async Task HandoverRecordsAnAllowedReasonAndReopensTheHistoryWhenARecheckIsRequired()
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        uint bootSession = 10;
        const string actorCode = "operator-finalization-ui";
        string jobId;
        await using (var workspace = OpenWorkspace())
        {
            await workspace.CreateJobAsync();
            await workspace.RecordSanitizationAsync(VerifiedSample());
            jobId = workspace.JobId;
            InHandover(workspace, (window, view) =>
            {
                var checks = Named<ComboBox>(view, "FinalizationCheck");
                Assert.Equal(FinalizationChecklist.Rules.Count, checks.Items.Count);
                Assert.Equal("Workshop check", new ComboBoxAutomationPeer(checks).GetName());
                checks.SelectedValue = FinalizationRuleId.PendingRestart;
                Assert.False(Named<StackPanel>(view, "FinalizationRecordFields").IsVisible);

                checks.SelectedValue = FinalizationRuleId.BrowserSessions;
                var outcome = Named<ComboBox>(view, "FinalizationOutcomeInput");
                Assert.True(checks.Focus());
                NextIs(window, outcome);
                outcome.SelectedValue = FinalizationOutcome.NotApplicable;
                var reason = Named<ComboBox>(view, "FinalizationReasonInput");
                Assert.Single(reason.Items);
                var save = Named<Button>(view, "SaveFinalizationButton");
                Assert.Equal("Save check result", ShellTestHost.ButtonPeer(save).GetName());
                Invoke(window, save);
                Assert.Same(reason, Keyboard.FocusedElement);
                Assert.Empty(workspace.Finalization!.History);

                reason.SelectedValue = FinalizationReason.NoWorkshopUse;
                Named<TextBox>(view, "FinalizationOperatorCode").Text = actorCode;
                Invoke(window, save);
                PumpUntil(window, () => workspace.Finalization?.History.Count == 1 && !workspace.IsBusy);

                var resultMessage = Named<TextBlock>(view, "FinalizationMessage");
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(resultMessage));
                Assert.Same(resultMessage, Keyboard.FocusedElement);
                Assert.Contains("result saved", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("handover remains open", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Operator confirmation", Named<TextBlock>(view, "FinalizationFinding").Text, StringComparison.Ordinal);
            });
        }

        await using var reopened = OpenWorkspace();
        await reopened.OpenJobAsync(jobId);
        InHandover(reopened, (window, view) =>
        {
            var checks = Named<ComboBox>(view, "FinalizationCheck");
            checks.SelectedValue = FinalizationRuleId.BrowserSessions;
            var history = Named<TextBox>(view, "FinalizationHistory");
            Assert.Contains("Current confirmation", history.Text, StringComparison.Ordinal);
            Assert.Contains(actorCode, history.Text, StringComparison.Ordinal);
            Assert.Contains("The workshop did not use this", history.Text, StringComparison.Ordinal);

            bootSession++;
            Invoke(window, Named<Button>(view, "RefreshFinalizationButton"));
            PumpUntil(window, () => reopened.CanRecordFinalization && reopened.Finalization!.Rows.Single(row =>
                row.Rule.Id == FinalizationRuleId.BrowserSessions).NeedsRecheck);
            Assert.Contains("Recheck required", Named<TextBlock>(view, "FinalizationFinding").Text, StringComparison.Ordinal);
            Assert.Contains("Earlier confirmation", history.Text, StringComparison.Ordinal);
            Assert.Single(reopened.Finalization!.History);
        });

        AuditWorkspace OpenWorkspace() => new(
            async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, clock, faultInjector: null, () => 180_000, () => bootSession, token)
                .ConfigureAwait(false),
            [AuditTestProvider.Available("finalization-ui")], clock);
    }

    internal static void InHandover(AuditWorkspace workspace, Action<Window, FrameworkElement> check)
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Handover);
                PumpUntil(window, () => workspace.CanRecordFinalization, () => workspace.FinalizationStatus);
                check(window, Named<FrameworkElement>(window, "AuditView"));
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }
}
