using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class FinalizationStoreTests
{
    private static readonly OperatorId Operator = new("operator-finalization-test");

    [Fact]
    public async Task MigrationAndReopenKeepAppendOnlyConfirmationHistory()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        await using (var prior = await OpenAsync(workspace, clock, () => 9, schemaVersion: 14))
        {
            await prior.CreateJobAsync(job);
            Assert.Null(await prior.LoadFinalizationAsync(job.JobId));
        }

        var browser = FinalizationRuleId.BrowserSessions;
        await using (var migrated = await OpenAsync(workspace, clock, () => 9))
        {
            Assert.Equal(job, (await migrated.LoadJobAsync(job.JobId))?.Job);
            Assert.Null(await migrated.LoadFinalizationAsync(new JobId("job-missing")));
            var initial = Assert.IsType<FinalizationChecklist>(await migrated.LoadFinalizationAsync(job.JobId));
            Assert.True(initial.CanRecord);
            Assert.True(initial.HasBlockingChecks);
            Assert.Empty(initial.History);
            var pending = initial.Rows.Single(row => row.Rule.Id == FinalizationRuleId.PendingRestart);
            Assert.Equal(FinalizationOutcome.Unknown, pending.Outcome);
            await Assert.ThrowsAsync<ArgumentException>(() => migrated.SaveFinalizationConfirmationAsync(job.JobId,
                initial, FinalizationRuleId.PendingRestart, FinalizationOutcome.Passed, null, Operator));
            await Assert.ThrowsAsync<ArgumentException>(() => migrated.SaveFinalizationConfirmationAsync(job.JobId,
                initial, browser, FinalizationOutcome.NotApplicable, null, Operator));
            await migrated.SaveFinalizationConfirmationAsync(job.JobId, initial, browser, FinalizationOutcome.Failed,
                null, Operator);
        }

        await using var reopened = await OpenAsync(workspace, clock, () => 9);
        var afterFailure = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        var failure = Assert.Single(afterFailure.History);
        Assert.Equal(Operator, failure.OperatorId);
        Assert.Equal(clock.GetUtcNow(), failure.RecordedAtUtc);
        Assert.Equal(FinalizationOutcome.Failed, afterFailure.Rows.Single(row => row.Rule.Id == browser).Outcome);
        clock.Advance(TimeSpan.FromMinutes(1));
        await reopened.SaveFinalizationConfirmationAsync(job.JobId, afterFailure, browser,
            FinalizationOutcome.NotApplicable, FinalizationReason.NoWorkshopUse, Operator);

        var updated = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        Assert.Equal(failure, updated.History[0]);
        Assert.Equal(afterFailure.EvidenceVersion, updated.EvidenceVersion);
        var confirmation = updated.Rows.Single(row => row.Rule.Id == browser);
        Assert.Equal(FinalizationOutcome.NotApplicable, confirmation.Outcome);
        Assert.Equal(FinalizationSource.OperatorConfirmation, confirmation.Source);
        Assert.Equal(FinalizationReason.NoWorkshopUse, confirmation.Confirmation?.Reason);
        Assert.Equal(2, updated.History.Count);
    }

    [Fact]
    public async Task ChangedEvidenceAndBootKeepOldReviewAndRejectStaleWrites()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        uint? boot = 10;
        await using var store = await OpenAsync(workspace, clock, () => boot);
        await store.CreateJobAsync(job);
        var initial = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        var rule = FinalizationRuleId.WorkshopAccounts;
        await store.SaveFinalizationConfirmationAsync(job.JobId, initial, rule, FinalizationOutcome.Passed, null, Operator);
        var confirmed = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        var historical = Assert.Single(confirmed.History);

        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId,
            observations: [PersistenceTestData.CreateObservation()]));
        var changed = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        Assert.NotEqual(confirmed.EvidenceVersion, changed.EvidenceVersion);
        Assert.Equal(historical, Assert.Single(changed.History));
        var afterEvidence = changed.Rows.Single(row => row.Rule.Id == rule);
        Assert.Equal(FinalizationOutcome.Unknown, afterEvidence.Outcome);
        Assert.True(afterEvidence.NeedsRecheck);
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.SaveFinalizationConfirmationAsync(job.JobId, confirmed, rule, FinalizationOutcome.Passed, null, Operator));

        boot++;
        var afterBoot = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        Assert.NotEqual(changed.EvidenceVersion, afterBoot.EvidenceVersion);
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.SaveFinalizationConfirmationAsync(job.JobId, changed, rule, FinalizationOutcome.Passed, null, Operator));
        await store.SaveFinalizationConfirmationAsync(job.JobId, afterBoot, rule, FinalizationOutcome.Passed, null, Operator);

        boot = null;
        var unavailable = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        Assert.False(unavailable.CanRecord);
        Assert.Equal(2, unavailable.History.Count);
        Assert.Equal(FinalizationOutcome.Unknown, unavailable.Rows.Single(row => row.Rule.Id == rule).Outcome);
        Assert.True(unavailable.HasBlockingChecks);

        boot = 11;
        var beforeArchive = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        await store.SetArchiveStateAsync(job.JobId, true, clock.GetUtcNow());
        var archived = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        Assert.False(archived.CanRecord);
        Assert.Equal(beforeArchive.History.AsEnumerable(), archived.History);
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.SaveFinalizationConfirmationAsync(job.JobId, beforeArchive, rule, FinalizationOutcome.Failed, null, Operator));
        await store.SetArchiveStateAsync(job.JobId, false, clock.GetUtcNow());
        var restored = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
        Assert.True(restored.CanRecord);
        Assert.NotEqual(beforeArchive.EvidenceVersion, restored.EvidenceVersion);
        Assert.True(restored.Rows.Single(row => row.Rule.Id == rule).NeedsRecheck);
    }

    [Fact]
    public async Task SecondConnectionAndOtherJobCannotUseTheOriginalReview()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var other = PersistenceTestData.CreateJob("002");
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        await using var first = await OpenAsync(workspace, clock, () => 15);
        await first.CreateJobAsync(job);
        await first.CreateJobAsync(other);
        await using var second = await OpenAsync(workspace, clock, () => 15);
        var expected = Assert.IsType<FinalizationChecklist>(await second.LoadFinalizationAsync(job.JobId));
        var rule = FinalizationRuleId.WorkshopAccounts;
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            second.SaveFinalizationConfirmationAsync(other.JobId, expected, rule, FinalizationOutcome.Passed, null, Operator));
        await first.SaveFinalizationConfirmationAsync(job.JobId, expected, rule, FinalizationOutcome.Passed, null, Operator);
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            second.SaveFinalizationConfirmationAsync(job.JobId, expected, rule, FinalizationOutcome.Failed, null, Operator));
        var saved = Assert.IsType<FinalizationChecklist>(await second.LoadFinalizationAsync(job.JobId));
        Assert.Equal(FinalizationOutcome.Passed, Assert.Single(saved.History).Outcome);
        Assert.Empty(Assert.IsType<FinalizationChecklist>(await second.LoadFinalizationAsync(other.JobId)).History);
    }

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, FixedTimeProvider time,
        Func<uint?> bootSession, int? schemaVersion = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, schemaVersion ?? SqliteJobStore.CurrentSchemaVersion,
            time, null, () => 10_000, bootSession);
}
