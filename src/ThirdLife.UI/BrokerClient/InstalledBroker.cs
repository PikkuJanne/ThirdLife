using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.UI.BrokerClient;

/// <summary>Holds the installed image and its path stable through the Windows consent request.</summary>
internal sealed class InstalledBroker : IDisposable
{
    private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const int DigestLength = 64;
    private const int MaximumImageBytes = 32 * 1024 * 1024;
    private const int PathBufferLength = 512;
    private const string DosPrefix = @"\\?\";
    private const int GenericWrite = 0x40000000;
    private const int GenericAll = 0x10000000;
    private const int ModifyObject = (int)(FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership);
    private readonly List<SafeFileHandle> _guards;

    private InstalledBroker(string executable, List<SafeFileHandle> guards)
    {
        Executable = executable;
        _guards = guards;
    }

    public string Executable { get; }

    public static InstalledBroker? TryOpen() => TryOpen(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.SystemDirectory);

    internal static InstalledBroker? TryOpen(string programFiles, string systemDirectory)
    {
        if (!IsDosPath(programFiles) || !IsDosPath(systemDirectory))
        {
            return null;
        }

        var root = Path.GetPathRoot(programFiles)!;
        // Windows protects its boot drive mapping from ordinary-user redefinition.
        if (!root.Equals(Path.GetPathRoot(systemDirectory), StringComparison.OrdinalIgnoreCase) ||
            new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            return null;
        }

        var directory = Path.Combine(programFiles, "ThirdLife", "Broker");
        var ancestors = new Stack<string>();
        for (string? ancestor = directory; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
        {
            ancestors.Push(ancestor);
        }

        var guards = new List<SafeFileHandle>(ancestors.Count + 2);
        InstalledBroker? installation = null;
        try
        {
            while (ancestors.TryPop(out var ancestor))
            {
                var boundary = ancestor.Length > programFiles.Length ? ObjectKind.Installation :
                    ancestor == root ? ObjectKind.VolumeRoot : ObjectKind.SystemAncestor;
                if (!OpenGuard(ancestor, directory: true, boundary, guards, out _))
                {
                    return null;
                }
            }

            var metadata = Path.Combine(directory, "broker.sha256");
            var executable = Path.Combine(directory, "ThirdLife.Broker.exe");
            var entries = Directory.EnumerateFileSystemEntries(directory).Take(3).ToArray();
            if (entries.Length != 2 || !entries.Contains(metadata, StringComparer.OrdinalIgnoreCase) ||
                !entries.Contains(executable, StringComparer.OrdinalIgnoreCase) ||
                !OpenGuard(metadata, directory: false, ObjectKind.Installation, guards, out _) ||
                !OpenGuard(executable, directory: false, ObjectKind.Installation, guards, out var canonicalExecutable))
            {
                return null;
            }

            // The borrowing wrappers leave the original guards open through ShellExecuteEx.
            using var manifestStream = ReadGuardedFile(guards[^2]);
            using var executableStream = ReadGuardedFile(guards[^1]);
            if (MatchesArtifact(executableStream, manifestStream) && HasNativeStartupPolicy(executableStream))
            {
                installation = new InstalledBroker(canonicalExecutable, guards);
            }

            return installation;
        }
        finally
        {
            if (installation is null) { CloseGuards(guards); }
        }
    }

    public void Dispose() => CloseGuards(_guards);

    internal static bool HasTrustedAccess(RawSecurityDescriptor descriptor, ObjectKind kind)
    {
        if (descriptor.Owner is not { } owner || !IsTrustedPrincipal(owner, kind) ||
            descriptor.DiscretionaryAcl is not { } acl ||
            (descriptor.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0 ||
            (kind == ObjectKind.Installation && (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0))
        {
            return false;
        }

        var prohibited = ModifyObject;
        if (kind == ObjectKind.VolumeRoot)
        {
            // The real volume root can permit creation of unrelated children.
            // Replacing an existing named child requires delete rights, still forbidden here.
            prohibited &= ~(int)FileSystemRights.AppendData;
        }

        foreach (GenericAce ace in acl)
        {
            if (ace is not CommonAce { IsCallback: false } rule)
            {
                return false;
            }

            if ((rule.AceFlags & AceFlags.InheritOnly) == 0 &&
                rule.AceQualifier == AceQualifier.AccessAllowed &&
                (rule.AccessMask & (prohibited | GenericWrite | GenericAll)) != 0 &&
                !IsTrustedPrincipal(rule.SecurityIdentifier, kind))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool MatchesArtifact(Stream executable, Stream metadata)
    {
        if (executable.Length is <= 0 or > MaximumImageBytes || metadata.Length != DigestLength)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[DigestLength];
        metadata.ReadExactly(expected);
        foreach (var value in expected)
        {
            if (value is not (>= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f'))
            {
                return false;
            }
        }

        executable.Position = 0;
        var digest = Convert.ToHexStringLower(SHA256.HashData(executable));
        return digest.Equals(System.Text.Encoding.ASCII.GetString(expected), StringComparison.Ordinal);
    }

    internal static bool HasNativeStartupPolicy(Stream executable)
    {
        // IMAGE_LOAD_CONFIG_DIRECTORY64.DependentLoadFlags is at byte offset 78.
        const int loadFlagsOffset = 78;
        const int minimumLoadConfigSize = loadFlagsOffset + sizeof(ushort);
        const ushort searchSystem32 = 0x0800;
        if (executable.Length is <= 0 or > MaximumImageBytes) { return false; }
        try
        {
            executable.Position = 0;
            using var image = new PEReader(executable, PEStreamOptions.LeaveOpen);
            var headers = image.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64 ||
                (headers.CoffHeader.Characteristics & (Characteristics.ExecutableImage | Characteristics.Dll)) !=
                    Characteristics.ExecutableImage ||
                headers.PEHeader is not { Magic: PEMagic.PE32Plus } pe ||
                pe.CorHeaderTableDirectory.RelativeVirtualAddress != 0 || pe.CorHeaderTableDirectory.Size != 0 ||
                pe.LoadConfigTableDirectory is not { RelativeVirtualAddress: > 0, Size: >= minimumLoadConfigSize } configuration)
            {
                return false;
            }

            var section = image.GetSectionData(configuration.RelativeVirtualAddress);
            if (section.Length < configuration.Size) { return false; }
            var reader = section.GetReader(0, minimumLoadConfigSize);
            var configuredSize = reader.ReadUInt32();
            if (configuredSize < minimumLoadConfigSize || configuredSize > configuration.Size) { return false; }
            reader.Offset = loadFlagsOffset;
            return reader.ReadUInt16() == searchSystem32;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static bool IsTrustedPrincipal(SecurityIdentifier sid, ObjectKind kind) =>
        sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
        (kind != ObjectKind.Installation && sid.Value.Equals(TrustedInstallerSid, StringComparison.Ordinal));

    private static bool IsDosPath(string path) => path.Length is >= 3 and < PathBufferLength &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
        path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    private static bool OpenGuard(string path, bool directory, ObjectKind kind, List<SafeFileHandle> guards, out string canonical)
    {
        const uint readControl = 0x00020000;
        const uint readAttributes = 0x00000080;
        const uint genericRead = 0x80000000;
        const uint backupSemantics = 0x02000000;
        const uint openReparsePoint = 0x00200000;
        const uint openExisting = 3;
        canonical = string.Empty;
        var access = readControl | (directory ? readAttributes : genericRead);
        var share = directory && kind != ObjectKind.Installation ? FileShare.ReadWrite : FileShare.Read;
        var flags = openReparsePoint | (directory ? backupSemantics : 0);
        var handle = NativeMethods.CreateFile(path, access, share, IntPtr.Zero, openExisting, flags, IntPtr.Zero);
        // Store ownership immediately, including failed handles, so all exits are covered.
        guards.Add(handle);
        if (handle.IsInvalid || !NativeMethods.GetFileInformationByHandle(handle, out var information))
        {
            return false;
        }

        var expectedAttributes = directory ? FileAttributes.Directory : 0;
        if ((information.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != expectedAttributes ||
            (!directory && information.NumberOfLinks != 1))
        {
            return false;
        }

        var buffer = new char[PathBufferLength];
        var length = NativeMethods.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        if (length <= DosPrefix.Length || length >= buffer.Length ||
            !buffer.AsSpan(0, DosPrefix.Length).SequenceEqual(DosPrefix))
        {
            return false;
        }

        canonical = new string(buffer, DosPrefix.Length, (int)length - DosPrefix.Length);
        return canonical.Equals(path, StringComparison.OrdinalIgnoreCase) &&
            ReadDescriptor(handle) is { } descriptor && HasTrustedAccess(descriptor, kind);
    }

    private static RawSecurityDescriptor? ReadDescriptor(SafeFileHandle handle)
    {
        const uint fileObject = 1;
        const uint ownerAndDacl = 0x00000005;
        var error = NativeMethods.GetSecurityInfo(handle, fileObject, ownerAndDacl, out _, out _, out _, out _, out var pointer);
        if (error != 0 || pointer == IntPtr.Zero) { return null; }
        try
        {
            var length = NativeMethods.GetSecurityDescriptorLength(pointer);
            if (length is < 20 or > 65_536) { return null; }
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        finally
        {
            _ = NativeMethods.LocalFree(pointer);
        }
    }

    private static FileStream ReadGuardedFile(SafeFileHandle handle) =>
        new(new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false), FileAccess.Read);

    private static void CloseGuards(List<SafeFileHandle> guards)
    {
        for (var index = guards.Count - 1; index >= 0; index--) { guards[index].Dispose(); }
    }

    internal enum ObjectKind
    {
        Installation,
        SystemAncestor,
        VolumeRoot,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccess;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWrite;
        public uint VolumeSerial;
        public uint SizeHigh;
        public uint SizeLow;
        public uint NumberOfLinks;
        public uint IndexHigh;
        public uint IndexLow;
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string path, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, [Out] char[] path, uint size, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true)]
        internal static extern uint GetSecurityInfo(SafeFileHandle handle, uint type, uint information,
            out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true)]
        internal static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
