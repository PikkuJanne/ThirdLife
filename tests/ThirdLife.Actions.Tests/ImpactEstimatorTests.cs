using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;

namespace ThirdLife.Actions.Tests;

public sealed class ImpactEstimatorTests
{
    [Fact]
    public void EstimatesEachKnownImpactAndPreservesItsCompiledActionContract()
    {
        var plan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            SetBehavior(catalog, 2_048, 8_192, "required", "optional"));
        Assert.Null(plan.BlockedReason);

        var impact = ImpactEstimator.Estimate(plan);

        Assert.Equal(plan.ContentSha256, impact.PlanSha256);
        var planned = Assert.Single(plan.Actions);
        var action = Assert.Single(impact.Actions);
        Assert.Equal(planned.Request.ActionId, action.ActionId);
        Assert.Equal(planned.CapabilityId, action.CapabilityId);
        Assert.Equal(planned.ReasonCode, action.ReasonCode);
        Assert.Equal(CapabilityIntent.Essential, action.Intent);
        Assert.Equal(ActionScope.Machine, action.Scope);
        Assert.Equal(ActionPrivilege.Elevated, action.Privilege);
        Assert.Equal(2_048L, action.DownloadBytes);
        Assert.Equal(8_192L, action.InstalledBytes);
        Assert.Equal(RestartEstimate.Required, action.Restart);
        Assert.Equal(ActionNetworkUse.SoftwareDownload, action.NetworkCategory);
        Assert.Equal(NetworkEstimate.Optional, action.Network);
        Assert.Equal(ActionRollback.ManualReview, action.Rollback);
        Assert.Equal(ActionVerification.ExactPackageIdentity, action.Verification);
        AssertTotal(impact.Download, 2_048, 0, 2_048);
        AssertTotal(impact.Installation, 8_192, 0, 8_192);
    }

    [Fact]
    public void TotalsKeepKnownSubtotalsAndUnknownActionsDistinct()
    {
        var plan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
        {
            SetBehavior(catalog, 1_024, 4_096, "possible", "required");
            var apps = catalog["applications"]!.AsArray();
            var unknown = apps[0]!.DeepClone();
            unknown["id"] = "fictional.document-editor";
            unknown["capability_id"] = "document_editing";
            unknown["package"]!["package_id"] = "Fictional.Tests.DocumentEditor";
            unknown["verification"]!["expected_package_id"] = "Fictional.Tests.DocumentEditor";
            unknown["behavior"]!["download_bytes"] = null;
            unknown["behavior"]!["installed_bytes"] = null;
            apps.Add(unknown);
        });
        Assert.Null(plan.BlockedReason);

        var impact = ImpactEstimator.Estimate(plan);

        Assert.Equal(plan.Actions.Select(action => action.Request.ActionId), impact.Actions.Select(action => action.ActionId));
        var known = Assert.Single(impact.Actions, action => action.CapabilityId == "web_browsing");
        Assert.Equal(1_024L, known.DownloadBytes);
        Assert.Equal(4_096L, known.InstalledBytes);
        Assert.Equal(RestartEstimate.Possible, known.Restart);
        Assert.Equal(NetworkEstimate.Required, known.Network);
        var unknown = Assert.Single(impact.Actions, action => action.CapabilityId == "document_editing");
        Assert.Null(unknown.DownloadBytes);
        Assert.Null(unknown.InstalledBytes);
        AssertTotal(impact.Download, 1_024, 1, null);
        AssertTotal(impact.Installation, 4_096, 1, null);
    }

    [Fact]
    public void KeepsWhollyUnknownImpactSeparateFromKnownZero()
    {
        var plan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            SetBehavior(catalog, null, null, "unknown", "unknown"));
        Assert.Null(plan.BlockedReason);

        var impact = ImpactEstimator.Estimate(plan);

        var action = Assert.Single(impact.Actions);
        Assert.Null(action.DownloadBytes);
        Assert.Null(action.InstalledBytes);
        Assert.Equal(RestartEstimate.Unknown, action.Restart);
        Assert.Equal(ActionNetworkUse.SoftwareDownload, action.NetworkCategory);
        Assert.Equal(NetworkEstimate.Unknown, action.Network);
        AssertTotal(impact.Download, 0, 1, null);
        AssertTotal(impact.Installation, 0, 1, null);
    }

    [Fact]
    public void ExposesAContradictionWithoutDiscardingKnownByteEstimates()
    {
        var plan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            SetBehavior(catalog, 256, 512, "possible", "none"));
        Assert.Null(plan.BlockedReason);

        var impact = ImpactEstimator.Estimate(plan);

        var action = Assert.Single(impact.Actions);
        Assert.Equal(ActionNetworkUse.SoftwareDownload, action.NetworkCategory);
        Assert.Equal(NetworkEstimate.Conflicting, action.Network);
        Assert.Equal(256L, action.DownloadBytes);
        Assert.Equal(512L, action.InstalledBytes);
        AssertTotal(impact.Download, 256, 0, 256);
        AssertTotal(impact.Installation, 512, 0, 512);
    }

    [Fact]
    public void ReportsKnownZeroForNoOpsEvenWhenTheirCatalogMetadataIsUnknown()
    {
        var plan = PlanResolverTests.CreateSyntheticPlan();
        Assert.Null(plan.BlockedReason);
        Assert.NotEmpty(plan.Actions);
        Assert.All(plan.Actions, action =>
        {
            Assert.Null(action.CatalogEntry.Behavior.DownloadBytes);
            Assert.Null(action.CatalogEntry.Behavior.InstalledBytes);
        });

        var impact = ImpactEstimator.Estimate(plan);

        Assert.Equal(plan.ContentSha256, impact.PlanSha256);
        Assert.Equal(plan.Actions.Length, impact.Actions.Length);
        Assert.All(impact.Actions, action =>
        {
            Assert.Equal(0L, action.DownloadBytes);
            Assert.Equal(0L, action.InstalledBytes);
            Assert.Equal(RestartEstimate.None, action.Restart);
            Assert.Equal(ActionNetworkUse.None, action.NetworkCategory);
            Assert.Equal(NetworkEstimate.None, action.Network);
            Assert.Equal(ActionRollback.NoChanges, action.Rollback);
            Assert.Equal(ActionVerification.NoMachineChange, action.Verification);
        });
        AssertTotal(impact.Download, 0, 0, 0);
        AssertTotal(impact.Installation, 0, 0, 0);
    }

    [Fact]
    public void BlockedPlansHaveNoActionImpact()
    {
        var blocked = PlanResolverTests.CreateSyntheticPlan(archived: true);
        Assert.Equal(PlanBlockReason.SanitizationRequired, blocked.BlockedReason);

        var impact = ImpactEstimator.Estimate(blocked);

        Assert.Equal(blocked.ContentSha256, impact.PlanSha256);
        Assert.Empty(impact.Actions);
        AssertTotal(impact.Download, 0, 0, 0);
        AssertTotal(impact.Installation, 0, 0, 0);
    }

    [Fact]
    public void CalculatingImpactForANewCatalogDoesNotRefreshTheEarlierSnapshot()
    {
        var priorPlan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
            SetBehavior(catalog, 128, 256, "possible", "optional"));
        var priorImpact = ImpactEstimator.Estimate(priorPlan);
        var updatedPlan = PlanResolverTests.CreateReviewedPlan(adjustCatalog: catalog =>
        {
            catalog["version"] = "1.0.1";
            SetBehavior(catalog, 512, null, "required", "required");
        });
        Assert.Null(priorPlan.BlockedReason);
        Assert.Null(updatedPlan.BlockedReason);

        var updatedImpact = ImpactEstimator.Estimate(updatedPlan);
        var recomputedPrior = ImpactEstimator.Estimate(priorPlan);

        Assert.NotEqual(priorImpact.PlanSha256, updatedImpact.PlanSha256);
        Assert.Equal(priorImpact.PlanSha256, recomputedPrior.PlanSha256);
        Assert.Equal(priorImpact.Download, recomputedPrior.Download);
        Assert.Equal(priorImpact.Installation, recomputedPrior.Installation);
        AssertTotal(recomputedPrior.Download, 128, 0, 128);
        AssertTotal(recomputedPrior.Installation, 256, 0, 256);
        AssertTotal(updatedImpact.Download, 512, 0, 512);
        AssertTotal(updatedImpact.Installation, 0, 1, null);
    }

    private static void SetBehavior(JsonObject catalog, long? download, long? installed, string reboot, string network)
    {
        var behavior = catalog["applications"]![0]!["behavior"]!;
        behavior["download_bytes"] = JsonValue.Create(download);
        behavior["installed_bytes"] = JsonValue.Create(installed);
        behavior["reboot"] = reboot;
        behavior["network_use"] = network;
    }

    private static void AssertTotal(ByteEstimateTotal estimate, long knownBytes, int unknownActions, long? totalBytes)
    {
        Assert.Equal(knownBytes, estimate.KnownBytes);
        Assert.Equal(unknownActions, estimate.UnknownActionCount);
        Assert.Equal(totalBytes, estimate.TotalBytes);
    }
}
