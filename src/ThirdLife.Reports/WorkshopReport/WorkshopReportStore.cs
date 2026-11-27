using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Core.Jobs;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;

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

public sealed class WorkshopReportArtifact
{
    internal WorkshopReportArtifact(object issuer, JobId jobId, string fileName, int byteLength, byte[] digest)
    {
        Issuer = issuer;
        JobId = jobId;
        FileName = fileName;
        ByteLength = byteLength;
        Digest = digest;
    }

    public JobId JobId { get; }
    public string FileName { get; }
    internal object Issuer { get; }
    internal int ByteLength { get; }
    internal byte[] Digest { get; }
}

/// <summary>Retains protected report copies and opens only verified artifacts issued by this store.</summary>
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
            var committed = SaveStaged(security, bytes, temporary, destination, cancellationToken);
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

        using var security = OpenRoot(create: false);
        var path = Path.Combine(_rootDirectory, artifact.FileName);
        try
        {
            using var stream = security.OpenValidatedRecord(path, FileShare.Read);
            if (stream.Length != artifact.ByteLength || stream.Length is <= 0 or > MaximumArtifactBytes)
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.ArtifactChanged);
            }

            var bytes = new byte[artifact.ByteLength];
            for (var offset = 0; offset < bytes.Length; offset += TransferBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.ReadExactly(bytes.AsSpan(offset, Math.Min(TransferBytes, bytes.Length - offset)));
            }

            if (stream.ReadByte() != -1 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), artifact.Digest))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.ArtifactChanged);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Utf8.GetString(bytes);
        }
        catch (WorkshopReportStoreException)
        {
            throw;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception) || exception is DecoderFallbackException)
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
                security.EnsureSafeRoot();
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

    private static bool SaveStaged(
        DiagnosticStoreSecurity security,
        byte[] bytes,
        string temporary,
        string destination,
        CancellationToken cancellationToken)
    {
        using var stream = security.CreateRestrictedFile(
            temporary, FileShare.None, FileOptions.WriteThrough, TransferBytes);
        var committed = false;
        try
        {
            for (var offset = 0; offset < bytes.Length; offset += TransferBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Write(bytes.AsSpan(offset, Math.Min(TransferBytes, bytes.Length - offset)));
            }

            stream.Flush(flushToDisk: true);
            security.EnsureSafeRoot();
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                committed = PublishWindowsFile(stream.SafeFileHandle, temporary, destination);
            }
            else
            {
                stream.Dispose();
                try
                {
                    File.Move(temporary, destination, overwrite: false);
                    committed = true;
                }
                catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
                {
                    return false;
                }
            }

            return committed;
        }
        finally
        {
            if (!committed)
            {
                // Cleanup owns only this staging file. An interruption may leave a
                // protected temporary counted against the folder's storage limits.
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        WindowsDiagnosticFileIdentity.MarkForDeletion(stream.SafeFileHandle);
                    }
                    else
                    {
                        stream.Dispose();
                        using var owned = security.OpenValidatedRecordForDeletion(temporary);
                        security.DeleteOpenedRecord(temporary, owned);
                    }
                }
                catch (Exception exception) when (IsFileSystemFailure(exception))
                {
                    // Preserve the original failure and retained reports.
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool PublishWindowsFile(SafeFileHandle handle, string temporary, string destination)
    {
        // The Win32 rename takes an absolute name. Hold each destination
        // component without delete sharing so the lookup stays in this folder.
        var ancestors = new List<SafeFileHandle>();
        try
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(destination)!);
                 directory is not null;
                 directory = directory.Parent)
            {
                var guard = WindowsDiagnosticFileIdentity.OpenDirectoryGuard(directory.FullName);
                ancestors.Add(guard);
                var attributes = WindowsDiagnosticFileIdentity.GetAttributes(guard);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0 ||
                    !string.Equals(WindowsDiagnosticFileIdentity.GetFinalPath(guard), directory.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new WorkshopReportStoreException(WorkshopReportFailure.DestinationUnavailable);
                }
            }

            if (!string.Equals(WindowsDiagnosticFileIdentity.GetFinalPath(handle), temporary, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.DestinationUnavailable);
            }

            return RenameOpenFile(handle, destination);
        }
        finally
        {
            foreach (var guard in ancestors)
            {
                guard.Dispose();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool RenameOpenFile(SafeFileHandle handle, string destination)
    {
        // FILE_RENAME_INFO aligns the root handle to pointer size. The zeroed
        // buffer leaves ReplaceIfExists false and RootDirectory null.
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var fileNameLengthOffset = rootOffset + IntPtr.Size;
        var fileNameOffset = fileNameLengthOffset + sizeof(uint);
        var byteLength = checked(destination.Length * sizeof(char));
        var rename = new byte[checked(fileNameOffset + byteLength + sizeof(char))];
        BinaryPrimitives.WriteInt32LittleEndian(rename.AsSpan(fileNameLengthOffset), byteLength);
        Encoding.Unicode.GetBytes(destination.AsSpan(), rename.AsSpan(fileNameOffset));
        if (SetFileInformationByHandle(handle, 3, rename, (uint)rename.Length))
        {
            if (!string.Equals(WindowsDiagnosticFileIdentity.GetFinalPath(handle), destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkshopReportStoreException(WorkshopReportFailure.WriteFailed);
            }

            return true;
        }

        return Marshal.GetLastPInvokeError() switch
        {
            80 or 183 => false,
            _ => throw new WorkshopReportStoreException(WorkshopReportFailure.WriteFailed),
        };
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            DiagnosticContractException or NotSupportedException;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int informationClass,
        byte[] information,
        uint informationSize);
}
