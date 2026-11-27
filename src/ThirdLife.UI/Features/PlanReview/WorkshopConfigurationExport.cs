using System.Collections.Immutable;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.UI.Features.Disposition;

namespace ThirdLife.UI.Features.PlanReview;

internal sealed record WorkshopConfigurationFile(string FileName, string ContentSha256, ImmutableArray<byte> Utf8Json);

/// <summary>Exact validated configuration bytes, with no destination or adoption operation.</summary>
internal sealed class WorkshopConfigurationExport
{
    private WorkshopConfigurationExport(PlanProfileOption option, PolicySnapshot policy)
    {
        ProfileId = option.Profile.ProfileId;
        ProfileVersion = option.Profile.Version;
        SyntheticData = option.Profile.SyntheticData;
        Files =
        [
            new("profile.json", option.Profile.ContentSha256, option.Profile.Utf8Json),
            new("catalog.json", option.Catalog.ContentSha256, option.Catalog.Utf8Json),
            new("policy.json", policy.Document.ContentSha256, policy.Utf8Json),
        ];
    }

    public string ProfileId { get; }
    public string ProfileVersion { get; }
    public bool SyntheticData { get; }
    public ImmutableArray<WorkshopConfigurationFile> Files { get; }

    public static WorkshopConfigurationExport? ForOption(PlanProfileOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        var policy = option.PackagePolicy ?? WorkshopPolicyCandidate.ForDeviceClass(PolicyDeviceClass.Laptop);
        if (policy is not { Document: { IsCurrentSchema: true } document } ||
            option.Profile.Policy.Id != document.PolicyId || option.Profile.Policy.Version != document.Version ||
            option.Profile.SyntheticData != document.SyntheticData || option.Profile.SyntheticData != option.Catalog.SyntheticData ||
            option.Profile.CatalogContentSha256 != option.Catalog.ContentSha256)
        {
            return null;
        }

        return new(option, policy);
    }
}
