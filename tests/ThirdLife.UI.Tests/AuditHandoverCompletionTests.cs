using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Core.Handover;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditFinalizationTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditHandoverCompletionTests
{
    [Fact]
    public async Task OperatorCanReviewMissingEvidenceAndRecordOmissionWithoutCompletingJob()
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        const string actor = "operator-signoff-ui";
        await using var workspace = new AuditWorkspace(
            async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, clock, null, () => 180_000, () => 10, token).ConfigureAwait(false),
            [AuditTestProvider.Available("handover-signoff-ui", ProviderEvidenceOrigin.ActiveMachine)], clock,
            new WorkshopReportStore(Path.Combine(disk.Root, "reports")));
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());

        InHandover(workspace, (window, view) =>
        {
            var review = Named<Button>(view, "ReviewHandoverButton");
            Assert.Equal("Review handover readiness", ShellTestHost.ButtonPeer(review).GetName());
            Invoke(window, review);
            PumpUntil(window, () => !workspace.IsBusy && workspace.HandoverBlockers.Count > 0);
            Assert.Contains(workspace.HandoverBlockers, blocker => blocker.Contains("cold-boot", StringComparison.Ordinal));
            Assert.Contains(workspace.HandoverBlockers, blocker => blocker.Contains("Current workshop report", StringComparison.Ordinal));
            var status = Named<TextBlock>(view, "HandoverCompletionMessage");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Contains("blocked", status.Text, StringComparison.Ordinal);

            Assert.True(workspace.CanRecordSupportOmission);
            Named<TextBox>(view, "HandoverCompletionOperator").Text = actor;
            Named<ComboBox>(view, "SupportOmissionReasonChoice").SelectedValue = SupportBundleOmissionReason.PrivacyChoice;
            Invoke(window, Named<Button>(view, "RecordSupportOmissionButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.HandoverOutputSummary.Count == 1, () => workspace.HandoverCompletionStatus);
            Assert.Contains(actor, Assert.Single(workspace.HandoverOutputSummary), StringComparison.Ordinal);
            Assert.False(Named<Button>(view, "CompleteHandoverButton").IsEnabled);
            Assert.False(Named<CheckBox>(view, "HandoverCompletionConfirmed").IsEnabled);
        });
    }
}
