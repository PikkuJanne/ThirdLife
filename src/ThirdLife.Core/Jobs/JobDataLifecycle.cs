using System.Buffers;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;

namespace ThirdLife.Core.Jobs;

public enum JobLocalFileKind
{
    WorkshopReport = 1,
    RecipientGuide,
    FileHistoryOriginal,
}

public sealed record JobLocalFile
{
    private static readonly SearchValues<char> LowerHex = SearchValues.Create("0123456789abcdef");

    public JobLocalFile(JobLocalFileKind kind, string fileName, int byteLength, string sha256)
    {
        var (prefix, suffix, maximumBytes) = kind switch
        {
            JobLocalFileKind.WorkshopReport => ("workshop-", ".txt", HandoverFileReceipt.MaximumByteLength),
            JobLocalFileKind.RecipientGuide => ("recipient-guide-", ".html", HandoverFileReceipt.MaximumByteLength),
            JobLocalFileKind.FileHistoryOriginal => ("thirdlife-restore-", ".txt", 4096),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (fileName is null || fileName.Length != prefix.Length + 32 + suffix.Length ||
            !fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal) ||
            !IsLowerHex(fileName.AsSpan(prefix.Length, 32)))
        {
            throw new ArgumentException("The generated file name does not match its registered output kind.", nameof(fileName));
        }
        if (byteLength < 1 || byteLength > maximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }
        if (sha256 is not { Length: 64 } || !IsLowerHex(sha256))
        {
            throw new ArgumentException("An exact lowercase SHA-256 digest is required.", nameof(sha256));
        }

        Kind = kind;
        FileName = fileName;
        ByteLength = byteLength;
        Sha256 = sha256;
    }

    public JobLocalFileKind Kind { get; }
    public string FileName { get; }
    public int ByteLength { get; }
    public string Sha256 { get; }

    private static bool IsLowerHex(ReadOnlySpan<char> value) =>
        !value.ContainsAnyExcept(LowerHex);
}

public enum JobLocalFileRemoval
{
    Removed = 1,
    Missing,
    Retained,
}

public sealed record ArchivedJobDeletionReview(JobId JobId, CheckpointId ArchiveCheckpointId,
    IReadOnlyList<JobLocalFile> Files, OperatorId? ApprovedBy, DateTimeOffset? ApprovedAtUtc);

public sealed record ArchivedJobDeletionResult(bool Completed, IReadOnlyList<JobLocalFile> RetainedFiles,
    bool EmptyDirectoryRetained = false);

public interface IJobDataLifecycleStore
{
    Task<ArchivedJobDeletionReview?> ReviewArchivedJobDeletionAsync(JobId jobId, CheckpointId checkpointId,
        CancellationToken token = default);

    Task<ArchivedJobDeletionReview> ApproveArchivedJobDeletionAsync(ArchivedJobDeletionReview review, OperatorId actor,
        CancellationToken token = default);

    Task<ArchivedJobDeletionResult> ExecuteArchivedJobDeletionAsync(JobId jobId, CheckpointId checkpointId,
        Func<JobLocalFile, CancellationToken, JobLocalFileRemoval> removeFile, CancellationToken token = default);

    Task PublishJobLocalFileAsync(JobId jobId, JobLocalFile file, Action<CancellationToken> publish,
        CancellationToken token = default);

    Task<T> PublishFileHistoryOriginalAsync<T>(JobId jobId, FileHistorySampleAudit intent, Func<CancellationToken, T> publish,
        CancellationToken token = default);
}
