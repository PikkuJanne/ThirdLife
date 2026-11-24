using ThirdLife.Core.Jobs;

namespace ThirdLife.Policy.Versioning;

public sealed record StoredPolicyDecision(PolicySnapshot Snapshot, PolicyDecisionRecord Decision);

public interface IPolicyHistoryStore
{
    Task AppendPolicyDecisionAsync(
        PolicySnapshot snapshot, PolicyDecisionRecord decision, CancellationToken cancellationToken = default);

    Task<StoredPolicyDecision?> LoadPolicyDecisionAsync(
        JobId jobId, string decisionId, CancellationToken cancellationToken = default);

    Task<StoredPolicyDecision?> LoadLatestPolicyDecisionAsync(
        JobId jobId, string assessmentId, CancellationToken cancellationToken = default);
}
