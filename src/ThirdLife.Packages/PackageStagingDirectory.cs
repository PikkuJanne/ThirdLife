using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Packages;

internal sealed class PackageStagingDirectory : IDisposable
{
    private PackageDirectoryGuards? parents;
    private SafeFileHandle? guard;

    private PackageStagingDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public bool Removed { get; private set; }

    public static PackageStagingDirectory Create(long requiredBytes)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (!System.IO.Path.IsPathFullyQualified(local) || local.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new IOException("The package requires a local staging location.");
        }

        var location = new DirectoryInfo(local);
        var stagingVolume = new DriveInfo(location.Root.FullName);
        var systemVolume = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);
        if (stagingVolume.DriveType != DriveType.Fixed || systemVolume.DriveType != DriveType.Fixed ||
            stagingVolume.AvailableFreeSpace < requiredBytes || systemVolume.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException("The package requires local staging and free disk space.");
        }

        var directory = new PackageStagingDirectory(
            System.IO.Path.Combine(location.FullName, $"ThirdLife-package-{Guid.NewGuid():N}"));
        try
        {
            directory.parents = new PackageDirectoryGuards(location);
            var info = new DirectoryInfo(directory.Path);
            var security = PrivateDirectorySecurity();
            info.Create(security);
            directory.guard = WindowsInstallerNative.GuardDirectory(directory.Path);
            if (info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner) !=
                security.GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner))
            {
                throw new IOException("The package staging access rules differ from the private profile.");
            }

            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (guard is { IsClosed: false })
        {
            guard.Dispose();
            try
            {
                Directory.Delete(Path, recursive: false);
                Removed = true;
            }
            catch (Exception error) when (PackageFailure.IsUnavailable(error))
            {
                // A failed cleanup never follows an unexpected child or reuses the path.
            }
        }

        parents?.Dispose();
    }

    private static DirectorySecurity PrivateDirectorySecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new IOException("The package staging identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { owner, new(WellKnownSidType.LocalSystemSid, null),
                     new(WellKnownSidType.BuiltinAdministratorsSid, null) })
        {
            security.AddAccessRule(new(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }
}

internal sealed class PackageDirectoryGuards : IDisposable
{
    private readonly List<SafeFileHandle> guards = [];

    public PackageDirectoryGuards(DirectoryInfo location)
    {
        var ancestors = new Stack<string>();
        for (var parent = location; parent is not null; parent = parent.Parent)
        {
            ancestors.Push(parent.FullName);
        }

        try
        {
            foreach (var ancestor in ancestors)
            {
                guards.Add(WindowsInstallerNative.GuardDirectory(ancestor));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        for (var index = guards.Count - 1; index >= 0; index--)
        {
            guards[index].Dispose();
        }

        guards.Clear();
    }
}
