using ThirdLife.Core.Evidence;
using ThirdLife.Core.Repairs;
using ThirdLife.Inventory;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.UI.Features.Disposition;

internal sealed record RepairSuggestion(RepairComponent Component, EvidenceId FailedEvidenceId, string Caption);

internal static class RepairSuggestions
{
    public static IReadOnlyList<RepairSuggestion> For(AssessmentSnapshot? snapshot, PolicyDeviceClass? deviceClass)
    {
        if (snapshot is null || snapshot.Outcome is AssessmentOutcome.Incomplete or AssessmentOutcome.Cancelled)
        {
            return [];
        }

        var suggestions = new Dictionary<EvidenceId, RepairSuggestion>();
        if (deviceClass is { } selected && WorkshopPolicyCandidate.ForDeviceClass(selected) is { } policy)
        {
            var evaluation = PolicyEvaluator.Evaluate(policy.Document, snapshot.Observations, activeCapabilityIds: null);
            foreach (var rule in evaluation.Outcomes)
            {
                if (rule.State != PolicyRuleState.Unmet || rule.Evidence.Length != 1 ||
                    rule.Rule.Requirement.RequirementType != RequirementType.Repairable)
                {
                    continue;
                }

                var component = rule.Rule.Requirement.EvidenceKey.Value switch
                {
                    "battery.present" or "battery.full_charge_ratio_percent" => RepairComponent.Battery,
                    "storage.system.capacity_bytes" or "storage.system.free_bytes" or "storage.system.health_state" => RepairComponent.SystemStorage,
                    _ => (RepairComponent?)null,
                };
                if (component is { } affected)
                {
                    var id = rule.Evidence[0].EvidenceId;
                    var caption = WorkshopReportRenderer.SafePolicyPresentation(
                        $"{ComponentName(affected)} candidate check needs repair and retest. Evidence {id.Value}.");
                    suggestions.TryAdd(id, new(affected, id, caption));
                }
            }
        }

        foreach (var observation in snapshot.Observations)
        {
            if (observation.EvidenceKey.Value != "storage.physical_disk.reported_health" ||
                observation.Metadata.ValueAvailability != ValueAvailability.Available || observation.LimitationCode is not null ||
                observation.Value is not { Kind: EvidenceValueKind.Enum, StringValue: "warning" or "unhealthy" })
            {
                continue;
            }

            var id = observation.Metadata.EvidenceId;
            var caption = WorkshopReportRenderer.SafePolicyPresentation(
                $"Review the reported storage health and retest after completed work. Evidence {id.Value}.");
            suggestions.TryAdd(id, new(RepairComponent.SystemStorage, id, caption));
        }

        return Array.AsReadOnly(suggestions.Values
            .OrderBy(suggestion => suggestion.FailedEvidenceId.Value, StringComparer.Ordinal)
            .ToArray());
    }

    public static string ComponentName(RepairComponent component) => component switch
    {
        RepairComponent.Battery => "Battery",
        RepairComponent.SystemStorage => "Storage",
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
}
