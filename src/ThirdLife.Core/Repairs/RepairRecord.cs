using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Repairs;

[JsonConverter(typeof(StableStringEnumConverter<RepairComponent>))]
public enum RepairComponent
{
    [JsonStringEnumMemberName("battery")]
    Battery = 1,

    [JsonStringEnumMemberName("system_storage")]
    SystemStorage,
}

[JsonConverter(typeof(StableStringEnumConverter<RepairAction>))]
public enum RepairAction
{
    [JsonStringEnumMemberName("repaired")]
    Repaired = 1,

    [JsonStringEnumMemberName("replaced")]
    Replaced,
}

public sealed record RepairRecord(
    [property: JsonPropertyName("repair_id")] EvidenceId RepairId,
    [property: JsonPropertyName("job_id")] JobId JobId,
    [property: JsonPropertyName("prior_assessment_id")] string PriorAssessmentId,
    [property: JsonPropertyName("failed_evidence_id")] EvidenceId FailedEvidenceId,
    [property: JsonPropertyName("evidence_key")] EvidenceKey EvidenceKey,
    [property: JsonPropertyName("component")] RepairComponent Component,
    [property: JsonPropertyName("action")] RepairAction Action,
    [property: JsonPropertyName("operator_id")] OperatorId OperatorId,
    [property: JsonPropertyName("recorded_at_utc")] DateTimeOffset RecordedAtUtc,
    [property: JsonPropertyName("gate_decision_id_at_recording")] SanitizationGateDecisionId? GateDecisionIdAtRecording,
    [property: JsonPropertyName("sanitization_invalidation_evidence_id")] EvidenceId? SanitizationInvalidationEvidenceId);

public sealed record RepairRetestLink(
    [property: JsonPropertyName("retest_id")] EvidenceId RetestId,
    [property: JsonPropertyName("repair_id")] EvidenceId RepairId,
    [property: JsonPropertyName("prior_failure_evidence_id")] EvidenceId PriorFailureEvidenceId,
    [property: JsonPropertyName("assessment_id")] string AssessmentId,
    [property: JsonPropertyName("gate_decision_id")] SanitizationGateDecisionId GateDecisionId,
    [property: JsonPropertyName("started_at_utc")] DateTimeOffset StartedAtUtc);
