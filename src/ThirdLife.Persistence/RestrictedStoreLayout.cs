using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

[SupportedOSPlatform("windows")]
internal sealed class GuardedStoreFile : IDisposable
{
    public GuardedStoreFile(
        FileStream stream,
        bool createdNew,
        WindowsFileIdentity.FileIdentity identity,
        string finalPath)
    {
        Stream = stream;
        CreatedNew = createdNew;
        Identity = identity;
        FinalPath = finalPath;
    }

    public bool CreatedNew { get; }

    public string FinalPath { get; }

    public WindowsFileIdentity.FileIdentity Identity { get; }

    public FileStream Stream { get; }

    public void Dispose() => Stream.Dispose();
}

internal readonly record struct RegisteredStoreState(bool DatabaseExists, bool JournalExists);

internal sealed record InitializationStoreFiles(
    string DatabasePath,
    string JournalPath,
    GuardedStoreFile DatabaseGuard,
    GuardedStoreFile JournalGuard);

[SupportedOSPlatform("windows")]
internal sealed class RestrictedStoreLayout : IDisposable
{
    private const string DatabaseFileName = "thirdlife-jobs.sqlite3";
    internal const string InitializationFilePrefix = ".thirdlife-jobs.sqlite3-initialize-";
    private const string InitializationFileSuffix = ".tmp";
    private const int InitializationTokenLength = 32;
    private const int MaximumInitializationArtifacts = 64;
    private const string JobsDirectoryName = "jobs";
    private const string JournalSuffix = "-journal";
    private static readonly SearchValues<char> LowerHexCharacters = SearchValues.Create("0123456789abcdef");

    private readonly SecurityIdentifier _currentUser;
    private readonly HashSet<SecurityIdentifier> _allowedIdentities;
    private SafeFileHandle? _jobsGuard;
    private SafeFileHandle? _rootGuard;
    private WindowsFileIdentity.FileIdentity _jobsIdentity;
    private WindowsFileIdentity.FileIdentity _rootIdentity;
    private bool _disposed;

    private RestrictedStoreLayout(string rootPath, SecurityIdentifier currentUser)
    {
        RootPath = rootPath;
        JobsPath = Path.Combine(rootPath, JobsDirectoryName);
        DatabasePath = Path.Combine(rootPath, DatabaseFileName);
        JournalPath = string.Concat(DatabasePath, JournalSuffix);
        _currentUser = currentUser;
        _allowedIdentities =
        [
            currentUser,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
        ];
    }

    public string DatabasePath { get; }

    public string JobsPath { get; }

    public string JournalPath { get; }

    public string RootPath { get; }

    public static RestrictedStoreLayout CreateOrOpen(string rootPath)
    {
        var canonicalRoot = CanonicalizeRoot(rootPath);
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new JobStorePathException();
        var layout = new RestrictedStoreLayout(canonicalRoot, currentUser);

        try
        {
            ValidateExistingPathComponents(canonicalRoot);
            layout.CreateOrValidateDirectory(canonicalRoot);
            layout.CreateOrValidateDirectory(layout.JobsPath);
            layout._rootGuard = WindowsFileIdentity.OpenDirectoryGuard(canonicalRoot);
            layout._rootIdentity = ValidateDirectoryHandle(canonicalRoot, layout._rootGuard);
            layout._jobsGuard = WindowsFileIdentity.OpenDirectoryGuard(layout.JobsPath);
            layout._jobsIdentity = ValidateDirectoryHandle(layout.JobsPath, layout._jobsGuard);
            return layout;
        }
        catch (JobStoreException)
        {
            layout.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            layout.Dispose();
            throw new JobStorePathException();
        }
    }

    public RegisteredStoreState InspectRegisteredStore()
    {
        ThrowIfDisposed();

        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            ValidateDirectoryGuard(JobsPath, _jobsGuard, _jobsIdentity);
            RejectUnexpectedJournalModes();
            var databaseExists = InspectOptionalRegularFile(DatabasePath);
            var journalExists = InspectOptionalRegularFile(JournalPath);
            return new RegisteredStoreState(databaseExists, journalExists);
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public GuardedStoreFile OpenDatabaseGuard()
    {
        ThrowIfDisposed();
        RejectUnexpectedJournalModes();
        return OpenFileGuard(DatabasePath, allowDeleteShare: false);
    }

    public GuardedStoreFile OpenExistingDatabaseGuard()
    {
        ThrowIfDisposed();
        RejectUnexpectedJournalModes();
        return OpenExistingFileGuard(DatabasePath, allowDeleteShare: false);
    }

    public GuardedStoreFile OpenJournalGuard(bool allowDeleteShare = false)
    {
        ThrowIfDisposed();
        RejectUnexpectedJournalModes();
        return OpenFileGuard(JournalPath, allowDeleteShare);
    }

    public GuardedStoreFile OpenExistingJournalGuard(bool allowDeleteShare = false)
    {
        ThrowIfDisposed();
        RejectUnexpectedJournalModes();
        return OpenExistingFileGuard(JournalPath, allowDeleteShare);
    }

    public InitializationStoreFiles CreateInitializationStoreFiles()
    {
        ThrowIfDisposed();

        GuardedStoreFile? databaseGuard = null;
        GuardedStoreFile? journalGuard = null;
        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(InitializationTokenLength / 2));
            var databasePath = Path.Combine(
                RootPath,
                string.Concat(InitializationFilePrefix, token, InitializationFileSuffix));
            var journalPath = string.Concat(databasePath, JournalSuffix);
            EnsureContained(RootPath, databasePath);
            EnsureContained(RootPath, journalPath);

            databaseGuard = CreateNewOwnedFileGuard(databasePath, allowDeleteShare: true);
            journalGuard = CreateNewOwnedFileGuard(journalPath, allowDeleteShare: false);
            ValidateInitializationStoreGuards(
                databasePath,
                journalPath,
                databaseGuard,
                journalGuard);

            var files = new InitializationStoreFiles(
                databasePath,
                journalPath,
                databaseGuard,
                journalGuard);
            databaseGuard = null;
            journalGuard = null;
            return files;
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
        finally
        {
            journalGuard?.Dispose();
            databaseGuard?.Dispose();
        }
    }

    public void ValidateInitializationStoreGuards(
        string databasePath,
        string journalPath,
        GuardedStoreFile databaseGuard,
        GuardedStoreFile journalGuard)
    {
        ArgumentNullException.ThrowIfNull(databaseGuard);
        ArgumentNullException.ThrowIfNull(journalGuard);
        ThrowIfDisposed();

        try
        {
            ValidateInitializationPaths(databasePath, journalPath);
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            ValidateFileGuard(databasePath, databaseGuard);
            ValidateFileGuard(journalPath, journalGuard);
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public GuardedStoreFile OpenInitializationJournalGuard(string databasePath, string journalPath)
    {
        ThrowIfDisposed();
        ValidateInitializationPaths(databasePath, journalPath);
        return OpenFileGuard(journalPath, allowDeleteShare: false);
    }

    public void ReconcileInitializationArtifacts(long maximumArtifactBytes)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumArtifactBytes, 1);

        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            var artifacts = new List<string>();
            var observedCandidates = 0;
            foreach (var path in Directory.EnumerateFileSystemEntries(
                         RootPath,
                         string.Concat(InitializationFilePrefix, "*"),
                         SearchOption.TopDirectoryOnly))
            {
                observedCandidates++;
                if (observedCandidates > MaximumInitializationArtifacts)
                {
                    throw new JobStoreCorruptionException("store_record_limit_exceeded");
                }

                var fileName = Path.GetFileName(path);
                if (!IsInitializationDatabaseName(fileName) && !IsInitializationJournalName(fileName))
                {
                    continue;
                }

                artifacts.Add(path);
            }

            foreach (var path in artifacts.OrderByDescending(
                         item => IsInitializationJournalName(Path.GetFileName(item))))
            {
                _ = TryDeleteInitializationArtifact(path, maximumArtifactBytes);
            }
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public bool TryDeleteInitializationArtifact(string path, long maximumArtifactBytes)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumArtifactBytes, 1);

        ValidateInitializationArtifactPath(path);
        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            ValidateExistingPathComponents(path);
            WindowsFileIdentity.FileIdentity expectedIdentity;
            using (var probe = WindowsFileIdentity.TryOpenPathEntry(path))
            {
                if (probe is null)
                {
                    return true;
                }

                var attributes = WindowsFileIdentity.GetAttributes(probe);
                if ((attributes & FileAttributes.Directory) != 0 ||
                    (attributes & FileAttributes.ReparsePoint) != 0 ||
                    WindowsFileIdentity.GetLinkCount(probe) != 1 ||
                    !string.Equals(WindowsFileIdentity.GetFinalPath(probe), path, StringComparison.OrdinalIgnoreCase))
                {
                    throw new JobStorePathException();
                }

                expectedIdentity = WindowsFileIdentity.GetIdentity(probe);
            }

            using var guard = OpenFileDeleteGuard(path);
            if (guard.Identity != expectedIdentity)
            {
                throw new JobStorePathException();
            }
            if (guard.Stream.Length > maximumArtifactBytes)
            {
                throw new JobStoreCorruptionException("store_size_limit_exceeded");
            }

            WindowsFileIdentity.DeleteFile(guard.Stream.SafeFileHandle);
            return true;
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            return false;
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public bool TryPublishInitializationDatabase(
        string initializationDatabasePath,
        GuardedStoreFile initializedDatabaseGuard)
    {
        ArgumentNullException.ThrowIfNull(initializedDatabaseGuard);
        ThrowIfDisposed();
        ValidateInitializationArtifactPath(initializationDatabasePath, allowJournal: false);

        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            ValidateFileGuard(initializationDatabasePath, initializedDatabaseGuard);
            var registeredState = InspectRegisteredStore();
            if (!registeredState.DatabaseExists && registeredState.JournalExists)
            {
                throw new JobStoreCorruptionException("store_identity_mismatch");
            }
            if (registeredState.DatabaseExists)
            {
                return false;
            }

            var initializationJournalPath = string.Concat(initializationDatabasePath, JournalSuffix);
            if (InspectOptionalRegularFile(initializationJournalPath))
            {
                throw new JobStorePathException();
            }

            using var guard = OpenFileRenameGuard(initializationDatabasePath);
            if (guard.Identity != initializedDatabaseGuard.Identity)
            {
                throw new JobStorePathException();
            }
            if (!WindowsFileIdentity.TryRenameFile(guard.Stream.SafeFileHandle, DatabasePath))
            {
                var winnerState = InspectRegisteredStore();
                if (winnerState.DatabaseExists)
                {
                    return false;
                }

                throw new JobStorePathException();
            }

            ValidateMovedFileGuard(DatabasePath, guard);
            return true;
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public void ValidateStoreGuards(GuardedStoreFile databaseGuard, GuardedStoreFile journalGuard)
    {
        ArgumentNullException.ThrowIfNull(databaseGuard);
        ArgumentNullException.ThrowIfNull(journalGuard);
        ThrowIfDisposed();

        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            ValidateDirectoryGuard(JobsPath, _jobsGuard, _jobsIdentity);
            ValidateFileGuard(DatabasePath, databaseGuard);
            ValidateFileGuard(JournalPath, journalGuard);
            RejectUnexpectedJournalModes();
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public void ValidateDatabaseGuard(GuardedStoreFile databaseGuard)
    {
        ArgumentNullException.ThrowIfNull(databaseGuard);
        ThrowIfDisposed();

        try
        {
            ValidateDirectoryGuard(RootPath, _rootGuard, _rootIdentity);
            ValidateDirectoryGuard(JobsPath, _jobsGuard, _jobsIdentity);
            ValidateFileGuard(DatabasePath, databaseGuard);
            RejectUnexpectedJournalModes();
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public string EnsureJobDirectory(JobId jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ThrowIfDisposed();
        try
        {
            ValidateDirectoryGuard(JobsPath, _jobsGuard, _jobsIdentity);
            var candidate = GetJobDirectoryPath(jobId);
            EnsureContained(JobsPath, candidate);
            ValidateExistingPathComponents(candidate);
            CreateOrValidateDirectory(candidate);
            return candidate;
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public void ValidateJobDirectory(JobId jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ThrowIfDisposed();
        try
        {
            ValidateDirectoryGuard(JobsPath, _jobsGuard, _jobsIdentity);
            var path = GetJobDirectoryPath(jobId);
            ValidateExistingPathComponents(path);
            if (!Directory.Exists(path) || File.Exists(path))
            {
                throw new JobStorePathException();
            }

            using var guard = WindowsFileIdentity.OpenDirectoryGuard(path);
            _ = ValidateDirectoryHandle(path, guard);
            ValidateDirectorySecurity(new DirectoryInfo(path));
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    public void ReconcileJobDirectories(
        IReadOnlyCollection<JobId> jobIds,
        int maximumDirectories)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        ThrowIfDisposed();
        if (maximumDirectories < 1 || jobIds.Count > maximumDirectories)
        {
            throw new JobStoreCorruptionException("store_record_limit_exceeded");
        }

        try
        {
            ReconcileJobDirectoriesCore(jobIds, maximumDirectories);
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SystemException)
        {
            throw new JobStorePathException();
        }
    }

    private void ReconcileJobDirectoriesCore(
        IReadOnlyCollection<JobId> jobIds,
        int maximumDirectories)
    {
        ValidateDirectoryGuard(JobsPath, _jobsGuard, _jobsIdentity);

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var jobId in jobIds)
        {
            var path = GetJobDirectoryPath(jobId);
            if (!expected.Add(path))
            {
                throw new JobStoreCorruptionException("store_job_path_collision");
            }

            ValidateExistingPathComponents(path);
            CreateOrValidateDirectory(path);
        }

        var observedDirectoryCount = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(JobsPath))
        {
            observedDirectoryCount++;
            if (observedDirectoryCount > maximumDirectories)
            {
                throw new JobStoreCorruptionException("store_record_limit_exceeded");
            }

            if (expected.Contains(path))
            {
                continue;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0 ||
                !IsDerivedJobDirectoryName(Path.GetFileName(path)))
            {
                throw new JobStorePathException();
            }

            try
            {
                using var guard = WindowsFileIdentity.OpenDirectoryDeleteGuard(path);
                _ = ValidateDirectoryHandle(path, guard);
                ValidateDirectorySecurity(new DirectoryInfo(path));
                if (Directory.EnumerateFileSystemEntries(path).Any())
                {
                    throw new JobStorePathException();
                }

                WindowsFileIdentity.DeleteDirectory(guard);
            }
            catch (JobStoreException)
            {
                throw;
            }
            catch (Exception exception) when (exception is SystemException)
            {
                throw new JobStorePathException();
            }
        }
    }

    internal string GetJobDirectoryPath(JobId jobId)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        var name = string.Concat(
            "j-",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId.Value))));
        return Path.Combine(JobsPath, name);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _jobsGuard?.Dispose();
        _rootGuard?.Dispose();
    }

    private GuardedStoreFile CreateNewOwnedFileGuard(string path, bool allowDeleteShare)
    {
        ValidateExistingPathComponents(path);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new JobStorePathException();
        }

        WindowsFileIdentity.FileIdentity identity;
        using (var createdStream = new FileInfo(path).Create(
                   FileMode.CreateNew,
                   FileSystemRights.FullControl,
                   FileShare.ReadWrite,
                   bufferSize: 4_096,
                   FileOptions.RandomAccess,
                   CreateFileSecurity()))
        {
            ValidateFile(path, createdStream);
            identity = WindowsFileIdentity.GetIdentity(createdStream.SafeFileHandle);
        }

        var guard = OpenExistingFileGuard(path, allowDeleteShare);
        if (guard.Identity != identity)
        {
            guard.Dispose();
            throw new JobStorePathException();
        }

        return guard;
    }

    private GuardedStoreFile OpenExistingFileGuard(string path, bool allowDeleteShare)
    {
        try
        {
            ValidateExistingPathComponents(path);
            if (!File.Exists(path) || Directory.Exists(path))
            {
                throw new JobStorePathException();
            }

            var fileShare = allowDeleteShare
                ? FileShare.ReadWrite | FileShare.Delete
                : FileShare.ReadWrite;
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                fileShare,
                bufferSize: 1,
                FileOptions.RandomAccess);
            try
            {
                ValidateFile(path, stream);
                return new GuardedStoreFile(
                    stream,
                    createdNew: false,
                    WindowsFileIdentity.GetIdentity(stream.SafeFileHandle),
                    WindowsFileIdentity.GetFinalPath(stream.SafeFileHandle));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SystemException)
        {
            throw new JobStorePathException();
        }
    }

    private GuardedStoreFile OpenFileDeleteGuard(string path)
    {
        var stream = new FileStream(
            WindowsFileIdentity.OpenFileMutationGuard(path, allowReadWriteShare: false),
            FileAccess.Read,
            bufferSize: 1,
            isAsync: false);
        try
        {
            ValidateFile(path, stream);
            return new GuardedStoreFile(
                stream,
                createdNew: false,
                WindowsFileIdentity.GetIdentity(stream.SafeFileHandle),
                WindowsFileIdentity.GetFinalPath(stream.SafeFileHandle));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private GuardedStoreFile OpenFileRenameGuard(string path)
    {
        var stream = new FileStream(
            WindowsFileIdentity.OpenFileMutationGuard(path, allowReadWriteShare: true),
            FileAccess.Read,
            bufferSize: 1,
            isAsync: false);
        try
        {
            ValidateFile(path, stream);
            return new GuardedStoreFile(
                stream,
                createdNew: false,
                WindowsFileIdentity.GetIdentity(stream.SafeFileHandle),
                WindowsFileIdentity.GetFinalPath(stream.SafeFileHandle));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private GuardedStoreFile OpenFileGuard(string path, bool allowDeleteShare)
    {
        try
        {
            ValidateExistingPathComponents(path);
            if (Directory.Exists(path))
            {
                throw new JobStorePathException();
            }

            FileStream? stream = null;
            var createdNew = false;
            WindowsFileIdentity.FileIdentity? createdIdentity = null;
            if (!File.Exists(path))
            {
                try
                {
                    var fileShare = allowDeleteShare
                        ? FileShare.ReadWrite | FileShare.Delete
                        : FileShare.ReadWrite;
                    using var createdStream = new FileInfo(path).Create(
                        FileMode.CreateNew,
                        FileSystemRights.FullControl,
                        fileShare,
                        bufferSize: 4096,
                        FileOptions.RandomAccess,
                        CreateFileSecurity());
                    createdIdentity = WindowsFileIdentity.GetIdentity(createdStream.SafeFileHandle);
                    createdNew = true;
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }

            var existingFileShare = allowDeleteShare
                ? FileShare.ReadWrite | FileShare.Delete
                : FileShare.ReadWrite;
            stream ??= new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                existingFileShare,
                bufferSize: 1,
                FileOptions.RandomAccess);

            try
            {
                ValidateFile(path, stream);
                var identity = WindowsFileIdentity.GetIdentity(stream.SafeFileHandle);
                if (createdIdentity is not null && identity != createdIdentity.Value)
                {
                    throw new JobStorePathException();
                }

                return new GuardedStoreFile(
                    stream,
                    createdNew,
                    identity,
                    WindowsFileIdentity.GetFinalPath(stream.SafeFileHandle));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (JobStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SystemException)
        {
            throw new JobStorePathException();
        }
    }

    private static string CanonicalizeRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Path.IsPathFullyQualified(rootPath) ||
            rootPath.StartsWith("\\\\", StringComparison.Ordinal) ||
            rootPath.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            rootPath.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            throw new JobStorePathException();
        }

        string canonical;
        try
        {
            canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new JobStorePathException();
        }

        var pathRoot = Path.GetPathRoot(canonical);
        if (pathRoot is null ||
            canonical.Length <= Path.TrimEndingDirectorySeparator(pathRoot).Length ||
            canonical.Length < 3 ||
            canonical[1] != ':' ||
            canonical.AsSpan(2).Contains(':'))
        {
            throw new JobStorePathException();
        }

        return canonical;
    }

    private static void EnsureContained(string parent, string candidate)
    {
        var prefix = string.Concat(Path.TrimEndingDirectorySeparator(parent), Path.DirectorySeparatorChar);
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStorePathException();
        }
    }

    private void CreateOrValidateDirectory(string path)
    {
        if (File.Exists(path))
        {
            throw new JobStorePathException();
        }

        if (!Directory.Exists(path))
        {
            new DirectoryInfo(path).Create(CreateDirectorySecurity());
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new JobStorePathException();
        }

        ValidateDirectorySecurity(new DirectoryInfo(path));
    }

    private static void ValidateExistingPathComponents(string path)
    {
        var current = File.Exists(path) || Directory.Exists(path)
            ? new FileInfo(path).Directory
            : new DirectoryInfo(path).Parent;

        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new JobStorePathException();
            }

            current = current.Parent;
        }

        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new JobStorePathException();
        }
    }

    private DirectorySecurity CreateDirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(_currentUser);
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

    private FileSecurity CreateFileSecurity()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(_currentUser);
        foreach (var identity in _allowedIdentities)
        {
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }

    private void ValidateDirectorySecurity(DirectoryInfo directory)
    {
        var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        ValidateSecurityDescriptor(security);
    }

    private static WindowsFileIdentity.FileIdentity ValidateDirectoryHandle(string expectedPath, SafeFileHandle handle)
    {
        if ((WindowsFileIdentity.GetAttributes(handle) & FileAttributes.Directory) == 0 ||
            (WindowsFileIdentity.GetAttributes(handle) & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(WindowsFileIdentity.GetFinalPath(handle), expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStorePathException();
        }

        return WindowsFileIdentity.GetIdentity(handle);
    }

    private static void ValidateDirectoryGuard(
        string expectedPath,
        SafeFileHandle? handle,
        WindowsFileIdentity.FileIdentity expectedIdentity)
    {
        if (handle is null || handle.IsClosed || handle.IsInvalid ||
            ValidateDirectoryHandle(expectedPath, handle) != expectedIdentity)
        {
            throw new JobStorePathException();
        }
    }

    private void ValidateFile(string expectedPath, FileStream stream)
    {
        var attributes = WindowsFileIdentity.GetAttributes(stream.SafeFileHandle);
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(
                WindowsFileIdentity.GetFinalPath(stream.SafeFileHandle),
                expectedPath,
                StringComparison.OrdinalIgnoreCase) ||
            WindowsFileIdentity.GetLinkCount(stream.SafeFileHandle) != 1)
        {
            throw new JobStorePathException();
        }

        ValidateSecurityDescriptor(stream.GetAccessControl());
    }

    private void ValidateFileGuard(string expectedPath, GuardedStoreFile guard)
    {
        if (guard.Stream.SafeFileHandle.IsClosed ||
            guard.Stream.SafeFileHandle.IsInvalid ||
            guard.Identity != WindowsFileIdentity.GetIdentity(guard.Stream.SafeFileHandle) ||
            !string.Equals(guard.FinalPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStorePathException();
        }

        ValidateFile(expectedPath, guard.Stream);
        using var currentPath = new FileStream(
            expectedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.RandomAccess);
        ValidateFile(expectedPath, currentPath);
        if (WindowsFileIdentity.GetIdentity(currentPath.SafeFileHandle) != guard.Identity)
        {
            throw new JobStorePathException();
        }
    }

    private void ValidateSecurityDescriptor(FileSystemSecurity security)
    {
        if (!security.AreAccessRulesProtected)
        {
            throw new JobStorePathException();
        }

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !_allowedIdentities.Contains(owner))
        {
            throw new JobStorePathException();
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
                throw new JobStorePathException();
            }

            if ((rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            {
                identitiesWithFullControl.Add(identity);
            }
        }

        if (!_allowedIdentities.SetEquals(identitiesWithFullControl))
        {
            throw new JobStorePathException();
        }
    }

    private void RejectUnexpectedJournalModes()
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = string.Concat(DatabasePath, suffix);
            using var handle = WindowsFileIdentity.TryOpenPathEntry(path);
            if (handle is not null)
            {
                throw new JobStorePathException();
            }
        }
    }

    private static bool IsDerivedJobDirectoryName(string value) =>
        value.Length == 66 &&
        value.StartsWith("j-", StringComparison.Ordinal) &&
        !value.AsSpan(2).ContainsAnyExcept(LowerHexCharacters);

    private void ValidateMovedFileGuard(string expectedPath, GuardedStoreFile guard)
    {
        if (guard.Stream.SafeFileHandle.IsClosed ||
            guard.Stream.SafeFileHandle.IsInvalid ||
            guard.Identity != WindowsFileIdentity.GetIdentity(guard.Stream.SafeFileHandle))
        {
            throw new JobStorePathException();
        }

        var attributes = WindowsFileIdentity.GetAttributes(guard.Stream.SafeFileHandle);
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0 ||
            WindowsFileIdentity.GetLinkCount(guard.Stream.SafeFileHandle) != 1)
        {
            throw new JobStorePathException();
        }

        using var currentPath = new FileStream(
            expectedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.RandomAccess);
        ValidateFile(expectedPath, currentPath);
        if (WindowsFileIdentity.GetIdentity(currentPath.SafeFileHandle) != guard.Identity)
        {
            throw new JobStorePathException();
        }
    }

    private static bool InspectOptionalRegularFile(string path)
    {
        ValidateExistingPathComponents(path);
        using var handle = WindowsFileIdentity.TryOpenPathEntry(path);
        if (handle is null)
        {
            return false;
        }

        var attributes = WindowsFileIdentity.GetAttributes(handle);
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0 ||
            WindowsFileIdentity.GetLinkCount(handle) != 1 ||
            !string.Equals(WindowsFileIdentity.GetFinalPath(handle), path, StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStorePathException();
        }

        return true;
    }

    private void ValidateInitializationPaths(string databasePath, string journalPath)
    {
        ValidateInitializationArtifactPath(databasePath, allowJournal: false);
        ValidateInitializationArtifactPath(journalPath, allowJournal: true);
        if (!string.Equals(journalPath, string.Concat(databasePath, JournalSuffix), StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStorePathException();
        }
    }

    private void ValidateInitializationArtifactPath(string path, bool allowJournal = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new JobStorePathException();
        }

        string canonical;
        try
        {
            canonical = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new JobStorePathException();
        }

        if (!string.Equals(canonical, path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(canonical), RootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new JobStorePathException();
        }

        EnsureContained(RootPath, canonical);
        var fileName = Path.GetFileName(canonical);
        if (!IsInitializationDatabaseName(fileName) &&
            (!allowJournal || !IsInitializationJournalName(fileName)))
        {
            throw new JobStorePathException();
        }
    }

    private static bool IsInitializationDatabaseName(string value)
    {
        var expectedLength = InitializationFilePrefix.Length +
            InitializationTokenLength +
            InitializationFileSuffix.Length;
        if (value.Length != expectedLength ||
            !value.StartsWith(InitializationFilePrefix, StringComparison.Ordinal) ||
            !value.EndsWith(InitializationFileSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var token = value.AsSpan(InitializationFilePrefix.Length, InitializationTokenLength);
        return !token.ContainsAnyExcept(LowerHexCharacters);
    }

    private static bool IsInitializationJournalName(string value) =>
        value.EndsWith(JournalSuffix, StringComparison.Ordinal) &&
        IsInitializationDatabaseName(value[..^JournalSuffix.Length]);

    private static bool IsSharingViolation(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

[SupportedOSPlatform("windows")]
internal static class WindowsFileIdentity
{
    private const uint GenericRead = 0x80000000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;

    public readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex);

    public static SafeFileHandle OpenDirectoryGuard(
        string path,
        bool allowDeleteShare = false)
    {
        var handle = CreateFile(
            path,
            FileReadAttributes,
            FileShareRead | FileShareWrite | (allowDeleteShare ? FileShareDelete : 0),
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new JobStorePathException();
        }

        return handle;
    }

    public static SafeFileHandle OpenDirectoryDeleteGuard(string path)
    {
        var handle = CreateFile(
            path,
            DeleteAccess | FileReadAttributes,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new JobStorePathException();
        }

        return handle;
    }

    public static SafeFileHandle OpenFileMutationGuard(string path, bool allowReadWriteShare)
    {
        var handle = CreateFile(
            path,
            GenericRead | DeleteAccess,
            allowReadWriteShare ? FileShareRead | FileShareWrite : 0,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("The protected file handle could not be opened.", errorCode);
        }

        return handle;
    }

    public static SafeFileHandle? TryOpenPathEntry(string path)
    {
        var handle = CreateFile(
            path,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var errorCode = Marshal.GetLastWin32Error();
        handle.Dispose();
        if (errorCode is 2 or 3)
        {
            return null;
        }

        throw new JobStorePathException();
    }

    public static void DeleteDirectory(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInformation { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
                handle,
                fileInformationClass: 4,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInformation>()))
        {
            throw new JobStorePathException();
        }
    }

    public static void DeleteFile(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInformation { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
                handle,
                fileInformationClass: 4,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInformation>()))
        {
            throw new JobStorePathException();
        }
    }

    public static bool TryRenameFile(SafeFileHandle handle, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destinationBytes = Encoding.Unicode.GetBytes(destinationPath);
        var rootDirectoryOffset = IntPtr.Size == 8 ? 8 : 4;
        var fileNameLengthOffset = rootDirectoryOffset + IntPtr.Size;
        var fileNameOffset = fileNameLengthOffset + sizeof(uint);
        var bufferSize = checked(fileNameOffset + destinationBytes.Length + sizeof(char));
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var offset = 0; offset < bufferSize; offset++)
            {
                Marshal.WriteByte(buffer, offset, 0);
            }

            Marshal.WriteInt32(buffer, 0, 0);
            Marshal.WriteIntPtr(buffer, rootDirectoryOffset, IntPtr.Zero);
            Marshal.WriteInt32(buffer, fileNameLengthOffset, destinationBytes.Length);
            Marshal.Copy(destinationBytes, 0, IntPtr.Add(buffer, fileNameOffset), destinationBytes.Length);

            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (SetFileInformationByHandle(
                        handle,
                        fileInformationClass: 3,
                        buffer,
                        (uint)bufferSize))
                {
                    return true;
                }

                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode is 32 or 33 && attempt < 9)
                {
                    Thread.Sleep(millisecondsTimeout: 20);
                    continue;
                }
                if (errorCode is 5 or 80 or 183)
                {
                    return false;
                }

                throw new JobStorePathException();
            }

            throw new JobStorePathException();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
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
                throw new JobStorePathException();
            }
            if (length < buffer.Length)
            {
                var value = new string(buffer, 0, (int)length);
                if (value.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Concat("\\\\", value.AsSpan(8));
                }
                if (value.StartsWith("\\\\?\\", StringComparison.Ordinal))
                {
                    value = value[4..];
                }

                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            }

            capacity = checked((int)length + 1);
        }

        throw new JobStorePathException();
    }

    private static ByHandleFileInformation GetInformation(SafeFileHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed || GetFileInformationByHandle(handle, out var information) == 0)
        {
            throw new JobStorePathException();
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        public int DeleteFile;
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
