using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

public sealed partial class RebootCheckpointStoreTests
{
    [Fact]
    public async Task PackageRebootRequiresAuthenticatedProofOfANewerBootAndConsumesItOnce()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        ulong? uptime = 9_000;
        JournalEntry applied;
        RebootResumeCandidate checkpoint;
        await using (var store = await OpenPackageAsync(workspace, clock, () => uptime))
        {
            applied = await SaveAppliedAsync(store, "package-reboot", needsRestart: true, packageBackend: true);
            var waiting = Assert.IsType<PackageRebootCandidate>(await store.LoadPackageRebootCheckpointAsync(applied));
            checkpoint = waiting.Candidate;
            Assert.False(waiting.CanVerify);
            Assert.False(waiting.IsExpired);
            Assert.Equal(uptime, checkpoint.Checkpoint.PackageBootUptime);
            Assert.Equal(checkpoint.Checkpoint, (await store.IssueRebootCheckpointAsync(applied))?.Checkpoint);
            Assert.Empty(await store.ListPendingRebootCheckpointsAsync(applied.Plan.JobId));
            Assert.Null(await store.LoadRebootCheckpointAsync(applied.Plan.JobId, checkpoint.Checkpoint.Id));
            Assert.Null(await store.DecideRebootCheckpointAsync(applied.Plan.JobId, checkpoint.Checkpoint.Id,
                checkpoint.Token, RebootDisposition.Resume));
        }

        await using var reopened = await OpenPackageAsync(workspace, clock, () => uptime);
        var verification = VerifyPackage(applied, JournalCheckpoint.PackageRebootVerification, clock.GetUtcNow());
        uptime = 10_000;
        Assert.False(await reopened.RecordPackageRebootVerificationAsync(verification, checkpoint.Token, checkpoint.Checkpoint.Id));
        uptime = null;
        Assert.False((await reopened.LoadPackageRebootCheckpointAsync(applied))?.CanVerify);
        uptime = 1_000;
        Assert.True((await reopened.LoadPackageRebootCheckpointAsync(applied))?.CanVerify);
        Assert.True(await reopened.RecordPackageRebootVerificationAsync(verification, checkpoint.Token, checkpoint.Checkpoint.Id));
        Assert.False(await reopened.RecordPackageRebootVerificationAsync(verification, checkpoint.Token, checkpoint.Checkpoint.Id));
        Assert.Null(await reopened.LoadPackageRebootCheckpointAsync(applied));
        Assert.Null(await reopened.LoadLatestRebootDecisionAsync(applied.Plan.JobId));
        Assert.Equal(verification, (await reopened.LoadJournalAsync(applied.Plan))?.Entries[^1]);
    }

    [Fact]
    public async Task StampedPackageRebootRequiresALaterBootSequenceEvenWhenUptimeChanges()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        uint? boot = 17;
        ulong uptime = 9_000;
        JournalEntry applied;
        RebootResumeCandidate issued;
        await using (var store = await Open())
        {
            applied = await SaveAppliedAsync(store, "package-boot-sequence", needsRestart: true, packageBackend: true);
            issued = Assert.IsType<PackageRebootCandidate>(await store.LoadPackageRebootCheckpointAsync(applied)).Candidate;
            Assert.Equal(boot, issued.Checkpoint.PackageBootSession);
        }

        await using var reopened = await Open();
        var verification = VerifyPackage(applied, JournalCheckpoint.PackageRebootVerification, clock.GetUtcNow());
        uptime = 1_000;
        foreach (var unavailableOrEarlier in new uint?[] { null, 16, 17 })
        {
            boot = unavailableOrEarlier;
            var checkpoint = Assert.IsType<PackageRebootCandidate>(await reopened.LoadPackageRebootCheckpointAsync(applied));
            Assert.Equal(issued.Checkpoint, checkpoint.Candidate.Checkpoint);
            Assert.False(checkpoint.CanVerify);
            Assert.False(await reopened.RecordPackageRebootVerificationAsync(verification, issued.Token, issued.Checkpoint.Id));
        }

        boot = 18;
        uptime = 12_000;
        Assert.True((await reopened.LoadPackageRebootCheckpointAsync(applied))?.CanVerify);
        Assert.True(await reopened.RecordPackageRebootVerificationAsync(verification, issued.Token, issued.Checkpoint.Id));
        Assert.Equal(verification, (await reopened.LoadJournalAsync(applied.Plan))?.Entries[^1]);
        Assert.Null(await reopened.LoadPackageRebootCheckpointAsync(applied));

        Task<SqliteJobStore> Open() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => uptime, () => boot);
    }

    [Fact]
    public async Task PackageRecoveryRejectsStaleGatesExpiryWrongAttemptsAndGenericAppend()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        ulong uptime = 9_000;
        await using var store = await OpenPackageAsync(workspace, clock, () => uptime);
        var applied = await SaveAppliedAsync(store, "package-expire", needsRestart: true, packageBackend: true);
        var second = await SaveAppliedAsync(store, "package-other", needsRestart: true, packageBackend: true);
        var candidate = Assert.IsType<PackageRebootCandidate>(await store.LoadPackageRebootCheckpointAsync(applied)).Candidate;
        var other = Assert.IsType<PackageRebootCandidate>(await store.LoadPackageRebootCheckpointAsync(second)).Candidate;
        var verification = VerifyPackage(applied, JournalCheckpoint.PackageRebootVerification, clock.GetUtcNow());
        var wrongVerification = VerifyPackage(second, JournalCheckpoint.PackageRebootVerification, clock.GetUtcNow());
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.AppendJournalEntryAsync(verification));
        uptime = 1_000;
        Assert.False(await store.RecordPackageRebootVerificationAsync(wrongVerification, candidate.Token, other.Checkpoint.Id));
        Assert.False(await store.RecordPackageRebootVerificationAsync(wrongVerification, candidate.Token, candidate.Checkpoint.Id));
        var alteredToken = candidate.Token.AuthenticityBlob.ToArray();
        alteredToken[0] ^= 1;
        Assert.False(await store.RecordPackageRebootVerificationAsync(verification, new RebootResumeToken(alteredToken), candidate.Checkpoint.Id));

        await store.AppendEvidenceAsync(new JobEvidenceBatch(second.Plan.JobId,
            sanitizationEvidence: [PersistenceTestData.CreateSanitization("replacement-package-intake")]));
        Assert.Null(await store.LoadPackageRebootCheckpointAsync(second));
        Assert.False(await store.RecordPackageRebootVerificationAsync(wrongVerification, other.Token, other.Checkpoint.Id));
        clock.Advance(candidate.Checkpoint.ExpiresAtUtc - clock.GetUtcNow());
        var expired = Assert.IsType<PackageRebootCandidate>(await store.LoadPackageRebootCheckpointAsync(applied));
        Assert.True(expired.IsExpired);
        Assert.False(expired.CanVerify);
        Assert.False(await store.RecordPackageRebootVerificationAsync(verification, candidate.Token, candidate.Checkpoint.Id));
        await AssertAppliedAsync(store, applied);
        await AssertAppliedAsync(store, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Extended")]
    public async Task PackageRebootVerificationAndTokenConsumeCommitTogether(bool committed)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        ulong uptime = 9_000;
        JournalEntry applied;
        RebootResumeCandidate checkpoint;
        await using (var store = await OpenPackageAsync(workspace, clock, () => uptime))
        {
            applied = await SaveAppliedAsync(store, "package-atomic", needsRestart: true, packageBackend: true);
            checkpoint = Assert.IsType<PackageRebootCandidate>(await store.LoadPackageRebootCheckpointAsync(applied)).Candidate;
        }

        uptime = 1_000;
        var fault = committed ? JobStoreFaultPoint.AfterWriteCommit : JobStoreFaultPoint.BeforeWriteCommit;
        var verification = VerifyPackage(applied, JournalCheckpoint.PackageRebootVerification, clock.GetUtcNow());
        await using (var interrupted = await OpenPackageAsync(workspace, clock, () => uptime, new ThrowingFaultInjector(fault)))
        {
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => interrupted.RecordPackageRebootVerificationAsync(
                verification, checkpoint.Token, checkpoint.Checkpoint.Id));
        }

        await using var reopened = await OpenPackageAsync(workspace, clock, () => uptime);
        Assert.Equal(committed ? verification : applied, (await reopened.LoadJournalAsync(applied.Plan))?.Entries[^1]);
        Assert.Equal(!committed, await reopened.LoadPackageRebootCheckpointAsync(applied) is not null);
        Assert.Equal(!committed, await reopened.RecordPackageRebootVerificationAsync(
            verification, checkpoint.Token, checkpoint.Checkpoint.Id));
        Assert.Equal(verification, (await reopened.LoadJournalAsync(applied.Plan))?.Entries[^1]);
        Assert.Null(await reopened.LoadPackageRebootCheckpointAsync(applied));
    }

    [Fact]
    public async Task PackageRecoveryVerificationRequiresTheExactPreviouslyAppliedEntry()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(IssueTime);
        await using var store = await OpenPackageAsync(workspace, clock, () => 2_000);
        var applied = await SaveAppliedAsync(store, "package-observed", needsRestart: false, packageBackend: true);
        var other = await SaveAppliedAsync(store, "package-wrong-prior", needsRestart: false, packageBackend: true);
        var verification = VerifyPackage(applied, JournalCheckpoint.PackageRecoveryVerification, clock.GetUtcNow());
        Assert.False(await store.RecordPackageRecoveryVerificationAsync(other, verification));
        Assert.True(await store.RecordPackageRecoveryVerificationAsync(applied, verification));
        Assert.False(await store.RecordPackageRecoveryVerificationAsync(applied, verification));
        Assert.Equal(verification, (await store.LoadJournalAsync(applied.Plan))?.Entries[^1]);
        await AssertAppliedAsync(store, other);
    }

    private static JournalEntry VerifyPackage(JournalEntry applied, JournalCheckpoint checkpoint, DateTimeOffset now) =>
        Entry(applied.Plan, applied.ActionId, VerifiedId, JournalState.Applied, JournalState.Verified, JournalActor.Verifier,
            JournalResult.PackageLaunchVerified, now, checkpoint, ActionVerification.ExactPackageIdentity);

    private static Task<SqliteJobStore> OpenPackageAsync(PersistenceTestWorkspace workspace, FixedTimeProvider clock,
        Func<ulong?> readBootUptime, IJobStoreFaultInjector? fault = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion, clock, fault, readBootUptime);
}
