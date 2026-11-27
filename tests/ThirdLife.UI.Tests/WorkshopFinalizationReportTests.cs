using System.IO;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed class WorkshopFinalizationReportTests
{
    [Fact]
    public async Task ReportsReflectTheCurrentEvidenceWithoutRewritingAnEarlierConfirmation()
    {
        using var disk = new AuditTestStore();
        uint bootSession = 10;
        var clock = TimeProvider.System;
        Task<SqliteJobStore> OpenStore(CancellationToken token = default) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion, clock,
            faultInjector: null, () => 180_000, () => bootSession, token);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "workshop-reports"));
        await using var workspace = new AuditWorkspace(async token => await OpenStore(token).ConfigureAwait(false),
            [AuditTestProvider.Available("workshop-finalization-report")], clock, reports);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        var jobId = new JobId(workspace.JobId);
        await using (var store = await OpenStore())
        {
            var checklist = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(jobId));
            await store.SaveFinalizationConfirmationAsync(jobId, checklist, FinalizationRuleId.BrowserSessions,
                FinalizationOutcome.NotApplicable, FinalizationReason.NoWorkshopUse, new OperatorId("operator-report-review"));
        }

        await workspace.GenerateReportAsync();
        await workspace.OpenReportAsync();
        Assert.Contains("Current confirmation for Browser sessions. Not applicable.", workspace.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("Required finalization checks remain open.", workspace.ReportPreview, StringComparison.Ordinal);
        var first = workspace.ReportArtifact;

        bootSession++;
        await workspace.GenerateReportAsync();
        await workspace.OpenReportAsync();
        Assert.NotEqual(first, workspace.ReportArtifact);
        Assert.Contains("Browser sessions. Required, blocks handover. Needs recheck", workspace.ReportPreview, StringComparison.Ordinal);
        Assert.Contains("Earlier or superseded confirmation for Browser sessions. Not applicable.", workspace.ReportPreview,
            StringComparison.Ordinal);
        Assert.Contains("operator-report-review", workspace.ReportPreview, StringComparison.Ordinal);
        await using var reopened = await OpenStore();
        Assert.Single(Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(jobId)).History);
    }
}
