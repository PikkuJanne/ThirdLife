using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Diagnostics.Logging;

internal sealed class DiagnosticStoreSecurity : IDisposable
{
    private readonly string _rootPath;
    private readonly SecurityIdentifier? _currentUser;
    private readonly HashSet<SecurityIdentifier> _allowedIdentities = [];
    private SafeFileHandle? _rootGuard;
    private WindowsDiagnosticFileIdentity.FileIdentity _rootIdentity;

    public DiagnosticStoreSecurity(string rootPath)
    {
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (OperatingSystem.IsWindows())
        {
            _currentUser = WindowsIdentity.GetCurrent().User ?? throw UnsafeStore();
            _allowedIdentities =
            [
                _currentUser,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
            ];
        }
    }

    public void EnsureSafeRoot()
    {
        ValidateExistingPathComponents(_rootPath);
        if (File.Exists(_rootPath))
        {
            throw UnsafeStore();
        }

        if (OperatingSystem.IsWindows())
        {
            EnsureSafeWindowsRoot();
        }
        else
        {
            EnsureSafePortableRoot();
        }
    }

    public FileStream CreateRestrictedFile(
        string path,
        FileShare share,
        FileOptions options,
        int bufferSize)
    {
        EnsureSafeRoot();
        EnsureContained(path);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException("The registered diagnostic file already exists.");
        }

        if (OperatingSystem.IsWindows())
        {
            var stream = new FileInfo(path).Create(
                FileMode.CreateNew,
                FileSystemRights.FullControl,
                share,
                bufferSize,
                options,
                CreateFileSecurity());
            try
            {
                ValidateWindowsFile(path, stream);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        var portable = new FileStream(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.CreateNew,
                Share = share,
                Options = options,
                BufferSize = bufferSize,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
        ValidatePortableFile(path);
        return portable;
    }

    public FileStream? TryAcquireExclusiveLock(string path)
    {
        EnsureSafeRoot();
        EnsureContained(path);
        if (Directory.Exists(path))
        {
            throw UnsafeStore();
        }

        if (!File.Exists(path))
        {
            try
            {
                using var created = CreateRestrictedFile(
                    path,
                    FileShare.ReadWrite,
                    FileOptions.WriteThrough,
                    bufferSize: 1);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        try
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    ValidateWindowsFile(path, stream);
                }
                else
                {
                    ValidatePortableFile(path);
                }

                if (stream.Length != 0)
                {
                    throw UnsafeStore();
                }

                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (IOException)
        {
            return null;
        }
    }

    public FileStream OpenValidatedRecord(string path, FileShare share)
    {
        EnsureSafeRoot();
        EnsureContained(path);
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            share,
            bufferSize: 4096,
            FileOptions.RandomAccess);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                ValidateWindowsFile(path, stream);
            }
            else
            {
                ValidatePortableFile(path);
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public FileStream OpenValidatedRecordForDeletion(string path)
    {
        EnsureSafeRoot();
        EnsureContained(path);
        if (OperatingSystem.IsWindows())
        {
            var handle = WindowsDiagnosticFileIdentity.OpenFileForDeletion(path);
            try
            {
                var stream = new FileStream(handle, FileAccess.Read, bufferSize: 4096, isAsync: false);
                try
                {
                    ValidateWindowsFile(path, stream);
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        var portable = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.RandomAccess);
        try
        {
            ValidatePortableFile(path);
            return portable;
        }
        catch
        {
            portable.Dispose();
            throw;
        }
    }

    public void DeleteOpenedRecord(string path, FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        EnsureSafeRoot();
        EnsureContained(path);
        if (OperatingSystem.IsWindows())
        {
            ValidateWindowsFile(path, stream);
            WindowsDiagnosticFileIdentity.MarkForDeletion(stream.SafeFileHandle);
        }
        else
        {
            ValidatePortableFile(path);
            File.Delete(path);
        }
    }

    public void Dispose()
    {
        _rootGuard?.Dispose();
        _rootGuard = null;
    }

    [SupportedOSPlatform("windows")]
    private void EnsureSafeWindowsRoot()
    {
        if (!Directory.Exists(_rootPath))
        {
            new DirectoryInfo(_rootPath).Create(CreateDirectorySecurity());
        }

        var attributes = File.GetAttributes(_rootPath);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw UnsafeStore();
        }

        ValidateSecurityDescriptor(
            new DirectoryInfo(_rootPath).GetAccessControl(
                AccessControlSections.Access | AccessControlSections.Owner));

        _rootGuard ??= WindowsDiagnosticFileIdentity.OpenDirectoryGuard(_rootPath);
        var identity = ValidateDirectoryHandle(_rootGuard);
        if (_rootIdentity == default)
        {
            _rootIdentity = identity;
        }
        else if (_rootIdentity != identity)
        {
            throw UnsafeStore();
        }

        using var current = WindowsDiagnosticFileIdentity.OpenDirectoryGuard(_rootPath);
        if (ValidateDirectoryHandle(current) != _rootIdentity)
        {
            throw UnsafeStore();
        }
    }

    [UnsupportedOSPlatform("windows")]
    private void EnsureSafePortableRoot()
    {
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
            File.SetUnixFileMode(
                _rootPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var mode = File.GetUnixFileMode(_rootPath);
        var prohibited = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & prohibited) != 0 || (File.GetAttributes(_rootPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw UnsafeStore();
        }
    }

    [SupportedOSPlatform("windows")]
    private DirectorySecurity CreateDirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(_currentUser!);
        foreach (var identity in _allowedIdentities)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }

    [SupportedOSPlatform("windows")]
    private FileSecurity CreateFileSecurity()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(_currentUser!);
        foreach (var identity in _allowedIdentities)
        {
            security.AddAccessRule(
                new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }

    [SupportedOSPlatform("windows")]
    private WindowsDiagnosticFileIdentity.FileIdentity ValidateDirectoryHandle(SafeFileHandle handle)
    {
        var attributes = WindowsDiagnosticFileIdentity.GetAttributes(handle);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(
                WindowsDiagnosticFileIdentity.GetFinalPath(handle),
                _rootPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw UnsafeStore();
        }

        return WindowsDiagnosticFileIdentity.GetIdentity(handle);
    }

    [SupportedOSPlatform("windows")]
    private void ValidateWindowsFile(string expectedPath, FileStream stream)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedPath));
        var attributes = WindowsDiagnosticFileIdentity.GetAttributes(stream.SafeFileHandle);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            WindowsDiagnosticFileIdentity.GetLinkCount(stream.SafeFileHandle) != 1 ||
            !string.Equals(
                WindowsDiagnosticFileIdentity.GetFinalPath(stream.SafeFileHandle),
                canonical,
                StringComparison.OrdinalIgnoreCase))
        {
            throw UnsafeStore();
        }

        ValidateSecurityDescriptor(stream.GetAccessControl());
    }

    [UnsupportedOSPlatform("windows")]
    private static void ValidatePortableFile(string path)
    {
        var attributes = File.GetAttributes(path);
        var mode = File.GetUnixFileMode(path);
        var prohibited = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            (mode & prohibited) != 0)
        {
            throw UnsafeStore();
        }
    }

    [SupportedOSPlatform("windows")]
    private void ValidateSecurityDescriptor(FileSystemSecurity security)
    {
        if (!security.AreAccessRulesProtected ||
            security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner ||
            !_allowedIdentities.Contains(owner))
        {
            throw UnsafeStore();
        }

        var identitiesWithFullControl = new HashSet<SecurityIdentifier>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: true,
                     typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier identity ||
                rule.IsInherited ||
                rule.AccessControlType != AccessControlType.Allow ||
                !_allowedIdentities.Contains(identity))
            {
                throw UnsafeStore();
            }

            if ((rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            {
                identitiesWithFullControl.Add(identity);
            }
        }

        if (!_allowedIdentities.SetEquals(identitiesWithFullControl))
        {
            throw UnsafeStore();
        }
    }

    private static void ValidateExistingPathComponents(string path)
    {
        var current = File.Exists(path) || Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new DirectoryInfo(path).Parent;
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw UnsafeStore();
            }

            current = current.Parent;
        }
    }

    private void EnsureContained(string path)
    {
        var canonical = Path.GetFullPath(path);
        var prefix = string.Concat(_rootPath, Path.DirectorySeparatorChar);
        if (!canonical.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            canonical.AsSpan(prefix.Length).Contains(Path.DirectorySeparatorChar) ||
            canonical.AsSpan(prefix.Length).Contains(Path.AltDirectorySeparatorChar))
        {
            throw UnsafeStore();
        }
    }

    private static DiagnosticContractException UnsafeStore() =>
        new(
            "diagnostic_store_object_unsafe",
            "The sanitized diagnostic store contains an unsafe object or access policy.");
}

[SupportedOSPlatform("windows")]
internal static class WindowsDiagnosticFileIdentity
{
    private const uint FileReadAttributes = 0x00000080;
    private const uint DeleteAccess = 0x00010000;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    public readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);

    public static SafeFileHandle OpenDirectoryGuard(string path)
    {
        var handle = CreateFile(
            path,
            FileReadAttributes,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("The diagnostic-store directory guard is unavailable.", error);
        }

        return handle;
    }

    public static SafeFileHandle OpenFileForDeletion(string path)
    {
        var handle = CreateFile(
            path,
            GenericRead | DeleteAccess | FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("The diagnostic-store record deletion handle is unavailable.", error);
        }

        return handle;
    }

    public static void MarkForDeletion(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInformation { DeleteFile = true };
        if (SetFileInformationByHandle(
                handle,
                FileInformationClass.FileDispositionInfo,
                ref disposition,
                Marshal.SizeOf<FileDispositionInformation>()) == 0)
        {
            throw new IOException(
                "The diagnostic-store record could not be marked for deletion.",
                Marshal.GetLastWin32Error());
        }
    }

    public static FileAttributes GetAttributes(SafeFileHandle handle) =>
        (FileAttributes)GetInformation(handle).FileAttributes;

    public static FileIdentity GetIdentity(SafeFileHandle handle)
    {
        var information = GetInformation(handle);
        return new FileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    public static uint GetLinkCount(SafeFileHandle handle) => GetInformation(handle).NumberOfLinks;

    public static string GetFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (capacity <= 32768)
        {
            var buffer = new char[capacity];
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, flags: 0);
            if (length == 0)
            {
                throw new IOException("The diagnostic-store object path is unavailable.");
            }

            if (length < buffer.Length)
            {
                var value = new string(buffer, 0, (int)length);
                if (value.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.TrimEndingDirectorySeparator(string.Concat("\\\\", value.AsSpan(8)));
                }

                if (value.StartsWith("\\\\?\\", StringComparison.Ordinal))
                {
                    value = value[4..];
                }

                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            }

            capacity = checked((int)length + 1);
        }

        throw new IOException("The diagnostic-store object path exceeds its bound.");
    }

    private static ByHandleFileInformation GetInformation(SafeFileHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed || GetFileInformationByHandle(handle, out var information) == 0)
        {
            throw new IOException("The diagnostic-store object identity is unavailable.");
        }

        return information;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle fileHandle,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        FileInformationClass fileInformationClass,
        ref FileDispositionInformation fileInformation,
        int bufferSize);

    private enum FileInformationClass
    {
        FileDispositionInfo = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DeleteFile;
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
