using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Repairs;

public static class RepairJournal
{
    public const string ReservedProviderId = "repair-journal";
    public const string ReservedKeyPrefix = "repair.";
    private const string RepairKey = "repair.recorded";
    private const string RetestKey = "repair.retest_started";
    private const string Revision = "repair-journal-v1";
    private const int MaximumEvidenceRecords = 10_000;
    private const int MaximumMarkerCharacters = 2_048;
    private static readonly ProviderId Provider = new(ReservedProviderId);
    private static readonly JsonSerializerOptions Json = DomainJson.CreateStrictOptions();

    public static IReadOnlyList<RepairRecord> GetRepairs(StoredJob job) => Read(job).Repairs;

    public static IReadOnlyList<RepairRetestLink> GetRetests(StoredJob job) => Read(job).Retests;

    public static (IReadOnlyList<RepairRecord> Repairs, IReadOnlyList<RepairRetestLink> Retests) GetHistory(StoredJob job)
    {
        var history = Read(job);
        return (history.Repairs, history.Retests);
    }

    public static bool CanRetest(StoredJob job, EvidenceId repairId)
    {
        ArgumentNullException.ThrowIfNull(repairId);
        var history = Read(job);
        var repair = history.Repairs.SingleOrDefault(item => item.RepairId == repairId);
        var gate = SanitizationGate.Inspect(job);
        return repair is not null && gate.AllowsAssessment && history.Gates.Allows(repair, gate.Decision!);
    }

    public static Observation CreateRetestMarker(
        StoredJob job,
        EvidenceId repairId,
        string assessmentId,
        SanitizationGateDecisionId currentGateId,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(repairId);
        _ = DomainValue.RequireIdentifier(assessmentId, nameof(assessmentId));
        ArgumentNullException.ThrowIfNull(currentGateId);
        var started = DomainValue.RequireTimestamp(startedAtUtc, nameof(startedAtUtc));
        var history = Read(job);
        var repair = history.Repairs.SingleOrDefault(item => item.RepairId == repairId);
        if (repair is null || assessmentId == repair.PriorAssessmentId || started < repair.RecordedAtUtc ||
            history.Retests.Any(item => item.AssessmentId == assessmentId))
        {
            throw new JobLifecycleException("repair_link_conflict", "Select a recorded repair and start a new assessment for its retest.");
        }

        var gate = SanitizationGate.Inspect(job);
        if (!gate.AllowsAssessment || gate.Decision!.DecisionId != currentGateId ||
            gate.Decision.EvaluatedAtUtc > started || !history.Gates.Allows(repair, gate.Decision))
        {
            throw new JobLifecycleException("repair_gate_required", "Complete the required sanitization record before starting this retest.");
        }

        var link = new RepairRetestLink(EvidenceId.New(), repairId, repair.FailedEvidenceId, assessmentId, currentGateId, started);
        return CreateMarker(link, link.RetestId, RetestKey, started, ProvenanceKind.SystemGenerated, operatorId: null);
    }

    public static bool IsRepairEvidence(Observation observation, RepairComponent component)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Value is not { } value || observation.Metadata.ValueAvailability != ValueAvailability.Available ||
            observation.LimitationCode is not null ||
            observation.Metadata.ProviderId == Provider)
        {
            return false;
        }

        return (component, observation.EvidenceKey.Value, value.Kind) switch
        {
            (RepairComponent.Battery, "battery.present", EvidenceValueKind.Boolean) => value.BooleanValue == false,
            (RepairComponent.Battery, "battery.swelling_inspection", EvidenceValueKind.Text or EvidenceValueKind.Enum) =>
                value.StringValue == "swelling_observed",
            (RepairComponent.Battery, "battery.full_charge_ratio_percent" or "battery.pack.full_charge_ratio_percent",
                EvidenceValueKind.WholeNumber or EvidenceValueKind.DecimalNumber) => value.IntegerValue >= 0 || value.DecimalValue >= 0,
            (RepairComponent.SystemStorage, "storage.system.health_state" or "storage.physical_disk.reported_health",
                EvidenceValueKind.Text or EvidenceValueKind.Enum) => value.StringValue is "warning" or "unhealthy",
            (RepairComponent.SystemStorage, "storage.system.capacity_bytes" or "storage.system.free_bytes" or
                "storage.system_volume.caller_capacity_bytes" or "storage.system_volume.caller_free_bytes" or
                "storage.system_volume.total_free_bytes", EvidenceValueKind.WholeNumber) => value.IntegerValue >= 0,
            _ => false,
        };
    }

    internal static bool IsStorageReplacement(RepairComponent component, RepairAction action) =>
        component == RepairComponent.SystemStorage && action == RepairAction.Replaced;

    internal static Observation CreateRepairMarker(RepairRecord repair, bool isFixture) =>
        CreateMarker(repair, repair.RepairId, RepairKey, repair.RecordedAtUtc,
            isFixture ? ProvenanceKind.SyntheticFixture : ProvenanceKind.HumanConfirmation,
            isFixture ? null : repair.OperatorId);

    internal static SanitizationEvidence CreateSanitizationInvalidation(EvidenceId id, DateTimeOffset recordedAt, bool isFixture) =>
        new(new EvidenceMetadata(id, PrivacyClassification.WorkshopRestricted, EvidenceClassification.NotAvailable, Provider,
            recordedAt, new EvidenceProvenance(isFixture ? ProvenanceKind.SyntheticFixture : ProvenanceKind.SystemGenerated, Revision),
            ValueAvailability.Unknown), SanitizationState.Unknown, "unknown", null, null, null,
            SanitizationVerificationState.NotAvailable, Revision);

    private static Observation CreateMarker<T>(
        T item, EvidenceId id, string key, DateTimeOffset recordedAt, ProvenanceKind origin, OperatorId? operatorId)
    {
        var json = JsonSerializer.Serialize(item, Json);
        if (json.Length > MaximumMarkerCharacters)
        {
            throw Invalid();
        }

        var classification = operatorId is null ? EvidenceClassification.Observed : EvidenceClassification.HumanConfirmed;
        return new Observation(new EvidenceMetadata(id, PrivacyClassification.WorkshopRestricted, classification, Provider,
            recordedAt, new EvidenceProvenance(origin, Revision), ValueAvailability.Available),
            new EvidenceKey(key), EvidenceValue.FromString(json), null, null, operatorId);
    }

    private static History Read(StoredJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Observations.Count > MaximumEvidenceRecords || job.SanitizationEvidence.Count > MaximumEvidenceRecords ||
            job.SanitizationGateDecisions.Count > MaximumEvidenceRecords)
        {
            throw Invalid();
        }

        try
        {
            var gates = new GateHistory(job);
            if (!job.Observations.Any(marker => marker.Metadata.ProviderId == Provider ||
                marker.EvidenceKey.Value.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal)))
            {
                return new History([], [], gates);
            }

            var sources = new Dictionary<EvidenceId, Observation>();
            var repairs = new Dictionary<EvidenceId, RepairRecord>();
            var orderedRepairs = new List<RepairRecord>();
            var retests = new List<RepairRetestLink>();
            var assessmentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var marker in job.Observations)
            {
                if (!sources.TryAdd(marker.Metadata.EvidenceId, marker))
                {
                    throw Invalid();
                }

                var reservedKey = marker.EvidenceKey.Value.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal);
                var reservedProvider = marker.Metadata.ProviderId == Provider;
                if (!reservedKey && !reservedProvider)
                {
                    continue;
                }

                if (!reservedKey || !reservedProvider || marker.Metadata.PrivacyClassification != PrivacyClassification.WorkshopRestricted ||
                    marker.Metadata.Provenance.SourceReference != Revision || marker.Unit is not null || marker.LimitationCode is not null ||
                    marker.Metadata.ValueAvailability != ValueAvailability.Available || marker.Value?.Kind != EvidenceValueKind.Text ||
                    marker.Value.StringValue is not { Length: <= MaximumMarkerCharacters } payload)
                {
                    throw Invalid();
                }

                switch (marker.EvidenceKey.Value)
                {
                    case RepairKey:
                        var repair = ParseRepair(job, marker, payload, sources, gates);
                        repairs.Add(repair.RepairId, repair);
                        orderedRepairs.Add(repair);
                        break;
                    case RetestKey:
                        var retest = ParseRetest(marker, payload, repairs, gates);
                        if (!assessmentIds.Add(retest.AssessmentId))
                        {
                            throw Invalid();
                        }

                        retests.Add(retest);
                        break;
                    default:
                        throw Invalid();
                }
            }

            return new History(orderedRepairs.AsReadOnly(), retests.AsReadOnly(), gates);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw Invalid();
        }
    }

    private static RepairRecord ParseRepair(
        StoredJob job, Observation marker, string payload, Dictionary<EvidenceId, Observation> sources, GateHistory gates)
    {
        var repair = JsonSerializer.Deserialize<RepairRecord>(payload, Json) ?? throw Invalid();
        _ = DomainValue.RequireIdentifier(repair.PriorAssessmentId, nameof(repair.PriorAssessmentId));
        if (repair.RepairId != marker.Metadata.EvidenceId || repair.JobId != job.Job.JobId || repair.OperatorId is null ||
            repair.RecordedAtUtc != marker.Metadata.CollectedAtUtc || repair.RecordedAtUtc.Offset != TimeSpan.Zero ||
            repair.FailedEvidenceId is null || !sources.TryGetValue(repair.FailedEvidenceId, out var source) ||
            !IsRepairEvidence(source, repair.Component) || source.EvidenceKey != repair.EvidenceKey ||
            source.Metadata.CollectedAtUtc < job.Job.CreatedAtUtc || source.Metadata.CollectedAtUtc > repair.RecordedAtUtc ||
            !gates.VerifiesRepair(repair, source.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture))
        {
            throw Invalid();
        }

        var isFixture = source.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture;
        var attributed = isFixture
            ? marker.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture &&
                marker.Metadata.EvidenceClassification == EvidenceClassification.Observed && marker.OperatorId is null
            : marker.Metadata.Provenance.Kind == ProvenanceKind.HumanConfirmation && marker.OperatorId == repair.OperatorId;
        return attributed ? repair : throw Invalid();
    }

    private static RepairRetestLink ParseRetest(
        Observation marker, string payload, Dictionary<EvidenceId, RepairRecord> repairs, GateHistory gates)
    {
        var link = JsonSerializer.Deserialize<RepairRetestLink>(payload, Json) ?? throw Invalid();
        _ = DomainValue.RequireIdentifier(link.AssessmentId, nameof(link.AssessmentId));
        if (link.RetestId != marker.Metadata.EvidenceId || marker.Metadata.Provenance.Kind != ProvenanceKind.SystemGenerated ||
            marker.Metadata.EvidenceClassification != EvidenceClassification.Observed || marker.OperatorId is not null ||
            link.StartedAtUtc != marker.Metadata.CollectedAtUtc || link.StartedAtUtc.Offset != TimeSpan.Zero ||
            link.RepairId is null || !repairs.TryGetValue(link.RepairId, out var repair) ||
            link.PriorFailureEvidenceId != repair.FailedEvidenceId || link.AssessmentId == repair.PriorAssessmentId ||
            link.StartedAtUtc < repair.RecordedAtUtc || link.GateDecisionId is null ||
            !gates.Decisions.TryGetValue(link.GateDecisionId, out var gate) || gate.EvaluatedAtUtc > link.StartedAtUtc ||
            !gates.Allows(repair, gate))
        {
            throw Invalid();
        }

        return link;
    }

    private static JobLifecycleException Invalid() =>
        new("repair_journal_invalid", "The saved repair history could not be read safely.");

    private sealed record History(IReadOnlyList<RepairRecord> Repairs, IReadOnlyList<RepairRetestLink> Retests, GateHistory Gates);

    private sealed class GateHistory(StoredJob job)
    {
        private readonly Dictionary<EvidenceId, (SanitizationEvidence Evidence, int Index)> _evidence = job.SanitizationEvidence
            .Select((evidence, index) => (Evidence: evidence, Index: index)).ToDictionary(item => item.Evidence.Metadata.EvidenceId);

        public Dictionary<SanitizationGateDecisionId, SanitizationGateDecision> Decisions { get; } = job.SanitizationGateDecisions
            .ToDictionary(decision => decision.DecisionId);

        public bool VerifiesRepair(RepairRecord repair, bool isFixture)
        {
            if (repair.GateDecisionIdAtRecording is { } oldGateId &&
                (!Decisions.TryGetValue(oldGateId, out var oldGate) || oldGate.JobId != repair.JobId ||
                    oldGate.EvaluatedAtUtc > repair.RecordedAtUtc))
            {
                return false;
            }

            if (!IsStorageReplacement(repair.Component, repair.Action))
            {
                return repair.SanitizationInvalidationEvidenceId is null;
            }

            return repair.SanitizationInvalidationEvidenceId is { } invalidationId && _evidence.TryGetValue(invalidationId, out var item) &&
                item.Evidence == CreateSanitizationInvalidation(invalidationId, repair.RecordedAtUtc, isFixture);
        }

        public bool Allows(RepairRecord repair, SanitizationGateDecision gate)
        {
            if (gate.JobId != repair.JobId || gate.Outcome != SanitizationGateOutcome.AllowAssessment ||
                !_evidence.TryGetValue(gate.EvidenceId, out var applicable) || !SanitizationGate.IsConsistent(gate, applicable.Evidence))
            {
                return false;
            }

            if (!IsStorageReplacement(repair.Component, repair.Action))
            {
                return true;
            }

            return gate.Reason == SanitizationGateReason.ReplacementStorageVerified && gate.DecisionId != repair.GateDecisionIdAtRecording &&
                gate.EvaluatedAtUtc >= repair.RecordedAtUtc && applicable.Evidence.Metadata.CollectedAtUtc >= repair.RecordedAtUtc &&
                repair.SanitizationInvalidationEvidenceId is { } id && _evidence.TryGetValue(id, out var invalidation) &&
                applicable.Index > invalidation.Index &&
                (applicable.Evidence.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ==
                (invalidation.Evidence.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture);
        }
    }
}
