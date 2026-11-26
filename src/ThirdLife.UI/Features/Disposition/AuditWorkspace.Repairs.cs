using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Repairs;
using ThirdLife.Policy.Schema;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.Disposition;

namespace ThirdLife.UI.Shell;

internal sealed record SavedRepairChoice(EvidenceId RepairId, string Caption);

internal sealed partial class AuditWorkspace
{
    private PolicyDeviceClass? _repairDeviceClass;

    public PolicyDeviceClass? RepairDeviceClass
    {
        get => _repairDeviceClass;
        set
        {
            if (_repairDeviceClass == value || !CanEditJob)
            {
                return;
            }

            _repairDeviceClass = value;
            ClearPlanReview();
            RepairCandidates = RepairSuggestions.For(Assessment, value);
            Publish();
        }
    }

    public string RepairPolicyStatus => RepairDeviceClass switch
    {
        null => "Select the device class to review the available candidate thresholds.",
        { } selected when WorkshopPolicyCandidate.ForDeviceClass(selected) is null =>
            "A candidate policy is unavailable for this device class.",
        _ => "Candidate policy thresholds are shown for workshop review.",
    };

    public IReadOnlyList<RepairSuggestion> RepairCandidates { get; private set; } = [];

    public IReadOnlyList<SavedRepairChoice> SavedRepairs { get; private set; } = [];

    public IReadOnlyList<string> RepairHistory { get; private set; } = [];

    public bool CanRecordRepair => CanEditJob && RepairCandidates.Count > 0;

    public bool CanStartRetest => CanAssess && SavedRepairs.Count > 0;

    public string? RepairRecordRestriction => JobEditRestriction ??
        (RepairCandidates.Count == 0 ? "No saved finding is eligible for a repair record." : null);

    public string? RepairRetestRestriction => JobEditRestriction ??
        (SavedRepairs.Count == 0 ? "Record completed work before starting a linked retest."
            : !CanAssess ? GateStatus : null);

    private string? JobEditRestriction => CanEditJob ? null : Job switch
    {
        null => "Open a workshop job to continue.",
        { IsArchived: true } => "This job is archived. Its saved records are read-only.",
        _ when _hasSeparateEvidenceOrigin => GateStatus,
        _ => "Wait for the active job operation to finish.",
    };

    public Task RecordRepairAsync(RepairSuggestion suggestion, RepairAction action, string operatorCode) => RunAsync(async () =>
    {
        var job = RequireWritableJob();
        if (Assessment is not { } previous || previous.JobId != job.Job.JobId || !RepairCandidates.Contains(suggestion))
        {
            throw new InvalidOperationException("Select a repair finding from the current saved assessment.");
        }

        var service = new RepairRetestService(_store!, _timeProvider);
        await Task.Run(() => service.RecordAsync(job.Job.JobId, previous.AssessmentId.Value, suggestion.FailedEvidenceId,
            suggestion.Component, action, new OperatorId(operatorCode.Trim())));
        await ApplyJobAsync(await LoadJobAsync(job.Job.JobId));
        ClearReport();
        Status = suggestion.Component == RepairComponent.SystemStorage && action == RepairAction.Replaced
            ? "Storage replacement recorded. Save verified replacement-storage evidence before starting a linked retest."
            : "Repair recorded. Select it to start a linked read-only retest.";
    });

    public Task StartRetestAsync(EvidenceId repairId) => StartAssessmentAsync(repairId);

    private void ClearRepairs()
    {
        _repairDeviceClass = null;
        RepairCandidates = [];
        SavedRepairs = [];
        RepairHistory = [];
    }

    private void ApplyRepairs(IReadOnlyList<RepairRecord> repairs, IReadOnlyList<RepairRetestLink> retests)
    {
        RepairCandidates = RepairSuggestions.For(Assessment, RepairDeviceClass);
        if (repairs.Count == 0)
        {
            SavedRepairs = [];
            RepairHistory = [];
            return;
        }

        var failedIds = repairs.Select(repair => repair.FailedEvidenceId).ToHashSet();
        var originalFindings = Job!.Observations
            .Where(observation => failedIds.Contains(observation.Metadata.EvidenceId))
            .ToDictionary(observation => observation.Metadata.EvidenceId);
        var choices = repairs.Reverse().Select(repair =>
            new SavedRepairChoice(repair.RepairId, DescribeRepair(repair, originalFindings[repair.FailedEvidenceId]))).ToArray();
        SavedRepairs = Array.AsReadOnly(choices);
        var retestsByRepair = retests.ToLookup(retest => retest.RepairId);
        RepairHistory = Array.AsReadOnly(choices.SelectMany(repair =>
            new[] { repair.Caption }.Concat(retestsByRepair[repair.RepairId].Select(retest =>
                WorkshopReportRenderer.SafePolicyPresentation(
                    $"Linked retest started for assessment {retest.AssessmentId}. Prior failure evidence {retest.PriorFailureEvidenceId.Value}.")))).ToArray());
    }

    private static string DescribeRepair(RepairRecord repair, Observation originalFinding)
    {
        var action = repair.Action == RepairAction.Replaced ? "replacement recorded" : "repair recorded";
        var time = repair.RecordedAtUtc.UtcDateTime.ToString("u", CultureInfo.InvariantCulture);
        var evidenceValue = originalFinding.Value switch
        {
            { BooleanValue: false } => "absent",
            { IntegerValue: { } amount } =>
                string.Create(CultureInfo.InvariantCulture, $"{amount} {originalFinding.Unit}").TrimEnd(),
            { StringValue: { } token } => token.Replace('_', ' '),
            _ => "recorded",
        };
        return WorkshopReportRenderer.SafePolicyPresentation(
            $"{RepairSuggestions.ComponentName(repair.Component)} {action}. Operator {repair.OperatorId.Value}. " +
            $"{time}. Prior finding {originalFinding.EvidenceKey.Value} was {evidenceValue}. Evidence {repair.FailedEvidenceId.Value}.");
    }
}
