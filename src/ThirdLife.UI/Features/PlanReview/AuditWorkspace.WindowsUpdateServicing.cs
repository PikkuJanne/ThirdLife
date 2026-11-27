using System.IO;
using System.Text.Json;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Broker.Protocol;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Inventory;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.UI.Features.Disposition;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private sealed record ReviewedUpdate(WindowsUpdateOffer Offer, string? DecisionId, string? ServicingPolicySha256, PolicySnapshot Policy);
    private IWindowsUpdateDispatchBackend? UpdateBackend { get; init; }
    private ReviewedUpdate? _reviewedUpdate;
    private string _updateOperator = string.Empty;
    private bool _servicingUpdate;

    public string UpdateOperator
    {
        get => _updateOperator;
        set { if (_updateOperator != value) { _updateOperator = value; Publish(); } }
    }

    public bool ShowsUpdatePolicyEvaluation => _reviewedUpdate is { DecisionId: null } && HasWindowsUpdatePreview;
    public bool CanEvaluateUpdatePolicy => CanAssess && ShowsUpdatePolicyEvaluation && CurrentAssessmentRestriction is null;
    public bool ShowsUpdateApproval => _reviewedUpdate?.DecisionId is not null && HasWindowsUpdatePreview;
    public bool CanApproveWindowsUpdate => ShowsUpdateApproval && CanAssess && !string.IsNullOrWhiteSpace(_updateOperator);

    private WindowsUpdateEntry? SavedServicingApproval => _updateHistory.LastOrDefault() is
    { Stage: WindowsUpdateStage.Approved, Approval: { Actions: not null } scope } saved &&
        UpdateBackend is not null && Job is { IsArchived: false } && Gate?.Decision?.DecisionId == saved.GateId &&
        scope.Actions.ApprovalId == saved.Id && scope.Actions.MatchesCurrentActions(scope.Exact) &&
        (IsFixtureScenario ? scope.ServicingPolicySha256 is null : scope.ServicingPolicySha256 is { } digest &&
            WindowsUpdateServicingPolicy.CurrentQualificationPolicySha256(scope.Exact, _timeProvider.GetUtcNow()) == digest) ? saved : null;

    public bool ShowsApprovedUpdate => SavedServicingApproval is not null;
    public bool CanRunApprovedUpdate => CanAssess && ShowsApprovedUpdate;
    public string ApprovedUpdateScope
    {
        get
        {
            if (SavedServicingApproval is not { Approval: { } saved } entry) { return string.Empty; }
            var text = $"{DescribeUpdate(saved.Exact)}{Environment.NewLine}{DescribeServicingImpact(saved.Exact)}";
            if (saved.ServicingPolicySha256 is not { } digest) { return text; }
            if (WindowsUpdateServicingPolicy.CurrentQualificationSummary(saved.Exact, _timeProvider.GetUtcNow()) is { } review &&
                review.PolicySha256 == digest) { text += Environment.NewLine + DescribeUpdateRecovery(review); }
            var approvalDigest = BrokerWindowsUpdateRequest.ComputeApprovalSha256(entry.JobId, entry.GateId, saved.ScanId,
                saved.PolicyDecisionId, digest, saved.Actions!, saved.Exact);
            return $"{text}{Environment.NewLine}Approval SHA-256 {approvalDigest}{Environment.NewLine}" +
                $"Download action {saved.Actions!.Download.ActionId:D}{Environment.NewLine}Installation action {saved.Actions.Install.ActionId:D}";
        }
    }

    private PolicySnapshot? UpdateOrganizationPolicy(WindowsUpdateCandidate exact, DateTimeOffset now)
    {
        if (IsFixtureScenario)
        {
            var deviceClass = RepairDeviceClass ?? PolicyDeviceClass.Laptop;
            var fixture = _fixturePlanPolicy?.Invoke(deviceClass);
            return fixture?.Document is
            {
                IsCurrentSchema: true, Status: ThirdLife.Policy.Schema.PolicyStatus.Candidate, SyntheticData: true,
                ClaimScope: "windows_update_fixture"
            } && fixture.Document.Target.DeviceClass == deviceClass ? fixture : null;
        }
        if (RepairDeviceClass is not { } selectedClass) { return null; }
        var hash = WindowsUpdateServicingPolicy.CurrentQualificationOrganizationPolicySha256(exact, now);
        if (hash is null) { return null; }
        try
        {
            var bytes = WorkshopPlanCandidate.ReadPinned($"ThirdLife.UI.ServicingPolicy.{hash}.json", PolicySchema.MaximumDocumentBytes, hash);
            var policy = PolicySnapshot.Restore(bytes, hash);
            return policy.Document is
            {
                IsCurrentSchema: true, Status: ThirdLife.Policy.Schema.PolicyStatus.Effective, SyntheticData: false,
                ClaimScope: WindowsUpdateServicingPolicy.OrganizationClaimScope, Effective.FromUtc: { } from
            } &&
                from <= now && (policy.Document.Effective.UntilUtc is not { } until || now < until) && policy.Document.Target.DeviceClass == selectedClass ? policy : null;
        }
        catch (Exception error) when (error is IOException or JsonException) { return null; }
    }

    private async Task<ReviewedUpdate?> ReviewServicingPolicyAsync(IWindowsUpdateJournal journal, JobId jobId,
        SanitizationGateDecisionId gateId, WindowsUpdateOffer offer, WindowsUpdatePreparation observation,
        DateTimeOffset requestedAt, CancellationToken token)
    {
        if (UpdateBackend is null) { return null; }
        var now = _timeProvider.GetUtcNow();
        var digest = WindowsUpdateServicingPolicy.CurrentQualificationPolicySha256(offer.Exact, now);
        if (!HasServicingResources(offer.Exact, digest, observation, requestedAt, now)) { return null; }
        var policy = UpdateOrganizationPolicy(offer.Exact, now);
        if (policy is null || !await IsCurrentOfferAsync(_store!, journal, jobId, gateId, offer, token)) { return null; }
        var decision = await journal.FindWindowsUpdatePolicyAsync(jobId, gateId, offer.ScanId, offer.Exact,
            IsFixtureScenario ? null : digest, token);
        return new(offer, decision, IsFixtureScenario ? null : digest, policy);
    }

    private bool HasServicingResources(WindowsUpdateCandidate exact, string? digest, WindowsUpdatePreparation observation,
        DateTimeOffset requestedAt, DateTimeOffset now) => IsFixtureScenario
        ? observation is
        {
            Status: WindowsUpdateStatus.Succeeded, EligibilityReason: WindowsUpdateEligibilityReason.Eligible,
            RebootRequired: false, ServicingSupported: true, PowerReady: true, HasRecommendedSpace: true, RemovalSupported: true
        } &&
            observation.Exact == exact && observation.ObservedAtUtc >= requestedAt && observation.ObservedAtUtc <= now &&
            now - requestedAt < WindowsUpdateServicingPolicy.ObservationWindow
        : digest is not null && WindowsUpdateServicingPolicy.EvaluateQualification(exact, digest, observation, requestedAt, now) == WindowsUpdateServicingDecision.Allowed;

    private WindowsUpdateWorkflow CreateServicingWorkflow(IWindowsUpdateJournal journal, IWindowsUpdateAgent agent, string? digest) =>
        new(journal, agent, UpdateBackend!, async (exact, token) =>
        {
            var requestedAt = _timeProvider.GetUtcNow();
            var observation = await agent.AssessPreparationAsync(exact, token).ConfigureAwait(false);
            return HasServicingResources(exact, digest, observation, requestedAt, _timeProvider.GetUtcNow());
        }, _timeProvider);

    public Task EvaluateUpdatePolicyAsync()
    {
        if (!CanEvaluateUpdatePolicy || _reviewedUpdate is not { } displayed || _store is not IPolicyHistoryStore store ||
            _store is not IWindowsUpdateJournal journal || Job is not { } job || Gate?.Decision is not { } gate) { return Task.CompletedTask; }

        return RunAsync(async () =>
        {
            var jobId = job.Job.JobId;
            var now = _timeProvider.GetUtcNow();
            var current = await LoadJobAsync(jobId);
            var assessment = AssessmentOrchestrator.ReadLatest(current);
            if (assessment is null || assessment.AssessmentId != Assessment?.AssessmentId ||
                IsSeparateEvidenceOrigin(current) || assessment.GateDecisionId != gate.DecisionId ||
                UpdateOrganizationPolicy(displayed.Offer.Exact, now)?.Document.ContentSha256 != displayed.Policy.Document.ContentSha256 ||
                !await IsCurrentOfferAsync(_store, journal, jobId, gate.DecisionId, displayed.Offer, CancellationToken.None))
            {
                DiscardStaleOffers();
                return;
            }
            var previous = await store.LoadLatestPolicyDecisionAsync(jobId, assessment.AssessmentId.Value);
            var decision = await Task.Run(() =>
            {
                var evidence = AuditPolicyEvidence.Create(current, assessment, now);
                // This snapshot is reviewed for servicing. Evaluate every capability-conditioned rule it contains.
                var capabilities = displayed.Policy.Document.Rules.SelectMany(rule => rule.ActiveForCapabilityIds)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
                var evaluated = DispositionEngine.Evaluate(displayed.Policy.Document, evidence, capabilities);
                return PolicyDecisionRecord.Create(displayed.Policy, jobId, assessment.AssessmentId.Value, evaluated,
                    evidence.Select(item => item.Metadata.EvidenceId), now, previous?.Decision.DecisionId,
                    current.Observations.Count + current.SanitizationEvidence.Count + current.HumanTests.Count);
            });
            if (previous is not null && !previous.Decision.EvidenceIds.AsSpan().SequenceEqual(decision.EvidenceIds.AsSpan()))
            {
                _reviewedUpdate = null;
                WindowsUpdatePreview = "The job's evidence changed. Run a new assessment before evaluating the servicing policy.";
                return;
            }
            await store.AppendPolicyDecisionAsync(displayed.Policy, decision);
            ApplySavedPolicy(new(displayed.Policy, decision));
            ClearReport();
            _reviewedUpdate = null;
            WindowsUpdatePreview = decision.Disposition == Disposition.ReadyToPrepare
                ? "The update policy allows preparation. Check the offer again to review its current scope before approving."
                : $"The update policy requires review. {decision.Summary}";
        });
    }

    public Task ApproveWindowsUpdateAsync()
    {
        if (!CanApproveWindowsUpdate || _reviewedUpdate is not { } displayed || _selectedUpdateOffer != displayed.Offer ||
            _store is not IWindowsUpdateJournal journal || UpdateAgent is not { } agent || Gate?.Decision is not { } gate ||
            Job is not { } job) { return Task.CompletedTask; }

        return RunAsync(async () =>
        {
            var actor = new OperatorId(_updateOperator.Trim());
            _checkingUpdateOffer = true;
            _updateCancellation = new CancellationTokenSource();
            var token = _updateCancellation.Token;
            Publish();
            try
            {
                var requestedAt = _timeProvider.GetUtcNow();
                var observation = await agent.AssessPreparationAsync(displayed.Offer.Exact, token);
                var confirmed = await ReviewServicingPolicyAsync(journal, job.Job.JobId, gate.DecisionId, displayed.Offer,
                    observation, requestedAt, token);
                if (confirmed?.DecisionId is null || confirmed.DecisionId != displayed.DecisionId ||
                    confirmed.Policy.Document.ContentSha256 != displayed.Policy.Document.ContentSha256 ||
                    confirmed.ServicingPolicySha256 != displayed.ServicingPolicySha256)
                {
                    _reviewedUpdate = null;
                    WindowsUpdatePreview = "The offer, policy or resource check changed. Check the offer again before approving.";
                    return;
                }
                await CreateServicingWorkflow(journal, agent, confirmed.ServicingPolicySha256).ApproveAsync(job.Job.JobId,
                    gate.DecisionId, displayed.Offer.ScanId, displayed.Offer.Exact, actor, confirmed.DecisionId,
                    confirmed.ServicingPolicySha256, token);
                await LoadWindowsUpdateHistoryAsync(job.Job.JobId);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _reviewedUpdate = null;
                WindowsUpdatePreview = "Approval check cancelled. Check the offer again before approving.";
            }
            finally { _checkingUpdateOffer = false; }
        });
    }

    public Task RunApprovedUpdateAsync()
    {
        if (!CanRunApprovedUpdate || SavedServicingApproval is not { Approval: { } scope } saved ||
            _store is not IWindowsUpdateJournal journal || UpdateAgent is not { } agent) { return Task.CompletedTask; }
        return RunAsync(async () =>
        {
            _servicingUpdate = true;
            _updateCancellation = new CancellationTokenSource();
            var token = _updateCancellation.Token;
            Publish();
            try
            {
                await Task.Run(() => CreateServicingWorkflow(journal, agent, scope.ServicingPolicySha256)
                    .ExecuteAsync(saved.JobId, saved.Id, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Status = "Servicing cancelled. Review the saved update history.";
            }
            finally
            {
                _servicingUpdate = false;
                await LoadWindowsUpdateHistoryAsync(saved.JobId);
            }
        });
    }
}
