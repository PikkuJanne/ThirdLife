using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Decisions;

namespace ThirdLife.Actions.Planning;

internal static class PlanIdentity
{
    internal const string CurrentVersion = "thirdlife.plan.v2";
    internal const string LegacyVersion = "thirdlife.plan.v1";
    private static readonly JsonSerializerOptions EvidenceJson = DomainJson.CreateStrictOptions();

    public static string PlanHash(
        PlanSubject subject,
        DispositionDecision? decision,
        PlanBlockReason? reason,
        ImmutableArray<PlannedAction> actions,
        ImmutableArray<ProfileRecipientChoice> choices) =>
        Convert.ToHexStringLower(SHA256.HashData(WritePlan(subject, decision, reason, actions, choices).WrittenSpan));

    public static byte[] PlanBytes(ProposedPlan plan) =>
        WritePlan(
            new PlanSubject(plan.JobId, plan.GateDecisionId, plan.ProfileSha256, plan.CatalogSha256, plan.PolicySha256,
                plan.EvidenceSha256, plan.SyntheticData, plan.ActiveCapabilityIds),
            plan.PolicyDecision, plan.BlockedReason, plan.Actions, plan.RecipientChoices).WrittenSpan.ToArray();

    private static ArrayBufferWriter<byte> WritePlan(
        PlanSubject subject,
        DispositionDecision? decision,
        PlanBlockReason? reason,
        ImmutableArray<PlannedAction> actions,
        ImmutableArray<ProfileRecipientChoice> choices)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", CurrentVersion);
            WriteSubject(writer, subject);
            WriteDecision(writer, decision);
            if (reason is { } blocked)
            {
                writer.WriteNumber("blocked_reason", (int)blocked);
            }
            else
            {
                writer.WriteNull("blocked_reason");
            }

            writer.WriteStartArray("actions");
            foreach (var action in actions)
            {
                WriteAction(writer, action);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("recipient_choices");
            foreach (var choice in choices)
            {
                writer.WriteStartObject();
                writer.WriteString("choice_id", choice.ChoiceId);
                writer.WriteNumber("choice_type", (int)choice.Type);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer;
    }

    public static Guid ActionId(PlanSubject subject, DispositionDecision decision, string capabilityId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "thirdlife.plan-action-id.v1");
            WriteSubject(writer, subject);
            WriteDecision(writer, decision);
            writer.WriteString("capability_id", capabilityId);
            writer.WriteEndObject();
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(buffer.WrittenSpan, digest);
        // Mark a name-derived custom UUID with the RFC variant so it cannot be an empty identifier.
        digest[6] = (byte)((digest[6] & 0x0F) | 0x80);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        return new Guid(digest[..16], bigEndian: true);
    }

    public static string EvidenceHash(IEnumerable<Observation> orderedUniqueFacts)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData("thirdlife.plan-evidence.v1\n"u8);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        foreach (var fact in orderedUniqueFacts)
        {
            JsonSerializer.Serialize(writer, NormalizeMeasurement(fact), EvidenceJson);
            writer.Flush();
            digest.AppendData(buffer.WrittenSpan);
            // Compact JSON escapes embedded line breaks. Each raw newline delimits one fact.
            digest.AppendData("\n"u8);
            buffer.Clear();
            writer.Reset(buffer);
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static Observation NormalizeMeasurement(Observation fact)
    {
        if (fact.Value?.DecimalValue is not { } measurement)
        {
            return fact;
        }

        // Decimal equality ignores representation scale. Hash equal measurements identically.
        var normalized = decimal.Parse(measurement.ToString("G29", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        return new Observation(fact.Metadata, fact.EvidenceKey, EvidenceValue.FromDecimal(normalized),
            fact.Unit, fact.LimitationCode, fact.OperatorId);
    }

    private static void WriteSubject(Utf8JsonWriter writer, PlanSubject subject)
    {
        writer.WriteStartObject("subject");
        writer.WriteString("job_id", subject.JobId.Value);
        writer.WriteString("gate_decision_id", subject.GateDecisionId?.Value);
        writer.WriteString("profile_sha256", subject.ProfileSha256);
        writer.WriteString("catalog_sha256", subject.CatalogSha256);
        writer.WriteString("policy_sha256", subject.PolicySha256);
        writer.WriteString("evidence_sha256", subject.EvidenceSha256);
        writer.WriteBoolean("synthetic_data", subject.SyntheticData);
        writer.WriteStartArray("active_capability_ids");
        foreach (var capabilityId in subject.ActiveCapabilityIds)
        {
            writer.WriteStringValue(capabilityId);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDecision(Utf8JsonWriter writer, DispositionDecision? decision)
    {
        if (decision is null)
        {
            writer.WriteNull("decision");
            return;
        }

        writer.WriteStartObject("decision");
        writer.WriteNumber("disposition", (int)decision.Disposition);
        writer.WriteStartArray("policy_exception_ids");
        var exceptionIds = decision.RuleDetails
            .Where(detail => detail.Exception is not null)
            .Select(detail => detail.Exception!.ExceptionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var exceptionId in exceptionIds)
        {
            writer.WriteStringValue(exceptionId);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, PlannedAction action)
    {
        writer.WriteStartObject();
        writer.WriteString("capability_id", action.CapabilityId);
        writer.WriteString("reason_code", action.ReasonCode);
        writer.WriteNumber("intent", (int)action.Intent);
        writer.WriteString("catalog_entry_id", action.CatalogEntry.Id);
        writer.WriteString("catalog_entry_version", action.CatalogEntry.Version);
        writer.WritePropertyName("request");
        writer.WriteRawValue(ActionRequestCodec.Serialize(action.Request));
        writer.WriteStartObject("definition");
        var definition = action.Definition;
        writer.WriteNumber("kind", (int)definition.Kind);
        writer.WriteNumber("scope", (int)definition.Scope);
        writer.WriteNumber("privilege", (int)definition.Privilege);
        writer.WriteNumber("reboot", (int)definition.Reboot);
        writer.WriteNumber("network_use", (int)definition.NetworkUse);
        writer.WriteNumber("verification", (int)definition.Verification);
        writer.WriteNumber("rollback", (int)definition.Rollback);
        writer.WriteNumber("idempotency", (int)definition.Idempotency);
        writer.WriteNumber("availability", (int)definition.Availability);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
