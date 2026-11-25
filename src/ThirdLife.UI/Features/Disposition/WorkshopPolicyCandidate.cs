using System.IO;
using System.Text.Json;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.UI.Features.Disposition;

internal static class WorkshopPolicyCandidate
{
    private const string ResourceName = "ThirdLife.UI.community-laptop-policy.json";
    private const string CandidateSha256 = "34e60ed972a4f8860196f17f0f3c8d6412d621be786aaac82474a9911a489519";
    private static readonly Lazy<PolicySnapshot?> Candidate = new(Load);

    public static PolicySnapshot? ForDeviceClass(PolicyDeviceClass deviceClass)
    {
        var snapshot = Candidate.Value;
        return snapshot is { Document.IsCurrentSchema: true } &&
            DeviceClassPolicy.Select(deviceClass, [snapshot.Document]) is not null ? snapshot : null;
    }

    private static PolicySnapshot? Load()
    {
        using var stream = typeof(WorkshopPolicyCandidate).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null || stream.Length is < 1 or > PolicySchema.MaximumDocumentBytes)
        {
            return null;
        }

        var bytes = new byte[(int)stream.Length];
        try
        {
            stream.ReadExactly(bytes);
            return PolicySnapshot.Restore(bytes, CandidateSha256);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }
}
