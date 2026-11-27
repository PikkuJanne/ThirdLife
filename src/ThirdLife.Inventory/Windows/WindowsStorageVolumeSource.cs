using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsStorageVolumeSource : IStorageVolumeSource
{
    private const int MaximumPathCharacters = 32_768;

    public StorageVolumeSnapshot Read(CancellationToken cancellationToken)
    {
        var status = ReadWindowsDirectory(cancellationToken, out var directoryPath);
        return directoryPath is null
            ? StorageSourceFailures.Volume(status)
            : ReadVolume(directoryPath, cancellationToken, out _);
    }

    internal static SystemInventorySourceValue<char> ReadSystemDriveLetter(CancellationToken cancellationToken)
    {
        var status = ReadWindowsDirectory(cancellationToken, out var directoryPath);
        if (directoryPath is null)
        {
            return StorageSourceFailures.Field<char>(status);
        }

        status = ResolveFixedVolumePath(directoryPath, cancellationToken, out var volumePath);
        return volumePath is { Length: 3 }
            ? SystemInventorySourceValue<char>.Available(char.ToUpperInvariant(volumePath[0]))
            : StorageSourceFailures.Field<char>(status);
    }

    private static StorageCollectionInfo ReadWindowsDirectory(CancellationToken cancellationToken, out string? directoryPath)
    {
        directoryPath = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return StorageSourceFailures.Unavailable;
        }

        var directory = new char[MaximumPathCharacters];
        var length = NativeMethods.GetSystemWindowsDirectoryW(directory, MaximumPathCharacters);
        if (length == 0)
        {
            return NativeFailure();
        }

        if (length >= MaximumPathCharacters)
        {
            return StorageSourceFailures.InvalidData;
        }

        directoryPath = new string(directory, 0, checked((int)length));
        return StorageSourceFailures.Collected;
    }

    internal static (ulong? AvailableBytes, string? MountPath) ReadPreparationVolume(string directoryPath)
    {
        try
        {
            if (!IsLocalAbsolutePath(directoryPath))
            {
                return default;
            }

            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
            if (NativeMethods.GetDriveTypeW(path[..3]) != 3 || !HasPlainDirectoryPath(path))
            {
                return default;
            }

            var volume = ReadVolume(path, CancellationToken.None, out var mountPath);
            return volume.CallerAvailableBytes.Status == SystemInventorySourceStatus.Available
                ? (volume.CallerAvailableBytes.GetRequiredValue(), mountPath)
                : default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }

    internal static bool? ShareVolume(string? firstMountPath, string? secondMountPath)
    {
        if (firstMountPath is null || secondMountPath is null)
        {
            return null;
        }

        if (string.Equals(firstMountPath, secondMountPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // One fixed volume can have more than one drive letter or mounted-folder alias.
        var firstVolume = ReadVolumeIdentity(firstMountPath);
        var secondVolume = ReadVolumeIdentity(secondMountPath);
        return firstVolume is not null && secondVolume is not null ? firstVolume == secondVolume : null;
    }

    private static bool HasPlainDirectoryPath(string path)
    {
        // Visit ancestors before children so a redirected ancestor is not followed.
        const FileAttributes mask = FileAttributes.Directory | FileAttributes.ReparsePoint;
        for (var index = 3; index < path.Length; index++)
        {
            if (path[index] == '\\' && (File.GetAttributes(path[..index]) & mask) != FileAttributes.Directory)
            {
                return false;
            }
        }

        return (File.GetAttributes(path) & mask) == FileAttributes.Directory;
    }

    private static Guid? ReadVolumeIdentity(string mountPath)
    {
        var buffer = new char[64];
        if (!NativeMethods.GetVolumeNameForVolumeMountPointW(mountPath, buffer, (uint)buffer.Length))
        {
            return null;
        }

        var terminator = Array.IndexOf(buffer, '\0');
        const string prefix = @"\\?\Volume";
        return terminator == prefix.Length + 39 &&
            buffer.AsSpan(0, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
            buffer[terminator - 1] == '\\' &&
            Guid.TryParseExact(buffer.AsSpan(prefix.Length, 38), "B", out var identity) && identity != Guid.Empty
                ? identity : null;
    }

    private static StorageVolumeSnapshot ReadVolume(string directoryPath, CancellationToken cancellationToken, out string? mountPath)
    {
        mountPath = null;
        var status = ResolveFixedVolumePath(directoryPath, cancellationToken, out var volumePath);
        if (volumePath is null)
        {
            return StorageSourceFailures.Volume(status);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.GetDiskFreeSpaceExW(volumePath, out var callerAvailable, out var callerTotal, out var totalFree))
        {
            return StorageSourceFailures.Volume(NativeFailure());
        }

        cancellationToken.ThrowIfCancellationRequested();
        mountPath = volumePath;
        return new(
            StorageSourceFailures.Collected,
            SystemInventorySourceValue<ulong>.Available(callerTotal),
            SystemInventorySourceValue<ulong>.Available(callerAvailable),
            SystemInventorySourceValue<ulong>.Available(totalFree));
    }

    private static StorageCollectionInfo ResolveFixedVolumePath(string directoryPath, CancellationToken cancellationToken,
        out string? volumePath)
    {
        volumePath = null;
        if (!IsLocalAbsolutePath(directoryPath))
        {
            return StorageSourceFailures.InvalidData;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var volume = new char[MaximumPathCharacters];
        if (!NativeMethods.GetVolumePathNameW(directoryPath, volume, MaximumPathCharacters))
        {
            return NativeFailure();
        }

        var terminator = Array.IndexOf(volume, '\0');
        if (terminator < 0)
        {
            return StorageSourceFailures.InvalidData;
        }

        var path = new string(volume, 0, terminator);
        if (!IsLocalAbsolutePath(path) || NativeMethods.GetDriveTypeW(path) != 3)
        {
            return StorageSourceFailures.Unavailable;
        }

        cancellationToken.ThrowIfCancellationRequested();
        volumePath = path;
        return StorageSourceFailures.Collected;
    }

    internal static bool IsLocalAbsolutePath(string path) => path.Length is >= 3 and < MaximumPathCharacters &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' && !path.Any(char.IsControl);

    private static StorageCollectionInfo NativeFailure() =>
        StorageSourceFailures.FromNativeError(unchecked((uint)Marshal.GetLastPInvokeError()));

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern uint GetSystemWindowsDirectoryW([Out] char[] buffer, uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumePathNameW(string path, [Out] char[] volumePath, uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumeNameForVolumeMountPointW(string mountPath, [Out] char[] volumeName, uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint GetDriveTypeW(string rootPath);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDiskFreeSpaceExW(
            string directory,
            out ulong availableBytes,
            out ulong callerTotalBytes,
            out ulong totalFreeBytes);
    }
}
