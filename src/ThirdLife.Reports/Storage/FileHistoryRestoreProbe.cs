using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Reports.Storage;

public enum FileHistorySamplePreparation
{
    Prepared = 1,
    CapacityReached,
    Unavailable,
}

/// <summary>Stages and independently reads the exact harmless sample in two protected directories.</summary>
public sealed class FileHistoryRestoreProbe
{
    private const int MaximumSampleBytes = 4096;
    private const int MaximumPreparedSamples = 16;
    private const string WriteLockName = ".restore-sample.lock";
    private readonly string _originalDirectory;
    private readonly string _recoveredDirectory;

    public FileHistoryRestoreProbe(string registeredOriginalDirectory, string registeredRecoveredDirectory)
    {
        _originalDirectory = ValidateDirectory(registeredOriginalDirectory);
        _recoveredDirectory = ValidateDirectory(registeredRecoveredDirectory);
        if (string.Equals(_originalDirectory, _recoveredDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The restore sample requires two separate directories.");
        }
    }

    public FileHistorySamplePreparation Prepare(FileHistoryRestoreSample sample, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return FileHistorySamplePreparation.Unavailable;
        }

        try
        {
            using var original = new DiagnosticStoreSecurity(_originalDirectory);
            using var recovered = new DiagnosticStoreSecurity(_recoveredDirectory);
            original.EnsureSafeRoot(requireCurrentOwner: true);
            recovered.EnsureSafeRoot();
            using var writeLock = original.TryAcquireExclusiveLock(Path.Combine(_originalDirectory, WriteLockName));
            if (writeLock is null) { return FileHistorySamplePreparation.Unavailable; }
            if (Directory.EnumerateFileSystemEntries(_originalDirectory)
                .Where(entry => !Path.GetFileName(entry).Equals(WriteLockName, StringComparison.Ordinal))
                .Take(MaximumPreparedSamples).Count() == MaximumPreparedSamples)
            {
                return FileHistorySamplePreparation.CapacityReached;
            }

            var staging = Path.Combine(_originalDirectory, $".sample-{sample.SampleId:N}.tmp");
            var published = Path.Combine(_originalDirectory, sample.FileName);
            return ProtectedArtifactPublication.Publish(original, sample.CopyContent(), staging, published, cancellationToken,
                requireCurrentOwner: true)
                ? FileHistorySamplePreparation.Prepared : FileHistorySamplePreparation.Unavailable;
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return FileHistorySamplePreparation.Unavailable;
        }
    }

    public FileHistorySampleObservation? Observe(FileHistoryRestoreSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return OperatingSystem.IsWindows() ? ObserveWindows(sample) : null;
    }

    public bool VerifyOriginal(FileHistoryRestoreSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return VerifyOriginal(sample.FileName, new(sample.ByteLength, sample.Sha256));
    }

    public bool VerifyRecordedOriginal(FileHistorySampleAudit recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        return VerifyOriginal(recorded.FileName, new(recorded.ByteLength, recorded.Sha256));
    }

    public JobLocalFileRemoval RemoveRecordedOriginal(JobLocalFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Kind != JobLocalFileKind.FileHistoryOriginal) { return JobLocalFileRemoval.Retained; }

        const string originalPrefix = "thirdlife-restore-";
        var identifier = file.FileName.AsSpan(originalPrefix.Length, 32);
        return ProtectedArtifactRemoval.Remove(_originalDirectory, file, string.Concat(".sample-", identifier, ".tmp"),
            cancellationToken);
    }

    private bool VerifyOriginal(string fileName, FileHistorySampleDigest expected)
    {
        if (!OperatingSystem.IsWindows()) { return false; }
        try
        {
            using var root = new DiagnosticStoreSecurity(_originalDirectory);
            using var original = OpenOriginal(root, fileName);
            return ReadDigest(original) == expected;
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private FileHistorySampleObservation? ObserveWindows(FileHistoryRestoreSample sample)
    {
        try
        {
            using var originalRoot = new DiagnosticStoreSecurity(_originalDirectory);
            using var recoveredRoot = new DiagnosticStoreSecurity(_recoveredDirectory);
            using var original = OpenOriginal(originalRoot, sample.FileName);
            using var recovered = recoveredRoot.OpenValidatedRestoredSample(Path.Combine(_recoveredDirectory, sample.FileName));
            if (WindowsDiagnosticFileIdentity.GetIdentity(original.SafeFileHandle) ==
                WindowsDiagnosticFileIdentity.GetIdentity(recovered.SafeFileHandle))
            {
                return null;
            }

            return new FileHistorySampleObservation(sample.SampleId, ReadDigest(original), ReadDigest(recovered));
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return null;
        }
    }

    private FileStream OpenOriginal(DiagnosticStoreSecurity root, string fileName) =>
        root.OpenValidatedRecord(Path.Combine(_originalDirectory, fileName), FileShare.Read, createRootIfMissing: false);

    private static bool IsProbeFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        SecurityException or DiagnosticContractException or NotSupportedException;

    private static FileHistorySampleDigest ReadDigest(FileStream file)
    {
        var byteCount = file.Length;
        if (byteCount is <= 0 or > MaximumSampleBytes)
        {
            return default;
        }

        Span<byte> bytes = stackalloc byte[(int)byteCount];
        file.ReadExactly(bytes);
        return file.ReadByte() == -1
            ? new FileHistorySampleDigest(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)))
            : default;
    }

    private static string ValidateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("The restore sample requires a protected local directory.", nameof(path));
        }
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(fullPath)!).DriveType is not (DriveType.Fixed or DriveType.Removable))
        {
            throw new ArgumentException("The restore sample requires a protected local directory.", nameof(path));
        }
        return fullPath;
    }
}
