using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditAccessibilityTests
{
    [Fact]
    public void AuditControlsExposeKeyboardOrderGateStateAndLiveResultsInTheLoadedWindow()
    {
        using var disk = new AuditTestStore();
        var provider = AuditTestProvider.Available("keyboard");
        var workspace = disk.OpenWorkspace([provider]);
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                var view = Named<FrameworkElement>(window, "AuditView");
                Assert.True(view.IsVisible);
                var create = Named<Button>(view, "CreateJobButton");
                Assert.Same(create, Keyboard.FocusedElement);
                var jobIdInput = Named<TextBox>(view, "JobIdInput");
                var open = Named<Button>(view, "OpenJobButton");
                Assert.True(create.Focus());
                Assert.Equal("Create job", ShellTestHost.ButtonPeer(create).GetName());
                NextIs(window, jobIdInput);
                Assert.Equal("Job identifier to open", AutomationProperties.GetName(jobIdInput));
                jobIdInput.Text = "/";
                NextIs(window, open);
                Invoke(window, open);
                PumpUntil(window, () => workspace.OperationError.Length != 0);
                Assert.Equal("/", jobIdInput.Text);
                var error = Named<TextBlock>(view, "OperationErrorMessage");
                Assert.True(error.IsVisible);
                Assert.Equal(AutomationLiveSetting.Polite, new TextBlockAutomationPeer(error).GetLiveSetting());

                var start = Named<Button>(view, "StartAssessmentButton");
                Assert.False(ShellTestHost.ButtonPeer(start).IsEnabled());
                Assert.Throws<ElementNotEnabledException>(() => Invoke(window, start));

                Invoke(window, create);
                PumpUntil(window, () => workspace.Job is not null && workspace.CanEditJob);
                var status = Named<TextBlock>(view, "AuditStatus");
                var statusPeer = new TextBlockAutomationPeer(status);
                Assert.Equal(AutomationLiveSetting.Polite, statusPeer.GetLiveSetting());
                Assert.NotEmpty(statusPeer.GetName());
                Assert.False(start.IsEnabled);

                var operatorCode = Named<TextBox>(view, "OperatorIdInput");
                operatorCode.Text = "fixture-operator";
                Assert.True(operatorCode.Focus());
                var sanitization = Named<ComboBox>(view, "SanitizationResult");
                NextIs(window, sanitization);
                Assert.Equal("External sanitization result", AutomationProperties.GetName(sanitization));
                sanitization.SelectedValue = SanitizationState.Verified;
                Named<ComboBox>(view, "SanitizationMethod").SelectedValue = "clear";
                Named<DatePicker>(view, "SanitizationDate").SelectedDate = new DateTime(2026, 10, 1);
                Named<TextBox>(view, "SanitizationMediaId").Text = "fixture-media";
                Named<CheckBox>(view, "ExternalRecordConfirmed").IsChecked = true;
                var saveIntake = Named<Button>(view, "RecordSanitizationButton");
                Assert.True(saveIntake.Focus());
                Invoke(window, saveIntake);
                PumpUntil(window, () => workspace.CanAssess);
                Assert.True(start.IsEnabled);
                Assert.True(start.Focus());
                Assert.Equal("Start assessment", ShellTestHost.ButtonPeer(start).GetName());
                Invoke(window, start);
                PumpUntil(window, () => workspace.Assessment is not null && workspace.CanEditJob);
                var assessment = new TextBlockAutomationPeer(Named<TextBlock>(view, "AssessmentMessage"));
                Assert.Equal(AutomationLiveSetting.Polite, assessment.GetLiveSetting());
                Assert.Contains("Assessment complete", assessment.GetName(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, provider.InvocationCount);

                Named<TabItem>(view, "HardwareChecksTab").IsSelected = true;
                ShellTestHost.Drain(window);
                var manualCheck = Named<ComboBox>(view, "ManualCheck");
                manualCheck.SelectedValue = "MHT-002";
                Assert.True(manualCheck.Focus());
                var manualOutcome = Named<ComboBox>(view, "ManualOutcome");
                Assert.False(Named<ComboBoxItem>(view, "ManualPassOption").IsEnabled);
                Assert.False(Named<ComboBoxItem>(view, "ManualFailOption").IsEnabled);
                MoveNext(window);
                Assert.IsType<ToggleButton>(Keyboard.FocusedElement);
                NextIs(window, Named<TextBox>(view, "ManualOperatorCode"));
                NextIs(window, manualOutcome);
                manualOutcome.SelectedValue = HumanTestResult.NotAvailable;
                var reason = Named<ComboBox>(view, "ManualUnavailableReason");
                NextIs(window, reason);
                Assert.Equal("Reason the manual check is unavailable", AutomationProperties.GetName(reason));
                reason.SelectedValue = ManualTestUnavailableReason.EquipmentMissing;
                Assert.Equal(ManualTestUnavailableReason.EquipmentMissing, reason.SelectedValue);
                var notes = Named<TextBox>(view, "ManualNotes");
                NextIs(window, notes);
                notes.Text = "Fixture equipment unavailable";
                var save = Named<Button>(view, "SaveManualResultButton");
                NextIs(window, save);
                Invoke(window, save);
                PumpUntil(window, () => workspace.Job?.HumanTests.Count == 1 && workspace.CanEditJob,
                    () => Named<TextBlock>(view, "ManualInputError").Text);
                Assert.Equal(HumanTestResult.NotAvailable, Assert.Single(workspace.Job!.HumanTests).TestResult);

                manualCheck.SelectedValue = ManualTestCatalog.RemoteAccessReview;
                Assert.Equal("Manual hardware or security check", new ComboBoxAutomationPeer(manualCheck).GetName());
                Assert.False(notes.IsVisible);
                Assert.True(Named<TextBlock>(view, "SecurityReviewScope").IsVisible);
                manualOutcome.SelectedValue = HumanTestResult.NotAvailable;
                reason.SelectedValue = ManualTestUnavailableReason.PermissionDenied;
                Assert.True(reason.Focus());
                NextIs(window, save);
                Invoke(window, save);
                PumpUntil(window, () => workspace.Job?.HumanTests.Count == 2 && workspace.CanEditJob);
                var review = workspace.Job!.HumanTests[1];
                Assert.Equal(ManualTestCatalog.SecurityReviewRevision, review.Details!.ProcedureRevision);
                Assert.Equal(workspace.Assessment!.AssessmentId.Value, review.Details.AssessmentId);
                Assert.Equal(string.Empty, review.Details.Notes);
                Assert.Equal("permission_denied", review.LimitationCode);
                Assert.Equal(ProvenanceKind.SyntheticFixture, review.Metadata.Provenance.Kind);

                manualOutcome.SelectedValue = HumanTestResult.NotRun;
                Invoke(window, start);
                PumpUntil(window, () => workspace.SecurityReviewAssessmentId is { } current &&
                    current != review.Details.AssessmentId && workspace.CanEditJob);
                Assert.Null(manualOutcome.SelectedValue);
                Assert.Empty(Assert.Single(workspace.ManualProgress!.Items, item =>
                    item.Case.Id == ManualTestCatalog.RemoteAccessReview).History);

                ShellTestHost.Invoke(window, "JobsButton");
                Assert.Same(create, Keyboard.FocusedElement);
                Assert.NotNull(workspace.Job);
            }
            finally { CloseWindow(window); }
        });
    }

    [Fact]
    public async Task CompletingAnInterruptedSanitizationGateKeepsFocusInTheAuditRoute()
    {
        using var fixture = new M1AuditFixture();
        await using var workspace = fixture.CreateWorkspace();
        await workspace.CreateJobAsync();
        var job = Assert.IsType<StoredJob>(workspace.Job).Job;
        var eventDate = new DateTimeOffset(M1AuditFixture.FixtureDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var metadata = new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
            EvidenceClassification.Observed, new ProviderId("workshop-intake"), eventDate,
            new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "m1-intake"), ValueAvailability.Available);
        var record = new SanitizationEvidence(metadata, SanitizationState.Verified, "clear",
            new OperatorId(M1AuditFixture.OperatorCode), eventDate, new MediaIdentifier(M1AuditFixture.MediaCode),
            SanitizationVerificationState.Verified, "m1-intake-v1");

        await using (var store = await fixture.OpenSqliteAsync())
        {
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [record]));
        }

        await workspace.OpenJobAsync(job.JobId.Value);
        Assert.True(workspace.CanCompleteSavedSanitization, workspace.GateStatus);
        Assert.False(workspace.CanAssess);

        ShellTestHost.Run(() =>
        {
            var window = fixture.CreateWindow(workspace);
            try
            {
                ShellTestHost.Show(window);
                var view = Named<FrameworkElement>(window, "AuditView");
                window.ShowStage(ShellStage.Inspect);
                ShellTestHost.Drain(window);
                Invoke(window, Named<Button>(view, "GoToSanitizationButton"));
                var complete = Named<Button>(view, "CompleteSavedSanitizationButton");
                Assert.Same(complete, Keyboard.FocusedElement);
                Invoke(window, complete);
                PumpUntil(window, () => workspace.CanAssess, () => workspace.Status);
                Assert.Same(Named<Button>(view, "StartAssessmentButton"), Keyboard.FocusedElement);
                Assert.Null(workspace.Assessment);
            }
            finally { CloseWindow(window); }
        });
    }

    [Fact]
    public async Task CancelIsKeyboardReachableAfterNavigatingAwayFromARunningAssessment()
    {
        using var disk = new AuditTestStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new AuditTestProvider("cancel-keyboard", token =>
        {
            entered.TrySetResult();
            return WaitForCancellationAsync(token);
        });
        var workspace = disk.OpenWorkspace([provider]);
        try
        {
            await workspace.CreateJobAsync();
            await workspace.RecordSanitizationAsync(VerifiedSample());
            ShellTestHost.Run(() =>
            {
                var window = new MainWindow(workspace) { Width = 640, Height = 420 };
                try
                {
                    ShellTestHost.Show(window);
                    var view = Named<FrameworkElement>(window, "AuditView");
                    window.ShowStage(ShellStage.Inspect);
                    ShellTestHost.Drain(window);
                    var start = Named<Button>(view, "StartAssessmentButton");
                    Invoke(window, start);
                    PumpUntil(window, () => entered.Task.IsCompleted && workspace.CanCancel);

                    window.ShowStage(ShellStage.Handover);
                    var cancel = Named<Button>(view, "CancelAssessmentButton");
                    Assert.False(cancel.IsVisible);
                    Assert.True(workspace.IsAssessmentRunning);
                    window.ShowStage(ShellStage.Inspect);
                    ShellTestHost.Drain(window);
                    Assert.True(cancel.IsVisible);
                    Assert.True(cancel.Focus());
                    var cancelPeer = ShellTestHost.ButtonPeer(cancel);
                    Assert.Equal("Cancel assessment", cancelPeer.GetName());
                    Assert.True(cancelPeer.IsKeyboardFocusable());
                    Invoke(window, cancel);

                    PumpUntil(window, () => workspace.Assessment?.Outcome == AssessmentOutcome.Cancelled && !workspace.IsBusy);
                    Assert.False(cancel.IsEnabled);
                    Assert.Contains("cancelled", new TextBlockAutomationPeer(Named<TextBlock>(view, "AssessmentMessage")).GetName(),
                        StringComparison.OrdinalIgnoreCase);
                }
                finally { CloseWindow(window); }
            });
        }
        finally { await workspace.CloseAsync(); }
    }

    internal static T Named<T>(FrameworkElement element, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(element.FindName(name));

    internal static void CloseWindow(Window window)
    {
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        PumpUntil(window, () => closed);
    }

    internal static void Invoke(Window window, Button button)
    {
        var peer = ShellTestHost.ButtonPeer(button);
        Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke)).Invoke();
        ShellTestHost.Drain(window);
    }

    internal static void NextIs(Window window, FrameworkElement expected)
    {
        MoveNext(window);
        Assert.Same(expected, Keyboard.FocusedElement);
    }

    private static void MoveNext(Window window)
    {
        var focused = Assert.IsAssignableFrom<FrameworkElement>(Keyboard.FocusedElement);
        Assert.True(focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
        ShellTestHost.Drain(window);
    }

    internal static void PumpUntil(Window window, Func<bool> condition, Func<string>? detail = null,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        if (!condition())
        {
            var frame = new DispatcherFrame();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                if (condition() || deadline.Elapsed > limit) { frame.Continue = false; }
            };
            try
            {
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            finally { timer.Stop(); }
        }

        Assert.True(condition(), $"The audit window did not reach the expected state. {detail?.Invoke()}");
        ShellTestHost.Drain(window);
    }
}
