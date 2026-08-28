using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
internal sealed class PersistenceTestWorkspace : IDisposable
{
    private readonly string _basePath;
    private bool _disposed;

    public PersistenceTestWorkspace()
    {
        _basePath = Path.Combine(
            Path.GetTempPath(),
            string.Concat("ThirdLife-TL0102-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));
        Directory.CreateDirectory(_basePath);
        StoreRoot = Path.Combine(_basePath, "store");
    }

    public string StoreRoot { get; }

    public string GetPath(string name) => Path.Combine(_basePath, name);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount)
    {
        _utcNow = _utcNow.Add(amount);
    }
}

internal sealed class BlockingFaultInjector : IJobStoreFaultInjector
{
    private readonly JobStoreFaultPoint _target;
    private readonly int? _detail;
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BlockingFaultInjector(JobStoreFaultPoint target, int? detail = null)
    {
        _target = target;
        _detail = detail;
    }

    public Task Reached => _reached.Task;

    public void Release() => _release.TrySetResult();

    public async ValueTask OnFaultPointAsync(
        JobStoreFaultPoint point,
        int detail,
        CancellationToken cancellationToken)
    {
        if (point != _target || (_detail is not null && detail != _detail.Value))
        {
            return;
        }

        _reached.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ThrowingFaultInjector : IJobStoreFaultInjector
{
    private readonly JobStoreFaultPoint _target;
    private readonly int? _detail;

    public ThrowingFaultInjector(JobStoreFaultPoint target, int? detail = null)
    {
        _target = target;
        _detail = detail;
    }

    public ValueTask OnFaultPointAsync(
        JobStoreFaultPoint point,
        int detail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (point == _target && (_detail is null || detail == _detail.Value))
        {
            throw new InjectedPersistenceException();
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class InjectedPersistenceException : Exception
{
}

internal static class PersistenceTestData
{
    public static readonly DateTimeOffset Timestamp = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public static Job CreateJob(string suffix = "001") =>
        new(new JobId(string.Concat("job-synthetic-", suffix)), new DeviceId(string.Concat("device-synthetic-", suffix)), Timestamp);

    public static Observation CreateObservation(string suffix = "001") =>
        new(
            CreateMetadata(
                string.Concat("evidence-observation-", suffix),
                EvidenceClassification.Observed,
                ProvenanceKind.ProviderObservation,
                ValueAvailability.Available),
            new EvidenceKey(string.Concat("system.synthetic_", suffix)),
            EvidenceValue.FromInteger(8_589_934_592),
            "bytes",
            limitationCode: null);

    public static SanitizationEvidence CreateSanitization(string suffix = "001") =>
        CreateSanitization(SanitizationState.Verified, suffix, Timestamp.AddMinutes(1));

    public static SanitizationEvidence CreateSanitization(
        SanitizationState state,
        string suffix,
        DateTimeOffset collectedAtUtc)
    {
        var unknown = state == SanitizationState.Unknown;
        var hasMedia = state is SanitizationState.Verified or SanitizationState.ReplacementStorage or SanitizationState.Failed;
        return new SanitizationEvidence(
            new EvidenceMetadata(
                new EvidenceId(string.Concat("evidence-sanitization-", suffix)),
                PrivacyClassification.WorkshopRestricted,
                unknown ? EvidenceClassification.NotAvailable : EvidenceClassification.Observed,
                new ProviderId("provider-synthetic-001"),
                collectedAtUtc,
                new EvidenceProvenance(ProvenanceKind.SyntheticFixture, string.Concat("source-synthetic-", suffix)),
                unknown ? ValueAvailability.Unknown : ValueAvailability.Available),
            state,
            unknown ? "not_available" : "external_sanitization",
            unknown ? null : new OperatorId("operator-synthetic-001"),
            unknown ? null : Timestamp.AddMinutes(-30),
            hasMedia ? new MediaIdentifier(string.Concat("SYNTHETIC-MEDIA-", suffix)) : null,
            state switch
            {
                SanitizationState.Unknown => SanitizationVerificationState.NotAvailable,
                SanitizationState.Failed => SanitizationVerificationState.Failed,
                _ => SanitizationVerificationState.Verified,
            },
            "community-policy@1.0.0");
    }

    public static HumanTestEvidence CreateHumanTest(Job job, string suffix = "001") =>
        new(
            new HumanTestId(string.Concat("human-test-synthetic-", suffix)),
            job.JobId,
            job.DeviceId,
            CreateMetadata(
                string.Concat("evidence-human-test-", suffix),
                EvidenceClassification.HumanConfirmed,
                ProvenanceKind.HumanConfirmation,
                ValueAvailability.Available),
            HumanTestResult.Pass,
            new OperatorId("operator-synthetic-001"),
            limitationCode: null);

    public static EvidenceMetadata CreateMetadata(
        string evidenceId,
        EvidenceClassification classification,
        ProvenanceKind provenanceKind,
        ValueAvailability availability) =>
        new(
            new EvidenceId(evidenceId),
            PrivacyClassification.WorkshopRestricted,
            classification,
            new ProviderId("provider-synthetic-001"),
            Timestamp.AddMinutes(1),
            new EvidenceProvenance(provenanceKind, string.Concat("source-synthetic-", evidenceId)),
            availability);
}

[SupportedOSPlatform("windows")]
internal static class SqliteTestControl
{
    public static async Task<int> ReadUserVersionAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public static async Task ExecuteAsync(string databasePath, string sql)
    {
        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task<int> ReadJobCountAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM jobs;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync().ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public static async Task<int> ReadGateDecisionCountAsync(string databasePath)
    {
        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sanitization_gate_decisions;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync().ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    public static async Task SeedJobsAsync(string databasePath, int count)
    {
        if (count is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH digits(value) AS (
                VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)
            ),
            numbers(value) AS (
                SELECT (thousands.value * 1000) + (hundreds.value * 100) +
                       (tens.value * 10) + ones.value + 1
                FROM digits AS thousands
                CROSS JOIN digits AS hundreds
                CROSS JOIN digits AS tens
                CROSS JOIN digits AS ones
            )
            INSERT INTO jobs (
                job_id,
                device_id,
                created_at_utc,
                job_payload_json,
                job_payload_sha256)
            SELECT
                printf('job-seeded-%05d', value),
                printf('device-seeded-%05d', value),
                '2030-01-01T08:00:00.0000000+00:00',
                '{}',
                lower(hex(zeroblob(32)))
            FROM numbers
            WHERE value <= $count
            ORDER BY value;
            """;
        command.Parameters.AddWithValue("$count", count);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task InsertVersionOneJobAsync(string databasePath, Job job)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(job, DomainJson.CreateStrictOptions());
        var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
        var digest = Convert.ToHexStringLower(SHA256.HashData(jsonBytes));

        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO jobs (job_id, device_id, created_at_utc, job_payload_json, job_payload_sha256)
            VALUES ($job_id, $device_id, $created_at_utc, $payload_json, $payload_sha256);
            """;
        command.Parameters.AddWithValue("$job_id", job.JobId.Value);
        command.Parameters.AddWithValue("$device_id", job.DeviceId.Value);
        command.Parameters.AddWithValue("$created_at_utc", job.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$payload_json", json);
        command.Parameters.AddWithValue("$payload_sha256", digest);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task SeedCheckpointsAsync(string databasePath, JobId jobId, int count)
    {
        if (count is < 1 or > 9_999)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        await using var connection = await OpenAsync(databasePath).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH digits(value) AS (
                VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)
            ),
            numbers(value) AS (
                SELECT (thousands.value * 1000) + (hundreds.value * 100) +
                       (tens.value * 10) + ones.value + 1
                FROM digits AS thousands
                CROSS JOIN digits AS hundreds
                CROSS JOIN digits AS tens
                CROSS JOIN digits AS ones
            )
            INSERT INTO store_checkpoints (
                checkpoint_id,
                job_id,
                checkpoint_kind,
                recorded_at_utc)
            SELECT
                printf('checkpoint-seeded-%05d', value),
                $job_id,
                'evidence_committed',
                $recorded_at_utc
            FROM numbers
            WHERE value <= $count
            ORDER BY value;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        command.Parameters.AddWithValue("$recorded_at_utc", PersistenceTestData.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$count", count);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static string HashFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4_096,
            FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static void AssertProtectedAcl(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        AssertProtectedSecurityDescriptor(security);
    }

    public static void AssertProtectedFileAcl(string path)
    {
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        AssertProtectedSecurityDescriptor(security);
    }

    private static void AssertProtectedSecurityDescriptor(FileSystemSecurity security)
    {
        Assert.True(security.AreAccessRulesProtected);
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("A Windows identity is required.");
        var allowedSids = new HashSet<SecurityIdentifier>
        {
            currentUser,
            new(WellKnownSidType.LocalSystemSid, domainSid: null),
            new(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
        };
        Assert.Equal(currentUser, security.GetOwner(typeof(SecurityIdentifier)));

        var fullControlSids = new HashSet<SecurityIdentifier>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: true,
                     typeof(SecurityIdentifier)))
        {
            Assert.False(rule.IsInherited);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            var identity = Assert.IsType<SecurityIdentifier>(rule.IdentityReference);
            Assert.Contains(identity, allowedSids);
            if ((rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            {
                fullControlSids.Add(identity);
            }
        }

        Assert.True(allowedSids.SetEquals(fullControlSids));
    }

    public static async Task CreateJunctionAsync(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The junction helper did not start.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        Assert.Equal(0, process.ExitCode);
    }

    public static async Task CreateHardLinkAsync(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/H");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The hard-link helper did not start.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        Assert.Equal(0, process.ExitCode);
    }

    private static async Task<SqliteConnection> OpenAsync(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync().ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = PERSIST;";
            var mode = Convert.ToString(
                await command.ExecuteScalarAsync().ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            Assert.Equal("persist", mode, ignoreCase: true);
        }
        return connection;
    }
}
