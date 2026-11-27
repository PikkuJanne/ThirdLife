using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class RecipientHandoverStoreTests
{
    private static readonly OperatorId Operator = new("operator-handover-test");

    [Fact]
    public async Task ModeChangesInvalidateFinalizationAndKeepHistoricalRecipientChoices()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var rule = FinalizationRuleId.WorkshopAccounts;
        FinalizationRecord historicalConfirmation;
        await using (var prior = await OpenAsync(workspace, clock, () => 10, schemaVersion: 15))
        {
            await prior.CreateJobAsync(job);
            Assert.Null(await prior.LoadRecipientHandoverAsync(job.JobId));
            var original = Assert.IsType<FinalizationChecklist>(await prior.LoadFinalizationAsync(job.JobId));
            await prior.SaveFinalizationConfirmationAsync(job.JobId, original, rule, FinalizationOutcome.Passed, null, Operator);
            historicalConfirmation = Assert.Single((await prior.LoadFinalizationAsync(job.JobId))!.History);
        }

        await using (var store = await OpenAsync(workspace, clock, () => 10))
        {
            var initial = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            Assert.Null(initial.Handover);
            Assert.True(initial.CanRecord);
            var migrated = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
            Assert.Equal(FinalizationOutcome.Passed, migrated.Rows.Single(row => row.Rule.Id == rule).Outcome);

            await store.SaveHandoverModeAsync(job.JobId, initial, HandoverMode.Assisted, Operator);
            var assisted = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            Assert.Equal(HandoverMode.Assisted, assisted.Handover?.Mode);
            Assert.Equal(1, assisted.ModeSequence);
            await store.SaveHandoverModeAsync(job.JobId, assisted, HandoverMode.Assisted, Operator);
            Assert.Single((await store.LoadRecipientHandoverAsync(job.JobId))!.History);

            var recheck = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
            Assert.True(recheck.Rows.Single(row => row.Rule.Id == rule).NeedsRecheck);
            Assert.Equal(historicalConfirmation, Assert.Single(recheck.History));
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                store.SaveFinalizationConfirmationAsync(job.JobId, migrated, rule, FinalizationOutcome.Passed, null, Operator));
            await store.SaveFinalizationConfirmationAsync(job.JobId, recheck, rule, FinalizationOutcome.Passed, null, Operator);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveRecipientChoiceAsync(job.JobId, assisted,
                RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested, false, Operator));
            await store.SaveRecipientChoiceAsync(job.JobId, assisted, RecipientSetupArea.Backup,
                RecipientSetupChoice.GuidanceRequested, true, Operator);
            var afterChoice = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId));
            Assert.Equal(recheck.EvidenceVersion, afterChoice.EvidenceVersion);
            Assert.Equal(FinalizationOutcome.Passed, afterChoice.Rows.Single(row => row.Rule.Id == rule).Outcome);
        }

        await using var reopened = await OpenAsync(workspace, clock, () => 11);
        var current = Assert.IsType<SavedRecipientHandover>(await reopened.LoadRecipientHandoverAsync(job.JobId));
        var choice = Assert.Single(current.History, entry => entry.RecipientPresent);
        Assert.Equal(Operator, choice.OperatorId);
        Assert.Equal(clock.GetUtcNow(), choice.RecordedAtUtc);
        Assert.Contains(new RecipientSetupDecision(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested),
            current.Handover!.Decisions);
        await reopened.SaveHandoverModeAsync(job.JobId, current, HandoverMode.Sealed, Operator);
        var sealedMode = Assert.IsType<SavedRecipientHandover>(await reopened.LoadRecipientHandoverAsync(job.JobId));
        Assert.Equal(3, sealedMode.ModeSequence);
        Assert.All(sealedMode.Handover!.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.SaveRecipientChoiceAsync(job.JobId, sealedMode,
            RecipientSetupArea.Backup, RecipientSetupChoice.Declined, true, Operator));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => reopened.SaveRecipientChoiceAsync(job.JobId, current,
            RecipientSetupArea.Backup, RecipientSetupChoice.Declined, true, Operator));
        await reopened.SaveHandoverModeAsync(job.JobId, sealedMode, HandoverMode.Assisted, Operator);
        var newAssisted = Assert.IsType<SavedRecipientHandover>(await reopened.LoadRecipientHandoverAsync(job.JobId));
        Assert.Equal(choice, newAssisted.History[1]);
        Assert.All(newAssisted.Handover!.Decisions, decision => Assert.Equal(RecipientSetupChoice.Deferred, decision.Choice));
    }

    [Fact]
    public async Task BootUnavailableLeavesModeRecordingAvailableAndAnArchiveDoesNot()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        await using var store = await OpenAsync(workspace, clock, () => null);
        await store.CreateJobAsync(job);
        var initial = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
        Assert.True(initial.CanRecord);
        Assert.Null(await store.LoadRecipientHandoverAsync(new JobId("job-missing")));
        await store.SaveHandoverModeAsync(job.JobId, initial, HandoverMode.Assisted, Operator);
        var beforeArchive = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
        await store.SetArchiveStateAsync(job.JobId, true, clock.GetUtcNow());
        var archived = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
        Assert.False(archived.CanRecord);
        Assert.Single(archived.History);
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.SaveHandoverModeAsync(job.JobId, beforeArchive, HandoverMode.Sealed, Operator));
        Assert.Single((await store.LoadRecipientHandoverAsync(job.JobId))!.History);
    }

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, FixedTimeProvider time,
        Func<uint?> bootSession, int? schemaVersion = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, schemaVersion ?? SqliteJobStore.CurrentSchemaVersion,
            time, null, () => 10_000, bootSession);
}
