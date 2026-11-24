using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;

namespace ThirdLife.Policy.Decisions;

public static class PolicyRuleReport
{
    private const int MaximumEvidenceIdsPerRule = 3;

    public static string Render(DispositionDecision decision, bool includeExceptionAttribution = true)
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

            if (includeExceptionAttribution && detail.Exception is { } exception)
            {
                report.Append("\n  ");
                AppendExceptionAttribution(report, exception);
            }
        }

        return report.ToString();
    }

    public static string RenderExceptions(DispositionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var report = new StringBuilder();
        foreach (var detail in decision.RuleDetails)
        {
            if (detail.Exception is not { } exception)
            {
                continue;
            }

            if (report.Length > 0)
            {
                report.Append('\n');
            }

            report.Append("- Policy rule ").Append(detail.Outcome.Rule.Requirement.RequirementId.Value)
                .Append(". Evidence ID ").Append(exception.EvidenceId.Value).Append(". ");
            AppendExceptionAttribution(report, exception);
        }

        return report.Length == 0 ? "No authorized exceptions were applied." : report.ToString();
    }

    private static void AppendExceptionAttribution(StringBuilder report, PolicyExceptionRecord exception)
    {
        report.Append("Exception record ").Append(exception.ExceptionId)
            .Append(". Authorization ").Append(exception.AuthorizationId)
            .Append(". Operator code ").Append(exception.OperatorId.Value)
            .Append(". Recorded ").Append(exception.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            .Append('.');
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
