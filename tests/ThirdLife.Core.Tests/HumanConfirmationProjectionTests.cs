using ThirdLife.Core.Evidence;
using ThirdLife.Core.HumanConfirmation;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using FixedTimeProvider = ThirdLife.Core.Tests.JobLifecycleTests.FixedTimeProvider;
using InMemoryJobStore = ThirdLife.Core.Tests.JobLifecycleTests.InMemoryJobStore;

namespace ThirdLife.Core.Tests;

public sealed class HumanConfirmationProjectionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Job Job = new(new("job-projection"), new("device-projection"), Timestamp);
    private static readonly OperatorId Operator = new("operator-workshop");

    [Theory]
    [InlineData("MHT-002", "function.keyboard", HumanTestResult.Pass, "passed")]
    [InlineData("MHT-003", "function.pointing", HumanTestResult.Fail, "failed")]
    [InlineData("MHT-005", "function.display", HumanTestResult.Pass, "passed")]
    [InlineData("MHT-007", "function.audio_output", HumanTestResult.Fail, "failed")]
    [InlineData("MHT-008", "function.microphone", HumanTestResult.Pass, "passed")]
    [InlineData("MHT-009", "function.camera", HumanTestResult.Fail, "failed")]
    public async Task MappedResultsPreserveAttributionAndEvidence(
        string caseId, string evidenceKey, HumanTestResult result, string token)
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        var recorded = await service.RecordAsync(Job.JobId, caseId, result, Operator, "Recorded local observation");

        var observations = HumanConfirmationProjection.FromJob((await store.LoadJobAsync(Job.JobId))!);

        var observed = Assert.Single(observations);
        Assert.Equal(new EvidenceKey(evidenceKey), observed.EvidenceKey);
        Assert.Same(recorded.Metadata, observed.Metadata);
        Assert.Equal(Operator, observed.OperatorId);
        Assert.Equal(EvidenceValue.FromEnum(token), observed.Value);
        Assert.Null(observed.Unit);
    }

    [Theory]
    [InlineData(HumanTestResult.NotRun, null, "not_run")]
    [InlineData(HumanTestResult.NotAvailable, ManualTestUnavailableReason.EquipmentMissing, "equipment_missing")]
    public async Task LatestUnavailableResultsReplaceProjectedPassAndPreserveHistory(
        HumanTestResult nextResult, ManualTestUnavailableReason? reason, string limitation)
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        var passed = await service.RecordAsync(Job.JobId, "MHT-002", HumanTestResult.Pass, Operator, "Earlier check");
        var latest = await service.RecordAsync(Job.JobId, "MHT-002", nextResult, Operator, "Later attempt", reason);
        var job = (await store.LoadJobAsync(Job.JobId))!;

        var observation = Assert.Single(HumanConfirmationProjection.FromJob(job));

        Assert.Equal(new[] { passed, latest }, job.HumanTests);
        Assert.Equal(passed.HumanTestId, latest.Details!.RetestOf);
        Assert.Same(latest.Metadata, observation.Metadata);
        Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
        Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
        Assert.Equal(limitation, observation.LimitationCode);
        Assert.Null(observation.Value);
        Assert.Null(observation.OperatorId);
    }

    [Fact]
    public async Task AttributableNotApplicableResultRetainsItsAvailability()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var metadata = new EvidenceMetadata(
            EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.HumanConfirmed,
            new ProviderId("manual-test-capture"), Timestamp,
            new EvidenceProvenance(ProvenanceKind.HumanConfirmation, ManualTestCatalog.ProcedureRevision),
            ValueAvailability.NotApplicable);
        var result = new HumanTestEvidence(
            HumanTestId.New(), Job.JobId, Job.DeviceId, metadata, HumanTestResult.NotAvailable, Operator,
            "capability_absent", new ManualTestDetails("MHT-009", ManualTestCatalog.ProcedureRevision, "No camera"));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job.JobId, humanTests: [result]));

        var observation = Assert.Single(HumanConfirmationProjection.FromJob((await store.LoadJobAsync(Job.JobId))!));

        Assert.Same(metadata, observation.Metadata);
        Assert.Equal(ValueAvailability.NotApplicable, observation.Metadata.ValueAvailability);
        Assert.Equal(Operator, observation.OperatorId);
        Assert.Equal("capability_absent", observation.LimitationCode);
        Assert.Null(observation.Value);
    }

    [Fact]
    public async Task FixtureAttemptKeepsSyntheticUnavailableEvidence()
    {
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var intake = new SanitizationEvidence(
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
                EvidenceClassification.Observed, new ProviderId("fixture-intake"), Timestamp,
                new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "manual-fixture"), ValueAvailability.Available),
            SanitizationState.Verified, "clear", Operator, Timestamp, new MediaIdentifier("fixture-media"),
            SanitizationVerificationState.Verified, "m1-intake-v1");
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job.JobId, sanitizationEvidence: [intake]));
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        var attempt = await service.RecordAsync(Job.JobId, "MHT-005", HumanTestResult.NotAvailable, Operator,
            "No built-in display in this fixture", ManualTestUnavailableReason.CapabilityAbsent);

        var observation = Assert.Single(HumanConfirmationProjection.FromJob((await store.LoadJobAsync(Job.JobId))!));

        Assert.Same(attempt.Metadata, observation.Metadata);
        Assert.Equal(new EvidenceKey("function.display"), observation.EvidenceKey);
        Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
        Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
        Assert.Null(observation.Value);
        Assert.Null(observation.OperatorId);
    }

    [Fact]
    public async Task BroadChecksAndUnrecognizedProcedureDoNotCreateFunctionEvidence()
    {
        const string unrecognizedProcedure = "MHT-unrecognized";
        await using var store = new InMemoryJobStore();
        await store.CreateJobAsync(Job);
        var service = new ManualTestService(store, new FixedTimeProvider(Timestamp));
        foreach (var unprojectedCase in new[] { "MHT-001", "MHT-006", "MHT-010", "MHT-011" })
        {
            await service.RecordAsync(Job.JobId, unprojectedCase, HumanTestResult.Pass, Operator, "Recorded local observation");
        }

        var recognized = await service.RecordAsync(Job.JobId, "MHT-002", HumanTestResult.Pass, Operator, "Earlier check");
        var metadata = new EvidenceMetadata(
            EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.HumanConfirmed,
            new ProviderId("manual-test-capture"), Timestamp,
            new EvidenceProvenance(ProvenanceKind.HumanConfirmation, unrecognizedProcedure),
            ValueAvailability.Available);
        var later = new HumanTestEvidence(
            HumanTestId.New(), Job.JobId, Job.DeviceId, metadata, HumanTestResult.Pass, Operator, null,
            new ManualTestDetails("MHT-002", unrecognizedProcedure, "Unrecognized revision", recognized.HumanTestId));
        await store.AppendEvidenceAsync(new JobEvidenceBatch(Job.JobId, humanTests: [later]));

        Assert.Empty(HumanConfirmationProjection.FromJob((await store.LoadJobAsync(Job.JobId))!));
    }
}
