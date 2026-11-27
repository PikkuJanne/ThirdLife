using System.Text.Json.Nodes;
using ThirdLife.Actions.Planning;

namespace ThirdLife.Actions.Tests;

public sealed class PreparationPreflightTests
{
    private const long Download = 2_048;
    private const long Installed = 8_192;
    private const ulong SystemRequired =
        PreparationPreflight.SystemReserveBytes + PreparationPreflight.InstallerOverheadBytes + Installed;
    private const ulong StagingRequired = PreparationPreflight.StagingReserveBytes + Download;
    private const ulong SharedRequired = SystemRequired + StagingRequired;
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SharedVolumeCountsBothRolesAndUsesTheLowerAvailableSpace()
    {
        var impact = PackageImpact();
        var exact = new PreparationResourceSnapshot(ObservedAt, PreparationPlatform.Windows11X64,
            PreparationPower.ExternalPower, SharedRequired, SharedRequired, SharedVolume: true);

        var ready = PreparationPreflight.Evaluate(impact, exact);
        var blocked = PreparationPreflight.Evaluate(impact, exact with
        {
            ObservedAtUtc = ObservedAt.AddSeconds(1),
            StagingAvailableBytes = SharedRequired - 1,
        });

        Assert.True(ready.AllowsPreparation);
        Assert.Equal(SystemRequired, ready.SystemRequiredBytes);
        Assert.Equal(StagingRequired, ready.StagingRequiredBytes);
        Assert.Equal(SharedRequired, ready.SharedRequiredBytes);
        Assert.False(blocked.AllowsPreparation);
        Assert.Equal(ObservedAt.AddSeconds(1), blocked.ObservedAtUtc);
        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.InsufficientSharedSpace], blocked.Issues);
    }

    [Fact]
    public void SeparateVolumesRequireEachRoleToHaveItsOwnSpace()
    {
        var impact = PackageImpact();

        var ready = PreparationPreflight.Evaluate(impact, Resources());
        var systemShort = PreparationPreflight.Evaluate(impact, Resources() with { SystemAvailableBytes = SystemRequired - 1 });
        var stagingShort = PreparationPreflight.Evaluate(impact, Resources() with { StagingAvailableBytes = StagingRequired - 1 });

        Assert.True(ready.AllowsPreparation);
        Assert.Null(ready.SharedRequiredBytes);
        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.InsufficientSystemSpace], systemShort.Issues);
        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.InsufficientStagingSpace], stagingShort.Issues);
    }

    [Fact]
    public void MissingVolumeFactsCannotPassAndRemainDistinctFromZero()
    {
        var impact = PackageImpact();
        var observation = Resources() with { StagingAvailableBytes = null, SharedVolume = null };

        var unavailable = PreparationPreflight.Evaluate(impact, observation);
        var full = PreparationPreflight.Evaluate(impact, observation with { StagingAvailableBytes = 0, SharedVolume = false });

        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.StorageLayoutUnavailable, PreparationPreflightIssue.StagingSpaceUnavailable],
            unavailable.Issues);
        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.InsufficientStagingSpace], full.Issues);
    }

    [Fact]
    public void UnknownEstimatesDeferWorkWhileShowingKnownSpaceNeeds()
    {
        var impact = PackageImpact(null, null);

        var result = PreparationPreflight.Evaluate(impact, Resources());

        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.DownloadEstimateUnavailable, PreparationPreflightIssue.InstallationEstimateUnavailable],
            result.Issues);
        Assert.Null(impact.Download.TotalBytes);
        Assert.Null(impact.Installation.TotalBytes);
        Assert.Equal(SystemRequired - Installed, result.SystemRequiredBytes);
        Assert.Equal(StagingRequired - Download, result.StagingRequiredBytes);
    }

    [Fact]
    public void ReviewedFootprintCanSupplyAnExactInstallerAllowanceWithoutChangingItsCatalogEstimate()
    {
        var impact = PackageImpact(installed: null);

        var ready = PreparationPreflight.Evaluate(impact, Resources(), reviewedInstalledFootprint: Installed);
        var blocked = PreparationPreflight.Evaluate(impact, Resources(), reviewedInstalledFootprint: Installed + 1);

        Assert.True(ready.AllowsPreparation);
        Assert.Null(impact.Installation.TotalBytes);
        Assert.Equal(Installed, ready.ReviewedInstalledFootprintBytes);
        Assert.Equal(SystemRequired, ready.SystemRequiredBytes);
        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.InsufficientSystemSpace], blocked.Issues);
    }

    [Fact]
    public void RecheckingAStagedInstallerCountsOnlyBytesStillNeeded()
    {
        var impact = PackageImpact();
        var remaining = SharedRequired - (ulong)Download;
        var afterDownload = Resources() with
        {
            SharedVolume = true,
            SystemAvailableBytes = remaining,
            StagingAvailableBytes = remaining,
        };

        Assert.False(PreparationPreflight.Evaluate(impact, afterDownload).AllowsPreparation);
        var ready = PreparationPreflight.Evaluate(impact, afterDownload, downloadAlreadyStaged: true);

        Assert.True(ready.AllowsPreparation);
        Assert.Equal(remaining, ready.SharedRequiredBytes);
        Assert.Equal(PreparationPreflight.StagingReserveBytes, ready.StagingRequiredBytes);
    }

    [Fact]
    public void SingleInstallerAllowancesDoNotCoverUnknownBytesInAMultiPackagePlan()
    {
        var plan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
        {
            var apps = catalog["applications"]!.AsArray();
            SetBehavior(apps[0]!, Download, Installed);
            var unknown = apps[0]!.DeepClone();
            unknown["id"] = "fictional.document-editor";
            unknown["capability_id"] = "document_editing";
            unknown["package"]!["package_id"] = "Fictional.Tests.DocumentEditor";
            unknown["verification"]!["expected_package_id"] = "Fictional.Tests.DocumentEditor";
            SetBehavior(unknown, Download, null);
            apps.Add(unknown);
        });

        var result = PreparationPreflight.Evaluate(ImpactEstimator.Estimate(plan),
            Resources() with { StagingAvailableBytes = StagingRequired + Download },
            downloadAlreadyStaged: true, reviewedInstalledFootprint: Installed);

        Assert.Equal<PreparationPreflightIssue>([PreparationPreflightIssue.InstallationEstimateUnavailable], result.Issues);
        Assert.Equal(SystemRequired, result.SystemRequiredBytes);
        Assert.Equal(StagingRequired + Download, result.StagingRequiredBytes);
        Assert.Null(result.ReviewedInstalledFootprintBytes);
    }

    [Theory]
    [InlineData(PreparationPlatform.Unknown, PreparationPreflightIssue.PlatformUnavailable)]
    [InlineData(PreparationPlatform.UnsupportedOperatingSystem, PreparationPreflightIssue.UnsupportedOperatingSystem)]
    [InlineData(PreparationPlatform.UnsupportedArchitecture, PreparationPreflightIssue.UnsupportedArchitecture)]
    public void ChecksTheCurrentPlatformAgainstPackageRequirements(PreparationPlatform platform, PreparationPreflightIssue issue)
    {
        var result = PreparationPreflight.Evaluate(PackageImpact(), Resources() with { Platform = platform });

        Assert.Equal<PreparationPreflightIssue>([issue], result.Issues);
        Assert.False(result.AllowsPreparation);
    }

    [Theory]
    [InlineData(PreparationPower.Unknown, PreparationPreflightIssue.PowerUnavailable)]
    [InlineData(PreparationPower.OnBattery, PreparationPreflightIssue.ExternalPowerRequired)]
    [InlineData(PreparationPower.CriticallyLowBattery, PreparationPreflightIssue.BatteryCriticallyLow)]
    public void DefersWhenLivePowerNoLongerSupportsTheOperation(PreparationPower power, PreparationPreflightIssue issue)
    {
        var impact = PackageImpact();
        var ready = PreparationPreflight.Evaluate(impact, Resources() with { Power = PreparationPower.ExternalPower });
        var blocked = PreparationPreflight.Evaluate(impact, Resources() with { Power = power });

        Assert.True(ready.AllowsPreparation);
        Assert.Equal<PreparationPreflightIssue>([issue], blocked.Issues);
        Assert.False(blocked.AllowsPreparation);
    }

    [Fact]
    public void SyntheticNoOpsNeedNoInstallationCapacityOrPowerEvidence()
    {
        var impact = ImpactEstimator.Estimate(PlanResolverTests.CreateSyntheticPlan());
        var observation = new PreparationResourceSnapshot(ObservedAt, PreparationPlatform.Unknown,
            PreparationPower.Unknown, null, null, null);

        var result = PreparationPreflight.Evaluate(impact, observation);

        Assert.NotEmpty(impact.Actions);
        Assert.True(result.AllowsPreparation);
        Assert.Null(result.SystemRequiredBytes);
        Assert.Null(result.StagingRequiredBytes);
    }

    private static PreparationResourceSnapshot Resources() => new(ObservedAt, PreparationPlatform.Windows11X64,
        PreparationPower.NoBattery, SystemRequired, StagingRequired, SharedVolume: false);

    private static PlanImpact PackageImpact(long? download = Download, long? installed = Installed)
    {
        var plan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            SetBehavior(catalog["applications"]![0]!, download, installed));
        Assert.Null(plan.BlockedReason);
        Assert.Single(plan.Actions);
        return ImpactEstimator.Estimate(plan);
    }

    private static void SetBehavior(JsonNode application, long? download, long? installed)
    {
        var behavior = application["behavior"]!;
        behavior["download_bytes"] = JsonValue.Create(download);
        behavior["installed_bytes"] = JsonValue.Create(installed);
    }
}
