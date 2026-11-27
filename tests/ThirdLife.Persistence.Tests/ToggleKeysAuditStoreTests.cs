using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class ToggleKeysAuditStoreTests
{
    [Fact]
    public async Task OnlyExactAssistedApprovalCanBeStagedAndHistorySurvivesInterruption()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var actor = new OperatorId("operator-toggle-audit");
        var binding = Guid.NewGuid();
        ToggleKeysAuditEntry pending;
        await using (var store = await Open())
        {
            await store.CreateJobAsync(job);
            var initial = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            await store.SaveHandoverModeAsync(job.JobId, initial, HandoverMode.Assisted, actor);
            var assisted = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            var exact = Assert.IsType<ToggleKeysChange>(ToggleKeysChange.Preview(assisted, new(binding, false), true).Change);
            var approved = exact.Approve(exact.ChangeId, recipientConfirmed: true);
            pending = await store.StageToggleKeysAsync(approved, actor);
            Assert.Equal(ToggleKeysAuditOutcome.ApprovedPending, pending.Outcome);
            Assert.Null(pending.IndependentlyObserved);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => store.StageToggleKeysAsync(approved, actor));
        }

        await using var reopened = await Open();
        Assert.Equal(pending, Assert.Single(await reopened.LoadToggleKeysAttemptsAsync(job.JobId)));
        var sameMode = Assert.IsType<SavedRecipientHandover>(await reopened.LoadRecipientHandoverAsync(job.JobId));
        await reopened.SaveHandoverModeAsync(job.JobId, sameMode, HandoverMode.Sealed, actor);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.RecordToggleKeysOutcomeAsync(job.JobId, pending,
            ToggleKeysAuditOutcome.Verified, true));
        await reopened.RecordToggleKeysOutcomeAsync(job.JobId, pending, ToggleKeysAuditOutcome.VerificationUnavailable, null);
        var history = await reopened.LoadToggleKeysAttemptsAsync(job.JobId);
        Assert.Equal(pending, history[0]);
        Assert.Equal(ToggleKeysAuditOutcome.VerificationUnavailable, history[^1].Outcome);
        Assert.Equal(pending.ChangeId, history[^1].ChangeId);
        Assert.Null(history[^1].IndependentlyObserved);

        Task<SqliteJobStore> Open() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => 10);
    }
}
