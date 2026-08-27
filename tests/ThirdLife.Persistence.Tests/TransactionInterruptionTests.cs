using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class TransactionInterruptionTests
{
    [Theory]
    [InlineData("initial_before_migration", false)]
    [InlineData("initial_migration_before_commit", false)]
    [InlineData("initial_before_publish", false)]
    [InlineData("initial_after_publish", true)]
    public async Task ProcessKillDuringFirstRunPublishesOnlyACompleteDatabase(
        string mode,
        bool expectedPublished)
    {
        using var workspace = new PersistenceTestWorkspace();
        var databasePath = Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3");
        var journalPath = string.Concat(databasePath, "-journal");

        await RunAndKillChildAsync(workspace, mode);

        Assert.Equal(expectedPublished, File.Exists(databasePath));
        Assert.False(File.Exists(journalPath));
        if (!expectedPublished)
        {
            var initializationArtifacts = EnumerateInitializationArtifacts(workspace.StoreRoot);
            Assert.NotEmpty(initializationArtifacts);
            foreach (var artifact in initializationArtifacts)
            {
                SqliteTestControl.AssertProtectedFileAcl(artifact);
            }
        }

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Equal(SqliteJobStore.CurrentSchemaVersion, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        Assert.Empty(EnumerateInitializationArtifacts(workspace.StoreRoot));
    }

    [Theory]
    [InlineData("after_first_insert", false)]
    [InlineData("before_commit", false)]
    [InlineData("after_commit", true)]
    public async Task ProcessKillAroundWriteCommitLeavesNoneOrTheCompleteBatch(
        string mode,
        bool expectedCommitted)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("crash");

        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
        }

        await RunAndKillChildAsync(workspace, mode);

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = await reopened.LoadJobAsync(job.JobId);
        Assert.NotNull(actual);

        if (expectedCommitted)
        {
            Assert.Equal(2, actual.Observations.Count);
            Assert.Equal(2, actual.Checkpoints.Count);
        }
        else
        {
            Assert.Empty(actual.Observations);
            Assert.Single(actual.Checkpoints);
        }
    }

    [Fact]
    public async Task ProcessKillBeforeMigrationCommitLeavesThePriorSchemaComplete()
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

        await RunAndKillChildAsync(workspace, "migration_before_commit");

        Assert.Equal(1, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        await using (var stillVersionOne = await SqliteJobStore.OpenForTestingAsync(
                         workspace.StoreRoot,
                         maximumMigrationVersion: 1,
                         TimeProvider.System,
                         faultInjector: null))
        {
            Assert.Equal(1, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        }

        await using var migrated = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.Equal(SqliteJobStore.CurrentSchemaVersion, await SqliteTestControl.ReadUserVersionAsync(databasePath));
    }

    private static async Task RunAndKillChildAsync(PersistenceTestWorkspace workspace, string mode)
    {
        var markerPath = workspace.GetPath(string.Concat("child-ready-", mode, ".marker"));
        var assemblyPath = typeof(TransactionCrashChildTests).Assembly.Location;
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(dotnetHost) ? "dotnet" : dotnetHost,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add(
            "/TestCaseFilter:FullyQualifiedName=ThirdLife.Persistence.Tests.TransactionCrashChildTests.ChildBlocksAtConfiguredFaultPoint");
        startInfo.ArgumentList.Add("--logger:console;verbosity=quiet");
        startInfo.Environment[TransactionCrashChildTests.ModeVariable] = mode;
        startInfo.Environment[TransactionCrashChildTests.RootVariable] = workspace.StoreRoot;
        startInfo.Environment[TransactionCrashChildTests.MarkerVariable] = markerPath;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The bounded transaction-interruption child did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!File.Exists(markerPath) && !process.HasExited && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            if (!File.Exists(markerPath))
            {
                var exitState = process.HasExited
                    ? process.ExitCode.ToString(CultureInfo.InvariantCulture)
                    : "running";
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                }
                var childOutput = await standardOutput.WaitAsync(TimeSpan.FromSeconds(5));
                var childError = await standardError.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The interruption child did not reach its bounded checkpoint; exit={exitState}; output={Bound(childOutput)}; error={Bound(childError)}."));
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            _ = await standardOutput.ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    private static string Bound(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 512 ? normalized : normalized[..512];
    }

    private static string[] EnumerateInitializationArtifacts(string rootPath) =>
        Directory.Exists(rootPath)
            ? Directory.EnumerateFileSystemEntries(rootPath, string.Concat(RestrictedStoreLayout.InitializationFilePrefix, "*"))
                .ToArray()
            : [];
}

[SupportedOSPlatform("windows")]
public sealed class TransactionCrashChildTests
{
    internal const string MarkerVariable = "THIRDLIFE_TL0102_CHILD_MARKER";
    internal const string ModeVariable = "THIRDLIFE_TL0102_CHILD_MODE";
    internal const string RootVariable = "THIRDLIFE_TL0102_CHILD_ROOT";

    [Fact]
    public async Task ChildBlocksAtConfiguredFaultPoint()
    {
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (string.IsNullOrEmpty(mode))
        {
            return;
        }

        var root = Environment.GetEnvironmentVariable(RootVariable)
            ?? throw new InvalidOperationException("The child root was not supplied.");
        var marker = Environment.GetEnvironmentVariable(MarkerVariable)
            ?? throw new InvalidOperationException("The child marker was not supplied.");
        var (point, detail) = mode switch
        {
            "initial_before_migration" => (JobStoreFaultPoint.BeforeInitialStoreMigration, (int?)null),
            "initial_migration_before_commit" => (JobStoreFaultPoint.BeforeMigrationCommit, (int?)1),
            "initial_before_publish" => (JobStoreFaultPoint.BeforeInitialStorePublish, (int?)null),
            "initial_after_publish" => (JobStoreFaultPoint.AfterInitialStorePublish, (int?)null),
            "after_first_insert" => (JobStoreFaultPoint.AfterFirstEvidenceInsert, (int?)null),
            "before_commit" => (JobStoreFaultPoint.BeforeWriteCommit, (int?)null),
            "after_commit" => (JobStoreFaultPoint.AfterWriteCommit, (int?)null),
            "migration_before_commit" => (JobStoreFaultPoint.BeforeMigrationCommit, (int?)2),
            _ => throw new InvalidOperationException("The child interruption mode is unknown."),
        };
        var injector = new ProcessBlockingFaultInjector(point, detail, marker);

        if (mode.StartsWith("initial_", StringComparison.Ordinal) ||
            string.Equals(mode, "migration_before_commit", StringComparison.Ordinal))
        {
            await using var ignored = await SqliteJobStore.OpenForTestingAsync(
                root,
                SqliteJobStore.CurrentSchemaVersion,
                TimeProvider.System,
                injector);
            return;
        }

        var job = PersistenceTestData.CreateJob("crash");
        await using var store = await SqliteJobStore.OpenForTestingAsync(
            root,
            SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System,
            injector);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            job.JobId,
            [
                PersistenceTestData.CreateObservation("crash-first"),
                PersistenceTestData.CreateObservation("crash-second"),
            ]));
    }

    private sealed class ProcessBlockingFaultInjector : IJobStoreFaultInjector
    {
        private readonly int? _detail;
        private readonly string _markerPath;
        private readonly JobStoreFaultPoint _point;

        public ProcessBlockingFaultInjector(JobStoreFaultPoint point, int? detail, string markerPath)
        {
            _point = point;
            _detail = detail;
            _markerPath = markerPath;
        }

        public async ValueTask OnFaultPointAsync(
            JobStoreFaultPoint point,
            int detail,
            CancellationToken cancellationToken)
        {
            if (point != _point || (_detail is not null && detail != _detail.Value))
            {
                return;
            }

            var pendingMarker = string.Concat(_markerPath, ".pending");
            await using (var marker = new FileStream(
                pendingMarker,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await marker.WriteAsync(Encoding.ASCII.GetBytes("ready"), cancellationToken);
                await marker.FlushAsync(cancellationToken);
                marker.Flush(flushToDisk: true);
            }
            File.Move(pendingMarker, _markerPath);

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
