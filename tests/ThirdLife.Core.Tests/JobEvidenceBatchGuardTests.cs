using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Tests;

public sealed class JobEvidenceBatchGuardTests
{
    private static readonly JobId Job = new("job-guarded");
    private static readonly SanitizationGateDecisionId Gate = new("gate-guarded");
    private static readonly DateTimeOffset Timestamp = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GuardIsOptionalAndPreservesExactDecisionIdentity()
    {
        var observation = new Observation(Metadata("evidence-observed"), new("system.synthetic"),
            EvidenceValue.FromBoolean(true), null, null);
        var ordinary = new JobEvidenceBatch(Job, [observation]);
        var guarded = new JobEvidenceBatch(Job, [observation], expectedSanitizationGateDecisionId: Gate);

        Assert.Null(ordinary.ExpectedSanitizationGateDecisionId);
        Assert.Equal(Gate, guarded.ExpectedSanitizationGateDecisionId);
        Assert.Same(observation, Assert.Single(guarded.Observations));
        Assert.Empty(guarded.SanitizationEvidence);
        Assert.Empty(guarded.HumanTests);
    }

    [Fact]
    public void GuardCannotAuthorizeSanitizationChanges()
    {
        var evidence = new SanitizationEvidence(
            Metadata("evidence-sanitized"), SanitizationState.NoDonorStorage, "external_check",
            new("operator-synthetic"), Timestamp, null, SanitizationVerificationState.Verified, "policy-1");

        var error = Assert.Throws<ArgumentException>(() => new JobEvidenceBatch(Job,
            sanitizationEvidence: [evidence], expectedSanitizationGateDecisionId: Gate));

        Assert.Equal("expectedSanitizationGateDecisionId", error.ParamName);
        Assert.Single(new JobEvidenceBatch(Job, sanitizationEvidence: [evidence]).SanitizationEvidence);
    }

    [Fact]
    public void GuardCannotAuthorizeHumanTestChanges()
    {
        var metadata = new EvidenceMetadata(new("evidence-human"), PrivacyClassification.WorkshopRestricted,
            EvidenceClassification.HumanConfirmed, new("operator-entry"), Timestamp,
            new(ProvenanceKind.HumanConfirmation, "human.test"), ValueAvailability.Available);
        var test = new HumanTestEvidence(new("test-synthetic"), Job, new("device-synthetic"), metadata,
            HumanTestResult.Pass, new("operator-synthetic"), null);

        var error = Assert.Throws<ArgumentException>(() => new JobEvidenceBatch(Job,
            humanTests: [test], expectedSanitizationGateDecisionId: Gate));

        Assert.Equal("expectedSanitizationGateDecisionId", error.ParamName);
        Assert.Single(new JobEvidenceBatch(Job, humanTests: [test]).HumanTests);
    }

    [Fact]
    public void GuardDoesNotPermitEmptyEvidenceBatch()
    {
        Assert.Throws<ArgumentException>(() => new JobEvidenceBatch(Job, expectedSanitizationGateDecisionId: Gate));
    }

    private static EvidenceMetadata Metadata(string id) => new(new(id), PrivacyClassification.WorkshopRestricted,
        EvidenceClassification.Observed, new("provider-synthetic"), Timestamp,
        new(ProvenanceKind.SyntheticFixture, "fixture.guard"), ValueAvailability.Available);
}
