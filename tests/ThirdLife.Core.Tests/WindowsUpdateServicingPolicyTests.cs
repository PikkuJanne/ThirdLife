using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Core.Tests;

public sealed class WindowsUpdateServicingPolicyTests
{
    private const WindowsUpdateServicingRoute Qualification = WindowsUpdateServicingRoute.SupervisedQualification;
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly WindowsUpdateCandidate Exact = new(
        new(new Guid("ce584c75-cbc9-497c-a57f-725e85d5bb49"), 7), WindowsUpdateClass.Security, 400);
    private static readonly WindowsUpdateServicingTarget Target = new(26300, 9700, 0x30);
    private static readonly WindowsUpdateServicingReview Review = new(Exact, Target, 600, 200,
        new string('a', 64), new string('b', 64), Qualification, new string('c', 64), Now.AddDays(-1), Now.AddDays(1));
    private static readonly WindowsUpdatePreparation Observation = new(Exact, WindowsUpdateStatus.Succeeded, Now,
        RebootRequired: false, ServicingSupported: true, PowerReady: true, SystemAvailableBytes: 1_200, CacheAvailableBytes: 1_200,
        CacheSharesSystemVolume: true, EligibilityReason: WindowsUpdateEligibilityReason.Eligible, RemovalSupported: true, Target: Target);

    [Fact]
    public void SyntheticReviewCannotSelectProductionPolicyOrAuthorizeAnOffer()
    {
        Assert.Null(WindowsUpdateServicingPolicy.CurrentQualificationPolicySha256(Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.CurrentQualificationSummary(Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.CurrentQualificationOrganizationPolicySha256(Exact, Now));
        Assert.False(WindowsUpdateServicingPolicy.MatchesQualificationOrganizationPolicy(Exact, Review.PolicySha256, Review.OrganizationPolicySha256, Now));
        Assert.Equal(WindowsUpdateServicingDecision.NoEffectivePolicy, WindowsUpdateServicingPolicy.EvaluateQualification(
            Exact, Review.PolicySha256, Observation, Now, Now));
        Assert.Same(Review, WindowsUpdateServicingPolicy.FindEffective([Review], Qualification, Exact, Review.EffectiveFrom));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review], Qualification, Exact, Review.EffectiveUntil));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review, Review], Qualification, Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review with { RollbackReserveBytes = 0 }], Qualification, Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review with { RouteAuthorityEvidenceSha256 = "" }], Qualification, Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review with { RecoveryEvidenceSha256 = "unreviewed" }], Qualification, Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review], Qualification, new(Exact.Identity, WindowsUpdateClass.Driver, 400), Now));
    }

    [Fact]
    public void AuthorityForOneRouteCannotAuthorizeTheOtherOrCreateAmbiguityInItsScope()
    {
        var ordinary = Review with { Route = WindowsUpdateServicingRoute.OrdinaryDispatch };
        Assert.Null(ordinary.QualificationSummary());
        Assert.Null((Review with { RouteAuthorityEvidenceSha256 = "" }).QualificationSummary());
        Assert.Equal(new WindowsUpdateQualificationSummary(Review.PolicySha256, Review.OrganizationPolicySha256,
            Review.InstallWorkingBytes, Review.RollbackReserveBytes, Review.EffectiveUntil), Review.QualificationSummary());
        Assert.NotEqual(Review.PolicySha256, ordinary.PolicySha256);
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([ordinary], Qualification, Exact, Now));
        Assert.Null(WindowsUpdateServicingPolicy.FindEffective([Review], ordinary.Route, Exact, Now));
        Assert.Same(Review, WindowsUpdateServicingPolicy.FindEffective([ordinary, Review], Qualification, Exact, Now));
        Assert.Same(ordinary, WindowsUpdateServicingPolicy.FindEffective([ordinary, Review], ordinary.Route, Exact, Now));
        Assert.Equal(WindowsUpdateServicingDecision.PolicyChanged, Review.Evaluate(ordinary.PolicySha256, Observation, Now, Now));
        Assert.False((Review with { Route = default }).IsComplete);
    }

    [Fact]
    public void ChangingReviewedScopeBudgetRecoveryProvenanceOrRouteAuthorityChangesItsIdentity()
    {
        var variants = new[]
        {
            Review with { Exact = new(new(Exact.Identity.UpdateId, 8), Exact.Classification, 400) },
            Review with { Exact = new(Exact.Identity, WindowsUpdateClass.Critical, 400) },
            Review with { Exact = new(Exact.Identity, Exact.Classification, 401) },
            Review with { Target = Target with { BuildRevision = 9701 } },
            Review with { Target = Target with { Edition = 4 } },
            Review with { InstallWorkingBytes = 601 },
            Review with { RollbackReserveBytes = 201 },
            Review with { OrganizationPolicySha256 = new string('f', 64) },
            Review with { RecoveryEvidenceSha256 = new string('f', 64) },
            Review with { RouteAuthorityEvidenceSha256 = new string('f', 64) },
            Review with { EffectiveUntil = Review.EffectiveUntil.AddTicks(1) },
        };
        Assert.Equal("4b77a0563cd4133f25baeeaa33477c68526e95d21f4e65eb1ed84023374b7b8b", Review.PolicySha256);
        foreach (var changed in variants)
        {
            Assert.NotEqual(Review.PolicySha256, changed.PolicySha256);
            Assert.Equal(WindowsUpdateServicingDecision.PolicyChanged, changed.Evaluate(Review.PolicySha256, Observation, Now, Now));
        }
    }

    [Fact]
    public void EachPhaseRequiresFreshExactProviderFactsAndTheReviewedWindowsRecoveryProcedure()
    {
        Assert.Equal(WindowsUpdateServicingDecision.Allowed, Decide(Observation));
        var invalid = new[]
        {
            Observation with { Exact = new(Exact.Identity, WindowsUpdateClass.Security, 401) },
            Observation with { Status = WindowsUpdateStatus.Partial },
            Observation with { EligibilityReason = WindowsUpdateEligibilityReason.Bundle },
            Observation with { ServicingSupported = null },
            Observation with { PowerReady = false },
            Observation with { RebootRequired = true },
            Observation with { ObservedAtUtc = Now.AddTicks(-1) },
            Observation with { ObservedAtUtc = Now.AddTicks(1) },
        };
        foreach (var observation in invalid) { Assert.Equal(WindowsUpdateServicingDecision.ObservationUnavailable, Decide(observation)); }
        Assert.Equal(WindowsUpdateServicingDecision.ObservationUnavailable, Review.Evaluate(Review.PolicySha256,
            Observation, Now - WindowsUpdateServicingPolicy.ObservationWindow, Now));
        Assert.Equal(WindowsUpdateServicingDecision.TargetChanged, Decide(Observation with { Target = Target with { BuildRevision = 9701 } }));
        Assert.Equal(WindowsUpdateServicingDecision.TargetChanged, Decide(Observation with { Target = null }));
        Assert.Equal(WindowsUpdateServicingDecision.RecoveryUnavailable, Decide(Observation with { RemovalSupported = false }));
        Assert.Equal(WindowsUpdateServicingDecision.RecoveryUnavailable, Decide(Observation with { RemovalSupported = null }));
    }

    [Fact]
    public void ChargesTransferInstallPeakAndRollbackReserveAgainstTheirActualVolumesWithoutOverflow()
    {
        Assert.Equal(WindowsUpdateServicingDecision.Allowed, Decide(Observation));
        Assert.Equal(WindowsUpdateServicingDecision.InsufficientSpace, Decide(Observation with { SystemAvailableBytes = 1_199 }));
        Assert.Equal(WindowsUpdateServicingDecision.InsufficientSpace, Decide(Observation with { RecommendedInstallationBytes = 601 }));
        Assert.Equal(WindowsUpdateServicingDecision.SpaceUnavailable, Decide(Observation with { CacheSharesSystemVolume = null }));
        Assert.Equal(WindowsUpdateServicingDecision.SpaceUnavailable, Decide(Observation with { CacheAvailableBytes = null }));
        var split = Observation with { CacheSharesSystemVolume = false, SystemAvailableBytes = 800, CacheAvailableBytes = 400 };
        Assert.Equal(WindowsUpdateServicingDecision.Allowed, Decide(split));
        Assert.Equal(WindowsUpdateServicingDecision.InsufficientSpace, Decide(split with { CacheAvailableBytes = 399 }));
        Assert.Equal(WindowsUpdateServicingDecision.InsufficientSpace, Decide(split with { SystemAvailableBytes = 799 }));
        var excessive = Review with { RollbackReserveBytes = long.MaxValue, InstallWorkingBytes = long.MaxValue };
        Assert.Equal(WindowsUpdateServicingDecision.InsufficientSpace, excessive.Evaluate(excessive.PolicySha256,
            Observation with { CacheAvailableBytes = ulong.MaxValue, SystemAvailableBytes = ulong.MaxValue }, Now, Now));
    }

    private static WindowsUpdateServicingDecision Decide(WindowsUpdatePreparation observation) =>
        Review.Evaluate(Review.PolicySha256, observation, Now, Now);
}
