using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Actions.Journal;

public sealed record JournalPlanKey
{
    public JournalPlanKey(JobId jobId, string planSha256, string impactSha256)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        PlanSha256 = RequireDigest(planSha256, nameof(planSha256));
        ImpactSha256 = RequireDigest(impactSha256, nameof(impactSha256));
    }

    public JobId JobId { get; }
    public string PlanSha256 { get; }
    public string ImpactSha256 { get; }

    private static string RequireDigest(string? digest, string parameter) =>
        JournalPlanEncoding.IsDigest(digest) ? digest! : throw new ArgumentException("A lowercase SHA-256 digest is required.", parameter);
}

public sealed record JournalAction(ActionRequest Request, ActionDefinition Definition, string CapabilityId, string ReasonCode,
    CapabilityIntent Intent)
{
    public Guid ActionId => Request.ActionId;
}

public sealed class JournalPlanSnapshot
{
    public const int MaximumPlanBytes = 65_536;
    public const int MaximumImpactBytes = 16_384;
    private const string InvalidSnapshot = "The action journal plan or impact snapshot is invalid.";

    private JournalPlanSnapshot(
        JournalPlanKey key,
        SanitizationGateDecisionId gateDecisionId,
        byte[] canonicalPlan,
        byte[] canonicalImpact,
        ImmutableArray<JournalAction> actions,
        bool hasPlanningProvenance,
        bool isCurrentPolicyPlan,
        string catalogSha256,
        string profileSha256,
        string policySha256,
        string evidenceSha256,
        bool syntheticData,
        ActionSchedule? schedule)
    {
        Key = key;
        GateDecisionId = gateDecisionId;
        CanonicalPlan = ImmutableArray.CreateRange(canonicalPlan);
        CanonicalImpact = ImmutableArray.CreateRange(canonicalImpact);
        Actions = actions;
        HasPlanningProvenance = hasPlanningProvenance;
        IsCurrentPolicyPlan = isCurrentPolicyPlan;
        CatalogSha256 = catalogSha256;
        ProfileSha256 = profileSha256;
        PolicySha256 = policySha256;
        EvidenceSha256 = evidenceSha256;
        SyntheticData = syntheticData;
        Schedule = schedule;
    }

    public JournalPlanKey Key { get; }
    public SanitizationGateDecisionId GateDecisionId { get; }
    public ImmutableArray<byte> CanonicalPlan { get; }
    public ImmutableArray<byte> CanonicalImpact { get; }
    public ImmutableArray<JournalAction> Actions { get; }
    public bool HasPlanningProvenance { get; }
    public string CatalogSha256 { get; }
    public string ProfileSha256 { get; }
    public string PolicySha256 { get; }
    public string EvidenceSha256 { get; }
    public bool SyntheticData { get; }

    /// <summary>The graph bound to this exact plan digest, when the plan used the scheduled contract.</summary>
    public ActionSchedule? Schedule { get; }

    /// <summary>Indicates the plan was written using the current policy plan contract.</summary>
    /// <remarks>The contract version alone does not establish live readiness or dispatch authority.</remarks>
    public bool IsCurrentPolicyPlan { get; }

    /// <summary>Captures the exact proposal and preapproval estimate presented for review.</summary>
    public static JournalPlanSnapshot Create(ProposedPlan plan, PlanImpact impact)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(impact);
        if (plan.BlockedReason is not null || plan.GateDecisionId is null || plan.Actions.IsDefaultOrEmpty ||
            plan.ContentSha256 != impact.PlanSha256 ||
            plan.PolicyDecision is not { Evaluation.Policy.IsCurrentSchema: true })
        {
            throw new ArgumentException("The estimate must belong to a nonblocked proposal under the current policy schema.", nameof(impact));
        }

        var canonicalPlan = PlanIdentity.PlanBytes(plan);
        var canonicalImpact = JournalPlanEncoding.WriteImpact(plan, impact);
        var key = new JournalPlanKey(plan.JobId, plan.ContentSha256, Digest(canonicalImpact));
        return Open(key, canonicalPlan, canonicalImpact, hasPlanningProvenance: true);
    }

    /// <summary>Verifies the bytes of a saved snapshot against its key and a supported journal contract.</summary>
    /// <remarks>Snapshot integrity alone does not establish approval or authorize execution.</remarks>
    public static JournalPlanSnapshot Restore(
        JournalPlanKey key, ReadOnlyMemory<byte> plan, ReadOnlyMemory<byte> impact) =>
        Open(key, plan, impact, hasPlanningProvenance: false);

    private static JournalPlanSnapshot Open(
        JournalPlanKey key, ReadOnlyMemory<byte> plan, ReadOnlyMemory<byte> impact, bool hasPlanningProvenance)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (plan.Length is < 1 or > MaximumPlanBytes || impact.Length is < 1 or > MaximumImpactBytes)
        {
            throw new JsonException(InvalidSnapshot);
        }

        var ownedPlan = plan.ToArray();
        var ownedImpact = impact.ToArray();
        if (Digest(ownedPlan) != key.PlanSha256 || Digest(ownedImpact) != key.ImpactSha256)
        {
            throw new JsonException(InvalidSnapshot);
        }

        try
        {
            var (gate, actions, current, catalog, profile, policy, evidence, synthetic, schedule) =
                JournalPlanEncoding.ReadPlan(key, ownedPlan);
            JournalPlanEncoding.RequireValidImpact(key, actions, ownedImpact);
            return new JournalPlanSnapshot(key, gate, ownedPlan, ownedImpact, actions, hasPlanningProvenance, current,
                catalog, profile, policy, evidence, synthetic, schedule);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or
            FormatException or KeyNotFoundException)
        {
            // Reader failures may contain untrusted data.
            throw new JsonException(InvalidSnapshot);
        }
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
