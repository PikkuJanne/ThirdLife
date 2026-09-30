using System.Globalization;
using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Sanitization;

[JsonConverter(typeof(StableStringEnumConverter<SanitizationGateOutcome>))]
public enum SanitizationGateOutcome
{
    [JsonStringEnumMemberName("allow_assessment")]
    AllowAssessment = 1,

    [JsonStringEnumMemberName("blocked")]
    Blocked,
}

[JsonConverter(typeof(StableStringEnumConverter<SanitizationGateReason>))]
public enum SanitizationGateReason
{
    [JsonStringEnumMemberName("sanitization_verified")]
    SanitizationVerified = 1,

    [JsonStringEnumMemberName("replacement_storage_verified")]
    ReplacementStorageVerified,

    [JsonStringEnumMemberName("no_donor_storage_verified")]
    NoDonorStorageVerified,

    [JsonStringEnumMemberName("sanitization_unknown")]
    SanitizationUnknown,

    [JsonStringEnumMemberName("sanitization_failed")]
    SanitizationFailed,

    [JsonStringEnumMemberName("sanitization_evidence_missing")]
    SanitizationEvidenceMissing,

    [JsonStringEnumMemberName("gate_decision_missing")]
    GateDecisionMissing,

    [JsonStringEnumMemberName("gate_decision_stale")]
    GateDecisionStale,

    [JsonStringEnumMemberName("job_archived")]
    JobArchived,

}

[JsonConverter(typeof(SanitizationGateDecisionIdJsonConverter))]
public sealed record SanitizationGateDecisionId
{
    public SanitizationGateDecisionId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static SanitizationGateDecisionId New() =>
        new(string.Concat("gate-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SanitizationGateDecision
{
    [JsonConstructor]
    public SanitizationGateDecision(
        SanitizationGateDecisionId decisionId,
        JobId jobId,
        EvidenceId evidenceId,
        string policyVersion,
        SanitizationGateOutcome outcome,
        SanitizationGateReason reason,
        DateTimeOffset evaluatedAtUtc)
    {
        DecisionId = decisionId ?? throw new ArgumentNullException(nameof(decisionId));
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        EvidenceId = evidenceId ?? throw new ArgumentNullException(nameof(evidenceId));
        PolicyVersion = DomainValue.RequireVersion(policyVersion, nameof(policyVersion));
        Outcome = DomainValue.RequireDefined(outcome, nameof(outcome));
        Reason = DomainValue.RequireDefined(reason, nameof(reason));
        EvaluatedAtUtc = DomainValue.RequireTimestamp(evaluatedAtUtc, nameof(evaluatedAtUtc));

        if (!IsEvidenceDecision(outcome, reason))
        {
            throw new ArgumentException(
                "A persisted sanitization decision must use an evidence-derived outcome and reason.",
                nameof(reason));
        }
    }

    [JsonPropertyName("decision_id")]
    public SanitizationGateDecisionId DecisionId { get; }

    [JsonPropertyName("job_id")]
    public JobId JobId { get; }

    [JsonPropertyName("evidence_id")]
    public EvidenceId EvidenceId { get; }

    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; }

    [JsonPropertyName("outcome")]
    public SanitizationGateOutcome Outcome { get; }

    [JsonPropertyName("reason")]
    public SanitizationGateReason Reason { get; }

    [JsonPropertyName("evaluated_at_utc")]
    public DateTimeOffset EvaluatedAtUtc { get; }

    internal static bool IsEvidenceDecision(
        SanitizationGateOutcome outcome,
        SanitizationGateReason reason) =>
        (outcome, reason) is
            (SanitizationGateOutcome.AllowAssessment, SanitizationGateReason.SanitizationVerified) or
            (SanitizationGateOutcome.AllowAssessment, SanitizationGateReason.ReplacementStorageVerified) or
            (SanitizationGateOutcome.AllowAssessment, SanitizationGateReason.NoDonorStorageVerified) or
            (SanitizationGateOutcome.Blocked, SanitizationGateReason.SanitizationUnknown) or
            (SanitizationGateOutcome.Blocked, SanitizationGateReason.SanitizationFailed);
}

public sealed record SanitizationGateStatus
{
    public SanitizationGateStatus(
        SanitizationGateOutcome outcome,
        SanitizationGateReason reason,
        EvidenceId? evidenceId,
        SanitizationGateDecision? decision)
    {
        Outcome = DomainValue.RequireDefined(outcome, nameof(outcome));
        Reason = DomainValue.RequireDefined(reason, nameof(reason));
        EvidenceId = evidenceId;
        Decision = decision;

        if (outcome == SanitizationGateOutcome.AllowAssessment && decision is null)
        {
            throw new ArgumentException("Assessment access requires a persisted gate decision.", nameof(decision));
        }

        if (decision is not null &&
            (evidenceId is null ||
             evidenceId != decision.EvidenceId ||
             decision.Outcome != outcome ||
             decision.Reason != reason))
        {
            throw new ArgumentException(
                "The gate status must agree with its persisted decision and evidence reference.",
                nameof(decision));
        }
    }

    public SanitizationGateOutcome Outcome { get; }

    public SanitizationGateReason Reason { get; }

    public EvidenceId? EvidenceId { get; }

    public SanitizationGateDecision? Decision { get; }

    public bool AllowsAssessment => Outcome == SanitizationGateOutcome.AllowAssessment;
}

public static class SanitizationGate
{
    public static SanitizationGateDecision Evaluate(
        JobId jobId,
        SanitizationEvidence evidence,
        DateTimeOffset evaluatedAtUtc,
        SanitizationGateDecisionId? decisionId = null)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(evidence);

        var (outcome, reason) = Map(evidence);
        return new SanitizationGateDecision(
            decisionId ?? SanitizationGateDecisionId.New(),
            jobId,
            evidence.Metadata.EvidenceId,
            evidence.PolicyVersion,
            outcome,
            reason,
            evaluatedAtUtc);
    }

    public static SanitizationGateStatus Inspect(StoredJob storedJob)
    {
        ArgumentNullException.ThrowIfNull(storedJob);

        var latestEvidence = storedJob.SanitizationEvidence.Count == 0
            ? null
            : storedJob.SanitizationEvidence[storedJob.SanitizationEvidence.Count - 1];
        if (storedJob.IsArchived)
        {
            return Blocked(
                SanitizationGateReason.JobArchived,
                latestEvidence?.Metadata.EvidenceId);
        }

        if (latestEvidence is null)
        {
            return Blocked(SanitizationGateReason.SanitizationEvidenceMissing, evidenceId: null);
        }

        var latestDecision = storedJob.SanitizationGateDecisions.Count == 0
            ? null
            : storedJob.SanitizationGateDecisions[storedJob.SanitizationGateDecisions.Count - 1];
        if (latestDecision is null)
        {
            return Blocked(
                SanitizationGateReason.GateDecisionMissing,
                latestEvidence.Metadata.EvidenceId);
        }

        if (latestDecision.JobId != storedJob.Job.JobId ||
            latestDecision.EvidenceId != latestEvidence.Metadata.EvidenceId ||
            !IsConsistent(latestDecision, latestEvidence))
        {
            return Blocked(
                SanitizationGateReason.GateDecisionStale,
                latestEvidence.Metadata.EvidenceId);
        }

        return new SanitizationGateStatus(
            latestDecision.Outcome,
            latestDecision.Reason,
            latestDecision.EvidenceId,
            latestDecision);
    }

    public static bool IsConsistent(
        SanitizationGateDecision decision,
        SanitizationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(evidence);

        var (outcome, reason) = Map(evidence);
        return decision.EvidenceId == evidence.Metadata.EvidenceId &&
               string.Equals(decision.PolicyVersion, evidence.PolicyVersion, StringComparison.Ordinal) &&
               decision.Outcome == outcome &&
               decision.Reason == reason;
    }

    private static SanitizationGateStatus Blocked(
        SanitizationGateReason reason,
        EvidenceId? evidenceId) =>
        new(SanitizationGateOutcome.Blocked, reason, evidenceId, decision: null);

    private static (SanitizationGateOutcome Outcome, SanitizationGateReason Reason) Map(
        SanitizationEvidence evidence)
    {
        return DomainValue.RequireDefined(evidence.SanitizationState, nameof(evidence)) switch
        {
            SanitizationState.Verified =>
                (SanitizationGateOutcome.AllowAssessment, SanitizationGateReason.SanitizationVerified),
            SanitizationState.ReplacementStorage =>
                (SanitizationGateOutcome.AllowAssessment, SanitizationGateReason.ReplacementStorageVerified),
            SanitizationState.NoDonorStorage =>
                (SanitizationGateOutcome.AllowAssessment, SanitizationGateReason.NoDonorStorageVerified),
            SanitizationState.Unknown =>
                (SanitizationGateOutcome.Blocked, SanitizationGateReason.SanitizationUnknown),
            SanitizationState.Failed =>
                (SanitizationGateOutcome.Blocked, SanitizationGateReason.SanitizationFailed),
            _ => throw new ArgumentOutOfRangeException(nameof(evidence)),
        };
    }
}

internal sealed class SanitizationGateDecisionIdJsonConverter : StringValueJsonConverter<SanitizationGateDecisionId>
{
    protected override SanitizationGateDecisionId Create(string value) => new(value);

    protected override string GetValue(SanitizationGateDecisionId value) => value.Value;
}
