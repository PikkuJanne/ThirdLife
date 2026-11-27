using System.Security;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Reports.Storage;

namespace ThirdLife.Reports.Configuration;

public enum ConfigurationArchiveSaveResult
{
    Saved = 1,
    DestinationUnavailable,
    AlreadyExists,
    InsufficientSpace,
    PublicationUnconfirmed,
}

public static class ConfigurationArchiveStore
{
    private const int ReserveBytes = 4 * 1024 * 1024;

    public static ConfigurationArchiveSaveResult Save(ConfigurationArchivePreview preview,
        ConfigurationArchiveDestination destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return ConfigurationArchiveSaveResult.DestinationUnavailable;
        }

        var selected = destination.SelectedFolder;
        var publishing = false;
        try
        {
            var root = Path.GetPathRoot(selected);
            if (root is null || new DriveInfo(root) is not { DriveType: DriveType.Fixed } drive)
            {
                return ConfigurationArchiveSaveResult.DestinationUnavailable;
            }

            using var selectedFolder = WindowsDiagnosticFileIdentity.OpenDirectoryGuard(selected);
            var attributes = WindowsDiagnosticFileIdentity.GetAttributes(selectedFolder);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(WindowsDiagnosticFileIdentity.GetFinalPath(selectedFolder), selected, StringComparison.OrdinalIgnoreCase))
            {
                return ConfigurationArchiveSaveResult.DestinationUnavailable;
            }

            if (drive.AvailableFreeSpace < preview.ArchiveByteCount + ReserveBytes)
            {
                return ConfigurationArchiveSaveResult.InsufficientSpace;
            }

            var folder = Path.Combine(selected, ConfigurationArchiveDestination.ProtectedFolderName);
            var file = Path.Combine(folder, preview.FileName);
            var staging = Path.Combine(folder, $".configuration-{Guid.NewGuid():N}.tmp");
            using var security = new DiagnosticStoreSecurity(folder);
            cancellationToken.ThrowIfCancellationRequested();
            security.EnsureSafeRoot();
            if (File.Exists(file) || Directory.Exists(file))
            {
                return ConfigurationArchiveSaveResult.AlreadyExists;
            }

            publishing = true;
            return ProtectedArtifactPublication.Publish(security, preview.CopyArchiveBytes(), staging, file, cancellationToken)
                ? ConfigurationArchiveSaveResult.Saved
                : ConfigurationArchiveSaveResult.AlreadyExists;
        }
        catch (ProtectedPublicationException error) when (error.DestinationChanged)
        {
            return ConfigurationArchiveSaveResult.DestinationUnavailable;
        }
        catch (Exception error) when (error is DiagnosticContractException or UnauthorizedAccessException or SecurityException)
        {
            return ConfigurationArchiveSaveResult.DestinationUnavailable;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException)
        {
            return publishing ? ConfigurationArchiveSaveResult.PublicationUnconfirmed : ConfigurationArchiveSaveResult.DestinationUnavailable;
        }
    }
}
