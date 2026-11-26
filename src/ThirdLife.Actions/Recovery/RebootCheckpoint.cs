using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThirdLife.Actions.Journal;

namespace ThirdLife.Actions.Recovery;

/// <summary>Identifies the exact journal state an operator can reopen for restart review.</summary>
public sealed record RebootCheckpoint
{
    public const int MaximumAuthenticationDataBytes = 1_024;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(24);

    [JsonConstructor]
    public RebootCheckpoint(
        Guid id,
        JournalPlanKey plan,
        Guid actionId,
        Guid journalEntryId,
        Guid approvalId,
        Guid correlationId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        ulong? packageBootUptime = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(journalEntryId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(approvalId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(correlationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(issuedAtUtc, default);
        if (expiresAtUtc <= issuedAtUtc || expiresAtUtc - issuedAtUtc > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A reboot checkpoint must expire within 24 hours of issuance.");
        }

        Id = id;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ActionId = actionId;
        JournalEntryId = journalEntryId;
        ApprovalId = approvalId;
        CorrelationId = correlationId;
        IssuedAtUtc = issuedAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        PackageBootUptime = packageBootUptime;
    }

    public Guid Id { get; }
    public JournalPlanKey Plan { get; }
    public Guid ActionId { get; }
    public Guid JournalEntryId { get; }
    public Guid ApprovalId { get; }
    public Guid CorrelationId { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>Kernel interrupt time in 100 ns ticks at the durable package restart result.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? PackageBootUptime { get; }

    public bool IsCurrent(DateTimeOffset now) => now >= IssuedAtUtc && now < ExpiresAtUtc;

    public bool MatchesRestart(JournalEntry? entry) => IsRestartResult(entry,
        PackageBootUptime is null ? JournalActor.Broker : JournalActor.PackageBackend) &&
        entry!.Plan == Plan && entry.ActionId == ActionId && entry.EntryId == JournalEntryId &&
        entry.ApprovalId == ApprovalId && entry.CorrelationId == CorrelationId &&
        entry.OccurredAtUtc <= IssuedAtUtc;

    public static bool IsRestartResult(JournalEntry? entry, JournalActor backend) =>
        entry is
        {
            PriorState: JournalState.Started,
            State: JournalState.Applied,
            Result: JournalResult.BackendApplied,
            Checkpoint: JournalCheckpoint.RestartRequired,
            Verification: null,
            ApprovalId: not null,
        } && entry.Actor == backend && backend is JournalActor.Broker or JournalActor.PackageBackend;

    /// <summary>Encodes versioned workshop metadata for a user-scoped authenticator.</summary>
    public byte[] CreateAuthenticationData()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("purpose", PackageBootUptime is null
                ? "thirdlife.reboot-checkpoint.v1" : "thirdlife.package-reboot-checkpoint.v1");
            writer.WriteString("checkpoint_id", Id);
            writer.WriteString("job_id", Plan.JobId.Value);
            writer.WriteString("plan_sha256", Plan.PlanSha256);
            writer.WriteString("impact_sha256", Plan.ImpactSha256);
            writer.WriteString("action_id", ActionId);
            writer.WriteString("journal_entry_id", JournalEntryId);
            writer.WriteString("approval_id", ApprovalId);
            writer.WriteString("correlation_id", CorrelationId);
            writer.WriteString("issued_at_utc", IssuedAtUtc);
            writer.WriteString("expires_at_utc", ExpiresAtUtc);
            if (PackageBootUptime is { } uptime)
            {
                writer.WriteNumber("package_boot_uptime", uptime);
            }
            writer.WriteEndObject();
        }

        return buffer.WrittenCount <= MaximumAuthenticationDataBytes
            ? buffer.WrittenSpan.ToArray()
            : throw new InvalidOperationException("The reboot checkpoint exceeds the authentication size limit.");
    }
}
