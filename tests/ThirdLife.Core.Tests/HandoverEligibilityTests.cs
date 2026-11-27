using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Tests;

public sealed class HandoverEligibilityTests
{
    private const string FinalizationRevision = "finalization-checklist-01";
    private const string OutputRevision = "artifact-version-1";
    private const string OutputDigest = "123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0";
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly HandoverReviewScope Scope = new(new JobId("job-handover"), "handover-review-1",
        HandoverEvidenceOrigin.SyntheticFixture);

    [Fact]
    public void CompleteScopedEvidenceMakesOperatorSignOffAvailable()
    {
        var result = Evaluate(CompleteEvidence());
        Assert.True(result.CanRequestSignOff);
        Assert.Empty(result.Blockers);
        Assert.Equal(Scope, result.Scope);
    }

    [Fact]
    public void EveryRequiredProjectionMustBePresent()
    {
        var evidence = CompleteEvidence();
        foreach (var item in evidence)
        {
            var result = Evaluate(evidence.Where(candidate => candidate != item).ToArray());
            Assert.False(result.CanRequestSignOff);
            Assert.Equal(new(item.Requirement, HandoverBlockReason.EvidenceMissing), Assert.Single(result.Blockers));
        }
    }

    [Theory]
    [InlineData(HandoverRequirement.Sanitization, HandoverEvidenceState.Failed, HandoverBlockReason.CheckFailed)]
    [InlineData(HandoverRequirement.Ownership, HandoverEvidenceState.Unavailable, HandoverBlockReason.EvidenceUnavailable)]
    [InlineData(HandoverRequirement.AuthorizedExceptions, HandoverEvidenceState.Unauthorized, HandoverBlockReason.AuthorizationMissing)]
    [InlineData(HandoverRequirement.EssentialActionVerification, HandoverEvidenceState.Stale, HandoverBlockReason.EvidenceStale)]
    [InlineData(HandoverRequirement.ColdBootVerification, HandoverEvidenceState.Stale, HandoverBlockReason.EvidenceStale)]
    [InlineData(HandoverRequirement.PendingRestart, HandoverEvidenceState.Failed, HandoverBlockReason.CheckFailed)]
    public void ABlockerKeepsSignOffUnavailable(HandoverRequirement requirement, HandoverEvidenceState state,
        HandoverBlockReason reason)
    {
        var result = Evaluate(WithEvidence(new(requirement, state, Scope)));
        Assert.False(result.CanRequestSignOff);
        Assert.Equal(new(requirement, reason), Assert.Single(result.Blockers));
    }

    [Fact]
    public void StaleOtherJobAndDifferentOriginEvidenceHaveNoAuthority()
    {
        var kind = HandoverRequirement.FinalProfileVerification;
        var contexts = new HandoverReviewScope[]
        {
            new(Scope.JobId, "handover-review-older", Scope.Origin),
            new(new JobId("job-other"), Scope.Version, Scope.Origin),
            new(Scope.JobId, Scope.Version, HandoverEvidenceOrigin.ActiveDevice),
        };
        foreach (var review in contexts)
        {
            var result = Evaluate(WithEvidence(new(kind, HandoverEvidenceState.Verified, review, "verification-1")));
            Assert.Equal(new(kind, HandoverBlockReason.ScopeMismatch), Assert.Single(result.Blockers));
        }
    }

    [Fact]
    public void ReadyPolicyAndCurrentUnblockedChecklistAreRequired()
    {
        var otherPolicy = HandoverEligibility.Evaluate(Scope, false,
            Disposition.HumanReviewRequired, Finalization(), CompleteEvidence());
        Assert.Equal(new(HandoverRequirement.PolicyDisposition, HandoverBlockReason.PolicyRequiresReview),
            Assert.Single(otherPolicy.Blockers));

        var incomplete = HandoverEligibility.Evaluate(Scope, false, Disposition.ReadyToPrepare,
            FinalizationChecklist.Evaluate(FinalizationRevision, []), CompleteEvidence());
        Assert.Equal(new(HandoverRequirement.Finalization, HandoverBlockReason.FinalizationIncomplete),
            Assert.Single(incomplete.Blockers));

        var stale = Evaluate(WithEvidence(new(HandoverRequirement.Finalization, HandoverEvidenceState.Verified,
            Scope, "finalization-older")));
        Assert.Equal(new(HandoverRequirement.Finalization, HandoverBlockReason.FinalizationIncomplete),
            Assert.Single(stale.Blockers));
    }

    [Fact]
    public void ConflictingSourcesAndArchivedJobsRemainExplicit()
    {
        var evidence = CompleteEvidence();
        var result = HandoverEligibility.Evaluate(Scope, true, Disposition.ReadyToPrepare,
            Finalization(), [.. evidence, evidence[0]]);
        Assert.Equal(new HandoverBlocker[]
        {
            new(HandoverRequirement.OpenJob, HandoverBlockReason.JobArchived),
            new(evidence[0].Requirement, HandoverBlockReason.EvidenceConflict),
        }, result.Blockers);
    }

    [Fact]
    public void OptionalSupportOmissionRequiresAnExactSavedChoice()
    {
        var support = HandoverRequirement.SupportBundleDecision;
        var omission = new HandoverSupportBundleOmission(Scope, "recorded-choice-1", new OperatorId("operator-support"),
            RecordedAt, SupportBundleOmissionReason.NoSupportRequest);
        Assert.True(Evaluate(WithEvidence(new(support, HandoverEvidenceState.OmittedByRecordedChoice, Scope,
            omission.Version, supportBundleOmission: omission))).CanRequestSignOff);
        Assert.Equal(new(support, HandoverBlockReason.OutputUnbound), Assert.Single(Evaluate(WithEvidence(
            new(support, HandoverEvidenceState.OmittedByRecordedChoice, Scope, omission.Version))).Blockers));
        Assert.ThrowsAny<ArgumentException>(() =>
            new HandoverRequirementEvidence(support, HandoverEvidenceState.OmittedByRecordedChoice, Scope));
        Assert.Throws<ArgumentException>(() => new HandoverRequirementEvidence(HandoverRequirement.RecipientGuide,
            HandoverEvidenceState.OmittedByRecordedChoice, Scope, "recorded-choice-2"));
    }

    [Fact]
    public void OutputRequiresExactVersionVerifiedBytesAndScope()
    {
        var guide = HandoverRequirement.RecipientGuide;
        var missingReceipt = Evaluate(WithEvidence(new(guide, HandoverEvidenceState.Verified, Scope, OutputRevision)));
        Assert.Equal(new(guide, HandoverBlockReason.OutputUnbound), Assert.Single(missingReceipt.Blockers));
        var wrongVersion = new HandoverOutputReference(HandoverOutputKind.RecipientGuide, Scope, "older-guide-1", OutputDigest);
        Assert.Equal(new(guide, HandoverBlockReason.OutputUnbound), Assert.Single(Evaluate(WithEvidence(
            new(guide, HandoverEvidenceState.Verified, Scope, OutputRevision, wrongVersion))).Blockers));
        Assert.Throws<ArgumentException>(() => new HandoverOutputReference(HandoverOutputKind.RecipientGuide, Scope,
            OutputRevision, "safe-name-only"));
    }

    [Fact]
    public void ChangedOutputBytesOrProofVersionInvalidateTheReviewedResult()
    {
        var reviewed = Evaluate(CompleteEvidence());
        Assert.True(Evaluate(CompleteEvidence().Reverse().ToArray()).MatchesReview(reviewed));
        var guide = new HandoverOutputReference(HandoverOutputKind.RecipientGuide, Scope, OutputRevision, new string('a', 64));
        var rerendered = Evaluate(WithEvidence(new(HandoverRequirement.RecipientGuide, HandoverEvidenceState.Verified,
            Scope, OutputRevision, guide)));
        Assert.True(rerendered.CanRequestSignOff);
        Assert.False(rerendered.MatchesReview(reviewed));
        var newPolicy = Evaluate(WithEvidence(new(HandoverRequirement.PolicyDisposition, HandoverEvidenceState.Verified,
            Scope, "verified-policy-2")));
        Assert.False(newPolicy.MatchesReview(reviewed));
    }

    [Fact]
    public void SignOffRetainsActorAndTheExactReviewedEvidence()
    {
        var reviewed = Evaluate(CompleteEvidence());
        var current = Evaluate(CompleteEvidence().Reverse().ToArray());
        var actor = new OperatorId("operator-release");
        var record = Assert.IsType<HandoverSignOff>(HandoverSignOff.ForReviewedState(reviewed, current, 1, actor, RecordedAt));

        Assert.Equal(1, record.Sequence);
        Assert.Equal(actor, record.OperatorId);
        Assert.Equal(RecordedAt, record.RecordedAtUtc);
        Assert.Equal(HandoverEvidenceOrigin.SyntheticFixture, record.Scope.Origin);
        Assert.True(record.IsCurrent(current));
        var reopened = Assert.IsType<HandoverSignOff>(JsonSerializer.Deserialize<HandoverSignOff>(JsonSerializer.Serialize(record)));
        Assert.True(reopened.IsCurrent(current));
        Assert.Equal(record.OperatorId, reopened.OperatorId);
        Assert.Equal(OutputDigest, record.Evidence.Single(item => item.Requirement == HandoverRequirement.RecipientGuide).Output!.Sha256);
    }

    [Fact]
    public void SignOffCannotBeRecordedForChangedEvidenceAndPriorRecordRemainsHistorical()
    {
        var reviewed = Evaluate(CompleteEvidence());
        var actor = new OperatorId("operator-release");
        var previous = Assert.IsType<HandoverSignOff>(HandoverSignOff.ForReviewedState(reviewed, reviewed, 1, actor, RecordedAt));
        var output = new HandoverOutputReference(HandoverOutputKind.RecipientGuide, Scope, OutputRevision, new string('a', 64));
        var updated = Evaluate(WithEvidence(new(HandoverRequirement.RecipientGuide, HandoverEvidenceState.Verified,
            Scope, OutputRevision, output)));
        var blocked = Evaluate(WithEvidence(new(HandoverRequirement.ColdBootVerification, HandoverEvidenceState.Stale, Scope)));

        Assert.Null(HandoverSignOff.ForReviewedState(reviewed, updated, 2, actor, RecordedAt.AddMinutes(1)));
        Assert.Null(HandoverSignOff.ForReviewedState(blocked, blocked, 2, actor, RecordedAt.AddMinutes(1)));
        Assert.False(previous.IsCurrent(updated));
        Assert.False(previous.IsCurrent(blocked));
        Assert.True(previous.IsCurrent(reviewed));
    }

    private static HandoverEligibility Evaluate(HandoverRequirementEvidence[] evidence) =>
        HandoverEligibility.Evaluate(Scope, false, Disposition.ReadyToPrepare, Finalization(), evidence);

    private static HandoverRequirementEvidence[] WithEvidence(HandoverRequirementEvidence replacement) =>
        CompleteEvidence().Select(item => item.Requirement == replacement.Requirement ? replacement : item).ToArray();

    private static HandoverRequirementEvidence[] CompleteEvidence() => Enum.GetValues<HandoverRequirement>()
        .Where(requirement => requirement != HandoverRequirement.OpenJob)
        .Select(requirement => requirement switch
        {
            HandoverRequirement.WorkshopRecord => OutputEvidence(requirement, HandoverOutputKind.WorkshopRecord),
            HandoverRequirement.RecipientGuide => OutputEvidence(requirement, HandoverOutputKind.RecipientGuide),
            HandoverRequirement.SupportBundleDecision => OutputEvidence(requirement, HandoverOutputKind.SupportBundle),
            _ => new HandoverRequirementEvidence(requirement, HandoverEvidenceState.Verified, Scope,
                requirement == HandoverRequirement.Finalization ? FinalizationRevision : "verified-record-1"),
        }).ToArray();

    private static HandoverRequirementEvidence OutputEvidence(HandoverRequirement requirement, HandoverOutputKind kind) =>
        new(requirement, HandoverEvidenceState.Verified, Scope, OutputRevision, new(kind, Scope, OutputRevision, OutputDigest));

    private static FinalizationChecklist Finalization()
    {
        var actor = new OperatorId("operator-finalization");
        var confirmations = FinalizationChecklist.Rules.Where(rule => rule.AllowsOperatorConfirmation)
            .Select((rule, index) => new FinalizationRecord(index + 1, rule.Id, FinalizationOutcome.Passed,
                null, actor, RecordedAt, FinalizationRevision)).ToArray();
        return FinalizationChecklist.Evaluate(FinalizationRevision, confirmations, FinalizationOutcome.Passed);
    }
}
