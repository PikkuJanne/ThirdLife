using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Reports.Storage;

namespace ThirdLife.Reports.Tests;

public sealed class FileHistoryRestoreProbeTests
{
    [Fact]
    public void OpensOnlyTheGeneratedNameInExistingProtectedRoots()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new RestoreFixture();
        Assert.False(fixture.Probe.VerifyOriginal(fixture.Sample));
        Assert.Null(fixture.Probe.Observe(fixture.Sample));
        Assert.False(Directory.Exists(fixture.Original));
        Assert.False(Directory.Exists(fixture.Recovered));

        fixture.Prepare();
        Assert.True(fixture.Probe.VerifyOriginal(fixture.Sample));
        File.WriteAllText(Path.Combine(fixture.Recovered, "unrelated.txt"), "Unrelated fixture");
        Assert.Null(fixture.Probe.Observe(fixture.Sample));
        fixture.Save(fixture.Recovered, fixture.Sample.CopyContent());
        var observed = Assert.IsType<FileHistorySampleObservation>(fixture.Probe.Observe(fixture.Sample));
        Assert.Equal(fixture.Sample.SampleId, observed.SampleId);
        Assert.Equal(fixture.Sample.Sha256, observed.Original.Sha256);
        Assert.Equal(observed.Original, observed.Recovered);
        Assert.Equal(FileHistoryRestoreOutcome.Pending,
            fixture.Approval.EvaluateRestore(fixture.Handover, true, fixture.Destination, fixture.Sample, observed, false));
    }

    [Fact]
    public void DoesNotAcceptTheSameDirectoryOrAnOversizedOrDifferentCopy()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new RestoreFixture();
        Assert.Throws<ArgumentException>(() => new FileHistoryRestoreProbe(
            fixture.Original, Path.Combine(fixture.Original, ".")));
        fixture.Prepare();
        var content = fixture.Sample.CopyContent();
        content[0] = (byte)'X';
        fixture.Save(fixture.Recovered, content);
        var observed = Assert.IsType<FileHistorySampleObservation>(fixture.Probe.Observe(fixture.Sample));
        Assert.NotEqual(fixture.Sample.Sha256, observed.Recovered.Sha256);
        Assert.Equal(fixture.Sample.Sha256, Convert.ToHexStringLower(SHA256.HashData(fixture.Sample.CopyContent())));
        fixture.Save(fixture.Recovered, new byte[4097]);
        var oversized = Assert.IsType<FileHistorySampleObservation>(fixture.Probe.Observe(fixture.Sample));
        Assert.Equal(default, oversized.Recovered);
    }

    [Fact]
    public void MatchingBytesDoNotOverrideAFileWithBroaderPermissions()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new RestoreFixture();
        fixture.Prepare();
        fixture.Save(fixture.Recovered, fixture.Sample.CopyContent());
        var restored = new FileInfo(Path.Combine(fixture.Recovered, fixture.Sample.FileName));
        var permissions = restored.GetAccessControl();
        permissions.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadData, AccessControlType.Allow));
        restored.SetAccessControl(permissions);
        Assert.Null(fixture.Probe.Observe(fixture.Sample));
    }

    [Fact]
    public void RecordedOriginalRequiresExistingProtectedRootsAndExactFileBytes()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new RestoreFixture();
        var recorded = new FileHistorySampleAudit(fixture.Sample.SampleId, 1, fixture.Sample.Sha256,
            fixture.Sample.ByteLength, new OperatorId("operator-restore-proof"), DateTimeOffset.UtcNow,
            FileHistorySampleAuditState.Preparing);
        Assert.Equal(fixture.Sample.FileName, recorded.FileName);
        Assert.False(fixture.Probe.VerifyRecordedOriginal(recorded));
        Assert.False(Directory.Exists(fixture.Original));
        Assert.False(Directory.Exists(fixture.Recovered));

        fixture.Prepare();
        var path = Path.Combine(fixture.Original, fixture.Sample.FileName);
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(fixture.Probe.VerifyRecordedOriginal(recorded));
        }
        Assert.True(fixture.Probe.VerifyRecordedOriginal(recorded));

        var file = new FileInfo(path);
        var permissions = file.GetAccessControl();
        permissions.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadData, AccessControlType.Allow));
        file.SetAccessControl(permissions);
        Assert.False(fixture.Probe.VerifyRecordedOriginal(recorded));
        Assert.Equal(fixture.Sample.CopyContent(), File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Recovered));
    }

    [Fact]
    public void RemovesOnlyTheRecordedOriginalAndLeavesAnExactRecoveredCopy()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new RestoreFixture();
        var recorded = new JobLocalFile(JobLocalFileKind.FileHistoryOriginal, fixture.Sample.FileName,
            fixture.Sample.ByteLength, fixture.Sample.Sha256);
        Assert.Equal(JobLocalFileRemoval.Missing, fixture.Probe.RemoveRecordedOriginal(recorded));
        Assert.False(Directory.Exists(fixture.Original));
        Assert.False(Directory.Exists(fixture.Recovered));

        fixture.Prepare();
        fixture.Save(fixture.Recovered, fixture.Sample.CopyContent());
        var recovered = Path.Combine(fixture.Recovered, fixture.Sample.FileName);
        using (var lockedCopy = File.Open(recovered, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(JobLocalFileRemoval.Removed, fixture.Probe.RemoveRecordedOriginal(recorded));
        }

        Assert.False(File.Exists(Path.Combine(fixture.Original, fixture.Sample.FileName)));
        Assert.Equal(fixture.Sample.CopyContent(), File.ReadAllBytes(recovered));
        Assert.Equal(JobLocalFileRemoval.Missing, fixture.Probe.RemoveRecordedOriginal(recorded));
    }

    [Fact]
    public void ExactSamplePreparationIsBoundedAndPreservesEarlierFiles()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new RestoreFixture();
        Assert.Equal(FileHistorySamplePreparation.Prepared, fixture.Probe.Prepare(fixture.Sample));
        Assert.Equal(FileHistorySamplePreparation.Unavailable, fixture.Probe.Prepare(fixture.Sample));
        for (var index = 1; index < 16; index++)
        {
            var additional = fixture.Approval.CreateRestoreSample(fixture.Handover, true, fixture.Destination);
            Assert.Equal(FileHistorySamplePreparation.Prepared, fixture.Probe.Prepare(additional));
        }

        var beyondLimit = fixture.Approval.CreateRestoreSample(fixture.Handover, true, fixture.Destination);
        Assert.Equal(FileHistorySamplePreparation.CapacityReached, fixture.Probe.Prepare(beyondLimit));
        Assert.Equal(fixture.Sample.CopyContent(), File.ReadAllBytes(Path.Combine(fixture.Original, fixture.Sample.FileName)));
        Assert.False(File.Exists(Path.Combine(fixture.Original, beyondLimit.FileName)));
        Assert.Empty(Directory.EnumerateFiles(fixture.Recovered));
    }

    private sealed class RestoreFixture : IDisposable
    {
        private readonly DirectoryInfo _owned = Directory.CreateTempSubdirectory("ThirdLife-restore-proof-");
        private readonly JobId _job = new("job-restore-proof");
        public string Original => Path.Combine(_owned.FullName, "source");
        public string Recovered => Path.Combine(_owned.FullName, "restored");
        public FileHistoryDestinationObservation Destination { get; } = new(Guid.NewGuid(), true, 20_000);
        public SavedRecipientHandover Handover { get; }
        public FileHistoryApproval Approval { get; }
        public FileHistoryRestoreSample Sample { get; }
        public FileHistoryRestoreProbe Probe => new(Original, Recovered);

        public RestoreFixture()
        {
            Handover = SavedRecipientHandover.Evaluate(_job, false,
                [new(1, HandoverMode.Assisted, null, null, false, new OperatorId("operator-restore-proof"), DateTimeOffset.UtcNow)]);
            var review = Assert.IsType<FileHistoryReview>(FileHistoryReview.Inspect(Handover, true, Destination, 1, true).Review);
            Approval = review.Approve(review.ReviewId, true);
            Sample = Approval.CreateRestoreSample(Handover, true, Destination);
        }

        public void Prepare()
        {
            Assert.Equal(FileHistorySamplePreparation.Prepared, Probe.Prepare(Sample));
        }

        public void Save(string root, byte[] bytes) => File.WriteAllBytes(Path.Combine(root, Sample.FileName), bytes);

        public void Dispose() => _owned.Delete(recursive: true);
    }
}
