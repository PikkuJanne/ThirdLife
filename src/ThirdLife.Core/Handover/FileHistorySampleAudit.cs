using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

public enum FileHistorySampleAuditState
{
    Preparing = 1,
    Prepared,
    PreparationUnavailable,
    CopyUnavailable,
    CopyMismatch,
    CopyMatches,
}

public sealed record FileHistorySampleAudit
{
    public FileHistorySampleAudit(Guid sampleId, long handoverSequence, string sha256, int byteLength,
        OperatorId operatorId, DateTimeOffset observedAtUtc, FileHistorySampleAuditState state)
    {
        ArgumentNullException.ThrowIfNull(operatorId);
        if (sampleId == Guid.Empty || handoverSequence <= 0 || sha256 is not { Length: 64 } ||
            !sha256.All(char.IsAsciiHexDigitLower) || byteLength is <= 0 or > 4096 ||
            observedAtUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(state))
        {
            throw new ArgumentException("The restore sample audit record is invalid.");
        }

        SampleId = sampleId;
        HandoverSequence = handoverSequence;
        Sha256 = sha256;
        ByteLength = byteLength;
        OperatorId = operatorId;
        ObservedAtUtc = observedAtUtc;
        State = state;
    }

    public Guid SampleId { get; }
    [JsonIgnore]
    public string FileName => FileHistoryRestoreSample.GetFileName(SampleId);
    public long HandoverSequence { get; }
    public string Sha256 { get; }
    public int ByteLength { get; }
    public OperatorId OperatorId { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public FileHistorySampleAuditState State { get; }

    public bool CanRecord(FileHistorySampleAuditState next) => State switch
    {
        FileHistorySampleAuditState.Preparing =>
            next is FileHistorySampleAuditState.Prepared or FileHistorySampleAuditState.PreparationUnavailable,
        FileHistorySampleAuditState.Prepared or FileHistorySampleAuditState.CopyUnavailable
            or FileHistorySampleAuditState.CopyMismatch or FileHistorySampleAuditState.CopyMatches =>
            next is FileHistorySampleAuditState.CopyUnavailable or FileHistorySampleAuditState.CopyMismatch or FileHistorySampleAuditState.CopyMatches,
        _ => false,
    };
}

public interface IFileHistorySampleAuditStore
{
    Task<FileHistorySampleAudit> StageFileHistorySampleAsync(FileHistoryApproval approval, FileHistoryRestoreSample sample,
        OperatorId actor, CancellationToken token = default);

    Task<FileHistorySampleAudit> RecordFileHistorySampleAsync(JobId jobId, FileHistorySampleAudit expected,
        FileHistorySampleAuditState outcome, OperatorId actor, CancellationToken token = default);

    Task<IReadOnlyList<FileHistorySampleAudit>> LoadFileHistorySampleHistoryAsync(JobId jobId, CancellationToken token = default);
}
