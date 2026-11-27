using ThirdLife.Policy.Schema;
using ThirdLife.Reports.Configuration;
using ThirdLife.UI.Features.Disposition;

namespace ThirdLife.UI.Features.PlanReview;

internal static class WorkshopConfigurationExport
{
    public static ConfigurationArchivePreview? ForOption(PlanProfileOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        var policy = option.PackagePolicy ?? WorkshopPolicyCandidate.ForDeviceClass(PolicyDeviceClass.Laptop);
        return policy is null ? null : ConfigurationArchivePreview.Create(option.Profile, option.Catalog, policy);
    }
}
