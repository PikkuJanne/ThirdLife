using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Diagnostics.Redaction;

public sealed record SupportPreviewFile(string Name, string Contents);

/// <summary>One immutable preview and its exact archive bytes.</summary>
public sealed class SupportArchivePreview
{
    public const string SchemaVersion = "thirdlife.support.v1";
    public const string ManifestVersion = "thirdlife.support-manifest.v1";
    public const int MaximumRecords = 64;
    public const int MaximumArchiveBytes = 256 * 1024;

    private const string ManifestName = "manifest.json";
    private const string RecordsName = "observations.jsonl";
    private static readonly DateTimeOffset ArchiveEntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly byte[] _archive;

    private SupportArchivePreview(string supportId, DateTimeOffset createdAtUtc, IReadOnlyList<SupportPreviewFile> files,
        byte[] archive)
    {
        InternalSupportId = supportId;
        CreatedAtUtc = createdAtUtc;
        Files = files;
        _archive = archive;
        ArchiveSha256 = Digest(archive);
    }

    public string InternalSupportId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public IReadOnlyList<SupportPreviewFile> Files { get; }
    public string ArchiveSha256 { get; }
    public int ArchiveByteCount => _archive.Length;

    public static SupportArchivePreview Create(IEnumerable<StructuredDiagnosticEvent> packageOutcomes,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default) =>
        Create(packageOutcomes, Guid.NewGuid(), createdAtUtc, cancellationToken);

    public static SupportArchivePreview Create(SupportMachineFacts facts, IReadOnlyList<SupportCheck> checks,
        IEnumerable<StructuredDiagnosticEvent> packageOutcomes, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(checks);
        return Create(packageOutcomes, Guid.NewGuid(), createdAtUtc, cancellationToken, facts, checks);
    }

    public bool Matches(SupportMachineFacts facts, IReadOnlyList<SupportCheck> checks,
        IEnumerable<StructuredDiagnosticEvent> packageOutcomes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(checks);
        var supportToken = Guid.ParseExact(InternalSupportId.AsSpan(4), "N");
        var current = Create(packageOutcomes, supportToken, CreatedAtUtc, cancellationToken, facts, checks);
        return string.Equals(current.ArchiveSha256, ArchiveSha256, StringComparison.Ordinal);
    }

    internal static SupportArchivePreview Create(IEnumerable<StructuredDiagnosticEvent> packageOutcomes, Guid supportToken,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken, SupportMachineFacts? facts = null,
        IReadOnlyList<SupportCheck>? checks = null)
    {
        ArgumentNullException.ThrowIfNull(packageOutcomes);
        if (supportToken == Guid.Empty || createdAtUtc == default)
        {
            throw new ArgumentException("A support identity and timestamp are required.");
        }

        var createdAt = createdAtUtc.ToUniversalTime();
        var supportId = $"SUP-{supportToken:N}";
        var records = CollectObservations(checks ?? [], packageOutcomes, cancellationToken);
        var manifest = CreateManifest(supportId, createdAt, records, facts);
        var files = Array.AsReadOnly<SupportPreviewFile>(
        [
            new(ManifestName, Encoding.UTF8.GetString(manifest)),
            new(RecordsName, Encoding.UTF8.GetString(records)),
        ]);

        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, ManifestName, manifest);
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(archive, RecordsName, records);
        }

        if (archiveStream.Length > MaximumArchiveBytes)
        {
            throw new DiagnosticContractException("support_archive_too_large", "The support archive exceeds its size limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(supportId, createdAt, files, archiveStream.ToArray());
    }

    internal byte[] CopyArchiveBytes() => (byte[])_archive.Clone();

    private static byte[] CollectObservations(IReadOnlyList<SupportCheck> checks,
        IEnumerable<StructuredDiagnosticEvent> packageOutcomes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        const int maximumChecks = 8;
        if (checks.Count > maximumChecks)
        {
            throw new DiagnosticContractException("support_record_count_exceeded", "The support preview contains too many checks.");
        }

        var seenChecks = new HashSet<SupportCheckId>();
        foreach (var check in checks.OrderBy(check => check.Check))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenChecks.Add(check.Check))
            {
                throw new DiagnosticContractException("support_check_duplicated", "A support check has more than one outcome.");
            }

            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("check_id", CheckCode(check.Check));
            writer.WriteString("check_outcome", OutcomeCode(check.Outcome));
            writer.WriteEndObject();
            writer.Flush();
            stream.WriteByte((byte)'\n');
        }

        var count = checks.Count;
        foreach (var packageOutcome in packageOutcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaximumRecords)
            {
                throw new DiagnosticContractException("support_record_count_exceeded", "The support preview contains too many events.");
            }

            var projected = PackageDiagnostic.ProjectForSupport(packageOutcome);
            if (stream.Length + projected.ByteCount + 1 > MaximumArchiveBytes - 4_096)
            {
                throw new DiagnosticContractException("support_archive_too_large", "The support archive exceeds its size limit.");
            }

            stream.Write(projected.GetUtf8Json());
            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }

    private static byte[] CreateManifest(string supportId, DateTimeOffset createdAtUtc, ReadOnlySpan<byte> records,
        SupportMachineFacts? facts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", SchemaVersion);
            writer.WriteString("manifest_version", ManifestVersion);
            writer.WriteString("internal_support_id", supportId);
            writer.WriteString("application_version", DiagnosticBuildIdentity.Current);
            var informational = typeof(SupportArchivePreview).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var suffix = informational?.Split('+', 2) is [_, var revision] ? revision : null;
            if (suffix is { Length: 40 or 64 } && suffix.All(char.IsAsciiHexDigit))
            {
                writer.WriteString("build_version", suffix.ToLowerInvariant());
            }

            writer.WriteString("export_created_at_utc", createdAtUtc.UtcDateTime);
            facts?.Write(writer);
            writer.WriteString("preview_content_digest_sha256", Digest(records));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string CheckCode(SupportCheckId check) => check switch
    {
        SupportCheckId.OperatingSystem => "operating_system",
        SupportCheckId.Activation => "activation",
        SupportCheckId.WindowsUpdate => "windows_update",
        SupportCheckId.PendingRestart => "pending_restart",
        SupportCheckId.SystemDiskHealth => "system_disk_health",
        SupportCheckId.SecurityBaseline => "security_baseline",
        SupportCheckId.EssentialCapabilities => "essential_capabilities",
        SupportCheckId.WorkshopFinalization => "workshop_finalization",
        _ => throw new ArgumentOutOfRangeException(nameof(check)),
    };

    private static string OutcomeCode(SupportCheckOutcome outcome) => outcome switch
    {
        SupportCheckOutcome.Passed => "passed",
        SupportCheckOutcome.Failed => "failed",
        SupportCheckOutcome.Unknown => "unknown",
        SupportCheckOutcome.NotAvailable => "not_available",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static void WriteEntry(ZipArchive archive, string name, ReadOnlySpan<byte> bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = ArchiveEntryTime;
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
