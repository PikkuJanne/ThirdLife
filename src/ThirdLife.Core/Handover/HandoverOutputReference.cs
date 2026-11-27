using ThirdLife.Core.Evidence;

namespace ThirdLife.Core.Handover;

public enum HandoverOutputKind
{
    WorkshopRecord = 1,
    RecipientGuide,
    SupportBundle,
}

/// <summary>Exact output bytes and the reviewed job state used to produce them.</summary>
public sealed record HandoverOutputReference
{
    public HandoverOutputReference(HandoverOutputKind kind, HandoverReviewScope scope, string version, string sha256)
    {
        Kind = DomainValue.RequireDefined(kind, nameof(kind));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Version = DomainValue.RequireVersion(version, nameof(version));
        if (sha256 is not { Length: 64 } || sha256.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("An exact lowercase SHA-256 digest is required.", nameof(sha256));
        }

        Sha256 = sha256;
    }

    public HandoverOutputKind Kind { get; }
    public HandoverReviewScope Scope { get; }
    public string Version { get; }
    public string Sha256 { get; }
}

public enum SupportBundleOmissionReason
{
    NoSupportRequest = 1,
    PrivacyChoice,
}

public sealed record HandoverSupportBundleOmission
{
    public HandoverSupportBundleOmission(HandoverReviewScope scope, string version, OperatorId operatorId,
        DateTimeOffset recordedAtUtc, SupportBundleOmissionReason reason)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Version = DomainValue.RequireVersion(version, nameof(version));
        OperatorId = operatorId ?? throw new ArgumentNullException(nameof(operatorId));
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
        Reason = DomainValue.RequireDefined(reason, nameof(reason));
    }

    public HandoverReviewScope Scope { get; }
    public string Version { get; }
    public OperatorId OperatorId { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public SupportBundleOmissionReason Reason { get; }
}
