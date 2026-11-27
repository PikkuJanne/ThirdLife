using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditFinalizationTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditRecipientHandoverTests
{
    [Fact]
    public async Task AssistedRecordsTheRecipientChoiceAndSealedReopensWithSetupDeferred()
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        const string actor = "operator-handover-ui";
        string jobId;

        await using (var workspace = OpenWorkspace())
        {
            await workspace.CreateJobAsync();
            await workspace.RecordSanitizationAsync(VerifiedSample());
            jobId = workspace.JobId;
            InHandover(workspace, (window, view) =>
            {
                var modeInput = Named<ComboBox>(view, "HandoverModeChoice");
                var actorInput = Named<TextBox>(view, "HandoverModeOperator");
                var modeButton = Named<Button>(view, "SaveHandoverModeButton");
                actorInput.Text = actor;
                modeInput.SelectedValue = HandoverMode.Assisted;
                Invoke(window, modeButton);
                PumpUntil(window, () => !workspace.IsBusy && workspace.RecipientHandover?.Handover?.Mode == HandoverMode.Assisted);

                var choiceArea = Named<ComboBox>(view, "RecipientSetupAreaChoice");
                var choiceResult = Named<ComboBox>(view, "RecipientSetupResult");
                var presence = Named<CheckBox>(view, "RecipientPresentCheck");
                var choiceButton = Named<Button>(view, "SaveRecipientChoiceButton");
                choiceArea.SelectedValue = RecipientSetupArea.Backup;
                choiceResult.SelectedValue = RecipientSetupChoice.GuidanceRequested;
                Invoke(window, choiceButton);
                Assert.Same(presence, Keyboard.FocusedElement);
                Assert.Single(workspace.RecipientHandover!.History);

                presence.IsChecked = true;
                Invoke(window, choiceButton);
                PumpUntil(window, () => !workspace.IsBusy && workspace.RecipientHandover?.History.Count == 2);
                Assert.Contains("Backup · Guidance requested", Named<TextBlock>(view, "RecipientChoicesSummary").Text, StringComparison.Ordinal);
                Assert.True(workspace.RecipientHandover!.History[^1].RecipientPresent);

                Named<ComboBox>(view, "FinalizationCheck").SelectedValue = FinalizationRuleId.TestMedia;
                Named<ComboBox>(view, "FinalizationOutcomeInput").SelectedValue = FinalizationOutcome.Passed;
                Named<TextBox>(view, "FinalizationOperatorCode").Text = actor;
                Invoke(window, Named<Button>(view, "SaveFinalizationButton"));
                PumpUntil(window, () => !workspace.IsBusy && workspace.Finalization?.History.Count == 1);
                Invoke(window, Named<Button>(view, "GenerateReportButton"));
                PumpUntil(window, () => !workspace.IsBusy && workspace.ReportArtifact is not null);

                modeInput.SelectedValue = HandoverMode.Sealed;
                Invoke(window, modeButton);
                PumpUntil(window, () => !workspace.IsBusy && workspace.RecipientHandover?.Handover?.Mode == HandoverMode.Sealed);
                Assert.Null(workspace.ReportArtifact);
                Assert.False(Named<StackPanel>(view, "RecipientChoiceFields").IsVisible);
                Assert.Contains("Backup · Pending", Named<TextBlock>(view, "RecipientChoicesSummary").Text, StringComparison.Ordinal);
                Assert.Equal(AutomationLiveSetting.Polite,
                    AutomationProperties.GetLiveSetting(Named<TextBlock>(view, "HandoverModeMessage")));
                Assert.True(Assert.Single(workspace.Finalization!.Rows, row => row.Rule.Id == FinalizationRuleId.TestMedia).NeedsRecheck);
            });
        }

        await using var reopened = OpenWorkspace();
        await reopened.OpenJobAsync(jobId);
        InHandover(reopened, (_, view) =>
        {
            var saved = Assert.IsType<SavedRecipientHandover>(reopened.RecipientHandover);
            Assert.Equal(HandoverMode.Sealed, saved.Handover?.Mode);
            Assert.Equal(3, saved.History.Count);
            Assert.All(saved.Handover!.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
            Assert.False(Named<StackPanel>(view, "RecipientChoiceFields").IsVisible);
        });

        AuditWorkspace OpenWorkspace() => new(
            async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, clock, faultInjector: null, () => 180_000, () => 10, token)
                .ConfigureAwait(false),
            [AuditTestProvider.Available("recipient-handover-ui")], clock, new WorkshopReportStore(Path.Combine(disk.Root, "reports")));
    }
}
