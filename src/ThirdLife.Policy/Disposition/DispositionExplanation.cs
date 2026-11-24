using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Evaluation;

namespace ThirdLife.Policy.Decisions;

internal static class DispositionExplanation
{
    private const int MaximumPreviewRules = 4;
    private const int MaximumPreviewEvidence = 3;

    internal static (string Title, string Summary, string Explanation) Describe(
        Disposition disposition,
        ImmutableArray<DispositionCitation> citations)
    {
        var (title, summary) = disposition switch
        {
            Disposition.ReadyToPrepare => ("Ready to prepare",
                "The required checks passed for this assessment."),
            Disposition.RepairAndRetest => ("Repair and retest",
                "A required check failed. Record the repair and retest the affected checks."),
            Disposition.HumanReviewRequired => ("Human review required",
                "Review the cited findings before proceeding."),
            Disposition.AlternativeOperatingSystemCandidate => ("Alternative operating system candidate",
                "A Windows preparation requirement failed. Consider a supported alternative operating system."),
            Disposition.DoNotDeploy => ("Do not deploy",
                "A blocking requirement failed. Stop deployment and review the cited findings."),
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

        var explanation = new StringBuilder(summary);
        foreach (var citation in citations.Take(MaximumPreviewRules))
        {
            AppendCitation(explanation, citation);
        }

        var omittedRules = citations.Length - MaximumPreviewRules;
        if (omittedRules > 0)
        {
            explanation.Append('\n').Append(omittedRules.ToString(CultureInfo.InvariantCulture))
                .Append(omittedRules == 1
                    ? " more controlling rule is listed in the details."
                    : " more controlling rules are listed in the details.");
        }

        return (title, summary, explanation.ToString());
    }

    private static void AppendCitation(StringBuilder explanation, DispositionCitation citation)
    {
        var source = citation.Source switch
        {
            DispositionSource.CoreTarget => "Core rule ",
            DispositionSource.OrganizationPolicy => "Policy rule ",
            _ => throw new ArgumentOutOfRangeException(nameof(citation), "The citation source is invalid."),
        };
        explanation.Append('\n').Append(source).Append(citation.RuleId.Value)
            .Append(' ').Append(DescribeReason(citation.Reason));

        if (citation.Evidence.IsDefaultOrEmpty)
        {
            explanation.Append(" No saved evidence is linked to this check.");
            return;
        }

        explanation.Append(citation.Evidence.Length == 1 ? " Evidence ID " : " Evidence IDs ")
            .AppendJoin(", ", citation.Evidence.Take(MaximumPreviewEvidence).Select(reference => reference.EvidenceId.Value))
            .Append('.');
        var omittedEvidence = citation.Evidence.Length - MaximumPreviewEvidence;
        if (omittedEvidence > 0)
        {
            explanation.Append(' ').Append(omittedEvidence.ToString(CultureInfo.InvariantCulture))
                .Append(omittedEvidence == 1
                    ? " more evidence record is linked to this check."
                    : " more evidence records are linked to this check.");
        }
    }

    private static string DescribeReason(PolicyRuleReason reason) => reason switch
    {
        PolicyRuleReason.ConditionMet => "passed.",
        PolicyRuleReason.ConditionUnmet => "failed.",
        PolicyRuleReason.EvidenceMissing => "has no saved observation.",
        PolicyRuleReason.EvidenceUnavailable => "has unavailable evidence.",
        PolicyRuleReason.EvidenceConflict => "has conflicting evidence.",
        PolicyRuleReason.EvidenceStale => "requires current evidence.",
        PolicyRuleReason.EvidenceTypeOrUnitMismatch => "has evidence in a different format or unit.",
        PolicyRuleReason.EvidenceValueUnrecognized => "has an unrecognized evidence value.",
        PolicyRuleReason.HumanConfirmationRequired => "requires human confirmation.",
        PolicyRuleReason.CapabilitySelectionUnavailable => "needs the selected capabilities.",
        PolicyRuleReason.CapabilityInactive => "is inactive for the selected capabilities.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
