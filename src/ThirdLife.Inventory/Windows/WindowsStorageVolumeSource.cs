using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsStorageVolumeSource : IStorageVolumeSource
{
    private const int MaximumPathCharacters = 32_768;

    public StorageVolumeSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return StorageSourceFailures.Volume(StorageSourceFailures.Unavailable);
        }

        var directory = new char[MaximumPathCharacters];
        var length = NativeMethods.GetSystemWindowsDirectoryW(directory, MaximumPathCharacters);
        if (length == 0)
        {
            return Failure();
        }

        if (length >= MaximumPathCharacters)
        {
            return StorageSourceFailures.Volume(StorageSourceFailures.InvalidData);
        }

        var directoryPath = new string(directory, 0, checked((int)length));
        if (!IsLocalAbsolutePath(directoryPath))
        {
            return StorageSourceFailures.Volume(StorageSourceFailures.InvalidData);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var volume = new char[MaximumPathCharacters];
        if (!NativeMethods.GetVolumePathNameW(directoryPath, volume, MaximumPathCharacters))
        {
            return Failure();
        }

        var terminator = Array.IndexOf(volume, '\0');
        if (terminator < 0)
        {
            return StorageSourceFailures.Volume(StorageSourceFailures.InvalidData);
        }

        var volumePath = new string(volume, 0, terminator);
        if (!IsLocalAbsolutePath(volumePath) || NativeMethods.GetDriveTypeW(volumePath) != 3)
        {
            return StorageSourceFailures.Volume(StorageSourceFailures.Unavailable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.GetDiskFreeSpaceExW(volumePath, out var callerAvailable, out var callerTotal, out var totalFree))
        {
            return Failure();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(
            StorageSourceFailures.Collected,
            SystemInventorySourceValue<ulong>.Available(callerTotal),
            SystemInventorySourceValue<ulong>.Available(callerAvailable),
            SystemInventorySourceValue<ulong>.Available(totalFree));
    }

    internal static bool IsLocalAbsolutePath(string path) => path.Length is >= 3 and < MaximumPathCharacters &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' && !path.Any(char.IsControl);

    private static StorageVolumeSnapshot Failure() => StorageSourceFailures.Volume(
        StorageSourceFailures.FromNativeError(unchecked((uint)Marshal.GetLastPInvokeError())));

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
