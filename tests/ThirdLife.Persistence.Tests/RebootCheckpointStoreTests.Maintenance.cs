using System.Collections.Immutable;
using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Verification;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    [Theory]
    [InlineData(0, false, MaintenancePhase.WindowsUpdates, false)]
    [InlineData(1, true, MaintenancePhase.Packages, false)]
    [InlineData(2, false, MaintenancePhase.PostPackageChecks, false)]
    [InlineData(1, false, MaintenancePhase.FinalRestart, false)]
    [InlineData(0, true, MaintenancePhase.FinalVerification, false)]
    [InlineData(0, false, MaintenancePhase.WindowsUpdates, true)]
    public async Task FullMaintenanceReopensAtEveryBoundaryAndRequiresFreshStateAfterFinalRestart(
        int servicingRestarts, bool interruptedAfterCommit, MaintenancePhase unexpectedShutdownAt, bool historicalRecovery)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        ulong uptime = 100_000;
        uint? boot = 10;
        var fault = interruptedAfterCommit ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit;
        var scanner = new WindowsUpdateWorkflowTests.FixtureAgent();
        var schemaVersion = unexpectedShutdownAt == MaintenancePhase.FinalVerification ? 12 : SqliteJobStore.CurrentSchemaVersion;
        JournalPlanSnapshot plan;
        JournalApproval approval;
        await using (var prepare = await Open())
        {
            plan = await SaveFullMaintenancePlanAsync(prepare, "full-maintenance");
            using var review = JsonDocument.Parse(plan.CanonicalPlan.AsMemory());
            Assert.Equal(["windows_updates", "required_update_restarts", "packages", "post_package_checks", "final_restart", "final_verification"],
                review.RootElement.GetProperty("maintenance_phases").EnumerateArray().Select(phase => phase.GetString()));
            approval = new(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
        }

        var entries = ScheduledApprovalEntries(plan, approval);
        await using (var interrupted = await Open(new ThrowingFaultInjector(fault)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.RecordMaintenanceApprovalAsync(approval, entries));
        }

        await using (var reopened = await Open())
        {
            var committed = await reopened.LoadMaintenanceAsync(plan.Key.JobId);
            var history = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(plan.Key));
            Assert.Equal(interruptedAfterCommit, committed is not null);
            Assert.Equal(interruptedAfterCommit ? approval : null, history.ActiveApproval);
            Assert.Equal(interruptedAfterCommit ? entries.Length : 0, history.Entries.Length);
            if (!interruptedAfterCommit)
            {
                await reopened.RecordMaintenanceApprovalAsync(approval, entries);
            }
        }

        var action = Assert.IsType<InstallPackageActionRequest>(Assert.Single(plan.Actions).Request);
        var started = ScheduledPackageStart(approval, action.ActionId, clock.GetUtcNow());
        var current = await ReadPhase(MaintenancePhase.WindowsUpdates);
        await InterruptAtSelectedBoundary(current);
        await using (var windows = await Open())
        {
            await Assert.ThrowsAsync<JobStoreConflictException>(() => windows.AppendJournalEntryAsync(started));
            for (var pass = 0; pass < servicingRestarts; pass++)
            {
                var pending = await Scan(windows, true);
                await Assert.ThrowsAsync<JobStoreConflictException>(() => windows.AdvanceMaintenanceAsync(current, pending.Id));
                var sameBoot = await Scan(windows, false);
                await Assert.ThrowsAsync<JobStoreConflictException>(() => windows.AdvanceMaintenanceAsync(current, sameBoot.Id));
                Restart();
                await Scan(windows, false);
            }
        }

        current = await AdvanceAcrossInterruption(current, MaintenancePhase.Packages);
        await InterruptAtSelectedBoundary(current);
        var applied = ScheduledPackageApplied(started, clock.GetUtcNow());
        await using (var packages = await Open())
        {
            var scan = await Scan(packages, false);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => packages.AdvanceMaintenanceAsync(current, scan.Id));
            await packages.AppendJournalEntryAsync(started);
            await packages.AppendJournalEntryAsync(applied);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => packages.AdvanceMaintenanceAsync(current, scan.Id));
        }

        var verified = ScheduledPackageVerified(applied, clock.GetUtcNow(), JournalCheckpoint.PackageRecoveryVerification);
        await using (var recover = await Open())
        {
            Assert.True(await recover.RecordPackageRecoveryVerificationAsync(applied, verified));
            Assert.Equal(approval, (await recover.LoadJournalAsync(plan.Key))?.ActiveApproval);
        }

        current = await AdvanceAcrossInterruption(current, MaintenancePhase.PostPackageChecks);
        ImmutableArray<MaintenancePackageObservation> observed = [new(action.ActionId, action.ExactVersion)];
        await InterruptAtSelectedBoundary(current, observed);
        var mismatch = new MaintenanceCheckFailure(action.ActionId, MaintenanceCheckIssue.PackageMismatch);
        await using (var checks = await Open())
        {
            var scan = await Scan(checks, false);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => checks.AdvanceMaintenanceAsync(current, scan.Id));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => checks.AdvanceMaintenanceAsync(current, scan.Id,
                [new(action.ActionId, "0.0.0")]));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => checks.AdvanceMaintenanceAsync(current, scan.Id,
                [new(Guid.NewGuid(), action.ExactVersion)]));
            current = await checks.RecordMaintenanceCheckFailureAsync(current, scan.Id, mismatch);
        }
        current = await ReadPhase(MaintenancePhase.PostPackageChecks);
        Assert.Equal(mismatch, current.LastCheckFailure);

        if (servicingRestarts == 2)
        {
            await using (var recheck = await Open())
            {
                scanner.Scan = new(WindowsUpdateStatus.Succeeded,
                    [new(new(Guid.NewGuid(), 1), WindowsUpdateClass.Security, 128)], false);
                var offered = await new WindowsUpdateWorkflow(recheck, scanner, clock).ScanAsync(plan.Key.JobId, plan.GateDecisionId);
                current = await recheck.AdvanceMaintenanceAsync(current, offered.Id);
            }
            current = await ReadPhase(MaintenancePhase.WindowsUpdates);
            current = await AdvanceAcrossInterruption(current, MaintenancePhase.Packages);
            current = await AdvanceAcrossInterruption(current, MaintenancePhase.PostPackageChecks);
        }

        current = await AdvanceAcrossInterruption(current, MaintenancePhase.FinalRestart, observed);
        Assert.Null(current.LastCheckFailure);
        Guid beforeFinalBoot;
        await using (var waiting = await Open())
        {
            var beforeRestart = await Scan(waiting, false);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => waiting.AdvanceMaintenanceAsync(current, beforeRestart.Id, observed));
            var precedingBoot = boot;
            boot = null;
            await Assert.ThrowsAsync<JobStoreConflictException>(() => waiting.AdvanceMaintenanceAsync(current, beforeRestart.Id, observed));
            boot = precedingBoot - 1;
            var rolledBack = await Scan(waiting, false);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => waiting.AdvanceMaintenanceAsync(current, rolledBack.Id, observed));
            boot = current.InitialBootSession + 5;
            var excessive = await Scan(waiting, false);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => waiting.AdvanceMaintenanceAsync(current, excessive.Id, observed));
            boot = precedingBoot;
            beforeFinalBoot = (await Scan(waiting, false)).Id;
        }
        if (unexpectedShutdownAt == MaintenancePhase.FinalRestart) { await InterruptAtSelectedBoundary(current, observed); }
        else { Restart(); }
        await using (var waiting = await Open())
        {
            await Assert.ThrowsAsync<JobStoreConflictException>(() => waiting.AdvanceMaintenanceAsync(current, beforeFinalBoot, observed));
            var afterRestart = await Scan(waiting, false);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => waiting.AdvanceMaintenanceAsync(current, afterRestart.Id));
            current = await waiting.RecordMaintenanceCheckFailureAsync(current, afterRestart.Id, mismatch);
        }

        current = await ReadPhase(MaintenancePhase.FinalRestart);
        Assert.Equal(mismatch, current.LastCheckFailure);
        current = await AdvanceAcrossInterruption(current, MaintenancePhase.FinalVerification, observed);
        Assert.Null(current.LastCheckFailure);
        await InterruptAtSelectedBoundary(current);
        schemaVersion = SqliteJobStore.CurrentSchemaVersion;
        await using var handoff = await Open();
        var saved = Assert.IsType<JournalHistory>(await handoff.LoadJournalAsync(plan.Key));
        Assert.Equal(verified, saved.Entries[^1]);
        Assert.Single(saved.Entries, entry => entry.State == JournalState.Started);
        Assert.Equal(approval, Assert.Single(saved.Approvals));
        Assert.Equal(plan.CanonicalPlan.AsEnumerable(), saved.Plan.CanonicalPlan.AsEnumerable());
        Assert.Equal(plan.CanonicalImpact.AsEnumerable(), saved.Plan.CanonicalImpact.AsEnumerable());
        Assert.Empty(scanner.Prepared);
        Assert.Empty(scanner.Mutations);

        var job = plan.Key.JobId;
        Assert.Equal(current, await handoff.LoadMaintenanceAsync(job));
        Assert.Null(await handoff.LoadProfileVerificationAsync(job));
        var start = clock.GetUtcNow();
        var finalScan = await Scan(handoff, false);
        var restartState = servicingRestarts == 0 ? ProfileVerificationState.Passed : ProfileVerificationState.Failed;
        var verification = new ProfileVerificationResult(start, clock.GetUtcNow(), false, plan.ProfileSha256, new string('b', 64),
            Enum.GetValues<ProfileCheckKind>().Select(kind => new ProfileVerificationCheck(kind,
                kind == ProfileCheckKind.PendingRestart ? restartState : ProfileVerificationState.Unavailable,
                kind is not (ProfileCheckKind.SystemVolumeCapacity or ProfileCheckKind.PhysicalStorageEvidence or ProfileCheckKind.AudioOutput or
                    ProfileCheckKind.Camera or ProfileCheckKind.Microphone))).ToImmutableArray(),
            [new(plan.Actions[0].CapabilityId, true, ProfileVerificationState.Failed, action.ExactVersion, "0.1.0", action.ActionId,
                ProfilePackageStatus.Mismatch)],
            [new(new("POL-OS-001"), PolicyRuleState.Unavailable, PolicyRuleReason.EvidenceMissing, true)],
            null, null, null, 0, 0, WindowsUpdateDisposition.Current, historicalRecovery);
        var result = new ProfileVerificationRecord(current, finalScan.Id, verified.EntryId, saved.Entries.Length, verification);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => handoff.SaveProfileVerificationAsync(result with { ScanId = current.ScanId!.Value }));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => handoff.SaveProfileVerificationAsync(result with { PackageJournalEntryId = applied.EntryId }));
        await handoff.SaveProfileVerificationAsync(result);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => handoff.SaveProfileVerificationAsync(result));

        await using var profileReview = await Open();
        var restored = Assert.IsType<SavedProfileVerification>(await profileReview.LoadProfileVerificationAsync(job));
        Assert.True(restored.IsCurrent);
        Assert.Equal(verification.Capabilities.AsEnumerable(), restored.Record.Result.Capabilities.AsEnumerable());
        Assert.False(restored.Record.Result.ProfileChecksPassed);
        Assert.Equal(current, await profileReview.LoadMaintenanceAsync(job));
        var finalization = Assert.IsType<FinalizationChecklist>(await profileReview.LoadFinalizationAsync(job));
        var restart = finalization.Rows.Single(row => row.Rule.Id == FinalizationRuleId.PendingRestart);
        var expectedRestart = historicalRecovery ? FinalizationOutcome.Unknown
            : servicingRestarts == 0 ? FinalizationOutcome.Passed : FinalizationOutcome.Failed;
        Assert.Equal(expectedRestart, restart.Outcome);
        Assert.Equal(historicalRecovery ? FinalizationSource.None : FinalizationSource.AutomatedObservation, restart.Source);
        Assert.Equal(restartState, restored.Record.Result.Checks.Single(check => check.Kind == ProfileCheckKind.PendingRestart).State);
        Assert.Empty(finalization.History);
        Restart();
        Assert.False((await profileReview.LoadProfileVerificationAsync(job))!.IsCurrent);
        var afterBoot = Assert.IsType<FinalizationChecklist>(await profileReview.LoadFinalizationAsync(job));
        Assert.Equal(FinalizationOutcome.Unknown, afterBoot.Rows.Single(row => row.Rule.Id == FinalizationRuleId.PendingRestart).Outcome);
        await Scan(profileReview, false);
        Assert.False((await profileReview.LoadProfileVerificationAsync(job))!.IsCurrent);
        Assert.Equal(approval, (await profileReview.LoadJournalAsync(plan.Key))!.ActiveApproval);

        Task<SqliteJobStore> Open(IJobStoreFaultInjector? writeFault = null) =>
            SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, schemaVersion, clock, writeFault,
                () => uptime, readBootSession: () => boot);

        void Restart()
        {
            uptime /= 2;
            boot++;
        }

        async Task<WindowsUpdateEntry> Scan(SqliteJobStore store, bool restartPending)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            scanner.Scan = new(WindowsUpdateStatus.Succeeded, [], restartPending);
            return await new WindowsUpdateWorkflow(store, scanner, clock).ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        }

        async Task<MaintenanceCheckpoint> ReadPhase(MaintenancePhase phase)
        {
            await using var store = await Open();
            var checkpoint = Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(plan.Key.JobId));
            Assert.Equal(plan.Key, checkpoint.Plan);
            Assert.Equal(approval.ApprovalId, checkpoint.ApprovalId);
            Assert.Equal(phase, checkpoint.Phase);
            return checkpoint;
        }

        async Task InterruptAtSelectedBoundary(MaintenanceCheckpoint checkpoint,
            ImmutableArray<MaintenancePackageObservation> observations = default)
        {
            if (checkpoint.Phase != unexpectedShutdownAt) { return; }
            var job = checkpoint.Plan.JobId;
            ImmutableArray<WindowsUpdateEntry> updateHistory;
            JournalHistory before;
            await using (var priorBoot = await Open())
            {
                if (checkpoint.Phase != MaintenancePhase.FinalVerification) { await Scan(priorBoot, false); }
                before = Assert.IsType<JournalHistory>(await priorBoot.LoadJournalAsync(checkpoint.Plan));
                updateHistory = await priorBoot.LoadWindowsUpdatesAsync(job);
            }

            Restart();
            await using var reopened = await Open();
            Assert.Equal(checkpoint, await reopened.LoadMaintenanceAsync(job));
            var after = Assert.IsType<JournalHistory>(await reopened.LoadJournalAsync(checkpoint.Plan));
            Assert.Equal(before.ActiveApproval, after.ActiveApproval);
            Assert.Equal(before.Entries.AsEnumerable(), after.Entries.AsEnumerable());
            var reopenedUpdates = await reopened.LoadWindowsUpdatesAsync(job);
            Assert.Equal(updateHistory.Select(entry => entry.Id), reopenedUpdates.Select(entry => entry.Id));
            if (checkpoint.Phase == MaintenancePhase.Packages)
            {
                await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.AppendJournalEntryAsync(started));
            }
            else
            {
                await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                    reopened.AdvanceMaintenanceAsync(checkpoint, updateHistory[^1].Id, observations));
            }
        }

        async Task<MaintenanceCheckpoint> AdvanceAcrossInterruption(MaintenanceCheckpoint prior, MaintenancePhase next,
            ImmutableArray<MaintenancePackageObservation> observations = default)
        {
            Guid scanId;
            await using (var resumed = await Open())
            {
                if (prior.ScanId is { } consumed)
                {
                    await Assert.ThrowsAsync<JobStoreConflictException>(() => resumed.AdvanceMaintenanceAsync(prior, consumed, observations));
                }
                scanId = (await Scan(resumed, false)).Id;
            }
            await using (var interrupted = await Open(new ThrowingFaultInjector(fault)))
            {
                await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.AdvanceMaintenanceAsync(prior, scanId, observations));
            }
            var durable = await ReadPhase(interruptedAfterCommit ? next : prior.Phase);
            await using var resumedAfterInterruption = await Open();
            if (interruptedAfterCommit)
            {
                await Assert.ThrowsAsync<JobStoreConflictException>(() => resumedAfterInterruption.AdvanceMaintenanceAsync(prior, scanId, observations));
            }
            else
            {
                durable = await resumedAfterInterruption.AdvanceMaintenanceAsync(prior, scanId, observations);
            }
            Assert.Equal(scanId, durable.ScanId);
            Assert.Equal(next, durable.Phase);
            return durable;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullMaintenanceRequiresNewReviewIfAnotherSessionAddsEvidenceUnderTheSameGate(bool packagePhase)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        await using var reviewing = await Open();
        await using var competing = await Open();
        var original = await SaveFullMaintenancePlanAsync(reviewing, "evidence-race");
        var job = original.Key.JobId;
        var originalApproval = new JournalApproval(Guid.NewGuid(), original.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
        await competing.AppendEvidenceAsync(new JobEvidenceBatch(job, observations: [PersistenceTestData.CreateObservation("before")]));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reviewing.RecordMaintenanceApprovalAsync(originalApproval,
            ScheduledApprovalEntries(original, originalApproval)));
        Assert.Null(await reviewing.LoadMaintenanceAsync(job));
        Assert.Empty((await reviewing.LoadJournalAsync(original.Key))!.Approvals);

        var actual = Assert.IsType<StoredJob>(await reviewing.LoadJobAsync(job));
        var updated = BuildFullMaintenancePlan(actual);
        Assert.Equal(original.GateDecisionId, updated.GateDecisionId);
        Assert.NotEqual(original.Key, updated.Key);
        await reviewing.SaveJournalPlanAsync(updated);
        var approval = new JournalApproval(Guid.NewGuid(), updated.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
        await reviewing.RecordMaintenanceApprovalAsync(approval, ScheduledApprovalEntries(updated, approval));
        var checkpoint = Assert.IsType<MaintenanceCheckpoint>(await reviewing.LoadMaintenanceAsync(job));
        var scanner = new WindowsUpdateWorkflowTests.FixtureAgent { Scan = new(WindowsUpdateStatus.Succeeded, [], false) };
        var workflow = new WindowsUpdateWorkflow(reviewing, scanner, clock);
        var scan = await workflow.ScanAsync(job, updated.GateDecisionId);
        if (packagePhase)
        {
            checkpoint = await reviewing.AdvanceMaintenanceAsync(checkpoint, scan.Id);
            Assert.Equal(MaintenancePhase.Packages, checkpoint.Phase);
        }
        await competing.AppendEvidenceAsync(new JobEvidenceBatch(job, observations: [PersistenceTestData.CreateObservation("after")]));
        if (packagePhase)
        {
            var dispatch = ScheduledPackageStart(approval, updated.Actions[0].ActionId, clock.GetUtcNow());
            await Assert.ThrowsAsync<JobStoreConflictException>(() => reviewing.AppendJournalEntryAsync(dispatch));
        }
        else
        {
            await Assert.ThrowsAsync<JobStoreConflictException>(() => reviewing.AdvanceMaintenanceAsync(checkpoint, scan.Id));
        }
        Assert.Equal(checkpoint, await reviewing.LoadMaintenanceAsync(job));
        var history = Assert.IsType<JournalHistory>(await reviewing.LoadJournalAsync(updated.Key));
        Assert.Equal(approval, history.ActiveApproval);
        Assert.All(history.Entries, entry => Assert.Equal(JournalState.Approved, entry.State));

        Task<SqliteJobStore> Open() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 1_000, () => 10);
    }

    [Fact]
    public async Task FullMaintenanceRequiresItsOwnReviewedApprovalAndPreservesThePreMigrationPackageSchedule()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        JournalPlanSnapshot historical;
        JournalApproval priorApproval;
        string database;
        await using (var prior = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 11, clock, null))
        {
            database = prior.DatabasePath;
            historical = await OpenPackageScheduleAsync(prior, "prior-maintenance", ids => [new(ids[0], 1, [])], 1);
            priorApproval = new(Guid.NewGuid(), historical.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            await prior.RecordPackageScheduleApprovalAsync(priorApproval, ScheduledApprovalEntries(historical, priorApproval));
        }
        await Assert.ThrowsAsync<InjectedPersistenceException>(() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            12, clock, new ThrowingFaultInjector(JobStoreFaultPoint.BeforeMigrationCommit, 12)));
        Assert.Equal(11, await SqliteTestControl.ReadUserVersionAsync(database));
        await using (var current = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 1_000, readBootSession: () => 10))
        {
            var saved = Assert.IsType<JournalHistory>(await current.LoadJournalAsync(historical.Key));
            Assert.Equal(priorApproval, saved.ActiveApproval);
            Assert.Equal(historical.CanonicalPlan.AsEnumerable(), saved.Plan.CanonicalPlan.AsEnumerable());
            Assert.Null(await current.LoadMaintenanceAsync(historical.Key.JobId));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => current.RecordMaintenanceApprovalAsync(priorApproval,
                ScheduledApprovalEntries(historical, priorApproval)));

            var plan = await SaveFullMaintenancePlanAsync(current, "full-approval");
            var approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
            var entries = ScheduledApprovalEntries(plan, approval);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => current.RecordPackageScheduleApprovalAsync(approval, entries));
            Assert.Null(await current.LoadMaintenanceAsync(plan.Key.JobId));
            Assert.Empty((await current.LoadJournalAsync(plan.Key))!.Approvals);
            await current.RecordMaintenanceApprovalAsync(approval, entries);
            var expected = Assert.IsType<MaintenanceCheckpoint>(await current.LoadMaintenanceAsync(plan.Key.JobId));
            var scanner = new WindowsUpdateWorkflowTests.FixtureAgent { Scan = new(WindowsUpdateStatus.Succeeded, [], false) };
            var scan = await new WindowsUpdateWorkflow(current, scanner, clock).ScanAsync(plan.Key.JobId, plan.GateDecisionId);
            await current.AppendEvidenceAsync(new JobEvidenceBatch(plan.Key.JobId,
                sanitizationEvidence: [PersistenceTestData.CreateSanitization("changed-maintenance-intake")]));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => current.AdvanceMaintenanceAsync(expected, scan.Id));
            Assert.Equal(expected, await current.LoadMaintenanceAsync(plan.Key.JobId));
        }
        await Assert.ThrowsAsync<JobStoreVersionException>(() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 11, clock, null));
    }

    [Fact]
    public async Task FullMaintenanceBlocksPackageDispatchAfterUnexpectedBootUntilRescanAndBoundsRepeatedBoots()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        uint boot = 20;
        ulong uptime = 1_000;
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => uptime, readBootSession: () => boot);
        var plan = await SaveFullMaintenancePlanAsync(store, "unexpected-boots");
        var approval = new JournalApproval(Guid.NewGuid(), plan.Key, Approver, Guid.NewGuid(), clock.GetUtcNow());
        await store.RecordMaintenanceApprovalAsync(approval, ScheduledApprovalEntries(plan, approval));
        var current = Assert.IsType<MaintenanceCheckpoint>(await store.LoadMaintenanceAsync(plan.Key.JobId));
        var scanner = new WindowsUpdateWorkflowTests.FixtureAgent();
        var workflow = new WindowsUpdateWorkflow(store, scanner, clock);
        var eligible = await workflow.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AdvanceMaintenanceAsync(current, eligible.Id));
        scanner.Scan = new(WindowsUpdateStatus.Succeeded, [], false);
        var clear = await workflow.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        clock.Advance(TimeSpan.FromSeconds(-1));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AdvanceMaintenanceAsync(current, clear.Id));
        clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AdvanceMaintenanceAsync(current, clear.Id));
        clear = await workflow.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        current = await store.AdvanceMaintenanceAsync(current, clear.Id);
        Assert.Equal(MaintenancePhase.Packages, current.Phase);
        var started = ScheduledPackageStart(approval, plan.Actions[0].ActionId, clock.GetUtcNow());
        for (var unexpected = 1; unexpected <= 4; unexpected++)
        {
            boot++;
            // A reopened job can be observed later in the new boot than it was in the old one.
            uptime += 10_000;
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(started));
            await workflow.ScanAsync(plan.Key.JobId, plan.GateDecisionId);
        }
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(started));
        var unchanged = Assert.IsType<JournalHistory>(await store.LoadJournalAsync(plan.Key));
        Assert.Equal(approval, unchanged.ActiveApproval);
        Assert.Equal(JournalState.Approved, unchanged.StateOf(started.ActionId));
        Assert.Empty(scanner.Mutations);
    }

    private static async Task<JournalPlanSnapshot> SaveFullMaintenancePlanAsync(SqliteJobStore store, string suffix)
    {
        var (job, _) = await CreateReviewedJobAsync(store, suffix);
        var plan = BuildFullMaintenancePlan(job);
        Assert.Equal(JournalWriteResult.Recorded, await store.SaveJournalPlanAsync(plan));
        return plan;
    }

    private static JournalPlanSnapshot BuildFullMaintenancePlan(StoredJob job) =>
        BuildReviewedPlan(job, job.SanitizationEvidence.Single(), CreateReviewedCatalog(), proposal =>
            proposal.WithSchedule(proposal.Actions.Select(action =>
                new ScheduledAction(action.Request.ActionId, MaintenancePhases.PackagePhase, [])), fullMaintenance: true));
}
