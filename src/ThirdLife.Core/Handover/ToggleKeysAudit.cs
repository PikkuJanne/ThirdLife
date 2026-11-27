using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

[JsonConverter(typeof(StableStringEnumConverter<ToggleKeysAuditOutcome>))]
public enum ToggleKeysAuditOutcome
{
    [JsonStringEnumMemberName("approved_pending")]
    ApprovedPending = 1,
    [JsonStringEnumMemberName("precondition_changed")]
    PreconditionChanged,
    [JsonStringEnumMemberName("windows_declined")]
    WindowsDeclined,
    [JsonStringEnumMemberName("verified")]
    Verified,
    [JsonStringEnumMemberName("observed_mismatch")]
    ObservedMismatch,
    [JsonStringEnumMemberName("verification_unavailable")]
    VerificationUnavailable,
}

/// <summary>Per-job receipt for one authorized setting attempt. The runtime user binding remains transient.</summary>
public sealed record ToggleKeysAuditEntry
{
    public ToggleKeysAuditEntry(Guid changeId, long handoverSequence, bool before, bool requested, OperatorId operatorId,
        DateTimeOffset recordedAtUtc, ToggleKeysAuditOutcome outcome, bool? independentlyObserved)
    {
        if (changeId == Guid.Empty || before == requested)
        {
            throw new ArgumentException("A nonempty setting change is required.", nameof(changeId));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(handoverSequence);
        Outcome = DomainValue.RequireDefined(outcome, nameof(outcome));
        var expected = outcome switch
        {
            ToggleKeysAuditOutcome.Verified => requested,
            ToggleKeysAuditOutcome.ObservedMismatch => !requested,
            _ => (bool?)null,
        };
        if (independentlyObserved != expected)
        {
            throw new ArgumentException("The selected outcome requires the matching independent observation.", nameof(independentlyObserved));
        }

        ChangeId = changeId;
        HandoverSequence = handoverSequence;
        Before = before;
        Requested = requested;
        OperatorId = operatorId ?? throw new ArgumentNullException(nameof(operatorId));
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
        IndependentlyObserved = independentlyObserved;
    }

    public Guid ChangeId { get; }
    public long HandoverSequence { get; }
    public bool Before { get; }
    public bool Requested { get; }
    public OperatorId OperatorId { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public ToggleKeysAuditOutcome Outcome { get; }
    public bool? IndependentlyObserved { get; }
}

public interface IToggleKeysAuditStore
{
    Task<ToggleKeysAuditEntry> StageToggleKeysAsync(ToggleKeysApproval approval, OperatorId actor, CancellationToken token = default);
    Task RecordToggleKeysOutcomeAsync(JobId jobId, ToggleKeysAuditEntry expected, ToggleKeysAuditOutcome result,
        bool? observed, CancellationToken token = default);
    Task<IReadOnlyList<ToggleKeysAuditEntry>> LoadToggleKeysAttemptsAsync(JobId jobId, CancellationToken token = default);
}
