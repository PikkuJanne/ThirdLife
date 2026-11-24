using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ThirdLife.Policy.Evaluation;

namespace ThirdLife.Policy.Decisions;

public static class PolicyRuleReport
{
    private const int MaximumEvidenceIdsPerRule = 3;

    public static string Render(DispositionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var report = new StringBuilder("Policy rule details");
        foreach (var detail in decision.RuleDetails)
        {
            var source = detail.Source switch
            {
                DispositionSource.CoreTarget => "Core rule ",
                DispositionSource.OrganizationPolicy => "Policy rule ",
                _ => throw new InvalidOperationException("The rule report source is invalid."),
            };
            report.Append("\n- ").Append(source).Append(detail.Outcome.Rule.Requirement.RequirementId.Value)
                .Append(". ").Append(detail.StatusLabel).Append('.');
            AppendEvidence(report, detail.Outcome.Evidence);

            if (detail.Exception is { } exception)
            {
                report.Append("\n  Exception record ").Append(exception.ExceptionId)
                    .Append(". Authorization ").Append(exception.AuthorizationId)
                    .Append(". Operator code ").Append(exception.OperatorId.Value)
                    .Append(". Recorded ").Append(exception.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
                    .Append('.');
            }
        }

        return report.ToString();
    }

    private static void AppendEvidence(StringBuilder report, ImmutableArray<PolicyEvidenceReference> evidence)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            report.Append(" No saved evidence is linked to this check.");
            return;
        }

        report.Append(evidence.Length == 1 ? " Evidence ID " : " Evidence IDs ")
            .AppendJoin(", ", evidence.Take(MaximumEvidenceIdsPerRule).Select(reference => reference.EvidenceId.Value))
            .Append('.');
        var omitted = evidence.Length - MaximumEvidenceIdsPerRule;
        if (omitted > 0)
        {
            report.Append(' ').Append(omitted.ToString(CultureInfo.InvariantCulture))
                .Append(omitted == 1
                    ? " more evidence record is linked to this check."
                    : " more evidence records are linked to this check.");
        }
    }
}
