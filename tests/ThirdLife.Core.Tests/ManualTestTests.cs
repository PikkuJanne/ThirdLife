using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using FixedTimeProvider = ThirdLife.Core.Tests.JobLifecycleTests.FixedTimeProvider;
using InMemoryJobStore = ThirdLife.Core.Tests.JobLifecycleTests.InMemoryJobStore;

namespace ThirdLife.Core.Tests;

public sealed class ManualTestTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Job Job = new(new("job-fixture"), new("device-fixture"), Timestamp);
    private static readonly OperatorId Operator = new("operator-workshop");
    private static readonly JsonSerializerOptions Json = DomainJson.CreateStrictOptions();

    public static TheoryData<string> ManualCases => new(ManualTestCatalog.Cases.Select(item => item.Id));

    [Theory]
    [InlineData(HumanTestResult.Pass, EvidenceClassification.HumanConfirmed, ProvenanceKind.HumanConfirmation)]
    [InlineData(HumanTestResult.Fail, EvidenceClassification.HumanConfirmed, ProvenanceKind.HumanConfirmation)]
    [InlineData(HumanTestResult.NotRun, EvidenceClassification.NotAvailable, ProvenanceKind.SystemGenerated)]
    [InlineData(HumanTestResult.NotAvailable, EvidenceClassification.NotAvailable, ProvenanceKind.SystemGenerated)]
    public async Task ExplicitOutcomesPreserveAttributionNotesAndDistinctEvidence(
        HumanTestResult outcome,
        EvidenceClassification classification,
        ProvenanceKind provenance)
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        ManualTestUnavailableReason? reason = outcome == HumanTestResult.NotAvailable
            ? ManualTestUnavailableReason.EquipmentMissing
            : null;

        var record = await service.RecordAsync(Job.JobId, "MHT-002", outcome, Operator, "Bounded observation", reason);

        Assert.Equal(outcome, record.TestResult);
        Assert.Equal(Operator, record.OperatorId);
        Assert.Equal(Timestamp, record.Metadata.CollectedAtUtc);
        Assert.Equal(classification, record.Metadata.EvidenceClassification);
        Assert.Equal(provenance, record.Metadata.Provenance.Kind);
        Assert.Equal(PrivacyClassification.WorkshopRestricted, record.Metadata.PrivacyClassification);
        Assert.Equal("MHT-002", record.Details!.CaseId);
        Assert.Equal("Bounded observation", record.Details.Notes);
        Assert.Equal(ManualTestCatalog.ProcedureRevision, record.Details.ProcedureRevision);
        Assert.Equal(record, JsonSerializer.Deserialize<HumanTestEvidence>(JsonSerializer.Serialize(record, Json), Json));
        Assert.Equal(record, Assert.Single((await store.LoadJobAsync(Job.JobId))!.HumanTests));
    }

    [Theory]
    [MemberData(nameof(ManualCases))]
    public async Task EveryGuidedCaseRecordsOnlyApplicableUnavailableReasons(string caseId)
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        var testCase = Assert.Single(ManualTestCatalog.Cases, item => item.Id == caseId);
        Assert.False(string.IsNullOrWhiteSpace(testCase.Instructions));
        Assert.False(string.IsNullOrWhiteSpace(testCase.PassCriteria));
        Assert.False(string.IsNullOrWhiteSpace(testCase.FailCriteria));
        Assert.False(string.IsNullOrWhiteSpace(testCase.Cleanup));

        var securityReview = ManualTestCatalog.IsSecurityReview(caseId);
        var assessmentId = securityReview ? "assessment-00000000000000000000000000000001" : null;
        if (securityReview)
        {
            var intake = new SanitizationEvidence(
                new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
                    EvidenceClassification.Observed, new ProviderId("fixture-intake"), Timestamp,
                    new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "manual-fixture"), ValueAvailability.Available),
                SanitizationState.NoDonorStorage, "external_check", Operator, Timestamp, null,
                SanitizationVerificationState.Verified, "m1-intake-v1");
            await store.AppendEvidenceAsync(new JobEvidenceBatch(Job.JobId, sanitizationEvidence: [intake]));
            await new JobService(store, new FixedTimeProvider(Timestamp)).CompleteIntakeAsync(Job.JobId, intake.Metadata.EvidenceId);
        }
        var notes = securityReview ? string.Empty : "The check could not run.";
        foreach (var reason in testCase.UnavailableReasons)
        {
            var record = await service.RecordAsync(
                Job.JobId, caseId, HumanTestResult.NotAvailable, Operator, notes, reason, assessmentId);

            Assert.Equal(HumanTestResult.NotAvailable, record.TestResult);
            Assert.Equal(ValueAvailability.Unknown, record.Metadata.ValueAvailability);
            Assert.NotNull(record.LimitationCode);
        }

        foreach (var reason in Enum.GetValues<ManualTestUnavailableReason>().Except(testCase.UnavailableReasons))
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAsync(
                Job.JobId, caseId, HumanTestResult.NotAvailable, Operator, notes, reason, assessmentId));
        }

        var progress = ManualTestService.GetProgress((await store.LoadJobAsync(Job.JobId))!, assessmentId);
        var item = Assert.Single(progress.Items, candidate => candidate.Case.Id == caseId);
        Assert.Equal(testCase.UnavailableReasons.Count, item.History.Count);
        Assert.False(item.HasBlockerCandidate);
    }

    [Fact]
    public async Task ClosingAndReopeningPreservesOriginalFailureAndLinkedRetest()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var time = new FixedTimeProvider(Timestamp);
        var initial = new ManualTestService(store, time);
        Assert.Equal("MHT-001", ManualTestService.GetProgress((await store.LoadJobAsync(Job.JobId))!).NextCase!.Id);
        await initial.RecordAsync(Job.JobId, "MHT-001", HumanTestResult.Pass, Operator, "No physical safety stop");
        var failure = await initial.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Fail, Operator, "One navigation key failed");
        await initial.RecordAsync(Job.JobId, "MHT-003", HumanTestResult.NotRun, Operator, "Paused next check");

        time.Advance(TimeSpan.FromMinutes(3));
        var reopened = new ManualTestService(store, time);
        var retest = await reopened.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Pass, Operator, "Navigation keys worked on retest");

        var progress = ManualTestService.GetProgress((await store.LoadJobAsync(Job.JobId))!);
        var keyboard = Assert.Single(progress.Items, item => item.Case.Id == "MHT-002");
        Assert.Equal(new[] { failure, retest }, keyboard.History);
        Assert.Equal(retest, keyboard.Latest);
        Assert.Equal(failure.HumanTestId, retest.Details!.RetestOf);
        Assert.Equal(Timestamp.AddMinutes(3), retest.Metadata.CollectedAtUtc);
        Assert.True(keyboard.HasBlockerCandidate);
        Assert.True(progress.HasEssentialFailure);
        Assert.Equal("MHT-003", progress.NextCase!.Id);
    }

    [Fact]
    public async Task MissingOrIncompatibleReasonsAndInvalidInputNeverCreateResults()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.NotAvailable, Operator, "Reason not selected"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Pass, Operator, "No direct check", ManualTestUnavailableReason.EquipmentMissing));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", (HumanTestResult)0, Operator, "Undefined outcome"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAsync(
            Job.JobId, "MHT-019", HumanTestResult.Pass, Operator, "Cold boot belongs to a later task"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Fail, Operator, new string('a', ManualTestService.MaximumNotesLength + 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Fail, Operator, "Invalid\nmultiline note"));
        Assert.Empty((await store.LoadJobAsync(Job.JobId))!.HumanTests);
    }

    [Fact]
    public async Task ArchivedMissingAndCancelledRequestsDoNotAppend()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        await store.SetArchiveStateAsync(Job.JobId, true, Timestamp);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));

        var archived = await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Fail, Operator, "Protected archive"));
        var missing = await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
            new JobId("job-missing"), "MHT-002", HumanTestResult.Fail, Operator, "Missing job"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.Fail, Operator, "Cancelled",
            cancellationToken: new CancellationToken(canceled: true)));

        Assert.Equal("job_archived", archived.ResultCode);
        Assert.Equal("job_not_found", missing.ResultCode);
        Assert.Empty((await store.LoadJobAsync(Job.JobId))!.HumanTests);
    }

    [Fact]
    public async Task LegacyAndFixtureEvidenceRetainTheirIdentityWithoutCompletingTheGuidedCase()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var legacy = new HumanTestEvidence(
            HumanTestId.New(), Job.JobId, Job.DeviceId,
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
                EvidenceClassification.NotAvailable, new ProviderId("historical-operator"), Timestamp,
                new EvidenceProvenance(ProvenanceKind.SystemGenerated, "historical"), ValueAvailability.Unknown),
            HumanTestResult.NotRun, Operator, "deferred");
        var fixture = new HumanTestEvidence(
            HumanTestId.New(), Job.JobId, Job.DeviceId,
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.PublicReference,
                EvidenceClassification.Observed, new ProviderId("manual-test-capture"), Timestamp,
                new EvidenceProvenance(ProvenanceKind.SyntheticFixture, ManualTestCatalog.ProcedureRevision),
                ValueAvailability.Available),
            HumanTestResult.Fail, Operator, null,
            new ManualTestDetails("MHT-001", ManualTestCatalog.ProcedureRevision, "Labelled synthetic fixture"));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job.JobId, humanTests: [legacy, fixture]));

        var json = JsonSerializer.Serialize(legacy, Json);
        Assert.Contains("\"not_tested\"", json);
        Assert.DoesNotContain("\"details\"", json);
        Assert.Equal(legacy, JsonSerializer.Deserialize<HumanTestEvidence>(json, Json));
        var stored = (await store.LoadJobAsync(Job.JobId))!;
        Assert.Equal(new[] { legacy, fixture }, stored.HumanTests);
        var progress = ManualTestService.GetProgress(stored);
        Assert.False(progress.HasEssentialFailure);
        Assert.Equal("MHT-001", progress.NextCase!.Id);
        Assert.All(progress.Items, item => Assert.Empty(item.History));
    }

    [Fact]
    public async Task FixtureJobCanResumeUnavailableChecksWithoutSavingPhysicalConclusions()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var fixtureIntake = new SanitizationEvidence(
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
                EvidenceClassification.Observed, new ProviderId("fixture-intake"), Timestamp,
                new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "manual-fixture"),
                ValueAvailability.Available),
            SanitizationState.Verified, "clear", Operator, Timestamp, new MediaIdentifier("fixture-media"),
            SanitizationVerificationState.Verified, "m1-intake-v1");
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job.JobId, sanitizationEvidence: [fixtureIntake]));
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));

        foreach (var outcome in new[] { HumanTestResult.Pass, HumanTestResult.Fail })
        {
            var denied = await Assert.ThrowsAsync<JobLifecycleException>(() => service.RecordAsync(
                Job.JobId, "MHT-002", outcome, Operator, "A fixture has no physical outcome"));
            Assert.Equal("fixture_manual_result", denied.ResultCode);
        }

        var deferred = await service.RecordAsync(
            Job.JobId, "MHT-002", HumanTestResult.NotRun, Operator, "Deferred fixture scenario");
        var returned = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        var unavailable = await returned.RecordAsync(Job.JobId, "MHT-002", HumanTestResult.NotAvailable,
            Operator, "Approved equipment unavailable in fixture scenario", ManualTestUnavailableReason.EquipmentMissing);

        var job = (await store.LoadJobAsync(Job.JobId))!;
        Assert.Equal(new[] { deferred, unavailable }, job.HumanTests);
        Assert.All(job.HumanTests, attempt =>
        {
            Assert.Equal(ProvenanceKind.SyntheticFixture, attempt.Metadata.Provenance.Kind);
            Assert.Equal(EvidenceClassification.NotAvailable, attempt.Metadata.EvidenceClassification);
            Assert.Equal(ValueAvailability.Unknown, attempt.Metadata.ValueAvailability);
        });
        Assert.Equal(deferred.HumanTestId, unavailable.Details!.RetestOf);
        var progress = ManualTestService.GetProgress(job);
        var keyboard = Assert.Single(progress.Items, item => item.Case.Id == "MHT-002");
        Assert.Equal(job.HumanTests, keyboard.History);
        Assert.False(progress.HasEssentialFailure);
    }
}
