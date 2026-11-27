using System.Runtime.Versioning;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Support;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class SupportExportAuditStoreTests
{
    private const string SupportId = "SUP-17896489b8634006a85fcf139d64dba6";
    private const string OtherSupportId = "SUP-0c61bc519f3946dc8cfa0862a10bba52";
    private static readonly OperatorId Operator = new("operator-support-fixture");

    [Fact]
    public async Task MigrationAndInterruptionRetainTheExactPendingAndPublishedReceipt()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        var intent = Audit(SupportExportState.Pending);
        var completed = Audit(SupportExportState.Published, offset: TimeSpan.FromSeconds(4));

        await using (var prior = await OpenAsync(workspace, schemaVersion: 17))
        {
            await prior.CreateJobAsync(job);
            Assert.Empty(await prior.LoadSupportExportsAsync(job.JobId));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => prior.StageSupportExportAsync(job.JobId, intent));
        }

        await using (var migrated = await OpenAsync(workspace))
        {
            Assert.Equal(job, (await migrated.LoadJobAsync(job.JobId))?.Job);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.RecordSupportExportAsync(job.JobId, completed));
            await migrated.StageSupportExportAsync(job.JobId, intent);
            await Assert.ThrowsAsync<JobStoreConflictException>(() => migrated.StageSupportExportAsync(job.JobId, intent));
        }

        await using (var resumed = await OpenAsync(workspace))
        {
            Assert.Equal(intent, Assert.Single(await resumed.LoadSupportExportsAsync(job.JobId)));
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                resumed.RecordSupportExportAsync(job.JobId, Audit(SupportExportState.Published, digest: new string('b', 64))));
            await resumed.SetArchiveStateAsync(job.JobId, true, PersistenceTestData.Timestamp.AddSeconds(3));
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                resumed.StageSupportExportAsync(job.JobId, Audit(SupportExportState.Pending, OtherSupportId)));
            await resumed.RecordSupportExportAsync(job.JobId, completed);
            await resumed.RecordSupportExportAsync(job.JobId, completed);
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                resumed.RecordSupportExportAsync(job.JobId, Audit(SupportExportState.NotPublished)));
        }

        await using (var reopened = await OpenAsync(workspace))
        {
            Assert.Equal(completed, Assert.Single(await reopened.LoadSupportExportsAsync(job.JobId)));
        }
    }

    [Fact]
    public async Task ARecordedNonpublicationRemainsTerminal()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        await using var store = await OpenAsync(workspace);
        await store.CreateJobAsync(job);
        var pending = Audit(SupportExportState.Pending);
        var notPublished = Audit(SupportExportState.NotPublished);
        await store.StageSupportExportAsync(job.JobId, pending);
        await store.RecordSupportExportAsync(job.JobId, notPublished);

        Assert.Equal(notPublished, Assert.Single(await store.LoadSupportExportsAsync(job.JobId)));
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            store.RecordSupportExportAsync(job.JobId, Audit(SupportExportState.Published)));
    }

    private static SupportExportAudit Audit(SupportExportState state, string supportId = SupportId,
        string? digest = null, TimeSpan offset = default) =>
        new(supportId, "thirdlife.support.v1", "thirdlife.support-manifest.v1", digest ?? new string('a', 64),
            PersistenceTestData.Timestamp + offset, Operator, state);

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, int? schemaVersion = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, schemaVersion ?? SqliteJobStore.CurrentSchemaVersion,
            new FixedTimeProvider(PersistenceTestData.Timestamp), null);
}
