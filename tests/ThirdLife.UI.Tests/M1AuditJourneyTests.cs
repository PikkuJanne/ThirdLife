using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class M1AuditJourneyTests
{
    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task OperatorRouteReopensSavedFixtureAndPreviewsItsWorkshopReport()
    {
        Assert.Equal(StartupAccess.StandardUser, StartupSafety.CheckCurrentProcess());
        using var fixture = new M1AuditFixture();
        await using var workspace = fixture.CreateWorkspace();
        JourneyResult? result = null;

        ShellTestHost.Run(() =>
        {
            var window = fixture.CreateWindow(workspace);
            try
            {
                ShellTestHost.Show(window);
                Assert.Null(workspace.Job);
                Assert.True(ShellTestHost.Element<Button>(window, "OpenAuditWorkspaceButton").Focus());
                ShellTestHost.Invoke(window, "OpenAuditWorkspaceButton");

                var view = Named<FrameworkElement>(window, "AuditView");
                Assert.Same(Named<GroupBox>(view, "JobRegion"), Keyboard.FocusedElement);
                Assert.True(Named<TextBlock>(view, "FixtureNotice").IsVisible);
                var assessmentStart = Named<Button>(view, "StartAssessmentButton");
                Assert.False(assessmentStart.IsEnabled);

                var create = Named<Button>(view, "CreateJobButton");
                Assert.True(create.Focus());
                Invoke(window, create);
                PumpUntil(window, () => workspace.CanEditJob, () => workspace.Status);
                var job = Assert.IsType<StoredJob>(workspace.Job);
                Assert.Equal(job.Job.JobId.Value, Named<TextBox>(view, "CurrentJobId").Text);
                Assert.False(assessmentStart.IsEnabled);

                SaveFixtureIntake(window, view);
                PumpUntil(window, () => workspace.CanAssess, () => workspace.GateStatus);
                var intake = Assert.Single(workspace.Job!.SanitizationEvidence);
                Assert.Equal(ProvenanceKind.SyntheticFixture, intake.Metadata.Provenance.Kind);
                Assert.Equal(SanitizationState.Verified, intake.SanitizationState);
                Assert.True(SanitizationGate.Inspect(workspace.Job).AllowsAssessment);

                Assert.True(assessmentStart.Focus());
                Invoke(window, assessmentStart);
                PumpUntil(window, () => workspace.Assessment is not null && !workspace.IsBusy,
                    () => workspace.AssessmentStatus);
                var assessment = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
                AssertFixtureAssessment(assessment, workspace.Findings);
                var assessmentMessage = new TextBlockAutomationPeer(Named<TextBlock>(view, "AssessmentMessage"));
                Assert.Equal(AutomationLiveSetting.Polite, assessmentMessage.GetLiveSetting());
                Assert.Contains("needs review", assessmentMessage.GetName(), StringComparison.OrdinalIgnoreCase);

                SaveUnavailableManualSample(window, view);
                PumpUntil(window, () => workspace.Job?.HumanTests.Count == 1 && !workspace.IsBusy,
                    () => Named<TextBlock>(view, "ManualInputError").Text);
                var manual = Assert.Single(workspace.Job!.HumanTests);
                Assert.Equal(HumanTestResult.NotAvailable, manual.TestResult);
                Assert.Equal(ProvenanceKind.SyntheticFixture, manual.Metadata.Provenance.Kind);
                Assert.Equal(M1AuditFixture.KeyboardCase, manual.Details!.CaseId);
                Assert.Equal(M1AuditFixture.ManualNote, manual.Details.Notes);
                Assert.Equal("equipment_missing", manual.LimitationCode);

                Invoke(window, Named<Button>(view, "CloseJobButton"));
                Assert.Null(workspace.Job);
                Assert.Null(workspace.Assessment);
                Assert.False(assessmentStart.IsEnabled);
                var identifier = Named<TextBox>(view, "JobIdInput");
                Assert.Equal(job.Job.JobId.Value, identifier.Text);
                Assert.Same(identifier, Keyboard.FocusedElement);
                NextIs(window, Named<Button>(view, "OpenJobButton"));
                Invoke(window, Named<Button>(view, "OpenJobButton"));
                PumpUntil(window, () => workspace.CanEditJob && workspace.Job?.Job.JobId == job.Job.JobId,
                    () => workspace.Status);
                Assert.Equal(assessment.AssessmentId, workspace.Assessment!.AssessmentId);
                Assert.Equal(assessment.Observations.ToArray(), workspace.Assessment.Observations.ToArray());
                Assert.Equal(intake, Assert.Single(workspace.Job!.SanitizationEvidence));
                Assert.Equal(manual, Assert.Single(workspace.Job.HumanTests));

                result = GenerateAndOpenReport(window, view, workspace, job.Job.JobId, assessment.AssessmentId);
            }
            finally { CloseWindow(window); }
        });

        var completed = Assert.IsType<JourneyResult>(result);
        StoredJob saved;
        string databasePath;
        await using (var durable = await fixture.OpenSqliteAsync())
        {
            saved = Assert.IsType<StoredJob>(await durable.LoadJobAsync(completed.JobId));
            databasePath = durable.DatabasePath;
        }

        Assert.Equal(completed.AssessmentId, AssessmentOrchestrator.ReadLatest(saved)!.AssessmentId);
        Assert.Single(saved.HumanTests);
        Assert.Contains(completed.JobId.Value, completed.Preview, StringComparison.Ordinal);
        Assert.Contains(saved.Job.DeviceId.Value, completed.Preview, StringComparison.Ordinal);
        Assert.Contains("WORKSHOP_RESTRICTED", completed.Preview, StringComparison.Ordinal);
        Assert.Contains("synthetic fixture", completed.Preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Requires review", completed.Preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test.keyboard.value: Yes", completed.Preview, StringComparison.Ordinal);
        Assert.Contains("test.unavailable.value: Unknown", completed.Preview, StringComparison.Ordinal);
        Assert.Contains("fixture-failed", completed.Preview, StringComparison.Ordinal);
        Assert.Contains(M1AuditFixture.KeyboardCase, completed.Preview, StringComparison.Ordinal);
        Assert.Contains("equipment_missing", completed.Preview, StringComparison.Ordinal);
        Assert.Contains("Readiness: Unknown", completed.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain(M1AuditFixture.ManualNote, completed.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain(M1AuditFixture.PrivateFailureMarker, completed.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("assessment.start", completed.Preview, StringComparison.Ordinal);

        var artifact = Assert.Single(Directory.EnumerateFiles(fixture.ReportRoot, "*.txt"));
        Assert.Equal(completed.FileName, Path.GetFileName(artifact));
        Assert.Matches(@"^workshop-[0-9a-f]{32}\.txt$", completed.FileName);
        Assert.Equal(Encoding.UTF8.GetBytes(completed.Preview), File.ReadAllBytes(artifact));
        Assert.DoesNotContain(M1AuditFixture.OperatorCode, completed.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain(M1AuditFixture.MediaCode, completed.FileName, StringComparison.Ordinal);
        var database = new FileInfo(databasePath);
        Assert.InRange(database.Length, 1, 1024 * 1024);
        Assert.DoesNotContain(M1AuditFixture.PrivateFailureMarker,
            Encoding.UTF8.GetString(File.ReadAllBytes(database.FullName)), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(database.DirectoryName!, "*.log", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(fixture.ReportRoot, "*.log"));
        Assert.Empty(Directory.EnumerateFiles(fixture.ReportRoot, "*.tmp"));
    }

    private static void SaveFixtureIntake(Window window, FrameworkElement view)
    {
        var operatorCode = Named<TextBox>(view, "OperatorIdInput");
        Assert.True(operatorCode.Focus());
        operatorCode.Text = M1AuditFixture.OperatorCode;
        var result = Named<ComboBox>(view, "SanitizationResult");
        NextIs(window, result);
        result.SelectedValue = SanitizationState.Verified;
        Named<ComboBox>(view, "SanitizationMethod").SelectedValue = "clear";
        Named<DatePicker>(view, "SanitizationDate").SelectedDate = M1AuditFixture.FixtureDate.ToDateTime(TimeOnly.MinValue);
        Named<TextBox>(view, "SanitizationMediaId").Text = M1AuditFixture.MediaCode;
        Named<CheckBox>(view, "ExternalRecordConfirmed").IsChecked = true;
        Invoke(window, Named<Button>(view, "RecordSanitizationButton"));
    }

    private static void AssertFixtureAssessment(
        AssessmentSnapshot assessment,
        IReadOnlyList<AssessmentFinding> findings)
    {
        Assert.Equal(AssessmentOutcome.RequiresReview, assessment.Outcome);
        Assert.Equal(3, assessment.Providers.Count);
        Assert.Contains(assessment.Providers, static provider =>
            provider.ProviderId.Value == "fixture-keyboard" && provider.Outcome == ProviderRunOutcome.Completed);
        Assert.Contains(assessment.Providers, static provider =>
            provider.ProviderId.Value == "fixture-unavailable" && provider.Outcome == ProviderRunOutcome.Unavailable);
        Assert.Contains(assessment.Providers, static provider =>
            provider.ProviderId.Value == "fixture-failed" && provider.Outcome == ProviderRunOutcome.Failed);
        Assert.All(assessment.Observations, static item =>
            Assert.Equal(ProvenanceKind.SyntheticFixture, item.Metadata.Provenance.Kind));
        Assert.Contains(findings, static item =>
            item.Name == "test.keyboard.value" && item.Value == "Yes" && item.Evidence == "Synthetic fixture");
        Assert.Contains(findings, static item =>
            item.Name == "test.unavailable.value" && item.Value == "Not available" &&
            item.Evidence.Contains("Synthetic fixture", StringComparison.Ordinal));
        Assert.Contains(findings, static item => item.Provider == "fixture-failed" && item.Value == "Failed");
    }

    private static void SaveUnavailableManualSample(Window window, FrameworkElement view)
    {
        var check = Named<ComboBox>(view, "ManualCheck");
        Assert.True(check.Focus());
        check.SelectedItem = Assert.Single(ManualTestCatalog.Cases, static item => item.Id == M1AuditFixture.KeyboardCase);
        Assert.False(Named<ComboBoxItem>(view, "ManualPassOption").IsEnabled);
        Assert.False(Named<ComboBoxItem>(view, "ManualFailOption").IsEnabled);
        var outcome = Named<ComboBox>(view, "ManualOutcome");
        Assert.True(outcome.Focus());
        outcome.SelectedValue = HumanTestResult.NotAvailable;
        var reason = Named<ComboBox>(view, "ManualUnavailableReason");
        NextIs(window, reason);
        reason.SelectedValue = ManualTestUnavailableReason.EquipmentMissing;
        var notes = Named<TextBox>(view, "ManualNotes");
        NextIs(window, notes);
        notes.Text = M1AuditFixture.ManualNote;
        var save = Named<Button>(view, "SaveManualResultButton");
        NextIs(window, save);
        Invoke(window, save);
    }

    private static JourneyResult GenerateAndOpenReport(
        Window window, FrameworkElement view, AuditWorkspace workspace, JobId jobId, AssessmentId assessmentId)
    {
        var generate = Named<Button>(view, "GenerateReportButton");
        Assert.True(generate.Focus());
        Invoke(window, generate);
        PumpUntil(window, () => workspace.CanOpenReport, () => workspace.ReportStatus);
        var open = Named<Button>(view, "OpenReportButton");
        Assert.Same(open, Keyboard.FocusedElement);
        Invoke(window, open);
        PumpUntil(window, () => workspace.ReportPreview.Length > 0 && !workspace.IsBusy, () => workspace.ReportStatus);
        var preview = Named<TextBox>(view, "WorkshopReportPreview");
        Assert.Same(preview, Keyboard.FocusedElement);
        Assert.True(preview.IsReadOnly);
        Assert.Equal(workspace.ReportPreview, preview.Text);
        var notice = new TextBlockAutomationPeer(Named<TextBlock>(view, "ReportMessage"));
        Assert.Equal(AutomationLiveSetting.Polite, notice.GetLiveSetting());
        Assert.Contains("readiness is unknown", notice.GetName(), StringComparison.OrdinalIgnoreCase);
        var artifact = Assert.IsType<WorkshopReportArtifact>(workspace.ReportArtifact);
        Assert.Equal(jobId, artifact.JobId);
        return new JourneyResult(jobId, assessmentId, preview.Text, artifact.FileName);
    }

    private sealed record JourneyResult(JobId JobId, AssessmentId AssessmentId, string Preview, string FileName);
}
