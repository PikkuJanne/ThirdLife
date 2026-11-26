using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Repairs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.UI.Features.Disposition;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class RepairWorkflowTests
{
    private const string OperatorCode = "fixture-repair-operator";

    [Fact]
    public async Task StorageReplacementRequiresNewIntakeAndPersistsLinkedRetestAcrossReopen()
    {
        using var disk = new AuditTestStore();
        var provider = new StorageHealthProvider();
        string jobId;
        AssessmentId priorAssessmentId;
        AssessmentId retestAssessmentId;
        EvidenceId priorFailureId;
        EvidenceId healthyEvidenceId;

        await using (var workspace = disk.OpenWorkspace([provider]))
        {
            await workspace.CreateJobAsync();
            jobId = workspace.JobId;
            await workspace.RecordSanitizationAsync(VerifiedSample());
            await workspace.StartAssessmentAsync();
            var prior = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
            priorAssessmentId = prior.AssessmentId;
            var failure = Assert.Single(prior.Observations);
            priorFailureId = failure.Metadata.EvidenceId;
            Assert.Equal("unhealthy", failure.Value!.StringValue);
            Assert.Equal(1, provider.InvocationCount);
            var finding = Assert.Single(workspace.RepairCandidates);
            Assert.Equal(RepairComponent.SystemStorage, finding.Component);
            Assert.Equal(priorFailureId, finding.FailedEvidenceId);
            Assert.Contains("Review the reported storage health", finding.Caption, StringComparison.Ordinal);

            await workspace.RecordRepairAsync(finding, RepairAction.Replaced, OperatorCode);
            var repair = Assert.Single(workspace.SavedRepairs);
            Assert.False(workspace.CanAssess);
            Assert.False(workspace.CanStartRetest);
            Assert.Contains("verified replacement-storage evidence", workspace.Status, StringComparison.Ordinal);
            Assert.Equal(SanitizationState.Unknown, Assert.IsType<StoredJob>(workspace.Job).SanitizationEvidence[^1].SanitizationState);

            await workspace.StartAssessmentAsync();
            await workspace.StartRetestAsync(repair.RepairId);
            Assert.Equal(1, provider.InvocationCount);
            Assert.Equal(priorAssessmentId, Assert.IsType<AssessmentSnapshot>(workspace.Assessment).AssessmentId);
            Assert.Empty(RepairJournal.GetRetests(Assert.IsType<StoredJob>(workspace.Job)));

            await workspace.RecordSanitizationAsync(VerifiedSample() with
            {
                State = SanitizationState.ReplacementStorage,
                MediaIdentifier = "replacement-fixture",
            });
            Assert.True(workspace.CanAssess, workspace.GateStatus);
            Assert.True(workspace.CanStartRetest);

            await workspace.StartRetestAsync(repair.RepairId);
            var retest = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
            retestAssessmentId = retest.AssessmentId;
            Assert.Equal(AssessmentOutcome.Completed, retest.Outcome);
            Assert.NotEqual(priorAssessmentId, retestAssessmentId);
            var healthy = Assert.Single(retest.Observations);
            healthyEvidenceId = healthy.Metadata.EvidenceId;
            Assert.NotEqual(priorFailureId, healthyEvidenceId);
            Assert.Equal(StorageHealthProvider.ReportedHealth, healthy.EvidenceKey);
            Assert.Equal("healthy", healthy.Value!.StringValue);
            Assert.Equal(2, provider.InvocationCount);
            Assert.Empty(workspace.RepairCandidates);
            var link = Assert.Single(RepairJournal.GetRetests(Assert.IsType<StoredJob>(workspace.Job)));
            Assert.Equal(retestAssessmentId.Value, link.AssessmentId);
            Assert.Equal(repair.RepairId, link.RepairId);
            Assert.Equal(priorFailureId, link.PriorFailureEvidenceId);
        }

        await using var reopened = disk.OpenWorkspace([provider]);
        await reopened.OpenJobAsync(jobId);

        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(retestAssessmentId, Assert.IsType<AssessmentSnapshot>(reopened.Assessment).AssessmentId);
        var reopenedJob = Assert.IsType<StoredJob>(reopened.Job);
        var saved = Assert.Single(RepairJournal.GetRepairs(reopenedJob));
        Assert.Equal(OperatorCode, saved.OperatorId.Value);
        Assert.Equal(priorAssessmentId.Value, saved.PriorAssessmentId);
        Assert.Equal(RepairAction.Replaced, saved.Action);
        Assert.Equal(priorFailureId, saved.FailedEvidenceId);
        Assert.Collection(reopenedJob.Observations.Where(observation => observation.EvidenceKey == StorageHealthProvider.ReportedHealth),
            failure =>
            {
                Assert.Equal(priorFailureId, failure.Metadata.EvidenceId);
                Assert.Equal("unhealthy", failure.Value!.StringValue);
            },
            healthy =>
            {
                Assert.Equal(healthyEvidenceId, healthy.Metadata.EvidenceId);
                Assert.Equal("healthy", healthy.Value!.StringValue);
            });
        Assert.Collection(reopened.RepairHistory,
            repair => Assert.Contains(priorFailureId.Value, repair, StringComparison.Ordinal),
            retest =>
            {
                Assert.Contains(retestAssessmentId.Value, retest, StringComparison.Ordinal);
                Assert.Contains(priorFailureId.Value, retest, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task RepairFormExposesNamedNativeControlsInKeyboardOrder()
    {
        using var disk = new AuditTestStore();
        await using var workspace = disk.OpenWorkspace([new StorageHealthProvider()]);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.StartAssessmentAsync();

        ShellTestHost.Run(() =>
        {
            var view = new RepairWorkflowView { DataContext = workspace };
            var window = new Window { Content = view, Width = 550, Height = 720 };
            try
            {
                var form = Assert.IsType<Expander>(view.Content);
                form.IsExpanded = true;
                ShellTestHost.Show(window);
                Assert.Equal("Repair and retest", new ExpanderAutomationPeer(form).GetName());

                var deviceClass = Named<ComboBox>(view, "DeviceClassInput");
                var finding = Named<ComboBox>(view, "RepairFindingInput");
                var action = Named<ComboBox>(view, "RepairActionInput");
                var operatorCode = Named<TextBox>(view, "RepairOperatorInput");
                var saved = Named<ComboBox>(view, "SavedRepairInput");
                var record = Named<Button>(view, "RecordRepairButton");
                var retest = Named<Button>(view, "StartRetestButton");

                Assert.Equal("Device class", new ComboBoxAutomationPeer(deviceClass).GetName());
                Assert.Equal("Saved finding for completed work", new ComboBoxAutomationPeer(finding).GetName());
                Assert.Equal("Completed work", new ComboBoxAutomationPeer(action).GetName());
                Assert.Equal("Repair operator code", new TextBoxAutomationPeer(operatorCode).GetName());
                Assert.Equal("Saved repair to retest", new ComboBoxAutomationPeer(saved).GetName());
                Assert.Equal("Record completed repair", ShellTestHost.ButtonPeer(record).GetName());
                Assert.Equal("Start linked read-only retest", ShellTestHost.ButtonPeer(retest).GetName());
                Assert.False(retest.IsEnabled);
                Assert.Equal(AutomationLiveSetting.Polite,
                    new TextBlockAutomationPeer(Named<TextBlock>(view, "RepairInputMessage")).GetLiveSetting());

                Assert.True(deviceClass.Focus());
                NextIs(window, finding);
                NextIs(window, action);
                NextIs(window, operatorCode);
                NextIs(window, record);
            }
            finally { CloseWindow(window); }
        });
    }

    private sealed class StorageHealthProvider : IInventoryProvider
    {
        public static EvidenceKey ReportedHealth { get; } = new("storage.physical_disk.reported_health");
        private int _calls;

        public int InvocationCount => Volatile.Read(ref _calls);

        public InventoryProviderDescriptor Descriptor { get; } = new(new("fixture-repair-storage"),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15),
            ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
            new(new("fixture.repair_storage.failed"), "fixture.repair_storage.failed"),
            [new(ReportedHealth, EvidenceValueKind.Enum, null, "fixture.repair_storage")]);

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var health = Interlocked.Increment(ref _calls) == 1 ? "unhealthy" : "healthy";
            return ValueTask.FromResult(ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(ReportedHealth, EvidenceValue.FromEnum(health))]));
        }
    }
}
