using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.UI.Features.PlanReview;

internal sealed record PlanProfileOption(
    string DisplayName, ProfileDocument Profile, CatalogDocument Catalog, PolicySnapshot? PackagePolicy = null);

internal static class WorkshopPlanCandidate
{
    private const string CatalogSha256 = "60880321e29f0db5deb77c85bd10d1c5926ca1b068d930445695580891c1bdce";
    private const string BasicSha256 = "d80c1c029067ebc3f172011cf66a551eff50c7661c6daf8874aced23324130e2";
    private const string JobSeekerSha256 = "928e0a6a5072a480d416861fc1051afd5efd2fc32b40459cad4aa4c3b113eb05";
    private static readonly DateTimeOffset FixtureAsOfUtc = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<ImmutableArray<PlanProfileOption>> Candidate = new(Load);

    public static ImmutableArray<PlanProfileOption> Options => Candidate.Value;

    private static ImmutableArray<PlanProfileOption> Load()
    {
        try
        {
            var catalog = CatalogSchema.Parse(ReadPinned(
                "ThirdLife.UI.catalog.json", CatalogSchema.MaximumDocumentBytes, CatalogSha256), FixtureAsOfUtc);
            if (!catalog.SyntheticData)
            {
                return [];
            }

            var basic = ProfileSchema.Parse(
                ReadPinned("ThirdLife.UI.basic.json", ProfileSchema.MaximumDocumentBytes, BasicSha256), catalog);
            var jobSeeker = ProfileSchema.Parse(
                ReadPinned("ThirdLife.UI.job-seeker.json", ProfileSchema.MaximumDocumentBytes, JobSeekerSha256), catalog);
            return basic.SyntheticData && jobSeeker.SyntheticData
                ? [new("Basic", basic, catalog), new("Job Seeker", jobSeeker, catalog)]
                : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return [];
        }
    }

    internal static byte[] ReadPinned(string resourceName, int maximumBytes, string expectedSha256)
    {
        using var stream = typeof(WorkshopPlanCandidate).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null || stream.Length < 1 || stream.Length > maximumBytes)
        {
            throw new IOException("A bundled planning document is unavailable.");
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha256)
        {
            throw new IOException("A bundled planning document failed its integrity check.");
        }

        return bytes;
    }
}
