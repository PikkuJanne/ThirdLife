using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Packages;

internal sealed class WindowsInstallerPackageRuntime : IPilotPackageRuntime
{
    private const string ProductCode = "{A23B657D-838B-4C9B-8F9D-386E50C1A76B}";
    private const string UpgradeCode = "{CDB13460-04DF-4708-A7FD-4CB4A0684605}";
    private const string InstalledPublisher = "Notepad++ team (MSI installer)";
    private const string InstallerName = "npp.8.9.9.1.Installer.x64.msi";
    private const int InstallerLength = 7_933_952;
    private const int BufferSize = 64 * 1024;
    private const int DiskHeadroomBytes = 128 * 1024 * 1024;
    private static readonly Uri Release = new(
        "https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.9.1/" + InstallerName);
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    });

    public PackageObservation Inspect()
    {
        if (WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported ||
            !File.Exists(InstallerExecutable()))
        {
            return new(PackageObservationStatus.Unavailable);
        }

        try
        {
            var (status, state) = WindowsInstallerNative.ReadMachineProperty(ProductCode, "State");
            if (status == WindowsInstallerNative.Success)
            {
                var (_, version) = WindowsInstallerNative.ReadMachineProperty(ProductCode, "VersionString");
                var (_, publisher) = WindowsInstallerNative.ReadMachineProperty(ProductCode, "Publisher");
                return state == "5" && version == PilotPackageService.Identity.ExactVersion && publisher == InstalledPublisher
                    ? new(PackageObservationStatus.Installed, version)
                    : new(PackageObservationStatus.Conflict);
            }

            if (status != WindowsInstallerNative.UnknownProduct)
            {
                return new(PackageObservationStatus.Unavailable);
            }

            return WindowsInstallerNative.HasRelatedProduct(UpgradeCode) || HasOtherDistribution()
                ? new(PackageObservationStatus.Conflict)
                : new(PackageObservationStatus.Absent);
        }
        catch (Exception error) when (PackageFailure.IsUnavailable(error))
        {
            return new(PackageObservationStatus.Unavailable);
        }
    }

    public async Task<IPackageArtifact> AcquireAsync(CancellationToken cancellationToken) =>
        await AcquireAsync(Client, PackageStagingDirectory.Create(DiskHeadroomBytes + InstallerLength), cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<IPackageArtifact> AcquireAsync(HttpClient client, PackageStagingDirectory directory,
        CancellationToken cancellationToken)
    {
        var artifact = new PackageArtifact(directory);
        try
        {
            using var response = await OpenReleaseAsync(client, cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != InstallerLength)
            {
                throw new PackageTrustException("The installer length differs from the reviewed artifact.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(artifact.Path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                var downloaded = 0;
                int count;
                while ((count = await ReadDownloadAsync(input, buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    downloaded += count;
                    if (downloaded > InstallerLength)
                    {
                        throw new PackageTrustException("The installer exceeds the reviewed artifact length.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }

                if (downloaded != InstallerLength)
                {
                    throw new PackageTrustException("The installer is shorter than the reviewed artifact.");
                }
            }

            var locked = artifact.Lock();
            var digest = await SHA256.HashDataAsync(locked, cancellationToken).ConfigureAwait(false);
            locked.Position = 0;
            cancellationToken.ThrowIfCancellationRequested();
            if (!digest.AsSpan().SequenceEqual(Convert.FromHexString(PilotPackageService.Identity.InstallerSha256)))
            {
                throw new PackageTrustException("The installer digest differs from the reviewed SHA-256.");
            }

            // The MSI SIP opens the protected path with its own native file handle.
            var signature = WindowsInstallerNative.VerifySignature(artifact.Path);
            if (signature != 0)
            {
                throw new PackageTrustException($"Windows Installer signature trust failed (0x{signature:X8}).");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return artifact;
        }
        catch
        {
            artifact.Dispose();
            throw;
        }
    }

    public async Task<PackageInstallResult> ApplyAsync(IPackageArtifact artifact, CancellationToken cancellationToken)
    {
        var installer = (PackageArtifact)artifact;
        using var currentIdentity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(currentIdentity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return new(PackageInstallStatus.Unavailable);
        }

        // The installed Windows Installer handles machine authorization and any UAC.
        var info = new ProcessStartInfo(InstallerExecutable())
        {
            WorkingDirectory = Environment.SystemDirectory,
        };
        info.ArgumentList.Add("/package");
        info.ArgumentList.Add(installer.Path);
        info.ArgumentList.Add("/passive");
        info.ArgumentList.Add("/norestart");
        info.ArgumentList.Add("ALLUSERS=1");
        info.ArgumentList.Add("NOUPDATER=1");

        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return new(PackageInstallStatus.AuthorizationDeclined);
        }

        using (process)
        {
            if (process is null)
            {
                return new(PackageInstallStatus.ReviewRequired);
            }

            // Windows Installer can continue after the UI stops waiting.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return new(process.ExitCode switch
            {
                0 => PackageInstallStatus.Applied,
                3010 => PackageInstallStatus.RestartRequired,
                1223 => PackageInstallStatus.AuthorizationDeclined,
                1602 => PackageInstallStatus.Cancelled,
                1618 or 1641 => PackageInstallStatus.ReviewRequired,
                _ => PackageInstallStatus.Failed,
            });
        }
    }

    private static async Task<HttpResponseMessage> OpenReleaseAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(Release, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect)
            {
                var target = response.Headers.Location;
                if (target is not
                    {
                        IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true,
                        Host: "release-assets.githubusercontent.com", UserInfo.Length: 0, Fragment.Length: 0
                    } ||
                    !target.AbsolutePath.StartsWith("/github-production-release-asset", StringComparison.Ordinal))
                {
                    throw new PackageTrustException("The installer redirect differs from the reviewed release host.");
                }

                response.Dispose();
                response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(null, null, response.StatusCode);
            }

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async ValueTask<int> ReadDownloadAsync(Stream input, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Only the network stream is read here. Staging writes and reads have separate failure semantics.
            throw new PackageNetworkException();
        }
    }

    private static string InstallerExecutable() => Path.Combine(Environment.SystemDirectory, "msiexec.exe");

    private static bool HasOtherDistribution()
    {
        const string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Notepad++";
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var product = root.OpenSubKey(uninstallKey, writable: false);
                if (product is not null)
                {
                    return true;
                }
            }
        }

        return Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Notepad++"))
            || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Notepad++"));
    }

    private sealed class PackageArtifact(PackageStagingDirectory directory) : IPackageArtifact
    {
        private FileStream? locked;
        public string Path { get; } = System.IO.Path.Combine(directory.Path, InstallerName);

        public FileStream Lock()
        {
            if (File.GetAttributes(Path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new PackageTrustException("The acquired installer path is redirected.");
            }

            // Keep the same approved bytes stable until the native installer exits.
            locked = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return locked;
        }

        public void Dispose()
        {
            locked?.Dispose();
            try
            {
                File.Delete(Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A failed cleanup leaves one bounded file. It is never reused.
            }
            finally
            {
                directory.Dispose();
            }
        }
    }
}
