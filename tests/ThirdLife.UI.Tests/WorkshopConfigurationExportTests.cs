using System.Security.Cryptography;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Tests;

public sealed class WorkshopConfigurationExportTests
{
    private static readonly DateTimeOffset CandidateReviewTime = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("basic", true)]
    [InlineData("job-seeker", true)]
    [InlineData("plain-text-editor", false)]
    public void BundledConfigurationExportPreservesValidatedExactBytes(string profileId, bool synthetic)
    {
        var option = string.Equals(profileId, "plain-text-editor", StringComparison.Ordinal)
            ? Assert.IsType<PlanProfileOption>(WorkshopPackageProfile.Load(CandidateReviewTime))
            : Assert.Single(WorkshopPlanCandidate.Options, candidate => candidate.Profile.ProfileId == profileId);
        var export = Assert.IsType<WorkshopConfigurationExport>(WorkshopConfigurationExport.ForOption(option));
        Assert.Equal(profileId, export.ProfileId);
        Assert.Equal(option.Profile.Version, export.ProfileVersion);
        Assert.Equal(synthetic, export.SyntheticData);
        Assert.Equal(["profile.json", "catalog.json", "policy.json"], export.Files.Select(file => file.FileName));
        Assert.All(export.Files, file =>
            Assert.Equal(file.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(file.Utf8Json.AsSpan()))));

        var catalogueFile = Assert.Single(export.Files, file => file.FileName == "catalog.json");
        var profileFile = Assert.Single(export.Files, file => file.FileName == "profile.json");
        var policyFile = Assert.Single(export.Files, file => file.FileName == "policy.json");
        Assert.Equal(option.Profile.Utf8Json.AsEnumerable(), profileFile.Utf8Json);
        Assert.Equal(option.Catalog.Utf8Json.AsEnumerable(), catalogueFile.Utf8Json);
        var catalogue = CatalogSchema.Parse(catalogueFile.Utf8Json.AsMemory(), CandidateReviewTime);
        var profile = ProfileSchema.Parse(profileFile.Utf8Json.AsMemory(), catalogue);
        var policy = PolicySnapshot.Restore(policyFile.Utf8Json.AsMemory(), policyFile.ContentSha256);
        Assert.Equal(option.Profile.ContentSha256, profile.ContentSha256);
        Assert.Equal(option.Profile.CatalogContentSha256, catalogue.ContentSha256);
        Assert.Equal(profile.Policy.Id, policy.Document.PolicyId);
        Assert.Equal(profile.Policy.Version, policy.Document.Version);
        Assert.Equal(synthetic, policy.Document.SyntheticData);
    }

    [Fact]
    public void RejectsADifferentCatalogDigestOrPolicyEvenWhenInputsAreWellFormed()
    {
        var option = Assert.Single(WorkshopPlanCandidate.Options, candidate => candidate.Profile.ProfileId == "basic");
        var changedCatalog = CatalogSchema.Parse(option.Catalog.Utf8Json.Add((byte)'\n').AsMemory(), CandidateReviewTime);
        Assert.Equal(option.Catalog.CatalogId, changedCatalog.CatalogId);
        Assert.Equal(option.Catalog.Version, changedCatalog.Version);
        Assert.NotEqual(option.Catalog.ContentSha256, changedCatalog.ContentSha256);
        Assert.Null(WorkshopConfigurationExport.ForOption(option with { Catalog = changedCatalog }));

        var other = Assert.IsType<PlanProfileOption>(WorkshopPackageProfile.Load(CandidateReviewTime));
        Assert.Null(WorkshopConfigurationExport.ForOption(option with { PackagePolicy = other.PackagePolicy }));
    }
}
