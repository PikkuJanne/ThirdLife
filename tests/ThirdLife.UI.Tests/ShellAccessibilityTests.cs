using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ThirdLife.UI.Features.ManualTests;
using ThirdLife.UI.Shell;
using ThirdLife.UI.Themes;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class ShellAccessibilityTests
{
    [Fact]
    public void StageSelectionExposesAllSixDestinationsWithoutStartingAnyJobWork()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var workspace = fixture.CreateWorkspace();
            var window = new MainWindow(workspace);
            try
            {
                ShellTestHost.Show(window);
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var jobs = Named<FrameworkElement>(audit, "JobRegion");
                var create = Named<Button>(audit, "CreateJobButton");
                Assert.True(jobs.IsVisible);
                Assert.Same(create, Keyboard.FocusedElement);
                var stageList = Named<ListBox>(window, "StageList");
                var stageListPeer = new ListBoxAutomationPeer(stageList);
                Assert.NotNull(stageListPeer.GetPattern(PatternInterface.Selection));
                Assert.Equal(6, stageList.Items.Count);
                var stageItems = stageList.Items.Cast<ListBoxItem>().ToArray();
                var navigation = new TextBlockAutomationPeer(Named<TextBlock>(window, "NavigationStatus"));
                Assert.Equal(AutomationLiveSetting.Polite, navigation.GetLiveSetting());

                foreach (ShellStage stage in Enum.GetValues<ShellStage>())
                {
                    var item = Named<ListBoxItem>(window, stage + "Stage");
                    var peer = new ListBoxItemAutomationPeer(item, stageListPeer);
                    Assert.Equal(stage.ToString(), peer.GetName());
                    Assert.Equal("Stage" + stage, peer.GetAutomationId());
                    var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
                    Assert.True(item.Focus());
                    selection.Select();
                    ShellTestHost.Drain(window);
                    Assert.True(selection.IsSelected);
                    Assert.Same(item, Keyboard.FocusedElement);
                    Assert.NotNull(item.FocusVisualStyle);
                    Assert.Equal($"{stage} opened.", navigation.GetName());
                    Assert.Single(stageItems, item => item.IsSelected);
                    Assert.False(jobs.IsVisible);
                    foreach (ShellStage stageSurface in Enum.GetValues<ShellStage>())
                    {
                        Assert.Equal(stageSurface == stage, Named<FrameworkElement>(audit, stageSurface + "Surface").IsVisible);
                    }
                    Assert.Null(workspace.Job);
                    Assert.Null(workspace.Assessment);
                    Assert.False(workspace.HasPlanReview);
                    Assert.False(workspace.IsBusy);
                }

                Invoke(window, Named<Button>(window, "JobsButton"));
                Assert.True(jobs.IsVisible);
                Assert.Null(stageList.SelectedItem);
                Assert.Same(create, Keyboard.FocusedElement);
            }
            finally { CloseWindow(window); }
        });
    }

    [Fact]
    public void LargeTextAndSimulatedDoubleScaleReflowTheMinimumWidthAndPreserveAccess()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace()) { Width = 320, Height = 300 };
            try
            {
                ShellTestHost.Show(window);
                var scroller = Named<ScrollViewer>(window, "ShellScroll");
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var workspace = Named<StackPanel>(window, "WorkspaceColumn");
                Assert.Equal(0, Grid.GetColumn(workspace));
                Assert.Equal(1, Grid.GetRow(workspace));

                window.Width = 660;
                window.Height = 600;
                var content = Assert.IsAssignableFrom<FrameworkElement>(scroller.Content);
                content.LayoutTransform = new ScaleTransform(2, 2);
                var openJob = Named<Button>(audit, "OpenJobButton");
                openJob.Content = "Den gespeicherten Werkstattauftrag anhand des vorhandenen Auftragskennzeichens öffnen";
                openJob.FontSize = 22;

                ShellTestHost.Drain(window);
                Assert.Equal(0, Grid.GetColumn(workspace));
                Assert.Equal(1, Grid.GetRow(workspace));
                Assert.Equal(0, scroller.ScrollableWidth);
                Assert.True(openJob.ActualHeight > 40);
                Assert.True(openJob.Focus());
                openJob.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.True(scroller.VerticalOffset > 0);
                var bounds = openJob.TransformToAncestor(scroller).TransformBounds(new Rect(openJob.RenderSize));
                Assert.InRange(bounds.Left, 0, scroller.ActualWidth);
                Assert.InRange(bounds.Right, 0, scroller.ActualWidth);
                Assert.InRange(bounds.Bottom, 0, scroller.ActualHeight);
                Assert.Same(openJob, Keyboard.FocusedElement);
            }
            finally { CloseWindow(window); }
        });
    }

    [Fact]
    public void AppearanceUpdatesActiveControlsAndIsolatedHighContrastUsesSystemColors()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace());
            try
            {
                ShellTestHost.Show(window);
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var create = Named<Button>(audit, "CreateJobButton");
                var jobCode = Named<TextBox>(audit, "JobIdInput");
                var appearance = Named<ComboBox>(window, "AppearanceChoice");
                jobCode.Text = "retained-test-code";

                Color initialCanvas = SystemParameters.HighContrast ? SystemColors.WindowColor : Color.FromRgb(11, 14, 17);
                Color initialAction = SystemParameters.HighContrast ? SystemColors.HighlightColor : Color.FromRgb(196, 243, 107);
                Assert.Equal(initialCanvas, BrushColor(window.Background));
                Assert.Equal(initialAction, BrushColor(create.Background));

                var appearancePeer = new ComboBoxAutomationPeer(appearance);
                var dropdown = Assert.IsAssignableFrom<IExpandCollapseProvider>(appearancePeer.GetPattern(PatternInterface.ExpandCollapse));
                dropdown.Expand();
                ShellTestHost.Drain(window);
                Assert.True(appearance.IsDropDownOpen);
                dropdown.Collapse();
                appearance.SelectedIndex = 1;
                ShellTestHost.Drain(window);
                Color lightCanvas = SystemParameters.HighContrast ? SystemColors.WindowColor : Color.FromRgb(244, 246, 240);
                Color lightAction = SystemParameters.HighContrast ? SystemColors.HighlightColor : Color.FromRgb(49, 77, 18);
                Assert.Equal(lightCanvas, BrushColor(window.Background));
                Assert.Equal(lightAction, BrushColor(create.Background));
                Assert.Equal("retained-test-code", jobCode.Text);

                // Exercise the real system-color resource bindings without changing the desktop's preference.
                var contrast = new ResourceDictionary
                {
                    Source = new Uri("/ThirdLife.UI;component/Themes/Trace.HighContrast.xaml", UriKind.Relative),
                };
                window.Resources.MergedDictionaries.Add(contrast);
                ShellTestHost.Drain(window);
                Assert.Equal(SystemColors.WindowColor, BrushColor(window.Background));
                Assert.Equal(SystemColors.HighlightColor, BrushColor(create.Background));
                Assert.Equal(SystemColors.HighlightTextColor, BrushColor(create.Foreground));
                Assert.Equal(SystemColors.WindowTextColor, BrushColor(jobCode.BorderBrush));
                window.ShowStage(ShellStage.Verify);
                ShellTestHost.Drain(window);
                var verify = Named<ListBoxItem>(window, "VerifyStage");
                Assert.Equal(SystemColors.HighlightColor, BrushColor(verify.Background));
                Assert.Equal(SystemColors.HighlightTextColor, BrushColor(verify.Foreground));
                Assert.NotNull(verify.FocusVisualStyle);
            }
            finally { CloseWindow(window); }
        });
    }


    [Fact]
    public void NoticeUsesTheOwnersThemeAndReturnsKeyboardFocusOnDismissal()
    {
        using var fixture = new M1AuditFixture();
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(fixture.CreateWorkspace());
            try
            {
                ShellTestHost.Show(window);
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var initiatingControl = Named<Button>(audit, "CreateJobButton");
                Assert.True(initiatingControl.Focus());

                ExceptionDispatchInfo? failure = null;
                TraceNotice? notice = null;
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        notice = Assert.Single(window.OwnedWindows.Cast<Window>().OfType<TraceNotice>());
                        var close = Named<Button>(notice, "DismissButton");
                        Assert.Same(close, Keyboard.FocusedElement);
                        Assert.Equal(BrushColor(window.Background), BrushColor(notice.Background));
                        Assert.Equal("Review required", new TextBlockAutomationPeer(Named<TextBlock>(notice, "Heading")).GetName());
                        Assert.True(close.IsDefault);
                        Assert.True(close.IsCancel);
                        Invoke(notice, close);
                    }
                    catch (Exception error)
                    {
                        failure = ExceptionDispatchInfo.Capture(error);
                        notice?.Close();
                    }
                }));
                TraceNotice.ShowMessage("Review required", "Review the saved job.", window);
                failure?.Throw();
                Assert.NotNull(notice);
                Assert.False(notice.IsVisible);
                ShellTestHost.Drain(window);
                Assert.Same(initiatingControl, Keyboard.FocusedElement);
            }
            finally { CloseWindow(window); }
        });
    }
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
}
