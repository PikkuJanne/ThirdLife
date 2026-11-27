using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

/// <summary>Exact publication metadata supported by the registered output stores.</summary>
public sealed record HandoverFileReceipt
{
    public const int MaximumByteLength = 8 * 1024 * 1024;

    public HandoverFileReceipt(HandoverOutputReference output, string fileName, int byteLength)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        if (output.Kind is not (HandoverOutputKind.WorkshopRecord or HandoverOutputKind.RecipientGuide))
        {
            throw new ArgumentException("Use the separate support export receipt for support archives.", nameof(output));
        }
        if (fileName is not { Length: > 0 and <= 64 } || fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'z' or '-' or '.')))
        {
            throw new ArgumentException("A bounded generated file name is required.", nameof(fileName));
        }
        if (byteLength is < 1 or > MaximumByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        FileName = fileName;
        ByteLength = byteLength;
    }

    public HandoverOutputReference Output { get; }
    public string FileName { get; }
    public int ByteLength { get; }
}

public interface IHandoverFileVerifier
{
    bool MatchesPublishedFile(HandoverFileReceipt receipt, CancellationToken token);
}

public interface IHandoverCompletionStore
{
    Task<HandoverEligibility?> LoadHandoverReviewAsync(JobId jobId, CancellationToken token = default);
    Task<HandoverEligibility?> LoadHandoverReviewAsync(JobId jobId, IHandoverFileVerifier verifier, CancellationToken token = default);
    Task SaveHandoverFileAsync(HandoverFileReceipt receipt, IHandoverFileVerifier verifier, CancellationToken token = default);
    Task SaveHandoverSupportOmissionAsync(HandoverReviewScope expected, OperatorId actor, SupportBundleOmissionReason reason,
        CancellationToken token = default);
    Task<HandoverSignOff> CompleteHandoverAsync(HandoverEligibility reviewed, OperatorId actor, IHandoverFileVerifier verifier,
        CancellationToken token = default);
    Task<SavedHandoverCompletion?> ReadHandoverCompletionAsync(JobId jobId, IHandoverFileVerifier verifier,
        CancellationToken token = default);
}

public sealed record SavedHandoverCompletion(HandoverSignOff Latest, bool IsCurrent);
