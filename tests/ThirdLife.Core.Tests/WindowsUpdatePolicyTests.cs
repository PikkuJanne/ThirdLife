using System.Text.Json;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Core.Tests;

public sealed class WindowsUpdatePolicyTests
{
    private static readonly WindowsUpdateIdentity Identity = new(new Guid("cfb24d33-e312-498e-b3bf-e097d840d694"), 1);
    private static readonly WindowsUpdateCandidate Security = new(Identity, WindowsUpdateClass.Security, 1024);

    [Theory]
    [InlineData(WindowsUpdateStatus.Offline)]
    [InlineData(WindowsUpdateStatus.ServiceUnavailable)]
    [InlineData(WindowsUpdateStatus.AccessDenied)]
    [InlineData(WindowsUpdateStatus.Failed)]
    [InlineData(WindowsUpdateStatus.LimitExceeded)]
    [InlineData(WindowsUpdateStatus.Partial)]
    public void IncompleteSearchCannotAuthorizeOrEstablishCurrency(WindowsUpdateStatus status)
    {
        var scan = new WindowsUpdateScan(status, [Security], false);
        Assert.False(WindowsUpdatePolicy.Matches(Security, scan));
        Assert.Equal(WindowsUpdateDisposition.NotCurrent, WindowsUpdatePolicy.Evaluate(scan, 0));
    }

    [Fact]
    public void PreparationComparesBothAffectedVolumesAndRequiresKnownPerUpdateSizing()
    {
        var exact = new WindowsUpdatePreparation(Security, WindowsUpdateStatus.Succeeded, DateTimeOffset.UnixEpoch,
            SystemAvailableBytes: 2_048, CacheAvailableBytes: 2_048, CacheSharesSystemVolume: true, RecommendedInstallationBytes: 1_024);
        Assert.True(exact.HasRecommendedSpace);
        Assert.False((exact with { CacheAvailableBytes = 2_047 }).HasRecommendedSpace);
        Assert.False((exact with { SystemAvailableBytes = 2_047 }).HasRecommendedSpace);
        var separate = exact with { CacheSharesSystemVolume = false, SystemAvailableBytes = 1_024, CacheAvailableBytes = 1_024 };
        Assert.True(separate.HasRecommendedSpace);
        Assert.False((separate with { SystemAvailableBytes = 1_023 }).HasRecommendedSpace);
        Assert.False((separate with { CacheAvailableBytes = 1_023 }).HasRecommendedSpace);
        Assert.Null((exact with { RecommendedInstallationBytes = null }).HasRecommendedSpace);
        Assert.Null((exact with { RecommendedInstallationBytes = 0 }).HasRecommendedSpace);
        Assert.Null((exact with { CacheSharesSystemVolume = null }).HasRecommendedSpace);
        Assert.Null((exact with { SystemAvailableBytes = null }).HasRecommendedSpace);
        Assert.Null((exact with { CacheAvailableBytes = null }).HasRecommendedSpace);
        Assert.Null((exact with { Status = WindowsUpdateStatus.Unavailable }).HasRecommendedSpace);
        Assert.Null((exact with { Exact = new(Identity, WindowsUpdateClass.Security) }).HasRecommendedSpace);
    }

    [Fact]
    public void AuthorizationRequiresExactFreshMetadataAndKnownRebootState()
    {
        var unbounded = new WindowsUpdateCandidate(Identity, WindowsUpdateClass.Security);
        Assert.False(WindowsUpdatePolicy.IsSelectable(unbounded));
        Assert.Equal(WindowsUpdateDisposition.NoEligibleOffers, WindowsUpdatePolicy.Evaluate(
            new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [unbounded], false), 0));
        var scan = new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [Security], false);
        Assert.True(WindowsUpdatePolicy.Matches(Security, scan));
        Assert.False(WindowsUpdatePolicy.Matches(new(new(Identity.UpdateId, 2), Security.Classification, 1024), scan));
        Assert.False(WindowsUpdatePolicy.Matches(new(Identity, WindowsUpdateClass.Critical, 1024), scan));
        Assert.False(WindowsUpdatePolicy.Matches(new(Identity, Security.Classification, null), scan));
        Assert.False(WindowsUpdatePolicy.Matches(Security, new(WindowsUpdateStatus.Succeeded, [Security], true)));
        Assert.False(WindowsUpdatePolicy.Matches(Security, new(WindowsUpdateStatus.Succeeded, [Security], null)));
    }

    [Fact]
    public void FreshScanSeparatesFinalCurrencyRestartManualReviewAndConvergenceLimit()
    {
        var remaining = new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [Security], false);
        Assert.Equal(WindowsUpdateDisposition.ApprovalRequired, WindowsUpdatePolicy.Evaluate(remaining, 2));
        Assert.Equal(WindowsUpdateDisposition.PassLimitReached, WindowsUpdatePolicy.Evaluate(remaining, WindowsUpdatePolicy.MaximumPasses));
        Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdatePolicy.Evaluate(new(WindowsUpdateStatus.Succeeded, [], false), 3));
        Assert.Equal(WindowsUpdateDisposition.RestartRequired, WindowsUpdatePolicy.Evaluate(new(WindowsUpdateStatus.Succeeded, [], true), 3));
        Assert.Equal(WindowsUpdateDisposition.NotCurrent, WindowsUpdatePolicy.Evaluate(new(WindowsUpdateStatus.Succeeded, [], null), 0));
        foreach (var classification in new[] { WindowsUpdateClass.Driver, WindowsUpdateClass.Other, WindowsUpdateClass.Restricted })
        {
            var excluded = new WindowsUpdateCandidate(Identity, classification);
            var scan = new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [excluded], false);
            Assert.False(WindowsUpdatePolicy.Matches(excluded, scan));
            Assert.Equal(WindowsUpdateDisposition.NoEligibleOffers, WindowsUpdatePolicy.Evaluate(scan, 0));
        }
    }

    [Fact]
    public void SavedEligibilityFindingsAreBoundedImmutableAndSeparateFromApproval()
    {
        var restricted = new WindowsUpdateCandidate(Identity, WindowsUpdateClass.Restricted, 2048);
        var reasons = new[] { WindowsUpdateEligibilityReason.UnsupportedHandler };
        var scan = new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [restricted], false, reasons);
        reasons[0] = WindowsUpdateEligibilityReason.Eligible;
        Assert.Equal(WindowsUpdateEligibilityReason.UnsupportedHandler, Assert.Single(scan.EligibilityReasons!));
        Assert.False(WindowsUpdatePolicy.Matches(restricted, scan));
        Assert.Throws<ArgumentException>(() => new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [restricted], false, reasons));
        Assert.Throws<ArgumentException>(() => new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [Security], false, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, [restricted], false,
            [(WindowsUpdateEligibilityReason)999]));
    }

    [Fact]
    public void StoredScanIsImmutableBoundedUnambiguousAndRoundTrips()
    {
        var input = new[] { Security };
        var scan = new WindowsUpdateScan(WindowsUpdateStatus.Succeeded, input, false);
        input[0] = new WindowsUpdateCandidate(Identity, WindowsUpdateClass.Driver);
        var options = DomainJson.CreateStrictOptions();
        var restored = JsonSerializer.Deserialize<WindowsUpdateScan>(JsonSerializer.Serialize(scan, options), options)!;
        Assert.True(WindowsUpdatePolicy.Matches(Security, restored));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateScan(WindowsUpdateStatus.Succeeded,
            [Security, new(new(Identity.UpdateId, 2), Security.Classification)], false));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateScan(WindowsUpdateStatus.Succeeded,
            new WindowsUpdateCandidate[WindowsUpdatePolicy.MaximumUpdates + 1], false));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateIdentity(Guid.Empty, 1));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateIdentity(Identity.UpdateId, 0));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateCandidate(Identity, (WindowsUpdateClass)0));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateCandidate(Identity, WindowsUpdateClass.Security, -1));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateOperation((WindowsUpdateStatus)1000, true));
        Assert.ThrowsAny<ArgumentException>(() => new WindowsUpdateOperation(WindowsUpdateStatus.Partial, true, 101));
        var partial = new WindowsUpdateOperation(WindowsUpdateStatus.Partial, true, 32);
        Assert.Equal(partial, JsonSerializer.Deserialize<WindowsUpdateOperation>(JsonSerializer.Serialize(partial, options), options));
    }
}
