using System.Text;
using System.Text.Json;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.Reports.Tests;

public sealed class PolicyReportTests
{
    private const string AssessmentId = "assessment-policy-fixture";
    private const string SecretShape = "AAAAA-BBBBB-CCCCC-DDDDD-EEEEE";
    private const string PolicyJson = """
        {
          "schema_version":"thirdlife.policy.v2", "policy_id":"workshop-policy", "version":"1.0.0-candidate.1",
          "status":"candidate", "synthetic_data":true, "classification":"PUBLIC_REFERENCE", "claim_scope":"pilot_fixture",
          "effective":{"status":"candidate_not_effective","effective_from_utc":null,"effective_until_utc":null},
          "target":{"operating_system":"windows_11","support_state":"supported","architecture":"x64","device_class":"laptop"},
          "rules":[
            {"id":"POL-KBD-001","requirement_type":"human_confirmed","severity":"required","evidence_key":"function.keyboard",
             "condition":{"type":"equals","value":"passed"},
             "decision":{"blocks_ready":true,"unmet_disposition":"repair_and_retest","unavailable_disposition":"human_review_required"}},
            {"id":"POL-OWN-001","requirement_type":"blocking","severity":"critical","evidence_key":"ownership.lock_state",
             "condition":{"type":"equals","value":"clear"},
             "decision":{"blocks_ready":true,"unmet_disposition":"do_not_deploy","unavailable_disposition":"human_review_required"}},
            {"id":"POL-STORAGE-001","requirement_type":"repairable","severity":"required","evidence_key":"storage.system.free_bytes",
             "condition":{"type":"minimum_integer","value":100,"unit":"bytes"},"exception_authorization_id":"workshop-storage-review",
             "decision":{"blocks_ready":true,"unmet_disposition":"repair_and_retest","unavailable_disposition":"human_review_required"}}
          ]
        }
        """;

    private static readonly DateTimeOffset SampleTime = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Job SampleJob = new(new("job-policy-fixture"), new("device-policy-fixture"), SampleTime);

    [Theory]
    [InlineData(Disposition.ReadyToPrepare)]
    [InlineData(Disposition.RepairAndRetest)]
    [InlineData(Disposition.HumanReviewRequired)]
    [InlineData(Disposition.AlternativeOperatingSystemCandidate)]
    [InlineData(Disposition.DoNotDeploy)]
    public void CapturedDispositionMatchesGoldenText(Disposition expected)
    {
        var (job, assessment, saved) = Fixture(expected);
        var report = WorkshopReportRenderer.RenderWithPolicy(job, assessment, saved);

        Assert.Equal(expected, saved.Decision.Disposition);
        const string captured = "\nCaptured disposition\n";
        var start = report.IndexOf(captured, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.Equal(ExpectedDisposition(expected).ReplaceLineEndings("\n") + "\n", report[(start + captured.Length)..]);
        Assert.Contains("\nPolicy\nPolicy ID workshop-policy.\nVersion 1.0.0-candidate.1.\n", report, StringComparison.Ordinal);
        Assert.Contains($"\nSHA-256 {saved.Snapshot.Document.ContentSha256}.\n", report, StringComparison.Ordinal);
        Assert.Contains(expected == Disposition.AlternativeOperatingSystemCandidate
            ? "\nHistorical policy evaluation. Use the current policy for a new preparation decision.\n"
            : "\nCandidate policy evaluation for workshop review.\n", report, StringComparison.Ordinal);
        Assert.Contains("\nSchema: workshop-disposition-v2\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', report);
    }

    [Fact]
    public void RuleExceptionsAndPendingConfirmationRemainDistinctAndSecretsStayWithheld()
    {
        var (job, assessment, saved) = Fixture(Disposition.HumanReviewRequired, includeException: true);

        var report = WorkshopReportRenderer.RenderWithPolicy(job, assessment, saved);

        Assert.Contains("- Core rule CORE-OS-ARCHITECTURE. Pass. Evidence ID evidence-architecture.", report, StringComparison.Ordinal);
        Assert.Contains("- Policy rule POL-KBD-001. Pending confirmation. No saved evidence is linked to this check.", report, StringComparison.Ordinal);
        Assert.Contains("- Policy rule POL-STORAGE-001. Exception. Evidence ID evidence-storage.", report, StringComparison.Ordinal);
        const string exceptionsHeading = "\nExceptions\n";
        var exceptionStart = report.IndexOf(exceptionsHeading, StringComparison.Ordinal);
        Assert.True(exceptionStart > report.IndexOf("\nPolicy rule details\n", StringComparison.Ordinal));
        var captureStart = report.IndexOf("\nCaptured disposition\n", StringComparison.Ordinal);
        Assert.True(captureStart > exceptionStart);
        var exceptions = report[exceptionStart..captureStart];
        Assert.Contains("Authorization workshop-storage-review.", exceptions, StringComparison.Ordinal);
        Assert.Contains("Operator code Withheld by the workshop schema.", exceptions, StringComparison.Ordinal);
        Assert.Contains("Exception record exception-", exceptions, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretShape, report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-reason@example.invalid", report, StringComparison.Ordinal);
        Assert.DoesNotContain("unreviewed.txt", report, StringComparison.Ordinal);
        Assert.Contains("\n- battery.present: Unknown\n", report, StringComparison.Ordinal);
        Assert.Contains("\n  Evidence ID evidence-battery-unknown.\n", report, StringComparison.Ordinal);
        Assert.Contains("\n  Evidence ID evidence-sanitization.\n", report, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyReportRejectsWrongAssessmentSnapshotOrEvidence()
    {
        var (job, assessment, saved) = Fixture(Disposition.ReadyToPrepare);
        var wrongAssessment = new WorkshopAssessmentSummary(SampleJob.JobId, assessment.GateDecisionId, SampleTime,
            SampleTime, WorkshopAssessmentOutcome.Completed, [], "assessment-other");
        var wrongSnapshot = PolicySnapshot.Parse(Encoding.UTF8.GetBytes(PolicyJson + "\n"));
        var missingObservations = new StoredJob(job.Job, false, null, [], job.SanitizationEvidence, job.HumanTests,
            job.SanitizationGateDecisions, []);

        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderWithPolicy(job, wrongAssessment, saved));
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderWithPolicy(job, assessment, saved with { Snapshot = wrongSnapshot }));
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderWithPolicy(missingObservations, assessment, saved));
        Assert.Throws<OperationCanceledException>(() => WorkshopReportRenderer.RenderWithPolicy(job, assessment, saved, new(true)));
    }

    [Fact]
    public void OldCapturedRowsStillRenderTheirCombinedRuleDetails()
    {
        var (job, assessment, saved) = Fixture(Disposition.ReadyToPrepare);
        var options = DomainJson.CreateStrictOptions();
        var legacy = JsonSerializer.SerializeToNode(saved.Decision, options)!.AsObject();
        legacy.Remove(nameof(PolicyDecisionRecord.ExceptionReport));
        legacy[nameof(PolicyDecisionRecord.RuleReport)] = "Policy rule details\n- Policy rule POL-LEGACY-001. Exception.";
        var restored = legacy.Deserialize<PolicyDecisionRecord>(options)!;

        var report = WorkshopReportRenderer.RenderWithPolicy(job, assessment, saved with { Decision = restored });

        Assert.Contains("\nPolicy rule details\n- Policy rule POL-LEGACY-001. Exception.\n", report, StringComparison.Ordinal);
        Assert.Contains("\nExceptions\nThe saved rule details include any exception statuses and records.\n", report, StringComparison.Ordinal);
    }

    private static (StoredJob Job, WorkshopAssessmentSummary Assessment, StoredPolicyDecision Saved) Fixture(
        Disposition scenario, bool includeException = false)
    {
        var alternate = scenario == Disposition.AlternativeOperatingSystemCandidate;
        var snapshot = PolicySnapshot.Parse(Encoding.UTF8.GetBytes(alternate
            ? PolicyJson.Replace(PolicySchema.Version, PolicySchema.LegacyVersion, StringComparison.Ordinal)
            : PolicyJson));
        var observations = new List<Observation>
        {
            Fact("evidence-architecture", "system.architecture", "windows-system-inventory", EvidenceValue.FromEnum("x64")),
            Fact("evidence-release", "os.family", "windows-operating-system", EvidenceValue.FromEnum(alternate ? "windows_10" : "windows_11")),
            Fact("evidence-support", "os.support_target", "windows-operating-system", EvidenceValue.FromEnum(alternate ? "audit_only" : "supported")),
            Fact("evidence-storage", "storage.system_volume.caller_free_bytes", "windows-storage",
                EvidenceValue.FromInteger(scenario == Disposition.RepairAndRetest || includeException ? 25 : 250), "bytes"),
            Fact("evidence-ownership", "ownership.lock_state", "fixture-ownership",
                EvidenceValue.FromEnum(scenario == Disposition.DoNotDeploy ? "locked" : "clear")),
            Fact("evidence-battery-unknown", "battery.present", "windows-battery-report", null),
            Fact("evidence-unreviewed", "private.details", "fixture-unreviewed", EvidenceValue.FromString(@"C:\private\unreviewed.txt")),
        };
        var operatorCode = new OperatorId("operator-fixture");
        var sanitization = new SanitizationEvidence(Metadata("evidence-sanitization", "workshop-intake", EvidenceClassification.Observed),
            SanitizationState.ReplacementStorage, "replacement_storage", operatorCode, SampleTime, new("medium-fixture"),
            SanitizationVerificationState.Verified, "m1-intake-v1");
        var gate = SanitizationGate.Evaluate(SampleJob.JobId, sanitization, SampleTime, new("gate-policy-fixture"));
        var assessment = new WorkshopAssessmentSummary(SampleJob.JobId, gate.DecisionId, SampleTime, SampleTime,
            WorkshopAssessmentOutcome.Completed, [], AssessmentId);
        List<HumanTestEvidence> humanTests = [];
        var policyObservations = new List<Observation>
        {
            observations[0],
            Project(observations[1], "operating_system.release"),
            Project(observations[2], "operating_system.support_state"),
            Project(observations[3], "storage.system.free_bytes"),
            observations[4],
        };
        if (scenario != Disposition.HumanReviewRequired)
        {
            var manualMetadata = new EvidenceMetadata(new("evidence-keyboard"), PrivacyClassification.WorkshopRestricted,
                EvidenceClassification.HumanConfirmed, new("manual-test-capture"), SampleTime,
                new EvidenceProvenance(ProvenanceKind.HumanConfirmation, "fixture-procedure"), ValueAvailability.Available);
            humanTests.Add(new HumanTestEvidence(new("manual-keyboard"), SampleJob.JobId, SampleJob.DeviceId,
                manualMetadata, HumanTestResult.Pass, operatorCode, null));
            policyObservations.Add(new Observation(manualMetadata, new("function.keyboard"), EvidenceValue.FromEnum("passed"),
                null, null, operatorCode));
        }

        var job = new StoredJob(SampleJob, false, null, observations, [sanitization], humanTests, [gate], []);
        var history = new PolicyExceptionHistory(SampleJob.JobId);
        var decision = DispositionEngine.Evaluate(snapshot.Document, policyObservations, [], history, SampleTime);
        if (includeException)
        {
            history = history.Record(decision, new("POL-STORAGE-001"), new(SecretShape), "secret-reason@example.invalid", SampleTime);
            decision = DispositionEngine.Evaluate(snapshot.Document, policyObservations, [], history, SampleTime);
        }

        var record = PolicyDecisionRecord.Create(snapshot, SampleJob.JobId, AssessmentId, decision,
            policyObservations.Select(observation => observation.Metadata.EvidenceId), SampleTime);
        return (job, assessment, new StoredPolicyDecision(snapshot, record));
    }

    private static Observation Fact(string id, string key, string provider, EvidenceValue? value, string? unit = null) =>
        new(Metadata(id, provider, value is null ? EvidenceClassification.NotAvailable : EvidenceClassification.Observed),
            new EvidenceKey(key), value, unit, value is null ? "provider_unavailable" : null);

    private static EvidenceMetadata Metadata(string id, string provider, EvidenceClassification classification) =>
        new(new(id), PrivacyClassification.WorkshopRestricted, classification, new(provider), SampleTime,
            new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "fixture-policy"),
            classification == EvidenceClassification.NotAvailable ? ValueAvailability.Unknown : ValueAvailability.Available);

    private static Observation Project(Observation fact, string key) =>
        new(fact.Metadata, new EvidenceKey(key), fact.Value, fact.Unit, fact.LimitationCode);

    private static string ExpectedDisposition(Disposition expected) => expected switch
    {
        Disposition.ReadyToPrepare => """
            Ready to prepare
            The required checks passed for this assessment.
            Core rule CORE-OS-ARCHITECTURE passed. Evidence ID evidence-architecture.
            Core rule CORE-OS-RELEASE passed. Evidence ID evidence-release.
            Core rule CORE-OS-SUPPORT passed. Evidence ID evidence-support.
            Policy rule POL-KBD-001 passed. Evidence ID evidence-keyboard.
            2 more controlling rules are listed in the details.
            """,
        Disposition.RepairAndRetest => """
            Repair and retest
            A required check failed. Record the repair and retest the affected checks.
            Policy rule POL-STORAGE-001 failed. Evidence ID evidence-storage.
            """,
        Disposition.HumanReviewRequired => """
            Human review required
            Review the cited findings before proceeding.
            Policy rule POL-KBD-001 requires human confirmation. No saved evidence is linked to this check.
            """,
        Disposition.AlternativeOperatingSystemCandidate => """
            Original recorded disposition and explanation follow.
            Alternative operating system candidate
            A Windows preparation requirement failed. Consider a supported alternative operating system.
            Core rule CORE-OS-RELEASE failed. Evidence ID evidence-release.
            Core rule CORE-OS-SUPPORT failed. Evidence ID evidence-support.
            """,
        Disposition.DoNotDeploy => """
            Do not deploy
            A blocking requirement failed. Stop deployment and review the cited findings.
            Policy rule POL-OWN-001 failed. Evidence ID evidence-ownership.
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(expected)),
    };
}
