using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;
using static ThirdLife.Policy.Tests.PolicyEvaluationFixture;

namespace ThirdLife.Policy.Tests;

public sealed class SecurityBaselinePolicyTests
{
    private static byte[] CandidateBytes => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy-candidate-3.json"));

    [Fact]
    public void ExpandedCandidatePreservesTheEarlierRulesAndRequiresAllEightAdditionalChecks()
    {
        var earlier = JsonNode.Parse(CandidatePolicyJson())!;
        var current = JsonNode.Parse(CandidateBytes)!;
        var priorRules = earlier["rules"]!.AsArray();
        var rules = current["rules"]!.AsArray();
        Assert.Equal("0.1.0-candidate.3", current["version"]!.GetValue<string>());
        Assert.Equal(priorRules.Count + 8, rules.Count);
        for (var i = 0; i < priorRules.Count; i++)
        {
            Assert.True(JsonNode.DeepEquals(priorRules[i], rules[i]));
        }

        var policy = PolicySchema.Parse(CandidateBytes);
        Assert.Equal(PolicyStatus.Candidate, policy.Status);
        Assert.Null(policy.Effective.FromUtc);
        var missing = DispositionEngine.Evaluate(policy, ReadyObservations(), ActiveCapabilities());
        Assert.Equal(Disposition.HumanReviewRequired, missing.Disposition);
        var passing = DispositionEngine.Evaluate(policy, BaselineObservations(), ActiveCapabilities());
        Assert.Equal(Disposition.ReadyToPrepare, passing.Disposition);
        Assert.DoesNotContain(passing.Evaluation.Outcomes, outcome => outcome.Rule.ExceptionAuthorizationId is not null);
    }

    [Theory]
    [InlineData("security.firewall_health", "snoozed", DispositionRuleStatus.Fail)]
    [InlineData("security.antivirus_health", "poor", DispositionRuleStatus.Fail)]
    [InlineData("security.antivirus_health", "not_monitored", DispositionRuleStatus.Unavailable)]
    [InlineData("security.defender_health", "requires_review", DispositionRuleStatus.Fail)]
    [InlineData("security.uac_state", "requires_review", DispositionRuleStatus.Fail)]
    [InlineData("security.uac_state", "unrecognized", DispositionRuleStatus.Unavailable)]
    [InlineData("security.time_sync_recency", "older_than_seven_days", DispositionRuleStatus.Fail)]
    [InlineData("reboot.pending_status", "pending_indicated", DispositionRuleStatus.Fail)]
    [InlineData("security.administrator_review", "passed", DispositionRuleStatus.PendingConfirmation)]
    [InlineData("security.remote_access_review", "passed", DispositionRuleStatus.PendingConfirmation)]
    public void EachRequiredCheckBlocksAndRejectsAnExceptionEvenWithAGrant(string key, string token, DispositionRuleStatus expected)
    {
        var bytes = JsonNode.Parse(CandidateBytes)!;
        var rule = bytes["rules"]!.AsArray().Single(node => node!["evidence_key"]!.GetValue<string>() == key)!;
        rule["exception_authorization_id"] = "test-security-exception";
        var policy = PolicySchema.Parse(Encoding.UTF8.GetBytes(bytes.ToJsonString()));
        var history = new PolicyExceptionHistory(new JobId("synthetic-security-policy"));
        var clock = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var observed = BaselineObservations().Where(item => item.EvidenceKey.Value != key)
            .Append(Fact("synthetic-security-attempt", key, EvidenceValue.FromEnum(token)));
        var decision = DispositionEngine.Evaluate(policy, observed, ActiveCapabilities(), history, clock);

        var detail = Assert.Single(decision.RuleDetails, detail => detail.Outcome.Rule.Requirement.EvidenceKey.Value == key);
        Assert.Equal(expected, detail.Status);
        Assert.NotEqual(Disposition.ReadyToPrepare, decision.Disposition);
        Assert.Throws<InvalidOperationException>(() => history.Record(decision, detail.Outcome.Rule.Requirement.RequirementId,
            new OperatorId("fixture-operator"), "Synthetic exception attempt", clock));
    }

    private static Observation[] BaselineObservations() =>
    [
        .. ReadyObservations(),
        Fact("baseline-firewall", "security.firewall_health", EvidenceValue.FromEnum("good")),
        Fact("baseline-antivirus", "security.antivirus_health", EvidenceValue.FromEnum("good")),
        Fact("baseline-defender", "security.defender_health", EvidenceValue.FromEnum("real_time_active_signatures_current")),
        Fact("baseline-uac", "security.uac_state", EvidenceValue.FromEnum("enabled"), EvidenceClassification.Inferred),
        Fact("baseline-time", "security.time_sync_recency", EvidenceValue.FromEnum("within_seven_days"), EvidenceClassification.Inferred),
        Fact("baseline-restart", "reboot.pending_status", EvidenceValue.FromEnum("clear")),
        Fact("baseline-admin", "security.administrator_review", EvidenceValue.FromEnum("passed"), EvidenceClassification.HumanConfirmed),
        Fact("baseline-remote", "security.remote_access_review", EvidenceValue.FromEnum("passed"), EvidenceClassification.HumanConfirmed),
    ];
}
