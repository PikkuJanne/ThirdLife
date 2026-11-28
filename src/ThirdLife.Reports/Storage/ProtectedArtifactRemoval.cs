using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Core.Jobs;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Reports.Storage;

internal static class ProtectedArtifactRemoval
{
    private const int TransferBytes = 16 * 1024;

    internal static JobLocalFileRemoval Remove(string registeredRoot, JobLocalFile file, string stagingName,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) { return JobLocalFileRemoval.Retained; }

        var guards = new List<SafeFileHandle>();
        try
        {
            if (!GuardExistingRoot(registeredRoot, guards)) { return JobLocalFileRemoval.Missing; }

            using var security = new DiagnosticStoreSecurity(registeredRoot);
            security.EnsureSafeRoot(createIfMissing: false, requireCurrentOwner: true);
            var staging = RemoveExact(security, Path.Combine(registeredRoot, stagingName), file, token);
            if (staging == JobLocalFileRemoval.Retained) { return staging; }

            var published = RemoveExact(security, Path.Combine(registeredRoot, file.FileName), file, token);
            return published == JobLocalFileRemoval.Missing ? staging : published;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return JobLocalFileRemoval.Retained;
        }
        finally
        {
            Release(guards);
        }
    }

    internal static bool HasPublicationCollision(string registeredRoot, string fileName, string stagingName)
    {
        var published = Path.Combine(registeredRoot, fileName);
        var staging = Path.Combine(registeredRoot, stagingName);
        if (!OperatingSystem.IsWindows())
        {
            return File.Exists(published) || Directory.Exists(published) ||
                File.Exists(staging) || Directory.Exists(staging);
        }

        var guards = new List<SafeFileHandle>();
        try
        {
            if (!GuardExistingRoot(registeredRoot, guards)) { return false; }

            using var security = new DiagnosticStoreSecurity(registeredRoot);
            security.EnsureSafeRoot(createIfMissing: false, requireCurrentOwner: true);
            using var publishedEntry = OpenEntryIfPresent(published);
            using var stagedEntry = OpenEntryIfPresent(staging);
            return publishedEntry is not null || stagedEntry is not null;
        }
        finally
        {
            Release(guards);
        }
    }

    [SupportedOSPlatform("windows")]
    private static JobLocalFileRemoval RemoveExact(DiagnosticStoreSecurity security, string path, JobLocalFile file,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var stream = security.OpenValidatedRecordForDeletion(path, requireExclusiveOwnership: true);
            if (!Matches(stream, file, token)) { return JobLocalFileRemoval.Retained; }

            // Once disposition starts, observe the outcome even if cancellation arrives.
            token.ThrowIfCancellationRequested();
            security.DeleteOpenedRecord(path, stream, requireExclusiveOwnership: true);
        }
        catch (IOException exception) when (IsMissing(exception))
        {
            using var missingCheck = OpenEntryIfPresent(path);
            return missingCheck is null ? JobLocalFileRemoval.Missing : JobLocalFileRemoval.Retained;
        }

        using var entry = OpenEntryIfPresent(path);
        return entry is null ? JobLocalFileRemoval.Removed : JobLocalFileRemoval.Retained;
    }

    private static bool Matches(FileStream stream, JobLocalFile file, CancellationToken token)
    {
        if (stream.Length != file.ByteLength) { return false; }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[TransferBytes];
        var remaining = file.ByteLength;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            var block = buffer[..Math.Min(remaining, buffer.Length)];
            stream.ReadExactly(block);
            hash.AppendData(block);
            remaining -= block.Length;
        }

        return stream.ReadByte() == -1 && CryptographicOperations.FixedTimeEquals(
            hash.GetHashAndReset(), Convert.FromHexString(file.Sha256));
    }

    [SupportedOSPlatform("windows")]
    private static bool GuardExistingRoot(string root, List<SafeFileHandle> guards)
    {
        var volume = Path.GetPathRoot(root);
        if (volume is not { Length: 3 } || volume[1] != ':' || root == volume ||
            root.AsSpan(2).Contains(':') || new DriveInfo(volume).DriveType != DriveType.Fixed)
        {
            throw new IOException("The protected output root is unavailable.");
        }

        var paths = new Stack<string>();
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            paths.Push(directory.FullName);
        }

        foreach (var path in paths)
        {
            var guard = OpenEntryIfPresent(path);
            if (guard is null) { return false; }
            guards.Add(guard);
            var attributes = WindowsDiagnosticFileIdentity.GetAttributes(guard);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(WindowsDiagnosticFileIdentity.GetFinalPath(guard), path, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The protected output root changed.");
            }
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle? OpenEntryIfPresent(string path)
    {
        try
        {
            return WindowsDiagnosticFileIdentity.OpenDirectoryGuard(path);
        }
        catch (IOException exception) when (IsMissing(exception))
        {
            return null;
        }
    }

    private static bool IsMissing(IOException exception) => exception.HResult is 2 or 3;

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            DiagnosticContractException or NotSupportedException;

    private static void Release(List<SafeFileHandle> guards)
    {
        foreach (var guard in guards) { guard.Dispose(); }
    }
}
