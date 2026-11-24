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
        HumanTestId? retestOf = null)
    {
        CaseId = DomainValue.RequireCode(caseId, nameof(caseId));
        if (CaseId.Length != 7 || !CaseId.StartsWith("MHT-", StringComparison.Ordinal) ||
            !char.IsAsciiDigit(CaseId[4]) || !char.IsAsciiDigit(CaseId[5]) || !char.IsAsciiDigit(CaseId[6]))
        {
            throw new ArgumentException("A guided check requires a stable manual-test case ID.", nameof(caseId));
        }

        ProcedureRevision = DomainValue.RequireVersion(procedureRevision, nameof(procedureRevision));
        Notes = DomainValue.RequireText(notes, nameof(notes), ManualTestService.MaximumNotesLength);
        RetestOf = retestOf;
    }

    [JsonPropertyName("case_id")]
    public string CaseId { get; }

    [JsonPropertyName("procedure_revision")]
    public string ProcedureRevision { get; }

    [JsonPropertyName("notes")]
    public string Notes { get; }

    [JsonPropertyName("retest_of")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HumanTestId? RetestOf { get; }
}
