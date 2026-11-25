using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;

namespace ThirdLife.UI.Features.PlanReview;

internal sealed record PlanProfileOption(string DisplayName, ProfileDocument Profile, CatalogDocument Catalog);

internal static class WorkshopPlanCandidate
{
    private const string CatalogSha256 = "60880321e29f0db5deb77c85bd10d1c5926ca1b068d930445695580891c1bdce";
    private const string BasicSha256 = "fd4c7b3a07c9d655bfb430287464b3e3ba9f8a3670ffeda4399b81798c0e72d4";
    private const string JobSeekerSha256 = "a72d3158384fa144ce7363d9727a86abe5adca4ae8451d7e9dd8c189014f0b2e";
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

    private static byte[] ReadPinned(string resourceName, int maximumBytes, string expectedSha256)
    {
        using var stream = typeof(WorkshopPlanCandidate).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null || stream.Length < 1 || stream.Length > maximumBytes)
        {
            throw new IOException("A bundled planning fixture is unavailable.");
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha256)
        {
            throw new IOException("A bundled planning fixture failed its integrity check.");
        }

        return bytes;
    }
}
