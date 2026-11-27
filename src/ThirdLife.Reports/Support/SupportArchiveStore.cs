using System.Security;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Diagnostics.Redaction;
using ThirdLife.Reports.Storage;

namespace ThirdLife.Reports.Support;

public sealed record SupportArchiveArtifact(string FileName, string Digest, int ByteCount);

public sealed class SupportArchiveStore
{
    private const long ReserveBytes = 4 * 1024 * 1024;
    private readonly string _directory;

    public SupportArchiveStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The protected export folder requires an absolute path.", nameof(directory));
        }

        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    public SupportArchiveArtifact Export(SupportArchivePreview preview, string approvedArchiveSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(preview.ArchiveSha256, approvedArchiveSha256, StringComparison.Ordinal))
        {
            throw ExportFailure("support_preview_changed", "Preview the current archive before exporting.");
        }

        var name = $"{preview.InternalSupportId}.zip";
        var destination = Path.Combine(_directory, name);
        var staging = Path.Combine(_directory, $".support-{Guid.NewGuid():N}.tmp");
        try
        {
            var root = Path.GetPathRoot(_directory);
            if (root is null || root == _directory ||
                (OperatingSystem.IsWindows() && (root.Length != 3 || root[1] != ':' ||
                    _directory.AsSpan(2).Contains(':') || new DriveInfo(root).DriveType != DriveType.Fixed)))
            {
                throw ExportFailure("support_destination_unavailable", "Use the protected local support folder.");
            }

            using var security = new DiagnosticStoreSecurity(_directory);
            security.EnsureSafeRoot();
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw ExportFailure("support_file_exists", "An archive with that support code already exists.");
            }

            if (new DriveInfo(root).AvailableFreeSpace < preview.ArchiveByteCount + ReserveBytes)
            {
                throw ExportFailure("support_capacity", "The protected support folder has insufficient free space.");
            }

            if (!ProtectedArtifactPublication.Publish(security, preview.CopyArchiveBytes(), staging, destination, cancellationToken))
            {
                throw ExportFailure("support_file_exists", "An archive with that support code already exists.");
            }

            return new(name, preview.ArchiveSha256, preview.ArchiveByteCount);
        }
        catch (DiagnosticContractException exception) when (exception.ResultCode.StartsWith("support_", StringComparison.Ordinal))
        {
            throw;
        }
        catch (ProtectedPublicationException exception) when (exception.DestinationChanged)
        {
            throw ExportFailure("support_destination_unavailable", "The protected support folder changed before publication.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or
            DiagnosticContractException or NotSupportedException)
        {
            throw ExportFailure("support_export_failed", "Could not publish the support archive in the protected local folder.");
        }
    }

    private static DiagnosticContractException ExportFailure(string code, string message) => new(code, message);
}
