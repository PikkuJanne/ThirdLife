using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Support;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Redaction;
using ThirdLife.Reports.RecipientGuide;
using ThirdLife.Reports.Support;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.Reports.Tests;

public sealed class WorkshopReportStoreTests
{
    private static readonly JobId Job = new("job-synthetic-storage");
    private static readonly Guid FirstToken = new("372839a4-4d44-428e-8a87-5fe056fcbdf2");
    private const string Report = "WORKSHOP_RESTRICTED\nObservation: café\n";

    [Fact]
    public void WritesExactUtf8AndOnlyItsIssuerCanOpenTheArtifact()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot, () => FirstToken);
        var artifact = store.Write(Job, Report);

        Assert.Equal(Job, artifact.JobId);
        Assert.Equal("workshop-372839a44d44428e8a875fe056fcbdf2.txt", artifact.FileName);
        Assert.Equal(Encoding.UTF8.GetBytes(Report), File.ReadAllBytes(Path.Combine(directory.ReportRoot, artifact.FileName)));
        Assert.Equal(Report, store.Read(artifact));

        var independentStore = new WorkshopReportStore(directory.ReportRoot);
        TheFailure(WorkshopReportFailure.ReadFailed, () => independentStore.Read(artifact));
    }

    [Fact]
    public void ReopenedStoreVerifiesExactReceiptAndRejectsChangedBytesOrDestination()
    {
        using var directory = new FixtureDirectory();
        var saved = new WorkshopReportStore(directory.ReportRoot).Write(Job, Report);
        var reopened = new WorkshopReportStore(directory.ReportRoot);
        reopened.VerifyReceipt(saved.Receipt);

        TheFailure(WorkshopReportFailure.ReadFailed, () => reopened.VerifyReceipt(saved.Receipt with
        {
            FileName = "..\\" + saved.FileName,
        }));
        TheFailure(WorkshopReportFailure.ReadFailed, () => reopened.VerifyReceipt(saved.Receipt with
        {
            Sha256 = "invalid-digest",
        }));
        TheFailure(WorkshopReportFailure.ReadFailed, () => new WorkshopReportStore(directory.ReportRoot,
            Guid.NewGuid, printableRecipientGuide: true).VerifyReceipt(saved.Receipt));

        File.WriteAllText(Path.Combine(directory.ReportRoot, saved.FileName), Report.Replace("café", "safé", StringComparison.Ordinal));
        TheFailure(WorkshopReportFailure.ArtifactChanged, () => reopened.VerifyReceipt(saved.Receipt));
    }

    [Fact]
    public void CollisionPreservesTheExistingReportAndCapability()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot, () => FirstToken);
        var saved = store.Write(Job, Report);

        TheFailure(WorkshopReportFailure.NameCollision, () => store.Write(Job, "different report"));

        Assert.Equal(Report, store.Read(saved));
        Assert.Single(Directory.EnumerateFiles(directory.ReportRoot, "*.txt"));
        Assert.Empty(Directory.EnumerateFiles(directory.ReportRoot, "*.tmp"));
    }

    [Fact]
    public async Task OwnershipCallbackReceivesExactBytesBeforePublicationAndCanPublishOnlyOnce()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot, () => FirstToken);
        Action<CancellationToken>? captured = null;
        var calls = 0;
        var saved = await store.WriteWithOwnershipAsync(Job, Report, (artifact, publish, token) =>
        {
            calls++;
            captured = publish;
            Assert.Equal(Job, artifact.JobId);
            Assert.Equal(Encoding.UTF8.GetByteCount(Report), artifact.Receipt.ByteLength);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Report))), artifact.Receipt.Sha256);
            Assert.False(Directory.Exists(directory.ReportRoot));
            publish(token);
            Assert.Throws<InvalidOperationException>(() => publish(token));
            return Task.CompletedTask;
        });

        Assert.Equal(1, calls);
        Assert.Equal(Report, store.Read(saved));
        Assert.NotNull(captured);
        Assert.Throws<InvalidOperationException>(() => captured(CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(directory.ReportRoot, "*.tmp"));

        var collision = await Assert.ThrowsAsync<WorkshopReportStoreException>(() =>
            store.WriteWithOwnershipAsync(Job, Report, (_, _, _) =>
            {
                calls++;
                return Task.CompletedTask;
            }));
        Assert.Equal(WorkshopReportFailure.NameCollision, collision.Failure);
        Assert.Equal(1, calls);
        Assert.Equal(Report, store.Read(saved));
    }

    [Fact]
    public async Task OwnershipCallbackThatDoesNotPublishLeavesTheFolderAbsent()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot);
        Action<CancellationToken>? delayed = null;
        var failure = await Assert.ThrowsAsync<WorkshopReportStoreException>(() =>
            store.WriteWithOwnershipAsync(Job, Report, (_, publish, _) =>
            {
                delayed = publish;
                return Task.CompletedTask;
            }));

        Assert.Equal(WorkshopReportFailure.WriteFailed, failure.Failure);
        Assert.NotNull(delayed);
        Assert.Throws<InvalidOperationException>(() => delayed(CancellationToken.None));
        Assert.False(Directory.Exists(directory.ReportRoot));
    }

    [Fact]
    public void ExactRemovalDistinguishesMissingFromDenialAndLeavesIndependentFiles()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot, () => FirstToken);
        var reference = new JobLocalFile(JobLocalFileKind.WorkshopReport, $"workshop-{FirstToken:N}.txt",
            Encoding.UTF8.GetByteCount(Report), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Report))));
        Assert.Equal(JobLocalFileRemoval.Missing, store.RemoveRecordedFile(reference));
        Assert.False(Directory.Exists(directory.ReportRoot));
        Assert.Equal(Report, store.Read(store.Write(Job, Report)));
        var path = Path.Combine(directory.ReportRoot, reference.FileName);
        var other = Path.Combine(directory.ReportRoot, "unrelated.txt");
        File.WriteAllText(other, "unrelated file");

        using (var mutation = File.Open(path, FileMode.Open, FileAccess.ReadWrite,
                   FileShare.Read | FileShare.Write | FileShare.Delete))
        {
            Assert.Equal(JobLocalFileRemoval.Retained, store.RemoveRecordedFile(reference));
        }
        var changed = Report.Replace("café", "safé", StringComparison.Ordinal);
        File.WriteAllText(path, changed);
        Assert.Equal(JobLocalFileRemoval.Retained, store.RemoveRecordedFile(reference));
        Assert.Equal(changed, File.ReadAllText(path));

        File.WriteAllText(path, Report);
        var info = new FileInfo(path);
        var permissions = info.GetAccessControl();
        var broadRule = new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadData, AccessControlType.Allow);
        permissions.AddAccessRule(broadRule);
        info.SetAccessControl(permissions);
        Assert.Equal(JobLocalFileRemoval.Retained, store.RemoveRecordedFile(reference));
        Assert.Equal(Report, File.ReadAllText(path));
        permissions.RemoveAccessRuleSpecific(broadRule);
        info.SetAccessControl(permissions);

        Assert.Equal(JobLocalFileRemoval.Removed, store.RemoveRecordedFile(reference));
        Assert.False(File.Exists(path));
        Assert.Equal(JobLocalFileRemoval.Missing, store.RemoveRecordedFile(reference));
        Assert.Equal("unrelated file", File.ReadAllText(other));
    }

    [Fact]
    public void ExactRemovalReconcilesAnIntactOwnedStageAndRetainsPartialStageBytes()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot, () => FirstToken);
        var stamped = store.Write(Job, Report);
        var reference = new JobLocalFile(JobLocalFileKind.WorkshopReport, stamped.FileName,
            stamped.Receipt.ByteLength, stamped.Receipt.Sha256);
        var destination = Path.Combine(directory.ReportRoot, stamped.FileName);
        var staging = Path.Combine(directory.ReportRoot, $".workshop-{FirstToken:N}.tmp");
        File.Move(destination, staging);

        File.WriteAllText(staging, "partial");
        Assert.Equal(JobLocalFileRemoval.Retained, store.RemoveRecordedFile(reference));
        Assert.Equal("partial", File.ReadAllText(staging));
        File.WriteAllText(staging, Report);
        Assert.Equal(JobLocalFileRemoval.Removed, store.RemoveRecordedFile(reference));
        Assert.False(File.Exists(staging));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task GuideUsesItsOwnRegisteredPublicationAndRemovalKind()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var directory = new FixtureDirectory();
        var guideRoot = directory.PathFor("guides");
        var guide = RecipientGuideRenderer.Render(new(RecipientHandover.Start(HandoverMode.Sealed), null, []));
        var store = new RecipientGuideStore(guideRoot);
        var saved = await store.SaveArtifactWithOwnershipAsync(Job, guide, (artifact, publish, token) =>
        {
            Assert.False(Directory.Exists(guideRoot));
            Assert.StartsWith("recipient-guide-", artifact.FileName, StringComparison.Ordinal);
            publish(token);
            return Task.CompletedTask;
        });
        var guidePath = Path.Combine(guideRoot, saved.FileName);
        Assert.Equal(guide.PrintableHtml, File.ReadAllText(guidePath));

        var reference = new JobLocalFile(JobLocalFileKind.RecipientGuide, saved.FileName,
            saved.Receipt.ByteLength, saved.Receipt.Sha256);
        Assert.Equal(JobLocalFileRemoval.Retained, new WorkshopReportStore(guideRoot).RemoveRecordedFile(reference));
        Assert.True(File.Exists(guidePath));
        Assert.Equal(JobLocalFileRemoval.Removed, store.RemoveRecordedFile(reference));
        Assert.False(File.Exists(guidePath));
    }

    [Fact]
    public void ObstructedDestinationIsReportedWithoutChangingItsFile()
    {
        using var directory = new FixtureDirectory();
        const string existing = "existing unrelated fixture";
        File.WriteAllText(directory.ReportRoot, existing);
        var store = new WorkshopReportStore(directory.ReportRoot);

        TheFailure(WorkshopReportFailure.DestinationUnavailable, () => store.Write(Job, Report));
        Assert.Equal(existing, File.ReadAllText(directory.ReportRoot));
    }

    [Fact]
    public void MissingAndTamperedReportsAreNotOpened()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot);
        var original = store.Write(Job, Report);
        var path = Path.Combine(directory.ReportRoot, original.FileName);
        var modified = Report.Replace("café", "safé", StringComparison.Ordinal);
        File.WriteAllText(path, modified);

        TheFailure(WorkshopReportFailure.ArtifactChanged, () => store.Read(original));
        Assert.Equal(modified, File.ReadAllText(path));
        File.Delete(path);
        TheFailure(WorkshopReportFailure.ReadFailed, () => store.Read(original));
    }

    [Fact]
    public void OversizedOrInvalidTextLeavesExistingReportIntact()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot);
        var original = store.Write(Job, Report);
        var largeUtf8 = new string('é', WorkshopReportStore.MaximumArtifactBytes / 2 + 1);

        TheFailure(WorkshopReportFailure.ContentTooLarge, () => store.Write(Job, largeUtf8));
        TheFailure(WorkshopReportFailure.WriteFailed, () => store.Write(Job, "\uD800"));
        Assert.Equal(Report, store.Read(original));
        Assert.Single(Directory.EnumerateFiles(directory.ReportRoot, "*.txt"));
    }

    [Fact]
    public void RetentionLimitPreservesPreviousReportsAndStopsWriting()
    {
        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot);
        var original = store.Write(Job, Report);
        for (var index = 1; index < WorkshopReportStore.MaximumRetainedFiles; index++)
        {
            File.WriteAllText(Path.Combine(directory.ReportRoot, $"fixture-{index:D2}.txt"), "retained fixture");
        }

        TheFailure(WorkshopReportFailure.CapacityExceeded, () => store.Write(Job, Report));
        Assert.Equal(WorkshopReportStore.MaximumRetainedFiles, Directory.EnumerateFiles(directory.ReportRoot, "*.txt").Count());
        Assert.Equal(Report, store.Read(original));
    }

    [Fact]
    public void CancelledWriteNeverCreatesAReportFolder()
    {
        using var directory = new FixtureDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new WorkshopReportStore(directory.ReportRoot);

        Assert.Throws<OperationCanceledException>(() => store.Write(Job, Report, cancellation.Token));
        Assert.False(Directory.Exists(directory.ReportRoot));
    }

    [Fact]
    public void VerificationOfPriorSupportPublicationRejectsChangedBytes()
    {
        using var directory = new FixtureDirectory();
        var preview = SupportArchivePreview.Create([], DateTimeOffset.UtcNow);
        var store = new SupportArchiveStore(directory.ReportRoot);
        var intent = new SupportExportAudit(preview.InternalSupportId, SupportArchivePreview.SchemaVersion,
            SupportArchivePreview.ManifestVersion, preview.ArchiveSha256, DateTimeOffset.UtcNow, new OperatorId("operator-export-test"),
            SupportExportState.Pending);
        var saved = store.Export(preview, preview.ArchiveSha256);
        Assert.Equal(saved, store.VerifyPriorPublication(intent));
        var existing = Path.Combine(directory.ReportRoot, saved.FileName);
        var content = File.ReadAllBytes(existing);
        content[0] ^= 0xff;
        File.WriteAllBytes(existing, content);
        var error = Assert.Throws<DiagnosticContractException>(() => store.VerifyPriorPublication(intent));
        Assert.Equal("support_archive_mismatch", error.ResultCode);
    }

    [Fact]
    public void SupportExportRequiresThePreviewDigestAndPublishesThoseBytesOnce()
    {
        using var directory = new FixtureDirectory();
        var preview = SupportArchivePreview.Create([], DateTimeOffset.UtcNow);
        var store = new SupportArchiveStore(directory.ReportRoot);

        var changed = Assert.Throws<DiagnosticContractException>(() => store.Export(preview, new string('0', 64)));
        Assert.Equal("support_preview_changed", changed.ResultCode);
        Assert.False(Directory.Exists(directory.ReportRoot));
        Assert.Throws<OperationCanceledException>(() =>
            store.Export(preview, preview.ArchiveSha256, new CancellationToken(canceled: true)));
        Assert.False(Directory.Exists(directory.ReportRoot));

        var archive = store.Export(preview, preview.ArchiveSha256);
        var path = Path.Combine(directory.ReportRoot, archive.FileName);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(preview.ArchiveSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(preview.ArchiveByteCount, bytes.Length);

        var collision = Assert.Throws<DiagnosticContractException>(() => store.Export(preview, preview.ArchiveSha256));
        Assert.Equal("support_file_exists", collision.ResultCode);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(directory.ReportRoot, "*.tmp"));
    }

    [Fact]
    public async Task JunctionOrSymbolicLinkCannotRedirectReportStorage()
    {
        using var directory = new FixtureDirectory();
        var target = Directory.CreateDirectory(directory.PathFor("another-folder")).FullName;
        var outsideName = $"workshop-{FirstToken:N}.txt";
        var outside = Path.Combine(target, outsideName);
        File.WriteAllText(outside, "unchanged");
        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", directory.ReportRoot, target })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Junction test helper did not start.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, process.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(directory.ReportRoot, target);
        }

        try
        {
            var store = new WorkshopReportStore(directory.ReportRoot);
            TheFailure(WorkshopReportFailure.DestinationUnavailable, () => store.Write(Job, Report));
            var reference = new JobLocalFile(JobLocalFileKind.WorkshopReport, outsideName, 9,
                Convert.ToHexStringLower(SHA256.HashData("unchanged"u8)));
            Assert.Equal(JobLocalFileRemoval.Retained, store.RemoveRecordedFile(reference));
            var preview = SupportArchivePreview.Create([], DateTimeOffset.UtcNow);
            var support = new SupportArchiveStore(directory.ReportRoot);
            Assert.Throws<DiagnosticContractException>(() => support.Export(preview, preview.ArchiveSha256));
            Assert.Equal("unchanged", File.ReadAllText(outside));
            Assert.Single(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(directory.ReportRoot);
        }
    }

    [Fact]
    public void WindowsHardLinkedReportCannotBeOpened()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new FixtureDirectory();
        var store = new WorkshopReportStore(directory.ReportRoot);
        var original = store.Write(Job, Report);
        var alias = directory.PathFor("hard-link.txt");
        Assert.True(CreateHardLink(alias, Path.Combine(directory.ReportRoot, original.FileName), IntPtr.Zero));

        TheFailure(WorkshopReportFailure.ReadFailed, () => store.Read(original));
        Assert.Equal(JobLocalFileRemoval.Retained, store.RemoveRecordedFile(new(JobLocalFileKind.WorkshopReport,
            original.FileName, original.Receipt.ByteLength, original.Receipt.Sha256)));
        Assert.Equal(Report, File.ReadAllText(alias));
    }

    private static void TheFailure(WorkshopReportFailure failure, Action operation) =>
        Assert.Equal(failure, Assert.Throws<WorkshopReportStoreException>(operation).Failure);

    private sealed class FixtureDirectory : IDisposable
    {
        private readonly DirectoryInfo _parent = Directory.CreateTempSubdirectory("tl0115-report-");

        public string ReportRoot => PathFor("Reports");
        public string PathFor(string name) => Path.Combine(_parent.FullName, name);
        public void Dispose() => _parent.Delete(recursive: true);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr attributes);
}
