using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Persistence;

[SupportedOSPlatform("windows")]
public sealed class SqliteJobStore : IJobStore
{
    public const int MaximumCheckpointsPerJob = 10_000;
    public const int MaximumGateDecisionsPerJob = 10_000;
    public const long MaximumDatabaseBytes = 256L * 1024 * 1024;
    public const int MaximumJobs = 10_000;
    public const int MaximumPayloadBytes = 65_536;
    public const int MaximumRecordsPerJob = 10_000;

    private const int BusyTimeoutMilliseconds = 5_000;
    private static readonly object ProviderLock = new();
    private static bool _providerInitialized;

    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly RestrictedStoreLayout _layout;
    private readonly GuardedStoreFile _databaseGuard;
    private readonly GuardedStoreFile _journalGuard;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IJobStoreFaultInjector? _faultInjector;
    private bool _disposed;

    private SqliteJobStore(
        RestrictedStoreLayout layout,
        GuardedStoreFile databaseGuard,
        GuardedStoreFile journalGuard,
        SqliteConnection connection,
        string databaseEngineVersion,
        TimeProvider timeProvider,
        IJobStoreFaultInjector? faultInjector)
    {
        _layout = layout;
        _databaseGuard = databaseGuard;
        _journalGuard = journalGuard;
        _connection = connection;
        DatabaseEngineVersion = databaseEngineVersion;
        _timeProvider = timeProvider;
        _faultInjector = faultInjector;
        _jsonOptions = DomainJson.CreateStrictOptions();
    }

    public static int CurrentSchemaVersion => SqliteMigrationCatalog.CurrentVersion;

    public string DatabaseEngineVersion { get; }

    public static Task<SqliteJobStore> OpenAsync(CancellationToken cancellationToken = default) =>
        OpenInternalAsync(
            ResolveRegisteredRoot(),
            SqliteMigrationCatalog.CurrentVersion,
            TimeProvider.System,
            faultInjector: null,
            cancellationToken);

    internal static Task<SqliteJobStore> OpenAsync(
        string rootPath,
        CancellationToken cancellationToken = default) =>
        OpenInternalAsync(
            rootPath,
            SqliteMigrationCatalog.CurrentVersion,
            TimeProvider.System,
            faultInjector: null,
            cancellationToken);

    public async Task CreateJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ThrowIfDisposed();

        var payload = SerializePayload(job);
        var checkpoint = CreateCheckpoint(job.JobId, JobCheckpointKind.JobCreated);

        await ExecuteWriteAsync(
            async (connection, transaction, token) =>
            {
                var currentJobCount = await ReadJobCountAsync(connection, transaction, token)
                    .ConfigureAwait(false);
                if (currentJobCount >= MaximumJobs)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(job),
                        "The bounded job-store limit would be exceeded.");
                }

                _layout.EnsureJobDirectory(job.JobId);
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO jobs (
                            job_id,
                            device_id,
                            created_at_utc,
                            job_payload_json,
                            job_payload_sha256)
                        VALUES ($job_id, $device_id, $created_at_utc, $payload_json, $payload_sha256);
                        """;
                    command.Parameters.AddWithValue("$job_id", job.JobId.Value);
                    command.Parameters.AddWithValue("$device_id", job.DeviceId.Value);
                    command.Parameters.AddWithValue("$created_at_utc", FormatTimestamp(job.CreatedAtUtc));
                    command.Parameters.AddWithValue("$payload_json", payload.Json);
                    command.Parameters.AddWithValue("$payload_sha256", payload.Sha256);
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await InsertCheckpointAsync(connection, transaction, checkpoint, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendEvidenceAsync(
        JobEvidenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ThrowIfDisposed();
        _layout.ValidateJobDirectory(batch.JobId);

        await ExecuteWriteAsync(
            async (connection, transaction, token) =>
            {
                var deviceId = await ReadDeviceIdAsync(connection, transaction, batch.JobId, token).ConfigureAwait(false);
                var currentCount = await ReadEvidenceCountAsync(connection, transaction, batch.JobId, token)
                    .ConfigureAwait(false);
                var proposedCount = batch.Observations.Count + batch.SanitizationEvidence.Count + batch.HumanTests.Count;
                if (currentCount > MaximumRecordsPerJob - proposedCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(batch),
                        "The bounded per-job evidence-record limit would be exceeded.");
                }

                var inserted = 0;
                foreach (var observation in batch.Observations)
                {
                    await InsertEvidenceAsync(
                        connection,
                        transaction,
                        batch.JobId,
                        "observation",
                        observation.Metadata.EvidenceId.Value,
                        observation.Metadata.EvidenceId.Value,
                        observation.Metadata.CollectedAtUtc,
                        observation,
                        token).ConfigureAwait(false);
                    inserted++;
                    await SignalFirstEvidenceInsertAsync(inserted, token).ConfigureAwait(false);
                }

                foreach (var evidence in batch.SanitizationEvidence)
                {
                    await InsertEvidenceAsync(
                        connection,
                        transaction,
                        batch.JobId,
                        "sanitization",
                        evidence.Metadata.EvidenceId.Value,
                        evidence.Metadata.EvidenceId.Value,
                        evidence.Metadata.CollectedAtUtc,
                        evidence,
                        token).ConfigureAwait(false);
                    inserted++;
                    await SignalFirstEvidenceInsertAsync(inserted, token).ConfigureAwait(false);
                }

                foreach (var humanTest in batch.HumanTests)
                {
                    if (humanTest.JobId != batch.JobId || humanTest.DeviceId != deviceId)
                    {
                        throw new ArgumentException(
                            "Human-test evidence must be bound to the persisted job and device.",
                            nameof(batch));
                    }

                    await InsertEvidenceAsync(
                        connection,
                        transaction,
                        batch.JobId,
                        "human_test",
                        humanTest.Metadata.EvidenceId.Value,
                        humanTest.HumanTestId.Value,
                        humanTest.Metadata.CollectedAtUtc,
                        humanTest,
                        token).ConfigureAwait(false);
                    inserted++;
                    await SignalFirstEvidenceInsertAsync(inserted, token).ConfigureAwait(false);
                }

                await InsertCheckpointAsync(
                    connection,
                    transaction,
                    CreateCheckpoint(batch.JobId, JobCheckpointKind.EvidenceCommitted),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task SetArchiveStateAsync(
        JobId jobId,
        bool isArchived,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ThrowIfDisposed();
        _layout.ValidateJobDirectory(jobId);
        var timestamp = RequireTimestamp(changedAtUtc, nameof(changedAtUtc));

        return ExecuteWriteAsync(
            async (connection, transaction, token) =>
            {
                int changed;
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE jobs
                        SET is_archived = $is_archived,
                            archived_at_utc = $archived_at_utc
                        WHERE job_id = $job_id
                          AND is_archived <> $is_archived;
                        """;
                    command.Parameters.AddWithValue("$job_id", jobId.Value);
                    command.Parameters.AddWithValue("$is_archived", isArchived ? 1 : 0);
                    command.Parameters.AddWithValue(
                        "$archived_at_utc",
                        isArchived ? FormatTimestamp(timestamp) : DBNull.Value);
                    changed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                if (changed == 0)
                {
                    await EnsureJobExistsAsync(connection, transaction, jobId, token).ConfigureAwait(false);
                    return;
                }

                await InsertCheckpointAsync(
                    connection,
                    transaction,
                    new JobCheckpoint(
                        CheckpointId.New(),
                        jobId,
                        isArchived ? JobCheckpointKind.Archived : JobCheckpointKind.Restored,
                        timestamp),
                    token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public async Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(
        SanitizationGateDecision candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ThrowIfDisposed();
        _layout.ValidateJobDirectory(candidate.JobId);

        SanitizationGateDecision? accepted = null;
        await ExecuteWriteAsync(
            async (connection, transaction, token) =>
            {
                if (await ReadArchiveStateAsync(
                        connection,
                        transaction,
                        candidate.JobId,
                        token).ConfigureAwait(false))
                {
                    throw new JobStoreConflictException();
                }

                var evidence = await ReadLatestSanitizationEvidenceAsync(
                    connection,
                    transaction,
                    candidate.JobId,
                    token).ConfigureAwait(false);
                if (evidence is null || evidence.Metadata.EvidenceId != candidate.EvidenceId)
                {
                    throw new JobStoreConflictException();
                }

                if (!SanitizationGate.IsConsistent(candidate, evidence))
                {
                    throw new ArgumentException(
                        "The proposed gate decision does not match its sanitization evidence.",
                        nameof(candidate));
                }

                var existing = await ReadGateDecisionForEvidenceAsync(
                    connection,
                    transaction,
                    candidate.JobId,
                    candidate.EvidenceId,
                    token).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (!SanitizationGate.IsConsistent(existing, evidence))
                    {
                        throw new JobStoreCorruptionException("store_payload_mismatch");
                    }

                    accepted = existing;
                    return;
                }

                var currentCount = await ReadGateDecisionCountAsync(
                    connection,
                    transaction,
                    candidate.JobId,
                    token).ConfigureAwait(false);
                if (currentCount >= MaximumGateDecisionsPerJob)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(candidate),
                        "The bounded per-job sanitization-decision limit would be exceeded.");
                }

                await InsertGateDecisionAsync(
                    connection,
                    transaction,
                    candidate,
                    token).ConfigureAwait(false);
                accepted = candidate;
            },
            cancellationToken).ConfigureAwait(false);

        return accepted ?? throw new JobStoreUnavailableException();
    }

    public async Task<StoredJob?> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ThrowIfDisposed();

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TranslateReadErrorsAsync(
                async () =>
                {
                    _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                    ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                    // The first SELECT anchors one deferred read snapshot across the bounded
                    // projection queries without unnecessarily reserving the database for writing.
                    await using var transaction = _connection.BeginTransaction(deferred: true);
                    var storedJob = await ReadStoredJobAsync(
                        _connection,
                        transaction,
                        jobId,
                        cancellationToken).ConfigureAwait(false);
                    if (storedJob is not null)
                    {
                        _layout.ValidateJobDirectory(jobId);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                    ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                    return storedJob;
                }).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _connection.DisposeAsync().ConfigureAwait(false);
        _journalGuard.Dispose();
        _databaseGuard.Dispose();
        _layout.Dispose();
        _connectionGate.Dispose();
    }

    internal string GetJobDirectoryPath(JobId jobId) => _layout.GetJobDirectoryPath(jobId);

    internal string DatabasePath => _layout.DatabasePath;

    internal static Task<SqliteJobStore> OpenForTestingAsync(
        string rootPath,
        int maximumMigrationVersion,
        TimeProvider timeProvider,
        IJobStoreFaultInjector? faultInjector,
        CancellationToken cancellationToken = default) =>
        OpenInternalAsync(rootPath, maximumMigrationVersion, timeProvider, faultInjector, cancellationToken);

    private static async Task<SqliteJobStore> OpenInternalAsync(
        string rootPath,
        int maximumMigrationVersion,
        TimeProvider timeProvider,
        IJobStoreFaultInjector? faultInjector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The protected SQLite job store requires Windows.");
        }

        EnsureSqliteProvider();
        RestrictedStoreLayout? layout = null;
        GuardedStoreFile? databaseGuard = null;
        GuardedStoreFile? journalGuard = null;
        SqliteConnection? connection = null;

        try
        {
            layout = RestrictedStoreLayout.CreateOrOpen(rootPath);
            var registeredState = layout.InspectRegisteredStore();
            if (!registeredState.DatabaseExists)
            {
                if (registeredState.JournalExists)
                {
                    using var orphanJournal = layout.OpenExistingJournalGuard(allowDeleteShare: true);
                    ValidateStoreFileBounds(orphanJournal);
                    throw new JobStoreCorruptionException("store_identity_mismatch");
                }

                await EnsureInitialStorePublishedAsync(
                    layout,
                    maximumMigrationVersion,
                    timeProvider,
                    faultInjector,
                    cancellationToken).ConfigureAwait(false);
                registeredState = layout.InspectRegisteredStore();
            }

            databaseGuard = layout.OpenExistingDatabaseGuard();
            if (registeredState.JournalExists)
            {
                journalGuard = layout.OpenExistingJournalGuard(allowDeleteShare: true);
                layout.ValidateStoreGuards(databaseGuard, journalGuard);
                ValidateStoreFileBounds(databaseGuard, journalGuard);
                journalGuard.Dispose();
                journalGuard = null;
            }
            else
            {
                layout.ValidateDatabaseGuard(databaseGuard);
                ValidateStoreFileBounds(databaseGuard);
            }

            var connectionString = CreateConnectionString(layout.DatabasePath);
            connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, configureJournal: false, cancellationToken).ConfigureAwait(false);
            layout.ValidateDatabaseGuard(databaseGuard);
            await AssertDatabaseIdentityPreflightAsync(
                connection,
                databaseCreatedNew: false,
                maximumMigrationVersion,
                cancellationToken).ConfigureAwait(false);
            await ConfigurePageLimitAsync(connection, cancellationToken).ConfigureAwait(false);
            journalGuard = layout.OpenJournalGuard();
            await ConfigureJournalAsync(connection, cancellationToken).ConfigureAwait(false);
            layout.ValidateStoreGuards(databaseGuard, journalGuard);
            ValidateStoreFileBounds(databaseGuard, journalGuard);
            await AssertDatabaseIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);

            var runner = new SqliteMigrationRunner(timeProvider, faultInjector);
            await runner.ApplyAsync(
                connection,
                maximumMigrationVersion,
                databaseCreatedNew: false,
                cancellationToken).ConfigureAwait(false);
            await AssertDatabaseIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
            await ReconcileJobDirectoriesAsync(connection, layout, cancellationToken).ConfigureAwait(false);

            var engineVersion = await ReadDatabaseEngineVersionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            var store = new SqliteJobStore(
                layout,
                databaseGuard,
                journalGuard,
                connection,
                engineVersion,
                timeProvider,
                faultInjector);
            databaseGuard = null;
            journalGuard = null;
            connection = null;
            layout = null;
            return store;
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw TranslateSqliteException(exception);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new JobStorePathException();
        }
        catch (Exception exception) when (IsPersistedValueFailure(exception))
        {
            throw new JobStoreCorruptionException();
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            journalGuard?.Dispose();
            databaseGuard?.Dispose();
            layout?.Dispose();
        }
    }

    private static async Task EnsureInitialStorePublishedAsync(
        RestrictedStoreLayout layout,
        int maximumMigrationVersion,
        TimeProvider timeProvider,
        IJobStoreFaultInjector? faultInjector,
        CancellationToken cancellationToken)
    {
        layout.ReconcileInitializationArtifacts(MaximumDatabaseBytes);
        var registeredState = layout.InspectRegisteredStore();
        if (registeredState.DatabaseExists)
        {
            return;
        }
        if (registeredState.JournalExists)
        {
            using var orphanJournal = layout.OpenExistingJournalGuard(allowDeleteShare: true);
            ValidateStoreFileBounds(orphanJournal);
            throw new JobStoreCorruptionException("store_identity_mismatch");
        }

        var files = layout.CreateInitializationStoreFiles();
        GuardedStoreFile? databaseGuard = files.DatabaseGuard;
        GuardedStoreFile? journalGuard = files.JournalGuard;
        SqliteConnection? connection = null;
        var published = false;
        Exception? initializationFailure = null;

        try
        {
            layout.ValidateInitializationStoreGuards(
                files.DatabasePath,
                files.JournalPath,
                databaseGuard,
                journalGuard);
            ValidateStoreFileBounds(databaseGuard, journalGuard);

            journalGuard.Dispose();
            journalGuard = null;

            connection = new SqliteConnection(CreateConnectionString(files.DatabasePath));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, configureJournal: false, cancellationToken).ConfigureAwait(false);
            await AssertDatabaseIdentityPreflightAsync(
                connection,
                databaseCreatedNew: true,
                maximumMigrationVersion,
                cancellationToken).ConfigureAwait(false);
            await ConfigurePageLimitAsync(connection, cancellationToken).ConfigureAwait(false);
            journalGuard = layout.OpenInitializationJournalGuard(files.DatabasePath, files.JournalPath);
            await ConfigureJournalAsync(connection, cancellationToken).ConfigureAwait(false);
            layout.ValidateInitializationStoreGuards(
                files.DatabasePath,
                files.JournalPath,
                databaseGuard,
                journalGuard);
            ValidateStoreFileBounds(databaseGuard, journalGuard);
            await AssertDatabaseIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);

            if (faultInjector is not null)
            {
                await faultInjector.OnFaultPointAsync(
                    JobStoreFaultPoint.BeforeInitialStoreMigration,
                    detail: 0,
                    cancellationToken).ConfigureAwait(false);
            }

            var runner = new SqliteMigrationRunner(timeProvider, faultInjector);
            await runner.ApplyAsync(
                connection,
                maximumMigrationVersion,
                databaseCreatedNew: true,
                cancellationToken).ConfigureAwait(false);
            await AssertDatabaseIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
            _ = await ReadDatabaseEngineVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            layout.ValidateInitializationStoreGuards(
                files.DatabasePath,
                files.JournalPath,
                databaseGuard,
                journalGuard);
            ValidateStoreFileBounds(databaseGuard, journalGuard);

            await connection.DisposeAsync().ConfigureAwait(false);
            connection = null;
            layout.ValidateInitializationStoreGuards(
                files.DatabasePath,
                files.JournalPath,
                databaseGuard,
                journalGuard);
            ValidateStoreFileBounds(databaseGuard, journalGuard);

            journalGuard.Dispose();
            journalGuard = null;
            if (!layout.TryDeleteInitializationArtifact(files.JournalPath, MaximumDatabaseBytes))
            {
                throw new JobStoreBusyException();
            }

            if (faultInjector is not null)
            {
                await faultInjector.OnFaultPointAsync(
                    JobStoreFaultPoint.BeforeInitialStorePublish,
                    detail: 0,
                    cancellationToken).ConfigureAwait(false);
            }

            published = layout.TryPublishInitializationDatabase(files.DatabasePath, databaseGuard);
            databaseGuard.Dispose();
            databaseGuard = null;
            if (published && faultInjector is not null)
            {
                await faultInjector.OnFaultPointAsync(
                    JobStoreFaultPoint.AfterInitialStorePublish,
                    detail: 0,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            initializationFailure = exception;
            throw;
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            journalGuard?.Dispose();
            databaseGuard?.Dispose();

            if (!published)
            {
                try
                {
                    _ = layout.TryDeleteInitializationArtifact(files.JournalPath, MaximumDatabaseBytes);
                    _ = layout.TryDeleteInitializationArtifact(files.DatabasePath, MaximumDatabaseBytes);
                }
                catch (JobStoreException) when (initializationFailure is not null)
                {
                }
            }
        }
    }

    private static async Task<string> ReadDatabaseEngineVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var engineVersion = Convert.ToString(
            await ExecuteScalarAsync(connection, "SELECT sqlite_version();", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(engineVersion) || engineVersion.Length > 32)
        {
            throw new JobStoreCorruptionException("store_engine_unavailable");
        }

        return engineVersion;
    }

    private async Task ExecuteWriteAsync(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await TranslateSqliteErrorsAsync(
                async () =>
                {
                    _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                    ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                    await using var transaction = _connection.BeginTransaction(deferred: false);
                    await operation(_connection, transaction, cancellationToken).ConfigureAwait(false);

                    if (_faultInjector is not null)
                    {
                        await _faultInjector.OnFaultPointAsync(
                            JobStoreFaultPoint.BeforeWriteCommit,
                            detail: 0,
                            cancellationToken).ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                    ValidateStoreFileBounds(_databaseGuard, _journalGuard);

                    if (_faultInjector is not null)
                    {
                        await _faultInjector.OnFaultPointAsync(
                            JobStoreFaultPoint.AfterWriteCommit,
                            detail: 0,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return true;
                }).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static string CreateConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
            ForeignKeys = true,
            RecursiveTriggers = false,
        }.ToString();

    private static string ResolveRegisteredRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData) || !Path.IsPathFullyQualified(localApplicationData))
        {
            throw new JobStoreUnavailableException();
        }

        return Path.Combine(localApplicationData, "ThirdLife", "SetupCore", "JobStore");
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        bool configureJournal,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            connection,
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};"),
            cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA synchronous = FULL;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA trusted_schema = OFF;", cancellationToken).ConfigureAwait(false);
        if (configureJournal)
        {
            await ConfigureJournalAsync(connection, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ConfigureJournalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var journalMode = Convert.ToString(
            await ExecuteScalarAsync(connection, "PRAGMA journal_mode = PERSIST;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (!string.Equals(journalMode, "persist", StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStoreUnavailableException();
        }
    }

    private static async Task AssertDatabaseIdentityPreflightAsync(
        SqliteConnection connection,
        bool databaseCreatedNew,
        int maximumMigrationVersion,
        CancellationToken cancellationToken)
    {
        var applicationId = Convert.ToInt32(
            await ExecuteScalarAsync(connection, "PRAGMA application_id;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        var userVersion = Convert.ToInt32(
            await ExecuteScalarAsync(connection, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        var hasSchema = Convert.ToInt32(
            await ExecuteScalarAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%');",
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;

        if (databaseCreatedNew)
        {
            if (applicationId != 0 || userVersion != 0 || hasSchema)
            {
                throw new JobStoreCorruptionException("store_identity_mismatch");
            }

            return;
        }

        if (applicationId != SqliteMigrationRunner.ApplicationId)
        {
            throw new JobStoreCorruptionException("store_identity_mismatch");
        }
        if (userVersion > SqliteMigrationCatalog.CurrentVersion || userVersion > maximumMigrationVersion)
        {
            throw new JobStoreVersionException();
        }
    }

    private static async Task AssertDatabaseIntegrityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var quickCheck = Convert.ToString(
            await ExecuteScalarAsync(connection, "PRAGMA quick_check(1);", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (!string.Equals(quickCheck, "ok", StringComparison.Ordinal))
        {
            throw new JobStoreCorruptionException();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new JobStoreCorruptionException("store_foreign_key_mismatch");
        }
    }

    private static async Task ReconcileJobDirectoriesAsync(
        SqliteConnection connection,
        RestrictedStoreLayout layout,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        var jobIds = new List<JobId>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT job_id FROM jobs ORDER BY job_id LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", MaximumJobs + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (jobIds.Count >= MaximumJobs)
                {
                    throw new JobStoreCorruptionException("store_record_limit_exceeded");
                }

                try
                {
                    jobIds.Add(new JobId(reader.GetString(0)));
                }
                catch (Exception exception) when (IsPersistedValueFailure(exception))
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }
            }
        }

        layout.ReconcileJobDirectories(jobIds.AsReadOnly(), MaximumJobs);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Payload SerializePayload<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
        if (bytes.Length is < 2 or > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The normalized record exceeds the persistence byte limit.");
        }

        return new Payload(
            Encoding.UTF8.GetString(bytes),
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private T DeserializePayload<T>(string json, string expectedSha256)
        where T : class
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length is < 2 or > MaximumPayloadBytes ||
            expectedSha256.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(bytes))),
                Encoding.ASCII.GetBytes(expectedSha256)))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bytes, _jsonOptions)
                ?? throw new JobStoreCorruptionException("store_payload_mismatch");
        }
        catch (JsonException)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
        catch (ArgumentException)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private async Task InsertEvidenceAsync<T>(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        string kind,
        string evidenceId,
        string domainRecordId,
        DateTimeOffset collectedAtUtc,
        T value,
        CancellationToken cancellationToken)
    {
        var payload = SerializePayload(value);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO evidence_records (
                evidence_id,
                job_id,
                evidence_kind,
                domain_record_id,
                collected_at_utc,
                payload_json,
                payload_sha256)
            VALUES (
                $evidence_id,
                $job_id,
                $evidence_kind,
                $domain_record_id,
                $collected_at_utc,
                $payload_json,
                $payload_sha256);
            """;
        command.Parameters.AddWithValue("$evidence_id", evidenceId);
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        command.Parameters.AddWithValue("$evidence_kind", kind);
        command.Parameters.AddWithValue("$domain_record_id", domainRecordId);
        command.Parameters.AddWithValue("$collected_at_utc", FormatTimestamp(collectedAtUtc));
        command.Parameters.AddWithValue("$payload_json", payload.Json);
        command.Parameters.AddWithValue("$payload_sha256", payload.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertGateDecisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SanitizationGateDecision decision,
        CancellationToken cancellationToken)
    {
        var payload = SerializePayload(decision);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sanitization_gate_decisions (
                decision_id,
                job_id,
                evidence_id,
                policy_version,
                outcome,
                reason_code,
                evaluated_at_utc,
                payload_json,
                payload_sha256)
            VALUES (
                $decision_id,
                $job_id,
                $evidence_id,
                $policy_version,
                $outcome,
                $reason_code,
                $evaluated_at_utc,
                $payload_json,
                $payload_sha256);
            """;
        command.Parameters.AddWithValue("$decision_id", decision.DecisionId.Value);
        command.Parameters.AddWithValue("$job_id", decision.JobId.Value);
        command.Parameters.AddWithValue("$evidence_id", decision.EvidenceId.Value);
        command.Parameters.AddWithValue("$policy_version", decision.PolicyVersion);
        command.Parameters.AddWithValue("$outcome", ToWireName(decision.Outcome));
        command.Parameters.AddWithValue("$reason_code", ToWireName(decision.Reason));
        command.Parameters.AddWithValue("$evaluated_at_utc", FormatTimestamp(decision.EvaluatedAtUtc));
        command.Parameters.AddWithValue("$payload_json", payload.Json);
        command.Parameters.AddWithValue("$payload_sha256", payload.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCheckpointAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var currentCount = await ReadCheckpointCountAsync(
            connection,
            transaction,
            checkpoint.JobId,
            cancellationToken).ConfigureAwait(false);
        if (currentCount >= MaximumCheckpointsPerJob)
        {
            throw new ArgumentOutOfRangeException(
                nameof(checkpoint),
                "The bounded per-job checkpoint limit would be exceeded.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO store_checkpoints (
                checkpoint_id,
                job_id,
                checkpoint_kind,
                recorded_at_utc)
            VALUES ($checkpoint_id, $job_id, $checkpoint_kind, $recorded_at_utc);
            """;
        command.Parameters.AddWithValue("$checkpoint_id", checkpoint.CheckpointId.Value);
        command.Parameters.AddWithValue("$job_id", checkpoint.JobId.Value);
        command.Parameters.AddWithValue("$checkpoint_kind", ToWireName(checkpoint.Kind));
        command.Parameters.AddWithValue("$recorded_at_utc", FormatTimestamp(checkpoint.RecordedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoredJob?> ReadStoredJobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        Job job;
        bool isArchived;
        DateTimeOffset? archivedAtUtc;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    device_id,
                    created_at_utc,
                    job_payload_json,
                    job_payload_sha256,
                    is_archived,
                    archived_at_utc
                FROM jobs
                WHERE job_id = $job_id;
                """;
            command.Parameters.AddWithValue("$job_id", jobId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var indexedDeviceId = reader.GetString(0);
            var indexedCreatedAt = ParseTimestamp(reader.GetString(1));
            job = DeserializePayload<Job>(reader.GetString(2), reader.GetString(3));
            var archiveValue = reader.GetInt32(4);
            if (archiveValue is not 0 and not 1)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            isArchived = archiveValue == 1;
            archivedAtUtc = reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5));

            if (job.JobId != jobId ||
                !string.Equals(job.DeviceId.Value, indexedDeviceId, StringComparison.Ordinal) ||
                job.CreatedAtUtc != indexedCreatedAt ||
                isArchived != (archivedAtUtc is not null))
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }
        }

        if (_faultInjector is not null)
        {
            await _faultInjector.OnFaultPointAsync(
                JobStoreFaultPoint.DuringSnapshotRead,
                detail: 1,
                cancellationToken).ConfigureAwait(false);
        }

        var observations = new List<Observation>();
        var sanitization = new List<SanitizationEvidence>();
        var humanTests = new List<HumanTestEvidence>();
        await ReadEvidenceAsync(
            connection,
            transaction,
            job,
            observations,
            sanitization,
            humanTests,
            cancellationToken).ConfigureAwait(false);
        var gateDecisions = await ReadGateDecisionsAsync(
            connection,
            transaction,
            job,
            sanitization,
            cancellationToken).ConfigureAwait(false);
        var checkpoints = await ReadCheckpointsAsync(connection, transaction, jobId, cancellationToken)
            .ConfigureAwait(false);
        ValidateArchiveHistory(isArchived, archivedAtUtc, checkpoints);

        return new StoredJob(
            job,
            isArchived,
            archivedAtUtc,
            observations,
            sanitization,
            humanTests,
            gateDecisions,
            checkpoints);
    }

    private async Task ReadEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Job job,
        List<Observation> observations,
        List<SanitizationEvidence> sanitization,
        List<HumanTestEvidence> humanTests,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT evidence_id, evidence_kind, domain_record_id, collected_at_utc, payload_json, payload_sha256
            FROM evidence_records
            WHERE job_id = $job_id
            ORDER BY evidence_sequence
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job_id", job.JobId.Value);
        command.Parameters.AddWithValue("$limit", MaximumRecordsPerJob + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            count++;
            if (count > MaximumRecordsPerJob)
            {
                throw new JobStoreCorruptionException("store_record_limit_exceeded");
            }

            var evidenceId = reader.GetString(0);
            var kind = reader.GetString(1);
            var domainRecordId = reader.GetString(2);
            var collectedAtUtc = ParseTimestamp(reader.GetString(3));
            var json = reader.GetString(4);
            var sha256 = reader.GetString(5);

            switch (kind)
            {
                case "observation":
                    {
                        var value = DeserializePayload<Observation>(json, sha256);
                        ValidateEvidenceMetadata(value.Metadata, evidenceId, collectedAtUtc);
                        if (!string.Equals(domainRecordId, evidenceId, StringComparison.Ordinal))
                        {
                            throw new JobStoreCorruptionException("store_payload_mismatch");
                        }

                        observations.Add(value);
                        break;
                    }
                case "sanitization":
                    {
                        var value = DeserializePayload<SanitizationEvidence>(json, sha256);
                        ValidateEvidenceMetadata(value.Metadata, evidenceId, collectedAtUtc);
                        if (!string.Equals(domainRecordId, evidenceId, StringComparison.Ordinal))
                        {
                            throw new JobStoreCorruptionException("store_payload_mismatch");
                        }

                        sanitization.Add(value);
                        break;
                    }
                case "human_test":
                    {
                        var value = DeserializePayload<HumanTestEvidence>(json, sha256);
                        ValidateEvidenceMetadata(value.Metadata, evidenceId, collectedAtUtc);
                        if (value.JobId != job.JobId ||
                            value.DeviceId != job.DeviceId ||
                            !string.Equals(value.HumanTestId.Value, domainRecordId, StringComparison.Ordinal))
                        {
                            throw new JobStoreCorruptionException("store_payload_mismatch");
                        }

                        humanTests.Add(value);
                        break;
                    }
                default:
                    throw new JobStoreCorruptionException("store_payload_mismatch");
            }
        }
    }

    private async Task<IReadOnlyList<SanitizationGateDecision>> ReadGateDecisionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Job job,
        List<SanitizationEvidence> sanitizationEvidence,
        CancellationToken cancellationToken)
    {
        var decisions = new List<SanitizationGateDecision>();
        var evidencePositions = sanitizationEvidence
            .Select((evidence, index) => (evidence.Metadata.EvidenceId, Index: index))
            .ToDictionary(item => item.EvidenceId, item => item.Index);
        var previousEvidencePosition = -1;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                decision_id,
                evidence_id,
                policy_version,
                outcome,
                reason_code,
                evaluated_at_utc,
                payload_json,
                payload_sha256
            FROM sanitization_gate_decisions
            WHERE job_id = $job_id
            ORDER BY decision_sequence
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job_id", job.JobId.Value);
        command.Parameters.AddWithValue("$limit", MaximumGateDecisionsPerJob + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (decisions.Count >= MaximumGateDecisionsPerJob)
            {
                throw new JobStoreCorruptionException("store_record_limit_exceeded");
            }

            var decision = ReadIndexedGateDecision(reader, job.JobId);
            if (!evidencePositions.TryGetValue(decision.EvidenceId, out var evidencePosition) ||
                evidencePosition <= previousEvidencePosition ||
                !SanitizationGate.IsConsistent(decision, sanitizationEvidence[evidencePosition]))
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            previousEvidencePosition = evidencePosition;
            decisions.Add(decision);
        }

        return decisions.AsReadOnly();
    }

    private async Task<SanitizationGateDecision?> ReadGateDecisionForEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        EvidenceId evidenceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                decision_id,
                evidence_id,
                policy_version,
                outcome,
                reason_code,
                evaluated_at_utc,
                payload_json,
                payload_sha256
            FROM sanitization_gate_decisions
            WHERE job_id = $job_id AND evidence_id = $evidence_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        command.Parameters.AddWithValue("$evidence_id", evidenceId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var decision = ReadIndexedGateDecision(reader, jobId);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return decision;
    }

    private SanitizationGateDecision ReadIndexedGateDecision(
        SqliteDataReader reader,
        JobId expectedJobId)
    {
        var indexedDecisionId = reader.GetString(0);
        var indexedEvidenceId = reader.GetString(1);
        var indexedPolicyVersion = reader.GetString(2);
        var indexedOutcome = ParseGateOutcome(reader.GetString(3));
        var indexedReason = ParseGateReason(reader.GetString(4));
        var indexedEvaluatedAtUtc = ParseTimestamp(reader.GetString(5));
        var decision = DeserializePayload<SanitizationGateDecision>(
            reader.GetString(6),
            reader.GetString(7));

        if (decision.JobId != expectedJobId ||
            !string.Equals(decision.DecisionId.Value, indexedDecisionId, StringComparison.Ordinal) ||
            !string.Equals(decision.EvidenceId.Value, indexedEvidenceId, StringComparison.Ordinal) ||
            !string.Equals(decision.PolicyVersion, indexedPolicyVersion, StringComparison.Ordinal) ||
            decision.Outcome != indexedOutcome ||
            decision.Reason != indexedReason ||
            decision.EvaluatedAtUtc != indexedEvaluatedAtUtc)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return decision;
    }

    private async Task<SanitizationEvidence?> ReadLatestSanitizationEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT evidence_id, domain_record_id, collected_at_utc, payload_json, payload_sha256
            FROM evidence_records
            WHERE job_id = $job_id AND evidence_kind = 'sanitization'
            ORDER BY evidence_sequence DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var evidenceId = reader.GetString(0);
        var domainRecordId = reader.GetString(1);
        var collectedAtUtc = ParseTimestamp(reader.GetString(2));
        var evidence = DeserializePayload<SanitizationEvidence>(reader.GetString(3), reader.GetString(4));
        ValidateEvidenceMetadata(evidence.Metadata, evidenceId, collectedAtUtc);
        if (!string.Equals(domainRecordId, evidenceId, StringComparison.Ordinal))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return evidence;
    }

    private static async Task<IReadOnlyList<JobCheckpoint>> ReadCheckpointsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        var checkpoints = new List<JobCheckpoint>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT checkpoint_id, checkpoint_kind, recorded_at_utc
            FROM store_checkpoints
            WHERE job_id = $job_id
            ORDER BY checkpoint_sequence
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        command.Parameters.AddWithValue("$limit", MaximumCheckpointsPerJob + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (checkpoints.Count >= MaximumCheckpointsPerJob)
            {
                throw new JobStoreCorruptionException("store_record_limit_exceeded");
            }

            checkpoints.Add(new JobCheckpoint(
                new CheckpointId(reader.GetString(0)),
                jobId,
                ParseCheckpointKind(reader.GetString(1)),
                ParseTimestamp(reader.GetString(2))));
        }

        return checkpoints.AsReadOnly();
    }

    private static async Task<DeviceId> ReadDeviceIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT device_id FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string deviceId)
        {
            throw new JobStoreConflictException();
        }

        try
        {
            return new DeviceId(deviceId);
        }
        catch (ArgumentException)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static async Task<int> ReadEvidenceCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM evidence_records WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ReadArchiveStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT is_archived FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not long archiveValue || archiveValue is not 0 and not 1)
        {
            if (value is null)
            {
                throw new JobStoreConflictException();
            }

            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return archiveValue == 1;
    }

    private static async Task<int> ReadGateDecisionCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sanitization_gate_decisions WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ConfigurePageLimitAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var pageSize = Convert.ToInt64(
            await ExecuteScalarAsync(connection, "PRAGMA page_size;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (pageSize is < 512 or > 65_536 || (pageSize & (pageSize - 1)) != 0)
        {
            throw new JobStoreCorruptionException("store_size_limit_exceeded");
        }

        var maximumPageCount = MaximumDatabaseBytes / pageSize;
        var configuredPageCount = Convert.ToInt64(
            await ExecuteScalarAsync(
                connection,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PRAGMA max_page_count = {maximumPageCount};"),
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (configuredPageCount < 1 || configuredPageCount > maximumPageCount)
        {
            throw new JobStoreCorruptionException("store_size_limit_exceeded");
        }
    }

    private static void ValidateStoreFileBounds(params GuardedStoreFile[] guards)
    {
        ArgumentNullException.ThrowIfNull(guards);
        try
        {
            if (guards.Length is < 1 or > 2 ||
                guards.Any(guard => guard is null || guard.Stream.Length > MaximumDatabaseBytes))
            {
                throw new JobStoreCorruptionException("store_size_limit_exceeded");
            }
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    private static async Task<int> ReadJobCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM jobs;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task<int> ReadCheckpointCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM store_checkpoints WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task EnsureJobExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM jobs WHERE job_id = $job_id);";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        var exists = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
        if (!exists)
        {
            throw new JobStoreConflictException();
        }
    }

    private async ValueTask SignalFirstEvidenceInsertAsync(int inserted, CancellationToken cancellationToken)
    {
        if (inserted == 1 && _faultInjector is not null)
        {
            await _faultInjector.OnFaultPointAsync(
                JobStoreFaultPoint.AfterFirstEvidenceInsert,
                inserted,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private JobCheckpoint CreateCheckpoint(JobId jobId, JobCheckpointKind kind) =>
        new(CheckpointId.New(), jobId, kind, _timeProvider.GetUtcNow());

    private static void ValidateEvidenceMetadata(
        EvidenceMetadata metadata,
        string evidenceId,
        DateTimeOffset collectedAtUtc)
    {
        if (!string.Equals(metadata.EvidenceId.Value, evidenceId, StringComparison.Ordinal) ||
            metadata.CollectedAtUtc != collectedAtUtc)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static string ToWireName(JobCheckpointKind kind) => kind switch
    {
        JobCheckpointKind.JobCreated => "job_created",
        JobCheckpointKind.EvidenceCommitted => "evidence_committed",
        JobCheckpointKind.Archived => "archived",
        JobCheckpointKind.Restored => "restored",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static JobCheckpointKind ParseCheckpointKind(string value) => value switch
    {
        "job_created" => JobCheckpointKind.JobCreated,
        "evidence_committed" => JobCheckpointKind.EvidenceCommitted,
        "archived" => JobCheckpointKind.Archived,
        "restored" => JobCheckpointKind.Restored,
        _ => throw new JobStoreCorruptionException("store_payload_mismatch"),
    };

    private static string ToWireName(SanitizationGateOutcome outcome) => outcome switch
    {
        SanitizationGateOutcome.AllowAssessment => "allow_assessment",
        SanitizationGateOutcome.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static SanitizationGateOutcome ParseGateOutcome(string value) => value switch
    {
        "allow_assessment" => SanitizationGateOutcome.AllowAssessment,
        "blocked" => SanitizationGateOutcome.Blocked,
        _ => throw new JobStoreCorruptionException("store_payload_mismatch"),
    };

    private static string ToWireName(SanitizationGateReason reason) => reason switch
    {
        SanitizationGateReason.SanitizationVerified => "sanitization_verified",
        SanitizationGateReason.ReplacementStorageVerified => "replacement_storage_verified",
        SanitizationGateReason.NoDonorStorageVerified => "no_donor_storage_verified",
        SanitizationGateReason.SanitizationUnknown => "sanitization_unknown",
        SanitizationGateReason.SanitizationFailed => "sanitization_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static SanitizationGateReason ParseGateReason(string value) => value switch
    {
        "sanitization_verified" => SanitizationGateReason.SanitizationVerified,
        "replacement_storage_verified" => SanitizationGateReason.ReplacementStorageVerified,
        "no_donor_storage_verified" => SanitizationGateReason.NoDonorStorageVerified,
        "sanitization_unknown" => SanitizationGateReason.SanitizationUnknown,
        "sanitization_failed" => SanitizationGateReason.SanitizationFailed,
        _ => throw new JobStoreCorruptionException("store_payload_mismatch"),
    };

    private static void ValidateArchiveHistory(
        bool isArchived,
        DateTimeOffset? archivedAtUtc,
        IReadOnlyList<JobCheckpoint> checkpoints)
    {
        if (checkpoints.Count == 0)
        {
            if (isArchived || archivedAtUtc is not null)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            // Version-one jobs predate the checkpoint table and remain valid after migration.
            return;
        }

        if (checkpoints[0].Kind != JobCheckpointKind.JobCreated)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        var historyIsArchived = false;
        DateTimeOffset? latestArchivedAtUtc = null;
        for (var index = 0; index < checkpoints.Count; index++)
        {
            var checkpoint = checkpoints[index];
            switch (checkpoint.Kind)
            {
                case JobCheckpointKind.JobCreated when index != 0:
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                case JobCheckpointKind.Archived when historyIsArchived:
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                case JobCheckpointKind.Archived:
                    historyIsArchived = true;
                    latestArchivedAtUtc = checkpoint.RecordedAtUtc;
                    break;
                case JobCheckpointKind.Restored when !historyIsArchived:
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                case JobCheckpointKind.Restored:
                    historyIsArchived = false;
                    latestArchivedAtUtc = null;
                    break;
            }
        }

        if (historyIsArchived != isArchived ||
            (isArchived && latestArchivedAtUtc != archivedAtUtc) ||
            (!isArchived && archivedAtUtc is not null))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        RequireTimestamp(value, nameof(value)).ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset RequireTimestamp(DateTimeOffset value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException("A non-default timestamp is required.", parameterName);
        }

        return value.ToUniversalTime();
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return timestamp.ToUniversalTime();
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> TranslateSqliteErrorsAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (SqliteException exception)
        {
            throw TranslateSqliteException(exception);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new JobStorePathException();
        }
    }

    private static async Task<T> TranslateReadErrorsAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw TranslateSqliteException(exception);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new JobStorePathException();
        }
        catch (Exception exception) when (IsPersistedValueFailure(exception))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static bool IsPersistedValueFailure(Exception exception) =>
        exception is ArgumentException or InvalidCastException or InvalidOperationException or FormatException or OverflowException;

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static JobStoreException TranslateSqliteException(SqliteException exception) =>
        exception.SqliteErrorCode switch
        {
            5 or 6 => new JobStoreBusyException(),
            1 or 11 or 20 or 26 => new JobStoreCorruptionException(),
            19 => new JobStoreConflictException(),
            _ => new JobStoreUnavailableException(),
        };

    private static void EnsureSqliteProvider()
    {
        if (_providerInitialized)
        {
            return;
        }

        lock (ProviderLock)
        {
            if (_providerInitialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            _providerInitialized = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record Payload(string Json, string Sha256);
}
