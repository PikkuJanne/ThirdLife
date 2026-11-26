using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.UI.Features.ManualTests;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Features.Recovery;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class RebootRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RebootDisposition.Resume, false, true)]
    [InlineData(RebootDisposition.Abandon, true, true)]
    [InlineData(RebootDisposition.Abandon, false, false)]
    public async Task ReopenedJobRequiresASelectedCheckpointAndAnExplicitDecision(
        RebootDisposition disposition, bool expired, bool currentPolicyPlan)
    {
        using var disk = new AuditTestStore();
        var job = new Job(new JobId("job-synthetic-restart-review"), new DeviceId("device-synthetic-restart-review"), Now.AddDays(-1));
        await using (var setup = await disk.OpenSqliteAsync())
        {
            await setup.CreateJobAsync(job);
        }

        var candidate = CreateCandidate(job.JobId, expired, currentPolicyPlan);
        var provider = AuditTestProvider.Available("reboot-recovery");
        RecoveryFixtureStore? openedStore = null;
        await using var workspace = new AuditWorkspace(
            async token =>
            {
                var inner = await disk.OpenSqliteAsync(token).ConfigureAwait(false);
                openedStore = new RecoveryFixtureStore(inner, candidate);
                return openedStore;
            },
            [provider], new FixtureClock());

        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                var audit = Named<AuditWorkspaceView>(window, "AuditView");
                var verification = Named<PackageVerificationView>(audit, "PackageVerificationPanel");
                var recovery = Named<RebootRecoveryView>(verification, "RebootRecoveryPanel");
                var selector = Named<ComboBox>(recovery, "RecoveryChoice");
                var resume = Named<Button>(recovery, "ResumeButton");
                var abandon = Named<Button>(recovery, "AbandonButton");
                var status = Named<TextBlock>(recovery, "DecisionStatus");
                Assert.False(selector.IsVisible);
                Assert.Null(openedStore);

                OpenJob(window, audit, workspace, job.JobId);
                window.ShowStage(ShellStage.Verify);
                var store = Assert.IsType<RecoveryFixtureStore>(openedStore);
                var savedJob = Assert.IsType<StoredJob>(workspace.Job);
                PumpUntil(window, () => selector.IsVisible, () =>
                    $"Verify visible: {verification.IsVisible}. Recovery choices: {workspace.RebootRecoveryChoices.Count}. {workspace.RebootRecoveryStatus}");
                Assert.Equal("Saved restart checkpoint", new ComboBoxAutomationPeer(selector).GetName());
                Assert.Equal("Resume for review", ShellTestHost.ButtonPeer(resume).GetName());
                Assert.Equal("Abandon resume", ShellTestHost.ButtonPeer(abandon).GetName());
                Assert.Equal(AutomationLiveSetting.Polite, new TextBlockAutomationPeer(status).GetLiveSetting());
                Assert.Null(selector.SelectedItem);
                Assert.False(resume.IsEnabled);
                Assert.False(abandon.IsEnabled);
                Assert.Equal(0, store.DecisionCount);

                selector.BringIntoView();
                Assert.True(selector.Focus());
                var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(selector));
                selector.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Down)
                {
                    RoutedEvent = Keyboard.KeyDownEvent,
                });
                PumpUntil(window, () => selector.SelectedItem is not null);
                Assert.Same(candidate, workspace.SelectedRebootRecovery!.Candidate);
                Assert.Equal(!expired && currentPolicyPlan, resume.IsEnabled);
                if (!currentPolicyPlan)
                {
                    Assert.Contains("Historical checkpoint", workspace.SelectedRebootRecovery.Label, StringComparison.Ordinal);
                }
                Assert.True(abandon.IsEnabled);
                Assert.Equal(0, store.DecisionCount);

                var verify = Named<ListBoxItem>(window, "VerifyStage");
                Assert.True(verify.Focus());
                Invoke(window, disposition == RebootDisposition.Resume ? resume : abandon);
                PumpUntil(window, () => !workspace.IsBusy && store.DecisionCount == 1 && status.Text.Length > 0,
                    () => workspace.RebootRecoveryStatus);
                var recorded = Assert.IsType<RebootRecoveryDecision>(store.Decision);
                Assert.Equal(candidate.Checkpoint, recorded.Checkpoint);
                Assert.Equal(disposition, recorded.Disposition);
                Assert.Contains(disposition == RebootDisposition.Resume ? "Resume recorded" : "Resume abandoned",
                    status.Text, StringComparison.Ordinal);
                Assert.Equal(status.Text, new TextBlockAutomationPeer(status).GetName());
                Assert.Same(verify, Keyboard.FocusedElement);
                Assert.False(resume.IsEnabled);
                Assert.False(abandon.IsEnabled);
                Assert.Equal(savedJob.Checkpoints, workspace.Job!.Checkpoints);
                Assert.Equal(savedJob.Observations, workspace.Job.Observations);

                Invoke(window, Named<Button>(window, "JobsButton"));
                Invoke(window, Named<Button>(audit, "CloseJobButton"));
                Assert.Null(workspace.Job);
                OpenJob(window, audit, workspace, job.JobId);
                window.ShowStage(ShellStage.Verify);
                Assert.Equal(1, store.DecisionCount);
                Assert.Equal(0, provider.InvocationCount);
                Assert.Contains(disposition == RebootDisposition.Resume ? "Resume recorded" : "Resume abandoned",
                    status.Text, StringComparison.Ordinal);
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    private static void OpenJob(Window window, AuditWorkspaceView audit, AuditWorkspace workspace, JobId jobId)
    {
        Named<TextBox>(audit, "JobIdInput").Text = jobId.Value;
        Invoke(window, Named<Button>(audit, "OpenJobButton"));
        PumpUntil(window, () => !workspace.IsBusy && workspace.Job?.Job.JobId == jobId, () => workspace.Status);
    }

    private static RebootResumeCandidate CreateCandidate(JobId jobId, bool expired, bool currentPolicyPlan)
    {
        var plan = new JournalPlanKey(jobId, new string('a', 64), new string('b', 64));
        var checkpoint = new RebootCheckpoint(Guid.NewGuid(), plan, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Now.AddHours(-2), expired ? Now.AddHours(-1) : Now.AddHours(1));
        return new RebootResumeCandidate(checkpoint, new RebootResumeToken(new byte[] { 1, 2, 3 }), currentPolicyPlan);
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecoveryFixtureStore(IJobStore inner, RebootResumeCandidate candidate)
        : IJobStore, IRebootCheckpointStore
    {
        public RebootRecoveryDecision? Decision { get; private set; }
        public int DecisionCount { get; private set; }

        public Task<RebootResumeCandidate?> IssueRebootCheckpointAsync(
            JournalEntry restartEntry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test loads a previously issued restart checkpoint.");

        public Task<RebootResumeCandidate?> LoadRebootCheckpointAsync(
            JobId jobId, Guid checkpointId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Decision is null && candidate.Checkpoint.Plan.JobId == jobId &&
                candidate.Checkpoint.Id == checkpointId ? candidate : null);

        public Task<ImmutableArray<RebootResumeCandidate>> ListPendingRebootCheckpointsAsync(
            JobId jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ImmutableArray<RebootResumeCandidate>>(
                Decision is null && candidate.Checkpoint.Plan.JobId == jobId ? [candidate] : []);

        public Task<RebootRecoveryDecision?> LoadLatestRebootDecisionAsync(
            JobId jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(candidate.Checkpoint.Plan.JobId == jobId ? Decision : null);

        public Task<RebootCheckpoint?> DecideRebootCheckpointAsync(
            JobId jobId, Guid checkpointId, RebootResumeToken token, RebootDisposition disposition,
            CancellationToken cancellationToken = default)
        {
            DecisionCount++;
            if (Decision is not null || jobId != candidate.Checkpoint.Plan.JobId ||
                checkpointId != candidate.Checkpoint.Id || !ReferenceEquals(candidate.Token, token))
            {
                return Task.FromResult<RebootCheckpoint?>(null);
            }

            Decision = new RebootRecoveryDecision(candidate.Checkpoint, disposition, Now);
            return Task.FromResult<RebootCheckpoint?>(candidate.Checkpoint);
        }

        public Task CreateJobAsync(Job job, CancellationToken cancellationToken = default) =>
            inner.CreateJobAsync(job, cancellationToken);

        public Task AppendEvidenceAsync(JobEvidenceBatch batch, CancellationToken cancellationToken = default) =>
            inner.AppendEvidenceAsync(batch, cancellationToken);

        public Task SetArchiveStateAsync(JobId jobId, bool isArchived, DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken = default) =>
            inner.SetArchiveStateAsync(jobId, isArchived, changedAtUtc, cancellationToken);

        public Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(
            SanitizationGateDecision candidate, CancellationToken cancellationToken = default) =>
            inner.RecordSanitizationGateDecisionAsync(candidate, cancellationToken);

        public Task<StoredJob?> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default) =>
            inner.LoadJobAsync(jobId, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
