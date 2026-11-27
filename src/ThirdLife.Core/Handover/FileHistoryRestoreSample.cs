using System.Security.Cryptography;
using System.Text;

namespace ThirdLife.Core.Handover;

public sealed class FileHistoryRestoreSample
{
    private readonly byte[] _content;

    internal FileHistoryRestoreSample(Guid reviewId) : this(reviewId, Guid.NewGuid())
    {
    }

    internal FileHistoryRestoreSample(Guid reviewId, Guid sampleId)
    {
        ReviewId = reviewId;
        SampleId = sampleId;
        FileName = $"thirdlife-restore-{SampleId:N}.txt";
        _content = Encoding.ASCII.GetBytes($"ThirdLife File History restore sample\r\n{SampleId:N}\r\n");
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(_content));
    }

    internal Guid ReviewId { get; }
    public Guid SampleId { get; }
    public string FileName { get; }
    public int ByteLength => _content.Length;
    public string Sha256 { get; }

    public byte[] CopyContent() => [.. _content];

    public bool IsFor(FileHistoryApproval approval) => approval.Review.ReviewId == ReviewId;

    internal bool Matches(FileHistorySampleDigest digest) =>
        digest.ByteLength == ByteLength && string.Equals(digest.Sha256, Sha256, StringComparison.Ordinal);
}

public readonly record struct FileHistorySampleDigest(int ByteLength, string Sha256);

/// <summary>Independent observations of the two distinct guarded sample files.</summary>
public sealed record FileHistorySampleObservation(Guid SampleId, FileHistorySampleDigest Original, FileHistorySampleDigest Recovered);
