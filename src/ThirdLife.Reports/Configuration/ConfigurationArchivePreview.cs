using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Reports.Configuration;

public sealed record ConfigurationArchiveFile(string FileName, string ContentSha256, ImmutableArray<byte> Utf8Json)
{
    public int ByteCount => Utf8Json.Length;
}

public sealed class ConfigurationArchivePreview
{
    public const int MaximumArchiveBytes = 256 * 1024;
    private static readonly DateTimeOffset ZipTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly byte[] _archive;

    private ConfigurationArchivePreview(ProfileDocument profile, ImmutableArray<ConfigurationArchiveFile> files, byte[] archive)
    {
        ProfileId = profile.ProfileId;
        ProfileVersion = profile.Version;
        SyntheticData = profile.SyntheticData;
        Files = files;
        _archive = archive;
        ArchiveSha256 = Digest(archive);
        FileName = $"thirdlife-{ProfileId}-{ArchiveSha256[..12]}.zip";
    }

    public string ProfileId { get; }
    public string ProfileVersion { get; }
    public bool SyntheticData { get; }
    public ImmutableArray<ConfigurationArchiveFile> Files { get; }
    public string FileName { get; }
    public string ArchiveSha256 { get; }
    public int ArchiveByteCount => _archive.Length;

    public static ConfigurationArchivePreview? Create(ProfileDocument profile, CatalogDocument catalog, PolicySnapshot policy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(policy);
        var document = policy.Document;
        if (!document.IsCurrentSchema || profile.Policy.Id != document.PolicyId || profile.Policy.Version != document.Version ||
            profile.SyntheticData != document.SyntheticData || profile.SyntheticData != catalog.SyntheticData ||
            profile.Catalog.Id != catalog.CatalogId || profile.Catalog.Version != catalog.Version ||
            profile.CatalogContentSha256 != catalog.ContentSha256)
        {
            return null;
        }

        ImmutableArray<ConfigurationArchiveFile> files =
        [
            new("profile.json", profile.ContentSha256, profile.Utf8Json),
            new("catalog.json", catalog.ContentSha256, catalog.Utf8Json),
            new("policy.json", document.ContentSha256, policy.Utf8Json),
        ];
        if (!Matches(files[0], ProfileSchema.MaximumDocumentBytes) || !Matches(files[1], CatalogSchema.MaximumDocumentBytes) ||
            !Matches(files[2], PolicySchema.MaximumDocumentBytes))
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.FileName, CompressionLevel.NoCompression);
                entry.LastWriteTime = ZipTimestamp;
                using var content = entry.Open();
                content.Write(file.Utf8Json.AsSpan());
            }
        }

        return stream.Length <= MaximumArchiveBytes ? new(profile, files, stream.ToArray()) : null;
    }

    internal byte[] CopyArchiveBytes() => (byte[])_archive.Clone();

    private static bool Matches(ConfigurationArchiveFile file, int maximumBytes) =>
        file.ByteCount is > 0 && file.ByteCount <= maximumBytes &&
        string.Equals(file.ContentSha256, Digest(file.Utf8Json.AsSpan()), StringComparison.Ordinal);

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
