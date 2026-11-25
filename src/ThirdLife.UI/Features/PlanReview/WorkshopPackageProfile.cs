using System.IO;
using System.Text.Json;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Packages;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.UI.Features.PlanReview;

internal static class WorkshopPackageProfile
{
    private const string ResourcePrefix = "ThirdLife.UI.Features.PlanReview.PackageSetup.";

    public static PlanProfileOption? Load(DateTimeOffset asOfUtc)
    {
        const string policySha256 = "a31ca86348602d6d378f96502627525c9a3f344f4be6f02afcc9bdb286fedb59";
        try
        {
            var catalog = CatalogSchema.Parse(WorkshopPlanCandidate.ReadPinned(ResourcePrefix + "catalog.json",
                CatalogSchema.MaximumDocumentBytes, "11e9948a1e100c174487aed08d71f3314b3c83299891bf2de8c3f3e6b4dd87f4"), asOfUtc);
            var profile = ProfileSchema.Parse(WorkshopPlanCandidate.ReadPinned(ResourcePrefix + "profile.json",
                ProfileSchema.MaximumDocumentBytes, "276e13b23d75cb81f3e9eb06ea8d60ae64b8c70a79225db8a3bc7d4b76d027b5"), catalog);
            var policy = PolicySnapshot.Restore(WorkshopPlanCandidate.ReadPinned(ResourcePrefix + "policy.json",
                PolicySchema.MaximumDocumentBytes, policySha256), policySha256);
            var identity = PilotPackageService.Identity;
            if (catalog.SyntheticData || profile.SyntheticData ||
                catalog.Applications is not [var entry] ||
                entry.Package.SourceId != identity.SourceId || entry.Package.PackageId != identity.PackageId ||
                entry.Package.ExactVersion != identity.ExactVersion || entry.Review.Publisher != identity.Publisher ||
                entry.Package.InstallerSha256 != identity.InstallerSha256)
            {
                return null;
            }

            return new PlanProfileOption("Plain-text editor (machine-wide setup)", profile, catalog, policy);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }
}
