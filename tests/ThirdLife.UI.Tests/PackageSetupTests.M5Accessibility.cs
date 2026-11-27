using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ThirdLife.Actions.Planning;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Fact]
    [Trait("Category", "Extended")]
    public async Task M5KeyboardJourneyPreservesApprovalAndVerificationRecovery()
    {
        using var disk = new AuditTestStore();
        var clock = new FixedClock();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: true);
        var service = new PilotPackageService(runtime);
        var agent = new MaintenanceScanFixture();
        uint boot = 7;
        Task<SqliteJobStore> OpenStore(CancellationToken token) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000UL,
            readBootSession: () => boot, cancellationToken: token);
        AuditWorkspace Open() => OpenMaintenanceWorkspace(disk, clock, service, agent, OpenStore);

        await using var prepare = Open();
        var job = await PrepareInitialPackagePlanAsync(prepare);
        InM5ConstrainedWindow(prepare, (window, audit, scroll) =>
        {
            window.ShowStage(ShellStage.Prepare);
            ShellTestHost.Drain(window);
            var panel = Named<PlanReviewView>(audit, "PlanReviewPanel");
            var maintenance = Named<CheckBox>(panel, "IncludeFullMaintenanceChoice");
            var build = Named<Button>(panel, "BuildPlanReviewButton");
            Assert.True(maintenance.Focus());
            PressM5Space(maintenance);
            NextIs(window, build);
            PressM5Space(build);
            PumpUntil(window, () => prepare.HasPlanReview && !prepare.IsBusy);
            Assert.Contains("final check", prepare.PlanReviewDetails);
            Assert.Equal(AutomationLiveSetting.Polite,
                new TextBlockAutomationPeer(Named<TextBlock>(panel, "PlanStatusMessage")).GetLiveSetting());

            var operatorCode = Named<TextBox>(panel, "PackageOperator");
            var approve = Named<Button>(panel, "ApprovePlanReviewButton");
            Assert.True(operatorCode.Focus());
            operatorCode.Text = "fixture-keyboard-operator";
            ShellTestHost.Drain(window);
            NextIs(window, approve);
            AssertM5ControlInView(window, scroll, approve);
            Assert.Equal("Approve plan", ShellTestHost.ButtonPeer(approve).GetName());
            Assert.Equal(SystemColors.HighlightColor, Assert.IsType<SolidColorBrush>(approve.Background).Color);
            Assert.Equal(SystemColors.HighlightTextColor, Assert.IsType<SolidColorBrush>(approve.Foreground).Color);
            Assert.NotNull(approve.FocusVisualStyle);
            PressM5Space(approve);
            PumpUntil(window, () => prepare.CanContinueMaintenance, () => prepare.PlanReviewStatus);
        });

        Guid approval;
        await using (var apply = Open())
        {
            await apply.OpenJobAsync(job.Value);
            await apply.ContinueMaintenanceAsync();
            apply.SelectedPlanProfile = Assert.Single(apply.VisiblePlanProfiles);
            Assert.True(apply.IncludeFullMaintenance);
            await apply.BuildPlanReviewAsync();
            Assert.True(apply.CanInstallPackage, apply.PlanReviewStatus);
            await apply.InstallPackageAsync();
            await apply.ContinueMaintenanceAsync();
            await apply.ContinueMaintenanceAsync();
            await using var store = await OpenStore(default);
            var saved = Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(job));
            Assert.Equal(MaintenancePhase.FinalRestart, saved.Phase);
            approval = saved.ApprovalId;
        }

        boot++;
        await using var verify = Open();
        await verify.OpenJobAsync(job.Value);
        InM5ConstrainedWindow(verify, (window, audit, scroll) =>
        {
            window.ShowStage(ShellStage.Verify);
            ShellTestHost.Drain(window);
            var panel = Named<PackageVerificationView>(audit, "PackageVerificationPanel");
            var phase = Named<TextBlock>(panel, "MaintenancePauseMessage");
            var advance = Named<Button>(panel, "ContinueMaintenanceButton");
            Assert.True(phase.Focus());
            NextIs(window, advance);
            PressM5Space(advance);
            PumpUntil(window, () => verify.CanRunProfileVerification, () => verify.MaintenancePhaseStatus);
            Assert.Contains("awaiting independent final verification", new TextBlockAutomationPeer(phase).GetName());

            var status = Named<TextBlock>(panel, "ProfileVerificationMessage");
            var statusPeer = new TextBlockAutomationPeer(status);
            var run = Named<Button>(panel, "RunProfileVerificationButton");
            var cancel = Named<Button>(panel, "CancelProfileVerificationButton");
            Assert.Equal(AutomationLiveSetting.Polite, statusPeer.GetLiveSetting());
            Assert.Equal(AutomationControlType.Text, statusPeer.GetAutomationControlType());
            Assert.True(status.Focus());
            NextIs(window, run);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            agent.BeforeResult = () =>
            {
                entered.TrySetResult();
                return release.Task;
            };
            try
            {
                PressM5Space(run);
                PumpUntil(window, () => entered.Task.IsCompleted && verify.CanCancelProfileVerification);
                Assert.Contains("Reading current Windows", statusPeer.GetName());
                Assert.True(status.Focus());
                NextIs(window, cancel);
                AssertM5ControlInView(window, scroll, cancel);
                var cancelPeer = GetM5ControlPeer(window, cancel);
                Assert.Equal("Cancel final profile checks", cancelPeer.GetName());
                Assert.IsAssignableFrom<IInvokeProvider>(cancelPeer.GetPattern(PatternInterface.Invoke));
                PressM5Space(cancel);
                Assert.Contains("Stopping verification", statusPeer.GetName());
            }
            finally
            {
                agent.BeforeResult = null;
                release.TrySetResult();
            }
            PumpUntil(window, () => !verify.IsBusy);
            Assert.False(verify.HasProfileVerificationDetails);
            Assert.Contains("Verification was cancelled", statusPeer.GetName());
            Assert.Same(status, Keyboard.FocusedElement);

            NextIs(window, run);
            PressM5Space(run);
            PumpUntil(window, () => verify.HasProfileVerificationDetails && !verify.IsBusy, () => verify.ProfileVerificationStatus);
            Assert.Same(status, Keyboard.FocusedElement);
            Assert.Contains("an unmet or unresolved requirement", statusPeer.GetName());
            NextIs(window, run);
            var evidence = Named<TextBox>(panel, "ProfileVerificationEvidence");
            NextIs(window, evidence);
            AssertM5ControlInView(window, scroll, evidence);
            var evidencePeer = GetM5ControlPeer(window, evidence);
            Assert.Equal("Final profile verification results", evidencePeer.GetName());
            Assert.Equal(AutomationControlType.Edit, evidencePeer.GetAutomationControlType());
            var value = Assert.IsAssignableFrom<IValueProvider>(evidencePeer.GetPattern(PatternInterface.Value));
            Assert.True(value.IsReadOnly);
            Assert.Contains(PilotPackageService.Identity.ExactVersion, value.Value);

            window.ShowStage(ShellStage.Handover);
            ShellTestHost.Drain(window);
            var generate = Named<Button>(audit, "GenerateReportButton");
            Assert.True(generate.Focus());
            AssertM5ControlInView(window, scroll, generate);
            PressM5Space(generate);
            PumpUntil(window, () => verify.CanOpenReport);
            var open = Named<Button>(audit, "OpenReportButton");
            Assert.Same(open, Keyboard.FocusedElement);
            PressM5Space(open);
            PumpUntil(window, () => verify.ReportPreview.Length != 0);
            var preview = Named<TextBox>(audit, "WorkshopReportPreview");
            Assert.Same(preview, Keyboard.FocusedElement);
            var reportPeer = GetM5ControlPeer(window, preview);
            Assert.Equal("Restricted workshop report preview", reportPeer.GetName());
            Assert.True(Assert.IsAssignableFrom<IValueProvider>(reportPeer.GetPattern(PatternInterface.Value)).IsReadOnly);
            Assert.Contains(PilotPackageService.Identity.ExactVersion, preview.Text);
        });

        await using var reopened = Open();
        await reopened.OpenJobAsync(job.Value);
        Assert.Contains(PilotPackageService.Identity.ExactVersion, reopened.ProfileVerificationDetails);
        await using var finalStore = await OpenStore(default);
        var result = Assert.IsType<SavedProfileVerification>(await finalStore.LoadProfileVerificationAsync(job));
        Assert.True(result.IsCurrent);
        Assert.Equal(approval, result.Record.Checkpoint.ApprovalId);
        Assert.Equal(1, runtime.InstallCalls);
    }

    private static void InM5ConstrainedWindow(AuditWorkspace workspace, Action<MainWindow, FrameworkElement, ScrollViewer> check) =>
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/ThirdLife.UI;component/Themes/Trace.HighContrast.xaml", UriKind.Relative),
                });
                var audit = Named<FrameworkElement>(window, "AuditView");
                audit.LayoutTransform = new ScaleTransform(2, 2);
                ShellTestHost.Show(window);
                var scroll = Named<ScrollViewer>(window, "ShellScroll");
                Assert.Equal(0, Grid.GetColumn(Named<StackPanel>(window, "WorkspaceColumn")));
                check(window, audit, scroll);
            }
            finally { CloseWindow(window); }
        }, TimeSpan.FromSeconds(40));

    private static void AssertM5ControlInView(Window window, ScrollViewer scroll, FrameworkElement control)
    {
        control.BringIntoView();
        ShellTestHost.Drain(window);
        Assert.True(scroll.VerticalOffset > 0);
        Assert.Equal(0, scroll.ScrollableWidth);
        var bounds = control.TransformToAncestor(scroll).TransformBounds(new Rect(control.RenderSize));
        const double RoundingTolerance = 0.000001;
        Assert.InRange(bounds.Left, -RoundingTolerance, scroll.ActualWidth);
        Assert.InRange(bounds.Right, 0, scroll.ActualWidth + RoundingTolerance);
        var visibleHeight = Math.Min(bounds.Bottom, scroll.ActualHeight) - Math.Max(bounds.Top, 0);
        Assert.InRange(visibleHeight, Math.Min(bounds.Height, scroll.ActualHeight) - RoundingTolerance, scroll.ActualHeight);
    }

    private static AutomationPeer GetM5ControlPeer(MainWindow window, FrameworkElement control)
    {
        var peers = new Queue<AutomationPeer>();
        peers.Enqueue(Assert.IsAssignableFrom<WindowAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(window)));
        while (peers.TryDequeue(out var peer))
        {
            if (peer.GetAutomationId() == control.Name) { return peer; }
            foreach (var child in peer.GetChildren() ?? []) { peers.Enqueue(child); }
        }
        throw new InvalidOperationException($"The loaded window does not expose {control.Name} to UI Automation.");
    }

    private static void PressM5Space(UIElement control)
    {
        Assert.True(control.IsKeyboardFocused);
        var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(control));
        control.Dispatcher.Invoke(() =>
        {
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
            {
                RoutedEvent = Keyboard.KeyUpEvent,
            });
        }, DispatcherPriority.Normal);
    }
}
