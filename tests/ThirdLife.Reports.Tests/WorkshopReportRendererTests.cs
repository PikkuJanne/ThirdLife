using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.Reports.Tests;

public sealed class WorkshopReportRendererTests
{
    private static readonly DateTimeOffset SampleTime = new(2026, 11, 24, 12, 30, 0, TimeSpan.Zero);
    private static readonly Job SampleJob = new(new("job-report-fixture"), new("device-report-fixture"), SampleTime);
    private static readonly OperatorId Operator = new("fixture-operator");

    [Fact]
    public void WorkshopFixtureMatchesExactReportAcrossCultures()
    {
        var sanitization = FixtureSanitization();
        var gate = Gate(sanitization);
        var job = Stored(
            [
                Observation("test.keyboard.value", "fixture-keyboard", EvidenceValue.FromBoolean(true)),
                Observation("battery.pack.full_charge_ratio_percent", "windows-battery-report",
                    EvidenceValue.FromDecimal(84.5m), unit: "percent"),
                Observation("test.display.failure", "fixture-display", null, limitation: "provider_unavailable"),
                Observation("device.serial_number", "windows-system-inventory", EvidenceValue.FromString("SYNTHETIC-SERIAL-42")),
            ],
            [sanitization], [ManualResult()], [gate]);
        var assessment = new WorkshopAssessmentSummary(SampleJob.JobId, gate.DecisionId, SampleTime,
            null, WorkshopAssessmentOutcome.Incomplete, [new("fixture-camera")]);

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            var report = WorkshopReportRenderer.Render(job, assessment);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(report, WorkshopReportRenderer.Render(job, assessment));
            var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "workshop-audit.txt"));
            Assert.Equal(expected, report);
            Assert.DoesNotContain('\r', report);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ReportOmitsUnreviewedKeysOperationalPayloadsAndFreeFormNotes()
    {
        var job = Stored(
            [
                Observation("private.credential", "unreviewed-provider", EvidenceValue.FromString("arbitrary-sensitive-token")),
                Observation("device.model", "unreviewed-provider", EvidenceValue.FromString("private-foreign-model")),
                Observation("device.serial_number", "windows-system-inventory", EvidenceValue.FromString("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE")),
                Observation("device.model", "windows-system-inventory", EvidenceValue.FromString(@"C:\Users\workshop-test\private.txt")),
                Observation("device.manufacturer", "windows-system-inventory", EvidenceValue.FromString("Adaptor 192.168.12.15")),
                Observation("test.display.value", "fixture-display", EvidenceValue.FromString("unapproved-fixture-text")),
                Observation("assessment.start", "assessment-orchestrator", EvidenceValue.FromString("raw-internal-journal")),
            ],
            humanTests: [ManualResult("Workshop operator noted user-secret@example.invalid")]);

        var report = WorkshopReportRenderer.Render(job);

        Assert.DoesNotContain("private.credential", report);
        Assert.DoesNotContain("arbitrary-sensitive-token", report);
        Assert.DoesNotContain("private-foreign-model", report);
        Assert.DoesNotContain("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE", report);
        Assert.DoesNotContain("private.txt", report);
        Assert.DoesNotContain("192.168.12.15", report);
        Assert.DoesNotContain("unapproved-fixture-text", report);
        Assert.DoesNotContain("raw-internal-journal", report);
        Assert.DoesNotContain("user-secret@example.invalid", report);
        Assert.Contains("device.serial_number: Withheld by the workshop schema", report);
        Assert.Contains("Records outside the workshop allowlist: 3", report);
        Assert.Contains("Free-form notes remain in the protected job", report);
    }

    [Fact]
    public void EvidenceBoundToAnEarlierGateRemainsHistorical()
    {
        var sanitization = FixtureSanitization();
        var gate = Gate(sanitization);
        var newRecord = new SanitizationEvidence(
            Metadata("workshop-intake", EvidenceClassification.NotAvailable, ValueAvailability.Unknown),
            SanitizationState.Unknown, "unknown", null, null, null,
            SanitizationVerificationState.NotAvailable, "m1-intake-v1");
        var currentGate = SanitizationGate.Evaluate(SampleJob.JobId, newRecord, SampleTime.AddHours(1), new("gate-current"));
        var report = WorkshopReportRenderer.Render(
            Stored(sanitization: [sanitization, newRecord], decisions: [gate, currentGate]),
            new WorkshopAssessmentSummary(SampleJob.JobId, gate.DecisionId, SampleTime,
                SampleTime, WorkshopAssessmentOutcome.Cancelled, [new("fixture-camera")]));

        Assert.Contains("Cancelled. Committed findings are retained.", report);
        Assert.Contains("Historical assessment under an earlier sanitization decision.", report);
        Assert.Contains("Blocked. The external result is unknown.", report);
        Assert.Contains("Readiness: Unknown.", report);
        Assert.Contains("resolve the gate blocker", report);
    }

    [Fact]
    public void MissingEvidenceAndUnrunCollectionStayUnknown()
    {
        var report = WorkshopReportRenderer.Render(Stored());
        Assert.Contains("No assessment summary is saved.", report);
        Assert.Contains("No sanitization evidence is saved.", report);
        Assert.Contains("No reportable observations are saved.", report);
        Assert.Contains("No manual results are saved.", report);
        Assert.Contains("Readiness: Unknown.", report);
    }

    [Fact]
    public void RejectsAnAssessmentFromAnotherJobOrAnUnknownGate()
    {
        var sanitization = FixtureSanitization();
        var gate = Gate(sanitization);
        var job = Stored(sanitization: [sanitization], decisions: [gate]);
        var foreign = new WorkshopAssessmentSummary(new("other-job"), gate.DecisionId, SampleTime, null,
            WorkshopAssessmentOutcome.Incomplete, []);
        var invalidGate = new WorkshopAssessmentSummary(SampleJob.JobId, new("other-gate"), SampleTime, null,
            WorkshopAssessmentOutcome.Incomplete, []);

        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.Render(job, foreign));
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.Render(job, invalidGate));
    }

    [Fact]
    public void EnforcesEvidenceAndRenderedOutputBounds()
    {
        var observation = Observation("device.model", "windows-system-inventory", EvidenceValue.FromString(new('x', 128)));
        var atOutputLimit = Stored(Enumerable.Repeat(observation, 10_000));
        var aboveEvidenceLimit = Stored(Enumerable.Repeat(observation, 10_001));

        Assert.Contains("output limit", Assert.Throws<InvalidOperationException>(
            () => WorkshopReportRenderer.Render(atOutputLimit)).Message);
        Assert.Contains("evidence limit", Assert.Throws<InvalidOperationException>(
            () => WorkshopReportRenderer.Render(aboveEvidenceLimit)).Message);
    }

    [Fact]
    public void RenderingHonorsCancellation()
    {
        Assert.Throws<OperationCanceledException>(() =>
            WorkshopReportRenderer.Render(Stored(), cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public void PreparationRecordsExplainOptionalOmissionsAndPreserveEarlierFailures()
    {
        WorkshopPreparationAction earlier = new("document_editing", true, WorkshopPreparationState.Failed,
            WorkshopPreparationEffect.ReviewRequired, false, @"C:\private\installer.log", SampleTime, null, null);
        WorkshopPreparationAction optionalSkip = new("video_calling", false, WorkshopPreparationState.Skipped,
            WorkshopPreparationEffect.OptionalOmission, true, "Declined", SampleTime, Operator, null);
        WorkshopPreparationAction verified = new("document_editing", true, WorkshopPreparationState.Verified,
            WorkshopPreparationEffect.Verified, true, "VerificationPassed", SampleTime.AddMinutes(1), null, null);
        WorkshopPreparationPlan plan = new(SampleJob.JobId, new string('a', 64), false,
            Disposition.ReadyToPrepare, [earlier, optionalSkip, verified]);

        var report = WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null, [], [plan]);

        Assert.Contains("The current plan meets its essential action requirements", report);
        Assert.Contains("Whole-device acceptance still requires", report);
        Assert.Contains("video_calling (optional). Skipped", report);
        Assert.Contains("The preparation disposition allows this optional outcome", report);
        Assert.Contains("document_editing (essential). The attempt failed. A later record controls", report);
        Assert.Contains("document_editing (essential). Independently verified", report);
        Assert.DoesNotContain("installer.log", report);
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null,
            [], [plan with { JobId = new JobId("unrelated-job") }]));
    }

    private static StoredJob Stored(
        IEnumerable<Observation>? observations = null,
        IEnumerable<SanitizationEvidence>? sanitization = null,
        IEnumerable<HumanTestEvidence>? humanTests = null,
        IEnumerable<SanitizationGateDecision>? decisions = null) =>
        new(SampleJob, false, null, observations ?? [], sanitization ?? [], humanTests ?? [], decisions ?? [], []);

    private static SanitizationEvidence FixtureSanitization() =>
        new(Metadata("workshop-intake"), SanitizationState.Verified, "clear", Operator, SampleTime,
            new("fixture-media-1"), SanitizationVerificationState.Verified, "m1-intake-v1");

    private static SanitizationGateDecision Gate(SanitizationEvidence evidence) =>
        SanitizationGate.Evaluate(SampleJob.JobId, evidence, SampleTime, new("gate-report-fixture"));

    private static HumanTestEvidence ManualResult(string notes = "No safe fixture peripheral was available.") =>
        new(new("manual-report-fixture"), SampleJob.JobId, SampleJob.DeviceId,
            Metadata("manual-test-capture", EvidenceClassification.NotAvailable, ValueAvailability.Unknown),
            HumanTestResult.NotAvailable, Operator, "equipment_missing", new("MHT-002", "manual-v1", notes, null));

    private static Observation Observation(
        string key,
        string provider,
        EvidenceValue? value,
        string? unit = null,
        string? limitation = null) => new(
        Metadata(provider, value is null ? EvidenceClassification.NotAvailable : EvidenceClassification.Observed,
            value is null ? ValueAvailability.Unknown : ValueAvailability.Available),
        new(key), value, unit, limitation);

    private static EvidenceMetadata Metadata(
        string provider,
        EvidenceClassification classification = EvidenceClassification.Observed,
        ValueAvailability availability = ValueAvailability.Available) =>
        new(new($"evidence-{provider}"), PrivacyClassification.WorkshopRestricted, classification, new(provider),
            SampleTime, new(ProvenanceKind.SyntheticFixture, "fixture-report-v1"), availability);
}
