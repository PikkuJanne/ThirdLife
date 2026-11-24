using System.Buffers;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory;

internal static class AssessmentJournal
{
    internal const int MaximumProviders = 16;
    internal const int MaximumStoredObservations = 10_000;
    internal const string OwnershipRevision = "exclusive_evidence_owner_v1";
    internal const string ReservedProviderId = "assessment-orchestrator";
    internal const string ReservedKeyPrefix = "assessment.";
    private const int MaximumPayloadCharacters = 4096;
    private const string StartKey = "assessment.start";
    private const string ProviderKey = "assessment.provider";
    private const string FinishKey = "assessment.finish";
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create("0123456789abcdef");
    private static readonly string[] StartFields =
        ["schema_version", "assessment_id", "job_id", "gate_decision_id", "provider_ids", "ownership_revision"];
    private static readonly string[] ProviderFields =
        ["schema_version", "assessment_id", "provider_id", "outcome", "observation_ids"];
    private static readonly string[] FinishFields = ["schema_version", "assessment_id", "outcome"];

    internal static Observation CreateStart(
        JobId jobId,
        AssessmentId assessmentId,
        SanitizationGateDecisionId gateDecisionId,
        IReadOnlyList<ProviderId> providerIds,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(assessmentId);
        ArgumentNullException.ThrowIfNull(gateDecisionId);
        ArgumentNullException.ThrowIfNull(providerIds);
        if (providerIds.Count is < 1 or > MaximumProviders ||
            providerIds.Any(static provider => provider is null || provider.Value == ReservedProviderId) ||
            providerIds.Distinct().Count() != providerIds.Count)
        {
            throw Invalid(assessmentId);
        }

        return CreateMarker(assessmentId, StartKey, new
        {
            schema_version = 1,
            assessment_id = assessmentId.Value,
            job_id = jobId.Value,
            gate_decision_id = gateDecisionId.Value,
            provider_ids = providerIds.Select(static provider => provider.Value).Order(StringComparer.Ordinal).ToArray(),
            ownership_revision = OwnershipRevision,
        }, recordedAtUtc);
    }

    internal static Observation CreateProvider(
        AssessmentId assessmentId,
        ProviderRunResult result,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(assessmentId);
        ArgumentNullException.ThrowIfNull(result);
        if (result.ProviderId.Value == ReservedProviderId ||
            result.Observations.Count is < 1 or > InventoryProviderDescriptor.MaximumEvidenceKeyCount ||
            result.Observations.Any(static observation =>
                observation.EvidenceKey.Value.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal) ||
                !IsGeneratedEvidenceId(observation.Metadata.EvidenceId.Value)) ||
            result.Observations.Select(static observation => observation.Metadata.EvidenceId).Distinct().Count() !=
            result.Observations.Count)
        {
            throw Invalid(assessmentId);
        }

        return CreateMarker(assessmentId, ProviderKey, new
        {
            schema_version = 1,
            assessment_id = assessmentId.Value,
            provider_id = result.ProviderId.Value,
            outcome = (int)result.Outcome,
            observation_ids = result.Observations.Select(static observation => observation.Metadata.EvidenceId.Value).ToArray(),
        }, recordedAtUtc);
    }

    internal static Observation CreateFinish(
        AssessmentId assessmentId,
        AssessmentOutcome outcome,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(assessmentId);
        if (!Enum.IsDefined(outcome) || outcome == AssessmentOutcome.Incomplete)
        {
            throw Invalid(assessmentId);
        }

        return CreateMarker(assessmentId, FinishKey, new
        {
            schema_version = 1,
            assessment_id = assessmentId.Value,
            outcome = (int)outcome,
        }, recordedAtUtc);
    }

    internal static AssessmentSnapshot? ReadLatest(StoredJob storedJob)
    {
        ArgumentNullException.ThrowIfNull(storedJob);
        if (storedJob.Observations.Count > MaximumStoredObservations)
        {
            throw new AssessmentException("assessment_data_invalid");
        }

        Observation? latestStart = null;
        var hasJournalEvidence = false;
        foreach (var observation in storedJob.Observations)
        {
            if (observation is null)
            {
                throw new AssessmentException("assessment_data_invalid");
            }

            hasJournalEvidence |= observation.EvidenceKey.Value.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal) ||
                observation.Metadata.ProviderId.Value == ReservedProviderId;
            if (observation.EvidenceKey.Value == StartKey)
            {
                latestStart = observation;
            }
        }

        if (latestStart is null)
        {
            return hasJournalEvidence ? throw new AssessmentException("assessment_data_invalid") : null;
        }

        try
        {
            return Read(storedJob, new AssessmentId(latestStart.Metadata.Provenance.SourceReference));
        }
        catch (ArgumentException)
        {
            throw new AssessmentException("assessment_data_invalid");
        }
    }

    internal static AssessmentSnapshot? Read(StoredJob storedJob, AssessmentId assessmentId)
    {
        ArgumentNullException.ThrowIfNull(storedJob);
        ArgumentNullException.ThrowIfNull(assessmentId);
        try
        {
            return ReadCore(storedJob, assessmentId);
        }
        catch (Exception exception) when (exception is AssessmentException or ArgumentException or JsonException or
                                          InvalidOperationException or FormatException or OverflowException)
        {
            throw Invalid(assessmentId);
        }
    }

    private static AssessmentSnapshot? ReadCore(StoredJob storedJob, AssessmentId assessmentId)
    {
        if ((long)storedJob.Observations.Count + storedJob.SanitizationEvidence.Count + storedJob.HumanTests.Count > MaximumStoredObservations ||
            storedJob.SanitizationGateDecisions.Count > MaximumStoredObservations ||
            storedJob.Checkpoints.Count > MaximumStoredObservations ||
            storedJob.SanitizationGateDecisions.Any(static decision => decision is null) ||
            storedJob.SanitizationEvidence.Any(static evidence => evidence is null))
        {
            throw Invalid(assessmentId);
        }

        var observations = new Dictionary<string, (Observation Value, int Index)>(StringComparer.Ordinal);
        var markers = new List<Marker>();
        var referenceOwners = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < storedJob.Observations.Count; index++)
        {
            var observation = storedJob.Observations[index];
            if (observation is null || !observations.TryAdd(observation.Metadata.EvidenceId.Value, (observation, index)))
            {
                throw Invalid(assessmentId);
            }

            var reservedProvider = observation.Metadata.ProviderId.Value == ReservedProviderId;
            var reservedKey = observation.EvidenceKey.Value.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal);
            if (!reservedProvider && !reservedKey)
            {
                continue;
            }

            if (!reservedProvider || !reservedKey)
            {
                throw Invalid(assessmentId);
            }

            var marker = ParseMarker(observation, index);
            markers.Add(marker);
            foreach (var reference in marker.ObservationIds)
            {
                if (!observations.TryGetValue(reference, out var referenced) ||
                    referenced.Value.Metadata.ProviderId != marker.ProviderId ||
                    !referenceOwners.Add(reference))
                {
                    throw Invalid(assessmentId);
                }
            }
        }

        var selected = markers.Where(marker => marker.AssessmentId == assessmentId).ToArray();
        if (selected.Length == 0)
        {
            return null;
        }

        if (selected.Length > MaximumProviders + 2 || selected[0].Key != StartKey ||
            selected.Count(static marker => marker.Key == StartKey) != 1)
        {
            throw Invalid(assessmentId);
        }

        var start = selected[0];
        if (start.JobId != storedJob.Job.JobId || start.GateDecisionId is null)
        {
            throw Invalid(assessmentId);
        }

        ValidateHistoricalGate(storedJob, start.GateDecisionId, assessmentId);
        var providerResults = new Dictionary<ProviderId, AssessmentProviderSnapshot>();
        var keyOwners = new Dictionary<EvidenceKey, ProviderId>();
        Marker? finish = null;
        foreach (var marker in selected.Skip(1))
        {
            if (finish is not null)
            {
                throw Invalid(assessmentId);
            }

            if (marker.Key == FinishKey)
            {
                finish = marker;
                continue;
            }

            if (marker.Key != ProviderKey || marker.ProviderId is null ||
                !start.ProviderIds.Contains(marker.ProviderId) || providerResults.ContainsKey(marker.ProviderId))
            {
                throw Invalid(assessmentId);
            }

            var referenced = new List<Observation>(marker.ObservationIds.Length);
            var keySources = new HashSet<(EvidenceKey Key, string Source)>();
            var minimumIndex = marker.Index - marker.ObservationIds.Length;
            foreach (var reference in marker.ObservationIds)
            {
                if (!observations.TryGetValue(reference, out var persisted) ||
                    persisted.Index <= start.Index || persisted.Index < minimumIndex || persisted.Index >= marker.Index ||
                    persisted.Value.Metadata.ProviderId != marker.ProviderId ||
                    persisted.Value.EvidenceKey.Value.StartsWith(ReservedKeyPrefix, StringComparison.Ordinal) ||
                    !keySources.Add((persisted.Value.EvidenceKey, persisted.Value.Metadata.Provenance.SourceReference)))
                {
                    throw Invalid(assessmentId);
                }

                if (keyOwners.TryGetValue(persisted.Value.EvidenceKey, out var owner) && owner != marker.ProviderId)
                {
                    throw Invalid(assessmentId);
                }

                keyOwners[persisted.Value.EvidenceKey] = marker.ProviderId;
                referenced.Add(persisted.Value);
            }

            ValidateProviderOutcome(marker, referenced, assessmentId);
            providerResults.Add(marker.ProviderId, new AssessmentProviderSnapshot(
                marker.ProviderId,
                marker.ProviderOutcome,
                referenced.OrderBy(static observation => observation.EvidenceKey.Value, StringComparer.Ordinal)
                    .ThenBy(static observation => observation.Metadata.Provenance.SourceReference, StringComparer.Ordinal)));
        }

        var providers = start.ProviderIds.Select(provider => providerResults.TryGetValue(provider, out var result)
            ? result
            : new AssessmentProviderSnapshot(provider, outcome: null, [])).ToArray();
        var outcome = finish?.Outcome ?? AssessmentOutcome.Incomplete;
        if (outcome == AssessmentOutcome.Completed &&
            providers.Any(static provider => provider.Outcome != ProviderRunOutcome.Completed))
        {
            throw Invalid(assessmentId);
        }

        return new AssessmentSnapshot(
            assessmentId, storedJob.Job.JobId, start.GateDecisionId,
            start.Observation.Metadata.CollectedAtUtc, finish?.Observation.Metadata.CollectedAtUtc,
            outcome, providers);
    }

    private static void ValidateHistoricalGate(
        StoredJob storedJob,
        SanitizationGateDecisionId gateDecisionId,
        AssessmentId assessmentId)
    {
        var decisions = storedJob.SanitizationGateDecisions.Where(decision => decision.DecisionId == gateDecisionId).Take(2).ToArray();
        if (decisions.Length != 1 || decisions[0].JobId != storedJob.Job.JobId ||
            decisions[0].Outcome != SanitizationGateOutcome.AllowAssessment)
        {
            throw Invalid(assessmentId);
        }

        var evidence = storedJob.SanitizationEvidence.Where(value => value.Metadata.EvidenceId == decisions[0].EvidenceId).Take(2).ToArray();
        if (evidence.Length != 1 || !SanitizationGate.IsConsistent(decisions[0], evidence[0]))
        {
            throw Invalid(assessmentId);
        }
    }

    private static void ValidateProviderOutcome(Marker marker, List<Observation> observations, AssessmentId assessmentId)
    {
        if (marker.ProviderOutcome != ProviderRunOutcome.Completed)
        {
            var expected = new SanitizedProviderError(marker.ProviderOutcome!.Value).Limitation.ToCode();
            if (!observations.Any(observation =>
                    observation.Metadata.EvidenceClassification == EvidenceClassification.NotAvailable &&
                    observation.Metadata.ValueAvailability == ValueAvailability.Unknown &&
                    observation.Value is null && observation.LimitationCode == expected))
            {
                throw Invalid(assessmentId);
            }
        }
    }

    private static Observation CreateMarker<T>(AssessmentId assessmentId, string key, T payload, DateTimeOffset recordedAtUtc)
    {
        var json = JsonSerializer.Serialize(payload);
        if (json.Length > MaximumPayloadCharacters)
        {
            throw Invalid(assessmentId);
        }

        return new Observation(
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted,
                EvidenceClassification.Observed, new ProviderId(ReservedProviderId), recordedAtUtc,
                new EvidenceProvenance(ProvenanceKind.SystemGenerated, assessmentId.Value), ValueAvailability.Available),
            new EvidenceKey(key), EvidenceValue.FromString(json), unit: null, limitationCode: null);
    }

    private static Marker ParseMarker(Observation observation, int index)
    {
        var assessmentId = new AssessmentId(observation.Metadata.Provenance.SourceReference);
        if (observation.Metadata.PrivacyClassification != PrivacyClassification.WorkshopRestricted ||
            observation.Metadata.Provenance.Kind != ProvenanceKind.SystemGenerated ||
            observation.Metadata.EvidenceClassification != EvidenceClassification.Observed ||
            observation.Metadata.ValueAvailability != ValueAvailability.Available ||
            observation.Value?.Kind != EvidenceValueKind.Text || observation.Value.StringValue is null ||
            observation.Value.StringValue.Length > MaximumPayloadCharacters || observation.Unit is not null ||
            observation.LimitationCode is not null || observation.OperatorId is not null ||
            !IsGeneratedEvidenceId(observation.Metadata.EvidenceId.Value))
        {
            throw Invalid(assessmentId);
        }

        var key = observation.EvidenceKey.Value;
        var fields = key switch
        {
            StartKey => StartFields,
            ProviderKey => ProviderFields,
            FinishKey => FinishFields,
            _ => throw Invalid(assessmentId),
        };
        using var document = JsonDocument.Parse(observation.Value.StringValue, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(assessmentId);
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name))
            {
                throw Invalid(assessmentId);
            }
        }

        if (found.Count != fields.Length || root.GetProperty("schema_version").GetInt32() != 1 ||
            Text(root, "assessment_id") != assessmentId.Value)
        {
            throw Invalid(assessmentId);
        }

        var marker = new Marker(assessmentId, key, observation, index);
        if (key == StartKey)
        {
            var providers = Strings(root.GetProperty("provider_ids"), MaximumProviders, assessmentId)
                .Select(static value => new ProviderId(value)).ToArray();
            if (providers.Any(static provider => provider.Value == ReservedProviderId) ||
                !providers.Select(static provider => provider.Value).SequenceEqual(
                    providers.Select(static provider => provider.Value).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                Text(root, "ownership_revision") != OwnershipRevision)
            {
                throw Invalid(assessmentId);
            }

            return marker with
            {
                JobId = new JobId(Text(root, "job_id")),
                GateDecisionId = new SanitizationGateDecisionId(Text(root, "gate_decision_id")),
                ProviderIds = providers,
            };
        }

        var outcome = root.GetProperty("outcome").GetInt32();
        if (key == FinishKey)
        {
            if (!Enum.IsDefined((AssessmentOutcome)outcome) || outcome == (int)AssessmentOutcome.Incomplete)
            {
                throw Invalid(assessmentId);
            }

            return marker with { Outcome = (AssessmentOutcome)outcome };
        }

        var ids = Strings(root.GetProperty("observation_ids"), InventoryProviderDescriptor.MaximumEvidenceKeyCount, assessmentId);
        var providerId = new ProviderId(Text(root, "provider_id"));
        if (!Enum.IsDefined((ProviderRunOutcome)outcome) || ids.Any(static id => !IsGeneratedEvidenceId(id)) ||
            providerId.Value == ReservedProviderId)
        {
            throw Invalid(assessmentId);
        }

        return marker with { ProviderId = providerId, ProviderOutcome = (ProviderRunOutcome)outcome, ObservationIds = ids };
    }

    private static string Text(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new FormatException("The assessment marker is invalid.");

    private static string[] Strings(JsonElement array, int maximumCount, AssessmentId assessmentId)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 || array.GetArrayLength() > maximumCount)
        {
            throw Invalid(assessmentId);
        }

        var values = array.EnumerateArray().Select(static element =>
            element.GetString() ?? throw new FormatException("The assessment marker is invalid.")).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw Invalid(assessmentId);
        }

        return values;
    }

    private static bool IsGeneratedEvidenceId(string value) =>
        value.Length == 41 && value.StartsWith("evidence-", StringComparison.Ordinal) &&
        !value.AsSpan(9).ContainsAnyExcept(HexCharacters);

    private static AssessmentException Invalid(AssessmentId assessmentId) => new("assessment_data_invalid", assessmentId);

    private sealed record Marker(AssessmentId AssessmentId, string Key, Observation Observation, int Index)
    {
        public JobId? JobId { get; init; }
        public SanitizationGateDecisionId? GateDecisionId { get; init; }
        public ProviderId[] ProviderIds { get; init; } = [];
        public ProviderId? ProviderId { get; init; }
        public ProviderRunOutcome? ProviderOutcome { get; init; }
        public string[] ObservationIds { get; init; } = [];
        public AssessmentOutcome? Outcome { get; init; }
    }
}
