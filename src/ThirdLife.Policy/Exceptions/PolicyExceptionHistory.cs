using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Exceptions;

public sealed class PolicyExceptionHistory
{
    public const int MaximumRecords = 256;
    public const int MaximumReasonLength = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public PolicyExceptionHistory(JobId jobId)
        : this(jobId ?? throw new ArgumentNullException(nameof(jobId)), [])
    {
    }

    private PolicyExceptionHistory(JobId jobId, ImmutableArray<PolicyExceptionRecord> records)
    {
        JobId = jobId;
        Records = records;
    }

    public JobId JobId { get; }

    public ImmutableArray<PolicyExceptionRecord> Records { get; }

    public PolicyExceptionHistory Record(
        DispositionDecision decision,
        RequirementId ruleId,
        OperatorId operatorId,
        string reason,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(operatorId);
        var normalizedReason = RequireReason(reason);
        if (!ReferenceEquals(decision.ExceptionHistory, this) ||
            decision.JobId != JobId || decision.EvaluatedAtUtc is not { } evaluatedAt)
        {
            throw new InvalidOperationException("Evaluate this job against the current exception history before recording an exception.");
        }

        var recorded = recordedAtUtc.ToUniversalTime();
        if (recorded == default || recorded < evaluatedAt ||
            (Records.Length > 0 && recorded < Records[^1].RecordedAtUtc))
        {
            throw new ArgumentException("The exception timestamp precedes the applicable evaluation or history.", nameof(recordedAtUtc));
        }

        if (Records.Length >= MaximumRecords)
        {
            throw new InvalidOperationException("The exception history has reached its record limit.");
        }

        var evaluated = decision.Evaluation.Outcomes.SingleOrDefault(outcome => outcome.Rule.Requirement.RequirementId == ruleId);
        if (evaluated is null || !IsEligible(evaluated))
        {
            throw new InvalidOperationException("The selected rule and finding do not permit an exception.");
        }

        var policy = decision.Evaluation.Policy;
        var previous = FindApplicable(policy, evaluated, recorded);
        var item = new PolicyExceptionRecord(
            string.Concat("exception-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)),
            JobId,
            ruleId,
            policy.PolicyId,
            policy.Version,
            policy.ContentSha256,
            evaluated.Evidence[0].EvidenceId,
            evaluated.Rule.ExceptionAuthorizationId!,
            operatorId,
            normalizedReason,
            recorded,
            previous?.ExceptionId);
        return new PolicyExceptionHistory(JobId, Records.Add(item));
    }

    internal PolicyExceptionRecord? FindApplicable(
        PolicyDocument policy, PolicyRuleOutcome evaluated, DateTimeOffset evaluatedAtUtc)
    {
        if (!IsEligible(evaluated))
        {
            return null;
        }

        for (var index = Records.Length - 1; index >= 0; index--)
        {
            var record = Records[index];
            if (record.RecordedAtUtc <= evaluatedAtUtc &&
                record.RequirementId == evaluated.Rule.Requirement.RequirementId &&
                record.EvidenceId == evaluated.Evidence[0].EvidenceId &&
                record.PolicySha256 == policy.ContentSha256 &&
                record.AuthorizationId == evaluated.Rule.ExceptionAuthorizationId)
            {
                return record;
            }
        }

        return null;
    }

    private static bool IsEligible(PolicyRuleOutcome evaluated)
    {
        var rule = evaluated.Rule;
        return evaluated.State == PolicyRuleState.Unmet &&
            evaluated.Evidence.Length == 1 &&
            rule.ExceptionAuthorizationId is not null &&
            rule.Decision.BlocksReady &&
            rule.Decision.UnmetDisposition != Disposition.DoNotDeploy &&
            rule.Requirement.Severity != RequirementSeverity.Critical &&
            rule.Requirement.RequirementType != RequirementType.HumanConfirmed &&
            !IsProtectedFact(rule.Requirement.EvidenceKey.Value);
    }

    private static bool IsProtectedFact(string key) =>
        key is "system.architecture" or "storage.system.health_state" or "battery.swelling_inspection" ||
        key.StartsWith("sanitization.", StringComparison.Ordinal) ||
        key.StartsWith("operating_system.", StringComparison.Ordinal) ||
        key.StartsWith("os.", StringComparison.Ordinal) ||
        key.StartsWith("processor.", StringComparison.Ordinal) ||
        key.StartsWith("firmware.", StringComparison.Ordinal) ||
        key.StartsWith("secure_boot.", StringComparison.Ordinal) ||
        key.StartsWith("tpm.", StringComparison.Ordinal) ||
        key.StartsWith("ownership.", StringComparison.Ordinal) ||
        key.StartsWith("security.", StringComparison.Ordinal) ||
        key.StartsWith("reboot.", StringComparison.Ordinal) ||
        key.StartsWith("activation.", StringComparison.Ordinal);

    private static string RequireReason(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (reason.Length > MaximumReasonLength || string.IsNullOrWhiteSpace(reason) ||
            reason.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format))
        {
            throw new ArgumentException("Enter a nonempty exception reason within the allowed length and character limits.", nameof(reason));
        }

        try
        {
            _ = StrictUtf8.GetByteCount(reason);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Enter a well-formed exception reason.", nameof(reason));
        }

        return reason.Trim();
    }
}
