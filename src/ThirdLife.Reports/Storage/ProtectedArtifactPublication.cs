using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Reports.Storage;

internal sealed class ProtectedPublicationException(string message, bool destinationChanged) : IOException(message)
{
    internal bool DestinationChanged { get; } = destinationChanged;
}

internal static class ProtectedArtifactPublication
{
    private const int TransferBytes = 16 * 1024;

    internal static bool Publish(
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
                    throw new ProtectedPublicationException("Protected destination changed.", destinationChanged: true);
                }
            }

            if (!string.Equals(WindowsDiagnosticFileIdentity.GetFinalPath(handle), temporary, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProtectedPublicationException("Protected destination changed.", destinationChanged: true);
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
                throw new ProtectedPublicationException("Protected file publication failed.", destinationChanged: false);
            }

            return true;
        }

        return Marshal.GetLastPInvokeError() switch
        {
            80 or 183 => false,
            _ => throw new ProtectedPublicationException("Protected file publication failed.", destinationChanged: false),
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
