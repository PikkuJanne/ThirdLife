using System.IO;
using System.Reflection;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Windows;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

public sealed partial class PackageSetupTests
{
    // The host performs the orderly Windows restart and cold start on the existing guest.
    // This case uses the native boot reader and a real store, with controlled provider and package outcomes.
    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public async Task GuestColdBootReopensTheApprovedJobAndRunsFreshVerification()
    {
        Assert.Equal("THIRDLIFE-W11", Environment.MachineName, ignoreCase: true);
        Assert.True(Guid.TryParseExact(Environment.GetEnvironmentVariable("THIRDLIFE_COLD_BOOT_RUN"), "N", out var run));
        var stage = Environment.GetEnvironmentVariable("THIRDLIFE_COLD_BOOT_STAGE");
        Assert.True(stage is "prepare" or "request" or "same-boot" or "verify" or "reopen");
        var directory = Path.Combine(Path.GetTempPath(), "ThirdLife-TL0509-" + run.ToString("N"));
        var bindingPath = Path.Combine(directory, "binding.txt");
        var coldBootPath = Path.Combine(directory, "cold-checkpoint.txt");
        var clock = new FixedClock { Current = DateTimeOffset.UtcNow };
        var runtime = new GuestColdBootPackage(stage != "prepare");
        var agent = new MaintenanceScanFixture();
        var reports = new WorkshopReportStore(Path.Combine(directory, "reports"));

        Task<SqliteJobStore> OpenStore(CancellationToken token = default) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(directory, "store"), SqliteJobStore.CurrentSchemaVersion, clock, null,
            WindowsBootUptime.Read, WindowsBootUptime.ReadBootSession, WindowsColdBootEvidence.Read, token);

        await using var workspace = new AuditWorkspace(async token => await OpenStore(token), [OsSample(), SystemSample()],
            clock, reports, packageService: new PilotPackageService(runtime), observePreparationResources: ReadyPreparationResources);
        typeof(AuditWorkspace).GetProperty("UpdateAgent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(workspace, agent);
        if (stage == "prepare")
        {
            Directory.CreateDirectory(directory);
            await PrepareInitialPackagePlanAsync(workspace);
            workspace.IncludeFullMaintenance = true;
            await workspace.BuildPlanReviewAsync();
            workspace.PackageOperatorCode = "fixture-operator";
            await workspace.ApprovePlanReviewAsync();
            Assert.True(workspace.CanContinueMaintenance, workspace.OperationError);
            await workspace.ContinueMaintenanceAsync();
            await workspace.BuildPlanReviewAsync();
            Assert.True(workspace.CanInstallPackage, workspace.PlanReviewStatus + workspace.PackageSourceStatus);
            await workspace.InstallPackageAsync();
            Assert.Equal(1, runtime.InstallCalls);
            Assert.Equal(1, runtime.ProbeCalls);
            await workspace.ContinueMaintenanceAsync();
            await workspace.ContinueMaintenanceAsync();
            Assert.Contains("Final restart required", workspace.MaintenancePhaseStatus, StringComparison.Ordinal);
            await using var initial = await OpenStore();
            var boundary = Assert.IsType<MaintenanceCheckpoint>(await initial.LoadMaintenanceAsync(workspace.Job!.Job.JobId));
            Assert.Equal(MaintenancePhase.FinalRestart, boundary.Phase);
            await File.WriteAllLinesAsync(bindingPath, GuestJobBinding(boundary));
            return;
        }

        var binding = await File.ReadAllLinesAsync(bindingPath);
        await workspace.OpenJobAsync(binding[0]);
        var job = Assert.IsType<JobId>(workspace.Job?.Job.JobId);
        if (stage == "request")
        {
            Assert.True(workspace.CanContinueMaintenance, workspace.OperationError + workspace.MaintenancePhaseStatus);
            await workspace.ContinueMaintenanceAsync();
            Assert.True(workspace.CanRecordColdBootCheckpoint, workspace.OperationError + workspace.ColdBootVerificationStatus);
            await workspace.RecordColdBootCheckpointAsync();
        }

        await using var store = await OpenStore();
        var handoff = Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(job));
        Assert.Equal(MaintenancePhase.FinalVerification, handoff.Phase);
        Assert.Equal(binding, GuestJobBinding(handoff));
        var history = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(handoff.Plan));
        Assert.Equal(handoff.ApprovalId, Assert.Single(history.Approvals).ApprovalId);
        Assert.Single(history.Entries, entry => entry.State == JournalState.Started);
        var saved = Assert.IsType<SavedColdBootVerification>(await store.LoadColdBootVerificationAsync(job));
        if (stage is "request" or "same-boot")
        {
            Assert.Equal(ColdBootVerificationState.AwaitingShutdown, saved.State);
            Assert.False(workspace.CanRunPostBootVerification);
            var checkpointId = saved.Checkpoint.Id.ToString("D");
            if (stage == "request")
            {
                await File.WriteAllTextAsync(coldBootPath, checkpointId);
            }
            else
            {
                Assert.Equal(checkpointId, await File.ReadAllTextAsync(coldBootPath));
            }

            Assert.Equal(0, runtime.InstallCalls);
            Assert.Equal(0, runtime.ProbeCalls);
            return;
        }

        Assert.Equal(saved.Checkpoint.Id.ToString("D"), await File.ReadAllTextAsync(coldBootPath));
        Assert.True(saved.Observation is { } observation && observation.BootSession > handoff.BootSession);
        if (stage == "verify")
        {
            Assert.Equal(ColdBootVerificationState.ReadyToVerify, saved.State);
            Assert.True(workspace.CanRunPostBootVerification, workspace.OperationError + workspace.ColdBootVerificationStatus);
            await workspace.RunPostBootVerificationAsync();
            Assert.Equal(1, agent.Scans);
            Assert.Equal(1, runtime.ProbeCalls);
        }

        var verification = Assert.IsType<SavedProfileVerification>(await store.LoadProfileVerificationAsync(job));
        Assert.True(verification.IsCurrent, workspace.OperationError + workspace.ColdBootVerificationStatus);
        Assert.Equal(saved.Checkpoint.Id, verification.Record.ColdBootCheckpointId);
        Assert.Equal(handoff, verification.Record.Checkpoint);
        Assert.False(verification.Record.Result.ProfileChecksPassed);
        Assert.Contains("orderly shutdown and full startup", workspace.ColdBootVerificationDetails, StringComparison.Ordinal);
        Assert.Contains(PilotPackageService.Identity.ExactVersion, workspace.ProfileVerificationDetails, StringComparison.Ordinal);
        Assert.Equal(0, runtime.InstallCalls);
        Assert.Equal(0, runtime.AcquireCalls);
        if (stage == "reopen")
        {
            Assert.Equal(0, agent.Scans);
            Assert.Equal(0, runtime.ProbeCalls);
            await workspace.GenerateReportAsync();
            await workspace.OpenReportAsync();
            Assert.Contains(PilotPackageService.Identity.ExactVersion, workspace.ReportPreview, StringComparison.Ordinal);
        }
    }

    private static string[] GuestJobBinding(MaintenanceCheckpoint checkpoint) =>
        [checkpoint.Plan.JobId.Value, checkpoint.Plan.PlanSha256, checkpoint.Plan.ImpactSha256, checkpoint.ApprovalId.ToString("D")];

    private sealed class GuestColdBootPackage(bool registered) : IPilotPackageRuntime
    {
        public int AcquireCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public int ProbeCalls { get; private set; }

        public PackageObservation Inspect() => registered
            ? new(PackageObservationStatus.Installed, PilotPackageService.Identity.ExactVersion)
            : new(PackageObservationStatus.Absent);

        public Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCalls++;
            return Task.FromResult<IPackageArtifact>(new FixtureArtifact());
        }

        public Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallCalls++;
            registered = true;
            return Task.FromResult(new PackageInstallResult(PackageInstallStatus.Applied));
        }

        public Task<PackageVerificationStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCalls++;
            return Task.FromResult(PackageVerificationStatus.Passed);
        }
    }
}
