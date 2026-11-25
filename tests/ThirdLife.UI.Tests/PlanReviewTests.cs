using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.UI.Features.Execution;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed partial class PlanReviewTests
{
    private static readonly DateTimeOffset EvaluationTime = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly PolicySnapshot ReadyPolicy = PolicySnapshot.Parse("""
        {
          "schema_version": "thirdlife.policy.v2",
          "policy_id": "community-laptop-policy",
          "version": "0.1.0-candidate.2",
          "status": "candidate",
          "synthetic_data": true,
          "classification": "PUBLIC_REFERENCE",
          "claim_scope": "plan_review_test",
          "effective": {
            "status": "candidate_not_effective",
            "effective_from_utc": null,
            "effective_until_utc": null
          },
          "target": {
            "operating_system": "windows_11",
            "support_state": "supported",
            "architecture": "x64",
            "device_class": "laptop"
          },
          "rules": [{
            "id": "POL-SAN-001",
            "requirement_type": "blocking",
            "severity": "critical",
            "evidence_key": "sanitization.state",
            "condition": { "type": "equals", "value": "verified" },
            "decision": {
              "blocks_ready": true,
              "unmet_disposition": "human_review_required",
              "unavailable_disposition": "human_review_required"
            }
          }]
        }
        """u8.ToArray());

    [Fact]
    public async Task ProfileSelectionAndPreviewLeaveTheSavedJobUntouchedAndApprovalBindsTheDisplayedHash()
    {
        using var disk = new AuditTestStore();
        var provider = new PlanFactsProvider();
        await using var workspace = OpenWorkspace(disk, provider);
        await PrepareAssessmentAsync(workspace);
        var initialJob = workspace.Job!;
        var initialAssessment = workspace.Assessment!;

        InPlanReview(workspace, (window, panel) =>
        {
            var approve = Named<Button>(panel, "ApprovePlanReviewButton");
            Assert.False(approve.IsEnabled);
            SelectProfile(window, panel, workspace, "job-seeker");
            Assert.Null(workspace.PlanReview);
            Assert.Null(workspace.PlanReviewDecision);
            Assert.False(approve.IsEnabled);
            AssertUnchangedJob();

            Build(window, panel, workspace);
            var shown = workspace.PlanReview!;
            Assert.Null(shown.Plan.BlockedReason);
            Assert.Equal(4, shown.Plan.Actions.Length);
            Assert.Equal(shown.Plan.ContentSha256, shown.Impact.PlanSha256);
            Assert.All(shown.Plan.Actions, action => Assert.IsType<NoOpActionRequest>(action.Request));
            Assert.Null(workspace.PlanReviewDecision);
            Assert.True(approve.IsEnabled);
            AssertUnchangedJob();

            Invoke(window, approve);
            PumpUntil(window, () => workspace.PlanReviewDecision is not null && !workspace.IsBusy,
                () => workspace.PlanReviewStatus);
            var decision = workspace.PlanReviewDecision!;
            Assert.Equal(PlanReviewOutcome.Approved, decision.Outcome);
            Assert.Equal(shown.Plan.ContentSha256, decision.PlanSha256);
            Assert.Same(shown, decision.Snapshot);
            Assert.Equal(EvaluationTime, decision.DecidedAtUtc);
            AssertUnchangedJob();
        });

        void AssertUnchangedJob()
        {
            Assert.Equal(initialJob.Job.JobId, workspace.Job!.Job.JobId);
            Assert.Equal(initialJob.Checkpoints, workspace.Job.Checkpoints);
            Assert.Equal(initialJob.Observations, workspace.Job.Observations);
            Assert.Equal(initialAssessment.AssessmentId, workspace.Assessment!.AssessmentId);
            Assert.Equal(1, provider.InvocationCount);
        }
    }

    [Fact]
    public async Task RebuildingRetainsAnActiveApprovalAndAChangedSelectionRequiresASecondDecision()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenWorkspace(disk, new PlanFactsProvider());
        await PrepareAssessmentAsync(workspace);

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "basic");
            Build(window, panel, workspace);
            var original = Approve(window, panel, workspace);

            Build(window, panel, workspace);
            Assert.Same(original, workspace.PlanReviewDecision);
            Assert.Same(original, Assert.Single(workspace.PlanReviewHistory));

            var optional = Assert.Single(workspace.PlanCapabilities, choice => !choice.IsEssential);
            Toggle(window, panel, optional);
            Assert.Null(workspace.PlanReviewDecision);
            Build(window, panel, workspace);
            Assert.Null(workspace.PlanReviewDecision);
            Assert.True(workspace.CanApprovePlanReview);
            Assert.Contains("The plan changed", workspace.PlanReviewStatus, StringComparison.Ordinal);
            var details = Named<TextBox>(panel, "PlanDetailsText");
            Assert.Contains("Plan changes", details.Text, StringComparison.Ordinal);
            Assert.Contains(original.PlanSha256, details.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(workspace.PlanReview!.Plan.Actions, action => action.CapabilityId == optional.CapabilityId);

            var updated = Approve(window, panel, workspace);
            Assert.NotEqual(original.PlanSha256, updated.PlanSha256);
            Assert.Equal([original, updated], workspace.PlanReviewHistory);
            Assert.Contains(updated.PlanSha256, details.Text, StringComparison.Ordinal);

            Toggle(window, panel, optional);
            Build(window, panel, workspace);
            Assert.Equal(original.PlanSha256, workspace.PlanReview!.Plan.ContentSha256);
            Assert.Null(workspace.PlanReviewDecision);
            Assert.True(workspace.CanApprovePlanReview);
            Assert.Equal([original, updated], workspace.PlanReviewHistory);

            workspace.CloseJob();
            Assert.Empty(workspace.PlanReviewHistory);
        });
    }

    [Fact]
    public async Task ApprovalClickShowsAChangedPolicySnapshotAndRequiresASecondExplicitClick()
    {
        using var disk = new AuditTestStore();
        var policy = ReadyPolicy;
        await using var workspace = new AuditWorkspace(
            async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [new PlanFactsProvider()], new FixtureClock(), fixturePlanPolicy: _ => policy);
        await PrepareAssessmentAsync(workspace);

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "job-seeker");
            Build(window, panel, workspace);
            var displayed = workspace.PlanReview!;
            byte[] changedPolicy = [.. ReadyPolicy.Utf8Json, (byte)' '];
            policy = PolicySnapshot.Parse(changedPolicy);

            var approve = Named<Button>(panel, "ApprovePlanReviewButton");
            Invoke(window, approve);
            PumpUntil(window, () => !workspace.IsBusy && workspace.PlanReview is { } current &&
                current.Plan.ContentSha256 != displayed.Plan.ContentSha256, () => workspace.PlanReviewStatus);
            Assert.Null(workspace.PlanReviewDecision);
            Assert.Empty(workspace.PlanReviewHistory);
            Assert.True(approve.IsEnabled);
            Assert.Contains("The plan changed", workspace.PlanReviewStatus, StringComparison.Ordinal);
            var details = Named<TextBox>(panel, "PlanDetailsText");
            Assert.Contains("Plan changes", details.Text, StringComparison.Ordinal);
            Assert.Contains("policy", details.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(policy.Document.ContentSha256, workspace.PlanReview!.Plan.PolicySha256);

            var decision = Approve(window, panel, workspace);
            Assert.NotEqual(displayed.Plan.ContentSha256, decision.PlanSha256);
            Assert.Same(decision, Assert.Single(workspace.PlanReviewHistory));
        });
    }

    [Fact]
    public async Task CapabilityTogglesInvalidateReviewAndSkippingAnEssentialDisablesApproval()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenWorkspace(disk, new PlanFactsProvider());
        await PrepareAssessmentAsync(workspace);

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "basic");
            var video = Assert.Single(workspace.PlanCapabilities, item => item.CapabilityId == "video_calling");
            if (!video.IsIncluded)
            {
                Toggle(window, panel, video);
            }

            Build(window, panel, workspace);
            Assert.Contains(workspace.PlanReview!.Plan.Actions, item => item.CapabilityId == "video_calling");
            var firstHash = workspace.PlanReview.Plan.ContentSha256;
            Toggle(window, panel, video);
            Assert.False(video.IsIncluded);
            Assert.False(workspace.HasPlanReview);
            Assert.False(workspace.CanApprovePlanReview);
            Build(window, panel, workspace);
            Assert.DoesNotContain(workspace.PlanReview!.Plan.Actions, item => item.CapabilityId == "video_calling");
            Assert.NotEqual(firstHash, workspace.PlanReview.Plan.ContentSha256);
            Assert.True(workspace.CanApprovePlanReview);

            var browser = Assert.Single(workspace.PlanCapabilities, item => item.CapabilityId == "web_browsing");
            Assert.True(browser.IsIncluded);
            Toggle(window, panel, browser);
            Assert.False(browser.IsIncluded);
            Assert.False(workspace.CanApprovePlanReview);
            Assert.Contains("essential", Named<TextBlock>(panel, "PlanStatusMessage").Text, StringComparison.OrdinalIgnoreCase);
            Toggle(window, panel, browser);
            Build(window, panel, workspace);
            Assert.True(workspace.CanApprovePlanReview);
        });
    }

    [Fact]
    public async Task DeclineRecordsTheDisplayedProposalWithoutApproval()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenWorkspace(disk, new PlanFactsProvider());
        await PrepareAssessmentAsync(workspace);

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "basic");
            Build(window, panel, workspace);
            var shown = workspace.PlanReview!;
            var decline = Named<Button>(panel, "DeclinePlanReviewButton");
            Assert.True(workspace.CanDeclinePlanReview);
            Invoke(window, decline);
            PumpUntil(window, () => workspace.PlanReviewDecision is not null && !workspace.IsBusy,
                () => workspace.PlanReviewStatus);
            var decision = workspace.PlanReviewDecision!;
            Assert.Equal(PlanReviewOutcome.Declined, decision.Outcome);
            Assert.Equal(shown.Plan.ContentSha256, decision.PlanSha256);
            Assert.Same(shown, decision.Snapshot);
            Assert.Equal(EvaluationTime, decision.DecidedAtUtc);
        });
    }

    [Fact]
    public async Task OrdinaryCandidatePolicyFailureStaysVisibleAndCannotBeApproved()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenWorkspace(disk, new PlanFactsProvider(), useReadyPolicy: false);
        await PrepareAssessmentAsync(workspace);

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "basic");
            Build(window, panel, workspace);
            var review = workspace.PlanReview!;
            Assert.Equal(PlanBlockReason.PolicyBlocksPreparation, review.Plan.BlockedReason);
            Assert.Empty(review.Plan.Actions);
            Assert.Null(workspace.PlanReviewDecision);
            var approve = Named<Button>(panel, "ApprovePlanReviewButton");
            Assert.False(ShellTestHost.ButtonPeer(approve).IsEnabled());
            Assert.Throws<ElementNotEnabledException>(() => Invoke(window, approve));
            Assert.Equal("Preparation blocked", workspace.PlanReviewTitle);
            Assert.Contains("Human review required", workspace.PlanReviewDetails, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AtDoubleScaleDetailsAndDecisionControlsStayReachableByKeyboard()
    {
        using var disk = new AuditTestStore();
        await using var workspace = OpenWorkspace(disk, new PlanFactsProvider());
        await PrepareAssessmentAsync(workspace);

        InPlanReview(workspace, (window, panel) =>
        {
            panel.LayoutTransform = new ScaleTransform(2, 2);
            ShellTestHost.Drain(window);
            var selector = Named<ComboBox>(panel, "PlanProfileSelector");
            Assert.Equal("Preparation profile", new ComboBoxAutomationPeer(selector).GetName());
            Assert.True(selector.Focus());
            var status = Named<TextBlock>(panel, "PlanStatusMessage");
            Assert.Equal(AutomationLiveSetting.Polite, new TextBlockAutomationPeer(status).GetLiveSetting());
            SelectProfile(window, panel, workspace, "basic");
            Build(window, panel, workspace);

            var expander = Named<Expander>(panel, "PlanDetailsExpander");
            var details = Named<TextBox>(panel, "PlanDetailsText");
            Assert.True(expander.IsExpanded);
            Assert.True(details.IsVisible);
            var title = Named<TextBlock>(panel, "PlanResultTitle");
            Assert.True(title.Focus());
            Assert.True(title.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            ShellTestHost.Drain(window);
            var header = Assert.IsType<ToggleButton>(Keyboard.FocusedElement);
            Assert.True(expander.IsKeyboardFocusWithin);
            SendKey(header, Keyboard.KeyDownEvent, Key.Space);
            SendKey(header, Keyboard.KeyUpEvent, Key.Space);
            PumpUntil(window, () => !expander.IsExpanded);
            SendKey(header, Keyboard.KeyDownEvent, Key.Space);
            SendKey(header, Keyboard.KeyUpEvent, Key.Space);
            PumpUntil(window, () => expander.IsExpanded && details.IsKeyboardFocused);
            Assert.Equal("Plan details and impact", new TextBoxAutomationPeer(details).GetName());
            Assert.True(details.IsReadOnly);
            Assert.Equal(TextWrapping.Wrap, details.TextWrapping);
            Assert.Equal(workspace.PlanReviewDetails, details.Text);
            Assert.Contains("synthetic-fixture-source", details.Text, StringComparison.Ordinal);
            Assert.Contains("synthetic-fixture-publisher", details.Text, StringComparison.Ordinal);
            Assert.Contains("0.0.0-fixture.1", details.Text, StringComparison.Ordinal);
            Assert.Contains("machine", details.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(workspace.PlanReview!.Plan.ContentSha256, details.Text, StringComparison.Ordinal);
            SendKey(details, Keyboard.PreviewKeyDownEvent, Key.Escape);
            ShellTestHost.Drain(window);
            Assert.False(expander.IsExpanded);
            Assert.Same(header, Keyboard.FocusedElement);

            var approve = Named<Button>(panel, "ApprovePlanReviewButton");
            var decline = Named<Button>(panel, "DeclinePlanReviewButton");
            NextIs(window, approve);
            NextIs(window, decline);
            decline.BringIntoView();
            ShellTestHost.Drain(window);
            var scroll = ShellTestHost.Element<ScrollViewer>(window, "ShellScroll");
            Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
            Assert.Equal(0, scroll.ScrollableWidth);
            var bounds = decline.TransformToAncestor(scroll).TransformBounds(new Rect(decline.RenderSize));
            Assert.InRange(bounds.Left, 0, scroll.ActualWidth);
            Assert.InRange(bounds.Right, 0, scroll.ActualWidth);
            Assert.InRange(bounds.Bottom, 0, scroll.ActualHeight);
        });
    }

    private static void InPlanReview(AuditWorkspace workspace, Action<MainWindow, PlanReviewView> check) =>
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(new ShellViewModel(), workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                ShellTestHost.Invoke(window, "PrepareButton");
                var audit = Named<FrameworkElement>(window, "AuditView");
                var panel = Named<PlanReviewView>(audit, "PlanReviewPanel");
                Assert.True(panel.IsVisible);
                Assert.Same(Named<GroupBox>(panel, "PlanReviewRegion"), Keyboard.FocusedElement);
                check(window, panel);
            }
            finally { CloseWindow(window); }
        });

    [Fact]
    public async Task DurableNoOpApprovalStaysVisibleWhenBrokerIsUnavailable()
    {
        using var disk = new AuditTestStore();
        var clock = new FixtureClock();
        await using var workspace = OpenBrokerWorkspace(disk, clock);
        await PrepareAssessmentAsync(workspace);
        var jobId = workspace.JobId;
        string? planHash = null;

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "basic");
            Build(window, panel, workspace);
            var operatorCode = Named<TextBox>(panel, "BrokerOperator");
            Assert.True(operatorCode.Focusable && operatorCode.IsTabStop);
            Assert.False(Named<Button>(panel, "ApprovePlanReviewButton").IsEnabled);
            operatorCode.Text = "fixture-operator";
            ShellTestHost.Drain(window);
            planHash = Approve(window, panel, workspace).PlanSha256;

            var execution = Named<BrokerExecutionView>(panel, "BrokerExecutionPanel");
            var status = Named<TextBlock>(execution, "ExecutionStatus");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Contains("unavailable", status.Text, StringComparison.Ordinal);
            Assert.False(Named<Button>(execution, "RunBrokerButton").IsEnabled);
            Assert.False(Named<Button>(execution, "CancelBrokerButton").IsEnabled);
            var saved = Named<TextBox>(execution, "SavedBrokerHistory");
            Assert.True(saved.IsReadOnly && saved.IsTabStop);
            Assert.Contains("No action started", saved.Text, StringComparison.Ordinal);
        });

        await using var reopened = OpenBrokerWorkspace(disk, clock);
        await reopened.OpenJobAsync(jobId);
        Assert.True(reopened.HasBrokerHistory);
        Assert.Contains(planHash!, reopened.BrokerHistoryText, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-operator", reopened.BrokerHistoryText, StringComparison.Ordinal);
        await reopened.RunBrokerExecutionAsync();
        await using var store = await disk.OpenSqliteAsync();
        var history = Assert.Single(await store.LoadJobJournalsAsync(reopened.Job!.Job.JobId));
        Assert.Equal(planHash, Assert.Single(history.Approvals).Plan.PlanSha256);
        Assert.All(history.Entries, entry => Assert.Equal(JournalCheckpoint.BrokerApproval, entry.Checkpoint));
    }

    [Fact]
    public async Task DeclinedConsentCanBeReapprovedAndCancelledFromTheNoOpView()
    {
        using var disk = new AuditTestStore();
        var clock = new FixtureClock();
        var backend = new FixtureBrokerBackend(async (call, batch, authorize, progress, token) =>
        {
            if (call == 1)
            {
                return new NoOpBrokerBatchResult(NoOpBrokerBatchStatus.Declined, []);
            }

            Assert.True(await authorize(token));
            var first = batch.Actions[0];
            progress!.Report(new NoOpExecutionProgress(batch.CorrelationId, first.ActionId, NoOpProgressStage.Running));
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return new NoOpBrokerBatchResult(NoOpBrokerBatchStatus.Finished, batch.Actions.Select(action =>
                new NoOpBrokerActionResult(batch.CorrelationId, action.ActionId, NoOpBrokerActionStatus.Cancelled)).ToImmutableArray());
        });
        await using var workspace = OpenBrokerWorkspace(disk, clock, backend);
        await PrepareAssessmentAsync(workspace);
        var jobId = workspace.Job!.Job.JobId;

        InPlanReview(workspace, (window, panel) =>
        {
            SelectProfile(window, panel, workspace, "basic");
            Build(window, panel, workspace);
            var execution = Named<BrokerExecutionView>(panel, "BrokerExecutionPanel");
            var run = Named<Button>(execution, "RunBrokerButton");
            var status = Named<TextBlock>(execution, "ExecutionStatus");
            Assert.False(run.IsEnabled);
            workspace.BrokerOperatorCode = "fixture-operator";
            var original = Approve(window, panel, workspace);
            Assert.True(run.Focus());
            Invoke(window, run);
            PumpUntil(window, () => !workspace.IsBusy && workspace.BrokerHistoryText.Contains("consent declined", StringComparison.Ordinal),
                () => workspace.BrokerExecutionStatus);
            Assert.Contains("No action was sent", workspace.BrokerExecutionStatus, StringComparison.Ordinal);
            Assert.False(run.IsEnabled);
            Assert.Same(status, Keyboard.FocusedElement);

            var retry = Approve(window, panel, workspace);
            Assert.Equal(original.PlanSha256, retry.PlanSha256);
            Invoke(window, run);
            var cancel = Named<Button>(execution, "CancelBrokerButton");
            PumpUntil(window, () => cancel.IsEnabled && status.Text.Contains("No-op in progress", StringComparison.Ordinal),
                () => workspace.BrokerExecutionStatus);
            Assert.Equal("Cancel no-op execution", ShellTestHost.ButtonPeer(cancel).GetName());
            Invoke(window, cancel);
            PumpUntil(window, () => !workspace.IsBusy, () => workspace.BrokerExecutionStatus);
            Assert.False(cancel.IsEnabled);
            Assert.False(run.IsEnabled);
            Assert.Contains("needs review", workspace.BrokerHistoryText, StringComparison.Ordinal);
        });

        await using var store = await disk.OpenSqliteAsync();
        var history = Assert.Single(await store.LoadJobJournalsAsync(jobId));
        Assert.Equal(2, backend.Calls);
        Assert.Equal(2, history.Approvals.Length);
        Assert.All(history.Plan.Actions, action => Assert.Equal(JournalState.RequiresReview, history.StateOf(action.ActionId)));
    }

    [Fact]
    public async Task ReopenedNoOpReviewReconcilesAnExpiredDispatchWithoutRelaunching()
    {
        using var disk = new AuditTestStore();
        var clock = new FixtureClock();
        await using var original = OpenBrokerWorkspace(disk, clock);
        await PrepareAssessmentAsync(original);
        var jobId = original.Job!.Job.JobId;
        original.SelectedPlanProfile = Assert.Single(original.PlanProfiles, option => option.Profile.ProfileId == "basic");
        await original.BuildPlanReviewAsync();
        original.BrokerOperatorCode = "fixture-operator";
        await original.ApprovePlanReviewAsync();
        original.CloseJob();

        await using var store = await disk.OpenSqliteAsync();
        var history = Assert.Single(await store.LoadJobJournalsAsync(jobId));
        var approval = Assert.Single(history.Approvals);
        var correlation = Guid.NewGuid();
        var intents = history.Plan.Actions.Select(action => new JournalEntry(Guid.NewGuid(), approval.Plan, action.ActionId,
            approval.ApprovalId, JournalState.Approved, JournalState.Started, JournalActor.Coordinator,
            new OperatorId("fixture-coordinator"), correlation, clock.Current, JournalResult.None,
            JournalCheckpoint.BrokerDispatchIntent)).ToImmutableArray();
        await store.AppendBrokerBatchAsync(intents);

        await using var reopened = OpenBrokerWorkspace(disk, clock);
        await reopened.OpenJobAsync(jobId.Value);
        Assert.True(reopened.CanCheckBrokerRecovery);
        Assert.False(reopened.CanRunBrokerExecution);
        Assert.Contains("Waiting for a final result", reopened.BrokerHistoryText, StringComparison.Ordinal);
        clock.Current += ExecutionCoordinator.MaximumDispatchLifetime;
        await reopened.CheckBrokerRecoveryAsync();
        Assert.False(reopened.CanCheckBrokerRecovery);
        Assert.Contains("needs review", reopened.BrokerHistoryText, StringComparison.Ordinal);
        var recovered = Assert.Single(await store.LoadJobJournalsAsync(jobId));
        Assert.All(recovered.Plan.Actions, action => Assert.Equal(JournalState.RequiresReview, recovered.StateOf(action.ActionId)));
        reopened.CloseJob();
        await reopened.OpenJobAsync(jobId.Value);
        Assert.Contains("needs review", reopened.BrokerHistoryText, StringComparison.Ordinal);
        var saved = Assert.Single(await store.LoadJobJournalsAsync(jobId));
        Assert.Equal(recovered.Entries, saved.Entries.AsEnumerable());
    }

    private static AuditWorkspace OpenBrokerWorkspace(AuditTestStore disk, FixtureClock clock,
        INoOpBrokerBackend? backend = null) =>
        new(async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false), [new PlanFactsProvider()], clock,
            fixturePlanPolicy: static _ => ReadyPolicy, fixtureBrokerCoordinator: journal => new(journal, backend, clock));

    private sealed class FixtureBrokerBackend(Func<int, NoOpExecutionBatch, Func<CancellationToken, Task<bool>>,
        IProgress<NoOpExecutionProgress>?, CancellationToken, Task<NoOpBrokerBatchResult>> run) : INoOpBrokerBackend
    {
        public int Calls { get; private set; }

        public Task<NoOpBrokerBatchResult> RunAsync(NoOpExecutionBatch batch, Func<CancellationToken, Task<bool>> canDispatch,
            IProgress<NoOpExecutionProgress>? progress, CancellationToken cancellationToken) =>
            run(++Calls, batch, canDispatch, progress, cancellationToken);
    }

    private static void SelectProfile(Window window, PlanReviewView panel, AuditWorkspace workspace, string id)
    {
        var selector = Named<ComboBox>(panel, "PlanProfileSelector");
        Assert.True(selector.IsEnabled);
        var option = Assert.Single(workspace.PlanProfiles, item => item.Profile.ProfileId == id);
        selector.SelectedItem = option;
        ShellTestHost.Drain(window);
        Assert.Equal(option, workspace.SelectedPlanProfile);
    }

    private static void Build(Window window, PlanReviewView panel, AuditWorkspace workspace)
    {
        var button = Named<Button>(panel, "BuildPlanReviewButton");
        Assert.True(button.IsEnabled, workspace.PlanReviewStatus);
        Invoke(window, button);
        PumpUntil(window, () => workspace.HasPlanReview && !workspace.IsBusy, () => workspace.PlanReviewStatus);
    }

    private static PlanReviewDecision Approve(Window window, PlanReviewView panel, AuditWorkspace workspace)
    {
        var button = Named<Button>(panel, "ApprovePlanReviewButton");
        Assert.True(button.IsEnabled, workspace.PlanReviewStatus);
        Invoke(window, button);
        PumpUntil(window, () => workspace.PlanReviewDecision is not null && !workspace.IsBusy,
            () => workspace.PlanReviewStatus);
        var decision = workspace.PlanReviewDecision!;
        Assert.Equal(PlanReviewOutcome.Approved, decision.Outcome);
        return decision;
    }

    private static void Toggle(Window window, PlanReviewView panel, object item)
    {
        var checkbox = Assert.Single(VisualChildren<CheckBox>(panel), element => ReferenceEquals(item, element.DataContext));
        var peer = Assert.IsType<CheckBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(checkbox));
        Assert.NotEmpty(peer.GetName());
        Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle)).Toggle();
        ShellTestHost.Drain(window);
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in VisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void SendKey(UIElement target, RoutedEvent routedEvent, Key key)
    {
        var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(target));
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = routedEvent,
        });
    }

    private static AuditWorkspace OpenWorkspace(AuditTestStore disk, PlanFactsProvider provider, bool useReadyPolicy = true) =>
        new(async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [provider], new FixtureClock(), fixturePlanPolicy: useReadyPolicy ? static _ => ReadyPolicy : null);

    private static async Task PrepareAssessmentAsync(AuditWorkspace workspace)
    {
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.StartAssessmentAsync();
        workspace.RepairDeviceClass = PolicyDeviceClass.Laptop;
        Assert.True(workspace.CanReviewPlan, workspace.PlanReviewStatus);
    }

    private sealed class FixtureClock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = EvaluationTime;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class PlanFactsProvider : IInventoryProvider
    {
        private static readonly EvidenceKey Architecture = new("system.architecture");
        private static readonly EvidenceKey Release = new("operating_system.release");
        private static readonly EvidenceKey Support = new("operating_system.support_state");
        private int _invocations;

        public int InvocationCount => Volatile.Read(ref _invocations);

        public InventoryProviderDescriptor Descriptor { get; } = new(new("fixture-plan-review"),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15),
            ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
            new(new("fixture.plan_review.failed"), "fixture.plan_review.failed"),
            [
                new(Architecture, EvidenceValueKind.Enum, null, "fixture.plan_review.architecture"),
                new(Release, EvidenceValueKind.Enum, null, "fixture.plan_review.release"),
                new(Support, EvidenceValueKind.Enum, null, "fixture.plan_review.support"),
            ]);

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _invocations);
            return ValueTask.FromResult(ProviderReadResult.Collected(
            [
                NormalizedEvidence.Observed(Architecture, EvidenceValue.FromEnum("x64")),
                NormalizedEvidence.Observed(Release, EvidenceValue.FromEnum("windows_11")),
                NormalizedEvidence.Observed(Support, EvidenceValue.FromEnum("supported")),
            ]));
        }
    }
}
