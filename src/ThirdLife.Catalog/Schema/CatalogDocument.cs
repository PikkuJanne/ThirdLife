using System.Collections.Immutable;

namespace ThirdLife.Catalog.Schema;

public sealed class CatalogDocument
{
    internal CatalogDocument(
        string catalogId,
        string version,
        string contentSha256,
        bool syntheticData,
        ImmutableArray<CatalogApplication> applications)
    {
        CatalogId = catalogId;
        Version = version;
        ContentSha256 = contentSha256;
        SyntheticData = syntheticData;
        Applications = applications;
    }

    public string CatalogId { get; }
    public string Version { get; }
    public string ContentSha256 { get; }
    public bool SyntheticData { get; }
    public ImmutableArray<CatalogApplication> Applications { get; }
}

public sealed record CatalogApplication(
    string Id,
    string Version,
    string CapabilityId,
    bool ProductionEligible,
    CatalogPackage Package,
    CatalogReview Review,
    CatalogBehavior Behavior,
    CatalogVerification Verification);

public sealed record CatalogPackage(
    string SourceId,
    string PackageId,
    string ExactVersion,
    CatalogScope Scope,
    ImmutableArray<CatalogArchitecture> Architectures,
    string MinimumOs,
    bool ExternalArtifact)
{
    public string? InstallerSha256 { get; init; }
}

public sealed record CatalogReview(
    string Publisher,
    string DeclaredLicense,
    DateTimeOffset? LicenseReviewedAtUtc,
    DateTimeOffset? PrivacyReviewedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    bool InstallationAllowed,
    bool RedistributionAllowed);

public sealed record CatalogBehavior(
    CatalogReboot Reboot,
    CatalogPresence BackgroundService,
    CatalogPresence AutoUpdate,
    CatalogNetworkUse NetworkUse,
    long? DownloadBytes,
    long? InstalledBytes);

public sealed record CatalogVerification(
    bool Reviewed,
    string ExpectedSourceId,
    string ExpectedPackageId,
    string ExpectedVersion);

public enum CatalogScope
{
    User,
    Machine,
}

public enum CatalogArchitecture
{
    X64,
    Arm64,
    X86,
}

public enum CatalogReboot
{
    Unknown,
    None,
    Possible,
    Required,
}

public enum CatalogPresence
{
    Unknown,
    No,
    Yes,
}

public enum CatalogNetworkUse
{
    Unknown,
    None,
    Optional,
    Required,
    NotApplicable,
}
