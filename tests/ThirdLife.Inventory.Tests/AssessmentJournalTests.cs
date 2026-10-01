using System.Globalization;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory.Normalization;

namespace ThirdLife.Inventory.Tests;

public sealed class AssessmentJournalTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Job TestJob = new(new JobId("journal-job"), new DeviceId("journal-device"), Timestamp);
    private static readonly ProviderId Alpha = new("provider-alpha");
    private static readonly ProviderId Beta = new("provider-beta");
    private static readonly SanitizationEvidence Sanitization = CreateSanitization();
    private static readonly SanitizationGateDecision Gate = SanitizationGate.Evaluate(TestJob.JobId, Sanitization, Timestamp);

    [Fact]
    public void GeneratedAssessmentIdentityIsOpaqueAndStrict()
    {
        var first = AssessmentId.New();
        Assert.NotEqual(first, AssessmentId.New());
        Assert.Equal(first, new AssessmentId(first.Value));
        Assert.Equal(43, first.Value.Length);
        Assert.Equal(first.Value, first.ToString());
        Assert.Throws<ArgumentNullException>(() => new AssessmentId(null!));
        foreach (var invalid in new[] { "", "assessment-personal-name", first.Value.ToUpperInvariant(), first.Value + "0", " " + first.Value })
        {
            Assert.Throws<ArgumentException>(() => new AssessmentId(invalid));
        }
    }

    [Fact]
    public void MissingRunIsAbsentAndStartAloneIsAnImmutableIncompleteSnapshot()
    {
        var id = AssessmentId.New();
        Assert.Null(AssessmentJournal.Read(Stored([]), id));
        var start = Start(id, [Beta, Alpha]);
        var snapshot = Assert.IsType<AssessmentSnapshot>(AssessmentJournal.Read(Stored([start]), id));

        Assert.Equal(id, snapshot.AssessmentId);
        Assert.Equal(TestJob.JobId, snapshot.JobId);
        Assert.Equal(Gate.DecisionId, snapshot.GateDecisionId);
        Assert.Equal(Timestamp, snapshot.StartedAtUtc);
        Assert.Null(snapshot.FinishedAtUtc);
        Assert.Equal(AssessmentOutcome.Incomplete, snapshot.Outcome);
        Assert.Equal([Alpha, Beta], snapshot.Providers.Select(static provider => provider.ProviderId));
        Assert.All(snapshot.Providers, static provider => { Assert.Null(provider.Outcome); Assert.Empty(provider.Observations); });
        Assert.Empty(snapshot.Observations);
        Assert.Empty(snapshot.LifecycleEvidence);
        Assert.Throws<NotSupportedException>(() => ((IList<AssessmentProviderSnapshot>)snapshot.Providers).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Observation>)snapshot.Observations).Clear());

        Assert.Equal(PrivacyClassification.WorkshopRestricted, start.Metadata.PrivacyClassification);
        Assert.Equal(ProvenanceKind.SystemGenerated, start.Metadata.Provenance.Kind);
        Assert.Equal(id.Value, start.Metadata.Provenance.SourceReference);
        Assert.Equal("assessment-orchestrator", start.Metadata.ProviderId.Value);
        Assert.Null(start.OperatorId);
        Assert.Null(start.Unit);
        Assert.Null(start.LimitationCode);
    }

    [Fact]
    public void ProviderOrderAndClockChangesDoNotChangeSnapshotMembershipOrSemanticOrder()
    {
        var id = AssessmentId.New();
        var alpha = Completed(Alpha,
            Fact(Alpha, "alpha.z", "last", "fixture.z"),
            Fact(Alpha, "alpha.a", "second", "fixture.b"),
            Fact(Alpha, "alpha.a", "first", "fixture.a"));
        var beta = Completed(Beta, Fact(Beta, "beta.value", "beta"));
        var initial = Start(id, [Beta, Alpha]);
        var final = AssessmentJournal.CreateFinish(id, AssessmentOutcome.Completed, Timestamp.AddHours(-1));
        var forward = Read([initial, .. Batch(id, alpha), .. Batch(id, beta), final], id);
        var backward = Read([initial, .. Batch(id, beta), .. Batch(id, alpha), final], id);

        Assert.Equal(AssessmentOutcome.Completed, forward.Outcome);
        Assert.Equal(Timestamp.AddHours(-1), forward.FinishedAtUtc);
        Assert.Equal(forward.Observations, backward.Observations);
        Assert.Equal(["first", "second", "last", "beta"], forward.Observations.Select(static observation => observation.Value!.StringValue));
        Assert.All(forward.Providers, static provider => Assert.Equal(ProviderRunOutcome.Completed, provider.Outcome));
        Assert.Throws<NotSupportedException>(() => ((IList<Observation>)forward.Providers[0].Observations).Clear());
    }

    [Theory]
    [InlineData(AssessmentOutcome.Cancelled)]
    [InlineData(AssessmentOutcome.RequiresReview)]
    public void PartialFinishRetainsIndependentFactsAndUnrunProviders(AssessmentOutcome outcome)
    {
        var id = AssessmentId.New();
        var failed = Failed(Alpha, ProviderRunOutcome.TimedOut);
        var snapshot = Read([Start(id, [Alpha, Beta]), .. Batch(id, failed), AssessmentJournal.CreateFinish(id, outcome, Timestamp)], id);
        Assert.Equal(outcome, snapshot.Outcome);
        Assert.Equal(ProviderRunOutcome.TimedOut, snapshot.Providers[0].Outcome);
        Assert.Null(snapshot.Providers[1].Outcome);
        Assert.Single(snapshot.Observations);
        Assert.Null(snapshot.Observations[0].Value);
        Assert.Equal(ValueAvailability.Unknown, snapshot.Observations[0].Metadata.ValueAvailability);
    }

    [Fact]
    public void HistoricalLifecycleSnapshotKeepsExactReferencesAfterRerunAndNewGate()
    {
        var oldId = AssessmentId.New();
        var newId = AssessmentId.New();
        var oldResult = Completed(Alpha, Fact(Alpha, "os.lifecycle.revision", "revision-one"),
            Fact(Alpha, "os.lifecycle.as_of", "2026-09-30"), Fact(Alpha, "os.support_target", "supported"));
        var newResult = Completed(Alpha, Fact(Alpha, "os.lifecycle.revision", "revision-two"),
            Fact(Alpha, "os.lifecycle.as_of", "2026-10-30"), Fact(Alpha, "os.support_target", "unknown"));
        List<Observation> history =
        [
            Start(oldId, [Alpha]),
            .. Batch(oldId, oldResult),
            AssessmentJournal.CreateFinish(oldId, AssessmentOutcome.Completed, Timestamp),
        ];
        var before = Read(history, oldId);
        history.AddRange([Start(newId, [Alpha]), .. Batch(newId, newResult), AssessmentJournal.CreateFinish(newId, AssessmentOutcome.Completed, Timestamp)]);
        var replacement = CreateSanitization();
        var newGate = SanitizationGate.Evaluate(TestJob.JobId, replacement, Timestamp);
        var archived = new StoredJob(TestJob, true, Timestamp, history, [Sanitization, replacement], [], [Gate, newGate], []);
        var after = Assert.IsType<AssessmentSnapshot>(AssessmentJournal.Read(archived, oldId));

        Assert.Equal(before.LifecycleEvidence, after.LifecycleEvidence);
        Assert.Equal(oldResult.Observations.OrderBy(static observation => observation.EvidenceKey.Value, StringComparer.Ordinal), after.LifecycleEvidence);
        Assert.Equal("revision-one", after.LifecycleEvidence.Single(static observation => observation.EvidenceKey.Value == "os.lifecycle.revision").Value!.StringValue);
        Assert.Equal("revision-two", Read(history, newId).LifecycleEvidence.Single(static observation => observation.EvidenceKey.Value == "os.lifecycle.revision").Value!.StringValue);
        Assert.Throws<NotSupportedException>(() => ((IList<Observation>)after.LifecycleEvidence).Clear());
    }

    [Fact]
    public void LargestProviderBatchAndRosterFitTheBoundedJournalPayload()
    {
        var id = AssessmentId.New();
        var facts = Enumerable.Range(0, 64).Select(index => Fact(Alpha, "bulk.value", "value", string.Create(CultureInfo.InvariantCulture, $"fixture.{index}"))).ToArray();
        var result = Completed(Alpha, facts);
        var marker = AssessmentJournal.CreateProvider(id, result, Timestamp);
        Assert.InRange(marker.Value!.StringValue!.Length, 1, 4096);
        var snapshot = Read([Start(id, [Alpha]), .. facts, marker, AssessmentJournal.CreateFinish(id, AssessmentOutcome.Completed, Timestamp)], id);
        Assert.Equal(64, snapshot.Observations.Count);

        var providers = Enumerable.Range(0, 16).Select(index => new ProviderId(string.Create(CultureInfo.InvariantCulture, $"provider{index:D2}{new string('a', 118)}"))).ToArray();
        Assert.InRange(Start(id, providers).Value!.StringValue!.Length, 1, 4096);
        Assert.Throws<AssessmentException>(() => Start(id, [.. providers, Beta]));
        Assert.Throws<AssessmentException>(() => Start(id, []));
        Assert.Throws<AssessmentException>(() => Start(id, [Alpha, Alpha]));
        Assert.Throws<AssessmentException>(() => Start(id, [new ProviderId("assessment-orchestrator")]));
        Assert.Throws<AssessmentException>(() => AssessmentJournal.CreateProvider(id, Completed(Alpha, [.. facts, Fact(Alpha, "bulk.extra", "extra")]), Timestamp));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("id")]
    [InlineData("ownership")]
    [InlineData("unsorted")]
    [InlineData("duplicate_provider")]
    [InlineData("empty_provider")]
    [InlineData("provider_type")]
    [InlineData("root")]
    [InlineData("malformed")]
    public void MalformedStartPayloadFailsClosedWithoutLeakingInput(string mutation)
    {
        var id = AssessmentId.New();
        var start = Start(id, [Alpha, Beta]);
        var node = JsonNode.Parse(start.Value!.StringValue!)!.AsObject();
        var raw = mutation switch
        {
            "duplicate" => start.Value.StringValue!.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1", StringComparison.Ordinal),
            "root" => "[]",
            "malformed" => "{\"private_canary\":",
            _ => string.Empty,
        };
        switch (mutation)
        {
            case "unknown": node["private_canary"] = "private_canary"; break;
            case "missing": node.Remove("job_id"); break;
            case "schema": node["schema_version"] = 2; break;
            case "id": node["assessment_id"] = AssessmentId.New().Value; break;
            case "ownership": node["ownership_revision"] = "unknown_rule"; break;
            case "unsorted": node["provider_ids"] = new JsonArray(Beta.Value, Alpha.Value); break;
            case "duplicate_provider": node["provider_ids"] = new JsonArray(Alpha.Value, Alpha.Value); break;
            case "empty_provider": node["provider_ids"] = new JsonArray(); break;
            case "provider_type": node["provider_ids"] = "private_canary"; break;
        }

        AssertInvalid([Payload(start, raw.Length == 0 ? node.ToJsonString() : raw)], id);
    }

    [Theory]
    [InlineData("missing_reference")]
    [InlineData("duplicate_reference")]
    [InlineData("foreign_provider")]
    [InlineData("unrequested_provider")]
    [InlineData("outcome")]
    [InlineData("wrong_failure_outcome")]
    [InlineData("duplicate_field")]
    [InlineData("extra_field")]
    [InlineData("empty_references")]
    public void MalformedProviderReferencesAndOutcomesFailClosed(string mutation)
    {
        var id = AssessmentId.New();
        var fact = Fact(Alpha, "alpha.value", "value");
        var marker = AssessmentJournal.CreateProvider(id, Completed(Alpha, fact), Timestamp);
        var node = JsonNode.Parse(marker.Value!.StringValue!)!.AsObject();
        switch (mutation)
        {
            case "missing_reference": node["observation_ids"] = new JsonArray(EvidenceId.New().Value); break;
            case "duplicate_reference": node["observation_ids"] = new JsonArray(fact.Metadata.EvidenceId.Value, fact.Metadata.EvidenceId.Value); break;
            case "foreign_provider": node["provider_id"] = Beta.Value; break;
            case "unrequested_provider": node["provider_id"] = "provider-other"; break;
            case "outcome": node["outcome"] = 999; break;
            case "wrong_failure_outcome": node["outcome"] = (int)ProviderRunOutcome.AccessDenied; break;
            case "extra_field": node["private_canary"] = true; break;
            case "empty_references": node["observation_ids"] = new JsonArray(); break;
        }

        var raw = mutation == "duplicate_field"
            ? marker.Value.StringValue!.Replace("\"outcome\":1", "\"outcome\":1,\"outcome\":1", StringComparison.Ordinal)
            : node.ToJsonString();
        AssertInvalid([Start(id, [Alpha, Beta]), fact, Payload(marker, raw)], id);
    }

    [Fact]
    public void MarkerSequenceRequiresWholeUniqueProviderBatchesAndOneFinish()
    {
        var id = AssessmentId.New();
        var start = Start(id, [Alpha]);
        var result = Completed(Alpha, Fact(Alpha, "alpha.value", "value"));
        var batch = Batch(id, result);
        var finish = AssessmentJournal.CreateFinish(id, AssessmentOutcome.Completed, Timestamp);
        AssertInvalid([.. batch, start], id);
        AssertInvalid([start, start], id);
        AssertInvalid([start, finish], id);
        AssertInvalid([start, .. batch, finish, finish], id);
        AssertInvalid([start, finish, .. batch], id);
        AssertInvalid([start, batch[0], Fact(Beta, "beta.other", "other"), batch[1]], id);
        AssertInvalid([start, .. batch, .. Batch(id, Completed(Alpha, Fact(Alpha, "alpha.value", "new")))], id);
        AssertInvalid([start, .. Batch(id, Failed(Alpha, ProviderRunOutcome.Unavailable)), finish], id);
        Assert.Throws<AssessmentException>(() => AssessmentJournal.CreateFinish(id, AssessmentOutcome.Incomplete, Timestamp));
        Assert.Throws<AssessmentException>(() => AssessmentJournal.CreateFinish(id, (AssessmentOutcome)999, Timestamp));
    }

    [Fact]
    public void SameObservationCannotBelongToTwoAssessmentRuns()
    {
        var first = AssessmentId.New();
        var second = AssessmentId.New();
        var result = Completed(Alpha, Fact(Alpha, "alpha.value", "value"));
        AssertInvalid([Start(first, [Alpha]), .. Batch(first, result), Start(second, [Alpha]), AssessmentJournal.CreateProvider(second, result, Timestamp)], first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignRunReferencesMustAlreadyExistAndMatchTheirDeclaredProvider(bool wrongProvider)
    {
        var first = AssessmentId.New();
        var second = AssessmentId.New();
        var result = Completed(Alpha, Fact(Alpha, "alpha.value", "value"));
        var marker = AssessmentJournal.CreateProvider(second, result, Timestamp);
        var node = JsonNode.Parse(marker.Value!.StringValue!)!.AsObject();
        if (wrongProvider)
        {
            node["provider_id"] = Beta.Value;
        }

        var history = wrongProvider
            ? new[] { Start(first, [Alpha]), Start(second, [Alpha, Beta]), result.Observations[0], Payload(marker, node.ToJsonString()) }
            : new[] { Start(first, [Alpha]), Start(second, [Alpha]), marker, result.Observations[0] };
        AssertInvalid(history, first);
    }

    [Fact]
    public void ConflictingSemanticOwnershipIsRejectedInEitherProviderOrder()
    {
        var id = AssessmentId.New();
        var alpha = Completed(Alpha, Fact(Alpha, "shared.fact", "one"));
        var beta = Completed(Beta, Fact(Beta, "shared.fact", "two"));
        AssertInvalid([Start(id, [Alpha, Beta]), .. Batch(id, alpha), .. Batch(id, beta)], id);
        AssertInvalid([Start(id, [Alpha, Beta]), .. Batch(id, beta), .. Batch(id, alpha)], id);
    }

    [Fact]
    public void JournalCannotClaimAnAbsentBlockedOrDifferentJobGate()
    {
        var id = AssessmentId.New();
        var start = Start(id, [Alpha]);
        AssertInvalid(Stored([start], decisions: []), id);
        AssertInvalid(Stored([start], sanitization: []), id);
        AssertInvalid(Stored([start], decisions: [Gate, Gate]), id);
        AssertInvalid(Stored([Payload(start, start.Value!.StringValue!.Replace(TestJob.JobId.Value, "another-job", StringComparison.Ordinal))]), id);
        var blockedEvidence = new SanitizationEvidence(
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.NotAvailable,
                new ProviderId("sanitization"), Timestamp, new EvidenceProvenance(ProvenanceKind.SystemGenerated, "sanitization.unknown"), ValueAvailability.Unknown),
            SanitizationState.Unknown, "unknown", null, null, null, SanitizationVerificationState.NotAvailable, "policy@1");
        var blockedGate = SanitizationGate.Evaluate(TestJob.JobId, blockedEvidence, Timestamp);
        var blockedStart = AssessmentJournal.CreateStart(TestJob.JobId, id, blockedGate.DecisionId, [Alpha], Timestamp);
        AssertInvalid(Stored([blockedStart], [blockedEvidence], [blockedGate]), id);
    }

    [Fact]
    public void ReservedNamespaceAndMetadataAreClosedAndStoreParsingIsBounded()
    {
        var id = AssessmentId.New();
        var start = Start(id, [Alpha]);
        AssertInvalid([new Observation(start.Metadata, new EvidenceKey("assessment.unknown"), start.Value, null, null)], id);
        var wrongProvider = new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            Alpha, Timestamp, start.Metadata.Provenance, ValueAvailability.Available);
        AssertInvalid([new Observation(wrongProvider, start.EvidenceKey, start.Value, null, null)], id);
        AssertInvalid([new Observation(start.Metadata, new EvidenceKey("unreserved.key"), start.Value, null, null)], id);
        var wrongProvenance = new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
            start.Metadata.ProviderId, Timestamp, new EvidenceProvenance(ProvenanceKind.ProviderObservation, id.Value), ValueAvailability.Available);
        AssertInvalid([new Observation(wrongProvenance, start.EvidenceKey, start.Value, null, null)], id);
        AssertInvalid([new Observation(start.Metadata, start.EvidenceKey, start.Value, "unexpected", null)], id);
        AssertInvalid(Enumerable.Repeat(start, 10_001), id);
        Assert.Throws<AssessmentException>(() => AssessmentJournal.CreateProvider(id, Completed(Alpha, Fact(Alpha, "assessment.private", "value")), Timestamp));
    }

    private static Observation Start(AssessmentId id, IReadOnlyList<ProviderId> providers) =>
        AssessmentJournal.CreateStart(TestJob.JobId, id, Gate.DecisionId, providers, Timestamp);

    private static ProviderRunResult Completed(ProviderId providerId, params Observation[] observations) =>
        ProviderRunResult.Completed(providerId, observations);

    private static ProviderRunResult Failed(ProviderId providerId, ProviderRunOutcome outcome)
    {
        var error = new SanitizedProviderError(outcome);
        var observation = new Observation(
            new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.NotAvailable,
                providerId, Timestamp, new EvidenceProvenance(ProvenanceKind.SystemGenerated, "fixture.failure"), ValueAvailability.Unknown),
            new EvidenceKey("alpha.failure"), null, null, error.Limitation.ToCode());
        return ProviderRunResult.Failed(providerId, outcome, [observation], error);
    }

    private static Observation Fact(ProviderId providerId, string key, string value, string source = "fixture.value") =>
        new(new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
                providerId, Timestamp, new EvidenceProvenance(ProvenanceKind.SyntheticFixture, source), ValueAvailability.Available),
            new EvidenceKey(key), EvidenceValue.FromString(value), null, null);

    private static Observation[] Batch(AssessmentId id, ProviderRunResult result) =>
        [.. result.Observations, AssessmentJournal.CreateProvider(id, result, Timestamp)];

    private static Observation Payload(Observation marker, string json) =>
        new(marker.Metadata, marker.EvidenceKey, EvidenceValue.FromString(json), null, null);

    private static StoredJob Stored(
        IEnumerable<Observation> observations,
        IEnumerable<SanitizationEvidence>? sanitization = null,
        IEnumerable<SanitizationGateDecision>? decisions = null) =>
        new(TestJob, false, null, observations, sanitization ?? [Sanitization], [], decisions ?? [Gate], []);

    private static AssessmentSnapshot Read(IEnumerable<Observation> observations, AssessmentId id) =>
        Assert.IsType<AssessmentSnapshot>(AssessmentJournal.Read(Stored(observations), id));

    private static void AssertInvalid(IEnumerable<Observation> observations, AssessmentId id) => AssertInvalid(Stored(observations), id);

    private static void AssertInvalid(StoredJob storedJob, AssessmentId id)
    {
        var exception = Assert.Throws<AssessmentException>(() => AssessmentJournal.Read(storedJob, id));
        Assert.Equal("assessment_data_invalid", exception.ResultCode);
        Assert.Equal(id, exception.AssessmentId);
        Assert.DoesNotContain("private_canary", exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    private static SanitizationEvidence CreateSanitization() =>
        new(new EvidenceMetadata(EvidenceId.New(), PrivacyClassification.WorkshopRestricted, EvidenceClassification.Observed,
                new ProviderId("sanitization"), Timestamp, new EvidenceProvenance(ProvenanceKind.SyntheticFixture, "fixture.sanitization"), ValueAvailability.Available),
            SanitizationState.NoDonorStorage, "no_donor_storage", new OperatorId("fixture-operator"), Timestamp, null,
            SanitizationVerificationState.Verified, "policy@1");
}
