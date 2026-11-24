using System.Collections.Immutable;
using System.Text.Json;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Versioning;

public sealed class PolicySnapshot
{
    private PolicySnapshot(PolicyDocument document, ImmutableArray<byte> utf8Json)
    {
        Document = document;
        Utf8Json = utf8Json;
    }

    public PolicyDocument Document { get; }

    public ImmutableArray<byte> Utf8Json { get; }

    public static PolicySnapshot Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > PolicySchema.MaximumDocumentBytes)
        {
            throw new JsonException("The policy snapshot exceeds the supported input limits.");
        }

        var owned = utf8Json.ToArray();
        return new PolicySnapshot(PolicySchema.Parse(owned), ImmutableArray.CreateRange(owned));
    }

    public static PolicySnapshot Restore(ReadOnlyMemory<byte> utf8Json, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(expectedSha256);
        var snapshot = Parse(utf8Json);
        if (!string.Equals(snapshot.Document.ContentSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new JsonException("The saved policy bytes do not match their recorded SHA-256 digest.");
        }

        return snapshot;
    }
}
