using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Reports.WorkshopReport;

public enum WorkshopAssessmentOutcome
{
    Incomplete = 1,
    Completed,
    Cancelled,
    RequiresReview,
}

public sealed record WorkshopAssessmentSummary
{
    public WorkshopAssessmentSummary(
        JobId jobId,
        SanitizationGateDecisionId gateDecisionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? finishedAtUtc,
        WorkshopAssessmentOutcome outcome,
        IReadOnlyList<ProviderId> unrunProviders,
        string? assessmentId = null)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        GateDecisionId = gateDecisionId ?? throw new ArgumentNullException(nameof(gateDecisionId));
        ArgumentNullException.ThrowIfNull(unrunProviders);
        if (startedAtUtc == default || finishedAtUtc == default(DateTimeOffset) ||
            !Enum.IsDefined(outcome) || (outcome == WorkshopAssessmentOutcome.Incomplete) != (finishedAtUtc is null) ||
            unrunProviders.Count > 16 || unrunProviders.Any(static provider => provider is null) ||
            unrunProviders.Distinct().Count() != unrunProviders.Count ||
            (outcome == WorkshopAssessmentOutcome.Completed && unrunProviders.Count != 0))
        {
            throw new ArgumentException("The saved assessment summary is inconsistent.");
        }

        StartedAtUtc = startedAtUtc.ToUniversalTime();
        FinishedAtUtc = finishedAtUtc?.ToUniversalTime();
        Outcome = outcome;
        UnrunProviders = Array.AsReadOnly(unrunProviders.OrderBy(static provider => provider.Value, StringComparer.Ordinal).ToArray());
        AssessmentId = assessmentId is null ? null : new EvidenceId(assessmentId).Value;
    }

    public JobId JobId { get; }

    public SanitizationGateDecisionId GateDecisionId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? FinishedAtUtc { get; }

    public WorkshopAssessmentOutcome Outcome { get; }

    public IReadOnlyList<ProviderId> UnrunProviders { get; }

    public string? AssessmentId { get; }
}
