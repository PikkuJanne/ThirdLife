using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

public interface IRecipientHandoverStore
{
    Task<SavedRecipientHandover?> LoadRecipientHandoverAsync(JobId jobId, CancellationToken token = default);

    Task SaveHandoverModeAsync(JobId jobId, SavedRecipientHandover expected, HandoverMode mode,
        OperatorId operatorId, CancellationToken token = default);

    Task SaveRecipientChoiceAsync(JobId jobId, SavedRecipientHandover expected, RecipientSetupArea area,
        RecipientSetupChoice choice, bool recipientPresent, OperatorId operatorId, CancellationToken token = default);
}

public sealed record RecipientHandoverEvent
{
    public RecipientHandoverEvent(long sequence, HandoverMode mode, RecipientSetupArea? area,
        RecipientSetupChoice? choice, bool recipientPresent, OperatorId operatorId, DateTimeOffset recordedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        Mode = DomainValue.RequireDefined(mode, nameof(mode));
        if (area.HasValue != choice.HasValue || area.HasValue != recipientPresent ||
            (recipientPresent && mode != HandoverMode.Assisted))
        {
            throw new ArgumentException("A recipient choice requires an area and assisted handover with the recipient present.");
        }

        Sequence = sequence;
        Area = area is { } setupArea ? DomainValue.RequireDefined(setupArea, nameof(area)) : null;
        Choice = choice is { } setupChoice ? DomainValue.RequireDefined(setupChoice, nameof(choice)) : null;
        RecipientPresent = recipientPresent;
        OperatorId = operatorId ?? throw new ArgumentNullException(nameof(operatorId));
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
    }

    public long Sequence { get; }
    public HandoverMode Mode { get; }
    public RecipientSetupArea? Area { get; }
    public RecipientSetupChoice? Choice { get; }
    public bool RecipientPresent { get; }
    public OperatorId OperatorId { get; }
    public DateTimeOffset RecordedAtUtc { get; }
}

public sealed class SavedRecipientHandover
{
    public const int MaximumHistoryPerJob = 512;

    private SavedRecipientHandover(JobId jobId, bool isArchived, RecipientHandover? handover,
        long modeSequence, IReadOnlyList<RecipientHandoverEvent> history)
    {
        JobId = jobId;
        IsArchived = isArchived;
        Handover = handover;
        ModeSequence = modeSequence;
        History = history;
    }

    public JobId JobId { get; }
    public bool IsArchived { get; }
    public RecipientHandover? Handover { get; }
    public long ModeSequence { get; }
    public IReadOnlyList<RecipientHandoverEvent> History { get; }
    public long Sequence => History.Count == 0 ? 0 : History[^1].Sequence;
    public bool CanRecord => !IsArchived && History.Count < MaximumHistoryPerJob;

    public static SavedRecipientHandover Evaluate(JobId jobId, bool isArchived, IReadOnlyList<RecipientHandoverEvent> history)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count > MaximumHistoryPerJob)
        {
            throw new ArgumentOutOfRangeException(nameof(history));
        }

        var events = history.ToArray();
        var modeSequence = 0L;
        RecipientHandover? handover = null;
        for (var index = 0; index < events.Length; index++)
        {
            var entry = events[index];
            if (entry?.Sequence != index + 1)
            {
                throw new ArgumentException("Handover history must be contiguous and in sequence order.", nameof(history));
            }

            if (entry.Area is { } area && entry.Choice is { } choice)
            {
                if (handover is null || handover.Mode != entry.Mode)
                {
                    throw new ArgumentException("The selected mode must precede each recorded recipient choice.", nameof(history));
                }
                handover = handover.RecordChoice(area, choice, entry.RecipientPresent);
            }
            else
            {
                if (handover?.Mode == entry.Mode)
                {
                    throw new ArgumentException("Each saved selection must change the handover mode.", nameof(history));
                }
                handover = RecipientHandover.Start(entry.Mode);
                modeSequence = entry.Sequence;
            }
        }

        return new(jobId, isArchived, handover, modeSequence, Array.AsReadOnly(events));
    }
}
