using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using ThirdLife.UI.Features.ManualTests;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class BatteryInspectionTests
{
    private const string Warning = "Battery reports cannot detect swelling. Swelling requires human inspection.";

    [Fact]
    public void WarningIsReadableInTheHardwareAndSelectedBatteryContexts()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace());
            try
            {
                ShellTestHost.Show(window);
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var manualNotice = Named<TextBlock>(audit, "ManualBatterySafety");
                var evidenceNotice = Named<TextBlock>(audit, "BatterySafety");
                Assert.False(manualNotice.IsVisible);
                Assert.False(evidenceNotice.IsVisible);

                var findings = Named<ListBox>(audit, "AssessmentObservations");
                findings.ItemsSource = new[]
                {
                    new AssessmentFinding("machine", "Observed", "Fixture", "fixture-machine", "Machine", "Fixture time", "Fixture machine evidence"),
                    new AssessmentFinding("cycle-count", "Unavailable", "Fixture", "fixture-battery", "Battery cycle count", "Fixture time", "Fixture battery evidence"),
                };
                window.ShowStage(ShellStage.Inspect);
                ShellTestHost.Drain(window);
                findings.SelectedIndex = 0;
                ShellTestHost.Drain(window);
                Assert.False(evidenceNotice.IsVisible);
                findings.SelectedIndex = 1;
                ShellTestHost.Drain(window);
                Assert.True(evidenceNotice.IsVisible);
                var findingPeer = new ListBoxAutomationPeer(findings);
                var row = Assert.IsType<ListBoxItem>(findings.ItemContainerGenerator.ContainerFromIndex(1));
                var rowPeer = new ListBoxItemAutomationPeer(row, findingPeer);
                Assert.Equal("Battery cycle count", rowPeer.GetName());
                Assert.Equal("Fixture", rowPeer.GetItemStatus());
                Assert.Contains(rowPeer.GetChildren(), child => child.GetName() == "Unavailable");
                Assert.Equal(Warning, new TextBlockAutomationPeer(evidenceNotice).GetName());

                var tabs = Named<TabControl>(audit, "InspectionTabs");
                tabs.SelectedItem = Named<TabItem>(audit, "HardwareChecksTab");
                ShellTestHost.Drain(window);
                Assert.True(manualNotice.IsVisible);
                var peer = new TextBlockAutomationPeer(manualNotice);
                Assert.Equal(Warning, peer.GetName());
                Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
                Assert.Equal(TextWrapping.Wrap, manualNotice.TextWrapping);
                Assert.Equal(TextTrimming.None, manualNotice.TextTrimming);
                Assert.False(evidenceNotice.IsVisible);
            }
            finally { CloseWindow(window); }
        });
    }

    [Fact]
    public void EnlargedLongHardwareNoticeWrapsInSmallViewport()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace()) { Width = 660, Height = 600 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Inspect);
                var scroll = Named<ScrollViewer>(window, "ShellScroll");
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var message = Named<TextBlock>(audit, "ManualBatterySafety");
                Named<TabControl>(audit, "InspectionTabs").SelectedItem = Named<TabItem>(audit, "HardwareChecksTab");
                message.Text = string.Join(' ', Enumerable.Repeat(Warning, 2));
                message.FontSize = 24;
                Assert.IsType<StackPanel>(scroll.Content).LayoutTransform = new ScaleTransform(2, 2);
                ShellTestHost.Drain(window);

                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
                Assert.True(message.ActualHeight > message.FontSize);
                Assert.Equal(0, scroll.ScrollableWidth);
                message.BringIntoView();
                ShellTestHost.Drain(window);
                Rect bounds = message.TransformToAncestor(scroll).TransformBounds(new Rect(message.RenderSize));
                Assert.True(scroll.VerticalOffset > 0);
                Assert.InRange(bounds.Left, 0, scroll.ActualWidth);
                Assert.InRange(bounds.Right, 0, scroll.ActualWidth);
                Assert.InRange(bounds.Top, 0, scroll.ActualHeight);
            }
            finally { CloseWindow(window); }
        });
    }
}
