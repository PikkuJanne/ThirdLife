using System.Text;
using System.Windows.Controls;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Versioning;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PlanReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsUpdateApprovalAndExplicitExecutionSurviveReopen(bool decline)
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        var agent = new ServicingFixture(clock, decline);
        var policy = PolicySnapshot.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ReadyPolicy.Utf8Json.AsSpan())
            .Replace("plan_review_test", "windows_update_fixture", StringComparison.Ordinal)));
        AuditWorkspace Open() => new(async token => await disk.OpenSqliteAsync(token), [new PlanFactsProvider()], clock,
            fixturePlanPolicy: _ => policy, fixtureWindowsUpdates: agent);
        string jobId;
        await using (var workspace = Open())
        {
            await PrepareAssessmentAsync(workspace);
            jobId = workspace.JobId;
            await workspace.ScanWindowsUpdatesAsync();
            await workspace.CheckUpdateOfferAsync();
            Assert.True(workspace.CanEvaluateUpdatePolicy, workspace.WindowsUpdatePreview);
            await workspace.EvaluateUpdatePolicyAsync();
            Assert.Contains("allows preparation", workspace.WindowsUpdatePreview);
            await workspace.CheckUpdateOfferAsync();
            Assert.True(workspace.ShowsUpdateApproval, workspace.WindowsUpdatePreview);
            Assert.False(workspace.CanApproveWindowsUpdate);
            InPlanReview(workspace, (window, panel) =>
            {
                var actor = Named<TextBox>(panel, "UpdateOperatorInput");
                actor.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.True(actor.Focus());
                actor.Text = "fixture-refurbisher";
                ShellTestHost.Drain(window);
                var approve = Named<Button>(panel, "ApproveUpdateButton");
                Assert.True(approve.IsEnabled);
                Invoke(window, approve);
                PumpUntil(window, () => !workspace.IsBusy);
                Assert.True(workspace.CanRunApprovedUpdate, workspace.OperationError + workspace.WindowsUpdatePreview);
            });
            Assert.Empty(agent.Attempted);
        }

        await using var reopened = Open();
        await reopened.OpenJobAsync(jobId);
        Assert.Contains(ServicingFixture.Exact.Identity.UpdateId.ToString("D"), reopened.ApprovedUpdateScope);
        Assert.Empty(agent.Attempted);
        InPlanReview(reopened, (window, panel) =>
        {
            var start = Named<Button>(panel, "RunUpdateButton");
            start.BringIntoView();
            ShellTestHost.Drain(window);
            Assert.True(start.Focus());
            Invoke(window, start);
            PumpUntil(window, () => !reopened.IsBusy);
        });
        await using var store = await disk.OpenSqliteAsync();
        var history = await store.LoadWindowsUpdatesAsync(new JobId(jobId));
        var approval = Assert.Single(history, entry => entry.Stage == WindowsUpdateStage.Approved);
        Assert.Equal("fixture-refurbisher", approval.Approval!.OperatorId.Value);
        if (decline)
        {
            Assert.Empty(agent.Attempted);
            Assert.Equal(WindowsUpdateStage.DispatchNotSent, history[^1].Stage);
            Assert.Contains("elevation consent was declined", reopened.WindowsUpdateStatusText);
        }
        else
        {
            Assert.Equal(new[] { approval.Approval.Actions!.Download.ActionId, approval.Approval.Actions.Install.ActionId },
                agent.Attempted);
            Assert.True(history[^1].Verification!.Observation.Installed);
            Assert.Contains("exact revision installed", reopened.WindowsUpdateVerificationText);
        }
        Assert.False(reopened.CanRunApprovedUpdate);
    }

    private sealed class ServicingFixture(TimeProvider clock, bool decline) : IWindowsUpdateAgent, IWindowsUpdateDispatchBackend
    {
        internal static readonly WindowsUpdateCandidate Exact = new(new(Guid.Parse("0bd28c62-dcf0-4733-a7b9-3574b8636ac0"), 2),
            WindowsUpdateClass.Critical, 1024);
        internal List<Guid> Attempted { get; } = [];
        private bool _installed;

        public Task<WindowsUpdateScan> ScanAsync(CancellationToken token) =>
            Task.FromResult(new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, _installed ? [] : [Exact], false));
        public Task<WindowsUpdatePreparation> AssessPreparationAsync(WindowsUpdateCandidate exact, CancellationToken token) =>
            Task.FromResult(new WindowsUpdatePreparation(exact, WindowsUpdateStatus.Succeeded, clock.GetUtcNow(),
                false, true, true, 8192, 8192, true, 2048, WindowsUpdateEligibilityReason.Eligible, true));
        public Task<WindowsUpdateInstalledObservation> ObserveInstalledAsync(WindowsUpdateIdentity exact, CancellationToken token) =>
            Task.FromResult(new WindowsUpdateInstalledObservation(WindowsUpdateStatus.Succeeded, exact, _installed));
        public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken token) => throw new NotSupportedException();
        public Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken token) => throw new NotSupportedException();
        public Task<IWindowsUpdateDispatchChannel?> PrepareAsync(WindowsUpdateDispatch dispatch, CancellationToken token) =>
            decline ? throw new WindowsUpdateConsentDeclinedException() : Task.FromResult<IWindowsUpdateDispatchChannel?>(new Channel(this, dispatch));

        private sealed class Channel(ServicingFixture owner, WindowsUpdateDispatch dispatch) : IWindowsUpdateDispatchChannel
        {
            public WindowsUpdateDispatch Dispatch => dispatch;
            public Task<WindowsUpdateDispatchReceipt?> SendAsync(CancellationToken token)
            {
                owner.Attempted.Add(dispatch.Action.ActionId);
                owner._installed = dispatch.Action.Impact.Kind == ActionKind.InstallWindowsUpdate;
                return Task.FromResult<WindowsUpdateDispatchReceipt?>(new(dispatch, new(WindowsUpdateStatus.Succeeded, false, 100)));
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
