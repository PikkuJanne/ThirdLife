using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Policy.Exceptions;

public sealed class PolicyExceptionRecord
{
    internal PolicyExceptionRecord(
        string exceptionId,
        JobId jobId,
        RequirementId requirementId,
        string policyId,
        string policyVersion,
        string policySha256,
        EvidenceId evidenceId,
        string authorizationId,
        OperatorId operatorId,
        string reason,
        DateTimeOffset recordedAtUtc,
        string? supersedesExceptionId)
    {
        ExceptionId = exceptionId;
        JobId = jobId;
        RequirementId = requirementId;
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        PolicySha256 = policySha256;
        EvidenceId = evidenceId;
        AuthorizationId = authorizationId;
        OperatorId = operatorId;
        Reason = reason;
        RecordedAtUtc = recordedAtUtc;
        SupersedesExceptionId = supersedesExceptionId;
    }

    public string ExceptionId { get; }

    public JobId JobId { get; }

    public RequirementId RequirementId { get; }

    public string PolicyId { get; }

    public string PolicyVersion { get; }

    public string PolicySha256 { get; }

    public EvidenceId EvidenceId { get; }

    public string AuthorizationId { get; }

    public OperatorId OperatorId { get; }

    // The restricted store can project this explicitly. Ordinary decision
    // serialization and diagnostic strings never contain operator prose.
    [JsonIgnore]
    public string Reason { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public string? SupersedesExceptionId { get; }

    public override string ToString() => "[policy_exception]";
}
