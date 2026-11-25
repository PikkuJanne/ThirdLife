using System.Collections.Immutable;

namespace ThirdLife.Catalog.Profiles;

public sealed class ProfileDocument
{
    internal ProfileDocument(
        string profileId,
        string version,
        string contentSha256,
        bool syntheticData,
        ProfileReference catalog,
        string catalogContentSha256,
        ProfileReference policy,
        ImmutableArray<ProfileCapability> workshopCapabilities,
        ImmutableArray<ProfileRecipientChoice> recipientChoices)
    {
        ProfileId = profileId;
        Version = version;
        ContentSha256 = contentSha256;
        SyntheticData = syntheticData;
        Catalog = catalog;
        CatalogContentSha256 = catalogContentSha256;
        Policy = policy;
        WorkshopCapabilities = workshopCapabilities;
        RecipientChoices = recipientChoices;
    }

    public string ProfileId { get; }
    public string Version { get; }
    public string ContentSha256 { get; }
    public bool SyntheticData { get; }
    public ProfileReference Catalog { get; }
    public string CatalogContentSha256 { get; }
    public ProfileReference Policy { get; }
    public ImmutableArray<ProfileCapability> WorkshopCapabilities { get; }

    /// <summary>Deferred choices reserved for the recipient to make in person.</summary>
    public ImmutableArray<ProfileRecipientChoice> RecipientChoices { get; }
}

public sealed record ProfileReference(string Id, string Version);

public sealed record ProfileCapability(string CapabilityId, CapabilityIntent Intent, string ReasonCode);

public enum CapabilityIntent
{
    Essential,
    Optional,
}

public sealed record ProfileRecipientChoice(string ChoiceId, RecipientChoiceType Type);

public enum RecipientChoiceType
{
    BrowserPreference,
    AccessibilityPreferences,
    CloudAccountSignIn,
    BackupOnboarding,
}
