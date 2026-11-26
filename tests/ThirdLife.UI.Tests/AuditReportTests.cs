using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Providers;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditReportTests
{
    private const string KeyboardCase = "MHT-002";
    private const string ReviewerCode = "fixture-report-operator";
    private const string FixtureNotes = "Fixture hardware means the observed sample remains unavailable.";
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task KeyboardCanGenerateAndOpenAReportFromTheDurablePartialJob()
    {
        using var disk = new AuditTestStore();
        using var firstOutput = new ReportTestDirectory();
        using var secondOutput = new ReportTestDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IInventoryProvider[] providers =
        [
            AuditTestProvider.Available("report-a-collected"),
            new AuditTestProvider("report-b-unavailable", _ => ValueTask.FromResult(ProviderReadResult.Unavailable())),
            new AuditTestProvider("report-c-cancelled", token =>
            {
                entered.TrySetResult();
                return WaitForCancellationAsync(token);
            }),
            AuditTestProvider.Available("report-d-unrun"),
        ];
        await using var workspace = OpenWorkspace(disk, firstOutput.Store(), providers);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        var pending = workspace.StartAssessmentAsync();
        try
        {
            await entered.Task.WaitAsync(TestWait);
        }
        finally
        {
            workspace.CancelAssessment();
            await pending.WaitAsync(TestWait);
        }

        var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
        Assert.Equal(AssessmentOutcome.Cancelled, assessment.Outcome);
        var cached = Assert.IsType<StoredJob>(workspace.Job);
        var jobId = cached.Job.JobId;
        var manual = await AppendManualResult(disk, jobId);
        Assert.Empty(cached.HumanTests);

        string? previewText = null;
        ShowAuditWindow(workspace, (window, view) =>
        {
            var generate = Named<Button>(view, "GenerateReportButton");
            var open = Named<Button>(view, "OpenReportButton");
            Assert.True(generate.Focus());
            Assert.Contains("generate", ShellTestHost.ButtonPeer(generate).GetName(), StringComparison.OrdinalIgnoreCase);
            Assert.False(open.IsEnabled);
            Invoke(window, generate);
            PumpUntil(window, () => workspace.CanOpenReport, () => workspace.ReportStatus);
            Assert.Same(open, Keyboard.FocusedElement);

            Assert.True(generate.Focus());
            NextIs(window, open);
            Assert.Contains("open", ShellTestHost.ButtonPeer(open).GetName(), StringComparison.OrdinalIgnoreCase);
            Invoke(window, open);
            PumpUntil(window, () => workspace.ReportPreview.Length > 0 && !workspace.IsBusy, () => workspace.ReportStatus);

            var preview = Named<TextBox>(view, "WorkshopReportPreview");
            Assert.Same(preview, Keyboard.FocusedElement);
            Assert.True(preview.IsReadOnly);
            Assert.NotEmpty(AutomationProperties.GetName(preview));
            Assert.Equal(workspace.ReportPreview, preview.Text);
            AssertReportAnnouncement(view, workspace);
            previewText = preview.Text;

            Invoke(window, Named<Button>(view, "CreateJobButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.Job?.Job.JobId != jobId, () => workspace.Status);
            Assert.Null(workspace.ReportArtifact);
            Assert.Empty(workspace.ReportPreview);
            Assert.False(open.IsEnabled);
        });

        var report = Assert.IsType<string>(previewText);
        Assert.Contains("WORKSHOP_RESTRICTED", report, StringComparison.Ordinal);
        Assert.Contains(jobId.Value, report, StringComparison.Ordinal);
        Assert.Contains(cached.Job.DeviceId.Value, report, StringComparison.Ordinal);
        Assert.Contains("cancelled. committed findings", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test.report-a-collected.value", report, StringComparison.Ordinal);
        Assert.Contains("test.report-b-unavailable.value: Unknown", report, StringComparison.Ordinal);
        Assert.Contains("fixture-report-a-collected", report, StringComparison.Ordinal);
        Assert.Contains("fixture-report-b-unavailable", report, StringComparison.Ordinal);
        Assert.Contains("fixture-report-d-unrun: Not run", report, StringComparison.Ordinal);
        Assert.Contains("not available", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(manual.OperatorId.Value, report, StringComparison.Ordinal);
        Assert.Contains(KeyboardCase, report, StringComparison.Ordinal);
        Assert.Contains("2026-10-01T16:00:00", report, StringComparison.Ordinal);
        Assert.Contains("Readiness: Unknown", report, StringComparison.Ordinal);
        Assert.DoesNotContain(FixtureNotes, report, StringComparison.Ordinal);
        Assert.DoesNotContain("assessment.start", report, StringComparison.Ordinal);

        await using var reopened = OpenWorkspace(disk, secondOutput.Store(), providers);
        await reopened.OpenJobAsync(jobId.Value);
        await reopened.GenerateReportAsync();
        await reopened.OpenReportAsync();
        Assert.Equal(report, reopened.ReportPreview);
        Assert.Equal(File.ReadAllBytes(firstOutput.SingleReport()), File.ReadAllBytes(secondOutput.SingleReport()));
    }

    [Fact]
    public async Task ANameCollisionDoesNotReplaceThePublishedReportOrItsOpenCapability()
    {
        using var disk = new AuditTestStore();
        using var output = new ReportTestDirectory();
        var token = Guid.Parse("7762a223-e2d5-41a9-89f3-55374bbb62e5");
        var reportStore = new WorkshopReportStore(output.ReportRoot, () => token);
        await using var workspace = OpenWorkspace(disk, reportStore);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.GenerateReportAsync();
        var published = Assert.IsType<WorkshopReportArtifact>(workspace.ReportArtifact);
        var file = output.SingleReport();
        var originalBytes = File.ReadAllBytes(file);
        Assert.DoesNotContain(ReviewerCode, File.ReadAllText(file), StringComparison.Ordinal);

        await AppendManualResult(disk, new JobId(workspace.JobId));
        ShowAuditWindow(workspace, (window, view) =>
        {
            var before = workspace.ReportStatus;
            Invoke(window, Named<Button>(view, "GenerateReportButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.ReportStatus != before, () => workspace.ReportStatus);
            Assert.Contains("already uses that filename", workspace.ReportStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("existing output", workspace.ReportStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Same(published, workspace.ReportArtifact);
            Assert.Equal(originalBytes, File.ReadAllBytes(file));
            Assert.Equal(file, output.SingleReport());

            Invoke(window, Named<Button>(view, "OpenReportButton"));
            PumpUntil(window, () => workspace.ReportPreview.Length > 0 && !workspace.IsBusy, () => workspace.ReportStatus);
            Assert.Equal(File.ReadAllText(file), workspace.ReportPreview);
            AssertReportAnnouncement(view, workspace);
        });
    }

    [Fact]
    public async Task ACapacityWriteRefusalPreservesAnEarlierReportAndAnnouncesRecovery()
    {
        using var disk = new AuditTestStore();
        using var output = new ReportTestDirectory();
        await using var workspace = OpenWorkspace(disk, output.Store());
        await workspace.CreateJobAsync();
        await workspace.GenerateReportAsync();
        var earlier = Assert.IsType<WorkshopReportArtifact>(workspace.ReportArtifact);
        var file = output.SingleReport();
        var bytes = File.ReadAllBytes(file);

        // The bounded report folder is simulated with small regular files in this test's
        // own root. This exercises write refusal without altering host disk capacity.
        for (int index = 0; index < WorkshopReportStore.MaximumRetainedFiles - 1; index++)
        {
            File.WriteAllText(Path.Combine(output.ReportRoot, $"occupied-{index}.txt"), "fixture");
        }

        ShowAuditWindow(workspace, (window, view) =>
        {
            var before = workspace.ReportStatus;
            Invoke(window, Named<Button>(view, "GenerateReportButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.ReportStatus != before, () => workspace.ReportStatus);
            Assert.Contains("storage limits", workspace.ReportStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Same(earlier, workspace.ReportArtifact);
            Assert.True(workspace.CanOpenReport);
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Equal(WorkshopReportStore.MaximumRetainedFiles,
                Directory.EnumerateFiles(output.ReportRoot, "*.txt").Count());
            AssertReportAnnouncement(view, workspace);
        });
    }

    [Fact]
    public async Task AnUnavailableReportDestinationLeavesExistingDataIntactAndDoesNotEnableOpen()
    {
        using var disk = new AuditTestStore();
        using var output = new ReportTestDirectory();
        var occupied = Path.Combine(output.Root, "occupied");
        File.WriteAllText(occupied, "existing workshop file");
        await using var workspace = OpenWorkspace(disk, new WorkshopReportStore(occupied));
        ShowAuditWindow(workspace, (window, view) =>
        {
            var generate = Named<Button>(view, "GenerateReportButton");
            var open = Named<Button>(view, "OpenReportButton");
            Assert.False(generate.IsEnabled);
            Assert.False(open.IsEnabled);
            Invoke(window, Named<Button>(view, "CreateJobButton"));
            PumpUntil(window, () => workspace.CanGenerateReport, () => workspace.Status);
            Invoke(window, generate);
            PumpUntil(window, () => !workspace.IsBusy, () => workspace.ReportStatus);
            Assert.Contains("report destination is unavailable", workspace.ReportStatus, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(occupied, workspace.ReportStatus, StringComparison.Ordinal);
            Assert.False(open.IsEnabled);
            Assert.Null(workspace.ReportArtifact);
            Assert.Equal("existing workshop file", File.ReadAllText(occupied));
            AssertReportAnnouncement(view, workspace);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningMissingOrAlteredPublishedDataShowsARecoveryMessage(bool tampered)
    {
        using var disk = new AuditTestStore();
        using var output = new ReportTestDirectory();
        await using var workspace = OpenWorkspace(disk, output.Store());
        await workspace.CreateJobAsync();
        await workspace.GenerateReportAsync();
        Assert.NotNull(workspace.ReportArtifact);
        var file = output.SingleReport();
        const string AlteredText = "untrusted replacement in an isolated fixture";
        if (tampered)
        {
            File.WriteAllText(file, AlteredText);
        }
        else
        {
            File.Delete(file);
        }

        ShowAuditWindow(workspace, (window, view) =>
        {
            var before = workspace.ReportStatus;
            Invoke(window, Named<Button>(view, "OpenReportButton"));
            PumpUntil(window, () => !workspace.IsBusy && workspace.ReportStatus != before, () => workspace.ReportStatus);
            var explanation = tampered ? "saved report has changed" : "saved report could not be opened";
            Assert.Contains(explanation, workspace.ReportStatus, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(file, workspace.ReportStatus, StringComparison.Ordinal);
            Assert.Empty(workspace.ReportPreview);
            Assert.DoesNotContain(AlteredText, Named<TextBox>(view, "WorkshopReportPreview").Text, StringComparison.Ordinal);
            AssertReportAnnouncement(view, workspace);
        });
    }

    private static AuditWorkspace OpenWorkspace(AuditTestStore disk, WorkshopReportStore store,
        IReadOnlyList<IInventoryProvider>? providers = null) =>
        new(async cancellationToken => await disk.OpenSqliteAsync(cancellationToken).ConfigureAwait(false),
            providers ?? [AuditTestProvider.Available("report-no-collection")], new ReportClock(), store);

    private static async Task<HumanTestEvidence> AppendManualResult(AuditTestStore disk, JobId jobId)
    {
        await using var durable = await disk.OpenSqliteAsync();
        var service = new ManualTestService(durable, new ReportClock());
        return await service.RecordAsync(jobId, KeyboardCase, HumanTestResult.NotAvailable,
            new OperatorId(ReviewerCode), FixtureNotes,
            ManualTestUnavailableReason.EquipmentMissing);
    }

    private static void ShowAuditWindow(AuditWorkspace workspace, Action<Window, FrameworkElement> observe)
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Handover);
                ShellTestHost.Drain(window);
                observe(window, Named<FrameworkElement>(window, "AuditView"));
            }
            finally { CloseWindow(window); }
        });
    }

    private static void AssertReportAnnouncement(FrameworkElement view, AuditWorkspace workspace)
    {
        var peer = new TextBlockAutomationPeer(Named<TextBlock>(view, "ReportMessage"));
        Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
        Assert.Equal(workspace.ReportStatus, peer.GetName());
    }

    private sealed class ReportClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
    }

    private sealed class ReportTestDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ThirdLife-TL0115-");

        public string Root => _directory.FullName;

        public string ReportRoot => Path.Combine(Root, "reports");

        public WorkshopReportStore Store() => new(ReportRoot);

        public string SingleReport() => Assert.Single(Directory.EnumerateFiles(ReportRoot, "*.txt"));

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
