using System.Security.Cryptography;
using System.Security.Principal;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Packages;

internal static class WindowsPackageLaunchProbe
{
    internal const string ExecutableName = "notepad++.exe";
    private const string LocalSettings = "doLocalConf.xml";
    private const int MaximumWorkspacePathLength = 196;
    private static readonly ProbeFile[] Files =
    [
        new(ExecutableName, 8_594_928, "5518AD65F576B993C48DA1878BDA83A62F7A101D246AEFAB472379CA8800390C"),
        new("langs.model.xml", 584_703, "FB2A98A1CD701CF5C05723E337666B786ED9D5CB5DBD6C93C11E0D5C80A8A671"),
        new("stylers.model.xml", 258_540, "782C193C8A962D746735F7AA75C25BA3ECAE5662E867CF27E4DF60F906BDEA38"),
    ];
    private static readonly string[] GeneratedFiles =
        ["langs.xml", "stylers.xml", "config.xml", "contextMenu.xml", "shortcuts.xml"];
    private static int probeInProgress;

    public static PackageVerificationStatus Run(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref probeInProgress, 1, 0) != 0)
        {
            return PackageVerificationStatus.LaunchFailed;
        }

        PackageStagingDirectory? workspace = null;
        PackageVerificationStatus result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var identity = WindowsIdentity.GetCurrent();
            using var impersonation = WindowsIdentity.GetCurrent(ifImpersonating: true);
            if (WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported ||
                impersonation is not null || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return PackageVerificationStatus.LaunchFailed;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!Path.IsPathFullyQualified(programFiles) || programFiles.StartsWith(@"\\", StringComparison.Ordinal) ||
                new DriveInfo(Path.GetPathRoot(programFiles)!).DriveType != DriveType.Fixed)
            {
                return PackageVerificationStatus.ExecutableUntrusted;
            }

            var installed = Path.Combine(programFiles, "Notepad++");
            using var programFilesGuard = new PackageDirectoryGuards(new DirectoryInfo(programFiles));
            if (!Directory.Exists(installed))
            {
                return PackageVerificationStatus.ExecutableMissing;
            }

            using var installedGuard = WindowsInstallerNative.GuardDirectory(installed);
            workspace = PackageStagingDirectory.Create(Files.Sum(file => (long)file.Length) + 1024 * 1024);
            // This vendor build uses MAX_PATH buffers for local settings and their child paths.
            result = workspace.Path.Length <= MaximumWorkspacePathLength
                ? StageAndLaunch(installed, workspace.Path, cancellationToken)
                : PackageVerificationStatus.LaunchFailed;
        }
        catch (FileNotFoundException)
        {
            result = PackageVerificationStatus.ExecutableMissing;
        }
        catch (PackageTrustException)
        {
            result = PackageVerificationStatus.ExecutableUntrusted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = PackageVerificationStatus.Cancelled;
        }
        catch (Exception error) when (PackageFailure.IsUnavailable(error))
        {
            result = PackageVerificationStatus.LaunchFailed;
        }
        finally
        {
            try
            {
                if (workspace is not null)
                {
                    RemoveWorkspace(workspace);
                }
            }
            finally
            {
                Interlocked.Exchange(ref probeInProgress, 0);
            }
        }

        return workspace is { Removed: false } ? PackageVerificationStatus.CleanupUnconfirmed : result;
    }

    private static PackageVerificationStatus StageAndLaunch(string installed, string workspace, CancellationToken cancellationToken)
    {
        var locks = new List<FileStream>(Files.Length);
        try
        {
            foreach (var file in Files)
            {
                var destination = Path.Combine(workspace, file.Name);
                StageFile(Path.Combine(installed, file.Name), destination, file.Length, cancellationToken);
                var staged = new FileStream(WindowsInstallerNative.OpenProbeFile(destination), FileAccess.Read);
                locks.Add(staged);
                if (staged.Length != file.Length ||
                    !SHA256.HashData(staged).AsSpan().SequenceEqual(Convert.FromHexString(file.Sha256)))
                {
                    throw new PackageTrustException("The installed package probe file differs from the reviewed bytes.");
                }
            }

            // The vendor detects this marker beside the image and uses that directory for its settings.
            using var localMode = new FileStream(Path.Combine(workspace, LocalSettings), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read);
            cancellationToken.ThrowIfCancellationRequested();
            return PackageLaunchProcess.Run(workspace, cancellationToken);
        }
        finally
        {
            foreach (var stream in locks)
            {
                stream.Dispose();
            }
        }
    }

    private static void StageFile(string source, string destination, int length, CancellationToken cancellationToken)
    {
        using var installedFile = new FileStream(WindowsInstallerNative.OpenProbeFile(source), FileAccess.Read);
        if (installedFile.Length != length)
        {
            throw new PackageTrustException("The installed package probe file length differs from the reviewed length.");
        }

        using var staged = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[64 * 1024];
        for (var remaining = length; remaining > 0;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(remaining, buffer.Length);
            installedFile.ReadExactly(buffer.AsSpan(0, count));
            staged.Write(buffer, 0, count);
            remaining -= count;
        }
    }

    private static void RemoveWorkspace(PackageStagingDirectory workspace)
    {
        try
        {
            foreach (var name in Files.Select(file => file.Name).Append(LocalSettings).Concat(GeneratedFiles))
            {
                try
                {
                    File.Delete(Path.Combine(workspace.Path, name));
                }
                catch (Exception error) when (PackageFailure.IsUnavailable(error))
                {
                    // Removing the workspace below confirms whether any artifact remains.
                }
            }

            try
            {
                // This build creates an empty local plugins directory even when plugin loading is disabled.
                Directory.Delete(Path.Combine(workspace.Path, "plugins"), recursive: false);
            }
            catch (Exception error) when (PackageFailure.IsUnavailable(error))
            {
                // Missing is fine. Any remaining directory is reported by the workspace removal check.
            }
        }
        finally
        {
            workspace.Dispose();
        }
    }

    private sealed record ProbeFile(string Name, int Length, string Sha256);
}
