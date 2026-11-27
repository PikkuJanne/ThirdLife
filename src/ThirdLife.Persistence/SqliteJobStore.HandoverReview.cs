using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.Verification;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IHandoverCompletionStore
{
    public Task<HandoverEligibility?> LoadHandoverReviewAsync(JobId jobId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync((transaction, ct) => ReadHandoverReviewAsync(transaction, jobId, ct), token);
    }

    public Task<HandoverEligibility?> LoadHandoverReviewAsync(JobId jobId, IHandoverFileVerifier verifier,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(verifier);
        return ReadJournalTransactionAsync((transaction, ct) => ReadHandoverReviewAsync(transaction, jobId, ct, verifier), token);
    }

    private async Task<HandoverEligibility?> ReadHandoverReviewAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token, IHandoverFileVerifier? verifier = null)
    {
        if (!_supportsFinalizationArtifacts ||
            await ReadStoredJobAsync(_connection, transaction, jobId, token).ConfigureAwait(false) is not { } job)
        {
            return null;
        }

        var gate = SanitizationGate.Inspect(job);
        var finalization = await ReadFinalizationAsync(transaction, jobId, token).ConfigureAwait(false);
        var recipient = await ReadRecipientHandoverAsync(transaction, jobId, token).ConfigureAwait(false);
        var synthetic = job.SanitizationEvidence.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ||
            job.Observations.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ||
            job.HumanTests.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture);
        var policy = job.IsArchived ? null : await ReadCurrentHandoverPolicyAsync(transaction, jobId, token).ConfigureAwait(false);
        var revision = SerializePayload(new
        {
            Job = jobId.Value,
            Finalization = finalization?.EvidenceVersion,
            Confirmations = finalization?.History.Count,
            Scans = finalization?.ArtifactScans.Count,
            Recipient = recipient?.Sequence,
        }).Sha256;
        var scope = new HandoverReviewScope(jobId, revision,
            synthetic ? HandoverEvidenceOrigin.SyntheticFixture : HandoverEvidenceOrigin.ActiveDevice);
        var evidence = new List<HandoverRequirementEvidence>();
        if (gate is { AllowsAssessment: true, Decision: { } gateDecision } &&
            job.SanitizationEvidence[^1].Metadata.Provenance.Kind == ProvenanceKind.HumanConfirmation)
        {
            evidence.Add(new(HandoverRequirement.Sanitization, HandoverEvidenceState.Verified, scope, gateDecision.DecisionId.Value));
        }

        if (policy is { Decision: { } decision })
        {
            evidence.Add(new(HandoverRequirement.PolicyDisposition, HandoverEvidenceState.Verified, scope, decision.DecisionId));
            if (decision.Disposition == Disposition.ReadyToPrepare && gate.AllowsAssessment && !synthetic)
            {
                evidence.Add(new(HandoverRequirement.AuthorizedExceptions, HandoverEvidenceState.Verified, scope, decision.DecisionId));
            }

            if (await ReadProfileVerificationAsync(transaction, jobId, token).ConfigureAwait(false) is { } verification &&
                verification.Record.Result.PolicySha256 == decision.PolicySha256 && !synthetic)
            {
                var plan = await RequireJournalPlanAsync(_connection, transaction, verification.Record.Checkpoint.Plan, token).ConfigureAwait(false);
                var isCurrent = plan.PolicySha256 == decision.PolicySha256 &&
                    await IsCurrentProfileVerificationAsync(transaction, verification.Record, token).ConfigureAwait(false);
                var saved = new SavedProfileVerification(verification.Record, isCurrent);
                AddHandoverVerificationEvidence(scope, evidence, saved);
                var ownershipRules = policy.Snapshot.Document.Rules.Where(rule =>
                    rule.Requirement.EvidenceKey.Value == "ownership.lock_state" && rule.Decision.BlocksReady).ToArray();
                var ownershipState = ownershipRules.Length == 0 ? HandoverEvidenceState.Unavailable :
                    ownershipRules.All(rule => verification.Record.Result.PolicyFindings.Any(finding =>
                        finding.RuleId == rule.Requirement.RequirementId && finding.State == PolicyRuleState.Met))
                    ? HandoverEvidenceState.Verified : HandoverEvidenceState.Failed;
                evidence.Add(new(HandoverRequirement.Ownership,
                    isCurrent ? ownershipState : HandoverEvidenceState.Stale, scope, decision.DecisionId));
            }
        }

        if (finalization?.EvidenceVersion is { } version)
        {
            evidence.Add(new(HandoverRequirement.Finalization, HandoverEvidenceState.Verified, scope, version));
        }

        if (recipient is { Handover: not null, Sequence: > 0 })
        {
            evidence.Add(new(HandoverRequirement.RecipientHandoverMode, HandoverEvidenceState.Verified, scope, revision));
        }

        if (_supportsHandoverCompletion && verifier is not null && !job.IsArchived)
        {
            await AddPublicationEvidenceAsync(transaction, scope, evidence, verifier, token).ConfigureAwait(false);
        }

        return HandoverEligibility.Evaluate(scope, job.IsArchived, policy?.Decision.Disposition, finalization, evidence);
    }

    private async Task<StoredPolicyDecision?> ReadCurrentHandoverPolicyAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = PolicyDecisionProjection + "\nWHERE d.job_id = $job ORDER BY d.decision_sequence DESC LIMIT 1;";
        command.Parameters.AddWithValue("$job", jobId.Value);
        if (await ReadPolicyDecisionRowAsync(command, token).ConfigureAwait(false) is not { } policy ||
            policy.Snapshot.Document is not { IsCurrentSchema: true, SyntheticData: false, Status: PolicyStatus.Effective } ||
            policy.Decision.JobEvidenceCount != await ReadEvidenceCountAsync(_connection, transaction, jobId, token).ConfigureAwait(false))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (policy.Decision.EvaluatedAtUtc > now || policy.Snapshot.Document.Effective is not { FromUtc: { } start } window ||
            start > policy.Decision.EvaluatedAtUtc || window.UntilUtc <= now)
        {
            return null;
        }

        await RequirePolicyDecisionEvidenceAsync(_connection, transaction, policy.Decision, token, reading: true).ConfigureAwait(false);
        return policy;
    }

    private static void AddHandoverVerificationEvidence(HandoverReviewScope scope, List<HandoverRequirementEvidence> evidence,
        SavedProfileVerification verification)
    {
        var record = verification.Record;
        var result = record.Result;
        var version = record.ScanId.ToString("N");
        HandoverEvidenceState FromCheck(ProfileCheckKind kind) => result.Checks.Single(check => check.Kind == kind).State switch
        {
            ProfileVerificationState.Passed => HandoverEvidenceState.Verified,
            ProfileVerificationState.Failed => HandoverEvidenceState.Failed,
            ProfileVerificationState.Stale => HandoverEvidenceState.Stale,
            _ => HandoverEvidenceState.Unavailable,
        };
        void Add(HandoverRequirement requirement, HandoverEvidenceState state) => evidence.Add(new(requirement,
            verification.IsCurrent ? state : HandoverEvidenceState.Stale, scope, version));

        Add(HandoverRequirement.SupportedPlatform, FromCheck(ProfileCheckKind.OperatingSystemSupport));
        Add(HandoverRequirement.PendingRestart, result.UpdateRecoveryPending
            ? HandoverEvidenceState.Failed : FromCheck(ProfileCheckKind.PendingRestart));
        Add(HandoverRequirement.EssentialActionVerification, result.Capabilities.All(capability => !capability.Essential ||
            capability.State == ProfileVerificationState.Passed && capability.LaunchProbe is ProfileVerificationState.Passed)
            ? HandoverEvidenceState.Verified : HandoverEvidenceState.Failed);
        Add(HandoverRequirement.FinalProfileVerification, result.ProfileChecksPassed
            ? HandoverEvidenceState.Verified : HandoverEvidenceState.Failed);
        Add(HandoverRequirement.ColdBootVerification, verification.HasPassedPostBootVerification
            ? HandoverEvidenceState.Verified : HandoverEvidenceState.Unavailable);
    }
}
