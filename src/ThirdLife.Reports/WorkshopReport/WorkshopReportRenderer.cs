using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Repairs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Reports.WorkshopReport;

public static partial class WorkshopReportRenderer
{
    public const int MaximumReportCharacters = 1024 * 1024;

    private const int MaximumStoredEvidence = 10_000;
    private const string Withheld = "Withheld by the workshop schema";

    public static string Render(
        StoredJob job,
        WorkshopAssessmentSummary? latestAssessment = null,
        CancellationToken cancellationToken = default) => RenderCore(job, latestAssessment, null, cancellationToken: cancellationToken);

    private static string RenderCore(
        StoredJob job,
        WorkshopAssessmentSummary? latestAssessment,
        StoredPolicyDecision? savedDecision,
        IReadOnlyList<WorkshopPackageRecord>? packages = null,
        IReadOnlyList<WorkshopPreparationPlan>? preparation = null,
        WorkshopProfileVerification? profileVerification = null,
        WorkshopRecordDetails? technicalRecord = null,
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

        if (savedDecision is not null)
        {
            ValidatePolicyDecision(job, latestAssessment!, savedDecision);
        }

        var hasFixtureEvidence = job.Observations.Any(static item => IsFixture(item.Metadata)) ||
            job.SanitizationEvidence.Any(static item => IsFixture(item.Metadata)) ||
            job.HumanTests.Any(static item => IsFixture(item.Metadata));
        var gate = SanitizationGate.Inspect(job);
        var report = new StringBuilder();
        var showEvidenceIds = savedDecision is not null || technicalRecord is not null;
        Line(report, technicalRecord is not null ? "ThirdLife Setup Core - Technical workshop record" :
            showEvidenceIds ? "ThirdLife Setup Core - Workshop disposition record" : "ThirdLife Setup Core - Workshop audit record");
        Line(report, "Classification: WORKSHOP_RESTRICTED");
        Line(report, technicalRecord is not null ? "Schema: workshop-technical-v3" :
            showEvidenceIds ? "Schema: workshop-disposition-v2" : "Schema: workshop-audit-v1");
        Line(report, $"Job: {SafeCode(job.Job.JobId.Value)}");
        Line(report, $"Device: {SafeCode(job.Job.DeviceId.Value)}");
        Line(report, $"Job created: {Timestamp(job.Job.CreatedAtUtc)}");
        Line(report, $"Job status: {(job.IsArchived ? "Archived" : "Active")}");
        if (hasFixtureEvidence)
        {
            Line(report, "Scope: Contains synthetic fixture evidence. It does not describe actual device collection or physical function.");
        }

        if (savedDecision is null)
        {
            Line(report, "Readiness: Unknown. This audit gives no readiness approval.");
        }

        if (technicalRecord is not null)
        {
            WriteRecordProvenance(report, job.Job.JobId, technicalRecord.Plans, cancellationToken);
        }

        Line(report, "");
        Line(report, "Assessment summary");
        WriteAssessment(report, gate, latestAssessment);
        Line(report, "");
        Line(report, "External sanitization");
        WriteSanitization(report, job, gate, showEvidenceIds, cancellationToken);
        Line(report, "");
        Line(report, "Saved observations");
        var needsReview = WriteObservations(report, job, showEvidenceIds, technicalRecord is not null, cancellationToken);
        if (job.Observations.Any(item => item.Metadata.ProviderId.Value == "windows-security-baseline"))
        {
            Line(report, "Security Center reports firewall and antivirus category health. Defender service, real-time protection and signature health have a separate finding.");
            Line(report, "The time client setting records configuration. Windows Time supplies the last successful synchronization when available.");
            Line(report, "The Remote Desktop setting covers incoming Windows Remote Desktop connections. Review other remote access and local administrators in the manual checks.");
            Line(report, "BitLocker reports protection on the Windows volume. This assessment reads its status only.");
            Line(report, "A restart indicator can identify a pending restart. Missing indicators leave restart clearance unresolved.");
            Line(report, "Unresolved management or ownership blocks handover.");
        }
        Line(report, "");
        Line(report, "Manual check records");
        needsReview |= WriteManualTests(report, job, showEvidenceIds, cancellationToken);
        Line(report, "");
        if (technicalRecord is not null)
        {
            WriteRepairHistory(report, job, cancellationToken);
        }

        if (savedDecision is not null)
        {
            WritePolicySections(report, savedDecision, savedDecision.HasNewSecurityReview(job), cancellationToken);
        }
        else
        {
            if (technicalRecord is not null)
            {
                Line(report, "Policy");
                Line(report, "No policy decision is saved for the current assessment.");
                Line(report, "");
            }

            Line(report, "Next step");
            Line(report, NextStep(job, gate, latestAssessment, needsReview));
        }

        if (packages is { Count: > 0 })
        {
            WritePackageRecords(report, packages, cancellationToken);
        }
        else if (technicalRecord is not null)
        {
            Line(report, "");
            Line(report, "Machine-wide setup journal");
            Line(report, "No machine-wide package transaction is saved for this job.");
        }

        if (preparation is { Count: > 0 })
        {
            WritePreparationRecords(report, job.Job.JobId, preparation, cancellationToken);
        }

        if (profileVerification is not null)
        {
            WriteProfileVerification(report, job.Job.JobId, profileVerification, cancellationToken);
        }
        else if (technicalRecord is not null)
        {
            Line(report, "");
            Line(report, "Independent profile verification");
            Line(report, "No profile verification result is saved for this job.");
        }

        if (technicalRecord is not null)
        {
            WriteFinalizationReview(report, job.Job.JobId, technicalRecord.Finalization, cancellationToken);
        }

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
        StringBuilder report, StoredJob job, SanitizationGateStatus gate, bool showEvidenceIds, CancellationToken cancellationToken)
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
            WriteAttribution(report, item.Metadata, item.OperatorId, showEvidenceIds);
            Line(report, $"  External event: {Timestamp(item.OccurredAtUtc)}. Policy: {SafeCode(item.PolicyVersion)}.");
            if (item.MediaIdentifier is not null)
            {
                Line(report, $"  Workshop media reference: {SafeText(item.MediaIdentifier.Value)}.");
            }
        }
    }

    private static bool WriteObservations(StringBuilder report, StoredJob job, bool showEvidenceIds,
        bool showRepairHistory, CancellationToken cancellationToken)
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

            if (showRepairHistory && observation.Metadata.ProviderId.Value == RepairJournal.ReservedProviderId)
            {
                continue;
            }

            if (!WorkshopObservationFields.Allows(observation))
            {
                outsideSchema++;
                continue;
            }

            bool syncTimestamp = observation.EvidenceKey.Value == "security.last_successful_sync_utc";
            var value = syncTimestamp && observation.Value is not null
                ? WorkshopSyncTime(observation) ?? Withheld
                : DescribeValue(observation);
            string unit = !syncTimestamp && observation.Unit is { } storedUnit && observation.Value is not null && value != Withheld
                ? $" {SafeCode(storedUnit)}" : "";
            Line(report, $"- {observation.EvidenceKey.Value}: {value}{unit}");
            WriteAttribution(report, observation.Metadata, observation.OperatorId, showEvidenceIds);
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

    private static bool WriteManualTests(StringBuilder report, StoredJob job, bool showEvidenceIds, CancellationToken cancellationToken)
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
            if (ManualTestCatalog.IsSecurityReview(caseName) && ManualTestCatalog.TryGetCase(caseName, out var review))
            {
                caseName = $"{review.Title} ({review.Id})";
            }
            Line(report, $"- {caseName}: {result}");
            WriteAttribution(report, item.Metadata, item.OperatorId, showEvidenceIds);
            Line(report, $"  Record: {SafeCode(item.HumanTestId.Value)}.");
            WriteLimitation(report, item.LimitationCode);
            if (item.Details is { } details)
            {
                Line(report, $"  Procedure: {SafeCode(details.ProcedureRevision)}.");
                if (details.AssessmentId is { } reviewedAssessment)
                {
                    Line(report, $"  Reviewed assessment: {SafeCode(reviewedAssessment)}.");
                }
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

    private static void WriteAttribution(
        StringBuilder report, EvidenceMetadata metadata, OperatorId? operatorId, bool showEvidenceId)
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
        if (showEvidenceId)
        {
            Line(report, $"  Evidence ID {SafeCode(metadata.EvidenceId.Value)}.");
        }
    }

    private static void WriteLimitation(StringBuilder report, string? limitation)
    {
        if (limitation is not null)
        {
            Line(report, $"  Limitation: {SafeCode(limitation)}.");
        }
    }

    public static string? WorkshopSyncTime(Observation observation)
    {
        if (observation.EvidenceKey.Value != "security.last_successful_sync_utc" ||
            observation.Metadata.ProviderId.Value != "windows-security-baseline" ||
            observation.Metadata.EvidenceClassification != EvidenceClassification.Observed ||
            observation.Metadata.Provenance.Kind is not (ProvenanceKind.ProviderObservation or ProvenanceKind.SyntheticFixture) ||
            observation.Metadata.Provenance.SourceReference != "security.w32time.last_successful_sync" ||
            observation.Unit != "unix_seconds" || observation.Value?.IntegerValue is not { } seconds ||
            seconds <= 0 || seconds > observation.Metadata.CollectedAtUtc.ToUnixTimeSeconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
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
