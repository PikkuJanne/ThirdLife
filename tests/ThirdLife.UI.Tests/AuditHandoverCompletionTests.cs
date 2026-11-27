using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    [Fact]
    public async Task ReadinessControlReflowsAndKeepsKeyboardReachAtDoubleScaleAndHighContrast()
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        await using var workspace = new AuditWorkspace(
            async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, clock, null, () => 180_000, () => 10, token).ConfigureAwait(false),
            [AuditTestProvider.Available("handover-signoff-layout", ProviderEvidenceOrigin.ActiveMachine)], clock);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());

        InHandover(workspace, (window, view) =>
        {
            window.Width = 660;
            window.Height = 520;
            var scroller = Named<ScrollViewer>(window, "ShellScroll");
            var content = Assert.IsAssignableFrom<FrameworkElement>(scroller.Content);
            content.LayoutTransform = new ScaleTransform(2, 2);
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/ThirdLife.UI;component/Themes/Trace.HighContrast.xaml", UriKind.Relative),
            });
            var button = Named<Button>(view, "ReviewHandoverButton");
            const string longLabel = "Die gespeicherten Voraussetzungen für die Übergabe dieses Werkstattauftrags überprüfen";
            button.Content = longLabel;
            button.FontSize = 22;
            ShellTestHost.Drain(window);
            Assert.Equal(0, scroller.ScrollableWidth);
            Assert.Equal(longLabel, ShellTestHost.ButtonPeer(button).GetName());
            Assert.Equal(SystemColors.WindowColor, Assert.IsType<SolidColorBrush>(button.Background).Color);
            Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            Assert.True(button.Focus());
            button.BringIntoView();
            ShellTestHost.Drain(window);
            var bounds = button.TransformToAncestor(scroller).TransformBounds(new Rect(button.RenderSize));
            Assert.InRange(bounds.Right, 0, scroller.ActualWidth + 0.001);
            Assert.InRange(bounds.Bottom, 0, scroller.ActualHeight + 0.001);
            Assert.Same(button, Keyboard.FocusedElement);

            Invoke(window, button);
            PumpUntil(window, () => !workspace.IsBusy && workspace.HandoverBlockers.Count > 0);
            var status = Named<TextBlock>(view, "HandoverCompletionMessage");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Same(status, Keyboard.FocusedElement);
            Assert.Contains("blocked", status.Text, StringComparison.Ordinal);
            NextIs(window, Named<TextBox>(view, "HandoverCompletionOperator"));
            Assert.False(Named<Button>(view, "CompleteHandoverButton").IsEnabled);
        });
    }
}
