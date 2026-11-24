using System.IO;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.ManualTests;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Tests;

public sealed class AuditWorkflowTests
{
    private static readonly DateTimeOffset SampleTime = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly SampleDate = DateOnly.FromDateTime(SampleTime.UtcDateTime);
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);
    private const string KeyboardCase = "MHT-002";
    private const string OperatorCode = "fixture-operator";
    private const string PrivateFailureSample = "fixture exception detail";

    [Fact]
    public async Task CreateIntakeAssessmentAndFixtureManualHistorySurviveClosingAndReopeningTheStore()
    {
        using var disk = new AuditTestStore();
        var provider = AuditTestProvider.Available("presence");
        var first = disk.OpenWorkspace([provider]);
        string jobId;
        AssessmentId assessmentId;
        try
        {
            await first.CreateJobAsync();
            var created = Assert.IsType<StoredJob>(first.Job);
            jobId = first.JobId;
            Assert.Equal(created.Job.JobId.Value, jobId);
            Assert.Empty(created.SanitizationEvidence);
            Assert.Empty(created.HumanTests);
            Assert.False(first.CanAssess);

            await first.RecordSanitizationAsync(VerifiedSample());
            Assert.True(first.IsFixtureScenario);
            Assert.True(first.CanAssess);
            Assert.Equal(ProvenanceKind.SyntheticFixture,
                Assert.Single(Assert.IsType<StoredJob>(first.Job).SanitizationEvidence).Metadata.Provenance.Kind);

            await first.StartAssessmentAsync();
            var assessed = Assert.IsType<AssessmentSnapshot>(first.Assessment);
            Assert.Equal(AssessmentOutcome.Completed, assessed.Outcome);
            assessmentId = assessed.AssessmentId;
            Assert.Single(assessed.Observations);
            Assert.Empty(Assert.IsType<StoredJob>(first.Job).HumanTests);
            Assert.Contains("readiness", first.AssessmentStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, provider.InvocationCount);

            await first.RecordManualAsync(KeyboardCase, HumanTestResult.NotRun, OperatorCode, "Deferred", null);
            await first.RecordManualAsync(KeyboardCase, HumanTestResult.NotAvailable, OperatorCode, "Equipment absent",
                ManualTestUnavailableReason.EquipmentMissing);
            await first.RecordManualAsync(KeyboardCase, HumanTestResult.Fail, OperatorCode, "Key failed", null);
            await first.RecordManualAsync(KeyboardCase, HumanTestResult.Pass, OperatorCode, "Retest passed", null);
            Assert.False(Assert.IsType<ManualTestProgress>(first.ManualProgress).HasEssentialFailure);
            first.CloseJob();
            Assert.Null(first.Job);
        }
        finally { await first.CloseAsync(); }

        var reopened = disk.OpenWorkspace([provider]);
        try
        {
            await reopened.OpenJobAsync(jobId);
            Assert.Equal(jobId, reopened.JobId);
            Assert.True(reopened.CanAssess);
            Assert.Equal(assessmentId, Assert.IsType<AssessmentSnapshot>(reopened.Assessment).AssessmentId);
            Assert.Equal(1, provider.InvocationCount);
            var manualHistory = Assert.IsType<StoredJob>(reopened.Job).HumanTests;
            Assert.All(manualHistory, record => Assert.Equal(ProvenanceKind.SyntheticFixture, record.Metadata.Provenance.Kind));
            Assert.Collection(manualHistory,
                deferred => Assert.Equal(HumanTestResult.NotRun, deferred.TestResult),
                unavailable =>
                {
                    Assert.Equal(HumanTestResult.NotAvailable, unavailable.TestResult);
                    Assert.Equal("equipment_missing", unavailable.LimitationCode);
                });
        }
        finally { await reopened.CloseAsync(); }
    }

    [Fact]
    public async Task MissingAndFailedSanitizationRecordNoAssessmentAndInvokeNoProviders()
    {
        using var disk = new AuditTestStore();
        var provider = AuditTestProvider.Available("gate");
        var workspace = disk.OpenWorkspace([provider]);
        try
        {
            await workspace.CreateJobAsync();
            Assert.False(workspace.CanAssess);
            await workspace.StartAssessmentAsync();
            Assert.Null(workspace.Assessment);
            Assert.Equal(0, provider.InvocationCount);

            await workspace.RecordSanitizationAsync(VerifiedSample() with { State = SanitizationState.Failed });
            var job = Assert.IsType<StoredJob>(workspace.Job);
            Assert.Equal(SanitizationState.Failed, Assert.Single(job.SanitizationEvidence).SanitizationState);
            Assert.False(workspace.CanAssess);
            Assert.False(SanitizationGate.Inspect(job).AllowsAssessment);
            Assert.Contains("sanitiz", workspace.GateStatus, StringComparison.OrdinalIgnoreCase);

            await workspace.StartAssessmentAsync();
            Assert.Null(workspace.Assessment);
            Assert.Empty(Assert.IsType<StoredJob>(workspace.Job).Observations);
            Assert.Equal(0, provider.InvocationCount);
        }
        finally { await workspace.CloseAsync(); }
    }

    [Fact]
    public async Task NormalIntakeRequiresAnAttributableExternalRecordBeforeAllowingAssessment()
    {
        using var disk = new AuditTestStore();
        var provider = AuditTestProvider.Available("live-sentinel", ProviderEvidenceOrigin.ActiveMachine);
        var normal = disk.OpenWorkspace([provider]);
        try
        {
            await normal.CreateJobAsync();
            await normal.RecordSanitizationAsync(VerifiedSample() with { ConfirmedExternalRecord = false });
            Assert.False(normal.CanAssess);
            Assert.Empty(Assert.IsType<StoredJob>(normal.Job).SanitizationEvidence);

            await normal.RecordSanitizationAsync(VerifiedSample());
            var job = Assert.IsType<StoredJob>(normal.Job);
            var record = Assert.Single(job.SanitizationEvidence);
            Assert.Equal(EvidenceClassification.HumanConfirmed, record.Metadata.EvidenceClassification);
            Assert.Equal(ProvenanceKind.HumanConfirmation, record.Metadata.Provenance.Kind);
            Assert.Equal(OperatorCode, record.OperatorId?.Value);
            Assert.Equal("clear", record.MethodCode);
            Assert.NotNull(record.OccurredAtUtc);
            Assert.Equal("fixture-media", record.MediaIdentifier?.Value);
            Assert.True(SanitizationGate.Inspect(job).AllowsAssessment);
            Assert.True(normal.CanAssess);
            Assert.Equal(0, provider.InvocationCount);
        }
        finally { await normal.CloseAsync(); }
    }

    [Fact]
    public async Task AttributableManualRetestPreservesTheEssentialFailureAndItsHistory()
    {
        using var disk = new AuditTestStore();
        var provider = AuditTestProvider.Available("live-sentinel", ProviderEvidenceOrigin.ActiveMachine);
        var normal = disk.OpenWorkspace([provider]);
        try
        {
            await normal.CreateJobAsync();
            await normal.RecordSanitizationAsync(VerifiedSample());
            await normal.RecordManualAsync(KeyboardCase, HumanTestResult.Fail, OperatorCode, "Key failed", null);
            Assert.True(Assert.IsType<ManualTestProgress>(normal.ManualProgress).HasEssentialFailure);
            await normal.RecordManualAsync(KeyboardCase, HumanTestResult.Pass, OperatorCode, "Retest passed", null);

            var jobId = normal.JobId;
            normal.CloseJob();
            await normal.OpenJobAsync(jobId);
            var progress = Assert.IsType<ManualTestProgress>(normal.ManualProgress);
            var keyboard = Assert.Single(progress.Items, static item => item.Case.Id == KeyboardCase);
            Assert.True(keyboard.HasBlockerCandidate);
            Assert.Collection(keyboard.History,
                failed =>
                {
                    Assert.Equal(HumanTestResult.Fail, failed.TestResult);
                    Assert.Equal("Key failed", failed.Details?.Notes);
                    Assert.Equal(OperatorCode, failed.OperatorId.Value);
                    Assert.Equal(SampleTime, failed.Metadata.CollectedAtUtc);
                },
                passed =>
                {
                    Assert.Equal(HumanTestResult.Pass, passed.TestResult);
                    Assert.Equal(EvidenceClassification.HumanConfirmed, passed.Metadata.EvidenceClassification);
                });
            Assert.Equal(keyboard.History[0].HumanTestId, keyboard.History[1].Details!.RetestOf);
            Assert.Equal(0, provider.InvocationCount);
        }
        finally { await normal.CloseAsync(); }
    }

    [Fact]
    public async Task ProductionProviderRosterComposesButAnUnsanitizedJobCannotStartIt()
    {
        using var disk = new AuditTestStore();
        var workspace = AuditWorkspace.CreateLive(
            async cancellationToken => await disk.OpenSqliteAsync(cancellationToken).ConfigureAwait(false));
        try
        {
            await workspace.CreateJobAsync();
            Assert.False(workspace.IsFixtureScenario);
            Assert.False(workspace.CanAssess);
            await workspace.StartAssessmentAsync();
            Assert.Empty(Assert.IsType<StoredJob>(workspace.Job).Observations);
            Assert.Null(workspace.Assessment);
            Assert.Contains("sanitization", workspace.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally { await workspace.CloseAsync(); }
    }

    [Fact]
    public async Task AnAllowingFixtureGateCannotStartAnActiveMachineProvider()
    {
        using var disk = new AuditTestStore();
        var fixture = disk.OpenWorkspace([AuditTestProvider.Available("fixture")]);
        string jobId;
        try
        {
            await fixture.CreateJobAsync();
            await fixture.RecordSanitizationAsync(VerifiedSample());
            Assert.True(fixture.CanAssess);
            jobId = fixture.JobId;
        }
        finally { await fixture.CloseAsync(); }

        // This input has active-machine provenance to exercise the production admission rule.
        // Its read is a sentinel and does not access hardware even if the rule regresses.
        var live = AuditTestProvider.Available("live", ProviderEvidenceOrigin.ActiveMachine);
        var normal = disk.OpenWorkspace([live]);
        try
        {
            await normal.OpenJobAsync(jobId);
            Assert.False(normal.IsFixtureScenario);
            Assert.False(normal.CanEditJob);
            Assert.False(normal.CanAssess);
            await normal.RecordSanitizationAsync(VerifiedSample());
            await normal.StartAssessmentAsync();
            Assert.Equal(0, live.InvocationCount);
            Assert.Empty(Assert.IsType<StoredJob>(normal.Job).Observations);
        }
        finally { await normal.CloseAsync(); }

        await using var store = await disk.OpenSqliteAsync();
        var orchestrator = new AssessmentOrchestrator(store, [live]);
        var blocked = await Assert.ThrowsAsync<AssessmentException>(() => orchestrator.AssessAsync(new JobId(jobId)));
        Assert.Equal("assessment_fixture_gate_blocked", blocked.ResultCode);
        Assert.Equal(0, live.InvocationCount);
        var stored = Assert.IsType<StoredJob>(await store.LoadJobAsync(new JobId(jobId)));
        Assert.True(SanitizationGate.Inspect(stored).AllowsAssessment);
        Assert.Equal(ProvenanceKind.SyntheticFixture, Assert.Single(stored.SanitizationEvidence).Metadata.Provenance.Kind);
        Assert.Empty(stored.Observations);
    }

    [Theory]
    [InlineData(false, ProviderRunOutcome.Failed)]
    [InlineData(true, ProviderRunOutcome.TimedOut)]
    public async Task ProviderExceptionAndTimeoutRemainUnavailableAndRequireReview(bool timeOut, ProviderRunOutcome expected)
    {
        using var disk = new AuditTestStore();
        using var clock = new FixtureClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new AuditTestProvider("unavailable", cancellationToken =>
        {
            entered.TrySetResult();
            return timeOut
                ? WaitForCancellationAsync(cancellationToken)
                : throw new InvalidOperationException(PrivateFailureSample);
        });
        var workspace = disk.OpenWorkspace([provider], clock);
        try
        {
            await workspace.CreateJobAsync();
            await workspace.RecordSanitizationAsync(VerifiedSample());
            var run = workspace.StartAssessmentAsync();
            if (timeOut)
            {
                await entered.Task.WaitAsync(TestWait);
                var timeout = await clock.ProviderTimeout.Task.WaitAsync(TestWait);
                timeout.Fire();
            }

            await run.WaitAsync(TestWait);

            var result = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
            Assert.Equal(AssessmentOutcome.RequiresReview, result.Outcome);
            Assert.Equal(expected, Assert.Single(result.Providers).Outcome);
            var unavailable = Assert.Single(result.Observations,
                static observation => observation.EvidenceKey.Value.EndsWith(".value", StringComparison.Ordinal));
            Assert.Equal(EvidenceClassification.NotAvailable, unavailable.Metadata.EvidenceClassification);
            Assert.Null(unavailable.Value);
            Assert.Contains("review", workspace.AssessmentStatus, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PrivateFailureSample, workspace.Status, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateFailureSample, workspace.AssessmentStatus, StringComparison.Ordinal);
            Assert.DoesNotContain(workspace.Findings, static finding => finding.Value.Contains(PrivateFailureSample, StringComparison.Ordinal));
        }
        finally { await workspace.CloseAsync(); }
    }

    [Fact]
    public async Task CancelNotifiesRequestAndKeepsCompletedPartialEvidenceAfterReopen()
    {
        using var disk = new AuditTestStore();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = AuditTestProvider.Available("completed");
        var waiting = new AuditTestProvider("waiting", token =>
        {
            entered.TrySetResult();
            return WaitForCancellationAsync(token);
        });
        var workspace = disk.OpenWorkspace([completed, waiting]);
        var statuses = new List<string>();
        workspace.PropertyChanged += (_, change) =>
        {
            if (string.IsNullOrEmpty(change.PropertyName) || change.PropertyName == nameof(AuditWorkspace.AssessmentStatus))
            {
                lock (statuses) { statuses.Add(workspace.AssessmentStatus); }
            }
        };

        string jobId;
        AssessmentId assessmentId;
        try
        {
            await workspace.CreateJobAsync();
            jobId = workspace.JobId;
            await workspace.RecordSanitizationAsync(VerifiedSample());
            var run = workspace.StartAssessmentAsync();
            await entered.Task.WaitAsync(TestWait);
            Assert.True(workspace.IsAssessmentRunning);
            Assert.True(workspace.CanCancel);
            Assert.False(run.IsCompleted);

            workspace.CancelAssessment();
            await run.WaitAsync(TestWait);
            Assert.Contains(statuses, static status => status.Contains("request", StringComparison.OrdinalIgnoreCase));
            Assert.False(workspace.CanCancel);
            var cancelled = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
            assessmentId = cancelled.AssessmentId;
            Assert.Equal(AssessmentOutcome.Cancelled, cancelled.Outcome);
            Assert.Equal(ProviderRunOutcome.Completed, cancelled.Providers[0].Outcome);
            Assert.Equal(ProviderRunOutcome.Cancelled, cancelled.Providers[1].Outcome);
        }
        finally { await workspace.CloseAsync(); }

        var reopened = disk.OpenWorkspace([completed, waiting]);
        try
        {
            await reopened.OpenJobAsync(jobId);
            var partial = Assert.IsType<AssessmentSnapshot>(reopened.Assessment);
            Assert.Equal(assessmentId, partial.AssessmentId);
            Assert.Equal(AssessmentOutcome.Cancelled, partial.Outcome);
            Assert.Equal(ValueAvailability.Available,
                Assert.Single(partial.Providers[0].Observations).Metadata.ValueAvailability);
            Assert.Equal(1, completed.InvocationCount);
            Assert.Equal(1, waiting.InvocationCount);
        }
        finally { await reopened.CloseAsync(); }
    }

    [Fact]
    public async Task RestartShowsAnInterruptedAssessmentAndDoesNotRunTheProviderAgain()
    {
        using var disk = new AuditTestStore();
        var provider = AuditTestProvider.Available("before-interruption");
        var interrupted = new AuditWorkspace(
            async cancellationToken => new InterruptedAssessmentStore(await disk.OpenSqliteAsync(cancellationToken).ConfigureAwait(false)),
            [provider], new FixedClock());
        string jobId;
        try
        {
            await interrupted.CreateJobAsync();
            jobId = interrupted.JobId;
            await interrupted.RecordSanitizationAsync(VerifiedSample());
            await interrupted.StartAssessmentAsync();
            Assert.Equal(1, provider.InvocationCount);
        }
        finally { await interrupted.CloseAsync(); }

        var restarted = disk.OpenWorkspace([provider]);
        try
        {
            await restarted.OpenJobAsync(jobId);
            var durable = Assert.IsType<AssessmentSnapshot>(restarted.Assessment);
            Assert.Equal(AssessmentOutcome.Incomplete, durable.Outcome);
            Assert.Null(durable.FinishedAtUtc);
            Assert.Single(durable.Observations);
            Assert.Equal(1, provider.InvocationCount);
        }
        finally { await restarted.CloseAsync(); }
    }

    internal static SanitizationInput VerifiedSample() =>
        new(SanitizationState.Verified, OperatorCode, SampleDate, "fixture-media", ConfirmedExternalRecord: true, MethodCode: "clear");

    internal static async ValueTask<ProviderReadResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return ProviderReadResult.Unavailable();
    }

    private sealed class FixtureClock : TimeProvider, IDisposable
    {
        public TaskCompletionSource<FixtureTimer> ProviderTimeout { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow() => SampleTime;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FixtureTimer(callback, state);
            Assert.True(ProviderTimeout.TrySetResult(timer), "Only the waiting fixture provider should create a timer.");
            return timer;
        }

        public void Dispose()
        {
            if (ProviderTimeout.Task.IsCompletedSuccessfully) { ProviderTimeout.Task.Result.Dispose(); }
        }
    }

    private sealed class FixtureTimer(TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;

        public void Fire()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { callback(state); }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _disposed) == 0;

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class AuditTestStore : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ThirdLife-TL0114-");

        internal string Root => _directory.FullName;

        public Task<SqliteJobStore> OpenSqliteAsync(CancellationToken cancellationToken = default) =>
            SqliteJobStore.OpenAsync(Path.Combine(Root, "store"), cancellationToken);

        public AuditWorkspace OpenWorkspace(IReadOnlyList<IInventoryProvider> providers, TimeProvider? timeProvider = null) =>
            new(async cancellationToken => await OpenSqliteAsync(cancellationToken).ConfigureAwait(false),
                providers, timeProvider ?? new FixedClock());

        public void Dispose() => _directory.Delete(recursive: true);
    }

    internal sealed class AuditTestProvider(
        string name,
        Func<CancellationToken, ValueTask<ProviderReadResult>> observe,
        ProviderEvidenceOrigin origin = ProviderEvidenceOrigin.SyntheticFixture) : IInventoryProvider
    {
        private int _calls;

        public int InvocationCount => Volatile.Read(ref _calls);

        public InventoryProviderDescriptor Descriptor { get; } = new(new("fixture-" + name),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(15),
            ProviderNetworkUse.None, origin, [ProviderOperatingSystem.Windows11],
            new(new("test." + name + ".failure"), "fixture." + name + ".failure"),
            [new(new("test." + name + ".value"), EvidenceValueKind.Boolean, null, "fixture." + name)]);

        public static AuditTestProvider Available(string name, ProviderEvidenceOrigin origin = ProviderEvidenceOrigin.SyntheticFixture) =>
            new(name, _ => ValueTask.FromResult(ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(new("test." + name + ".value"), EvidenceValue.FromBoolean(true))])), origin);

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return observe(cancellationToken);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => SampleTime;
    }

    private sealed class InterruptedAssessmentStore(IJobStore inner) : IJobStore
    {
        public Task CreateJobAsync(Job job, CancellationToken cancellationToken = default) =>
            inner.CreateJobAsync(job, cancellationToken);

        public Task AppendEvidenceAsync(JobEvidenceBatch batch, CancellationToken cancellationToken = default)
        {
            if (batch.Observations.Any(static item => item.EvidenceKey.Value == "assessment.finish"))
            {
                throw new IOException("Synthetic interruption before the assessment finish record.");
            }

            return inner.AppendEvidenceAsync(batch, cancellationToken);
        }

        public Task SetArchiveStateAsync(JobId jobId, bool isArchived, DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken = default) =>
            inner.SetArchiveStateAsync(jobId, isArchived, changedAtUtc, cancellationToken);

        public Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(SanitizationGateDecision candidate,
            CancellationToken cancellationToken = default) =>
            inner.RecordSanitizationGateDecisionAsync(candidate, cancellationToken);

        public Task<StoredJob?> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default) =>
            inner.LoadJobAsync(jobId, cancellationToken);

        public Task RequireSanitizationGateAsync(JobId jobId, SanitizationGateDecisionId expectedDecisionId,
            CancellationToken cancellationToken = default) =>
            inner.RequireSanitizationGateAsync(jobId, expectedDecisionId, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
