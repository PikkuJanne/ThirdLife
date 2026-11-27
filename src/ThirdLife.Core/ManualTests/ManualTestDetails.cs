using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Core.ManualTests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManualTestDetails
{
    [JsonConstructor]
    public ManualTestDetails(
        string caseId,
        string procedureRevision,
        string notes,
        HumanTestId? retestOf = null,
        string? assessmentId = null)
    {
        CaseId = DomainValue.RequireCode(caseId, nameof(caseId));
        if (ManualTestCatalog.IsSecurityReview(CaseId))
        {
            AssessmentId = DomainValue.RequireIdentifier(assessmentId, nameof(assessmentId));
            if (notes != string.Empty)
            {
                throw new ArgumentException("Security reviews accept only structured results.", nameof(notes));
            }

            Notes = string.Empty;
        }
        else
        {
            if (CaseId.Length != 7 || !CaseId.StartsWith("MHT-", StringComparison.Ordinal) ||
                !char.IsAsciiDigit(CaseId[4]) || !char.IsAsciiDigit(CaseId[5]) || !char.IsAsciiDigit(CaseId[6]))
            {
                throw new ArgumentException("A guided check requires a stable manual-test case ID.", nameof(caseId));
            }

            if (assessmentId is not null)
            {
                throw new ArgumentException("Only a security review carries an assessment reference.", nameof(assessmentId));
            }

            Notes = DomainValue.RequireText(notes, nameof(notes), ManualTestService.MaximumNotesLength);
        }

        ProcedureRevision = DomainValue.RequireVersion(procedureRevision, nameof(procedureRevision));
        RetestOf = retestOf;
    }

    [JsonPropertyName("case_id")]
    public string CaseId { get; }

    [JsonPropertyName("procedure_revision")]
    public string ProcedureRevision { get; }

    [JsonPropertyName("notes")]
    public string Notes { get; }

    [JsonPropertyName("assessment_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssessmentId { get; }

    [JsonPropertyName("retest_of")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HumanTestId? RetestOf { get; }
}
