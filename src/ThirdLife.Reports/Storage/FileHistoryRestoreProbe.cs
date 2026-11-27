using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using ThirdLife.Core.Handover;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Reports.Storage;

/// <summary>Independently reads the exact harmless sample from two protected directories.</summary>
public sealed class FileHistoryRestoreProbe
{
    private const int MaximumSampleBytes = 4096;
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

    public FileHistorySampleObservation? Observe(FileHistoryRestoreSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return OperatingSystem.IsWindows() ? ObserveWindows(sample) : null;
    }

    [SupportedOSPlatform("windows")]
    private FileHistorySampleObservation? ObserveWindows(FileHistoryRestoreSample sample)
    {
        try
        {
            using var originalRoot = new DiagnosticStoreSecurity(_originalDirectory);
            using var recoveredRoot = new DiagnosticStoreSecurity(_recoveredDirectory);
            using var original = originalRoot.OpenValidatedRecord(Path.Combine(_originalDirectory, sample.FileName),
                FileShare.Read, createRootIfMissing: false);
            using var recovered = recoveredRoot.OpenValidatedRestoredSample(Path.Combine(_recoveredDirectory, sample.FileName));
            if (WindowsDiagnosticFileIdentity.GetIdentity(original.SafeFileHandle) ==
                WindowsDiagnosticFileIdentity.GetIdentity(recovered.SafeFileHandle))
            {
                return null;
            }

            return new FileHistorySampleObservation(sample.SampleId, ReadDigest(original), ReadDigest(recovered));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or
            DiagnosticContractException or NotSupportedException)
        {
            return null;
        }
    }

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
