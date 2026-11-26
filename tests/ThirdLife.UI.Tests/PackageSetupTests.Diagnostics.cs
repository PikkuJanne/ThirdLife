using System.IO;
using System.Text;
using System.Text.Json;
using ThirdLife.Actions.Journal;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Packages;
using ThirdLife.Reports.WorkshopReport;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    [Fact]
    public async Task PackagePreparationDeadlineStaysInReviewWithoutAnInstallerStart()
    {
        using var disk = new AuditTestStore();
        var runtime = new InterruptedPackageRuntime();
        var service = new PilotPackageService(runtime, preparationTimeout: TimeSpan.FromSeconds(1));
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        await using var workspace = OpenWorkspace(disk, reports, service);
        var jobId = await ApproveInitialPackagePlanAsync(workspace);
        runtime.NextAcquisition = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new FixtureArtifact();
        };
        await workspace.InstallPackageAsync();
        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        AssertInterruptedAttempt(saved, JournalState.RequiresReview, JournalResult.PackageDownloadTimedOut, JournalCheckpoint.Reconcile);
        workspace.CloseJob();
        await workspace.OpenJobAsync(jobId.Value);
        Assert.Contains("reached its time limit before dispatch", workspace.PackageHistoryText, StringComparison.Ordinal);
        Assert.True(workspace.CanCheckPackageRecovery);
        await workspace.CheckPackageRecoveryAsync();
        Assert.Equal("The approved version was not found. Keep this job in review.", workspace.PackageRecoveryStatus);
        Assert.Equal(0, runtime.InstallCalls);
        Assert.False(workspace.CanInstallPackage);
    }

    [Fact]
    [Trait("Category", "Extended")]
    public async Task PackageDiagnosticsDiscardOversizedMalformedBackendDetails()
    {
        using var disk = new AuditTestStore();
        var runtime = new FixtureInstaller(PackageInstallStatus.Applied, register: false);
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var events = new List<StructuredDiagnosticEvent>();
        await using var workspace = OpenWorkspace(disk, reports, service, appendPackageDiagnostic: (diagnostic, _) =>
        {
            events.Add(diagnostic);
            return Task.CompletedTask;
        });
        var jobId = await ApproveInitialPackagePlanAsync(workspace);
        Assert.Empty(events);

        string[] unsafeValues = ["fixture-user", "fixture-private-file", "fixture-ssid", "192.0.2.15", "fixture-serial", "download.invalid", "fixture-token"];
        var malformed = $"C:\\Users\\{unsafeValues[0]}\\{unsafeValues[1]} {unsafeValues[2]} {unsafeValues[3]} {unsafeValues[4]} " +
            $"https://{unsafeValues[5]}/x?token={unsafeValues[6]} \u0000\u202e\uD800 =HYPERLINK(A1) " + new string('x', 1024 * 1024);
        runtime.Acquiring = () => throw new IOException(malformed);
        await workspace.InstallPackageAsync();

        var saved = await ReadRecoveryHistoryAsync(disk, jobId);
        var outcome = saved.Entries[^1];
        Assert.Equal(JournalResult.PackageDownloadUnavailable, outcome.Result);
        Assert.Equal(JournalState.Failed, outcome.State);
        Assert.Equal(0, runtime.InstallCalls);
        Assert.False(workspace.CanInstallPackage);
        var execute = Assert.Single(events);
        var log = execute.GetUtf8Json();
        Assert.InRange(log.Length, 1, 1024);
        using var json = JsonDocument.Parse(log);
        Assert.Equal("package_download_unavailable", json.RootElement.GetProperty("result_code").GetString());
        Assert.Equal(outcome.CorrelationId.ToString("N"), json.RootElement.GetProperty("correlation_id").GetString());
        var projected = Encoding.UTF8.GetString(PackageDiagnostic.ProjectForSupport(execute).GetUtf8Json());
        Assert.Equal("""
            {"result_code":"package_download_unavailable","component_id":"packages","operation_type":"install_package","sanitized_error_category":"package_download","retryable":false}
            """, projected);

        workspace.CloseJob();
        await workspace.OpenJobAsync(jobId.Value);
        await workspace.GenerateReportAsync();
        await workspace.OpenReportAsync();
        Assert.Contains("could not be downloaded or staged", workspace.ReportPreview, StringComparison.Ordinal);
        var localText = Encoding.UTF8.GetString(log);
        var journal = JsonSerializer.Serialize(saved.Entries);
        foreach (var value in unsafeValues)
        {
            Assert.DoesNotContain(value, localText, StringComparison.Ordinal);
            Assert.DoesNotContain(value, projected, StringComparison.Ordinal);
            Assert.DoesNotContain(value, workspace.PackageHistoryText, StringComparison.Ordinal);
            Assert.DoesNotContain(value, workspace.ReportPreview, StringComparison.Ordinal);
            Assert.DoesNotContain(value, journal, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StalledDiagnosticStorageDoesNotHideTheSavedPackageOutcome()
    {
        using var disk = new AuditTestStore();
        var runtime = new FixtureInstaller(PackageInstallStatus.RestartRequired, register: false);
        var service = new PilotPackageService(runtime);
        var reports = new WorkshopReportStore(Path.Combine(disk.Root, "reports"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var workspace = OpenWorkspace(disk, reports, service, appendPackageDiagnostic: (_, _) => release.Task);
        var jobId = await ApproveInitialPackagePlanAsync(workspace);
        try
        {
            await workspace.InstallPackageAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Contains("a restart is required", workspace.PlanReviewStatus, StringComparison.Ordinal);
            Assert.Contains("Local diagnostic storage was not confirmed", workspace.PackageSourceStatus, StringComparison.Ordinal);
            Assert.True(workspace.CanCloseJob);
            Assert.False(workspace.CanInstallPackage);
            Assert.Equal(1, runtime.InstallCalls);
            Assert.Equal(0, runtime.ProbeCalls);
            var saved = await ReadRecoveryHistoryAsync(disk, jobId);
            Assert.Equal(JournalState.Applied, saved.Entries[^1].State);
            Assert.Equal(JournalCheckpoint.RestartRequired, saved.Entries[^1].Checkpoint);
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
