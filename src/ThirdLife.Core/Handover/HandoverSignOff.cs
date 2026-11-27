using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Core.Handover;

/// <summary>An attributable operator decision over one exact, eligible review.</summary>
public sealed class HandoverSignOff
{
    public const int MaximumHistoryPerJob = 128;

    [JsonConstructor]
    internal HandoverSignOff(long sequence, OperatorId operatorId, DateTimeOffset recordedAtUtc,
        HandoverReviewScope scope, IReadOnlyList<HandoverRequirementEvidence> evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        Sequence = sequence;
        OperatorId = operatorId ?? throw new ArgumentNullException(nameof(operatorId));
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        ArgumentNullException.ThrowIfNull(evidence);
        var proof = evidence.Take(33).ToArray();
        if (proof.Length is < 1 or > 32 || proof.Any(item => item is null || item.Scope != scope ||
                item.State is not (HandoverEvidenceState.Verified or HandoverEvidenceState.OmittedByRecordedChoice)) ||
            proof.Select(item => item.Requirement).Distinct().Count() != proof.Length)
        {
            throw new ArgumentException("Sign-off requires exact, scoped evidence.", nameof(evidence));
        }
        Evidence = Array.AsReadOnly(proof);
    }

    public long Sequence { get; }
    public OperatorId OperatorId { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public HandoverReviewScope Scope { get; }
    public IReadOnlyList<HandoverRequirementEvidence> Evidence { get; }

    public bool IsCurrent(HandoverEligibility current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.CanRequestSignOff && current.Scope == Scope && current.Evidence.SequenceEqual(Evidence);
    }

    public static HandoverSignOff? ForReviewedState(HandoverEligibility reviewed, HandoverEligibility current,
        long sequence, OperatorId operatorId, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.MatchesReview(reviewed)
            ? new(sequence, operatorId, recordedAtUtc, current.Scope, current.Evidence)
            : null;
    }
}
