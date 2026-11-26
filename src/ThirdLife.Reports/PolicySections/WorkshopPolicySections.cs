using System.Text;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Reports.WorkshopReport;

public static partial class WorkshopReportRenderer
{
    public static string RenderWithPolicy(
        StoredJob job,
        WorkshopAssessmentSummary assessment,
        StoredPolicyDecision savedDecision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(savedDecision);
        return RenderCore(job, assessment, savedDecision, cancellationToken: cancellationToken);
    }

    public static string SafePolicyPresentation(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumReportCharacters)
        {
            throw new InvalidOperationException("The policy presentation exceeds its output limit.");
        }

        var result = new StringBuilder(value.Length);
        var firstLine = true;
        foreach (var range in value.AsSpan().Split('\n'))
        {
            if (!firstLine)
            {
                result.Append('\n');
            }

            firstLine = false;
            var line = value.AsSpan()[range];
            var allowed = true;
            foreach (var character in line)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not (' ' or '-' or '_' or '.' or ',' or ':' or '+' or '(' or ')' or '\''))
                {
                    allowed = false;
                    break;
                }
            }

            result.Append(allowed ? SecretShape().Replace(line.ToString(), Withheld) : Withheld);
            if (result.Length > MaximumReportCharacters)
            {
                throw new InvalidOperationException("The policy presentation exceeds its output limit.");
            }
        }

        return result.ToString();
    }

    private static void ValidatePolicyDecision(
        StoredJob job, WorkshopAssessmentSummary assessment, StoredPolicyDecision savedDecision)
    {
        ArgumentNullException.ThrowIfNull(savedDecision.Decision);
        ArgumentNullException.ThrowIfNull(savedDecision.Snapshot);
        var decision = savedDecision.Decision;
        var policy = savedDecision.Snapshot.Document;
        if (assessment.AssessmentId is null || decision.AssessmentId != assessment.AssessmentId ||
            decision.JobId != job.Job.JobId || assessment.JobId != job.Job.JobId ||
            decision.EvaluatedAtUtc < (assessment.FinishedAtUtc ?? assessment.StartedAtUtc) ||
            decision.PolicyId != policy.PolicyId || decision.PolicyVersion != policy.Version ||
            decision.PolicySha256 != policy.ContentSha256)
        {
            throw new ArgumentException("The saved decision does not belong to this assessment and policy.", nameof(savedDecision));
        }

        var evidenceIds = job.Observations.Select(item => item.Metadata.EvidenceId)
            .Concat(job.SanitizationEvidence.Select(item => item.Metadata.EvidenceId))
            .Concat(job.HumanTests.Where(item => item.JobId == job.Job.JobId && item.DeviceId == job.Job.DeviceId)
                .Select(item => item.Metadata.EvidenceId)).ToHashSet();
        if (decision.EvidenceIds.Any(id => !evidenceIds.Contains(id)))
        {
            throw new ArgumentException("The saved decision references evidence outside this job.", nameof(savedDecision));
        }
    }

    private static void WritePolicySections(StringBuilder report, StoredPolicyDecision savedDecision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = savedDecision.Decision;
        Line(report, "Policy");
        Line(report, $"Policy ID {SafeCode(decision.PolicyId)}.");
        Line(report, $"Version {SafeCode(decision.PolicyVersion)}.");
        Line(report, $"SHA-256 {decision.PolicySha256}.");
        var policy = savedDecision.Snapshot.Document;
        Line(report, !policy.IsCurrentSchema
            ? "Historical policy evaluation. Use the current policy for a new preparation decision."
            : policy.Status == PolicyStatus.Candidate
            ? "Candidate policy evaluation for workshop review."
            : "Saved effective policy evaluation.");
        Line(report, $"Evaluated {Timestamp(decision.EvaluatedAtUtc)}.");
        Line(report, "");
        Line(report, SafePolicyPresentation(decision.RuleReport));
        Line(report, "");
        Line(report, "Exceptions");
        Line(report, decision.ExceptionReport is { } exceptions ? SafePolicyPresentation(exceptions) :
            "The saved rule details include any exception statuses and records.");
        Line(report, "");
        Line(report, "Captured disposition");
        if (!policy.IsCurrentSchema)
        {
            Line(report, "Original recorded disposition and explanation follow.");
        }

        Line(report, SafePolicyPresentation(decision.Title));
        Line(report, SafePolicyPresentation(decision.Explanation));
    }
}
