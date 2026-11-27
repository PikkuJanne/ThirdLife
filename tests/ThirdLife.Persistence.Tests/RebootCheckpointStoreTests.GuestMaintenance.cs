using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    // Invoke once per stage across two externally authorized, orderly restarts of the same guest.
    // Only the boot source and persistence are native. All package and update results are fixtures.
    [Fact]
    [Trait("Category", "Extended")]
    [Trait("Category", "ActiveMachine")]
    public async Task GuestMaintenanceResumesTheSameApprovedJobAcrossTwoWindowsRestarts()
    {
        Assert.Equal("THIRDLIFE-W11", Environment.MachineName, ignoreCase: true);
        Assert.True(Guid.TryParseExact(Environment.GetEnvironmentVariable("THIRDLIFE_MAINTENANCE_RUN"), "N", out var run));
        var stage = Environment.GetEnvironmentVariable("THIRDLIFE_MAINTENANCE_STAGE");
        Assert.True(stage is "prepare" or "packages" or "verify");
        var directory = Path.Combine(Path.GetTempPath(), "ThirdLife-TL0505-" + run.ToString("N"));
        var binding = Path.Combine(directory, "binding.txt");
        var clock = new FixedTimeProvider(IssueTime);
        var scanner = new WindowsUpdateWorkflowTests.FixtureAgent();
        const string suffix = "guest-maintenance";
        var job = PersistenceTestData.CreateJob("reboot-" + suffix).JobId;
        await using var store = await SqliteJobStore.OpenForTestingAsync(Path.Combine(directory, "store"),
            SqliteJobStore.CurrentSchemaVersion, clock, null);

        if (stage == "prepare")
        {
            var plan = await SaveFullMaintenancePlanAsync(store, suffix);
            var approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            await store.RecordMaintenanceApprovalAsync(approval, ScheduledApprovalEntries(plan, approval));
            await File.WriteAllLinesAsync(binding, [job.Value, plan.Key.PlanSha256, plan.Key.ImpactSha256, approval.ApprovalId.ToString("D")]);
        }

        var checkpoint = Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(job));
        var history = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(checkpoint.Plan));
        var approved = Assert.Single(history.Approvals);
        Assert.Equal(approved.ApprovalId, checkpoint.ApprovalId);
        Assert.Equal(new[] { job.Value, history.Plan.Key.PlanSha256, history.Plan.Key.ImpactSha256, approved.ApprovalId.ToString("D") },
            await File.ReadAllLinesAsync(binding));
        var package = Assert.IsType<InstallPackageActionRequest>(Assert.Single(history.Plan.Actions).Request);
        ImmutableArray<MaintenancePackageObservation> observations = [new(package.ActionId, package.ExactVersion)];
        var workflow = new WindowsUpdateWorkflow(store, scanner, clock);
        async Task<Guid> Scan(bool pending = false)
        {
            scanner.Scan = new(WindowsUpdateStatus.Succeeded, [], pending);
            return (await workflow.ScanAsync(job, history.Plan.GateDecisionId)).Id;
        }

        if (stage == "prepare")
        {
            Assert.Equal(MaintenancePhase.WindowsUpdates, checkpoint.Phase);
            await Scan(pending: true);
            var unchangedBoot = await Scan();
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AdvanceMaintenanceAsync(checkpoint, unchangedBoot));
        }
        else if (stage == "packages")
        {
            Assert.Equal(MaintenancePhase.WindowsUpdates, checkpoint.Phase);
            checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, await Scan());
            Assert.Equal(MaintenancePhase.Packages, checkpoint.Phase);
            Assert.True(checkpoint.BootSession > checkpoint.InitialBootSession);
            var started = ScheduledPackageStart(approved, package.ActionId, clock.GetUtcNow());
            var applied = ScheduledPackageApplied(started, clock.GetUtcNow());
            await store.AppendJournalEntryAsync(started);
            await store.AppendJournalEntryAsync(applied);
            await store.AppendJournalEntryAsync(ScheduledPackageVerified(applied, clock.GetUtcNow()));
            checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, await Scan());
            Assert.Equal(MaintenancePhase.PostPackageChecks, checkpoint.Phase);
            checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, await Scan(), observations);
            Assert.Equal(MaintenancePhase.FinalRestart, checkpoint.Phase);
            var unchangedBoot = await Scan();
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AdvanceMaintenanceAsync(checkpoint, unchangedBoot, observations));
        }
        else
        {
            Assert.Equal(MaintenancePhase.FinalRestart, checkpoint.Phase);
            var handoff = await store.AdvanceMaintenanceAsync(checkpoint, await Scan(), observations);
            Assert.True(handoff.BootSession > checkpoint.BootSession);
            Assert.Equal(MaintenancePhase.FinalVerification, handoff.Phase);
            Assert.Equal(JournalState.Verified, history.StateOf(package.ActionId));
            Assert.Single(history.Entries, entry => entry.State == JournalState.Started);
            Assert.Equal(handoff, await store.LoadMaintenanceAsync(job));
        }
        Assert.Empty(scanner.Mutations);
    }
}
