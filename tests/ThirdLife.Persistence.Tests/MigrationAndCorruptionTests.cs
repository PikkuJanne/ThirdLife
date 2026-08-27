using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class MigrationAndCorruptionTests
{
    [Fact]
    public async Task OversizedDatabaseIsRejectedBeforeSQLiteIntegrityScanning()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await using (var stream = new FileStream(
                         databasePath,
                         FileMode.Open,
                         FileAccess.Write,
                         FileShare.ReadWrite))
        {
            stream.SetLength(SqliteJobStore.MaximumDatabaseBytes + 1);
        }

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_size_limit_exceeded", exception.ResultCode);
        Assert.DoesNotContain(
            workspace.StoreRoot,
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingJournalWithoutItsDatabaseIsPreservedAndRejectedRepeatedly()
    {
        using var workspace = new PersistenceTestWorkspace();
        using var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot);
        var journalBytes = "synthetic-unpaired-journal"u8.ToArray();
        using (var createdJournal = layout.OpenJournalGuard(allowDeleteShare: true))
        {
        }
        await using (var journal = new FileStream(
                         layout.JournalPath,
                         FileMode.Open,
                         FileAccess.Write,
                         FileShare.Read))
        {
            await journal.WriteAsync(journalBytes);
        }

        var before = SqliteTestControl.HashFile(layout.JournalPath);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
                SqliteJobStore.OpenAsync(workspace.StoreRoot));

            Assert.Equal("store_identity_mismatch", exception.ResultCode);
            Assert.False(File.Exists(layout.DatabasePath));
            Assert.Equal(before, SqliteTestControl.HashFile(layout.JournalPath));
        }
    }

    [Theory]
    [InlineData("before_migration")]
    [InlineData("during_migration")]
    [InlineData("before_publish")]
    public async Task FirstRunFailureBeforePublicationLeavesNoRegisteredDatabaseAndRetrySucceeds(
        string mode)
    {
        using var workspace = new PersistenceTestWorkspace();
        var databasePath = Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3");
        var journalPath = string.Concat(databasePath, "-journal");
        var (faultPoint, detail) = mode switch
        {
            "before_migration" => (JobStoreFaultPoint.BeforeInitialStoreMigration, (int?)null),
            "during_migration" => (JobStoreFaultPoint.BeforeMigrationCommit, (int?)1),
            "before_publish" => (JobStoreFaultPoint.BeforeInitialStorePublish, (int?)null),
            _ => throw new InvalidOperationException("The first-run fault mode is unknown."),
        };

        await Assert.ThrowsAsync<InjectedPersistenceException>(() =>
            SqliteJobStore.OpenForTestingAsync(
                workspace.StoreRoot,
                SqliteJobStore.CurrentSchemaVersion,
                TimeProvider.System,
                new ThrowingFaultInjector(faultPoint, detail)));

        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists(journalPath));

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Equal(SqliteJobStore.CurrentSchemaVersion, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        Assert.Empty(EnumerateInitializationArtifacts(workspace.StoreRoot));
    }

    [Fact]
    public async Task CancellationBeforeFirstPublicationLeavesNoRegisteredDatabaseAndRetrySucceeds()
    {
        using var workspace = new PersistenceTestWorkspace();
        var databasePath = Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3");
        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeInitialStorePublish);
        using var cancellation = new CancellationTokenSource();
        var opening = SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker,
            cancellation.Token);

        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        blocker.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);

        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists(string.Concat(databasePath, "-journal")));
        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Empty(EnumerateInitializationArtifacts(workspace.StoreRoot));
    }

    [Fact]
    public async Task FailureAfterFirstPublicationLeavesACompleteSingleFileStoreThatReopens()
    {
        using var workspace = new PersistenceTestWorkspace();
        var databasePath = Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3");
        var journalPath = string.Concat(databasePath, "-journal");

        await Assert.ThrowsAsync<InjectedPersistenceException>(() =>
            SqliteJobStore.OpenForTestingAsync(
                workspace.StoreRoot,
                SqliteJobStore.CurrentSchemaVersion,
                TimeProvider.System,
                new ThrowingFaultInjector(JobStoreFaultPoint.AfterInitialStorePublish)));

        Assert.True(File.Exists(databasePath));
        Assert.False(File.Exists(journalPath));
        Assert.Empty(EnumerateInitializationArtifacts(workspace.StoreRoot));

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Equal(SqliteJobStore.CurrentSchemaVersion, await SqliteTestControl.ReadUserVersionAsync(databasePath));
    }

    [Fact]
    public async Task ConcurrentFirstOpenPublishesOneCompleteStoreAndBothCallersAdoptIt()
    {
        using var workspace = new PersistenceTestWorkspace();
        var blocker = new BlockingFaultInjector(JobStoreFaultPoint.BeforeInitialStorePublish);
        var firstOpen = SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            blocker);
        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
        var secondOpen = Task.Run(() => SqliteJobStore.OpenAsync(workspace.StoreRoot));

        await using var secondStore = await secondOpen.WaitAsync(TimeSpan.FromSeconds(15));
        blocker.Release();
        await using var firstStore = await firstOpen.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(firstStore.DatabasePath, secondStore.DatabasePath);
        Assert.Equal(
            SqliteJobStore.CurrentSchemaVersion,
            await SqliteTestControl.ReadUserVersionAsync(firstStore.DatabasePath));
        Assert.Empty(EnumerateInitializationArtifacts(workspace.StoreRoot));
    }

    [Fact]
    public async Task VersionOneStoreMigratesTransactionallyAndPreservesJob()
    {
        using var workspace = new PersistenceTestWorkspace();
        var time = new FixedTimeProvider(PersistenceTestData.Timestamp);
        var job = PersistenceTestData.CreateJob("legacy");
        string databasePath;

        await using (var versionOne = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         maximumMigrationVersion: 1,
                         time,
                         faultInjector: null))
        {
            databasePath = versionOne.DatabasePath;
        }

        using var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot);
        layout.EnsureJobDirectory(job.JobId);
        await SqliteTestControl.InsertVersionOneJobAsync(databasePath, job);
        Assert.Equal(1, await SqliteTestControl.ReadUserVersionAsync(databasePath));

        await using var migrated = await SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            time,
            faultInjector: null);
        var actual = await migrated.LoadJobAsync(job.JobId);

        Assert.Equal(SqliteJobStore.CurrentSchemaVersion, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        Assert.NotNull(actual);
        Assert.Equal(job, actual.Job);
        Assert.Empty(actual.Observations);
        Assert.Empty(actual.Checkpoints);
    }

    [Fact]
    public async Task FailedSecondMigrationLeavesCompleteVersionOneAndLaterRetrySucceeds()
    {
        using var workspace = new PersistenceTestWorkspace();
        var time = new FixedTimeProvider(PersistenceTestData.Timestamp);
        string databasePath;

        await using (var versionOne = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         maximumMigrationVersion: 1,
                         time,
                         faultInjector: null))
        {
            databasePath = versionOne.DatabasePath;
        }

        await Assert.ThrowsAsync<InjectedPersistenceException>(() =>
            SqliteJobStore.OpenForTestingAsync(
                workspace.StoreRoot,
                SqliteJobStore.CurrentSchemaVersion,
                time,
                new ThrowingFaultInjector(JobStoreFaultPoint.BeforeMigrationCommit, detail: 2)));

        Assert.Equal(1, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        await Assert.ThrowsAsync<SqliteException>(() =>
            SqliteTestControl.ExecuteAsync(databasePath, "SELECT COUNT(*) FROM evidence_records;"));

        await using var recovered = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Equal(SqliteJobStore.CurrentSchemaVersion, await SqliteTestControl.ReadUserVersionAsync(databasePath));
    }

    [Fact]
    public async Task ConcurrentUpgradeRevalidatesAfterTheMigrationLock()
    {
        using var workspace = new PersistenceTestWorkspace();
        var time = new FixedTimeProvider(PersistenceTestData.Timestamp);
        await using (var versionOne = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         maximumMigrationVersion: 1,
                         time,
                         faultInjector: null))
        {
        }

        var blocker = new BlockingFaultInjector(
            JobStoreFaultPoint.BeforeMigrationCommit,
            detail: 2);
        var firstOpen = SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion,
            time,
            blocker);
        await blocker.Reached.WaitAsync(TimeSpan.FromSeconds(10));
        var secondOpen = Task.Run(() => SqliteJobStore.OpenAsync(workspace.StoreRoot));

        blocker.Release();
        var stores = await Task.WhenAll(firstOpen, secondOpen).WaitAsync(TimeSpan.FromSeconds(15));
        foreach (var store in stores)
        {
            await store.DisposeAsync();
        }

        Assert.Equal(
            SqliteJobStore.CurrentSchemaVersion,
            await SqliteTestControl.ReadUserVersionAsync(
                Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3")));
    }

    [Fact]
    public async Task NewerSchemaIsRefusedWithoutChangingTheDatabase()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(databasePath, "PRAGMA user_version = 99;");
        var before = SqliteTestControl.HashFile(databasePath);

        var exception = await Assert.ThrowsAsync<JobStoreVersionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_newer_schema", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task MigrationDigestAndSchemaChangesAreDetectedWithoutAutomaticRepair()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(
            databasePath,
            "UPDATE schema_migrations SET script_sha256 = lower(hex(zeroblob(32))) WHERE version = 2;");
        var digestTampered = SqliteTestControl.HashFile(databasePath);

        var digestException = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));
        Assert.Equal("store_migration_mismatch", digestException.ResultCode);
        Assert.Equal(digestTampered, SqliteTestControl.HashFile(databasePath));

        string secondDatabase;
        await using (var secondStore = await SqliteJobStore.OpenAsync(workspace.GetPath("second-store")))
        {
            secondDatabase = secondStore.DatabasePath;
        }
        await SqliteTestControl.ExecuteAsync(secondDatabase, "CREATE TABLE unauthorized_schema_change (value TEXT);");
        var schemaTampered = SqliteTestControl.HashFile(secondDatabase);

        var schemaException = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.GetPath("second-store")));
        Assert.Equal("store_schema_mismatch", schemaException.ResultCode);
        Assert.Equal(schemaTampered, SqliteTestControl.HashFile(secondDatabase));
    }

    [Fact]
    public async Task InvalidDatabaseBytesAreReportedAndPreserved()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        var corruptBytes = Enumerable.Range(0, 257).Select(value => (byte)(value % 251)).ToArray();
        await File.WriteAllBytesAsync(databasePath, corruptBytes);
        var before = SqliteTestControl.HashFile(databasePath);

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_corrupt", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(databasePath));
        Assert.DoesNotContain(workspace.StoreRoot, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PayloadTamperingIsDetectedOnReadWithoutRewritingTheJob()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            databasePath = store.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(
            databasePath,
            "UPDATE jobs SET job_payload_json = replace(job_payload_json, 'device-synthetic-001', 'device-synthetic-999');");
        var before = SqliteTestControl.HashFile(databasePath);

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            reopened.LoadJobAsync(job.JobId));

        Assert.Equal("store_payload_mismatch", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task UnrelatedSQLiteDatabaseIsNotAdoptedAsAJobStore()
    {
        using var workspace = new PersistenceTestWorkspace();
        using var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot);
        string databasePath;
        using (var guard = layout.OpenDatabaseGuard())
        {
            databasePath = layout.DatabasePath;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE unrelated (value TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        var before = SqliteTestControl.HashFile(databasePath);
        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_identity_mismatch", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task TruncatedFormerStoreIsNotReinitializedAndItsZeroBytesArePreserved()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await File.WriteAllBytesAsync(databasePath, []);
        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_identity_mismatch", exception.ResultCode);
        Assert.Empty(await File.ReadAllBytesAsync(databasePath));
    }

    [Fact]
    public async Task PreexistingEmptySQLiteFileIsNotClaimedAsANewStore()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        string journalPath;
        using (var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot))
        using (var database = layout.OpenDatabaseGuard())
        {
            databasePath = layout.DatabasePath;
            journalPath = layout.JournalPath;
        }

        Assert.Empty(await File.ReadAllBytesAsync(databasePath));
        Assert.False(File.Exists(journalPath));
        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_identity_mismatch", exception.ResultCode);
        Assert.Empty(await File.ReadAllBytesAsync(databasePath));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public async Task LedgerlessNewerStoreIsRefusedBeforeAnySchemaChange()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(
            databasePath,
            "DROP TABLE schema_migrations; PRAGMA user_version = 99;");
        var before = SqliteTestControl.HashFile(databasePath);
        var exception = await Assert.ThrowsAsync<JobStoreVersionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_newer_schema", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task VersionOneSchemaTamperIsNotLaunderedByTheNextMigration()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var versionOne = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         maximumMigrationVersion: 1,
                         TimeProvider.System,
                         faultInjector: null))
        {
            databasePath = versionOne.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(databasePath, "CREATE TABLE unauthorized_before_upgrade (value TEXT);");
        var before = SqliteTestControl.HashFile(databasePath);
        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_schema_mismatch", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
        Assert.Equal(1, await SqliteTestControl.ReadUserVersionAsync(databasePath));
    }

    [Theory]
    [InlineData("schema_sha256", "lower(hex(zeroblob(32)))")]
    [InlineData("applied_at_utc", "'00000000000000000000'")]
    public async Task EveryHistoricalMigrationFieldIsValidated(string column, string replacement)
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(
            databasePath,
            $"UPDATE schema_migrations SET {column} = {replacement} WHERE version = 1;");
        var before = SqliteTestControl.HashFile(databasePath);
        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_migration_mismatch", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task OversizedMigrationLedgerFailsAsStableCorruptionWithoutMutation()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        await SqliteTestControl.ExecuteAsync(
            databasePath,
            """
            INSERT INTO schema_migrations (
                version,
                migration_name,
                script_sha256,
                schema_sha256,
                applied_at_utc)
            SELECT
                3,
                '003_unapproved.sql',
                script_sha256,
                schema_sha256,
                applied_at_utc
            FROM schema_migrations
            WHERE version = 2;
            """);
        var before = SqliteTestControl.HashFile(databasePath);

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_migration_mismatch", exception.ResultCode);
        Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
    }

    [Fact]
    public async Task InvalidArchiveProjectionIsAStableCorruptionResultWithoutRawValues()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("invalid-archive");
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);
        await SqliteTestControl.ExecuteAsync(
            store.DatabasePath,
            "PRAGMA ignore_check_constraints = ON; UPDATE jobs SET is_archived = 2;");

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            store.LoadJobAsync(job.JobId));

        Assert.Equal("store_payload_mismatch", exception.ResultCode);
        Assert.DoesNotContain("2", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.StoreRoot, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidCheckpointIdentifierIsMappedToAStableCorruptionResult()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("invalid-checkpoint");
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);
        await SqliteTestControl.ExecuteAsync(
            store.DatabasePath,
            "UPDATE store_checkpoints SET checkpoint_id = '../bad';");

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            store.LoadJobAsync(job.JobId));

        Assert.Equal("store_payload_mismatch", exception.ResultCode);
        Assert.DoesNotContain("../bad", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UPDATE store_checkpoints SET checkpoint_kind = 'restored' WHERE checkpoint_kind = 'job_created';")]
    [InlineData("INSERT INTO store_checkpoints (checkpoint_id, job_id, checkpoint_kind, recorded_at_utc) SELECT 'checkpoint-impossible-archive-1', job_id, 'archived', '2030-01-01T08:10:00.0000000+00:00' FROM jobs; INSERT INTO store_checkpoints (checkpoint_id, job_id, checkpoint_kind, recorded_at_utc) SELECT 'checkpoint-impossible-archive-2', job_id, 'archived', '2030-01-01T08:20:00.0000000+00:00' FROM jobs; UPDATE jobs SET is_archived = 1, archived_at_utc = '2030-01-01T08:20:00.0000000+00:00';")]
    public async Task ImpossibleArchiveCheckpointHistoryIsAStableCorruptionResult(string mutation)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("invalid-history");
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        await store.CreateJobAsync(job);
        await SqliteTestControl.ExecuteAsync(store.DatabasePath, mutation);

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            store.LoadJobAsync(job.JobId));

        Assert.Equal("store_payload_mismatch", exception.ResultCode);
    }

    [Fact]
    public async Task EmptyOrphanJobDirectoryIsReconciledOnTheNextOpen()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
        }

        string orphanPath;
        using (var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot))
        {
            orphanPath = layout.EnsureJobDirectory(PersistenceTestData.CreateJob("orphan").JobId);
        }

        Assert.True(Directory.Exists(orphanPath));
        await using (var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
        }

        Assert.False(Directory.Exists(orphanPath));
    }

    private static string[] EnumerateInitializationArtifacts(string rootPath) =>
        Directory.Exists(rootPath)
            ? Directory.EnumerateFileSystemEntries(rootPath, string.Concat(RestrictedStoreLayout.InitializationFilePrefix, "*"))
                .ToArray()
            : [];
}
