using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;

namespace ThirdLife.Core.HumanConfirmation;

public static class HumanConfirmationProjection
{
    private const string MappedProcedureRevision = "M1-MHT-1";

    /// <summary>
    /// Projects the latest mapped guided check per function without changing the job history.
    /// </summary>
    public static IReadOnlyList<Observation> FromJob(StoredJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var observations = new List<Observation>(6);
        foreach (var item in ManualTestService.GetProgress(job).Items)
        {
            if (item.Latest is not { Details.ProcedureRevision: MappedProcedureRevision } attempt ||
                EvidenceKeyFor(item.Case.Id) is not { } key)
            {
                continue;
            }

            var value = attempt.TestResult switch
            {
                HumanTestResult.Pass => EvidenceValue.FromEnum("passed"),
                HumanTestResult.Fail => EvidenceValue.FromEnum("failed"),
                HumanTestResult.NotRun or HumanTestResult.NotAvailable => null,
                _ => throw new InvalidOperationException("The guided check result is unsupported."),
            };
            var operatorId = attempt.Metadata.EvidenceClassification == EvidenceClassification.HumanConfirmed
                ? attempt.OperatorId
                : null;
            observations.Add(new Observation(attempt.Metadata, key, value, unit: null, attempt.LimitationCode, operatorId));
        }

        return observations.AsReadOnly();
    }

    private static EvidenceKey? EvidenceKeyFor(string caseId) => caseId switch
    {
        "MHT-002" => new EvidenceKey("function.keyboard"),
        "MHT-003" => new EvidenceKey("function.pointing"),
        "MHT-005" => new EvidenceKey("function.display"),
        "MHT-007" => new EvidenceKey("function.audio_output"),
        "MHT-008" => new EvidenceKey("function.microphone"),
        "MHT-009" => new EvidenceKey("function.camera"),
        _ => null,
    };
}
