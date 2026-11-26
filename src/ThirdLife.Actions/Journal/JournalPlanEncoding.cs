using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.Registry;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Actions.Journal;

internal static partial class JournalPlanEncoding
{
    private const int MaximumActionCount = 4;
    private const int MaximumChoiceCount = 4;
    private const int MaximumExceptionCount = 128;
    private const long MaximumEstimatedBytes = 9_007_199_254_740_991;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsDigest(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static (SanitizationGateDecisionId Gate, ImmutableArray<JournalAction> Actions, bool IsCurrentPolicyPlan,
        string CatalogSha256, string ProfileSha256, bool SyntheticData) ReadPlan(
        JournalPlanKey key, byte[] bytes)
    {
        using var document = Open(bytes);
        var root = document.RootElement;
        RequireCanonical(root, bytes);
        RequireFields(root, ["schema_version", "subject", "decision", "blocked_reason", "actions", "recipient_choices"]);
        var schemaVersion = ReadString(root.GetProperty("schema_version"));
        if (schemaVersion is not (PlanIdentity.CurrentVersion or PlanIdentity.LegacyVersion) ||
            root.GetProperty("blocked_reason").ValueKind != JsonValueKind.Null)
        {
            throw new JsonException();
        }

        RequireReadyDecision(root.GetProperty("decision"));
        RequireDeferredChoices(root.GetProperty("recipient_choices"));

        var subject = root.GetProperty("subject");
        RequireFields(subject,
            ["job_id", "gate_decision_id", "profile_sha256", "catalog_sha256", "policy_sha256", "evidence_sha256",
                "synthetic_data", "active_capability_ids"]);
        var catalog = ReadString(subject.GetProperty("catalog_sha256"));
        var profile = ReadString(subject.GetProperty("profile_sha256"));
        if (!subject.GetProperty("job_id").ValueEquals(key.JobId.Value) ||
            !IsDigest(profile) || !IsDigest(catalog) ||
            !IsDigest(ReadString(subject.GetProperty("policy_sha256"))) ||
            !IsDigest(ReadString(subject.GetProperty("evidence_sha256"))))
        {
            throw new JsonException();
        }

        var gate = new SanitizationGateDecisionId(ReadString(subject.GetProperty("gate_decision_id")));
        var synthetic = subject.GetProperty("synthetic_data").GetBoolean();
        var capabilities = ReadActiveCapabilities(subject.GetProperty("active_capability_ids"));
        return (gate, ReadActions(root.GetProperty("actions"), capabilities, synthetic),
            schemaVersion == PlanIdentity.CurrentVersion, catalog, profile, synthetic);
    }

    public static byte[] WriteImpact(ProposedPlan plan, PlanImpact impact)
    {
        if (impact.Actions.IsDefault || impact.Actions.Length != plan.Actions.Length)
        {
            throw new ArgumentException("The estimate does not describe the proposed actions.", nameof(impact));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("contract_version", impact.Version);
            writer.WriteString("plan_sha256", impact.PlanSha256);
            writer.WriteStartArray("actions");
            for (var index = 0; index < impact.Actions.Length; index++)
            {
                var row = impact.Actions[index];
                if (row.ActionId != plan.Actions[index].Request.ActionId)
                {
                    throw new ArgumentException("The estimate does not describe the proposed actions.", nameof(impact));
                }

                writer.WriteStartObject();
                writer.WriteString("action_id", row.ActionId);
                WriteEstimate(writer, "download_bytes", row.DownloadBytes);
                WriteEstimate(writer, "installed_bytes", row.InstalledBytes);
                writer.WriteNumber("restart", (int)row.Restart);
                writer.WriteNumber("network", (int)row.Network);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteTotal(writer, "download", impact.Download);
            WriteTotal(writer, "installation", impact.Installation);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static void RequireValidImpact(JournalPlanKey key, ImmutableArray<JournalAction> actions, byte[] bytes)
    {
        using var document = Open(bytes);
        var root = document.RootElement;
        RequireCanonical(root, bytes);
        RequireFields(root, ["contract_version", "plan_sha256", "actions", "download", "installation"]);
        var rows = root.GetProperty("actions");
        if (ReadInt(root.GetProperty("contract_version")) != PlanImpact.ContractVersion ||
            !root.GetProperty("plan_sha256").ValueEquals(key.PlanSha256) || rows.GetArrayLength() != actions.Length)
        {
            throw new JsonException();
        }

        var index = 0;
        var download = new ByteEstimateTotal(0, 0);
        var installation = new ByteEstimateTotal(0, 0);
        foreach (var row in rows.EnumerateArray())
        {
            RequireFields(row, ["action_id", "download_bytes", "installed_bytes", "restart", "network"]);
            var action = actions[index++];
            var restart = (RestartEstimate)ReadInt(row.GetProperty("restart"));
            var network = (NetworkEstimate)ReadInt(row.GetProperty("network"));
            var downloadedBytes = ReadEstimate(row.GetProperty("download_bytes"));
            var installedBytes = ReadEstimate(row.GetProperty("installed_bytes"));
            if (!row.GetProperty("action_id").ValueEquals(action.ActionId.ToString("D")) ||
                !Enum.IsDefined(restart) || !Enum.IsDefined(network) ||
                !ValidImpactKind(action.Request, restart, network, downloadedBytes, installedBytes))
            {
                throw new JsonException();
            }

            download = AddEstimate(download, downloadedBytes);
            installation = AddEstimate(installation, installedBytes);
        }

        RequireTotal(root.GetProperty("download"), download);
        RequireTotal(root.GetProperty("installation"), installation);
    }

    private static JsonDocument Open(byte[] bytes)
    {
        _ = StrictUtf8.GetCharCount(bytes);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowDuplicateProperties = false,
            MaxDepth = 8,
        });
    }

    private static void RequireCanonical(JsonElement root, ReadOnlySpan<byte> bytes)
    {
        if (!bytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(root)))
        {
            throw new JsonException();
        }
    }

    private static void RequireReadyDecision(JsonElement decision)
    {
        RequireFields(decision, ["disposition", "policy_exception_ids"]);
        if (ReadInt(decision.GetProperty("disposition")) != (int)Disposition.ReadyToPrepare)
        {
            throw new JsonException();
        }

        var ids = decision.GetProperty("policy_exception_ids");
        if (ids.GetArrayLength() > MaximumExceptionCount)
        {
            throw new JsonException();
        }

        string? previous = null;
        foreach (var idValue in ids.EnumerateArray())
        {
            var id = new EvidenceId(ReadString(idValue)).Value;
            if (string.CompareOrdinal(previous, id) >= 0)
            {
                throw new JsonException();
            }

            previous = id;
        }
    }

    private static HashSet<string> ReadActiveCapabilities(JsonElement capabilities)
    {
        if (capabilities.GetArrayLength() is < 1 or > MaximumActionCount)
        {
            throw new JsonException();
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var capability in capabilities.EnumerateArray())
        {
            var id = ReadString(capability);
            if (!IsCapability(id) || string.CompareOrdinal(previous, id) >= 0)
            {
                throw new JsonException();
            }

            result.Add(id);
            previous = id;
        }

        return result;
    }

    private static ImmutableArray<JournalAction> ReadActions(
        JsonElement actions, HashSet<string> capabilities, bool synthetic)
    {
        if (actions.GetArrayLength() != capabilities.Count)
        {
            throw new JsonException();
        }

        var result = ImmutableArray.CreateBuilder<JournalAction>(capabilities.Count);
        var ids = new HashSet<Guid>();
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        var lastCapability = string.Empty;
        var lastIntent = CapabilityIntent.Essential;
        foreach (var item in actions.EnumerateArray())
        {
            RequireFields(item, ["capability_id", "reason_code", "intent", "catalog_entry_id", "catalog_entry_version", "request", "definition"]);
            var capability = ReadString(item.GetProperty("capability_id"));
            var intent = (CapabilityIntent)ReadInt(item.GetProperty("intent"));
            var entryId = ReadToken(item.GetProperty("catalog_entry_id"), EntryIdPattern(), 128);
            _ = ReadToken(item.GetProperty("reason_code"), ReasonCodePattern(), 64);
            _ = ReadToken(item.GetProperty("catalog_entry_version"), VersionPattern(), 64);
            if (!capabilities.Remove(capability) || !entryIds.Add(entryId) ||
                entryId.StartsWith("generic.synthetic.", StringComparison.Ordinal) != synthetic ||
                !Enum.IsDefined(intent) || intent < lastIntent ||
                (result.Count == 0 && intent != CapabilityIntent.Essential) ||
                (intent == lastIntent && string.CompareOrdinal(lastCapability, capability) >= 0))
            {
                throw new JsonException();
            }

            var encodedRequest = StrictUtf8.GetBytes(item.GetProperty("request").GetRawText());
            var request = ActionRequestCodec.Parse(encodedRequest);
            if (!encodedRequest.AsSpan().SequenceEqual(ActionRequestCodec.Serialize(request)) ||
                !ids.Add(request.ActionId) || (request is NoOpActionRequest) != synthetic)
            {
                throw new JsonException();
            }

            var definition = ActionRegistry.Describe(request);
            RequireDefinition(item.GetProperty("definition"), definition);
            result.Add(new JournalAction(request, definition));
            lastIntent = intent;
            lastCapability = capability;
        }

        return result.MoveToImmutable();
    }

    private static void RequireDefinition(JsonElement value, ActionDefinition definition)
    {
        RequireFields(value, ["kind", "scope", "privilege", "reboot", "network_use", "verification", "rollback", "idempotency", "availability"]);
        if (ReadInt(value.GetProperty("kind")) != (int)definition.Kind ||
            ReadInt(value.GetProperty("scope")) != (int)definition.Scope ||
            ReadInt(value.GetProperty("privilege")) != (int)definition.Privilege ||
            ReadInt(value.GetProperty("reboot")) != (int)definition.Reboot ||
            ReadInt(value.GetProperty("network_use")) != (int)definition.NetworkUse ||
            ReadInt(value.GetProperty("verification")) != (int)definition.Verification ||
            ReadInt(value.GetProperty("rollback")) != (int)definition.Rollback ||
            ReadInt(value.GetProperty("idempotency")) != (int)definition.Idempotency ||
            ReadInt(value.GetProperty("availability")) != (int)definition.Availability)
        {
            throw new JsonException();
        }
    }

    private static void RequireDeferredChoices(JsonElement choices)
    {
        if (choices.GetArrayLength() > MaximumChoiceCount)
        {
            throw new JsonException();
        }

        var types = new HashSet<RecipientChoiceType>();
        string? previous = null;
        foreach (var choice in choices.EnumerateArray())
        {
            RequireFields(choice, ["choice_id", "choice_type"]);
            var id = ReadToken(choice.GetProperty("choice_id"), ChoiceIdPattern(), 64);
            var type = (RecipientChoiceType)ReadInt(choice.GetProperty("choice_type"));
            if (string.CompareOrdinal(previous, id) >= 0 || !Enum.IsDefined(type) || !types.Add(type))
            {
                throw new JsonException();
            }

            previous = id;
        }
    }

    private static bool ValidImpactKind(
        ActionRequest request, RestartEstimate restart, NetworkEstimate network, long? download, long? installed) =>
        request is NoOpActionRequest
            ? restart == RestartEstimate.None && network == NetworkEstimate.None && download == 0 && installed == 0
            : network != NetworkEstimate.None;

    private static ByteEstimateTotal AddEstimate(ByteEstimateTotal total, long? bytes) => bytes is { } estimate
        ? total with { KnownBytes = total.KnownBytes + estimate }
        : total with { UnknownActionCount = total.UnknownActionCount + 1 };

    private static void RequireTotal(JsonElement value, ByteEstimateTotal total)
    {
        RequireFields(value, ["known_bytes", "unknown_action_count"]);
        if (ReadLong(value.GetProperty("known_bytes")) != total.KnownBytes ||
            ReadInt(value.GetProperty("unknown_action_count")) != total.UnknownActionCount)
        {
            throw new JsonException();
        }
    }

    private static void WriteTotal(Utf8JsonWriter writer, string name, ByteEstimateTotal value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("known_bytes", value.KnownBytes);
        writer.WriteNumber("unknown_action_count", value.UnknownActionCount);
        writer.WriteEndObject();
    }

    private static long? ReadEstimate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var bytes = ReadLong(value);
        return bytes is >= 0 and <= MaximumEstimatedBytes ? bytes : throw new JsonException();
    }

    private static void WriteEstimate(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } bytes)
        {
            writer.WriteNumber(name, bytes);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static int ReadInt(JsonElement element) =>
        element.TryGetInt32(out var value) && element.GetRawText() == value.ToString(CultureInfo.InvariantCulture)
            ? value : throw new JsonException();

    private static long ReadLong(JsonElement element) =>
        element.TryGetInt64(out var value) && element.GetRawText() == value.ToString(CultureInfo.InvariantCulture)
            ? value : throw new JsonException();

    private static string ReadString(JsonElement element) => element.GetString() ?? throw new JsonException();

    private static string ReadToken(JsonElement element, Regex pattern, int maximumLength)
    {
        var value = ReadString(element);
        return value.Length <= maximumLength && pattern.IsMatch(value) ? value : throw new JsonException();
    }

    private static void RequireFields(JsonElement value, ReadOnlySpan<string> fields)
    {
        var index = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (index == fields.Length || !property.NameEquals(fields[index++]))
            {
                throw new JsonException();
            }
        }

        if (index != fields.Length)
        {
            throw new JsonException();
        }
    }

    private static bool IsCapability(string value) =>
        value is "web_browsing" or "document_editing" or "pdf_reading" or "video_calling";

    [GeneratedRegex(@"\A(?:[a-z][a-z0-9]*(?:-[a-z0-9]+)*\.)+[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex EntryIdPattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex ChoiceIdPattern();

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:_[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex ReasonCodePattern();

    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?\z", RegexOptions.NonBacktracking)]
    private static partial Regex VersionPattern();
}
