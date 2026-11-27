using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Core.Handover;
using ThirdLife.Persistence;
using ThirdLife.UI.Features.Finalization;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditFinalizationTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditToggleKeysTests
{
    [Fact]
    public async Task AssistedHandoverPreviewsAppliesVerifiesAndRestoresOnlyAfterSeparateApprovals()
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        var native = new ControlledToggleKeys();
        const string actor = "operator-toggle-ui";
        var registered = Path.Combine(disk.Root, "store");
        await using var workspace = new AuditWorkspace(
            async token => await SqliteJobStore.OpenForTestingAsync(registered, SqliteJobStore.CurrentSchemaVersion,
                clock, null, () => 180_000, () => 10, token).ConfigureAwait(false),
            [AuditTestProvider.Available("recipient-toggle-ui")], clock, fixtureToggleKeys: native);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());

        InHandover(workspace, (window, view) =>
        {
            PumpUntil(window, () => workspace.CanRecordHandoverMode);
            var mode = Named<ComboBox>(view, "HandoverModeChoice");
            var modeActor = Named<TextBox>(view, "HandoverModeOperator");
            modeActor.Text = actor;
            mode.SelectedValue = HandoverMode.Assisted;
            Invoke(window, Named<Button>(view, "SaveHandoverModeButton"));
            PumpUntil(window, () => workspace.CanPreviewToggleKeys, () => workspace.HandoverModeStatus);
            var preview = Named<Button>(view, "PreviewToggleKeysOnButton");
            var apply = Named<Button>(view, "ApplyToggleKeysButton");
            var confirm = Named<CheckBox>(view, "ToggleKeysConfirmed");
            var field = Named<TextBox>(view, "ToggleKeysOperator");
            var status = Named<TextBlock>(view, "ToggleKeysMessage");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Invoke(window, preview);
            PumpUntil(window, () => !workspace.IsBusy && workspace.ToggleKeysChangeReview is not null);
            Assert.Contains("Current Windows user only", status.Text, StringComparison.Ordinal);
            Assert.False(apply.IsEnabled);
            Assert.Equal(0, native.DispatchCount);

            confirm.IsChecked = true;
            field.Text = actor;
            Invoke(window, apply);
            PumpUntil(window, () => !workspace.IsBusy && workspace.CanReviewToggleKeysRestoration);
            Assert.Contains("independently confirmed", status.Text, StringComparison.Ordinal);
            Assert.Equal(1, native.DispatchCount);
            Assert.True(native.Enabled);

            Invoke(window, Named<Button>(view, "PreviewToggleKeysRestorationButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.ToggleKeysChangeReview is { Requested: false });
            Assert.False(apply.IsEnabled);
            Assert.False(confirm.IsChecked);
            confirm.IsChecked = true;
            field.Text = actor;
            Invoke(window, apply);
            PumpUntil(window, () => !workspace.IsBusy && native.DispatchCount == 2);
            Assert.False(native.Enabled);
            Assert.Contains("independently confirmed", status.Text, StringComparison.Ordinal);
            mode.SelectedValue = HandoverMode.Sealed;
            Invoke(window, Named<Button>(view, "SaveHandoverModeButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.RecipientHandover?.Handover?.Mode == HandoverMode.Sealed);
            Assert.False(workspace.ShowsToggleKeys);
        });

        var jobId = Assert.IsType<ThirdLife.Core.Jobs.StoredJob>(workspace.Job).Job.JobId;
        await using var verifier = await SqliteJobStore.OpenForTestingAsync(registered, SqliteJobStore.CurrentSchemaVersion,
            clock, null, () => 180_000, () => 10);
        var records = await verifier.LoadToggleKeysAttemptsAsync(jobId);
        Assert.Equal(4, records.Count);
        Assert.Collection(records.Where(row => row.Outcome == ToggleKeysAuditOutcome.Verified),
            entry => Assert.True(entry.IndependentlyObserved), entry => Assert.False(entry.IndependentlyObserved));
        Assert.Equal(2, native.DispatchCount);
    }

    private sealed class ControlledToggleKeys : IRecipientToggleKeys
    {
        private readonly Guid _binding = Guid.NewGuid();
        public bool Enabled { get; private set; }
        public int DispatchCount { get; private set; }
        public ToggleKeysObservation Read() => new(_binding, Enabled);

        public ToggleKeysDispatchResult Dispatch(ToggleKeysApproval approval, SavedRecipientHandover current)
        {
            Assert.True(approval.CanDispatch(current, Read()));
            DispatchCount++;
            Enabled = approval.Change.Requested;
            return ToggleKeysDispatchResult.Dispatched;
        }
    }
}
