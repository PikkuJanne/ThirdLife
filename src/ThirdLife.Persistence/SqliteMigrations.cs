using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ThirdLife.Persistence;

internal enum JobStoreFaultPoint
{
    BeforeMigrationCommit = 1,
    AfterMigrationCommit,
    AfterFirstEvidenceInsert,
    BeforeWriteCommit,
    AfterWriteCommit,
    DuringSnapshotRead,
    BeforeInitialStoreMigration,
    BeforeInitialStorePublish,
    AfterInitialStorePublish,
}

internal interface IJobStoreFaultInjector
{
    ValueTask OnFaultPointAsync(
        JobStoreFaultPoint point,
        int detail,
        CancellationToken cancellationToken);
}

internal sealed record SqliteMigration(int Version, string Name, string Script, string ScriptSha256);

internal static class SqliteMigrationCatalog
{
    private const string ResourcePrefix = "ThirdLife.Persistence.Migrations.";

    public static IReadOnlyList<SqliteMigration> All { get; } = Load();

    public static int CurrentVersion => All[^1].Version;

    private static ReadOnlyCollection<SqliteMigration> Load()
    {
        var assembly = typeof(SqliteMigrationCatalog).Assembly;
        var migrations = new List<SqliteMigration>();

        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                                    name.EndsWith(".sql", StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            var fileName = resourceName[ResourcePrefix.Length..];
            var separator = fileName.IndexOf('_', StringComparison.Ordinal);
            if (separator != 3 ||
                !int.TryParse(fileName.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            {
                throw new InvalidOperationException("A compiled SQLite migration has an invalid versioned name.");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("A compiled SQLite migration resource is unavailable.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var script = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            migrations.Add(new SqliteMigration(
                version,
                fileName,
                script,
                Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }

        if (migrations.Count < 2)
        {
            throw new InvalidOperationException("The SQLite job store requires at least two migration fixtures.");
        }

        for (var index = 0; index < migrations.Count; index++)
        {
            if (migrations[index].Version != index + 1)
            {
                throw new InvalidOperationException("Compiled SQLite migration versions must be contiguous and start at one.");
            }
        }

        return migrations.AsReadOnly();
    }
}

internal sealed class SqliteMigrationRunner
{
    internal const int ApplicationId = 0x544C5343;
    private const int MaximumSchemaObjects = 64;

    private static readonly SemaphoreSlim ExpectedSchemaGate = new(1, 1);
    private static IReadOnlyList<string>? _expectedSchemaFingerprints;

    private readonly TimeProvider _timeProvider;
    private readonly IJobStoreFaultInjector? _faultInjector;

    public SqliteMigrationRunner(TimeProvider timeProvider, IJobStoreFaultInjector? faultInjector)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _faultInjector = faultInjector;
    }

    public async Task ApplyAsync(
        SqliteConnection connection,
        int maximumVersion,
        bool databaseCreatedNew,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (maximumVersion < 1 || maximumVersion > SqliteMigrationCatalog.CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumVersion));
        }

        var expectedFingerprints = await GetExpectedSchemaFingerprintsAsync(cancellationToken).ConfigureAwait(false);
        var applicationId = await ReadPragmaInt32Async(
            connection,
            "PRAGMA application_id;",
            cancellationToken).ConfigureAwait(false);
        var userVersion = await ReadPragmaInt32Async(
            connection,
            "PRAGMA user_version;",
            cancellationToken).ConfigureAwait(false);
        var hasLedger = await HasObjectAsync(connection, "table", "schema_migrations", cancellationToken)
            .ConfigureAwait(false);
        var hasSchema = await HasUnownedSchemaAsync(connection, transaction: null, cancellationToken)
            .ConfigureAwait(false);

        List<AppliedMigration> applied;
        if (databaseCreatedNew)
        {
            if (applicationId != 0 || userVersion != 0 || hasLedger || hasSchema)
            {
                throw new JobStoreCorruptionException("store_identity_mismatch");
            }

            applied = [];
        }
        else
        {
            if (applicationId != ApplicationId)
            {
                throw new JobStoreCorruptionException("store_identity_mismatch");
            }
            if (userVersion > SqliteMigrationCatalog.CurrentVersion || userVersion > maximumVersion)
            {
                throw new JobStoreVersionException();
            }
            if (!hasLedger || userVersion < 1)
            {
                throw new JobStoreCorruptionException("store_migration_mismatch");
            }

            var actualFingerprint = await ComputeSchemaFingerprintAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualFingerprint, expectedFingerprints[userVersion - 1], StringComparison.Ordinal))
            {
                throw new JobStoreCorruptionException("store_schema_mismatch");
            }

            applied = await ReadAppliedMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
            ValidateAppliedMigrations(applied, userVersion, maximumVersion, expectedFingerprints);
        }

        foreach (var migration in SqliteMigrationCatalog.All.Where(
                     item => item.Version > applied.Count && item.Version <= maximumVersion))
        {
            var appliedMigration = await ApplyMigrationAsync(
                connection,
                migration,
                maximumVersion,
                expectedFingerprints,
                cancellationToken).ConfigureAwait(false);
            applied.Add(appliedMigration);
        }

        await ValidateCurrentSchemaAsync(
            connection,
            applied,
            maximumVersion,
            expectedFingerprints,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AppliedMigration> ApplyMigrationAsync(
        SqliteConnection connection,
        SqliteMigration migration,
        int maximumVersion,
        IReadOnlyList<string> expectedFingerprints,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);

        var currentApplicationId = await ReadPragmaInt32Async(
            connection,
            transaction,
            "PRAGMA application_id;",
            cancellationToken).ConfigureAwait(false);
        var currentUserVersion = await ReadPragmaInt32Async(
            connection,
            transaction,
            "PRAGMA user_version;",
            cancellationToken).ConfigureAwait(false);
        if (currentUserVersion > SqliteMigrationCatalog.CurrentVersion ||
            currentUserVersion > maximumVersion)
        {
            throw new JobStoreVersionException();
        }

        var currentHasLedger = await HasObjectAsync(
            connection,
            transaction,
            "table",
            "schema_migrations",
            cancellationToken).ConfigureAwait(false);
        if (currentUserVersion >= migration.Version)
        {
            if (currentApplicationId != ApplicationId || !currentHasLedger)
            {
                throw new JobStoreCorruptionException("store_migration_mismatch");
            }

            var concurrentlyApplied = await ReadAppliedMigrationsAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);
            ValidateAppliedMigrations(
                concurrentlyApplied,
                currentUserVersion,
                maximumVersion,
                expectedFingerprints);
            var currentFingerprint = await ComputeSchemaFingerprintAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    currentFingerprint,
                    expectedFingerprints[currentUserVersion - 1],
                    StringComparison.Ordinal))
            {
                throw new JobStoreCorruptionException("store_schema_mismatch");
            }

            return concurrentlyApplied[migration.Version - 1];
        }

        if (currentUserVersion != migration.Version - 1)
        {
            throw new JobStoreCorruptionException("store_migration_mismatch");
        }

        if (migration.Version == 1)
        {
            if (currentApplicationId != 0 || currentHasLedger)
            {
                throw new JobStoreCorruptionException("store_identity_mismatch");
            }
            if (await HasUnownedSchemaAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
            {
                throw new JobStoreCorruptionException("store_schema_mismatch");
            }
        }
        else
        {
            if (currentApplicationId != ApplicationId || !currentHasLedger)
            {
                throw new JobStoreCorruptionException("store_migration_mismatch");
            }
            var currentApplied = await ReadAppliedMigrationsAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);
            ValidateAppliedMigrations(
                currentApplied,
                currentUserVersion,
                maximumVersion,
                expectedFingerprints);
            var currentFingerprint = await ComputeSchemaFingerprintAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(currentFingerprint, expectedFingerprints[migration.Version - 2], StringComparison.Ordinal))
            {
                throw new JobStoreCorruptionException("store_schema_mismatch");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = migration.Script;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var schemaFingerprint = await ComputeSchemaFingerprintAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(schemaFingerprint, expectedFingerprints[migration.Version - 1], StringComparison.Ordinal))
        {
            throw new JobStoreCorruptionException("store_schema_mismatch");
        }

        var appliedAtUtc = FormatTimestamp(_timeProvider.GetUtcNow());
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO schema_migrations (
                    version,
                    migration_name,
                    script_sha256,
                    schema_sha256,
                    applied_at_utc)
                VALUES ($version, $name, $script_sha256, $schema_sha256, $applied_at_utc);
                """;
            command.Parameters.AddWithValue("$version", migration.Version);
            command.Parameters.AddWithValue("$name", migration.Name);
            command.Parameters.AddWithValue("$script_sha256", migration.ScriptSha256);
            command.Parameters.AddWithValue("$schema_sha256", schemaFingerprint);
            command.Parameters.AddWithValue("$applied_at_utc", appliedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await ExecuteCompiledPragmaAsync(
            connection,
            transaction,
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA application_id = {ApplicationId};"),
            cancellationToken).ConfigureAwait(false);
        await ExecuteCompiledPragmaAsync(
            connection,
            transaction,
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {migration.Version};"),
            cancellationToken).ConfigureAwait(false);

        if (_faultInjector is not null)
        {
            await _faultInjector.OnFaultPointAsync(
                JobStoreFaultPoint.BeforeMigrationCommit,
                migration.Version,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (_faultInjector is not null)
        {
            await _faultInjector.OnFaultPointAsync(
                JobStoreFaultPoint.AfterMigrationCommit,
                migration.Version,
                cancellationToken).ConfigureAwait(false);
        }

        return new AppliedMigration(
            migration.Version,
            migration.Name,
            migration.ScriptSha256,
            schemaFingerprint,
            appliedAtUtc);
    }

    private static async Task<List<AppliedMigration>> ReadAppliedMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        await ReadAppliedMigrationsAsync(
            connection,
            transaction: null,
            cancellationToken).ConfigureAwait(false);

    private static async Task<List<AppliedMigration>> ReadAppliedMigrationsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            var applied = new List<AppliedMigration>();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT version, migration_name, script_sha256, schema_sha256, applied_at_utc
                FROM schema_migrations
                ORDER BY version
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", SqliteMigrationCatalog.CurrentVersion + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                applied.Add(new AppliedMigration(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }

            return applied;
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidCastException or FormatException or OverflowException)
        {
            throw new JobStoreCorruptionException("store_migration_mismatch");
        }
    }

    private static void ValidateAppliedMigrations(
        IReadOnlyList<AppliedMigration> applied,
        int userVersion,
        int maximumVersion,
        IReadOnlyList<string> expectedFingerprints)
    {
        if (userVersion > SqliteMigrationCatalog.CurrentVersion || userVersion > maximumVersion)
        {
            throw new JobStoreVersionException();
        }
        if (applied.Count > SqliteMigrationCatalog.CurrentVersion || applied.Count > maximumVersion)
        {
            throw new JobStoreCorruptionException("store_migration_mismatch");
        }
        if (applied.Count != userVersion)
        {
            throw new JobStoreCorruptionException("store_migration_mismatch");
        }

        for (var index = 0; index < applied.Count; index++)
        {
            var expected = SqliteMigrationCatalog.All[index];
            var actual = applied[index];
            if (actual.Version != expected.Version ||
                !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal) ||
                !string.Equals(actual.ScriptSha256, expected.ScriptSha256, StringComparison.Ordinal) ||
                !string.Equals(actual.SchemaSha256, expectedFingerprints[index], StringComparison.Ordinal) ||
                !IsLowerHexSha256(actual.ScriptSha256) ||
                !IsLowerHexSha256(actual.SchemaSha256) ||
                !DateTimeOffset.TryParseExact(
                    actual.AppliedAtUtc,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                throw new JobStoreCorruptionException("store_migration_mismatch");
            }
        }
    }

    private static async Task ValidateCurrentSchemaAsync(
        SqliteConnection connection,
        IReadOnlyList<AppliedMigration> applied,
        int expectedVersion,
        IReadOnlyList<string> expectedFingerprints,
        CancellationToken cancellationToken)
    {
        var applicationId = await ReadPragmaInt32Async(connection, "PRAGMA application_id;", cancellationToken)
            .ConfigureAwait(false);
        var userVersion = await ReadPragmaInt32Async(connection, "PRAGMA user_version;", cancellationToken)
            .ConfigureAwait(false);
        if (applicationId != ApplicationId || userVersion != expectedVersion || applied.Count != expectedVersion)
        {
            throw new JobStoreCorruptionException("store_migration_mismatch");
        }

        var fingerprint = await ComputeSchemaFingerprintAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(fingerprint, expectedFingerprints[expectedVersion - 1], StringComparison.Ordinal) ||
            !string.Equals(fingerprint, applied[^1].SchemaSha256, StringComparison.Ordinal))
        {
            throw new JobStoreCorruptionException("store_schema_mismatch");
        }
    }

    private static async Task<IReadOnlyList<string>> GetExpectedSchemaFingerprintsAsync(
        CancellationToken cancellationToken)
    {
        if (_expectedSchemaFingerprints is not null)
        {
            return _expectedSchemaFingerprints;
        }

        await ExpectedSchemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_expectedSchemaFingerprints is not null)
            {
                return _expectedSchemaFingerprints;
            }

            var fingerprints = new List<string>(SqliteMigrationCatalog.All.Count);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            foreach (var migration in SqliteMigrationCatalog.All)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = migration.Script;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                fingerprints.Add(await ComputeSchemaFingerprintAsync(connection, cancellationToken).ConfigureAwait(false));
            }

            _expectedSchemaFingerprints = fingerprints.AsReadOnly();
            return _expectedSchemaFingerprints;
        }
        finally
        {
            ExpectedSchemaGate.Release();
        }
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static async Task<bool> HasUnownedSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE name NOT LIKE 'sqlite_%'
            );
            """;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<bool> HasObjectAsync(
        SqliteConnection connection,
        string type,
        string name,
        CancellationToken cancellationToken) =>
        await HasObjectAsync(
            connection,
            transaction: null,
            type,
            name,
            cancellationToken).ConfigureAwait(false);

    private static async Task<bool> HasObjectAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string type,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE type = $type AND name = $name
            );
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<int> ReadPragmaInt32Async(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken) =>
        await ReadPragmaInt32Async(
            connection,
            transaction: null,
            commandText,
            cancellationToken).ConfigureAwait(false);

    private static async Task<int> ReadPragmaInt32Async(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteCompiledPragmaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task<string> ComputeSchemaFingerprintAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        ComputeSchemaFingerprintAsync(connection, transaction: null, cancellationToken);

    private static async Task<string> ComputeSchemaFingerprintAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(int)];
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT type, name, tbl_name, COALESCE(sql, '')
            FROM sqlite_schema
            WHERE name NOT LIKE 'sqlite_%'
            ORDER BY type, name, tbl_name
            LIMIT $schema_limit;
            """;
        command.Parameters.AddWithValue("$schema_limit", MaximumSchemaObjects + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var schemaObjectCount = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            schemaObjectCount++;
            if (schemaObjectCount > MaximumSchemaObjects)
            {
                throw new JobStoreCorruptionException("store_schema_mismatch");
            }

            for (var index = 0; index < 4; index++)
            {
                var bytes = Encoding.UTF8.GetBytes(reader.GetString(index));
                BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record AppliedMigration(
        int Version,
        string Name,
        string ScriptSha256,
        string SchemaSha256,
        string AppliedAtUtc);
}
