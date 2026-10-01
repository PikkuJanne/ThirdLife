using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Inventory.Windows;

// Operation-owned raw XML is the sole raw-file exception. The root, lock, ownership
// record, and report are private to this fixed adapter; none is a report/export input.
internal sealed class BatteryReportWorkspace : IBatteryReportWorkspace
{
    internal const long MinimumFreeBytes = 16 * 1024 * 1024;
    internal const string LockName = ".lock";
    internal const string OwnershipName = "pending.bin";
    internal const int OwnershipRecordBytes = 44;
    private static readonly byte[] OwnershipMagic = "TLBAT001"u8.ToArray();
    private readonly SecurityIdentifier _user;
    private readonly HashSet<SecurityIdentifier> _allowed;
    private SafeFileHandle? _rootGuard;
    private FileStream? _lock;
    private SafeFileHandle? _reportGuard;
    private BatteryReportFileIdentity _rootIdentity;
    private BatteryReportFileIdentity _reportIdentity;
    private BatteryReportFileIdentity _ownershipIdentity;
    private bool _newReportCreated;
    private bool _ownershipCreated;
    private bool _cleaned;

    public BatteryReportWorkspace()
        : this(DefaultRoot(), bindKnownFolderTarget: true)
    {
    }

    internal BatteryReportWorkspace(string rootPath)
        : this(rootPath, bindKnownFolderTarget: false)
    {
    }

    private BatteryReportWorkspace(string rootPath, bool bindKnownFolderTarget)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        DirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        using var identity = WindowsIdentity.GetCurrent();
        _user = identity.User ?? throw new BatteryReportWorkspaceException();
        _allowed = [_user, new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null)];
        ReportFileName = string.Concat("report-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture), ".xml");
        try
        {
            ValidatePathComponents();
            if (!Directory.Exists(DirectoryPath))
            {
                new DirectoryInfo(DirectoryPath).Create(DirectorySecurity());
            }

            _rootGuard = BatteryReportFiles.OpenDirectoryGuard(DirectoryPath);
            if (bindKnownFolderTarget)
            {
                DirectoryPath = BindKnownFolderTarget();
            }
            ValidateRoot();
            _rootIdentity = BatteryReportFiles.Information(_rootGuard).Identity;
            AcquireLock();
            RecoverPreviousOperation();
            if (BatteryReportFiles.AvailableBytes(DirectoryPath) < MinimumFreeBytes)
            {
                throw new BatteryReportWorkspaceException();
            }

            using (var report = CreatePrivateFile(ReportPath, FileShare.ReadWrite))
            {
                _newReportCreated = true;
                _reportIdentity = BatteryReportFiles.Information(report.SafeFileHandle).Identity;
                ValidateFile(ReportPath, report, _reportIdentity);
            }

            _reportGuard = BatteryReportFiles.OpenFileGuard(ReportPath);
            using (var ownership = CreatePrivateFile(OwnershipPath, FileShare.None))
            {
                _ownershipCreated = true;
                _ownershipIdentity = BatteryReportFiles.Information(ownership.SafeFileHandle).Identity;
                using var writer = new BinaryWriter(ownership, Encoding.UTF8, leaveOpen: true);
                writer.Write(OwnershipMagic);
                writer.Write(Guid.ParseExact(ReportFileName.AsSpan(7, 32), "N").ToByteArray());
                writer.Write(_reportIdentity.Volume);
                writer.Write(_reportIdentity.Index);
                writer.Write(DateTimeOffset.UtcNow.UtcTicks);
                writer.Flush();
                ownership.Flush(flushToDisk: true);
            }

            ValidateRoot();
        }
        catch (Exception error)
        {
            var cleanup = _newReportCreated ? Cleanup() : new BatteryReportCleanup(true);
            Dispose();
            if (!cleanup.Complete)
            {
                throw new BatteryReportWorkspaceException(true, cleanup.NativeErrorCode);
            }

            if (error is BatteryReportNativeException native)
            {
                throw new BatteryReportWorkspaceException(false, native.NativeErrorCode);
            }

            throw;
        }
    }

    public string DirectoryPath { get; }
    public string ReportFileName { get; }
    private string ReportPath => Path.Combine(DirectoryPath, ReportFileName);
    private string OwnershipPath => Path.Combine(DirectoryPath, OwnershipName);

    public long ReportLength
    {
        get
        {
            if (_reportGuard is null)
            {
                throw new BatteryReportWorkspaceException(true);
            }

            var information = BatteryReportFiles.Information(_reportGuard);
            ValidateInformation(information, _reportIdentity, directory: false);
            return checked((long)information.Length);
        }
    }

    public Stream OpenReport()
    {
        ValidateRoot();
        var stream = new FileStream(ReportPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.SequentialScan);
        try
        {
            ValidateFile(ReportPath, stream, _reportIdentity);
            if (stream.Length is <= 0 or > BatteryReportParser.MaximumReportBytes)
            {
                throw new BatteryReportWorkspaceException();
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public BatteryReportCleanup Cleanup()
    {
        if (_cleaned)
        {
            return new(true);
        }

        try
        {
            _reportGuard?.Dispose();
            _reportGuard = null;
            ValidateRoot();
            if (_newReportCreated)
            {
                DeleteKnownFile(ReportPath, _reportIdentity);
            }

            if (_ownershipCreated)
            {
                DeleteKnownFile(OwnershipPath, _ownershipIdentity);
            }

            if (_ownershipCreated && !BatteryReportFiles.IsMissing(OwnershipPath))
            {
                throw new BatteryReportWorkspaceException(true);
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(DirectoryPath))
            {
                if (Path.GetFileName(path) != LockName)
                {
                    throw new BatteryReportWorkspaceException(true);
                }
            }

            _cleaned = true;
            return new(true);
        }
        catch (BatteryReportNativeException error)
        {
            return new(false, error.NativeErrorCode);
        }
        catch (Exception)
        {
            return new(false);
        }
    }

    public void Dispose()
    {
        _reportGuard?.Dispose();
        _reportGuard = null;
        _lock?.Dispose();
        _lock = null;
        _rootGuard?.Dispose();
        _rootGuard = null;
    }

    internal static bool IsReportFileName(string name) => name.Length == 43 &&
        name.StartsWith("report-", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal) &&
        name.AsSpan(7, 32).ToString().All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string DefaultRoot() => FromKnownFolderBase(BatteryReportFiles.LocalApplicationData());

    internal static string FromKnownFolderBase(string? local)
    {
        if (string.IsNullOrWhiteSpace(local) || !WindowsStorageVolumeSource.IsLocalAbsolutePath(local))
        {
            throw new BatteryReportWorkspaceException();
        }

        return Path.Combine(local, "ThirdLife", "SetupCore", "BatteryReports");
    }

    private string BindKnownFolderTarget()
    {
        if (_rootGuard is null)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        // Filesystem redirection can be inherited by an unpackaged child while the
        // known-folder API still returns its logical path. Only this default root may
        // bind once to the pinned handle's actual location, before any files are opened.
        var pinned = BatteryReportFiles.Information(_rootGuard);
        ValidateInformation(pinned, expected: null, directory: true);
        var canonical = BatteryReportFiles.FinalPath(_rootGuard);
        if (!HasRegisteredSuffix(DirectoryPath) || !HasRegisteredSuffix(canonical))
        {
            throw new BatteryReportWorkspaceException(true);
        }
        ValidatePathComponents(DirectoryPath);
        ValidatePathComponents(canonical);
        using var aliasHandle = BatteryReportFiles.OpenDirectoryGuard(DirectoryPath);
        using var canonicalHandle = BatteryReportFiles.OpenDirectoryGuard(canonical);
        var alias = BatteryReportFiles.Information(aliasHandle);
        var actual = BatteryReportFiles.Information(canonicalHandle);
        ValidateInformation(alias, pinned.Identity, directory: true);
        ValidateInformation(actual, pinned.Identity, directory: true);
        ValidateKnownFolderBinding(DirectoryPath, canonical, pinned.Identity, alias.Identity, actual.Identity);
        if (!string.Equals(BatteryReportFiles.FinalPath(aliasHandle), canonical, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(BatteryReportFiles.FinalPath(canonicalHandle), canonical, StringComparison.OrdinalIgnoreCase))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        ValidateSecurity(new DirectoryInfo(DirectoryPath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
        ValidateSecurity(new DirectoryInfo(canonical).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
        return canonical;
    }

    internal static void ValidateKnownFolderBinding(string requested, string canonical,
        BatteryReportFileIdentity pinnedIdentity, BatteryReportFileIdentity aliasIdentity,
        BatteryReportFileIdentity canonicalIdentity)
    {
        if (!HasRegisteredSuffix(requested) || !HasRegisteredSuffix(canonical) ||
            pinnedIdentity.Index == 0 || pinnedIdentity != aliasIdentity || pinnedIdentity != canonicalIdentity)
        {
            throw new BatteryReportWorkspaceException(true);
        }
    }

    private static bool HasRegisteredSuffix(string path)
    {
        if (!WindowsStorageVolumeSource.IsLocalAbsolutePath(path) || path.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        var components = path.Split(Path.DirectorySeparatorChar);
        return components.Length >= 4 && !components.Contains(".", StringComparer.Ordinal) &&
            !components.Contains("..", StringComparer.Ordinal) &&
            components[^3].Equals("ThirdLife", StringComparison.OrdinalIgnoreCase) &&
            components[^2].Equals("SetupCore", StringComparison.OrdinalIgnoreCase) &&
            components[^1].Equals("BatteryReports", StringComparison.OrdinalIgnoreCase);
    }

    private void AcquireLock()
    {
        var path = Path.Combine(DirectoryPath, LockName);
        if (BatteryReportFiles.IsMissing(path))
        {
            try
            {
                using var created = CreatePrivateFile(path, FileShare.None);
            }
            catch (IOException) when (!BatteryReportFiles.IsMissing(path))
            {
                // A competing instance may have created the one fixed lock first.
            }
        }

        try
        {
            _lock = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
        }
        catch (IOException error) when (error.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
        {
            throw new BatteryReportWorkspaceException(false, unchecked((uint)error.HResult) & 0xffff);
        }
        ValidateFile(path, _lock);
        if (_lock.Length != 0)
        {
            throw new BatteryReportWorkspaceException(true);
        }
    }

    private void RecoverPreviousOperation()
    {
        var entries = new List<string>(3);
        foreach (var path in Directory.EnumerateFileSystemEntries(DirectoryPath))
        {
            if (entries.Count == 3)
            {
                throw new BatteryReportWorkspaceException(true);
            }

            entries.Add(Path.GetFileName(path));
        }

        if (!entries.Contains(OwnershipName, StringComparer.Ordinal))
        {
            if (entries.Count != 1 || entries[0] != LockName)
            {
                // An unregistered object is preserved. No capture follows uncertain cleanup.
                throw new BatteryReportWorkspaceException(true);
            }

            return;
        }

        try
        {
            using (var ownership = OpenForDeletion(OwnershipPath))
            {
                ValidateFile(OwnershipPath, ownership);
                var record = ReadOwnership(ownership);
                if (entries.Any(name => name != LockName && name != OwnershipName && name != record.ReportName))
                {
                    throw new BatteryReportWorkspaceException(true);
                }

                DeleteKnownFile(Path.Combine(DirectoryPath, record.ReportName), record.Identity);
                BatteryReportFiles.MarkForDeletion(ownership.SafeFileHandle);
            }

            if (!BatteryReportFiles.IsMissing(OwnershipPath))
            {
                throw new BatteryReportWorkspaceException(true);
            }
        }
        catch (BatteryReportNativeException error)
        {
            throw new BatteryReportWorkspaceException(true, error.NativeErrorCode);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new BatteryReportWorkspaceException(true);
        }
    }

    private static OwnershipRecord ReadOwnership(FileStream stream)
    {
        if (stream.Length != OwnershipRecordBytes)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(OwnershipMagic))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        var id = new Guid(reader.ReadBytes(16));
        var identity = new BatteryReportFileIdentity(reader.ReadUInt32(), reader.ReadUInt64());
        var createdTicks = reader.ReadInt64();
        if (id == Guid.Empty || identity == default || createdTicks <= 0 || createdTicks > DateTimeOffset.UtcNow.UtcTicks)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        return new(string.Concat("report-", id.ToString("N", CultureInfo.InvariantCulture), ".xml"), identity);
    }

    private void DeleteKnownFile(string path, BatteryReportFileIdentity identity)
    {
        ValidateRoot();
        if (BatteryReportFiles.IsMissing(path))
        {
            return;
        }

        using (var file = OpenForDeletion(path))
        {
            ValidateFile(path, file, identity);
            BatteryReportFiles.MarkForDeletion(file.SafeFileHandle);
        }

        if (!BatteryReportFiles.IsMissing(path))
        {
            throw new BatteryReportWorkspaceException(true);
        }
    }

    private static FileStream OpenForDeletion(string path) =>
        new(BatteryReportFiles.OpenForDeletion(path), FileAccess.Read, bufferSize: 4096, isAsync: false);

    private FileStream CreatePrivateFile(string path, FileShare share) => new FileInfo(path).Create(
        FileMode.CreateNew, FileSystemRights.FullControl, share, 4096, FileOptions.WriteThrough, FileSecurity());

    private void ValidateRoot()
    {
        if (_rootGuard is null)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        ValidatePathComponents();
        ValidateInformation(BatteryReportFiles.Information(_rootGuard),
            _rootIdentity == default ? null : _rootIdentity, directory: true);
        if (!string.Equals(BatteryReportFiles.FinalPath(_rootGuard), DirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        using var current = BatteryReportFiles.OpenDirectoryGuard(DirectoryPath);
        if (BatteryReportFiles.Information(current).Identity != BatteryReportFiles.Information(_rootGuard).Identity)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        ValidateSecurity(new DirectoryInfo(DirectoryPath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
    }

    private void ValidateFile(string path, FileStream stream, BatteryReportFileIdentity? expected = null)
    {
        ValidateInformation(BatteryReportFiles.Information(stream.SafeFileHandle), expected, directory: false);
        if (!string.Equals(BatteryReportFiles.FinalPath(stream.SafeFileHandle), path, StringComparison.OrdinalIgnoreCase))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        ValidateSecurity(stream.GetAccessControl());
    }

    private static void ValidateInformation(BatteryReportFileInformation information,
        BatteryReportFileIdentity? expected, bool directory)
    {
        if ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
            ((information.Attributes & FileAttributes.Directory) != 0) != directory ||
            (!directory && information.Links != 1) || (expected is not null && information.Identity != expected))
        {
            throw new BatteryReportWorkspaceException(true);
        }
    }

    private void ValidatePathComponents() => ValidatePathComponents(DirectoryPath);

    private static void ValidatePathComponents(string path)
    {
        if (!WindowsStorageVolumeSource.IsLocalAbsolutePath(path))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || !BatteryReportFiles.IsFixedDrive(root))
        {
            throw new BatteryReportWorkspaceException(true);
        }

        // Examine ancestors from the local volume outward. Probing a descendant first
        // could follow a previously unvalidated junction to a remote filesystem.
        var current = root;
        foreach (var component in path[root.Length..].Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
            {
                throw new BatteryReportWorkspaceException(true);
            }
        }
    }

    private DirectorySecurity DirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(_user);
        foreach (var identity in _allowed)
        {
            security.AddAccessRule(new(identity, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }

    private FileSecurity FileSecurity()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(_user);
        foreach (var identity in _allowed)
        {
            security.AddAccessRule(new(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }

    private void ValidateSecurity(FileSystemSecurity security)
    {
        if (!security.AreAccessRulesProtected || security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner ||
            owner != _user)
        {
            throw new BatteryReportWorkspaceException(true);
        }

        var fullControl = new HashSet<SecurityIdentifier>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier identity || rule.IsInherited ||
                rule.AccessControlType != AccessControlType.Allow || !_allowed.Contains(identity) ||
                (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl)
            {
                throw new BatteryReportWorkspaceException(true);
            }

            fullControl.Add(identity);
        }

        if (!_allowed.SetEquals(fullControl))
        {
            throw new BatteryReportWorkspaceException(true);
        }
    }

    private sealed record OwnershipRecord(string ReportName, BatteryReportFileIdentity Identity);
}
