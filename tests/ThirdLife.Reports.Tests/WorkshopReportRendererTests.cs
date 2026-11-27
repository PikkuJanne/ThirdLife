using System.Collections.Immutable;
using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.Verification;

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

    [Theory]
    [InlineData("security.w32time.last_successful_sync", "unix_seconds", 1_700_000_000L, "2023-11-14 22:13:20 UTC")]
    [InlineData("security.w32time.last_successful_sync", "unix_seconds", long.MaxValue, "Withheld")]
    [InlineData("security.w32time.last_successful_sync", "milliseconds", 1_700_000_000L, "Withheld")]
    [InlineData("unrelated.source", "unix_seconds", 1_700_000_000L, "Withheld")]
    public void ReportFormatsOnlyAttributableWindowsTime(string source, string unit, long seconds, string expected)
    {
        var metadata = new EvidenceMetadata(new("time-sample"), PrivacyClassification.WorkshopRestricted,
            EvidenceClassification.Observed, new("windows-security-baseline"), SampleTime,
            new(ProvenanceKind.SyntheticFixture, source), ValueAvailability.Available);
        var observation = new Observation(metadata, new("security.last_successful_sync_utc"),
            EvidenceValue.FromInteger(seconds), unit, limitationCode: null);

        var report = WorkshopReportRenderer.Render(Stored([observation]));

        Assert.Contains($"security.last_successful_sync_utc: {expected}", report);
        Assert.Contains("Missing indicators leave restart clearance unresolved.", report);
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
    public void TechnicalRecordReportsBoundPlanProvenanceAndWithholdsHostileValues()
    {
        var created = new DateTimeOffset(2026, 11, 24, 12, 30, 0, TimeSpan.Zero);
        var validOperator = new OperatorId("operator-reviewed");
        var hostileCode = new OperatorId("AAAAA-BBBBB-CCCCC-DDDDD-EEEEE");
        var plan = new WorkshopPlanProvenance(SampleJob.JobId, new('a', 64), new('b', 64), new('c', 64),
            new('d', 64), new('e', 64), new('f', 64), true,
            [
                new(new("81918fd0-dc2c-4b1b-b253-f8619e520699"), hostileCode, created.AddSeconds(1)),
                new(new("ae3aee53-48e8-4898-8264-df4437ed964e"), validOperator, created),
            ]);
        var job = Stored([new Observation(Metadata("windows-system-inventory"), new("device.model"),
            EvidenceValue.FromString("<script>alert('hostile')</script>"), null, null)]);
        var details = new WorkshopRecordDetails([plan]);
        var report = WorkshopReportRenderer.RenderTechnicalRecord(job, null, null, [], null, null, details);

        var expected = $"""
            - Saved plan SHA-256 {plan.PlanSha256} (synthetic test).
              Impact SHA-256 {plan.ImpactSha256}.
              Catalog SHA-256 {plan.CatalogSha256}.
              Profile SHA-256 {plan.ProfileSha256}.
              Policy SHA-256 {plan.PolicySha256}.
              Reviewed evidence SHA-256 {plan.EvidenceSha256}.
              Operator approval ae3aee53-48e8-4898-8264-df4437ed964e at 2026-11-24T12:30:00.0000000Z.
              Workshop operator code operator-reviewed.
              Operator approval 81918fd0-dc2c-4b1b-b253-f8619e520699 at 2026-11-24T12:30:01.0000000Z.
              Workshop operator code Withheld by the workshop schema.
            """;
        Assert.Contains(expected.ReplaceLineEndings("\n"), report, StringComparison.Ordinal);
        Assert.Matches(@"Renderer build [0-9A-Za-z.+-]+\.", report);
        Assert.DoesNotContain(hostileCode.Value, report, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", report, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderTechnicalRecord(job, null, null, [],
            null, null, new([plan with { JobId = new("foreign-job") }])));
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderTechnicalRecord(job, null, null, [],
            null, null, new([plan with { ProfileSha256 = "<script>" }])));
    }

    [Fact]
    public void TechnicalRecordSeparatesCurrentAutomatedAndOperatorEvidenceFromEarlierResults()
    {
        var history = new FinalizationRecord[]
        {
            new(1, FinalizationRuleId.Credentials, FinalizationOutcome.Passed, null, Operator, SampleTime, "prior-scope"),
            new(2, FinalizationRuleId.BrowserSessions, FinalizationOutcome.NotApplicable, FinalizationReason.NoWorkshopUse,
                Operator, SampleTime.AddSeconds(1), "current-scope"),
        };
        var checklist = FinalizationChecklist.Evaluate("current-scope", history, FinalizationOutcome.Passed);
        var review = new WorkshopFinalizationReview(SampleJob.JobId, checklist);
        var report = WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [], null, null, new([], review));

        Assert.Contains("Required finalization checks remain open.", report, StringComparison.Ordinal);
        Assert.Contains("- Pending restart. Required. Automated observation. Checked and clear.", report, StringComparison.Ordinal);
        Assert.Contains("- Browser sessions. Required. Operator confirmation. Not applicable.", report, StringComparison.Ordinal);
        Assert.Contains("- Workshop credentials. Required, blocks handover. Needs recheck", report, StringComparison.Ordinal);
        Assert.Contains("- Earlier or superseded confirmation for Workshop credentials. Checked and clear.", report, StringComparison.Ordinal);
        Assert.Contains("- Current confirmation for Browser sessions. Not applicable.\n  Reason The workshop did not use this.\n",
            report, StringComparison.Ordinal);

        var missing = WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [], null, null, new([]));
        Assert.Contains("No checklist result is available. Required workshop checks remain unverified.", missing, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [],
            null, null, new([], review with { JobId = new("foreign-job") })));
    }

    [Fact]
    public void TechnicalRecordKeepsEarlierSampleEvidenceAndBackupReadinessDistinct()
    {
        var review = new WorkshopBackupSampleReview(SampleJob.JobId, FileHistorySampleAuditState.CopyMatches, EarlierHandover: true);
        var report = WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [], null, null,
            new([], BackupSample: review));
        Assert.Contains("Sample evidence is from an earlier handover.", report, StringComparison.Ordinal);
        Assert.Contains("The separate recovered copy matched the prepared sample.", report, StringComparison.Ordinal);
        Assert.Contains("Backup setup remains pending.", report, StringComparison.Ordinal);

        var unavailable = WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [], null, null,
            new([], BackupSample: review with { State = null }));
        Assert.Contains("Saved sample evidence is unavailable.", unavailable, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample evidence is from an earlier handover.", unavailable, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [],
            null, null, new([], BackupSample: review with { JobId = new("foreign-job") })));
    }

    [Fact]
    public void TechnicalRecordKeepsEarlierArtifactIndicationsAndCurrentSourceAvailabilityDistinct()
    {
        var earlier = Scan(1, "earlier-scope", WorkshopArtifactStatus.Present);
        var current = Scan(2, "current-scope", WorkshopArtifactStatus.AccessDenied);
        var checklist = FinalizationChecklist.Evaluate("current-scope", [], artifactScans: [earlier, current]);
        var review = new WorkshopFinalizationReview(SampleJob.JobId, checklist);
        var report = WorkshopReportRenderer.RenderTechnicalRecord(Stored(), null, null, [], null, null, new([], review));

        Assert.Contains("A saved workshop artifact indication still needs resolution.", report, StringComparison.Ordinal);
        Assert.Contains("Earlier or superseded scan", report, StringComparison.Ordinal);
        Assert.Contains("Current scan", report, StringComparison.Ordinal);
        Assert.Contains("Synthetic fixture. Detector workshop-artifact-detector-1.", report, StringComparison.Ordinal);
        Assert.Contains("Package staging folders. Indication found.", report, StringComparison.Ordinal);
        Assert.Contains("Package staging folders. Access denied.", report, StringComparison.Ordinal);
        Assert.Contains("Specified local SMB shares. No exact names entered.", report, StringComparison.Ordinal);

        static WorkshopArtifactScan Scan(long sequence, string scope, WorkshopArtifactStatus package) => new(sequence,
            SampleTime.AddSeconds(sequence), scope, WorkshopArtifactScan.CurrentDetectorRevision, ProvenanceKind.SyntheticFixture,
            Enum.GetValues<WorkshopArtifactKind>().Select(kind => new WorkshopArtifactFinding(kind, kind switch
            {
                WorkshopArtifactKind.PackageStagingResidue => package,
                WorkshopArtifactKind.ConfiguredSmbShares => WorkshopArtifactStatus.TargetsNotConfigured,
                _ => WorkshopArtifactStatus.Unavailable,
            })).ToArray());
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FinalProfileReportSeparatesExactVersionsFromDeviceAcceptance(bool matchingVersion)
    {
        var actionId = Guid.Parse("b7a0303f-70d8-46c0-9628-aafdcbf7d5f8");
        var capability = new ProfileCapabilityResult("document_editing", true,
            matchingVersion ? ProfileVerificationState.Passed : ProfileVerificationState.Failed,
            "8.9.9.1", matchingVersion ? "8.9.9.1" : "8.9.9.0", actionId,
            matchingVersion ? ProfilePackageStatus.ExactVersion : ProfilePackageStatus.Mismatch);
        var finding = new ProfilePolicyFinding(new("POL-SEC-001"),
            matchingVersion ? PolicyRuleState.Met : PolicyRuleState.Unavailable,
            matchingVersion ? PolicyRuleReason.ConditionMet : PolicyRuleReason.EvidenceStale, true);
        var result = new ProfileVerificationResult(SampleTime, SampleTime.AddSeconds(1), false,
            new string('a', 64), new string('b', 64),
            Enum.GetValues<ProfileCheckKind>().Select(kind => new ProfileVerificationCheck(kind, ProfileVerificationState.Passed,
                kind is not (ProfileCheckKind.SystemVolumeCapacity or ProfileCheckKind.PhysicalStorageEvidence or
                    ProfileCheckKind.AudioOutput or ProfileCheckKind.Camera or ProfileCheckKind.Microphone)))
                .ToImmutableArray(),
            [capability], [finding], 26300, 24_000_000_000, 64_000_000_000, 0, 0, WindowsUpdateDisposition.Current,
            updateRecoveryPending: false);
        var verification = new WorkshopProfileVerification(SampleJob.JobId, result, true);

        var report = WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null, [], null, verification);

        Assert.Contains("Independent final profile verification", report);
        Assert.Contains($"Resolved version 8.9.9.1. Installed version {capability.ObservedVersion}.", report);
        Assert.Contains($"Reviewed action {actionId:D}", report);
        Assert.Contains("Windows build 26300", report);
        Assert.Contains("0 applicable, nonhidden offers", report);
        Assert.Contains("System volume free space 24000000000 bytes", report);
        Assert.Contains("System volume capacity 64000000000 bytes", report);
        if (matchingVersion)
        {
            Assert.Contains("Cold-boot verification and finalization are still required for handover", report);
        }
        else
        {
            Assert.Contains("an unmet or unresolved requirement", report);
            Assert.Contains("The installed package did not match the approved version or scope", report);
            Assert.Contains("POL-SEC-001. Evidence was too old for this pass. Blocks profile verification.", report);
        }

        var historical = WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null, [], null,
            verification with { IsCurrent = false });
        Assert.Contains("saved final profile verification is historical", historical);
        Assert.DoesNotContain("final profile checks passed", historical);
        Assert.Throws<ArgumentException>(() => WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null,
            [], null, verification with { JobId = new JobId("unrelated-job") }));

        var checkpoint = new ColdBootCheckpoint(Guid.Parse("2d646ca6-cb7b-48ee-a771-04373435d3d4"), SampleTime.AddMinutes(-5), 7);
        var pending = verification with
        {
            Result = null,
            ColdBootCheckpoint = checkpoint,
            ColdBootState = ColdBootVerificationState.AwaitingShutdown,
        };
        var checkpointReport = WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null, [], null, pending);
        Assert.Contains("Cold-boot checkpoint", checkpointReport);
        Assert.Contains("Checkpoint saved at", checkpointReport);
        Assert.Contains("shutdown /s /t 0", checkpointReport);
        Assert.Contains("Windows boot session 7", checkpointReport);
        Assert.DoesNotContain("Independent final profile verification", checkpointReport);

        var postBoot = verification with
        {
            ColdBootCheckpoint = checkpoint,
            ColdBootObservation = new ColdBootObservation(8, SampleTime.AddMinutes(-1), ColdBootKind.OrderlyPowerOff),
            ColdBootState = ColdBootVerificationState.ReadyToVerify,
            IsPostBoot = true,
        };
        var withoutProbe = WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null, [], null, postBoot);
        Assert.Contains("No result was recorded. Blocks post-boot verification", withoutProbe);
        Assert.Contains("an unmet or unresolved requirement", withoutProbe);
        Assert.DoesNotContain("required launch probes passed", withoutProbe);

        var launch = matchingVersion ? ProfileVerificationState.Passed : ProfileVerificationState.Unavailable;
        var observed = new ProfileVerificationResult(result.StartedAtUtc, result.FinishedAtUtc, false,
            result.ProfileSha256, result.PolicySha256, result.Checks, [capability with { LaunchProbe = launch }],
            result.PolicyFindings, result.WindowsBuild, result.FreeBytes, result.VolumeCapacityBytes, result.UpdateOfferCount,
            result.EligibleUpdateOfferCount, result.UpdateDisposition, result.UpdateRecoveryPending);
        var completed = WorkshopReportRenderer.RenderWithPackageRecords(Stored(), null, null, [], null, postBoot with { Result = observed });
        Assert.Contains("Independent post-boot profile verification", completed);
        Assert.Contains("Windows recorded an orderly shutdown and full startup", completed);
        Assert.Contains("Windows boot session 8", completed);
        Assert.Contains("A physical power-cycle claim requires separate evidence", completed);
        Assert.Contains(matchingVersion ? "Post-boot launch probe. Verified." : "Post-boot launch probe. Evidence unavailable.", completed);
        Assert.Equal(matchingVersion, completed.Contains("required launch probes passed", StringComparison.Ordinal));
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
