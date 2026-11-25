using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;
using static ThirdLife.Policy.Tests.PolicyEvaluationFixture;

namespace ThirdLife.Policy.Tests;

public sealed class ExceptionTests
{
    private const string StorageRuleId = "POL-STORAGE-002";
    private const string AuthorizationId = "synthetic-owner-grant";
    private const string FirstReason = "Synthetic workshop retained a bounded storage plan.";
    private const string UpdatedReason = "Synthetic workshop reviewed the revised storage plan.";
    private static readonly RequirementId StorageRule = new(StorageRuleId);
    private static readonly JobId Job = new("synthetic-exception-job");
    private static readonly OperatorId Operator = new("synthetic-refurbisher");
    private static readonly DateTimeOffset AssessmentTime = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedTime = AssessmentTime.AddMinutes(5);
    private static readonly DateTimeOffset ReplacementTime = AssessmentTime.AddMinutes(10);

    [Fact]
    public void PermittedExceptionRemainsDistinctFromAPassAndRetainsAttribution()
    {
        var bytes = AuthorizedPolicyJson();
        var policy = PolicySchema.Parse(bytes);
        var history = new PolicyExceptionHistory(Job);
        var evidence = LowStorageEvidence();
        var before = Evaluate(policy, evidence, history, AssessmentTime);
        var recorded = history.Record(before, StorageRule, Operator, FirstReason, RecordedTime);

        var after = Evaluate(policy, evidence, recorded, RecordedTime);

        Assert.Empty(history.Records);
        Assert.Equal(Job, before.JobId);
        Assert.Equal(AssessmentTime, before.EvaluatedAtUtc);
        Assert.Equal(Disposition.RepairAndRetest, before.Disposition);
        Assert.Equal(Disposition.ReadyToPrepare, after.Disposition);
        var original = FindRule(before, StorageRuleId);
        var excepted = FindRule(after, StorageRuleId);
        Assert.Equal(DispositionRuleStatus.Fail, original.Status);
        Assert.Equal(DispositionRuleStatus.Exception, excepted.Status);
        Assert.Equal("Exception", excepted.StatusLabel);
        Assert.Equal(PolicyRuleState.Unmet, excepted.Outcome.State);
        Assert.Equal(PolicyRuleReason.ConditionUnmet, excepted.Outcome.Reason);
        Assert.Equal(original.Outcome.Evidence.AsEnumerable(), excepted.Outcome.Evidence.AsEnumerable());
        Assert.Equal(DispositionSource.OrganizationPolicy, excepted.Source);
        var record = Assert.Single(recorded.Records);
        Assert.Same(record, excepted.Exception);
        Assert.False(string.IsNullOrWhiteSpace(record.ExceptionId));
        Assert.Equal(Job, record.JobId);
        Assert.Equal(StorageRule, record.RequirementId);
        Assert.Equal(Operator, record.OperatorId);
        Assert.Equal(FirstReason, record.Reason);
        Assert.Equal(RecordedTime, record.RecordedAtUtc);
        Assert.Equal(AuthorizationId, record.AuthorizationId);
        Assert.Equal(policy.PolicyId, record.PolicyId);
        Assert.Equal(policy.Version, record.PolicyVersion);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), record.PolicySha256);
        Assert.Equal("storage-evidence-low", record.EvidenceId.Value);
        Assert.Null(record.SupersedesExceptionId);
    }

    [Fact]
    public void ReplacementAppendsAndEarlierReplaysUseTheEarlierException()
    {
        var policy = PolicySchema.Parse(AuthorizedPolicyJson());
        var evidence = LowStorageEvidence();
        var originalHistory = new PolicyExceptionHistory(Job);
        var first = originalHistory.Record(
            Evaluate(policy, evidence, originalHistory, AssessmentTime), StorageRule, Operator, FirstReason, RecordedTime);
        var replacementOperator = new OperatorId("synthetic-reviewing-operator");
        var second = first.Record(
            Evaluate(policy, evidence, first, RecordedTime), StorageRule, replacementOperator, UpdatedReason, ReplacementTime);

        var initialRecord = Assert.Single(first.Records);
        Assert.Equal(2, second.Records.Length);
        Assert.Same(initialRecord, second.Records[0]);
        var replaced = second.Records[1];
        Assert.Equal(initialRecord.ExceptionId, replaced.SupersedesExceptionId);
        Assert.NotEqual(initialRecord.ExceptionId, replaced.ExceptionId);
        Assert.Equal(replacementOperator, replaced.OperatorId);
        Assert.Equal(UpdatedReason, replaced.Reason);
        Assert.Equal(ReplacementTime, replaced.RecordedAtUtc);
        Assert.Equal(DispositionRuleStatus.Fail,
            FindRule(Evaluate(policy, evidence, second, AssessmentTime), StorageRuleId).Status);
        Assert.Same(initialRecord, FindRule(Evaluate(policy, evidence, second, RecordedTime), StorageRuleId).Exception);
        Assert.Same(replaced, FindRule(Evaluate(policy, evidence, second, ReplacementTime), StorageRuleId).Exception);
    }

    [Fact]
    public void StoredExceptionOnlyAppliesToExactPolicyAndEvidence()
    {
        var bytes = AuthorizedPolicyJson();
        var oldPolicy = PolicySchema.Parse(bytes);
        var oldEvidence = LowStorageEvidence();
        var initial = new PolicyExceptionHistory(Job);
        var recorded = initial.Record(
            Evaluate(oldPolicy, oldEvidence, initial, AssessmentTime), StorageRule, Operator, FirstReason, RecordedTime);
        var newVersion = PolicySchema.Parse(AuthorizedPolicyJson(version: "0.1.0-candidate.3"));
        var changedBytes = PolicySchema.Parse(bytes.Append((byte)' ').ToArray());
        var newEvidence = LowStorageEvidence("storage-evidence-retest");

        Assert.Equal(oldPolicy.Version, changedBytes.Version);
        Assert.NotEqual(oldPolicy.ContentSha256, changedBytes.ContentSha256);
        Assert.Equal(Disposition.RepairAndRetest, Evaluate(newVersion, oldEvidence, recorded, RecordedTime).Disposition);
        Assert.Equal(Disposition.RepairAndRetest, Evaluate(changedBytes, oldEvidence, recorded, RecordedTime).Disposition);
        Assert.Equal(Disposition.RepairAndRetest, Evaluate(oldPolicy, newEvidence, recorded, RecordedTime).Disposition);
        var historicalReplay = Evaluate(oldPolicy, oldEvidence, recorded, RecordedTime);
        Assert.Equal(Disposition.ReadyToPrepare, historicalReplay.Disposition);
        var original = FindRule(historicalReplay, StorageRuleId).Exception;
        Assert.NotNull(original);
        Assert.Equal(AuthorizationId, original.AuthorizationId);
        Assert.Equal(oldPolicy.ContentSha256, original.PolicySha256);
    }

    [Fact]
    public void ReopenedOrUnscopedDecisionsRequireEvaluationAgainstTheCurrentHistory()
    {
        var policy = PolicySchema.Parse(AuthorizedPolicyJson());
        var evidence = LowStorageEvidence();
        var history = new PolicyExceptionHistory(Job);
        var decision = Evaluate(policy, evidence, history, AssessmentTime);
        var otherJob = new PolicyExceptionHistory(new JobId("synthetic-other-job"));
        var reopened = new PolicyExceptionHistory(Job);
        var unscoped = DispositionEngine.Evaluate(policy, evidence, ActiveCapabilities());
        var appended = history.Record(decision, StorageRule, Operator, FirstReason, RecordedTime);

        Assert.Throws<InvalidOperationException>(() => otherJob.Record(decision, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Throws<InvalidOperationException>(() => reopened.Record(decision, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Throws<InvalidOperationException>(() => history.Record(unscoped, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Throws<InvalidOperationException>(() => appended.Record(decision, StorageRule, Operator, UpdatedReason, ReplacementTime));
    }

    [Fact]
    public void APolicyWithoutAnExactGrantCannotRecordAnException()
    {
        var history = new PolicyExceptionHistory(Job);
        var decision = Evaluate(CandidatePolicy(), LowStorageEvidence(), history, AssessmentTime);

        Assert.Throws<InvalidOperationException>(() => history.Record(decision, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Empty(history.Records);
    }

    [Theory]
    [InlineData("POL-OS-001", "operating_system.release", "windows_10", EvidenceClassification.Observed)]
    [InlineData("POL-STORAGE-003", "storage.system.health_state", "degraded", EvidenceClassification.Observed)]
    [InlineData("POL-OWNERSHIP-001", "ownership.lock_state", "locked", EvidenceClassification.Observed)]
    [InlineData("POL-FUNCTION-002", "function.keyboard", "failed", EvidenceClassification.HumanConfirmed)]
    public void AnExplicitGrantCannotWaiveAProhibitedRule(
        string ruleId, string key, string unacceptableValue, EvidenceClassification classification)
    {
        var policy = PolicySchema.Parse(AuthorizedPolicyJson(ruleId));
        var history = new PolicyExceptionHistory(Job);
        var evidence = WithFacts(Fact("protected-rule-finding", key, EvidenceValue.FromEnum(unacceptableValue), classification));
        var decision = Evaluate(policy, evidence, history, AssessmentTime);

        var detail = FindRule(decision, ruleId);
        Assert.Equal(AuthorizationId, detail.Outcome.Rule.ExceptionAuthorizationId);
        Assert.Equal(DispositionRuleStatus.Fail, detail.Status);
        Assert.Throws<InvalidOperationException>(() =>
            history.Record(decision, new RequirementId(ruleId), Operator, FirstReason, RecordedTime));
        Assert.Empty(history.Records);
    }

    [Fact]
    public void CoreTargetChecksHaveNoExceptionPath()
    {
        var policy = PolicySchema.Parse(AuthorizedPolicyJson("POL-OS-001"));
        var history = new PolicyExceptionHistory(Job);
        var evidence = WithFacts(Fact("unsupported-release", "operating_system.release", EvidenceValue.FromEnum("windows_10")));
        var decision = Evaluate(policy, evidence, history, AssessmentTime);

        Assert.Equal(Disposition.DoNotDeploy, decision.Disposition);
        Assert.Equal(DispositionRuleStatus.Fail, FindRule(decision, "CORE-OS-RELEASE").Status);
        Assert.Throws<InvalidOperationException>(() =>
            history.Record(decision, new RequirementId("CORE-OS-RELEASE"), Operator, FirstReason, RecordedTime));
        Assert.Empty(history.Records);
    }

    [Fact]
    public void GrantCannotWaivePassedMissingOrConflictingEvidence()
    {
        var policy = PolicySchema.Parse(AuthorizedPolicyJson());
        var history = new PolicyExceptionHistory(Job);
        var passed = Evaluate(policy, ReadyObservations(), history, AssessmentTime);
        var missing = ReadyObservations().Where(item => item.EvidenceKey.Value != "storage.system.free_bytes");
        var unavailable = Evaluate(policy, missing, history, AssessmentTime);
        var conflicting = Evaluate(policy, ReadyObservations().Append(LowStorageFact()), history, AssessmentTime);

        Assert.Throws<InvalidOperationException>(() => history.Record(passed, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Throws<InvalidOperationException>(() => history.Record(unavailable, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Throws<InvalidOperationException>(() => history.Record(conflicting, StorageRule, Operator, FirstReason, RecordedTime));
        Assert.Empty(history.Records);
    }

    [Fact]
    public void ReportDetailsDistinguishPassFailureExceptionAndPendingConfirmation()
    {
        const string rawHealthValue = "synthetic-raw-health-state";
        var policy = PolicySchema.Parse(AuthorizedPolicyJson());
        var history = new PolicyExceptionHistory(Job);
        var evidence = WithFacts(
                LowStorageFact(),
                Fact("battery-failure", "battery.present", EvidenceValue.FromBoolean(false)),
                Fact("storage-health-finding", "storage.system.health_state", EvidenceValue.FromEnum(rawHealthValue)))
            .Where(observation => observation.EvidenceKey.Value != "function.keyboard").ToArray();
        var recorded = history.Record(
            Evaluate(policy, evidence, history, AssessmentTime), StorageRule, Operator, FirstReason, RecordedTime);

        var decision = Evaluate(policy, evidence, recorded, RecordedTime);

        AssertRuleStatus(decision, "POL-OS-001", DispositionRuleStatus.Pass, "Pass");
        AssertRuleStatus(decision, "POL-BATTERY-001", DispositionRuleStatus.Fail, "Fail");
        AssertRuleStatus(decision, StorageRuleId, DispositionRuleStatus.Exception, "Exception");
        AssertRuleStatus(decision, "POL-FUNCTION-002", DispositionRuleStatus.PendingConfirmation, "Pending confirmation");
        Assert.Equal(Disposition.HumanReviewRequired, decision.Disposition);

        var record = Assert.Single(recorded.Records);
        var report = PolicyRuleReport.Render(decision);
        Assert.StartsWith("Policy rule details\n", report, StringComparison.Ordinal);
        Assert.Contains("- Policy rule POL-OS-001. Pass. Evidence ID SYNTHETIC-EVIDENCE-READY-001.", report, StringComparison.Ordinal);
        Assert.Contains("- Policy rule POL-BATTERY-001. Fail. Evidence ID battery-failure.", report, StringComparison.Ordinal);
        Assert.Contains("- Policy rule POL-FUNCTION-002. Pending confirmation. No saved evidence is linked to this check.",
            report, StringComparison.Ordinal);
        Assert.Contains(
            "- Policy rule POL-STORAGE-002. Exception. Evidence ID storage-evidence-low.\n" +
            $"  Exception record {record.ExceptionId}. Authorization synthetic-owner-grant. " +
            "Operator code synthetic-refurbisher. Recorded 2030-01-01T09:05:00.0000000Z.",
            report, StringComparison.Ordinal);
        Assert.Contains("- Policy rule POL-STORAGE-003. Fail. Evidence ID storage-health-finding.", report, StringComparison.Ordinal);
        Assert.DoesNotContain(FirstReason, report, StringComparison.Ordinal);
        Assert.DoesNotContain(rawHealthValue, report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("synthetic-sensitive-reason\r\nforged-line")]
    [InlineData("synthetic-sensitive-reason\u202E")]
    public void InvalidReasonsAreRejectedWithoutEchoingOperatorInput(string reason)
    {
        var history = new PolicyExceptionHistory(Job);
        var decision = Evaluate(PolicySchema.Parse(AuthorizedPolicyJson()), LowStorageEvidence(), history, AssessmentTime);

        var error = Assert.ThrowsAny<ArgumentException>(() => history.Record(decision, StorageRule, Operator, reason, RecordedTime));

        Assert.DoesNotContain("synthetic-sensitive-reason", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(history.Records);
    }

    [Fact]
    public void RequiresOperatorReasonAndChronologicallyValidDates()
    {
        var history = new PolicyExceptionHistory(Job);
        var decision = Evaluate(PolicySchema.Parse(AuthorizedPolicyJson()), LowStorageEvidence(), history, AssessmentTime);
        var oversized = new string('x', 5_000);

        Assert.Throws<ArgumentNullException>(() => history.Record(decision, StorageRule, null!, FirstReason, RecordedTime));
        Assert.Throws<ArgumentNullException>(() => history.Record(decision, StorageRule, Operator, null!, RecordedTime));
        Assert.ThrowsAny<ArgumentException>(() => history.Record(decision, StorageRule, Operator, oversized, RecordedTime));
        Assert.ThrowsAny<ArgumentException>(() => history.Record(decision, StorageRule, Operator, FirstReason, default));
        Assert.ThrowsAny<ArgumentException>(() => history.Record(decision, StorageRule, Operator, FirstReason, AssessmentTime.AddSeconds(-1)));
        Assert.Empty(history.Records);
    }

    [Fact]
    public void AcceptedReasonsRemainOutOfDefaultStringsAndSerialization()
    {
        const string privateReason = "Synthetic restricted note should only be read deliberately.";
        var history = new PolicyExceptionHistory(Job);
        var policy = PolicySchema.Parse(AuthorizedPolicyJson());
        var evidence = LowStorageEvidence();
        var recorded = history.Record(
            Evaluate(policy, evidence, history, AssessmentTime), StorageRule, Operator, privateReason, RecordedTime);
        var record = Assert.Single(recorded.Records);
        var decision = Evaluate(policy, evidence, recorded, RecordedTime);

        Assert.Equal(privateReason, record.Reason);
        Assert.DoesNotContain(privateReason, record.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(privateReason, JsonSerializer.Serialize(record), StringComparison.Ordinal);
        Assert.DoesNotContain(privateReason, JsonSerializer.Serialize(decision), StringComparison.Ordinal);
        Assert.DoesNotContain(privateReason, decision.Explanation, StringComparison.Ordinal);
    }

    private static byte[] AuthorizedPolicyJson(string ruleId = StorageRuleId, string? version = null)
    {
        var document = JsonNode.Parse(CandidatePolicyJson())!.AsObject();
        var rule = Assert.Single(document["rules"]!.AsArray(), rule =>
            rule!["id"]!.GetValue<string>() == ruleId);
        rule!["exception_authorization_id"] = AuthorizationId;
        if (version is not null)
        {
            document["version"] = version;
        }

        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    private static DispositionDecision Evaluate(
        PolicyDocument policy, IEnumerable<Observation> evidence, PolicyExceptionHistory history, DateTimeOffset time) =>
        DispositionEngine.Evaluate(policy, evidence, ActiveCapabilities(), history, time);

    private static DispositionRuleDetail FindRule(DispositionDecision decision, string id) =>
        Assert.Single(decision.RuleDetails, detail => detail.Outcome.Rule.Requirement.RequirementId.Value == id);

    private static void AssertRuleStatus(
        DispositionDecision decision, string ruleId, DispositionRuleStatus status, string label)
    {
        var detail = FindRule(decision, ruleId);
        Assert.Equal(status, detail.Status);
        Assert.Equal(label, detail.StatusLabel);
    }

    private static Observation[] LowStorageEvidence(string id = "storage-evidence-low") => WithFacts(LowStorageFact(id));

    private static Observation LowStorageFact(string id = "storage-evidence-low") =>
        Fact(id, "storage.system.free_bytes", EvidenceValue.FromInteger(10_737_418_240), unit: "bytes");

    private static Observation[] WithFacts(params Observation[] replacements)
    {
        var byKey = replacements.ToDictionary(observation => observation.EvidenceKey);
        return ReadyObservations().Select(observation => byKey.GetValueOrDefault(observation.EvidenceKey, observation)).ToArray();
    }
}
