using System.Diagnostics;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Repairs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class AssessmentOrchestratorTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = AssessmentTestStore.Timestamp.AddMinutes(1);
    private static readonly string[] CompletedKeys = ["test.alpha.value", "test.beta.value"];
    private static readonly string[] RepeatedSources = ["source.alpha.1", "source.alpha.2"];
    private static readonly string[] GateFailureCodes = ["assessment_gate_blocked", "assessment_store_unavailable"];

    [Fact]
    public async Task CompletedAssessmentCommitsProvidersIndependentlyAndReloadsWithoutJournalMarkers()
    {
        await using var store = new AssessmentTestStore();
        var alpha = Available("alpha");
        var beta = Available("beta");
        var orchestrator = new AssessmentOrchestrator(store, [beta, alpha], new FixedTimeProvider(Now));

        var snapshot = await orchestrator.AssessAsync(store.JobId);

        Assert.Equal("Completed", snapshot.Outcome.ToString());
        Assert.Equal(store.JobId, snapshot.JobId);
        Assert.Equal(Now, snapshot.StartedAtUtc);
        Assert.NotNull(snapshot.FinishedAtUtc);
        Assert.Equal(2, snapshot.Providers.Count);
        Assert.Equal(CompletedKeys, snapshot.Observations.Select(static fact => fact.EvidenceKey.Value));
        Assert.All(snapshot.Providers, static provider => Assert.Equal(ProviderRunOutcome.Completed, provider.Outcome));
        Assert.All(snapshot.Observations, static fact =>
        {
            Assert.Equal(PrivacyClassification.WorkshopRestricted, fact.Metadata.PrivacyClassification);
            Assert.Equal(ProvenanceKind.SyntheticFixture, fact.Metadata.Provenance.Kind);
            Assert.DoesNotContain("assessment.", fact.EvidenceKey.Value, StringComparison.Ordinal);
        });
        Assert.Single(store.Committed, static batch => Contains(batch, "test.alpha.value"));
        Assert.Single(store.Committed, static batch => Contains(batch, "test.beta.value"));
        Assert.DoesNotContain(store.Committed, static batch => Contains(batch, "test.alpha.value") && Contains(batch, "test.beta.value"));
        Assert.All(store.Committed, batch => Assert.Equal(snapshot.GateDecisionId, batch.ExpectedSanitizationGateDecisionId));
        var reloaded = await orchestrator.LoadAsync(store.JobId, snapshot.AssessmentId);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(reloaded));
        Assert.Equal(1, alpha.InvocationCount);
        Assert.Equal(1, beta.InvocationCount);
    }

    [Theory]
    [InlineData(SanitizationState.Unknown)]
    [InlineData(SanitizationState.Failed)]
    public async Task BlockingSanitizationInvokesNoProviderAndWritesNoAssessment(SanitizationState state)
    {
        await using var store = new AssessmentTestStore(state);
        var provider = Available("alpha");
        var orchestrator = new AssessmentOrchestrator(store, [provider]);

        var error = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(store.JobId));

        Assert.Equal("assessment_gate_blocked", error.ResultCode);
        Assert.Null(error.AssessmentId);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Empty(store.Committed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingDecisionStaleDecisionAndArchivePreventAssessment(int condition)
    {
        await using var store = new AssessmentTestStore(recordDecision: condition != 0);
        if (condition == 1) { store.ChangeSanitization(SanitizationState.Verified, recordDecision: false); }
        if (condition == 2) { await store.SetArchiveStateAsync(store.JobId, true, Now); }
        var provider = Available("alpha");

        var error = await Assert.ThrowsAsync<AssessmentException>(() =>
            new AssessmentOrchestrator(store, [provider]).AssessAsync(store.JobId));

        Assert.Equal("assessment_gate_blocked", error.ResultCode);
        Assert.Equal(0, provider.InvocationCount);
        Assert.Empty(store.Committed);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task DuplicateIdsValueKeysAndFailureKeysRejectEveryInputOrder(int collision, bool reverse)
    {
        await using var store = new AssessmentTestStore();
        var first = Available("alpha");
        var second = new FakeInventoryProvider(Descriptor(collision == 0 ? "alpha" : "beta",
            key: collision == 1 ? "test.alpha.value" : collision == 2 ? "test.alpha.failure" : "test.beta.value",
            failureKey: collision == 3 ? "test.alpha.failure" : "test.beta.failure"),
            _ => ValueTask.FromResult(ProviderReadResult.Unavailable()));
        IInventoryProvider[] providers = reverse ? [second, first] : [first, second];

        Assert.ThrowsAny<ArgumentException>(() => new AssessmentOrchestrator(store, providers));

        Assert.Equal(0, first.InvocationCount);
        Assert.Equal(0, second.InvocationCount);
        Assert.Empty(store.Committed);
    }

    [Theory]
    [InlineData("assessment.spoofed", false)]
    [InlineData("assessment.spoofed", true)]
    [InlineData("repair.spoofed", false)]
    [InlineData("repair.spoofed", true)]
    public async Task ReservedJournalKeysCannotBeClaimedByProviders(string reservedKey, bool failureKey)
    {
        await using var store = new AssessmentTestStore();
        var provider = new FakeInventoryProvider(Descriptor("alpha",
            key: failureKey ? "test.alpha.value" : reservedKey,
            failureKey: failureKey ? reservedKey : "test.alpha.failure"),
            _ => ValueTask.FromResult(ProviderReadResult.Unavailable()));

        Assert.ThrowsAny<ArgumentException>(() => new AssessmentOrchestrator(store, [provider]));

        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public async Task ReservedRepairJournalProviderCannotJoinAssessmentRoster()
    {
        await using var store = new AssessmentTestStore();
        var descriptor = new InventoryProviderDescriptor(
            new("repair-journal"), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15), ProviderNetworkUse.None,
            ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
            new(new("test.alpha.failure"), "source.alpha.failure"),
            [new(new("test.alpha.value"), EvidenceValueKind.Boolean, null, "source.alpha")]);
        var provider = new FakeInventoryProvider(descriptor, _ => ValueTask.FromResult(ProviderReadResult.Unavailable()));

        Assert.ThrowsAny<ArgumentException>(() => new AssessmentOrchestrator(store, [provider]));
        Assert.Equal(0, provider.InvocationCount);
    }

    [Fact]
    public async Task FailedRetestStartCannotLeaveUnlinkedAssessmentAndRetryUsesTheSameGuard()
    {
        await using var store = new AssessmentTestStore();
        var key = new EvidenceKey("battery.present");
        var batteryPresent = false;
        var provider = new FakeInventoryProvider(Descriptor("battery", key: key.Value), _ =>
            ValueTask.FromResult(ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(key, EvidenceValue.FromBoolean(batteryPresent))])));
        var prior = await new AssessmentOrchestrator(store, [provider], new FixedTimeProvider(Now)).AssessAsync(store.JobId);
        var failure = Assert.Single(prior.Observations);
        var repair = await new RepairRetestService(store, new FixedTimeProvider(Now.AddMinutes(1))).RecordAsync(
            store.JobId, prior.AssessmentId.Value, failure.Metadata.EvidenceId, RepairComponent.Battery,
            RepairAction.Replaced, new OperatorId("operator-synthetic"));
        batteryPresent = true;
        var retestOrchestrator = new AssessmentOrchestrator(store, [provider], new FixedTimeProvider(Now.AddMinutes(2)));
        store.Fault = AssessmentStoreFault.BeforeCommit;
        store.FaultSelector = static batch => Contains(batch, "repair.retest_started");

        var error = await Assert.ThrowsAsync<AssessmentException>(() => retestOrchestrator.RetestAsync(store.JobId, repair.RepairId));
        Assert.Equal("assessment_store_unavailable", error.ResultCode);
        Assert.NotNull(error.AssessmentId);
        Assert.Null(await retestOrchestrator.LoadAsync(store.JobId, error.AssessmentId));
        Assert.DoesNotContain(store.Committed, static batch => Contains(batch, "repair.retest_started"));
        Assert.Equal(1, provider.InvocationCount);

        var retest = await retestOrchestrator.RetestAsync(store.JobId, repair.RepairId);
        var startBatch = Assert.Single(store.Committed, static batch => Contains(batch, "repair.retest_started"));
        Assert.Equal(retest.GateDecisionId, startBatch.ExpectedSanitizationGateDecisionId);
        Assert.Collection(startBatch.Observations,
            static observation => Assert.Equal("assessment.start", observation.EvidenceKey.Value),
            static observation => Assert.Equal("repair.retest_started", observation.EvidenceKey.Value));
        Assert.All(startBatch.Observations, observation => Assert.Equal(retest.StartedAtUtc, observation.Metadata.CollectedAtUtc));
        var saved = Assert.IsType<StoredJob>(await store.LoadJobAsync(store.JobId));
        var link = Assert.Single(RepairJournal.GetRetests(saved));
        Assert.Equal(repair.RepairId, link.RepairId);
        Assert.Equal(retest.AssessmentId.Value, link.AssessmentId);
        Assert.Equal(failure.Metadata.EvidenceId, link.PriorFailureEvidenceId);
        var updated = Assert.Single(retest.Observations);
        Assert.True(updated.Value!.BooleanValue);
        Assert.False(Assert.Single((await retestOrchestrator.LoadAsync(store.JobId, prior.AssessmentId))!.Observations).Value!.BooleanValue);
        Assert.NotEqual(failure.Metadata.EvidenceId, updated.Metadata.EvidenceId);
    }

    [Fact]
    public async Task PrivilegedProviderCannotEnterTheReadOnlyStandardUserAssessment()
    {
        await using var store = new AssessmentTestStore();
        var provider = new FakeInventoryProvider(Descriptor("alpha", privilege: ProviderPrivilegeRequirement.AdministratorReadOnly),
            _ => throw new InvalidOperationException("The privileged source must not run."));

        Assert.ThrowsAny<ArgumentException>(() => new AssessmentOrchestrator(store, [provider]));

        Assert.Equal(0, provider.InvocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ConcurrencyOutsideTheBoundIsRejected(int concurrency)
    {
        await using var store = new AssessmentTestStore();
        Assert.ThrowsAny<ArgumentException>(() => new AssessmentOrchestrator(store, [Available("alpha")], maximumConcurrency: concurrency));
    }

    [Fact]
    public async Task ProviderRosterEnumerationIsBoundedBeforeAnyWorkStarts()
    {
        await using var store = new AssessmentTestStore();
        var count = 0;
        IEnumerable<IInventoryProvider> Oversized()
        {
            for (var index = 0; index < 1000; index++)
            {
                Assert.True(++count <= 17, "The provider roster exceeded its sentinel bound.");
                yield return Available("provider" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        Assert.ThrowsAny<ArgumentException>(() => new AssessmentOrchestrator(store, Oversized()));

        Assert.Equal(17, count);
        Assert.Empty(store.Committed);
    }

    [Fact]
    public async Task AllProviderPermutationsPreserveUnknownConflictFreshnessAndSourceOwnership()
    {
        string? baseline = null;
        foreach (var permutation in Permutations([0, 1, 2, 3]))
        {
            await using var store = new AssessmentTestStore();
            IInventoryProvider[] providers =
            [
                Available("alpha"),
                Unknown("beta", ProviderLimitation.SourceValuesConflict),
                Unknown("gamma", ProviderLimitation.SourceValueStale),
                Unknown("delta", ProviderLimitation.SourceValueMissing),
            ];
            var snapshot = await new AssessmentOrchestrator(store, permutation.Select(index => providers[index]),
                new FixedTimeProvider(Now), maximumConcurrency: 2).AssessAsync(store.JobId);
            var semantic = Semantic(snapshot);
            baseline ??= semantic;
            Assert.Equal(baseline, semantic);
            Assert.True(Fact(snapshot, "test.alpha.value").Value!.BooleanValue);
            Assert.Null(Fact(snapshot, "test.beta.value").Value);
            Assert.Equal("provider_values_conflict", Fact(snapshot, "test.beta.value").LimitationCode);
            Assert.Null(Fact(snapshot, "test.gamma.value").Value);
            Assert.Equal("provider_value_stale", Fact(snapshot, "test.gamma.value").LimitationCode);
            Assert.All(providers, static provider => Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse));
        }
    }

    [Fact]
    public async Task RepeatedEvidenceWithinOneOwnerRetainsEverySourceAndCanonicalOrder()
    {
        await using var store = new AssessmentTestStore();
        var descriptor = new InventoryProviderDescriptor(new("provider-alpha"), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15), ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture,
            [ProviderOperatingSystem.Windows11], new(new("test.alpha.failure"), "source.alpha.failure"),
            [new(new("test.alpha.value"), EvidenceValueKind.WholeNumber, "bytes", "source.alpha", maximumCardinality: 2)]);
        var provider = new FakeInventoryProvider(descriptor, _ => ValueTask.FromResult(ProviderReadResult.Collected(
        [
            NormalizedEvidence.Observed(new("test.alpha.value"), EvidenceValue.FromInteger(200), sourceReference: "source.alpha.2"),
            NormalizedEvidence.Observed(new("test.alpha.value"), EvidenceValue.FromInteger(100), sourceReference: "source.alpha.1"),
        ])));

        var snapshot = await new AssessmentOrchestrator(store, [provider], new FixedTimeProvider(Now)).AssessAsync(store.JobId);

        Assert.Equal(RepeatedSources, snapshot.Observations.Select(static fact => fact.Metadata.Provenance.SourceReference));
        Assert.Equal(new long?[] { 100, 200 }, snapshot.Observations.Select(static fact => fact.Value!.IntegerValue));
    }

    [Fact]
    public async Task RerunNeverCarriesOlderKnownValueIntoNewUnknownResult()
    {
        await using var store = new AssessmentTestStore();
        var calls = 0;
        var provider = new FakeInventoryProvider(Descriptor("alpha"), _ => ValueTask.FromResult(++calls == 1
            ? Collected("alpha") : ProviderReadResult.Unavailable()));
        var orchestrator = new AssessmentOrchestrator(store, [provider], new FixedTimeProvider(Now));
        var first = await orchestrator.AssessAsync(store.JobId);
        var original = JsonSerializer.Serialize(first);

        var second = await orchestrator.AssessAsync(store.JobId);

        Assert.NotEqual(first.AssessmentId, second.AssessmentId);
        Assert.True(Fact(first, "test.alpha.value").Value!.BooleanValue);
        Assert.Null(Fact(second, "test.alpha.value").Value);
        Assert.Equal(ProviderRunOutcome.Unavailable, Assert.Single(second.Providers).Outcome);
        Assert.Equal("RequiresReview", second.Outcome.ToString());
        Assert.Equal(original, JsonSerializer.Serialize(await orchestrator.LoadAsync(store.JobId, first.AssessmentId)));
        Assert.DoesNotContain(Fact(first, "test.alpha.value").Metadata.EvidenceId,
            second.Observations.Select(static fact => fact.Metadata.EvidenceId));
    }

    [Fact]
    public async Task LifecycleRevisionAndAsOfAreFrozenWithTheirOriginalEvidenceReferences()
    {
        await using var store = new AssessmentTestStore();
        var revision = "synthetic.lifecycle.v1";
        var stale = false;
        string[] keys = ["os.lifecycle.source", "os.lifecycle.revision", "os.lifecycle.as_of", "os.lifecycle.fresh_through", "os.lifecycle.evaluated_on", "os.support_target"];
        var descriptor = new InventoryProviderDescriptor(new("provider-lifecycle"), ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15), ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture,
            [ProviderOperatingSystem.Windows11], new(new("os.provider_status"), "source.os.status"),
            keys.Select(key => new ProviderEvidenceDefinition(new(key), key == "os.support_target" ? EvidenceValueKind.Enum : EvidenceValueKind.Text,
                null, "source.synthetic.lifecycle")));
        var provider = new FakeInventoryProvider(descriptor, _ => ValueTask.FromResult(ProviderReadResult.Collected(
        [
            LifecycleText("source", "source.synthetic.lifecycle"), LifecycleText("revision", revision),
            LifecycleText("as_of", stale ? "2026-10-01" : "2026-09-30"), LifecycleText("fresh_through", "2026-10-30"),
            LifecycleText("evaluated_on", stale ? "2026-10-31" : "2026-10-01"),
            stale ? NormalizedEvidence.NotAvailable(new("os.support_target"), ProviderLimitation.SourceValueStale)
                : NormalizedEvidence.Inferred(new("os.support_target"), EvidenceValue.FromEnum("supported")),
        ])));
        var orchestrator = new AssessmentOrchestrator(store, [provider], new FixedTimeProvider(Now));
        var first = await orchestrator.AssessAsync(store.JobId);
        var original = JsonSerializer.Serialize(first);
        revision = "synthetic.lifecycle.v2";
        stale = true;

        var second = await orchestrator.AssessAsync(store.JobId);
        var reopened = Assert.IsType<AssessmentSnapshot>(await orchestrator.LoadAsync(store.JobId, first.AssessmentId));

        Assert.Equal(original, JsonSerializer.Serialize(reopened));
        Assert.Equal("synthetic.lifecycle.v1", Fact(reopened, "os.lifecycle.revision").Value!.StringValue);
        Assert.Equal("2026-09-30", Fact(reopened, "os.lifecycle.as_of").Value!.StringValue);
        Assert.Equal("synthetic.lifecycle.v2", Fact(second, "os.lifecycle.revision").Value!.StringValue);
        Assert.Null(Fact(second, "os.support_target").Value);
        Assert.Equal(first.LifecycleEvidence.Select(static fact => fact.Metadata.EvidenceId),
            reopened.LifecycleEvidence.Select(static fact => fact.Metadata.EvidenceId));
        Assert.Equal(6, first.LifecycleEvidence.Count);
        Assert.All(first.LifecycleEvidence, fact => Assert.Contains(fact, first.Observations));
    }

    [Fact]
    public async Task PrecancellationStartsNoAssessmentAndLeavesAdmissionAvailable()
    {
        await using var store = new AssessmentTestStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = Available("alpha");
        var orchestrator = new AssessmentOrchestrator(store, [provider]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.AssessAsync(store.JobId, cancellation.Token));

        Assert.Equal(0, provider.InvocationCount);
        Assert.Empty(store.Committed);
        Assert.Equal("Completed", (await orchestrator.AssessAsync(store.JobId).WaitAsync(TimeSpan.FromSeconds(5))).Outcome.ToString());
    }

    [Fact]
    public async Task CancellationAfterFirstCommitPreservesThatBatchAndDoesNotStartNextProvider()
    {
        await using var store = new AssessmentTestStore();
        using var cancellation = new CancellationTokenSource();
        var alpha = Available("alpha");
        var beta = Available("beta");
        store.AfterCommit = batch => { if (Contains(batch, "test.alpha.value")) { cancellation.Cancel(); } };
        var orchestrator = new AssessmentOrchestrator(store, [alpha, beta]);

        var snapshot = await orchestrator.AssessAsync(store.JobId, cancellation.Token);

        Assert.Equal("Cancelled", snapshot.Outcome.ToString());
        Assert.True(Fact(snapshot, "test.alpha.value").Value!.BooleanValue);
        Assert.Null(Assert.Single(snapshot.Providers, static value => value.ProviderId.Value == "provider-beta").Outcome);
        Assert.Equal(0, beta.InvocationCount);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(await orchestrator.LoadAsync(store.JobId, snapshot.AssessmentId)));
    }

    [Fact]
    public async Task ActiveCancellationCommitsUnknownOutcomeWithoutLateValues()
    {
        await using var store = new AssessmentTestStore();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var provider = new FakeInventoryProvider(Descriptor("alpha"), async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Collected("alpha");
        });
        var orchestrator = new AssessmentOrchestrator(store, [provider]);
        var pending = orchestrator.AssessAsync(store.JobId, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        var snapshot = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Cancelled", snapshot.Outcome.ToString());
        Assert.Equal(ProviderRunOutcome.Cancelled, Assert.Single(snapshot.Providers).Outcome);
        Assert.All(snapshot.Observations, static fact => Assert.Null(fact.Value));
        Assert.Single(store.Committed, static batch => Contains(batch, "test.alpha.value"));
        await orchestrator.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConcurrentAssessmentCannotStartASecondWorker()
    {
        await using var store = new AssessmentTestStore();
        var entered = Signal();
        var release = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeInventoryProvider(Descriptor("alpha"), _ => { entered.TrySetResult(); return new(release.Task); });
        var orchestrator = new AssessmentOrchestrator(store, [provider]);
        var first = orchestrator.AssessAsync(store.JobId);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var error = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(store.JobId));
            Assert.Equal("assessment_busy", error.ResultCode);
            Assert.Null(error.AssessmentId);
            Assert.Equal(1, provider.InvocationCount);
        }
        finally { release.TrySetResult(Collected("alpha")); }
        Assert.Equal("Completed", (await first.WaitAsync(TimeSpan.FromSeconds(5))).Outcome.ToString());
    }

    [Fact]
    public async Task BoundedWorkersRequireARealCompletionBeforeDispatchingAnotherProvider()
    {
        await using var store = new AssessmentTestStore();
        var bothEntered = Signal();
        var thirdEntered = Signal();
        var firstRelease = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximum = 0;
        var entered = 0;
        var counterGate = new object();
        FakeInventoryProvider Held(string name, Task<ProviderReadResult> release) => new(Descriptor(name), async _ =>
        {
            lock (counterGate)
            {
                maximum = Math.Max(maximum, ++active);
                if (++entered == 2) { bothEntered.TrySetResult(); }
            }
            try { return await release; }
            finally { lock (counterGate) { active--; } }
        });
        var alpha = Held("alpha", firstRelease.Task);
        var beta = Held("beta", secondRelease.Task);
        var gamma = new FakeInventoryProvider(Descriptor("gamma"), _ => { thirdEntered.TrySetResult(); return ValueTask.FromResult(Collected("gamma")); });
        var orchestrator = new AssessmentOrchestrator(store, [gamma, beta, alpha], maximumConcurrency: 2);
        var pending = orchestrator.AssessAsync(store.JobId);
        try
        {
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, gamma.InvocationCount);
            firstRelease.TrySetResult(Collected("alpha"));
            await thirdEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Volatile.Read(ref active));
            Assert.Equal(2, Volatile.Read(ref maximum));
        }
        finally
        {
            firstRelease.TrySetResult(Collected("alpha"));
            secondRelease.TrySetResult(Collected("beta"));
        }
        Assert.Equal("Completed", (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Outcome.ToString());
    }

    [Fact]
    public async Task ManualTimeoutStopsQueuedWorkAndRetainsAdmissionUntilLateCleanup()
    {
        await using var store = new AssessmentTestStore();
        using var clock = new ManualClock();
        var entered = Signal();
        var cancelled = Signal();
        var release = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var alpha = new FakeInventoryProvider(Descriptor("alpha"), async token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            return await release.Task;
        });
        var beta = Available("beta");
        var orchestrator = new AssessmentOrchestrator(store, [alpha, beta], clock);
        var pending = orchestrator.AssessAsync(store.JobId);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireTimeout();
            var snapshot = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("RequiresReview", snapshot.Outcome.ToString());
            Assert.Equal(ProviderRunOutcome.TimedOut, Assert.Single(snapshot.Providers,
                static value => value.ProviderId.Value == "provider-alpha").Outcome);
            Assert.Null(Assert.Single(snapshot.Providers, static value => value.ProviderId.Value == "provider-beta").Outcome);
            Assert.Equal(0, beta.InvocationCount);
            Assert.All(snapshot.Observations, static fact => Assert.Null(fact.Value));
            var original = JsonSerializer.Serialize(snapshot);
            var busy = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(store.JobId));
            Assert.Equal("assessment_busy", busy.ResultCode);
            Assert.Equal(original, JsonSerializer.Serialize(await orchestrator.LoadAsync(store.JobId, snapshot.AssessmentId)));
        }
        finally { release.TrySetResult(Collected("alpha")); }
        await orchestrator.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Completed", (await orchestrator.AssessAsync(store.JobId)).Outcome.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCleanupFailurePermanentlyBlocksRerunAfterTimeoutOrCancellation(bool cancel)
    {
        await using var store = new AssessmentTestStore();
        using var clock = new ManualClock();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var release = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeInventoryProvider(Descriptor("alpha"), _ =>
        {
            entered.TrySetResult();
            return new(release.Task);
        });
        var orchestrator = new AssessmentOrchestrator(store, [provider], clock);
        var pending = orchestrator.AssessAsync(store.JobId, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancel) { await cancellation.CancelAsync(); }
            else { clock.FireTimeout(); }
            var snapshot = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(cancel ? "Cancelled" : "RequiresReview", snapshot.Outcome.ToString());
            Assert.All(snapshot.Observations, static fact => Assert.Null(fact.Value));
        }
        finally { release.TrySetResult(ProviderReadResult.CleanupIncomplete()); }
        await orchestrator.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(store.JobId));

        Assert.Equal("assessment_cleanup_incomplete", error.ResultCode);
        Assert.Null(error.AssessmentId);
        Assert.Equal(1, provider.InvocationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureCompletingDuringCommitOrGateCheckPreventsWorkerRefill(bool duringGateCheck)
    {
        await using var store = new AssessmentTestStore();
        using var clock = new ManualClock();
        var alphaEntered = Signal();
        var betaEntered = Signal();
        var betaCancelled = Signal();
        var alphaRelease = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var betaRelease = new TaskCompletionSource<ProviderReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var alpha = new FakeInventoryProvider(Descriptor("alpha"), _ =>
        {
            alphaEntered.TrySetResult();
            return new(alphaRelease.Task);
        });
        var beta = new FakeInventoryProvider(Descriptor("beta"), async token =>
        {
            using var registration = token.Register(() => betaCancelled.TrySetResult());
            betaEntered.TrySetResult();
            return await betaRelease.Task;
        });
        var gamma = Available("gamma");
        var orchestrator = new AssessmentOrchestrator(store, [alpha, beta, gamma], clock, maximumConcurrency: 2);
        var hookCalls = 0;
        async Task FinishBetaDuringAwait()
        {
            Interlocked.Increment(ref hookCalls);
            clock.FireTimeout();
            await betaCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            betaRelease.TrySetResult(ProviderReadResult.CleanupIncomplete());
            await orchestrator.CurrentOperationsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        if (duringGateCheck)
        { store.BeforeGateCheckAsync = check => check == 3 ? FinishBetaDuringAwait() : Task.CompletedTask; }
        else
        { store.AfterCommitAsync = batch => Contains(batch, "test.alpha.value") ? FinishBetaDuringAwait() : Task.CompletedTask; }
        var pending = orchestrator.AssessAsync(store.JobId);
        try
        {
            await alphaEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await betaEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await clock.TwoTimersCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            alphaRelease.TrySetResult(Collected("alpha"));
            var snapshot = await pending.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, Volatile.Read(ref hookCalls));
            Assert.Equal("RequiresReview", snapshot.Outcome.ToString());
            Assert.Equal(ProviderRunOutcome.TimedOut, Assert.Single(snapshot.Providers,
                static provider => provider.ProviderId.Value == "provider-beta").Outcome);
            Assert.Null(Assert.Single(snapshot.Providers, static provider => provider.ProviderId.Value == "provider-gamma").Outcome);
            Assert.Equal(0, gamma.InvocationCount);
            await orchestrator.CleanupCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            var error = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(store.JobId));
            Assert.Equal("assessment_cleanup_incomplete", error.ResultCode);
        }
        finally
        {
            alphaRelease.TrySetResult(Collected("alpha"));
            betaRelease.TrySetResult(ProviderReadResult.CleanupIncomplete());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureIsLocalAndCannotExposePrivateExceptionOrPassItsRequirement(bool denied)
    {
        await using var store = new AssessmentTestStore();
        const string canary = "SYNTHETIC-PRIVATE-DEVICE-OWNER-PATH";
        var failed = new FakeInventoryProvider(Descriptor("alpha"), _ => denied
            ? ValueTask.FromResult(ProviderReadResult.AccessDenied())
            : throw new InvalidOperationException(canary));
        var healthy = Available("beta");

        var snapshot = await new AssessmentOrchestrator(store, [failed, healthy]).AssessAsync(store.JobId);

        Assert.Equal("RequiresReview", snapshot.Outcome.ToString());
        Assert.Null(Fact(snapshot, "test.alpha.value").Value);
        Assert.True(Fact(snapshot, "test.beta.value").Value!.BooleanValue);
        Assert.Equal(denied ? ProviderRunOutcome.AccessDenied : ProviderRunOutcome.Failed,
            Assert.Single(snapshot.Providers, static value => value.ProviderId.Value == "provider-alpha").Outcome);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(store.Committed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterruptedProviderCommitLeavesEarlierBatchDurableAndRerunFresh()
    {
        await using var store = new AssessmentTestStore
        {
            Fault = AssessmentStoreFault.BeforeCommit,
            FaultSelector = static batch => Contains(batch, "test.beta.value"),
        };
        var orchestrator = new AssessmentOrchestrator(store, [Available("alpha"), Available("beta")]);

        var error = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(store.JobId));

        Assert.Equal("assessment_store_unavailable", error.ResultCode);
        Assert.NotNull(error.AssessmentId);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", error.ToString(), StringComparison.Ordinal);
        var partial = Assert.IsType<AssessmentSnapshot>(await orchestrator.LoadAsync(store.JobId, error.AssessmentId!));
        Assert.Equal("Incomplete", partial.Outcome.ToString());
        Assert.True(Fact(partial, "test.alpha.value").Value!.BooleanValue);
        Assert.DoesNotContain(partial.Observations, static value => value.EvidenceKey.Value == "test.beta.value");
        var retry = await orchestrator.AssessAsync(store.JobId);
        Assert.NotEqual(partial.AssessmentId, retry.AssessmentId);
        Assert.Equal("Completed", retry.Outcome.ToString());
    }

    [Fact]
    public async Task CommitThenLostAcknowledgementIsReconciledWithoutDuplicateWrites()
    {
        await using var store = new AssessmentTestStore
        {
            Fault = AssessmentStoreFault.AfterCommit,
            FaultSelector = static batch => Contains(batch, "test.alpha.value"),
        };
        var snapshot = await new AssessmentOrchestrator(store, [Available("alpha"), Available("beta")]).AssessAsync(store.JobId);

        Assert.Equal("Completed", snapshot.Outcome.ToString());
        Assert.Single(store.Committed, static batch => Contains(batch, "test.alpha.value"));
        var persisted = Assert.IsType<StoredJob>(await store.LoadJobAsync(store.JobId));
        Assert.Equal(persisted.Observations.Count, persisted.Observations.Select(static value => value.Metadata.EvidenceId).Distinct().Count());
    }

    [Fact]
    public async Task SameIdsWithDifferentCommittedContentCannotCountAsSuccessfulReconciliation()
    {
        await using var store = new AssessmentTestStore
        {
            Fault = AssessmentStoreFault.MismatchedCommit,
            FaultSelector = static batch => Contains(batch, "test.alpha.value"),
        };
        var beta = Available("beta");

        var error = await Assert.ThrowsAsync<AssessmentException>(() =>
            new AssessmentOrchestrator(store, [Available("alpha"), beta]).AssessAsync(store.JobId));

        Assert.Equal("assessment_store_unavailable", error.ResultCode);
        Assert.NotNull(error.AssessmentId);
        Assert.Equal(0, beta.InvocationCount);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GateChangeDuringCollectionPreventsItsResultCommit()
    {
        await using var store = new AssessmentTestStore();
        var alpha = new FakeInventoryProvider(Descriptor("alpha"), _ =>
        {
            store.ChangeSanitization(SanitizationState.Failed, recordDecision: true);
            return ValueTask.FromResult(Collected("alpha"));
        });
        var beta = Available("beta");

        var error = await Assert.ThrowsAsync<AssessmentException>(() =>
            new AssessmentOrchestrator(store, [alpha, beta]).AssessAsync(store.JobId));

        Assert.Contains(error.ResultCode, GateFailureCodes);
        Assert.DoesNotContain(store.Committed, static batch => Contains(batch, "test.alpha.value"));
        Assert.Equal(0, beta.InvocationCount);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewAllowingGateAfterFirstCommitStillRequiresAssessmentRestartBeforeNextDispatch()
    {
        await using var store = new AssessmentTestStore();
        store.AfterCommit = batch =>
        {
            if (Contains(batch, "test.alpha.value"))
            { store.ChangeSanitization(SanitizationState.Verified, recordDecision: true); }
        };
        var beta = Available("beta");

        var error = await Assert.ThrowsAsync<AssessmentException>(() =>
            new AssessmentOrchestrator(store, [Available("alpha"), beta]).AssessAsync(store.JobId));

        Assert.Equal("assessment_gate_unverified", error.ResultCode);
        Assert.NotNull(error.AssessmentId);
        Assert.Equal(0, beta.InvocationCount);
        Assert.Single(store.Committed, static batch => Contains(batch, "test.alpha.value"));
    }

    [Fact]
    public async Task RepresentativeAssessmentRecordsBoundedResourceMetrics()
    {
        const int providerCount = 16;
        const int factsPerProvider = 63;
        foreach (var concurrency in new[] { 1, 4 })
        {
            await using var store = new AssessmentTestStore();
            var providers = Enumerable.Range(0, providerCount).Select(index =>
            {
                var name = "resource" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                var definitions = Enumerable.Range(0, factsPerProvider).Select(fact =>
                    new ProviderEvidenceDefinition(new("test." + name + ".value" + fact.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)),
                        EvidenceValueKind.Boolean, null, "source." + name)).ToArray();
                var descriptor = new InventoryProviderDescriptor(new("provider-" + name), ProviderPrivilegeRequirement.StandardUser,
                    TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15), ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture,
                    [ProviderOperatingSystem.Windows11], new(new("test." + name + ".failure"), "source." + name + ".failure"), definitions);
                return new FakeInventoryProvider(descriptor, _ => ValueTask.FromResult(ProviderReadResult.Collected(
                    definitions.Select(static definition => NormalizedEvidence.Observed(definition.EvidenceKey, EvidenceValue.FromBoolean(true))))));
            }).ToArray();
            var orchestrator = new AssessmentOrchestrator(store, providers, new FixedTimeProvider(Now), concurrency);
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes();
            var started = Stopwatch.GetTimestamp();

            var snapshot = await orchestrator.AssessAsync(store.JobId);

            var elapsed = Stopwatch.GetElapsedTime(started);
            Assert.Equal("Completed", snapshot.Outcome.ToString());
            Assert.Equal(providerCount * factsPerProvider, snapshot.Observations.Count);
            Assert.All(snapshot.Providers, static provider => Assert.Equal(factsPerProvider, provider.Observations.Count));
            Assert.All(providers, static provider => Assert.Equal(1, provider.InvocationCount));
            Assert.All(store.Committed, static batch => Assert.InRange(batch.Observations.Count, 1, JobEvidenceBatch.MaximumRecordsPerBatch));
            Assert.Equal(providerCount, store.Committed.Count(static batch => batch.Observations.Any(static fact => fact.EvidenceKey.Value.StartsWith("test.resource", StringComparison.Ordinal))));
            var stored = Assert.IsType<StoredJob>(await store.LoadJobAsync(store.JobId));
            var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot.Observations).Length;
            process.Refresh();
            output.WriteLine(JsonSerializer.Serialize(new
            {
                task = "TL-0112",
                workload = "synthetic_16_providers_63_boolean_facts_v1",
                providerCount,
                factsPerProvider,
                concurrency,
                outcome = snapshot.Outcome.ToString(),
                durationMilliseconds = elapsed.TotalMilliseconds,
                processCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                processAllocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
                processWorkingSetBytes = process.WorkingSet64,
                normalizedObservationCount = snapshot.Observations.Count,
                storedObservationCount = stored.Observations.Count,
                serializedNormalizedBytes = serializedBytes,
                limitation = "In-memory synthetic store; database growth unmeasured. Fixture performs no filesystem, temporary-file, cache or network operations. Process metrics include test-host activity; no cross-hardware claim.",
            }));
        }
    }

    private static bool Contains(JobEvidenceBatch batch, string key) => batch.Observations.Any(fact => fact.EvidenceKey.Value == key);
    private static Observation Fact(AssessmentSnapshot snapshot, string key) => Assert.Single(snapshot.Observations, fact => fact.EvidenceKey.Value == key);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static NormalizedEvidence LifecycleText(string suffix, string value) =>
        NormalizedEvidence.Inferred(new("os.lifecycle." + suffix), EvidenceValue.FromString(value));

    private static FakeInventoryProvider Available(string name) => new(Descriptor(name), _ => ValueTask.FromResult(Collected(name)));
    private static FakeInventoryProvider Unknown(string name, ProviderLimitation limitation) => new(Descriptor(name),
        _ => ValueTask.FromResult(ProviderReadResult.Collected([NormalizedEvidence.NotAvailable(new("test." + name + ".value"), limitation)])));
    private static ProviderReadResult Collected(string name) => ProviderReadResult.Collected(
        [NormalizedEvidence.Observed(new("test." + name + ".value"), EvidenceValue.FromBoolean(true))]);

    private static InventoryProviderDescriptor Descriptor(string name, string? key = null, string? failureKey = null,
        ProviderPrivilegeRequirement privilege = ProviderPrivilegeRequirement.StandardUser) =>
        new(new("provider-" + name), privilege, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15),
            ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
            new(new(failureKey ?? "test." + name + ".failure"), "source." + name + ".failure"),
            [new(new(key ?? "test." + name + ".value"), EvidenceValueKind.Boolean, null, "source." + name)]);

    private static string Semantic(AssessmentSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        outcome = snapshot.Outcome.ToString(),
        providers = snapshot.Providers.Select(static provider => new { id = provider.ProviderId.Value, outcome = provider.Outcome }),
        values = snapshot.Observations.Select(static fact => new
        {
            key = fact.EvidenceKey.Value,
            provider = fact.Metadata.ProviderId.Value,
            fact.Metadata.Provenance,
            fact.Metadata.CollectedAtUtc,
            fact.Metadata.ValueAvailability,
            fact.Metadata.EvidenceClassification,
            fact.Metadata.PrivacyClassification,
            fact.Value,
            fact.Unit,
            fact.LimitationCode,
        }),
    });

    private static IEnumerable<int[]> Permutations(int[] input)
    {
        if (input.Length == 0) { yield return []; yield break; }
        foreach (var first in input)
        {
            foreach (var remaining in Permutations(input.Where(value => value != first).ToArray()))
            { yield return new[] { first }.Concat(remaining).ToArray(); }
        }
    }

    private sealed class ManualClock : TimeProvider, IDisposable
    {
        private ManualTimer? _timer;
        private int _timerCount;
        public TaskCompletionSource TimerCreated { get; } = Signal();
        public TaskCompletionSource TwoTimersCreated { get; } = Signal();
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(15), dueTime);
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            _timer = new(callback, state);
            TimerCreated.TrySetResult();
            if (Interlocked.Increment(ref _timerCount) == 2) { TwoTimersCreated.TrySetResult(); }
            return _timer;
        }
        public void FireTimeout() => _timer!.Fire();
        public void Dispose() => _timer?.Dispose();
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;
        public void Fire() { if (Volatile.Read(ref _disposed) == 0) { callback(state); } }
        public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _disposed) == 0;
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
