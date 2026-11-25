using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThirdLife.Catalog.Schema;
using static ThirdLife.Catalog.Schema.DeclarativeJson;

namespace ThirdLife.Catalog.Profiles;

public static partial class ProfileSchema
{
    public const string Version = "thirdlife.profile.v1";
    public const int MaximumDocumentBytes = 32_768;
    private const int MaximumCapabilities = 4;
    private const int MaximumChoices = 4;

    /// <summary>Validates a profile against its exact catalog snapshot and retains the exact required policy reference.</summary>
    public static ProfileDocument Parse(ReadOnlyMemory<byte> utf8Json, CatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            using var document = Open(utf8Json, MaximumDocumentBytes, out var contentSha256);
            return ReadProfile(document.RootElement, catalog, contentSha256);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new JsonException("Profile input does not satisfy the ThirdLife profile v1 contract.");
        }
    }

    private static ProfileDocument ReadProfile(JsonElement root, CatalogDocument catalog, string contentSha256)
    {
        RequireFields(root,
            ["schema_version", "profile_id", "version", "status", "synthetic_data", "classification", "policy", "catalog", "workshop", "recipient"]);
        var synthetic = root.GetProperty("synthetic_data").GetBoolean();
        RequireValue(root, "schema_version", Version);
        RequireValue(root, "classification", "PUBLIC_REFERENCE");
        RequireValue(root, "status", synthetic ? "candidate" : "reviewed");

        var catalogReference = ReadReference(root.GetProperty("catalog"));
        if (catalogReference.Id != catalog.CatalogId ||
            catalogReference.Version != catalog.Version ||
            synthetic != catalog.SyntheticData)
        {
            throw new JsonException();
        }

        return new ProfileDocument(
            ReadToken(root, "profile_id", LowerKebabPattern(), 64),
            ReadToken(root, "version", SemanticVersionPattern(), 64),
            contentSha256,
            synthetic,
            catalogReference,
            catalog.ContentSha256,
            ReadReference(root.GetProperty("policy")),
            ReadCapabilities(root.GetProperty("workshop"), catalog),
            ReadChoices(root.GetProperty("recipient")));
    }

    private static ProfileReference ReadReference(JsonElement element)
    {
        RequireFields(element, ["id", "version"]);
        return new ProfileReference(
            ReadToken(element, "id", LowerKebabPattern(), 64),
            ReadToken(element, "version", SemanticVersionPattern(), 64));
    }

    private static ImmutableArray<ProfileCapability> ReadCapabilities(JsonElement workshop, CatalogDocument catalog)
    {
        RequireFields(workshop, ["capabilities"]);
        var array = workshop.GetProperty("capabilities");
        var count = array.GetArrayLength();
        if (count is < 1 or > MaximumCapabilities)
        {
            throw new JsonException();
        }

        var capabilities = ImmutableArray.CreateBuilder<ProfileCapability>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hasEssential = false;
        foreach (var element in array.EnumerateArray())
        {
            RequireFields(element, ["capability_id", "intent", "reason_code"]);
            var id = element.GetProperty("capability_id").GetString();
            if (id is null || !CatalogSchema.IsGenericCapability(id) || !seen.Add(id) ||
                !catalog.Applications.Any(application => application.CapabilityId == id))
            {
                throw new JsonException();
            }

            var intent = element.GetProperty("intent").GetString() switch
            {
                "essential" => CapabilityIntent.Essential,
                "optional" => CapabilityIntent.Optional,
                _ => throw new JsonException(),
            };
            hasEssential |= intent == CapabilityIntent.Essential;
            capabilities.Add(new ProfileCapability(id, intent, ReadToken(element, "reason_code", ReasonPattern(), 64)));
        }

        return hasEssential ? capabilities.MoveToImmutable() : throw new JsonException();
    }

    private static ImmutableArray<ProfileRecipientChoice> ReadChoices(JsonElement recipient)
    {
        RequireFields(recipient, ["choices"]);
        var array = recipient.GetProperty("choices");
        var count = array.GetArrayLength();
        if (count > MaximumChoices)
        {
            throw new JsonException();
        }

        var choices = ImmutableArray.CreateBuilder<ProfileRecipientChoice>(count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<RecipientChoiceType>();
        foreach (var element in array.EnumerateArray())
        {
            RequireFields(element, ["choice_id", "choice_type", "state", "requires_recipient_presence", "workshop_action"]);
            var id = ReadToken(element, "choice_id", LowerKebabPattern(), 64);
            var type = element.GetProperty("choice_type").GetString() switch
            {
                "browser_preference" => RecipientChoiceType.BrowserPreference,
                "accessibility_preferences" => RecipientChoiceType.AccessibilityPreferences,
                "cloud_account_sign_in" => RecipientChoiceType.CloudAccountSignIn,
                "backup_onboarding" => RecipientChoiceType.BackupOnboarding,
                _ => throw new JsonException(),
            };
            RequireValue(element, "state", "deferred");
            if (!element.GetProperty("requires_recipient_presence").GetBoolean() ||
                element.GetProperty("workshop_action").GetBoolean() ||
                !ids.Add(id) || !types.Add(type))
            {
                throw new JsonException();
            }

            choices.Add(new ProfileRecipientChoice(id, type));
        }

        return choices.MoveToImmutable();
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:_[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex ReasonPattern();
}
