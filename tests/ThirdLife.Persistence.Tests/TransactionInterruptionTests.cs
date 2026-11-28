using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

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

    [Theory]
    [InlineData("guarded_after_first_insert", false)]
    [InlineData("guarded_before_commit", false)]
    [InlineData("guarded_after_commit", true)]
    public async Task ProcessKillAroundGuardedProviderBatchPreservesPriorEvidenceAndGate(
        string mode,
        bool expectedCommitted)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("guarded-crash");
        var sanitization = PersistenceTestData.CreateSanitization("guarded-crash");
        StoredJob before;
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await initializer.CreateJobAsync(job);
            await initializer.AppendEvidenceAsync(new JobEvidenceBatch(
                job.JobId, sanitizationEvidence: [sanitization]));
            var decision = await initializer.RecordSanitizationGateDecisionAsync(
                SanitizationGate.Evaluate(job.JobId, sanitization, PersistenceTestData.Timestamp.AddMinutes(2)));
            await initializer.AppendEvidenceAsync(new JobEvidenceBatch(
                job.JobId,
                [
                    PersistenceTestData.CreateObservation("guarded-prior-first"),
                    PersistenceTestData.CreateObservation("guarded-prior-second"),
                ],
                expectedSanitizationGateDecisionId: decision.DecisionId));
            before = (await initializer.LoadJobAsync(job.JobId))!;
        }

        await RunAndKillChildAsync(workspace, mode);

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = await reopened.LoadJobAsync(job.JobId);
        Assert.NotNull(actual);
        Assert.Equal(before.Job, actual.Job);
        Assert.Equal(before.Observations, actual.Observations.Take(before.Observations.Count));
        Assert.Equal(before.Checkpoints, actual.Checkpoints.Take(before.Checkpoints.Count));
        Assert.Equal(before.SanitizationEvidence, actual.SanitizationEvidence);
        Assert.Equal(before.SanitizationGateDecisions, actual.SanitizationGateDecisions);
        Assert.True(SanitizationGate.Inspect(actual).AllowsAssessment);
        Assert.Equal(expectedCommitted ? 4 : 2, actual.Observations.Count);
        Assert.Equal(expectedCommitted ? 4 : 3, actual.Checkpoints.Count);
        if (expectedCommitted)
        {
            Assert.Equal(PersistenceTestData.CreateObservation("guarded-next-first"), actual.Observations[2]);
            Assert.Equal(PersistenceTestData.CreateObservation("guarded-next-second"), actual.Observations[3]);
            Assert.Equal(JobCheckpointKind.EvidenceCommitted, actual.Checkpoints[^1].Kind);
        }
        Assert.Equal(
            SqliteJobStore.CurrentSchemaVersion,
            await SqliteTestControl.ReadUserVersionAsync(reopened.DatabasePath));
    }

    [Theory]
    [InlineData("job_before_commit", false)]
    [InlineData("job_after_commit", true)]
    public async Task ProcessKillAroundJobCreationReopensAsAbsentOrComplete(
        string mode,
        bool expectedCommitted)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("job-crash");
        await using (var initializer = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
        }

        await RunAndKillChildAsync(workspace, mode);

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = await reopened.LoadJobAsync(job.JobId);
        Assert.Equal(expectedCommitted, actual is not null);
        if (actual is not null)
        {
            Assert.Equal(job, actual.Job);
            Assert.Single(actual.Checkpoints);
        }
        else
        {
            await reopened.CreateJobAsync(job);
            Assert.NotNull(await reopened.LoadJobAsync(job.JobId));
        }
    }

    [Theory]
    [InlineData("gate_before_commit", false)]
    [InlineData("gate_after_commit", true)]
    public async Task ProcessKillAroundGateRecordingLeavesNoPartialDecision(
        string mode,
        bool expectedCommitted)
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("gate-crash");
        var evidence = PersistenceTestData.CreateSanitization("gate-crash");

        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, sanitizationEvidence: [evidence]));
        }

        await RunAndKillChildAsync(workspace, mode);

        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        var actual = await reopened.LoadJobAsync(job.JobId);
        Assert.NotNull(actual);
        Assert.Equal(expectedCommitted ? 1 : 0, actual.SanitizationGateDecisions.Count);
        Assert.Equal(
            expectedCommitted
                ? SanitizationGateReason.SanitizationVerified
                : SanitizationGateReason.GateDecisionMissing,
            SanitizationGate.Inspect(actual).Reason);
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

    [Fact]
    public async Task ProcessKillAfterOwnedFileRemovalReopensTheApprovedJobBeforeDatabaseCommit()
    {
        using var workspace = new PersistenceTestWorkspace();
        var job = PersistenceTestData.CreateJob("deletion-crash");
        var file = TransactionCrashChildTests.DeletionFixtureFile;
        var path = workspace.GetPath(file.FileName);
        var actor = new OperatorId("operator-crash-fixture");
        CheckpointId checkpoint;
        await using (var store = await SqliteJobStore.OpenAsync(workspace.StoreRoot))
        {
            await store.CreateJobAsync(job);
            await store.PublishJobLocalFileAsync(job.JobId, file,
                _ => File.WriteAllBytes(path, TransactionCrashChildTests.DeletionFixtureBytes.ToArray()));
            await store.SetArchiveStateAsync(job.JobId, true, PersistenceTestData.Timestamp.AddMinutes(2));
            checkpoint = Assert.IsType<StoredJob>(await store.LoadJobAsync(job.JobId)).Checkpoints[^1].CheckpointId;
            var review = Assert.IsType<ArchivedJobDeletionReview>(await store.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
            await store.ApproveArchivedJobDeletionAsync(review, actor);
        }

        await RunAndKillChildAsync(workspace, "deletion_before_commit");

        Assert.False(File.Exists(path));
        await using var reopened = await SqliteJobStore.OpenAsync(workspace.StoreRoot);
        Assert.NotNull(await reopened.LoadJobAsync(job.JobId));
        var approval = Assert.IsType<ArchivedJobDeletionReview>(await reopened.ReviewArchivedJobDeletionAsync(job.JobId, checkpoint));
        Assert.Equal(actor, approval.ApprovedBy);
        Assert.Equal(file, Assert.Single(approval.Files));
        var removed = await reopened.ExecuteArchivedJobDeletionAsync(job.JobId, checkpoint, (expected, _) =>
        {
            Assert.Equal(file, expected);
            Assert.False(File.Exists(path));
            return JobLocalFileRemoval.Missing;
        });
        Assert.True(removed.Completed);
        Assert.Null(await reopened.LoadJobAsync(job.JobId));
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
    internal static ReadOnlySpan<byte> DeletionFixtureBytes => "ThirdLife synthetic retirement crash fixture"u8;
    internal static JobLocalFile DeletionFixtureFile { get; } = new(JobLocalFileKind.WorkshopReport,
        "workshop-00000000000000000000000000000001.txt", DeletionFixtureBytes.Length,
        Convert.ToHexStringLower(SHA256.HashData(DeletionFixtureBytes)));

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
            "guarded_after_first_insert" => (JobStoreFaultPoint.AfterFirstEvidenceInsert, (int?)null),
            "guarded_before_commit" => (JobStoreFaultPoint.BeforeWriteCommit, (int?)null),
            "guarded_after_commit" => (JobStoreFaultPoint.AfterWriteCommit, (int?)null),
            "job_before_commit" => (JobStoreFaultPoint.BeforeWriteCommit, (int?)null),
            "job_after_commit" => (JobStoreFaultPoint.AfterWriteCommit, (int?)null),
            "gate_before_commit" => (JobStoreFaultPoint.BeforeWriteCommit, (int?)null),
            "gate_after_commit" => (JobStoreFaultPoint.AfterWriteCommit, (int?)null),
            "migration_before_commit" => (JobStoreFaultPoint.BeforeMigrationCommit, (int?)2),
            "deletion_before_commit" => (JobStoreFaultPoint.BeforeWriteCommit, (int?)null),
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
        if (mode.StartsWith("job_", StringComparison.Ordinal))
        {
            await store.CreateJobAsync(PersistenceTestData.CreateJob("job-crash"));
            return;
        }

        if (mode.StartsWith("gate_", StringComparison.Ordinal))
        {
            var gateJob = PersistenceTestData.CreateJob("gate-crash");
            var evidence = PersistenceTestData.CreateSanitization("gate-crash");
            await store.RecordSanitizationGateDecisionAsync(SanitizationGate.Evaluate(
                gateJob.JobId,
                evidence,
                PersistenceTestData.Timestamp.AddMinutes(2)));
            return;
        }

        if (string.Equals(mode, "deletion_before_commit", StringComparison.Ordinal))
        {
            var deletionJob = PersistenceTestData.CreateJob("deletion-crash");
            var archived = Assert.IsType<StoredJob>(await store.LoadJobAsync(deletionJob.JobId));
            await store.ExecuteArchivedJobDeletionAsync(deletionJob.JobId, archived.Checkpoints[^1].CheckpointId, (file, _) =>
            {
                Assert.Equal(DeletionFixtureFile, file);
                return RemoveDeletionFixture(root, file);
            });
            return;
        }

        if (mode.StartsWith("guarded_", StringComparison.Ordinal))
        {
            var guardedJob = PersistenceTestData.CreateJob("guarded-crash");
            var stored = await store.LoadJobAsync(guardedJob.JobId);
            Assert.NotNull(stored);
            var access = SanitizationGate.Inspect(stored);
            Assert.True(access.AllowsAssessment);
            await store.AppendEvidenceAsync(new JobEvidenceBatch(
                guardedJob.JobId,
                [
                    PersistenceTestData.CreateObservation("guarded-next-first"),
                    PersistenceTestData.CreateObservation("guarded-next-second"),
                ],
                expectedSanitizationGateDecisionId: access.Decision!.DecisionId));
            return;
        }

        await store.AppendEvidenceAsync(new JobEvidenceBatch(
            job.JobId,
            [
                PersistenceTestData.CreateObservation("crash-first"),
                PersistenceTestData.CreateObservation("crash-second"),
            ]));
    }

    private static JobLocalFileRemoval RemoveDeletionFixture(string root, JobLocalFile file)
    {
        var fixture = new DirectoryInfo(root).Parent ?? throw new InvalidOperationException("The fixture parent was not supplied.");
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        Assert.Equal(temp, fixture.Parent?.FullName, ignoreCase: true);
        Assert.StartsWith("ThirdLife-TL0102-", fixture.Name, StringComparison.Ordinal);
        var path = Path.Combine(fixture.FullName, file.FileName);
        using var handle = WindowsFileIdentity.OpenFileMutationGuard(path, allowReadWriteShare: false);
        Assert.Equal(path, WindowsFileIdentity.GetFinalPath(handle), ignoreCase: true);
        Assert.Equal(1U, WindowsFileIdentity.GetLinkCount(handle));
        Assert.False(WindowsFileIdentity.GetAttributes(handle).HasFlag(FileAttributes.ReparsePoint));
        using var data = new FileStream(handle, FileAccess.Read);
        Assert.Equal(file.ByteLength, data.Length);
        Span<byte> actual = stackalloc byte[file.ByteLength];
        data.ReadExactly(actual);
        Assert.True(actual.SequenceEqual(DeletionFixtureBytes));
        WindowsFileIdentity.DeleteFile(handle);
        return JobLocalFileRemoval.Removed;
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
