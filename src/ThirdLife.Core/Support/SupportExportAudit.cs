using System.Buffers;
using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Support;

public interface ISupportExportAuditStore
{
    Task StageSupportExportAsync(JobId jobId, SupportExportAudit intent, CancellationToken token = default);
    Task RecordSupportExportAsync(JobId jobId, SupportExportAudit completed, CancellationToken token = default);
    Task<IReadOnlyList<SupportExportAudit>> LoadSupportExportsAsync(JobId jobId, CancellationToken token = default);
}

[JsonConverter(typeof(StableStringEnumConverter<SupportExportState>))]
public enum SupportExportState
{
    [JsonStringEnumMemberName("pending")]
    Pending = 1,
    [JsonStringEnumMemberName("published")]
    Published,
    [JsonStringEnumMemberName("not_published")]
    NotPublished,
}

/// <summary>A protected receipt for an exact support archive. It carries no destination or archive content.</summary>
public sealed record SupportExportAudit
{
    private static readonly SearchValues<char> LowerHex = SearchValues.Create("0123456789abcdef");

    public SupportExportAudit(string internalSupportId, string schemaVersion, string manifestVersion, string contentSha256,
        DateTimeOffset recordedAtUtc, OperatorId operatorId, SupportExportState state, string? reviewVersion = null, OperatorId? recoveryOperatorId = null)
    {
        if (internalSupportId is not { Length: 36 } || !internalSupportId.StartsWith("SUP-", StringComparison.Ordinal) ||
            internalSupportId.AsSpan(4).ContainsAnyExcept(LowerHex) ||
            !Guid.TryParseExact(internalSupportId.AsSpan(4), "N", out var id) || id == Guid.Empty)
        {
            throw new ArgumentException("A registered opaque support code is required.", nameof(internalSupportId));
        }

        if (contentSha256 is not { Length: 64 } || contentSha256.AsSpan().ContainsAnyExcept(LowerHex))
        {
            throw new ArgumentException("The exact lowercase archive SHA-256 is required.", nameof(contentSha256));
        }

        InternalSupportId = internalSupportId;
        SchemaVersion = DomainValue.RequireVersion(schemaVersion, nameof(schemaVersion));
        ManifestVersion = DomainValue.RequireVersion(manifestVersion, nameof(manifestVersion));
        ContentSha256 = contentSha256;
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
        OperatorId = operatorId ?? throw new ArgumentNullException(nameof(operatorId));
        State = DomainValue.RequireDefined(state, nameof(state));
        ReviewVersion = reviewVersion is null ? null : DomainValue.RequireVersion(reviewVersion, nameof(reviewVersion));
        if (recoveryOperatorId is not null && state != SupportExportState.Published)
        {
            throw new ArgumentException("Recovery requires a verified published archive.", nameof(recoveryOperatorId));
        }
        RecoveryOperatorId = recoveryOperatorId;
    }

    public string InternalSupportId { get; }
    public string SchemaVersion { get; }
    public string ManifestVersion { get; }
    public string ContentSha256 { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public OperatorId OperatorId { get; }
    public SupportExportState State { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewVersion { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OperatorId? RecoveryOperatorId { get; }
}
