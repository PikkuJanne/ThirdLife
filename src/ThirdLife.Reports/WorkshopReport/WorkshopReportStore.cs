using System.Security;
using System.Security.Cryptography;
using System.Text;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Reports.Storage;

namespace ThirdLife.Reports.WorkshopReport;

public enum WorkshopReportFailure
{
    DestinationUnavailable,
    NameCollision,
    WriteFailed,
    ReadFailed,
    ContentTooLarge,
    ArtifactChanged,
    CapacityExceeded,
}

public sealed class WorkshopReportStoreException : IOException
{
    internal WorkshopReportStoreException(WorkshopReportFailure failure)
        : base(failure switch
        {
            WorkshopReportFailure.DestinationUnavailable => "The protected workshop report folder is unavailable.",
            WorkshopReportFailure.NameCollision => "A file already occupies the proposed report name. It was preserved.",
            WorkshopReportFailure.WriteFailed => "The workshop report could not be saved.",
            WorkshopReportFailure.ReadFailed => "The generated workshop report could not be opened.",
            WorkshopReportFailure.ContentTooLarge => "The workshop report exceeds the supported size.",
            WorkshopReportFailure.ArtifactChanged => "The generated workshop report has changed. Generate a new copy to view it.",
            WorkshopReportFailure.CapacityExceeded => "The workshop report folder is full. Review retained reports before generating another.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        }) => Failure = failure;

    public WorkshopReportFailure Failure { get; }
}

public sealed record WorkshopReportReceipt(string FileName, int ByteLength, string Sha256);

public sealed class WorkshopReportArtifact
{
    internal WorkshopReportArtifact(object issuer, JobId jobId, string fileName, int byteLength, byte[] digest)
    {
        Issuer = issuer;
        JobId = jobId;
        Receipt = new(fileName, byteLength, Convert.ToHexStringLower(digest));
    }

    public JobId JobId { get; }
    public string FileName => Receipt.FileName;
    internal object Issuer { get; }
    public WorkshopReportReceipt Receipt { get; }
}

/// <summary>Retains protected report copies and verifies exact bytes before opening a saved file.</summary>
public sealed class WorkshopReportStore
{
    public const int MaximumArtifactBytes = 8 * 1024 * 1024;
    public const int MaximumRetainedFiles = 64;
    public const long MaximumRetainedBytes = 64L * 1024 * 1024;
    private const int ReservedFreeBytes = 4 * 1024 * 1024;
    private const int TransferBytes = 16 * 1024;
    private const string LockFileName = ".workshop-write.lock";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string _rootDirectory;
    private readonly string _filePrefix;
    private readonly string _fileSuffix;
    private readonly Func<Guid> _nextReportToken;
    private readonly object _issuer = new();

    public WorkshopReportStore(string rootDirectory) : this(rootDirectory, Guid.NewGuid)
    {
    }

    internal WorkshopReportStore(string rootDirectory, Func<Guid> nextReportToken)
        : this(rootDirectory, nextReportToken, printableRecipientGuide: false)
    {
    }

    internal WorkshopReportStore(string rootDirectory, Func<Guid> nextReportToken, bool printableRecipientGuide)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(nextReportToken);
        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("The trusted report folder must use an absolute path.", nameof(rootDirectory));
        }

        _rootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _nextReportToken = nextReportToken;
        _filePrefix = printableRecipientGuide ? "recipient-guide-" : "workshop-";
        _fileSuffix = printableRecipientGuide ? ".html" : ".txt";
    }

    public WorkshopReportArtifact Write(
        JobId jobId,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentException.ThrowIfNullOrEmpty(content);
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Length > MaximumArtifactBytes)
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.ContentTooLarge);
        }

        byte[] bytes;
        try
        {
            var byteCount = Utf8.GetByteCount(content);
            if (byteCount > MaximumArtifactBytes)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.ContentTooLarge);
            }

            bytes = Utf8.GetBytes(content);
        }
        catch (EncoderFallbackException)
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.WriteFailed);
        }

        var fileName = $"{_filePrefix}{_nextReportToken():N}{_fileSuffix}";
        var destination = Path.Combine(_rootDirectory, fileName);
        using var security = OpenRoot(create: true);
        try
        {
            using var writeLock = security.TryAcquireExclusiveLock(Path.Combine(_rootDirectory, LockFileName))
                ?? throw new WorkshopReportStoreException(WorkshopReportFailure.DestinationUnavailable);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.NameCollision);
            }

            EnsureCapacity(bytes.Length, cancellationToken);
            var digest = SHA256.HashData(bytes);
            var temporary = Path.Combine(_rootDirectory, $".{_filePrefix}{Guid.NewGuid():N}.tmp");
            cancellationToken.ThrowIfCancellationRequested();
            var committed = ProtectedArtifactPublication.Publish(security, bytes, temporary, destination, cancellationToken);
            if (!committed)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.NameCollision);
            }

            return new WorkshopReportArtifact(_issuer, jobId, fileName, bytes.Length, digest);
        }
        catch (WorkshopReportStoreException)
        {
            throw;
        }
        catch (ProtectedPublicationException exception)
        {
            throw new WorkshopReportStoreException(exception.DestinationChanged
                ? WorkshopReportFailure.DestinationUnavailable : WorkshopReportFailure.WriteFailed);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.WriteFailed);
        }
    }

    public string Read(WorkshopReportArtifact artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(artifact.Issuer, _issuer))
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.ReadFailed);
        }

        return ReadText(artifact.Receipt, cancellationToken);
    }

    public string ReadSavedReport(JobId jobId, HandoverFileReceipt saved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Output.Kind != HandoverOutputKind.WorkshopRecord || saved.Output.Scope.JobId != jobId)
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.ReadFailed);
        }

        var receipt = new WorkshopReportReceipt(saved.FileName, saved.ByteLength, saved.Output.Sha256);
        ValidateReceipt(receipt);
        return ReadText(receipt, cancellationToken);
    }

    private string ReadText(WorkshopReportReceipt receipt, CancellationToken cancellationToken)
    {
        try
        {
            return Utf8.GetString(ReadVerifiedBytes(receipt, cancellationToken));
        }
        catch (DecoderFallbackException)
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.ReadFailed);
        }
    }

    public void VerifyReceipt(WorkshopReportReceipt receipt, CancellationToken cancellationToken = default)
    {
        ValidateReceipt(receipt);
        ReadVerifiedBytes(receipt, cancellationToken);
    }

    private void ValidateReceipt(WorkshopReportReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var name = receipt.FileName;
        if (name is null || name.Length != _filePrefix.Length + 32 + _fileSuffix.Length ||
            !name.StartsWith(_filePrefix, StringComparison.Ordinal) || !name.EndsWith(_fileSuffix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name.AsSpan(_filePrefix.Length, 32), "N", out var token) ||
            name != $"{_filePrefix}{token:N}{_fileSuffix}" || receipt.ByteLength is <= 0 or > MaximumArtifactBytes ||
            receipt.Sha256 is not { Length: 64 } ||
            receipt.Sha256.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.ReadFailed);
        }
    }

    private byte[] ReadVerifiedBytes(WorkshopReportReceipt receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var security = OpenRoot(create: false);
        var path = Path.Combine(_rootDirectory, receipt.FileName);
        try
        {
            using var stream = security.OpenValidatedRecord(path, FileShare.Read, createRootIfMissing: false);
            if (stream.Length != receipt.ByteLength || stream.Length is <= 0 or > MaximumArtifactBytes)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.ArtifactChanged);
            }

            var bytes = new byte[receipt.ByteLength];
            for (var offset = 0; offset < bytes.Length; offset += TransferBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.ReadExactly(bytes.AsSpan(offset, Math.Min(TransferBytes, bytes.Length - offset)));
            }

            if (stream.ReadByte() != -1 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(receipt.Sha256)))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.ArtifactChanged);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return bytes;
        }
        catch (WorkshopReportStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.ReadFailed);
        }
    }

    private DiagnosticStoreSecurity OpenRoot(bool create)
    {
        try
        {
            var volume = Path.GetPathRoot(_rootDirectory);
            if (volume is null || _rootDirectory == volume ||
                (OperatingSystem.IsWindows() &&
                    (volume.Length != 3 || volume[1] != ':' ||
                        _rootDirectory.AsSpan(2).Contains(':') || new DriveInfo(volume).DriveType != DriveType.Fixed)))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.DestinationUnavailable);
            }

            if (!create && !Directory.Exists(_rootDirectory))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.ReadFailed);
            }

            var security = new DiagnosticStoreSecurity(_rootDirectory);
            try
            {
                security.EnsureSafeRoot(createIfMissing: create);
                return security;
            }
            catch
            {
                security.Dispose();
                throw;
            }
        }
        catch (WorkshopReportStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.DestinationUnavailable);
        }
    }

    private void EnsureCapacity(int incomingBytes, CancellationToken cancellationToken)
    {
        var count = 0;
        var available = MaximumRetainedBytes - incomingBytes;
        foreach (var entry in Directory.EnumerateFileSystemEntries(_rootDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(entry).Equals(LockFileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (++count >= MaximumRetainedFiles)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.CapacityExceeded);
            }

            var file = new FileInfo(entry);
            if ((file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.DestinationUnavailable);
            }

            available -= file.Length;
            if (available < 0)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.CapacityExceeded);
            }
        }

        var volume = new DriveInfo(Path.GetPathRoot(_rootDirectory)!);
        if (volume.AvailableFreeSpace < incomingBytes + ReservedFreeBytes)
        {
            throw new WorkshopReportStoreException(WorkshopReportFailure.CapacityExceeded);
        }
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            DiagnosticContractException or NotSupportedException;
}
