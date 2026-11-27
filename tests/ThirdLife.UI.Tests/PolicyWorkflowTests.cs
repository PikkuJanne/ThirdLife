using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class PolicyWorkflowTests
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task SupportedDesktopAssessesEvaluatesReopensAndExportsAnIsolatedJob()
    {
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());
        Assert.Equal(StartupAccess.StandardUser, StartupSafety.CheckCurrentProcess());
        using var disk = new AuditTestStore();
        var provider = new MemoryProvider();
        var reportRoot = Path.Combine(disk.Root, "desktop-workshop-reports");
        await using var workspace = new AuditWorkspace(
            async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [provider], new FixtureClock(), new WorkshopReportStore(reportRoot));
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        workspace.RepairDeviceClass = PolicyDeviceClass.Laptop;
        var jobId = Assert.IsType<StoredJob>(workspace.Job).Job.JobId;
        M1AuditJourneyTests.JourneyResult? result = null;

        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 500 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Inspect);
                var audit = Named<FrameworkElement>(window, "AuditView");
                var start = Named<Button>(audit, "StartAssessmentButton");
                Assert.True(start.Focus());
                Invoke(window, start);
                PumpUntil(window, () => workspace.Assessment is not null && !workspace.IsBusy, () => workspace.AssessmentStatus);

                window.ShowStage(ShellStage.Decide);
                var panel = Named<PolicyDispositionView>(audit, "PolicyDispositionPanel");
                panel.LayoutTransform = new ScaleTransform(2, 2);
                ShellTestHost.Drain(window);
                var evaluate = Named<Button>(panel, "EvaluatePolicyButton");
                Assert.Equal("Evaluate candidate policy", ShellTestHost.ButtonPeer(evaluate).GetName());
                Assert.True(evaluate.Focus());
                Invoke(window, evaluate);
                PumpUntil(window, () => workspace.HasPolicyDecision && !workspace.IsBusy, () => workspace.PolicyStatus);
                Assert.Equal("Human review required", Named<TextBlock>(panel, "PolicyResultTitle").Text);
                var assessmentId = workspace.Assessment!.AssessmentId;

                Invoke(window, Named<Button>(audit, "CloseJobButton"));
                Assert.Null(workspace.Job);
                var idInput = Named<TextBox>(audit, "JobIdInput");
                Assert.Equal(jobId.Value, idInput.Text);
                Assert.Same(idInput, Keyboard.FocusedElement);
                Invoke(window, Named<Button>(audit, "OpenJobButton"));
                PumpUntil(window, () => workspace.HasPolicyDecision && !workspace.IsBusy, () => workspace.Status);
                Assert.Equal(assessmentId, workspace.Assessment!.AssessmentId);
                Assert.Equal("Human review required", workspace.PolicyDispositionTitle);

                window.ShowStage(ShellStage.Handover);
                result = M1AuditJourneyTests.GenerateAndOpenReport(window, audit, workspace, jobId, assessmentId);
            }
            finally { CloseWindow(window); }
        });

        var exported = Assert.IsType<M1AuditJourneyTests.JourneyResult>(result);
        await using var store = await disk.OpenSqliteAsync();
        var saved = Assert.IsType<StoredPolicyDecision>(
            await store.LoadLatestPolicyDecisionAsync(jobId, exported.AssessmentId.Value));
        Assert.True(saved.Snapshot.Document.IsCurrentSchema);
        Assert.Equal("0.1.0-candidate.3", saved.Decision.PolicyVersion);
        Assert.Equal(Disposition.HumanReviewRequired, saved.Decision.Disposition);
        Assert.Equal(1, provider.InvocationCount);
        Assert.Contains(saved.Decision.PolicySha256, exported.Preview, StringComparison.Ordinal);
        Assert.Contains(saved.Decision.Explanation, exported.Preview, StringComparison.Ordinal);
        Assert.Contains("synthetic fixture", exported.Preview, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(exported.Preview, await File.ReadAllTextAsync(Path.Combine(reportRoot, exported.FileName)));
    }

    [Fact]
    public async Task CandidateEvaluationAndExplicitReevaluationKeepTheOriginalSavedAssessmentAndDecision()
    {
        using var disk = new AuditTestStore();
        var provider = new MemoryProvider();
        JobId jobId;
        string assessmentId;
        EvidenceId observedId;
        EvidenceId[] initialEvidenceIds;
        PolicyDecisionRecord first;

        await using (var workspace = disk.OpenWorkspace([provider]))
        {
            await PrepareAssessmentAsync(workspace);
            var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
            jobId = assessment.JobId;
            assessmentId = assessment.AssessmentId.Value;
            observedId = Assert.Single(assessment.Observations).Metadata.EvidenceId;
            initialEvidenceIds = ObservationIds(workspace);
            Assert.True(workspace.CanEvaluatePolicy, workspace.PolicyStatus);
            Assert.False(workspace.CanReevaluatePolicy);
            Assert.False(workspace.HasPolicyDecision);

            await workspace.EvaluatePolicyAsync();

            Assert.True(workspace.HasPolicyDecision, workspace.PolicyStatus);
            Assert.Equal("Human review required", workspace.PolicyDispositionTitle);
            Assert.False(workspace.CanEvaluatePolicy);
            Assert.True(workspace.CanReevaluatePolicy);
            Assert.Equal(initialEvidenceIds, ObservationIds(workspace));
            Assert.Equal(1, provider.InvocationCount);
            await using var saved = await disk.OpenSqliteAsync();
            first = Assert.IsType<StoredPolicyDecision>(await saved.LoadLatestPolicyDecisionAsync(jobId, assessmentId)).Decision;
            Assert.Contains(observedId, first.EvidenceIds);
            Assert.Null(first.PreviousDecisionId);
        }

        var reportStore = new WorkshopReportStore(Path.Combine(disk.Root, "policy-workshop-reports"));
        await using var reopened = new AuditWorkspace(
            async token => await disk.OpenSqliteAsync(token).ConfigureAwait(false),
            [provider], new FixtureClock(), reportStore);
        await reopened.OpenJobAsync(jobId.Value);

        Assert.True(reopened.HasPolicyDecision, reopened.PolicyStatus);
        Assert.Equal(first.Title, reopened.PolicyDispositionTitle);
        Assert.False(reopened.CanEvaluatePolicy);
        Assert.True(reopened.CanReevaluatePolicy);

        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        var report = reopened.ReportPreview;
        Assert.Contains("Schema: workshop-disposition-v2", report, StringComparison.Ordinal);
        Assert.Contains("Saved observations", report, StringComparison.Ordinal);
        Assert.Contains($"Evidence ID {observedId.Value}.", report, StringComparison.Ordinal);
        Assert.Contains($"Policy ID {first.PolicyId}.", report, StringComparison.Ordinal);
        Assert.Contains($"Version {first.PolicyVersion}.", report, StringComparison.Ordinal);
        Assert.Contains($"SHA-256 {first.PolicySha256}.", report, StringComparison.Ordinal);
        Assert.Contains("Candidate policy evaluation for workshop review.", report, StringComparison.Ordinal);
        Assert.Contains("Policy rule details", report, StringComparison.Ordinal);
        Assert.Contains("Captured disposition", report, StringComparison.Ordinal);
        Assert.Contains(first.Title, report, StringComparison.Ordinal);

        await reopened.ReevaluatePolicyAsync();

        Assert.Equal(initialEvidenceIds, ObservationIds(reopened));
        Assert.Equal(1, provider.InvocationCount);
        await using var history = await disk.OpenSqliteAsync();
        var latest = Assert.IsType<StoredPolicyDecision>(await history.LoadLatestPolicyDecisionAsync(jobId, assessmentId)).Decision;
        Assert.NotEqual(first.DecisionId, latest.DecisionId);
        Assert.Equal(first.DecisionId, latest.PreviousDecisionId);
        Assert.Equal(first.EvidenceIds.AsEnumerable(), latest.EvidenceIds.AsEnumerable());
        Assert.Equal(first.PolicySha256, latest.PolicySha256);
        var historic = Assert.IsType<StoredPolicyDecision>(await history.LoadPolicyDecisionAsync(jobId, first.DecisionId)).Decision;
        Assert.Equal(first.RuleReport, historic.RuleReport);
        Assert.Equal(first.Explanation, historic.Explanation);

        await reopened.RecordSanitizationAsync(VerifiedSample() with { MediaIdentifier = "fixture-new-media" });
        Assert.True(reopened.CanAssess, reopened.GateStatus);
        Assert.True(reopened.HasPolicyDecision);
        Assert.False(reopened.CanReevaluatePolicy);
        Assert.Equal(assessmentId, reopened.Assessment!.AssessmentId.Value);
        Assert.Contains("earlier intake record", reopened.PolicyStatus, StringComparison.Ordinal);
        Assert.Contains("current intake record", reopened.PolicyEvaluationRestriction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadedPolicyControlsExposeNamesAndExpertDetailsReturnKeyboardFocus()
    {
        using var disk = new AuditTestStore();
        await using var workspace = disk.OpenWorkspace([AuditTestProvider.Available("policy-keyboard")]);
        await PrepareAssessmentAsync(workspace);

        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 500 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Decide);
                ShellTestHost.Drain(window);
                var audit = Named<FrameworkElement>(window, "AuditView");
                var panel = Named<PolicyDispositionView>(audit, "PolicyDispositionPanel");
                var evaluate = Named<Button>(panel, "EvaluatePolicyButton");
                var reevaluate = Named<Button>(panel, "ReevaluatePolicyButton");
                var status = Named<TextBlock>(panel, "PolicyStatusMessage");
                var title = Named<TextBlock>(panel, "PolicyResultTitle");
                var expert = Named<Expander>(panel, "PolicyExpertExpander");
                var detailText = Named<TextBox>(panel, "PolicyExpertText");
                var disclosure = new ExpanderAutomationPeer(expert);

                Assert.True(panel.IsVisible);
                Assert.Equal("Evaluate candidate policy", ShellTestHost.ButtonPeer(evaluate).GetName());
                Assert.Equal("Reevaluate candidate policy", ShellTestHost.ButtonPeer(reevaluate).GetName());
                Assert.Equal(AutomationLiveSetting.Polite, new TextBlockAutomationPeer(status).GetLiveSetting());
                Assert.True(evaluate.IsEnabled);
                Assert.False(reevaluate.IsEnabled);
                Assert.True(evaluate.IsVisible);
                Assert.False(reevaluate.IsVisible);
                Assert.False(expert.IsVisible);

                Assert.True(evaluate.Focus());
                Invoke(window, evaluate);
                PumpUntil(window, () => workspace.HasPolicyDecision && !workspace.IsBusy, () => workspace.PolicyStatus);
                Assert.Equal("Human review required", title.Text);
                Assert.False(evaluate.IsEnabled);
                Assert.True(reevaluate.IsEnabled);
                Assert.False(evaluate.IsVisible);
                Assert.True(reevaluate.IsVisible);
                Assert.Equal("Policy and evidence details", disclosure.GetName());
                Assert.False(expert.IsExpanded);
                Assert.False(detailText.IsVisible);
                Assert.True(title.Focus());
                Assert.True(title.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                ShellTestHost.Drain(window);
                var header = Assert.IsType<ToggleButton>(Keyboard.FocusedElement);
                Assert.True(expert.IsKeyboardFocusWithin);

                SendKey(header, Keyboard.KeyDownEvent, Key.Space);
                SendKey(header, Keyboard.KeyUpEvent, Key.Space);
                PumpUntil(window, () => expert.IsExpanded && detailText.IsKeyboardFocused);
                Assert.Equal("Saved policy and evidence details", new TextBoxAutomationPeer(detailText).GetName());
                Assert.True(detailText.IsReadOnly);
                Assert.Equal(workspace.PolicyExpertDetails, detailText.Text);
                Assert.NotEmpty(detailText.Text);

                SendKey(detailText, Keyboard.PreviewKeyDownEvent, Key.Escape);
                ShellTestHost.Drain(window);
                Assert.False(expert.IsExpanded);
                Assert.Same(header, Keyboard.FocusedElement);
            }
            finally { CloseWindow(window); }
        });
    }

    private static async Task PrepareAssessmentAsync(AuditWorkspace workspace)
    {
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.StartAssessmentAsync();
        workspace.RepairDeviceClass = PolicyDeviceClass.Laptop;
    }

    private static EvidenceId[] ObservationIds(AuditWorkspace workspace) =>
        Assert.IsType<StoredJob>(workspace.Job).Observations.Select(observation => observation.Metadata.EvidenceId).ToArray();

    private static void SendKey(UIElement target, RoutedEvent routedEvent, Key key)
    {
        var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(target));
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = routedEvent,
        });
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 14, 1, 0, TimeSpan.Zero);
    }

    private sealed class MemoryProvider : IInventoryProvider
    {
        private static readonly EvidenceKey MemoryKey = new("memory.installed_bytes");
        private int _invocations;

        public int InvocationCount => Volatile.Read(ref _invocations);

        public InventoryProviderDescriptor Descriptor { get; } = new(new("fixture-policy-memory"),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15),
            ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
            new(new("fixture.policy_memory.failed"), "fixture.policy_memory.failed"),
            [new(MemoryKey, EvidenceValueKind.WholeNumber, "bytes", "fixture.policy_memory")]);

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _invocations);
            return ValueTask.FromResult(ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(MemoryKey, EvidenceValue.FromInteger(17_179_869_184))]));
        }
    }
}
