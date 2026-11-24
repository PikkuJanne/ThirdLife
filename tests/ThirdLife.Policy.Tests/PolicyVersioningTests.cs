using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Versioning;
using static ThirdLife.Policy.Tests.PolicyEvaluationFixture;

namespace ThirdLife.Policy.Tests;

public sealed class PolicyVersioningTests
{
    private const string AssessmentId = "synthetic-assessment-001";
    private static readonly JobId Job = new("synthetic-replay-job");
    private static readonly DateTimeOffset EvaluationTime = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SnapshotOwnsOriginalBytesAndUsesAnExactStableSha256()
    {
        var bytes = CandidatePolicyJson();
        var original = bytes.ToArray();
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(original));

        var snapshot = PolicySnapshot.Parse(bytes);
        bytes[0] = (byte)' ';
        var restored = PolicySnapshot.Restore(original, expectedHash);
        var withWhitespace = PolicySnapshot.Parse(original.Append((byte)' ').ToArray());

        Assert.Equal(original, snapshot.Utf8Json.AsEnumerable());
        Assert.Equal(original, restored.Utf8Json.AsEnumerable());
        Assert.Equal(expectedHash, snapshot.Document.ContentSha256);
        Assert.Equal(expectedHash, restored.Document.ContentSha256);
        Assert.Equal(snapshot.Document.Version, withWhitespace.Document.Version);
        Assert.NotEqual(expectedHash, withWhitespace.Document.ContentSha256);
        Assert.Equal("0.1.0-candidate.1", restored.Document.Version);
    }

    [Fact]
    public void SnapshotRestoreRejectsWrongContentHashAndInvalidContent()
    {
        var original = CandidatePolicyJson();
        var oldHash = Convert.ToHexStringLower(SHA256.HashData(original));
        var modified = original.Append((byte)' ').ToArray();
        var malformed = "{"u8.ToArray();
        var matchingMalformedHash = Convert.ToHexStringLower(SHA256.HashData(malformed));

        Assert.Throws<JsonException>(() => PolicySnapshot.Restore(modified, oldHash));
        Assert.Throws<JsonException>(() => PolicySnapshot.Restore(malformed, matchingMalformedHash));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("0.1.0-candidate.1", "0.1.0-candidate.1")]
    [InlineData("0.0.0-0", "0.0.0-0")]
    public void IdenticalVersionsHaveEqualOrder(string left, string right) =>
        Assert.Equal(0, PolicyVersionOrder.Compare(left, right));

    [Theory]
    [InlineData("1.2.9", "1.2.10")]
    [InlineData("1.9.99", "1.10.0")]
    [InlineData("9.9.9", "10.0.0")]
    [InlineData("0.1.0-candidate.2", "0.1.0-candidate.12")]
    [InlineData("0.1.0-candidate.12", "0.1.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.0")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-Candidate", "1.0.0-candidate")]
    [InlineData("9999999999999999999999999.1.0", "10000000000000000000000000.0.0")]
    [InlineData("1.0.0-9999999999999999999999999", "1.0.0-10000000000000000000000000")]
    public void VersionOrderingIsNumericWithoutOverflowAndPrereleaseIsOrdinal(string older, string newer)
    {
        Assert.True(PolicyVersionOrder.Compare(older, newer) < 0);
        Assert.True(PolicyVersionOrder.Compare(newer, older) > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+reconstructed")]
    [InlineData(" 1.2.3")]
    public void VersionOrderRejectsInputOutsideThePolicyVersionContract(string version)
    {
        Assert.Throws<ArgumentException>(() => PolicyVersionOrder.Compare(version, "1.2.3"));
        Assert.Throws<ArgumentException>(() => PolicyVersionOrder.Compare("1.2.3", version));
    }

    [Fact]
    public void HistoricalReportRemainsReproducibleAfterAnExplicitReevaluation()
    {
        var older = PolicySnapshot.Parse(CandidatePolicyJson());
        var evidence = ReadyObservations();
        var ids = EvidenceIds(evidence);
        var prior = Capture(older, Evaluate(older, evidence), ids, EvaluationTime);
        var originalOutput = JsonSerializer.Serialize(prior);
        var updated = PolicySnapshot.Parse(NewPolicyBytes());

        var subsequent = Capture(updated, Evaluate(updated, evidence), ids.Reverse(),
            EvaluationTime.AddMinutes(10), prior.DecisionId);
        var restoredPolicy = PolicySnapshot.Restore(older.Utf8Json.ToArray(), prior.PolicySha256);
        var historicDecision = Evaluate(restoredPolicy, evidence);

        Assert.Equal(Disposition.ReadyToPrepare, prior.Disposition);
        Assert.Equal(Disposition.RepairAndRetest, subsequent.Disposition);
        Assert.NotEqual(prior.DecisionId, subsequent.DecisionId);
        Assert.Null(prior.PreviousDecisionId);
        Assert.Equal(prior.DecisionId, subsequent.PreviousDecisionId);
        Assert.Equal(prior.AssessmentId, subsequent.AssessmentId);
        Assert.Equal(prior.JobId, subsequent.JobId);
        Assert.Equal(prior.EvidenceIds.AsEnumerable(), subsequent.EvidenceIds.AsEnumerable());
        Assert.Equal(older.Document.PolicyId, prior.PolicyId);
        Assert.Equal(older.Document.Version, prior.PolicyVersion);
        Assert.Equal(updated.Document.Version, subsequent.PolicyVersion);
        Assert.Equal(updated.Document.ContentSha256, subsequent.PolicySha256);
        Assert.NotEqual(prior.PolicySha256, subsequent.PolicySha256);
        Assert.Equal(prior.Title, historicDecision.Title);
        Assert.Equal(prior.Summary, historicDecision.Summary);
        Assert.Equal(prior.Explanation, historicDecision.Explanation);
        Assert.Equal(prior.RuleReport, PolicyRuleReport.Render(historicDecision));
        Assert.Equal(originalOutput, JsonSerializer.Serialize(prior));
    }

    [Fact]
    public void DecisionCapturesSafeReportAndCanonicalReferencesForStrictRoundTrip()
    {
        const string rawObservationValue = "synthetic-raw-unexpected-storage-value";
        const string privateReason = "Synthetic restricted note for historical testing.";
        var policyJson = JsonNode.Parse(CandidatePolicyJson())!.AsObject();
        FindStorageRule(policyJson)["exception_authorization_id"] = "synthetic-owner-grant";
        var snapshot = PolicySnapshot.Parse(Encoding.UTF8.GetBytes(policyJson.ToJsonString()));
        var unsupportedHealth = Fact("storage-health-unexpected", "storage.system.health_state",
            EvidenceValue.FromEnum(rawObservationValue));
        var lowFreeSpace = Fact("storage-low", "storage.system.free_bytes", EvidenceValue.FromInteger(10_737_418_240), unit: "bytes");
        var evidence = ReadyObservations()
            .Where(observation => observation.EvidenceKey != unsupportedHealth.EvidenceKey &&
                observation.EvidenceKey != lowFreeSpace.EvidenceKey)
            .Append(unsupportedHealth).Append(lowFreeSpace).ToArray();
        var ids = EvidenceIds(evidence);
        var history = new PolicyExceptionHistory(Job);
        var before = DispositionEngine.Evaluate(
            snapshot.Document, evidence, ActiveCapabilities(), history, EvaluationTime.AddMinutes(-1));
        var recorded = history.Record(before, new RequirementId("POL-STORAGE-002"),
            new OperatorId("synthetic-history-operator"), privateReason, EvaluationTime);
        var decision = DispositionEngine.Evaluate(snapshot.Document, evidence, ActiveCapabilities(), recorded, EvaluationTime);
        var record = Capture(snapshot, decision, ids.Reverse().Append(ids[0]), EvaluationTime);
        var options = DomainJson.CreateStrictOptions();

        var json = JsonSerializer.Serialize(record, options);
        var restored = JsonSerializer.Deserialize<PolicyDecisionRecord>(json, options);

        Assert.NotNull(restored);
        Assert.True(record.HasEvaluationProvenance);
        Assert.False(restored.HasEvaluationProvenance);
        Assert.Equal(record.DecisionId, restored.DecisionId);
        Assert.Equal(AssessmentId, restored.AssessmentId);
        Assert.Equal(EvaluationTime, restored.EvaluatedAtUtc);
        Assert.Equal(record.Explanation, restored.Explanation);
        Assert.Equal(record.RuleReport, restored.RuleReport);
        Assert.Equal(ids.OrderBy(id => id.Value, StringComparer.Ordinal), restored.EvidenceIds);
        Assert.Contains("Evidence ID storage-health-unexpected.", restored.RuleReport, StringComparison.Ordinal);
        Assert.Contains("Exception. Evidence ID storage-low.", restored.RuleReport, StringComparison.Ordinal);
        Assert.Contains("Authorization synthetic-owner-grant.", restored.RuleReport, StringComparison.Ordinal);
        Assert.DoesNotContain(rawObservationValue, json, StringComparison.Ordinal);
        Assert.DoesNotContain(privateReason, json, StringComparison.Ordinal);
        Assert.DoesNotContain("HasEvaluationProvenance", json, StringComparison.Ordinal);
        Assert.Equal(json, JsonSerializer.Serialize(restored, options));

        var unknownField = JsonNode.Parse(json)!.AsObject();
        unknownField["Unexpected"] = true;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PolicyDecisionRecord>(unknownField.ToJsonString(), options));
    }

    [Fact]
    public void StrictReplayPreservesCanonicalCapabilitiesAndDistinguishesUnknownFromEmpty()
    {
        var snapshot = PolicySnapshot.Parse(CandidatePolicyJson());
        var evidence = ReadyObservations();
        var ids = EvidenceIds(evidence);
        var options = DomainJson.CreateStrictOptions();

        var selected = Replay(["web_browsing", "video_calling", "document_editing", "video_calling"]);
        var explicitlyEmpty = Replay([]);
        var unknown = Replay(null);

        string[] expected = ["document_editing", "video_calling", "web_browsing"];
        Assert.True(selected.ActiveCapabilityIds.HasValue);
        Assert.Equal(expected, selected.ActiveCapabilityIds.GetValueOrDefault().AsEnumerable());
        Assert.True(explicitlyEmpty.ActiveCapabilityIds.HasValue);
        Assert.Empty(explicitlyEmpty.ActiveCapabilityIds.GetValueOrDefault());
        Assert.Null(unknown.ActiveCapabilityIds);
        Assert.Equal(Disposition.ReadyToPrepare, explicitlyEmpty.Disposition);
        Assert.Equal(Disposition.HumanReviewRequired, unknown.Disposition);

        PolicyDecisionRecord Replay(string[]? capabilityIds)
        {
            var evaluated = DispositionEngine.Evaluate(snapshot.Document, evidence, capabilityIds);
            var saved = Capture(snapshot, evaluated, ids, EvaluationTime);
            return JsonSerializer.Deserialize<PolicyDecisionRecord>(JsonSerializer.Serialize(saved, options), options)!;
        }
    }

    [Fact]
    public void DecisionCaptureRejectsMismatchedPolicyAndEvidenceReferences()
    {
        var snapshot = PolicySnapshot.Parse(CandidatePolicyJson());
        var changed = PolicySnapshot.Parse(snapshot.Utf8Json.Append((byte)' ').ToArray());
        var evidence = ReadyObservations();
        var ids = EvidenceIds(evidence);
        var decision = Evaluate(snapshot, evidence);
        var missing = ids.Where(id => id.Value != "SYNTHETIC-EVIDENCE-READY-002");

        Assert.ThrowsAny<ArgumentException>(() => Capture(changed, decision, ids, EvaluationTime));
        Assert.ThrowsAny<ArgumentException>(() => Capture(snapshot, decision, missing, EvaluationTime));
    }

    [Fact]
    public void DecisionCaptureBoundsEvidenceIdsBeforeDeduplication()
    {
        var snapshot = PolicySnapshot.Parse(CandidatePolicyJson());
        var evidence = ReadyObservations();
        var ids = EvidenceIds(evidence);
        var decision = Evaluate(snapshot, evidence);
        const int maxEvidenceIds = PolicyEvaluator.MaximumObservations;
        var atLimit = ids.Concat(Enumerable.Repeat(ids[0], maxEvidenceIds - ids.Length));

        Assert.Equal(ids.Length, Capture(snapshot, decision, atLimit, EvaluationTime).EvidenceIds.Length);
        Assert.Throws<ArgumentException>(() => Capture(snapshot, decision, atLimit.Append(ids[0]), EvaluationTime));
    }

    [Fact]
    public void DecisionCaptureRequiresValidIdentifiersAndMatchingJobAndTime()
    {
        var snapshot = PolicySnapshot.Parse(CandidatePolicyJson());
        var evidence = ReadyObservations();
        var ids = EvidenceIds(evidence);
        var history = new PolicyExceptionHistory(Job);
        var decision = DispositionEngine.Evaluate(snapshot.Document, evidence, ActiveCapabilities(), history, EvaluationTime);

        Assert.ThrowsAny<ArgumentException>(() => PolicyDecisionRecord.Create(
            snapshot, new JobId("synthetic-other-job"), AssessmentId, decision, ids, EvaluationTime));
        Assert.ThrowsAny<ArgumentException>(() => Capture(snapshot, decision, ids, EvaluationTime.AddMinutes(1)));
        Assert.ThrowsAny<ArgumentException>(() => Capture(snapshot, decision, ids, default));
        Assert.ThrowsAny<ArgumentException>(() => PolicyDecisionRecord.Create(
            snapshot, Job, "../invalid-assessment", decision, ids, EvaluationTime));
        Assert.ThrowsAny<ArgumentException>(() => Capture(snapshot, decision, ids, EvaluationTime, "../invalid-predecessor"));
    }

    private static EvidenceId[] EvidenceIds(IEnumerable<Observation> evidence) =>
        evidence.Select(observation => observation.Metadata.EvidenceId).ToArray();

    private static PolicyDecisionRecord Capture(
        PolicySnapshot snapshot, DispositionDecision decision, IEnumerable<EvidenceId> evidenceIds,
        DateTimeOffset evaluatedAtUtc, string? previousDecisionId = null) =>
        PolicyDecisionRecord.Create(snapshot, Job, AssessmentId, decision, evidenceIds, evaluatedAtUtc, previousDecisionId);

    private static DispositionDecision Evaluate(PolicySnapshot snapshot, IEnumerable<Observation> evidence) =>
        DispositionEngine.Evaluate(snapshot.Document, evidence, ActiveCapabilities());

    private static byte[] NewPolicyBytes()
    {
        var document = JsonNode.Parse(CandidatePolicyJson())!.AsObject();
        document["version"] = "0.1.0-candidate.2";
        FindStorageRule(document)["condition"]!["value"] = 206_158_430_208L;
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    private static JsonNode FindStorageRule(JsonObject document) =>
        Assert.Single(document["rules"]!.AsArray(), rule => rule!["id"]!.GetValue<string>() == "POL-STORAGE-002")!;
}
