using System.Text.Json;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory;

/// <summary>Runs a fixed read-only roster and commits each complete provider attempt independently.</summary>
public sealed class AssessmentOrchestrator
{
    public const int MaximumProviders = 16;
    public const int MaximumConcurrency = 4;
    private readonly IJobStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly InventoryProviderRunner _runner;
    private readonly RegisteredProvider[] _providers;
    private readonly int _maximumConcurrency;
    private int _inProgress;
    private int _cleanupBlocked;
    private Task<ProviderReadResult>[] _operations = [];

    internal Task CleanupCompletion { get; private set; } = Task.CompletedTask;
    internal Task CurrentOperationsCompletion => Task.WhenAll(Volatile.Read(ref _operations));

    public AssessmentOrchestrator(IJobStore store, IEnumerable<IInventoryProvider> providers,
        TimeProvider? timeProvider = null, int maximumConcurrency = 1)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(providers);
        if (maximumConcurrency is < 1 or > MaximumConcurrency)
        { throw new ArgumentOutOfRangeException(nameof(maximumConcurrency)); }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _runner = new(_timeProvider);
        _maximumConcurrency = maximumConcurrency;
        var roster = providers.Take(MaximumProviders + 1).ToArray();
        if (roster.Length is 0 or > MaximumProviders || roster.Any(static provider => provider is null))
        { throw new ArgumentException("An assessment requires a bounded nonempty provider roster.", nameof(providers)); }

        var identities = new HashSet<ProviderId>();
        var keys = new HashSet<EvidenceKey>();
        _providers = roster.Select(provider =>
        {
            var descriptor = provider.Descriptor ?? throw new ArgumentException("A provider descriptor is required.", nameof(providers));
            if (descriptor.MinimumPrivilege != ProviderPrivilegeRequirement.StandardUser ||
                descriptor.NetworkUse != ProviderNetworkUse.None ||
                descriptor.ProviderId.Value == "assessment-orchestrator" ||
                !identities.Add(descriptor.ProviderId))
            { throw new ArgumentException("The provider roster violates the read-only assessment contract.", nameof(providers)); }

            foreach (var key in descriptor.EvidenceDefinitions.Select(static definition => definition.EvidenceKey)
                .Append(descriptor.FailureDefinition.EvidenceKey))
            {
                if (key.Value.StartsWith("assessment.", StringComparison.Ordinal) || !keys.Add(key))
                { throw new ArgumentException("Every evidence key must have one exclusive provider owner.", nameof(providers)); }
            }

            return new RegisteredProvider(provider, descriptor);
        }).OrderBy(static provider => provider.Descriptor.ProviderId.Value, StringComparer.Ordinal).ToArray();
    }

    public async Task<AssessmentSnapshot> AssessAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _cleanupBlocked) != 0)
        { throw new AssessmentException("assessment_cleanup_incomplete", null); }
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
        { throw new AssessmentException("assessment_busy", null); }

        var calls = new List<CapturedProvider>();
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AssessmentId? assessmentId = null;
        try
        {
            if (Volatile.Read(ref _cleanupBlocked) != 0)
            { throw new AssessmentException("assessment_cleanup_incomplete", null); }

            var storedJob = await ReadJobAsync(jobId, assessmentId, cancellationToken).ConfigureAwait(false);
            var access = SanitizationGate.Inspect(storedJob);
            if (!access.AllowsAssessment)
            { throw new AssessmentException("assessment_gate_blocked", null); }

            cancellationToken.ThrowIfCancellationRequested();
            assessmentId = AssessmentId.New();
            var gateId = access.Decision!.DecisionId;
            var start = AssessmentJournal.CreateStart(jobId, assessmentId, gateId,
                _providers.Select(static provider => provider.Descriptor.ProviderId).ToArray(), _timeProvider.GetUtcNow());
            await CommitAsync(jobId, assessmentId, gateId, [start], cancellationToken).ConfigureAwait(false);

            var active = new List<PendingProvider>();
            var nextProvider = 0;
            var requiresReview = false;
            var stopScheduling = false;
            while (active.Count != 0 || nextProvider < _providers.Length)
            {
                LatchCleanupFailure(calls.Select(static call => call.Operation).OfType<Task<ProviderReadResult>>());
                stopScheduling |= Volatile.Read(ref _cleanupBlocked) != 0;
                while (!stopScheduling && !runCancellation.IsCancellationRequested &&
                    active.Count < _maximumConcurrency && nextProvider < _providers.Length)
                {
                    try
                    {
                        await _store.RequireSanitizationGateAsync(jobId, gateId, runCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (runCancellation.IsCancellationRequested) { break; }
                    catch (Exception) { throw new AssessmentException("assessment_gate_unverified", assessmentId); }

                    LatchCleanupFailure(calls.Select(static call => call.Operation).OfType<Task<ProviderReadResult>>());
                    if (Volatile.Read(ref _cleanupBlocked) != 0) { stopScheduling = true; break; }
                    if (runCancellation.IsCancellationRequested) { break; }
                    var registered = _providers[nextProvider++];
                    var captured = new CapturedProvider(registered);
                    calls.Add(captured);
                    active.Add(new(captured, _runner.RunAsync(captured, runCancellation.Token).AsTask()));
                    Volatile.Write(ref _operations,
                        calls.Select(static call => call.Operation).OfType<Task<ProviderReadResult>>().ToArray());
                }

                if (active.Count == 0) { break; }
                var completed = await Task.WhenAny(active.Select(static item => item.Result)).ConfigureAwait(false);
                var pending = active.Single(item => item.Result == completed);
                active.Remove(pending);
                var result = await completed.ConfigureAwait(false);
                requiresReview |= result.Outcome != ProviderRunOutcome.Completed;
                if (result.Outcome == ProviderRunOutcome.CleanupIncomplete)
                {
                    Volatile.Write(ref _cleanupBlocked, 1);
                    stopScheduling = true;
                }

                // A timed-out native call still occupies a real worker. Do not refill that slot
                // or queue more work while its cleanup is outstanding.
                if (pending.Provider.Operation is { IsCompleted: false }) { stopScheduling = true; }
                var marker = AssessmentJournal.CreateProvider(assessmentId, result, _timeProvider.GetUtcNow());
                await CommitAsync(jobId, assessmentId, gateId, result.Observations.Append(marker),
                    CancellationToken.None).ConfigureAwait(false);
            }

            var outcome = cancellationToken.IsCancellationRequested ? AssessmentOutcome.Cancelled :
                requiresReview || nextProvider < _providers.Length ? AssessmentOutcome.RequiresReview : AssessmentOutcome.Completed;
            var finish = AssessmentJournal.CreateFinish(assessmentId, outcome, _timeProvider.GetUtcNow());
            await CommitAsync(jobId, assessmentId, gateId, [finish], CancellationToken.None).ConfigureAwait(false);
            return await LoadAsync(jobId, assessmentId, CancellationToken.None).ConfigureAwait(false)
                ?? throw new AssessmentException("assessment_data_invalid", assessmentId);
        }
        finally
        {
            RequestCancellation(runCancellation);
            var operations = calls.Select(static call => call.Operation).OfType<Task<ProviderReadResult>>().ToArray();
            CleanupCompletion = ReleaseAfterCleanupAsync(operations);
        }
    }

    public async Task<AssessmentSnapshot?> LoadAsync(JobId jobId, AssessmentId assessmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(assessmentId);
        var storedJob = await ReadJobAsync(jobId, assessmentId, cancellationToken).ConfigureAwait(false);
        return AssessmentJournal.Read(storedJob, assessmentId);
    }

    private async Task<StoredJob> ReadJobAsync(JobId jobId, AssessmentId? assessmentId, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _store.LoadJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                ?? throw new AssessmentException("assessment_job_missing", assessmentId);
            if (stored.Job.JobId != jobId) { throw new AssessmentException("assessment_data_invalid", assessmentId); }
            return stored;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AssessmentException) { throw; }
        catch (Exception) { throw new AssessmentException("assessment_store_unavailable", assessmentId); }
    }

    private async Task CommitAsync(JobId jobId, AssessmentId assessmentId, SanitizationGateDecisionId gateId,
        IEnumerable<Observation> observations, CancellationToken cancellationToken)
    {
        var batch = new JobEvidenceBatch(jobId, observations, expectedSanitizationGateDecisionId: gateId);
        try { await _store.AppendEvidenceAsync(batch, cancellationToken).ConfigureAwait(false); }
        catch (Exception)
        {
            // A write may have committed before a cancellation or post-commit guard failed.
            // Reconcile exact immutable IDs/payloads; never blindly retry an ambiguous write.
            var stored = await ReadJobAsync(jobId, assessmentId, CancellationToken.None).ConfigureAwait(false);
            var ids = batch.Observations.Select(static observation => observation.Metadata.EvidenceId).ToHashSet();
            var committed = stored.Observations.Where(observation => ids.Contains(observation.Metadata.EvidenceId)).ToArray();
            var options = DomainJson.CreateStrictOptions();
            if (committed.Length == batch.Observations.Count && batch.Observations.All(expected =>
                committed.Count(actual => actual.Metadata.EvidenceId == expected.Metadata.EvidenceId &&
                    JsonSerializer.Serialize(actual, options) == JsonSerializer.Serialize(expected, options)) == 1))
            { return; }

            if (committed.Length == 0 && cancellationToken.IsCancellationRequested)
            { throw new OperationCanceledException(cancellationToken); }
            throw new AssessmentException("assessment_store_unavailable", assessmentId);
        }
    }

    private async Task ReleaseAfterCleanupAsync(Task<ProviderReadResult>[] operations)
    {
        try { await Task.WhenAll(operations).ConfigureAwait(false); }
        catch (Exception) { /* The runner already recorded a sanitized outcome; never publish late data. */ }
        finally
        {
            LatchCleanupFailure(operations);
            Volatile.Write(ref _inProgress, 0);
        }
    }

    private void LatchCleanupFailure(IEnumerable<Task<ProviderReadResult>> operations)
    {
        if (operations.Any(static operation => operation.IsCompletedSuccessfully &&
            operation.Result?.Status == ProviderReadStatus.CleanupIncomplete))
        { Volatile.Write(ref _cleanupBlocked, 1); }
    }

    private static void RequestCancellation(CancellationTokenSource cancellation)
    {
        try { cancellation.Cancel(throwOnFirstException: false); }
        catch (AggregateException) { /* Provider callbacks cannot replace the durable assessment result. */ }
    }

    private sealed record RegisteredProvider(IInventoryProvider Provider, InventoryProviderDescriptor Descriptor);
    private sealed record PendingProvider(CapturedProvider Provider, Task<ProviderRunResult> Result);

    private sealed class CapturedProvider(RegisteredProvider registered) : IInventoryProvider
    {
        public InventoryProviderDescriptor Descriptor => registered.Descriptor;
        public Task<ProviderReadResult>? Operation { get; private set; }

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            Operation = Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await registered.Provider.ObserveAsync(cancellationToken).ConfigureAwait(false);
            }, CancellationToken.None);
            return new(Operation);
        }
    }
}
