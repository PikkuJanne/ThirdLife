using System.Runtime.Versioning;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class PathSecurityTests
{
    [Fact]
    public async Task LivePersistJournalCannotBeRenamedOrReplaced()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var journalPath = string.Concat(store.DatabasePath, "-journal");
        var replacementPath = workspace.GetPath("replacement-journal");

        Assert.Throws<IOException>(() => File.Move(journalPath, replacementPath));
        Assert.True(File.Exists(journalPath));
        Assert.False(File.Exists(replacementPath));
    }

    [Fact]
    public async Task LockedJournalFailureUsesOnlyTheStableSanitizedPathResult()
    {
        using var workspace = new PersistenceTestWorkspace();
        string journalPath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            journalPath = string.Concat(store.DatabasePath, "-journal");
        }

        using var blocker = new FileStream(
            journalPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_path_rejected", exception.ResultCode);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            workspace.StoreRoot,
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("relative-store")]
    [InlineData("\\\\server\\share\\store")]
    [InlineData("\\\\?\\C:\\store")]
    [InlineData("C:\\")]
    [InlineData("C:\\store:alternate")]
    public async Task UnsafeRootsAreRejectedBeforeStoreCreation(string root)
    {
        var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
            SqliteJobStore.OpenAsync(root));

        Assert.Equal("store_path_rejected", exception.ResultCode);
        Assert.DoesNotContain(root, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../recipient")]
    [InlineData("..\\recipient")]
    [InlineData("C:\\recipient")]
    [InlineData("recipient:name")]
    [InlineData("CON")]
    public void TraversalAndReservedJobIdentifiersAreRejectedByTheTypedBoundary(string value)
    {
        Assert.Throws<ArgumentException>(() => new JobId(value));
    }

    [Fact]
    public async Task CreatedStoreAndJobDirectoriesUseProtectedRestrictedAcls()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        string jobDirectory;
        string databasePath;

        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            jobDirectory = store.GetJobDirectoryPath(job.JobId);
            databasePath = store.DatabasePath;
        }

        SqliteTestControl.AssertProtectedAcl(workspace.StoreRoot);
        SqliteTestControl.AssertProtectedAcl(Path.Combine(workspace.StoreRoot, "jobs"));
        SqliteTestControl.AssertProtectedAcl(jobDirectory);
        SqliteTestControl.AssertProtectedFileAcl(databasePath);
        SqliteTestControl.AssertProtectedFileAcl(string.Concat(databasePath, "-journal"));
    }

    [Fact]
    public async Task RootJunctionIsRejectedWithoutTouchingItsTarget()
    {
        using var workspace = new PersistenceTestWorkspace();
        var outside = workspace.GetPath("outside");
        var junction = workspace.GetPath("root-junction");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "outside-remains-unchanged");
        await SqliteTestControl.CreateJunctionAsync(junction, outside);

        try
        {
            await Assert.ThrowsAsync<JobStorePathException>(() => SqliteJobStore.OpenAsync(junction));
            Assert.Equal("outside-remains-unchanged", await File.ReadAllTextAsync(sentinel));
            Assert.False(File.Exists(Path.Combine(outside, "thirdlife-jobs.sqlite3")));
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction, recursive: false);
            }
        }
    }

    [Fact]
    public async Task JobDirectoryJunctionIsRejectedOnReadAndOutsideTargetIsUntouched()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob();
        string jobDirectory;

        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            jobDirectory = store.GetJobDirectoryPath(job.JobId);
        }

        Directory.Delete(jobDirectory, recursive: false);
        var outside = workspace.GetPath("outside-job");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "outside-remains-unchanged");
        await SqliteTestControl.CreateJunctionAsync(jobDirectory, outside);

        try
        {
            var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
                SqliteJobStore.OpenAsync(workspace.StoreRoot));

            Assert.Equal("store_path_rejected", exception.ResultCode);
            Assert.Equal("outside-remains-unchanged", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            if (Directory.Exists(jobDirectory))
            {
                Directory.Delete(jobDirectory, recursive: false);
            }
        }
    }

    [Fact]
    public async Task HardLinkedDatabaseIsRejectedBeforeSQLiteUse()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        var hardLink = workspace.GetPath("database-hard-link.sqlite3");
        await SqliteTestControl.CreateHardLinkAsync(hardLink, databasePath);
        var before = SqliteTestControl.HashFile(databasePath);

        try
        {
            var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
                SqliteJobStore.OpenAsync(workspace.StoreRoot));
            Assert.Equal("store_path_rejected", exception.ResultCode);
            Assert.Equal(before, SqliteTestControl.HashFile(databasePath));
        }
        finally
        {
            if (File.Exists(hardLink))
            {
                File.Delete(hardLink);
            }
        }
    }

    [Fact]
    public async Task HardLinkedInitializationLookalikeIsPreservedAndNeverReconciled()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        string initializationPath;
        using (var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot))
        {
            databasePath = layout.DatabasePath;
            initializationPath = Path.Combine(
                workspace.StoreRoot,
                string.Concat(
                    RestrictedStoreLayout.InitializationFilePrefix,
                    new string('a', 32),
                    ".tmp"));
        }

        var outside = workspace.GetPath("outside-initialization-content");
        await File.WriteAllTextAsync(outside, "outside-remains-unchanged");
        await SqliteTestControl.CreateHardLinkAsync(initializationPath, outside);
        var before = SqliteTestControl.HashFile(outside);

        try
        {
            var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
                SqliteJobStore.OpenAsync(workspace.StoreRoot));

            Assert.Equal("store_path_rejected", exception.ResultCode);
            Assert.False(File.Exists(databasePath));
            Assert.True(File.Exists(initializationPath));
            Assert.Equal(before, SqliteTestControl.HashFile(outside));
        }
        finally
        {
            if (File.Exists(initializationPath))
            {
                File.Delete(initializationPath);
            }
        }
    }

    [Fact]
    public void InitializationPublicationRejectsAPathReplacementWhileTheValidatedHandleIsPinned()
    {
        using var workspace = new PersistenceTestWorkspace();
        using var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot);
        var initialized = layout.CreateInitializationStoreFiles();
        var replacement = layout.CreateInitializationStoreFiles();
        var displacedPath = Path.Combine(
            workspace.StoreRoot,
            string.Concat(
                RestrictedStoreLayout.InitializationFilePrefix,
                new string('c', 32),
                ".tmp"));

        try
        {
            replacement.DatabaseGuard.Dispose();
            File.Move(initialized.DatabasePath, displacedPath);
            File.Move(replacement.DatabasePath, initialized.DatabasePath);

            var exception = Assert.Throws<JobStorePathException>(() =>
                layout.TryPublishInitializationDatabase(
                    initialized.DatabasePath,
                    initialized.DatabaseGuard));

            Assert.Equal("store_path_rejected", exception.ResultCode);
            Assert.False(File.Exists(layout.DatabasePath));
        }
        finally
        {
            initialized.JournalGuard.Dispose();
            initialized.DatabaseGuard.Dispose();
            replacement.JournalGuard.Dispose();
            replacement.DatabaseGuard.Dispose();
            layout.ReconcileInitializationArtifacts(SqliteJobStore.MaximumDatabaseBytes);
        }
    }

    [Fact]
    public async Task PrefixLookalikeScanIsBoundedBeforeTokenValidation()
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        using (var layout = RestrictedStoreLayout.CreateOrOpen(workspace.StoreRoot))
        {
            databasePath = layout.DatabasePath;
        }

        for (var index = 0; index < 65; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(
                    workspace.StoreRoot,
                    string.Concat(RestrictedStoreLayout.InitializationFilePrefix, "invalid-", index)),
                "not-owned");
        }

        var exception = await Assert.ThrowsAsync<JobStoreCorruptionException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_record_limit_exceeded", exception.ResultCode);
        Assert.False(File.Exists(databasePath));
    }

    [Fact]
    public async Task DatabaseHardLinkAddedWhileStoreIsOpenIsRejectedBeforeTheNextOperation()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("live-hard-link");
        var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var hardLink = workspace.GetPath("live-database-hard-link.sqlite3");
        try
        {
            await store.CreateJobAsync(job);
            await SqliteTestControl.CreateHardLinkAsync(hardLink, store.DatabasePath);

            var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
                store.LoadJobAsync(job.JobId));
            Assert.Equal("store_path_rejected", exception.ResultCode);
        }
        finally
        {
            await store.DisposeAsync();
            if (File.Exists(hardLink))
            {
                File.Delete(hardLink);
            }
        }
    }

    [Fact]
    public async Task HardLinkedPersistentJournalIsRejectedBeforeRecoveryOrMigration()
    {
        using var workspace = new PersistenceTestWorkspace();
        string journalPath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            journalPath = string.Concat(store.DatabasePath, "-journal");
        }

        var hardLink = workspace.GetPath("journal-hard-link");
        await SqliteTestControl.CreateHardLinkAsync(hardLink, journalPath);
        try
        {
            var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
                SqliteJobStore.OpenAsync(workspace.StoreRoot));
            Assert.Equal("store_path_rejected", exception.ResultCode);
        }
        finally
        {
            if (File.Exists(hardLink))
            {
                File.Delete(hardLink);
            }
        }
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    public async Task UnexpectedJournalModeSidecarsAreRejected(string suffix)
    {
        using var workspace = new PersistenceTestWorkspace();
        string databasePath;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            databasePath = store.DatabasePath;
        }

        var sidecar = string.Concat(databasePath, suffix);
        await File.WriteAllTextAsync(sidecar, "synthetic-unexpected-sidecar");
        var before = await File.ReadAllTextAsync(sidecar);

        var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));
        Assert.Equal("store_path_rejected", exception.ResultCode);
        Assert.Equal(before, await File.ReadAllTextAsync(sidecar));
    }

    [Fact]
    public async Task JunctionInAnIntermediateParentIsRejectedWithoutCreatingAStoreInItsTarget()
    {
        using var workspace = new PersistenceTestWorkspace();
        var outside = workspace.GetPath("outside-parent");
        var junction = workspace.GetPath("parent-junction");
        Directory.CreateDirectory(outside);
        await SqliteTestControl.CreateJunctionAsync(junction, outside);
        var requestedRoot = Path.Combine(junction, "store");

        try
        {
            await Assert.ThrowsAsync<JobStorePathException>(() => SqliteJobStore.OpenAsync(requestedRoot));
            Assert.False(Directory.Exists(Path.Combine(outside, "store")));
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction, recursive: false);
            }
        }
    }

    [Fact]
    public async Task PreexistingBroadStoreDirectoryIsNotSilentlyTakenOver()
    {
        using var workspace = new PersistenceTestWorkspace();
        Directory.CreateDirectory(workspace.StoreRoot);

        var exception = await Assert.ThrowsAsync<JobStorePathException>(() =>
            SqliteJobStore.OpenAsync(workspace.StoreRoot));

        Assert.Equal("store_path_rejected", exception.ResultCode);
        Assert.False(File.Exists(Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3")));
    }
}
