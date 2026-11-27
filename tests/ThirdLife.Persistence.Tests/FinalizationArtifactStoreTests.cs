using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class FinalizationArtifactStoreTests
{
    private static readonly OperatorId Operator = new("operator-artifact-test");

    [Fact]
    public async Task MigrationReopenAndUnknownRescanPreserveTheKnownResidueBlock()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var rule = FinalizationRuleId.Downloads;
        SanitizationGateDecisionId gate;
        await using (var prior = await OpenAsync(workspace, clock, schemaVersion: 15))
        {
            await prior.CreateJobAsync(job);
            gate = await RecordSanitizationAsync(prior, job);
            var before = Assert.IsType<FinalizationChecklist>(await prior.LoadFinalizationAsync(job.JobId));
            await prior.SaveFinalizationConfirmationAsync(job.JobId, before, rule, FinalizationOutcome.Passed, null, Operator);
            var downloads = Assert.IsType<FinalizationChecklist>(await prior.LoadFinalizationAsync(job.JobId));
            await prior.SaveFinalizationConfirmationAsync(job.JobId, downloads, FinalizationRuleId.BrowserSessions,
                FinalizationOutcome.Passed, null, Operator);
        }

        await using (var migrated = await OpenAsync(workspace, clock))
        {
            var before = Assert.IsType<FinalizationChecklist>(await migrated.LoadFinalizationAsync(job.JobId));
            Assert.Equal(2, before.History.Count);
            await migrated.SaveFinalizationArtifactScanAsync(job.JobId, before, gate, Findings(WorkshopArtifactStatus.Present),
                ProvenanceKind.SyntheticFixture);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.SaveFinalizationConfirmationAsync(
                job.JobId, before, rule, FinalizationOutcome.Passed, null, Operator));
        }

        await using var reopened = await OpenAsync(workspace, clock);
        var found = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        Assert.Equal(ProvenanceKind.SyntheticFixture, Assert.Single(found.ArtifactScans).Origin);
        Assert.True(Row(found, rule).NeedsRecheck);
        Assert.True(Row(found, rule).HasBlockingArtifact);
        Assert.False(Row(found, FinalizationRuleId.BrowserSessions).IsBlocking);
        await reopened.SaveFinalizationArtifactScanAsync(job.JobId, found, gate, Findings(WorkshopArtifactStatus.AccessDenied),
            ProvenanceKind.SyntheticFixture);
        var unavailable = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        Assert.True(Row(unavailable, rule).HasBlockingArtifact);
        await reopened.SaveFinalizationConfirmationAsync(job.JobId, unavailable, rule, FinalizationOutcome.Passed, null, Operator);
        var stillBlocking = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        Assert.Equal(FinalizationOutcome.Passed, Row(stillBlocking, rule).Outcome);
        Assert.True(Row(stillBlocking, rule).IsBlocking);

        await reopened.SaveFinalizationArtifactScanAsync(job.JobId, stillBlocking, gate, Findings(WorkshopArtifactStatus.AbsentInCheckedScope),
            ProvenanceKind.SyntheticFixture);
        var observedAbsent = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        Assert.False(Row(observedAbsent, rule).HasBlockingArtifact);
        Assert.True(Row(observedAbsent, rule).IsBlocking);
        await reopened.SaveFinalizationConfirmationAsync(job.JobId, observedAbsent, rule, FinalizationOutcome.Passed, null, Operator);
        var resolved = Assert.IsType<FinalizationChecklist>(await reopened.LoadFinalizationAsync(job.JobId));
        Assert.False(Row(resolved, rule).IsBlocking);
        Assert.Equal(new long[] { 1, 2, 3 }, resolved.ArtifactScans.Select(scan => scan.Sequence));
        Assert.Equal(4, resolved.History.Count);
    }

    [Fact]
    public async Task ConcurrentStaleOtherJobAndMixedOriginScansAreRejected()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var otherJob = PersistenceTestData.CreateJob("other-002");
        var clock = new FixedTimeProvider(PersistenceTestData.Timestamp);
        await using var store = await OpenAsync(workspace, clock);
        await store.CreateJobAsync(job);
        await store.CreateJobAsync(otherJob);
        var gate = await RecordSanitizationAsync(store, job);
        await using var secondConnection = await OpenAsync(workspace, clock);
        var expected = Assert.IsType<FinalizationChecklist>(await secondConnection.LoadFinalizationAsync(job.JobId));
        var findings = Findings(WorkshopArtifactStatus.Unavailable);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.SaveFinalizationArtifactScanAsync(otherJob.JobId,
            expected, gate, findings, ProvenanceKind.SyntheticFixture));
        var unsanitized = Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(otherJob.JobId));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => store.SaveFinalizationArtifactScanAsync(otherJob.JobId,
            unsanitized, gate, findings, ProvenanceKind.SyntheticFixture));
        await store.SaveFinalizationArtifactScanAsync(job.JobId, expected, gate, findings, ProvenanceKind.SyntheticFixture);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => secondConnection.SaveFinalizationArtifactScanAsync(job.JobId,
            expected, gate, findings, ProvenanceKind.SyntheticFixture));
        var current = Assert.IsType<FinalizationChecklist>(await secondConnection.LoadFinalizationAsync(job.JobId));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => secondConnection.SaveFinalizationArtifactScanAsync(job.JobId,
            current, gate, findings, ProvenanceKind.ProviderObservation));
        await store.SetArchiveStateAsync(job.JobId, true, clock.GetUtcNow());
        await Assert.ThrowsAsync<JobStoreConflictException>(() => secondConnection.SaveFinalizationArtifactScanAsync(job.JobId,
            current, gate, findings, ProvenanceKind.SyntheticFixture));
        Assert.Single(Assert.IsType<FinalizationChecklist>(await store.LoadFinalizationAsync(job.JobId)).ArtifactScans);
    }

    private static WorkshopArtifactFinding[] Findings(WorkshopArtifactStatus package) =>
        Enum.GetValues<WorkshopArtifactKind>().Select(kind => new WorkshopArtifactFinding(kind,
            kind == WorkshopArtifactKind.PackageStagingResidue ? package : WorkshopArtifactStatus.Unavailable)).ToArray();

    private static FinalizationRow Row(FinalizationChecklist checklist, FinalizationRuleId rule) =>
        Assert.Single(checklist.Rows, row => row.Rule.Id == rule);

    private static async Task<SanitizationGateDecisionId> RecordSanitizationAsync(SqliteJobStore store, Job job)
    {
        var evidence = PersistenceTestData.CreateSanitization();
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));
        var decision = await store.RecordSanitizationGateDecisionAsync(
            SanitizationGate.Evaluate(job.JobId, evidence, PersistenceTestData.Timestamp));
        return decision.DecisionId;
    }

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, FixedTimeProvider clock,
        int? schemaVersion = null) => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            schemaVersion ?? SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000, () => 11);
}
