using System.IO;
using System.Text.Json;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Packages;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.UI.Features.PlanReview;

internal static class WorkshopPackageProfile
{
    private const string ResourcePrefix = "ThirdLife.UI.Features.PlanReview.PackageSetup.";
    private const string CatalogSha256 = "bf8db490a32f3a71ba6ce35c0a24ef91700d0f97536281fd1d2f69c395c90c89";
    private const string ProfileSha256 = "675d6e038c779deadec6d14e08fb71ca1c0893a2db537afaf1d0d6186837aa31";

    public static bool IncludesLaunchProbe(ProposedPlan plan) =>
        plan.CatalogSha256 == CatalogSha256 && plan.ProfileSha256 == ProfileSha256;

    public static bool IncludesLaunchProbe(JournalPlanSnapshot plan) =>
        !plan.SyntheticData && plan.CatalogSha256 == CatalogSha256 && plan.ProfileSha256 == ProfileSha256;

    public static PlanProfileOption? Load(DateTimeOffset asOfUtc)
    {
        const string policySha256 = "a31ca86348602d6d378f96502627525c9a3f344f4be6f02afcc9bdb286fedb59";
        try
        {
            var catalog = CatalogSchema.Parse(WorkshopPlanCandidate.ReadPinned(ResourcePrefix + "catalog.json",
                CatalogSchema.MaximumDocumentBytes, CatalogSha256), asOfUtc);
            var profile = ProfileSchema.Parse(WorkshopPlanCandidate.ReadPinned(ResourcePrefix + "profile.json",
                ProfileSchema.MaximumDocumentBytes, ProfileSha256), catalog);
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
