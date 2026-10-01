using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Inventory.Windows;

internal readonly record struct BatteryReportFileIdentity(uint Volume, ulong Index);
internal sealed record BatteryReportFileInformation(BatteryReportFileIdentity Identity, FileAttributes Attributes,
    uint Links, ulong Length);

internal static class BatteryReportFiles
{
    private static readonly Guid LocalApplicationDataId = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    internal static string LocalApplicationData()
    {
        nint path = 0;
        try
        {
            // RETURN_FILTER_REDIRECTION_TARGET | DONT_VERIFY. Resolve the private target
            // in packaged processes without probing a potentially remote known-folder path.
            // The workspace then enforces local-drive, ancestor, identity and ACL checks.
            var result = NativeMethods.SHGetKnownFolderPath(in LocalApplicationDataId, 0x00044000, 0, out path);
            if (result != 0)
            {
                throw new BatteryReportNativeException(unchecked((uint)result));
            }

            if (path == 0)
            {
                throw new BatteryReportWorkspaceException();
            }

            for (var length = 0; length < 32768; length++)
            {
                if (Marshal.ReadInt16(path, length * sizeof(char)) == 0)
                {
                    return Marshal.PtrToStringUni(path, length);
                }
            }

            throw new BatteryReportWorkspaceException();
        }
        finally
        {
            if (path != 0)
            {
                NativeMethods.CoTaskMemFree(path);
            }
        }
    }

    internal static bool IsFixedDrive(string root) => NativeMethods.GetDriveTypeW(root) == 3;

    internal static SafeFileHandle OpenDirectoryGuard(string path) => Open(path, 0x80, 3, 0x02200000);
    internal static SafeFileHandle OpenFileGuard(string path) => Open(path, 0x80, 3, 0x00200000);
    internal static SafeFileHandle OpenForDeletion(string path) => Open(path, 0x80010080, 0, 0x00200000);

    private static SafeFileHandle Open(string path, uint access, uint share, uint flags)
    {
        var handle = NativeMethods.CreateFileW(path, access, share, 0, 3, flags, 0);
        if (handle.IsInvalid)
        {
            var error = unchecked((uint)Marshal.GetLastPInvokeError());
            handle.Dispose();
            throw new BatteryReportNativeException(error);
        }

        return handle;
    }

    internal static bool IsMissing(string path)
    {
        using var handle = NativeMethods.CreateFileW(path, 0, 7, 0, 3, 0x00200000, 0);
        if (!handle.IsInvalid)
        {
            return false;
        }

        var error = unchecked((uint)Marshal.GetLastPInvokeError());
        return error == 2 ? true : throw new BatteryReportNativeException(error);
    }

    internal static BatteryReportFileInformation Information(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileInformationByHandle(handle, out var value))
        {
            throw new BatteryReportNativeException(unchecked((uint)Marshal.GetLastPInvokeError()));
        }

        return new(new(value.VolumeSerialNumber, ((ulong)value.FileIndexHigh << 32) | value.FileIndexLow),
            (FileAttributes)value.FileAttributes, value.NumberOfLinks, ((ulong)value.FileSizeHigh << 32) | value.FileSizeLow);
    }

    internal static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[32768];
        var length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        var path = new string(buffer, 0, checked((int)length));
        if (!path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path[4..]));
    }

    internal static void MarkForDeletion(SafeFileHandle handle)
    {
        var value = new FileDisposition { DeleteFile = 1 };
        if (!NativeMethods.SetFileInformationByHandle(handle, 4, in value, (uint)Marshal.SizeOf<FileDisposition>()))
        {
            throw new BatteryReportNativeException(unchecked((uint)Marshal.GetLastPInvokeError()));
        }
    }

    internal static ulong AvailableBytes(string root)
    {
        if (!NativeMethods.GetDiskFreeSpaceExW(root, out var available, out _, out _))
        {
            throw new BatteryReportNativeException(unchecked((uint)Marshal.GetLastPInvokeError()));
        }

        return available;
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("shell32.dll", ExactSpelling = true)]
        internal static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("ole32.dll", ExactSpelling = true)]
        internal static extern void CoTaskMemFree(nint memory);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint GetDriveTypeW(string root);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
            uint disposition, uint flags, nint template);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] buffer, uint length, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
            in FileDisposition information, uint length);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDiskFreeSpaceExW(string directory, out ulong available, out ulong callerTotal, out ulong totalFree);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDisposition
    {
        public byte DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
