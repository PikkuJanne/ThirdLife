using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Versioning;

public sealed class PolicyDecisionRecord
{
    public const int MaximumSerializedBytes = 2 * 1024 * 1024;
    private const int MaximumRuleReportCharacters = 160_000;

    [JsonConstructor]
    internal PolicyDecisionRecord(
        string decisionId,
        JobId jobId,
        string assessmentId,
        string policyId,
        string policyVersion,
        string policySha256,
        DateTimeOffset evaluatedAtUtc,
        string? previousDecisionId,
        ImmutableArray<EvidenceId> evidenceIds,
        ImmutableArray<string>? activeCapabilityIds,
        Disposition disposition,
        string title,
        string summary,
        string explanation,
        string ruleReport,
        string? exceptionReport = null,
        int? jobEvidenceCount = null)
    {
        if (jobEvidenceCount is { } count && (count is < 1 or > 10_000 || evidenceIds.IsDefault || count < evidenceIds.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(jobEvidenceCount));
        }
        JobEvidenceCount = jobEvidenceCount;
        DecisionId = RequireId(decisionId, nameof(decisionId));
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        AssessmentId = RequireId(assessmentId, nameof(assessmentId));
        PolicyId = RequirePolicyText(policyId, PolicySchema.IsPolicyId, nameof(policyId));
        PolicyVersion = RequirePolicyText(policyVersion, PolicySchema.IsPolicyVersion, nameof(policyVersion));
        PolicySha256 = RequirePolicyText(policySha256, IsSha256, nameof(policySha256));
        EvaluatedAtUtc = RequireTimestamp(evaluatedAtUtc, nameof(evaluatedAtUtc));
        PreviousDecisionId = previousDecisionId is null ? null : RequireId(previousDecisionId, nameof(previousDecisionId));
        if (PreviousDecisionId == DecisionId)
        {
            throw new ArgumentException("A decision cannot reference itself as its predecessor.", nameof(previousDecisionId));
        }

        RequireOrderedEvidence(evidenceIds);
        EvidenceIds = evidenceIds;
        RequireOrderedCapabilities(activeCapabilityIds);
        ActiveCapabilityIds = activeCapabilityIds;
        Disposition = Enum.IsDefined(disposition)
            ? disposition
            : throw new ArgumentException("The saved disposition is invalid.", nameof(disposition));
        Title = RequireReportText(title, 128, nameof(title));
        Summary = RequireReportText(summary, 512, nameof(summary));
        Explanation = RequireReportText(explanation, 8_192, nameof(explanation));
        RuleReport = RequireReportText(ruleReport, MaximumRuleReportCharacters, nameof(ruleReport));
        ExceptionReport = exceptionReport is null ? null : RequireReportText(exceptionReport, MaximumRuleReportCharacters, nameof(exceptionReport));
    }

    // An append-only job can contain additional evidence (including assessment journal markers)
    // that is outside the policy projection. Bind its full extent without citing those as rule facts.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? JobEvidenceCount { get; }

    public string DecisionId { get; }

    public JobId JobId { get; }

    public string AssessmentId { get; }

    public string PolicyId { get; }

    public string PolicyVersion { get; }

    public string PolicySha256 { get; }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public string? PreviousDecisionId { get; }

    public ImmutableArray<EvidenceId> EvidenceIds { get; }

    public ImmutableArray<string>? ActiveCapabilityIds { get; }

    public Disposition Disposition { get; }

    public string Title { get; }

    public string Summary { get; }

    public string Explanation { get; }

    public string RuleReport { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExceptionReport { get; }

    [JsonIgnore]
    public bool HasEvaluationProvenance { get; private init; }

    public static PolicyDecisionRecord Create(
        PolicySnapshot snapshot,
        JobId jobId,
        string assessmentId,
        DispositionDecision decision,
        IEnumerable<EvidenceId> evidenceIds,
        DateTimeOffset evaluatedAtUtc,
        string? previousDecisionId = null,
        int? jobEvidenceCount = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(evidenceIds);

        var policy = snapshot.Document;
        var evaluatedUtc = RequireTimestamp(evaluatedAtUtc, nameof(evaluatedAtUtc));
        if (decision.Evaluation.Policy.ContentSha256 != policy.ContentSha256 ||
            (decision.JobId is { } scopedJob && scopedJob != jobId) ||
            (decision.EvaluatedAtUtc is { } scopedTime && scopedTime != evaluatedUtc))
        {
            throw new ArgumentException("The decision does not belong to the supplied policy or assessment context.", nameof(decision));
        }

        var uniqueIds = new HashSet<EvidenceId>();
        var count = 0;
        foreach (var id in evidenceIds)
        {
            if (++count > PolicyEvaluator.MaximumObservations || id is null)
            {
                throw new ArgumentException("The decision evidence set exceeds its limit or contains an invalid ID.", nameof(evidenceIds));
            }

            uniqueIds.Add(id);
        }

        foreach (var references in decision.RuleDetails.Select(detail => detail.Outcome.Evidence).Distinct())
        {
            if (references.Any(reference => !uniqueIds.Contains(reference.EvidenceId)))
            {
                throw new ArgumentException("Every cited fact must be part of the selected immutable evidence set.", nameof(evidenceIds));
            }
        }

        return new PolicyDecisionRecord(
            string.Concat("policy-decision-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)),
            jobId,
            assessmentId,
            policy.PolicyId,
            policy.Version,
            policy.ContentSha256,
            evaluatedUtc,
            previousDecisionId,
            uniqueIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
            decision.Evaluation.ActiveCapabilityIds,
            decision.Disposition,
            decision.Title,
            decision.Summary,
            decision.Explanation,
            PolicyRuleReport.Render(decision, includeExceptionAttribution: false),
            PolicyRuleReport.RenderExceptions(decision),
            jobEvidenceCount)
        {
            HasEvaluationProvenance = true,
        };
    }

    private static void RequireOrderedEvidence(ImmutableArray<EvidenceId> evidenceIds)
    {
        if (evidenceIds.IsDefault || evidenceIds.Length > PolicyEvaluator.MaximumObservations)
        {
            throw new ArgumentException("The saved evidence ID set is invalid.", nameof(evidenceIds));
        }

        string? previous = null;
        foreach (var evidenceId in evidenceIds)
        {
            if (evidenceId is null || string.CompareOrdinal(previous, evidenceId.Value) >= 0)
            {
                throw new ArgumentException("Saved evidence IDs must be unique and ordinally sorted.", nameof(evidenceIds));
            }

            previous = evidenceId.Value;
        }
    }

    private static void RequireOrderedCapabilities(ImmutableArray<string>? activeCapabilityIds)
    {
        if (activeCapabilityIds is not { } capabilities)
        {
            return;
        }

        if (capabilities.IsDefault || capabilities.Length > PolicyEvaluator.MaximumCapabilityIds)
        {
            throw new ArgumentException("The saved capability selection is invalid.", nameof(activeCapabilityIds));
        }

        string? previous = null;
        foreach (var capability in capabilities)
        {
            if (capability is null || !PolicySchema.IsCapabilityId(capability) || string.CompareOrdinal(previous, capability) >= 0)
            {
                throw new ArgumentException("Saved capability IDs must be valid, unique and ordinally sorted.", nameof(activeCapabilityIds));
            }

            previous = capability;
        }
    }

    private static string RequireId(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        try
        {
            return new EvidenceId(value).Value;
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("The saved decision or assessment identifier is invalid.", parameter);
        }
    }

    private static string RequirePolicyText(string value, Func<string, bool> validator, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        return validator(value) ? value : throw new ArgumentException("The saved policy identity is invalid.", parameter);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static DateTimeOffset RequireTimestamp(DateTimeOffset timestamp, string parameter) =>
        timestamp == default
            ? throw new ArgumentException("A decision evaluation timestamp is required.", parameter)
            : timestamp.ToUniversalTime();

    private static string RequireReportText(string value, int maximumLength, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length > maximumLength || string.IsNullOrWhiteSpace(value) ||
            value.Any(character => char.IsControl(character) && character != '\n'))
        {
            throw new ArgumentException("The saved decision report text is invalid.", parameter);
        }

        return value;
    }
}
