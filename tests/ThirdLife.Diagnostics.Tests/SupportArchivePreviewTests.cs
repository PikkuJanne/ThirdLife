using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Tests;

public sealed class SupportArchivePreviewTests
{
    private static readonly DateTimeOffset PreviewTime = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly string[] FileNames = ["manifest.json", "observations.jsonl"];
    private static readonly Guid SupportToken = new("17896489-b863-4006-a85f-cf139d64dba6");
    private static readonly Guid LocalCorrelation = new("0c61bc51-9f39-46dc-8cfa-0862a10bba52");

    [Fact]
    public void PreviewMatchesEveryFixedZipEntryAndHasNoLocalCorrelation()
    {
        var diagnostic = PackageDiagnostic.Create(DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerFailed,
            DiagnosticCorrelationId.FromOperation(LocalCorrelation));

        var preview = SupportArchivePreview.Create([diagnostic], SupportToken, PreviewTime, default);
        var bytes = preview.CopyArchiveBytes();

        Assert.Equal(bytes, SupportArchivePreview.Create([diagnostic], SupportToken, PreviewTime, default).CopyArchiveBytes());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), preview.ArchiveSha256);
        Assert.Equal(bytes.Length, preview.ArchiveByteCount);
        Assert.DoesNotContain(LocalCorrelation.ToString("N"), Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(FileNames, archive.Entries.Select(entry => entry.FullName));
        Assert.Collection(preview.Files,
            file =>
            {
                using var manifest = JsonDocument.Parse(file.Contents);
                Assert.Equal(SupportArchivePreview.SchemaVersion, manifest.RootElement.GetProperty("schema_version").GetString());
                Assert.Equal(preview.InternalSupportId, manifest.RootElement.GetProperty("internal_support_id").GetString());
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preview.Files[1].Contents))),
                    manifest.RootElement.GetProperty("preview_content_digest_sha256").GetString());
            },
            file => Assert.Contains("package_installer_failed", file.Contents, StringComparison.Ordinal));
        foreach (var (file, entry) in preview.Files.Zip(archive.Entries))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            Assert.Equal(file.Name, entry.FullName);
            Assert.Equal(file.Contents, reader.ReadToEnd());
        }

        bytes[0] ^= 1;
        Assert.NotEqual(bytes, preview.CopyArchiveBytes());
    }

    [Fact]
    public void MachineFactsAreBucketedAndChecksAreClosedAndUnambiguous()
    {
        var machine = new SupportMachineFacts(26300, true, 11_800_000_000, SupportStorageKind.Nvme);
        var checks = new[] { new SupportCheck(SupportCheckId.PendingRestart, SupportCheckOutcome.Unknown) };
        var preview = SupportArchivePreview.Create(machine, checks, [], PreviewTime);
        using var manifest = JsonDocument.Parse(preview.Files[0].Contents);
        Assert.Equal("10.0.26300", manifest.RootElement.GetProperty("os_version").GetString());
        Assert.Equal("x64", manifest.RootElement.GetProperty("hardware_architecture").GetString());
        Assert.Equal("8-15 GiB", manifest.RootElement.GetProperty("memory_bucket").GetString());
        Assert.Equal("nvme", manifest.RootElement.GetProperty("storage_class").GetString());
        Assert.DoesNotContain("11800000000", preview.Files[0].Contents, StringComparison.Ordinal);
        using var observation = JsonDocument.Parse(preview.Files[1].Contents);
        Assert.Equal("pending_restart", observation.RootElement.GetProperty("check_id").GetString());
        Assert.Equal("unknown", observation.RootElement.GetProperty("check_outcome").GetString());

        var duplicate = Assert.Throws<DiagnosticContractException>(() =>
            SupportArchivePreview.Create(machine, [checks[0], checks[0] with { Outcome = SupportCheckOutcome.Passed }], [], PreviewTime));
        Assert.Equal("support_check_duplicated", duplicate.ResultCode);
    }

    [Fact]
    public void RecordCountAndCancellationAreBounded()
    {
        var diagnostic = PackageDiagnostic.Create(DiagnosticPhase.Execute, DiagnosticResultCode.PackageInstallerFailed,
            DiagnosticCorrelationId.FromOperation(LocalCorrelation));

        var tooMany = Enumerable.Repeat(diagnostic, SupportArchivePreview.MaximumRecords + 1);

        Assert.Equal("support_record_count_exceeded",
            Assert.Throws<DiagnosticContractException>(() => SupportArchivePreview.Create(tooMany, PreviewTime)).ResultCode);
        Assert.Throws<OperationCanceledException>(() =>
            SupportArchivePreview.Create([diagnostic], PreviewTime, new CancellationToken(canceled: true)));
    }
}
