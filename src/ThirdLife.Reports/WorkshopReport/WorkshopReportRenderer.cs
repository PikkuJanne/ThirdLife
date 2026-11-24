using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Reports.WorkshopReport;

public static partial class WorkshopReportRenderer
{
    public const int MaximumReportCharacters = 1024 * 1024;

    private const int MaximumStoredEvidence = 10_000;
    private const string Withheld = "Withheld by the workshop schema";

    public static string Render(
        StoredJob job,
        WorkshopAssessmentSummary? latestAssessment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        if ((long)job.Observations.Count + job.SanitizationEvidence.Count + job.HumanTests.Count > MaximumStoredEvidence ||
            job.SanitizationGateDecisions.Count > MaximumStoredEvidence)
        {
            throw new InvalidOperationException("This job exceeds the workshop report evidence limit.");
        }

        if (latestAssessment is not null && (latestAssessment.JobId != job.Job.JobId ||
            !job.SanitizationGateDecisions.Any(decision =>
                decision.JobId == job.Job.JobId && decision.DecisionId == latestAssessment.GateDecisionId &&
                decision.Outcome == SanitizationGateOutcome.AllowAssessment)))
        {
            throw new ArgumentException("The assessment does not belong to this saved job.", nameof(latestAssessment));
        }

        var hasFixtureEvidence = job.Observations.Any(static item => IsFixture(item.Metadata)) ||
            job.SanitizationEvidence.Any(static item => IsFixture(item.Metadata)) ||
            job.HumanTests.Any(static item => IsFixture(item.Metadata));
        var gate = SanitizationGate.Inspect(job);
        var report = new StringBuilder();
        Line(report, "ThirdLife Setup Core - Workshop audit record");
        Line(report, "Classification: WORKSHOP_RESTRICTED");
        Line(report, "Schema: workshop-audit-v1");
        Line(report, $"Job: {SafeCode(job.Job.JobId.Value)}");
        Line(report, $"Device: {SafeCode(job.Job.DeviceId.Value)}");
        Line(report, $"Job created: {Timestamp(job.Job.CreatedAtUtc)}");
        Line(report, $"Job status: {(job.IsArchived ? "Archived" : "Active")}");
        if (hasFixtureEvidence)
        {
            Line(report, "Scope: Contains synthetic fixture evidence. It does not describe actual device collection or physical function.");
        }

        Line(report, "Readiness: Unknown. This audit gives no readiness approval.");
        Line(report, "");
        Line(report, "Assessment summary");
        WriteAssessment(report, gate, latestAssessment);
        Line(report, "");
        Line(report, "External sanitization");
        WriteSanitization(report, job, gate, cancellationToken);
        Line(report, "");
        Line(report, "Saved observations");
        var needsReview = WriteObservations(report, job, cancellationToken);
        Line(report, "");
        Line(report, "Manual check records");
        needsReview |= WriteManualTests(report, job, cancellationToken);
        Line(report, "");
        Line(report, "Next step");
        Line(report, NextStep(job, gate, latestAssessment, needsReview));
        return report.ToString();
    }

    private static void WriteAssessment(
        StringBuilder report,
        SanitizationGateStatus gate,
        WorkshopAssessmentSummary? assessment)
    {
        if (assessment is null)
        {
            Line(report, "No assessment summary is saved.");
            return;
        }

        var description = assessment.Outcome switch
        {
            WorkshopAssessmentOutcome.Incomplete => "Incomplete. The saved findings may be partial.",
            WorkshopAssessmentOutcome.Completed => "Collection completed. Review the findings.",
            WorkshopAssessmentOutcome.Cancelled => "Cancelled. Committed findings are retained.",
            WorkshopAssessmentOutcome.RequiresReview => "Requires review. Some collection may be unavailable or failed.",
            _ => throw new InvalidOperationException("The assessment outcome is invalid."),
        };
        Line(report, description);
        if (gate.Decision?.DecisionId != assessment.GateDecisionId)
        {
            Line(report, "Historical assessment under an earlier sanitization decision.");
        }

        Line(report, $"Started: {Timestamp(assessment.StartedAtUtc)}");
        Line(report, $"Finished: {Timestamp(assessment.FinishedAtUtc)}");
        foreach (var provider in assessment.UnrunProviders)
        {
            Line(report, $"- {SafeCode(provider.Value)}: Not run. No provider evidence or collection time was recorded.");
        }
    }

    private static void WriteSanitization(
        StringBuilder report, StoredJob job, SanitizationGateStatus gate, CancellationToken cancellationToken)
    {
        Line(report, $"Gate: {DescribeGate(gate.Reason)}");
        if (gate.Decision is { } decision)
        {
            Line(report, $"Gate evaluated: {Timestamp(decision.EvaluatedAtUtc)}");
        }

        if (job.SanitizationEvidence.Count == 0)
        {
            Line(report, "No sanitization evidence is saved.");
        }

        foreach (var item in job.SanitizationEvidence)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = item.SanitizationState switch
            {
                SanitizationState.Verified => "Verified external sanitization",
                SanitizationState.ReplacementStorage => "Replacement storage verified",
                SanitizationState.NoDonorStorage => "No donor storage verified",
                SanitizationState.Unknown => "Unknown",
                SanitizationState.Failed => "Failed",
                _ => throw new InvalidOperationException("The sanitization result is invalid."),
            };
            var verification = item.VerificationState switch
            {
                SanitizationVerificationState.Verified => "Verified",
                SanitizationVerificationState.Failed => "Failed",
                _ => "Not available",
            };
            Line(report, $"- {result}. Method: {SafeCode(item.MethodCode)}. Verification: {verification}.");
            WriteAttribution(report, item.Metadata, item.OperatorId);
            Line(report, $"  External event: {Timestamp(item.OccurredAtUtc)}. Policy: {SafeCode(item.PolicyVersion)}.");
            if (item.MediaIdentifier is not null)
            {
                Line(report, $"  Workshop media reference: {SafeText(item.MediaIdentifier.Value)}.");
            }
        }
    }

    private static bool WriteObservations(StringBuilder report, StoredJob job, CancellationToken cancellationToken)
    {
        int included = 0;
        int outsideSchema = 0;
        bool unresolved = false;
        foreach (var observation in job.Observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.Metadata.ProviderId.Value == "assessment-orchestrator" &&
                observation.EvidenceKey.Value is "assessment.start" or "assessment.provider" or "assessment.finish")
            {
                // The supplied summary is reconstructed through the assessment service. Its
                // internal journal envelopes are never projected as device observations.
                continue;
            }

            if (!WorkshopObservationFields.Allows(observation))
            {
                outsideSchema++;
                continue;
            }

            var value = DescribeValue(observation);
            string unit = observation.Unit is { } storedUnit && observation.Value is not null && value != Withheld
                ? $" {SafeCode(storedUnit)}" : "";
            Line(report, $"- {observation.EvidenceKey.Value}: {value}{unit}");
            WriteAttribution(report, observation.Metadata, observation.OperatorId);
            WriteLimitation(report, observation.LimitationCode);
            included++;
            unresolved |= observation.Value is null || value == Withheld || observation.LimitationCode is not null;
        }

        if (included == 0)
        {
            Line(report, "No reportable observations are saved.");
        }

        if (outsideSchema != 0)
        {
            Line(report, string.Create(CultureInfo.InvariantCulture,
                $"Records outside the workshop allowlist: {outsideSchema}. Review the saved job for details."));
        }

        return unresolved || outsideSchema != 0;
    }

    private static bool WriteManualTests(StringBuilder report, StoredJob job, CancellationToken cancellationToken)
    {
        if (job.HumanTests.Count == 0)
        {
            Line(report, "No manual results are saved.");
            return true;
        }

        var unresolved = false;
        foreach (var item in job.HumanTests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.JobId != job.Job.JobId || item.DeviceId != job.Job.DeviceId)
            {
                throw new InvalidOperationException("A saved manual result belongs to another job.");
            }

            var result = item.TestResult switch
            {
                HumanTestResult.Pass => "Pass",
                HumanTestResult.Fail => "Fail",
                HumanTestResult.NotRun => "Not run",
                HumanTestResult.NotAvailable => "Not available",
                _ => throw new InvalidOperationException("The manual result is invalid."),
            };
            var caseName = item.Details?.CaseId ?? "Legacy manual check";
            Line(report, $"- {caseName}: {result}");
            WriteAttribution(report, item.Metadata, item.OperatorId);
            Line(report, $"  Record: {SafeCode(item.HumanTestId.Value)}.");
            WriteLimitation(report, item.LimitationCode);
            if (item.Details is { } details)
            {
                Line(report, $"  Procedure: {SafeCode(details.ProcedureRevision)}.");
                if (details.RetestOf is not null)
                {
                    Line(report, $"  Retest of: {SafeCode(details.RetestOf.Value)}.");
                }
            }

            unresolved |= item.TestResult != HumanTestResult.Pass;
        }

        Line(report, "Free-form notes remain in the protected job and are omitted from this record.");
        return unresolved;
    }

    private static void WriteAttribution(StringBuilder report, EvidenceMetadata metadata, OperatorId? operatorId)
    {
        var classification = metadata.EvidenceClassification switch
        {
            EvidenceClassification.Observed => "Observed",
            EvidenceClassification.Inferred => "Inferred",
            EvidenceClassification.HumanConfirmed => "Human confirmed",
            _ => "Not available",
        };
        var origin = metadata.Provenance.Kind switch
        {
            ProvenanceKind.ProviderObservation => "Provider observation",
            ProvenanceKind.HumanConfirmation => "Human confirmation",
            ProvenanceKind.ImportedRecord => "Imported record",
            ProvenanceKind.SyntheticFixture => "Synthetic fixture",
            _ => "System generated",
        };
        var attribution = $"Provider: {SafeCode(metadata.ProviderId.Value)}";
        if (operatorId is not null)
        {
            attribution += $". Operator code: {SafeCode(operatorId.Value)}";
        }

        Line(report, $"  {classification}. {origin}. {attribution}. Recorded: {Timestamp(metadata.CollectedAtUtc)}.");
    }

    private static void WriteLimitation(StringBuilder report, string? limitation)
    {
        if (limitation is not null)
        {
            Line(report, $"  Limitation: {SafeCode(limitation)}.");
        }
    }

    private static string DescribeValue(Observation observation) => observation.Value?.Kind switch
    {
        EvidenceValueKind.Boolean => observation.Value.BooleanValue == true ? "Yes" : "No",
        EvidenceValueKind.WholeNumber => observation.Value.IntegerValue!.Value.ToString(CultureInfo.InvariantCulture),
        EvidenceValueKind.DecimalNumber => observation.Value.DecimalValue!.Value.ToString(CultureInfo.InvariantCulture),
        EvidenceValueKind.Enum => SafeCode(observation.Value.StringValue!),
        EvidenceValueKind.Text => SafeText(observation.Value.StringValue!),
        _ => observation.Metadata.ValueAvailability == ValueAvailability.NotApplicable ? "Not applicable" : "Unknown",
    };

    private static string DescribeGate(SanitizationGateReason reason) => reason switch
    {
        SanitizationGateReason.SanitizationVerified => "Read-only assessment allowed after verified external sanitization.",
        SanitizationGateReason.ReplacementStorageVerified => "Read-only assessment allowed after verified storage replacement.",
        SanitizationGateReason.NoDonorStorageVerified => "Read-only assessment allowed after confirming no donor storage.",
        SanitizationGateReason.SanitizationUnknown => "Blocked. The external result is unknown.",
        SanitizationGateReason.SanitizationFailed => "Blocked. The external result failed.",
        SanitizationGateReason.SanitizationEvidenceMissing => "Blocked. No external sanitization result is recorded.",
        SanitizationGateReason.GateDecisionMissing => "Blocked. Complete the saved sanitization gate.",
        SanitizationGateReason.GateDecisionStale => "Blocked. The saved gate predates the current external result.",
        _ => "Blocked. The job is archived.",
    };

    private static string NextStep(
        StoredJob job,
        SanitizationGateStatus gate,
        WorkshopAssessmentSummary? assessment,
        bool needsReview)
    {
        if (job.IsArchived)
        {
            return "Review the archived findings in the workshop.";
        }

        if (!gate.AllowsAssessment)
        {
            return "Review the external sanitization evidence and resolve the gate blocker before assessment.";
        }

        if (assessment is null || gate.Decision?.DecisionId != assessment.GateDecisionId)
        {
            return "Run a read-only assessment against the current allowed sanitization record when applicable.";
        }

        return assessment.Outcome != WorkshopAssessmentOutcome.Completed || needsReview
            ? "Review unresolved findings and complete applicable checks before deciding what to do with the device."
            : "Review the saved findings and check which manual checks still apply.";
    }

    private static bool IsFixture(EvidenceMetadata metadata) => metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture;

    private static string Timestamp(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "Not recorded";

    private static string SafeCode(string value) =>
        value.Length <= 128 && value.All(static character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.') && IsSafeShape(value) ? value : Withheld;

    private static string SafeText(string value) =>
        value.Length <= 128 && value.All(static character => char.IsAsciiLetterOrDigit(character) ||
            character is ' ' or '-' or '_' or '.' or '(' or ')' or ',' or '+') && IsSafeShape(value) ? value : Withheld;

    private static bool IsSafeShape(string value) =>
        !SecretShape().IsMatch(value);

    [GeneratedRegex(
        @"(?:[A-Za-z0-9]{5}-){4}[A-Za-z0-9]{5}|(?:[0-9]{6}-){7}[0-9]{6}|(?:[0-9A-Fa-f]{2}-){5}[0-9A-Fa-f]{2}|(?<![0-9.])(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9]?[0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9]?[0-9])(?![0-9.])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretShape();

    private static void Line(StringBuilder report, string value)
    {
        if (value.Length > MaximumReportCharacters - report.Length - 1)
        {
            throw new InvalidOperationException("The workshop record exceeds its output limit.");
        }

        report.Append(value).Append('\n');
    }
}
