using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Verification;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    [Fact]
    public async Task ColdBootCheckpointRequiresTheNextOrderlyBootAndRetainsAFailedLaunch()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        uint boot = 10;
        ColdBootObservation? evidence = null;
        ColdBootCheckpoint request;
        MaintenanceCheckpoint boundary;
        JournalPlanSnapshot plan;
        await using (var before = await Open())
        {
            (boundary, plan) = await ReachColdBootBoundaryAsync(before, clock, "cold-boot-observation", () => boot++);
            request = await before.RecordColdBootCheckpointAsync(boundary);
        }

        await using var reopened = await Open();
        Assert.Equal(new(request, ColdBootVerificationState.AwaitingShutdown),
            await reopened.LoadColdBootVerificationAsync(plan.Key.JobId));
        Assert.Equal(request, await reopened.RecordColdBootCheckpointAsync(boundary));
        clock.Advance(TimeSpan.FromMinutes(1));
        boot++;
        var currentEvidence = new ColdBootObservation(boot, clock.GetUtcNow(), ColdBootKind.OrderlyPowerOff);
        var result = await CreateColdBootProfileRecordAsync(reopened, clock, boundary, plan, request.Id);
        foreach (var unusable in new ColdBootObservation?[]
        {
            null,
            currentEvidence with { Kind = ColdBootKind.Restart },
            currentEvidence with { Kind = ColdBootKind.HybridOrResume },
            currentEvidence with { Kind = ColdBootKind.Ambiguous },
            currentEvidence with { BootSession = request.RequestedBootSession },
            currentEvidence with { StartedAtUtc = request.RequestedAtUtc },
            currentEvidence with { StartedAtUtc = clock.GetUtcNow().AddMinutes(1) },
        })
        {
            evidence = unusable;
            Assert.Equal(ColdBootVerificationState.AwaitingEvidence,
                (await reopened.LoadColdBootVerificationAsync(plan.Key.JobId))?.State);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.SaveProfileVerificationAsync(result));
        }
        Assert.Null(await reopened.LoadProfileVerificationAsync(plan.Key.JobId));

        evidence = currentEvidence;
        Assert.Equal(new(request, ColdBootVerificationState.ReadyToVerify, currentEvidence),
            await reopened.LoadColdBootVerificationAsync(plan.Key.JobId));
        await reopened.SaveProfileVerificationAsync(result);
        var saved = Assert.IsType<SavedProfileVerification>(await reopened.LoadProfileVerificationAsync(plan.Key.JobId));
        Assert.True(saved.IsCurrent);
        Assert.Equal(currentEvidence, saved.Record.ColdBootObservation);
        Assert.Equal(ProfileVerificationState.Failed, Assert.Single(saved.Record.Result.Capabilities).LaunchProbe);
        Assert.False(saved.HasPassedPostBootVerification);

        boot++;
        Assert.Equal(ColdBootVerificationState.Historical, (await reopened.LoadColdBootVerificationAsync(plan.Key.JobId))?.State);
        Assert.False((await reopened.LoadProfileVerificationAsync(plan.Key.JobId))?.IsCurrent);

        Task<SqliteJobStore> Open() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => boot, (_, _) => evidence);
    }

    [Fact]
    public async Task ColdBootCheckpointExpiresAndFollowsTheExactReviewedJob()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        uint boot = 10;
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => boot,
            (session, requested) => new(session, requested.AddSeconds(30), ColdBootKind.OrderlyPowerOff));
        var (boundary, plan) = await ReachColdBootBoundaryAsync(store, clock, "cold-boot-original", () => boot++);
        var expired = await store.RecordColdBootCheckpointAsync(boundary);
        clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(new(expired, ColdBootVerificationState.Historical), await store.LoadColdBootVerificationAsync(plan.Key.JobId));
        var renewed = await store.RecordColdBootCheckpointAsync(boundary);
        Assert.NotEqual(expired.Id, renewed.Id);

        var otherPlan = await SaveFullMaintenancePlanAsync(store, "cold-boot-other");
        Assert.NotEqual(plan.Key.JobId, otherPlan.Key.JobId);
        Assert.Null(await store.LoadColdBootVerificationAsync(otherPlan.Key.JobId));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.RecordColdBootCheckpointAsync(boundary with { Plan = otherPlan.Key }));

        boot++;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ColdBootVerificationState.ReadyToVerify, (await store.LoadColdBootVerificationAsync(plan.Key.JobId))?.State);
        var result = await CreateColdBootProfileRecordAsync(store, clock, boundary, plan, renewed.Id);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.SaveProfileVerificationAsync(result with { ColdBootCheckpointId = expired.Id }));

        await store.AppendEvidenceAsync(new JobEvidenceBatch(plan.Key.JobId,
            observations: [PersistenceTestData.CreateObservation("cold-boot-review-changed")]));
        Assert.Equal(new(renewed, ColdBootVerificationState.Historical), await store.LoadColdBootVerificationAsync(plan.Key.JobId));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.SaveProfileVerificationAsync(result));
        Assert.Null(await store.LoadProfileVerificationAsync(plan.Key.JobId));
    }

    [Fact]
    public async Task ColdBootCheckpointAuthenticatorCannotFollowACopiedFixtureDatabase()
    {
        using var original = new PersistenceTestWorkspace();
        using var destination = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        uint boot = 10;
        const string suffix = "cold-boot-copy";
        string originalPath;
        string destinationPath;
        MaintenanceCheckpoint boundary;
        JournalPlanSnapshot plan;
        ColdBootCheckpoint request;
        await using (var source = await Open(original))
        {
            (boundary, plan) = await ReachColdBootBoundaryAsync(source, clock, suffix, () => boot++);
            request = await source.RecordColdBootCheckpointAsync(boundary);
            originalPath = source.DatabasePath;
        }

        await using (var empty = await Open(destination))
        {
            var (job, _) = await CreateReviewedJobAsync(empty, suffix);
            Assert.Equal(plan.Key.JobId, job.Job.JobId);
            destinationPath = empty.DatabasePath;
        }
        File.Copy(originalPath, destinationPath, overwrite: true);
        boot++;
        clock.Advance(TimeSpan.FromMinutes(1));

        await using var retained = await Open(original);
        await using var copied = await Open(destination);
        Assert.Equal(boundary, await copied.LoadMaintenanceAsync(plan.Key.JobId));
        Assert.Equal(ColdBootVerificationState.ReadyToVerify, (await retained.LoadColdBootVerificationAsync(plan.Key.JobId))?.State);
        Assert.Equal(new(request, ColdBootVerificationState.Historical), await copied.LoadColdBootVerificationAsync(plan.Key.JobId));
        var result = await CreateColdBootProfileRecordAsync(copied, clock, boundary, plan, request.Id);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => copied.SaveProfileVerificationAsync(result));
        Assert.Null(await copied.LoadProfileVerificationAsync(plan.Key.JobId));

        Task<SqliteJobStore> Open(PersistenceTestWorkspace workspace) => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => boot,
            (session, requested) => new(session, requested.AddSeconds(30), ColdBootKind.OrderlyPowerOff));
    }

    [Fact]
    public async Task ColdBootMigrationKeepsTheSchema13ProfileResultAndEnablesNewCheckpoint()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        uint boot = 10;
        ProfileVerificationRecord expected;
        await using (var prior = await Open(13))
        {
            var (boundary, plan) = await ReachColdBootBoundaryAsync(prior, clock, "cold-boot-migration", () => boot++);
            expected = await CreateColdBootProfileRecordAsync(prior, clock, boundary, plan, checkpointId: null);
            await prior.SaveProfileVerificationAsync(expected);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => prior.RecordColdBootCheckpointAsync(boundary));
        }

        await using (var migrated = await Open(14))
        {
            var job = expected.Checkpoint.Plan.JobId;
            var saved = Assert.IsType<SavedProfileVerification>(await migrated.LoadProfileVerificationAsync(job));
            Assert.True(saved.IsCurrent);
            Assert.Equal(expected.Checkpoint, saved.Record.Checkpoint);
            Assert.Equal(expected.ScanId, saved.Record.ScanId);
            Assert.Equal(expected.PackageJournalEntryId, saved.Record.PackageJournalEntryId);
            Assert.Equal(expected.Result.Capabilities.AsEnumerable(), saved.Record.Result.Capabilities.AsEnumerable());
            Assert.True(saved.Record.Result.ProfileChecksPassed);
            Assert.False(saved.HasPassedPostBootVerification);
            Assert.Null(saved.Record.ColdBootCheckpointId);
            Assert.Null(saved.Record.ColdBootObservation);
            Assert.Null(await migrated.LoadColdBootVerificationAsync(job));
            var request = await migrated.RecordColdBootCheckpointAsync(expected.Checkpoint);
            Assert.Equal(new(request, ColdBootVerificationState.AwaitingShutdown), await migrated.LoadColdBootVerificationAsync(job));
        }

        await Assert.ThrowsAsync<JobStoreVersionException>(() => Open(13));

        Task<SqliteJobStore> Open(int schema) => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, schema, clock, null,
            () => 10_000, () => boot, (_, _) => null);
    }

    private static async Task<(MaintenanceCheckpoint Boundary, JournalPlanSnapshot Plan)> ReachColdBootBoundaryAsync(
        SqliteJobStore store, FixedTimeProvider clock, string suffix, Action restart)
    {
        var plan = await SaveFullMaintenancePlanAsync(store, suffix);
        var approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
        await store.RecordMaintenanceApprovalAsync(approval, ScheduledApprovalEntries(plan, approval));
        var checkpoint = Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(plan.Key.JobId));
        checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, (await ScanForColdBootAsync(store, clock, plan)).Id);

        var action = Assert.IsType<InstallPackageActionRequest>(Assert.Single(plan.Actions).Request);
        var started = ScheduledPackageStart(approval, action.ActionId, clock.GetUtcNow());
        var applied = ScheduledPackageApplied(started, clock.GetUtcNow());
        await store.AppendJournalEntryAsync(started);
        await store.AppendJournalEntryAsync(applied);
        await store.AppendJournalEntryAsync(ScheduledPackageVerified(applied, clock.GetUtcNow()));
        checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, (await ScanForColdBootAsync(store, clock, plan)).Id);
        ImmutableArray<MaintenancePackageObservation> observed = [new(action.ActionId, action.ExactVersion)];
        checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, (await ScanForColdBootAsync(store, clock, plan)).Id, observed);
        restart();
        checkpoint = await store.AdvanceMaintenanceAsync(checkpoint, (await ScanForColdBootAsync(store, clock, plan)).Id, observed);
        Assert.Equal(MaintenancePhase.FinalVerification, checkpoint.Phase);
        return (checkpoint, plan);
    }

    private static async Task<ProfileVerificationRecord> CreateColdBootProfileRecordAsync(SqliteJobStore store, FixedTimeProvider clock,
        MaintenanceCheckpoint boundary, JournalPlanSnapshot plan, Guid? checkpointId)
    {
        var start = clock.GetUtcNow();
        var scan = await ScanForColdBootAsync(store, clock, plan);
        var action = Assert.Single(plan.Actions);
        var request = Assert.IsType<InstallPackageActionRequest>(action.Request);
        var history = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(plan.Key));
        var result = new ProfileVerificationResult(start, clock.GetUtcNow(), false, plan.ProfileSha256, new string('b', 64),
            Enum.GetValues<ProfileCheckKind>().Select(kind => new ProfileVerificationCheck(kind, ProfileVerificationState.Passed,
                kind is not (ProfileCheckKind.SystemVolumeCapacity or ProfileCheckKind.PhysicalStorageEvidence or ProfileCheckKind.AudioOutput or
                    ProfileCheckKind.Camera or ProfileCheckKind.Microphone))).ToImmutableArray(),
            [new(action.CapabilityId, true, ProfileVerificationState.Passed, request.ExactVersion, request.ExactVersion, action.ActionId,
                ProfilePackageStatus.ExactVersion, checkpointId.HasValue ? ProfileVerificationState.Failed : null)],
            [new(new("POL-OS-001"), PolicyRuleState.Met, PolicyRuleReason.ConditionMet, true)],
            26100, 64_000_000_000, 128_000_000_000, 0, 0, WindowsUpdateDisposition.Current, false);
        return new(boundary, scan.Id, history.Entries[^1].EntryId, history.Entries.Length, result, checkpointId);
    }

    private static Task<WindowsUpdateEntry> ScanForColdBootAsync(SqliteJobStore store, FixedTimeProvider clock, JournalPlanSnapshot plan)
    {
        clock.Advance(TimeSpan.FromSeconds(1));
        var scanner = new WindowsUpdateWorkflowTests.FixtureAgent { Scan = new(WindowsUpdateStatus.Succeeded, [], false) };
        return new WindowsUpdateWorkflow(store, scanner, clock).ScanAsync(plan.Key.JobId, plan.GateDecisionId);
    }
}
