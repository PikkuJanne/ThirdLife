using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Persistence.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateWorkflowTests
{
    private static readonly FixedTimeProvider Clock = new(PersistenceTestData.Timestamp.AddMinutes(5));
    private static readonly WindowsUpdateCandidate Update = new(new(Guid.Parse("86d299a0-7c08-4cf9-b1d1-bea1f083dd56"), 4),
        WindowsUpdateClass.Security, 1_024);
    private static readonly OperatorId Operator = new("operator-synthetic-workshop");

    [Theory]
    [InlineData(WindowsUpdateStatus.Offline)]
    [InlineData(WindowsUpdateStatus.ServiceUnavailable)]
    [InlineData(WindowsUpdateStatus.Cancelled)]
    public async Task MigrationAndScanFailureRetainPreviousInventoryAfterReopen(WindowsUpdateStatus failure)
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision gate;
        await using (var old = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 6, Clock, null))
        {
            (job, gate, _) = await SeedAsync(old);
        }

        await Assert.ThrowsAsync<InjectedPersistenceException>(() => SqliteJobStore.OpenForTestingAsync(
            workspace.StoreRoot, 7, Clock, new ThrowingFaultInjector(JobStoreFaultPoint.BeforeMigrationCommit, 7)));
        var provider = new FixtureAgent { Scan = Available(Update) };
        await using (var store = await OpenAsync(workspace))
        {
            var workflow = new WindowsUpdateWorkflow(store, provider, Clock);
            await workflow.ScanAsync(job.JobId, gate.DecisionId);
            provider.Scan = new(failure, [], null);
            await workflow.ScanAsync(job.JobId, gate.DecisionId);
        }

        await using var reopened = await OpenAsync(workspace);
        var history = await reopened.LoadWindowsUpdatesAsync(job.JobId);
        Assert.Equal(4, history.Length);
        Assert.Equal(Update, Assert.Single(history[1].Scan!.Updates));
        Assert.Equal(failure, history[^1].Scan!.Status);
        Assert.Equal(WindowsUpdateDisposition.NotCurrent, WindowsUpdateHistory.Disposition(history));
        Assert.Empty(provider.Mutations);
    }

    [Fact]
    public async Task StaleGateOrMissingQualificationCannotStartProviderWork()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace);
        var (job, gate, policy) = await SeedAsync(store);
        var provider = new FixtureAgent();
        var workflow = new WindowsUpdateWorkflow(store, provider, Clock);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => workflow.ScanAsync(job.JobId, new("stale-gate")));
        Assert.Equal(0, provider.Scans);
        var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy));
        var controlled = Controlled(store, provider);
        await Assert.ThrowsAsync<JobStoreConflictException>(() => controlled.ApproveAsync(job.JobId, gate.DecisionId, scan.Id,
            new(Update.Identity, WindowsUpdateClass.Driver), Operator, policy));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => controlled.ApproveAsync(job.JobId, gate.DecisionId, scan.Id,
            Update, Operator, "policy-decision-00000000000000000000000000000000"));
        await store.SetArchiveStateAsync(job.JobId, true, Clock.GetUtcNow());
        await Assert.ThrowsAsync<JobStoreConflictException>(() =>
            controlled.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy));
        Assert.Empty(provider.Mutations);
    }

    [Fact]
    public async Task ThreeExactPassesRequireFreshReviewAndStopWhileFurtherUpdatesApply()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace);
        var (job, gate, policy) = await SeedAsync(store);
        var provider = new FixtureAgent();
        var workflow = Controlled(store, provider);
        var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
        for (var pass = 0; pass < WindowsUpdatePolicy.MaximumPasses; pass++)
        {
            var exact = Assert.Single(scan.Scan!.Updates);
            var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, exact, Operator, policy);
            provider.AfterInstall = () => provider.Scan = Available(new WindowsUpdateCandidate(new(exact.Identity.UpdateId,
                exact.Identity.Revision + 1), WindowsUpdateClass.Critical, 2_048));
            scan = await workflow.ExecuteAsync(job.JobId, approval.Id);
            Assert.Equal(WindowsUpdateStage.ScanCompleted, scan.Stage);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(job.JobId, approval.Id));
        }

        var history = await store.LoadWindowsUpdatesAsync(job.JobId);
        Assert.Equal(WindowsUpdateDisposition.PassLimitReached, WindowsUpdateHistory.Disposition(history));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => workflow.ApproveAsync(job.JobId, gate.DecisionId,
            scan.Id, scan.Scan!.Updates.Single(), Operator, policy));
        Assert.Equal(6, provider.Mutations.Count);
        Assert.All(history.Where(e => e.Stage == WindowsUpdateStage.InstallCompleted), e => Assert.Equal(100, e.Operation!.ProgressPercent));
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("class")]
    [InlineData("resources")]
    [InlineData("download")]
    [InlineData("install")]
    [InlineData("uncertain")]
    [InlineData("empty")]
    public async Task QualifiedFixtureRechecksExactScopeAndRetainsFailureOrUncertainState(string scenario)
    {
        using var workspace = new PersistenceTestWorkspace();
        var provider = new FixtureAgent();
        Job job;
        SanitizationGateDecision gate;
        await using (var store = await OpenAsync(workspace))
        {
            string policy;
            (job, gate, policy) = await SeedAsync(store);
            var workflow = Controlled(store, provider, (_, _) => Task.FromResult(scenario != "resources"));
            var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
            var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
            switch (scenario)
            {
                case "revision": provider.Scan = Available(new WindowsUpdateCandidate(new(Update.Identity.UpdateId, 5), Update.Classification)); break;
                case "class": provider.AfterDownload = () => provider.Scan = Available(new WindowsUpdateCandidate(Update.Identity, WindowsUpdateClass.Driver)); break;
                case "download": provider.DownloadResult = new(WindowsUpdateStatus.Failed, null, 14); break;
                case "install": provider.InstallResult = new(WindowsUpdateStatus.Partial, null, 20); break;
                case "uncertain": provider.AfterInstall = () => throw new InjectedPersistenceException(); break;
                case "empty": provider.AfterInstall = () => provider.Scan = Available(); break;
            }

            if (scenario == "uncertain")
            {
                await Assert.ThrowsAsync<InjectedPersistenceException>(() => workflow.ExecuteAsync(job.JobId, approval.Id));
            }
            else
            {
                await workflow.ExecuteAsync(job.JobId, approval.Id);
            }
        }

        await using var reopened = await OpenAsync(workspace);
        var saved = await reopened.LoadWindowsUpdatesAsync(job.JobId);
        Assert.Equal(scenario switch
        {
            "revision" => WindowsUpdateStage.DownloadChecked,
            "class" => WindowsUpdateStage.InstallChecked,
            "resources" => WindowsUpdateStage.ResourcesDeferred,
            "download" => WindowsUpdateStage.DownloadCompleted,
            "install" => WindowsUpdateStage.InstallCompleted,
            "uncertain" => WindowsUpdateStage.InstallStarted,
            _ => WindowsUpdateStage.ScanCompleted,
        }, saved[^1].Stage);
        Assert.Equal(scenario switch { "revision" or "resources" => 0, "class" or "download" => 1, _ => 2 }, provider.Mutations.Count);
        Assert.All(provider.Mutations, item => Assert.Equal(Update, item));
        if (scenario is "uncertain" or "install")
        {
            var workflow = Controlled(reopened, provider);
            provider.Scan = Available();
            await workflow.ScanAsync(job.JobId, gate.DecisionId);
            Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        }
        else if (scenario == "empty")
        {
            Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdateHistory.Disposition(saved));
        }
    }

    [Fact]
    public async Task RestartResultRequiresChangedBootObservationAndSuccessfulScan()
    {
        using var workspace = new PersistenceTestWorkspace();
        ulong uptime = 100_000;
        var provider = new FixtureAgent { InstallResult = new(WindowsUpdateStatus.Succeeded, true, 100) };
        Job job;
        SanitizationGateDecision gate;
        await using (var store = await OpenAsync(workspace, () => uptime))
        {
            string policy;
            (job, gate, policy) = await SeedAsync(store);
            var workflow = Controlled(store, provider);
            var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
            var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
            await workflow.ExecuteAsync(job.JobId, approval.Id);
        }

        await using var reopened = await OpenAsync(workspace, () => uptime);
        var observer = new WindowsUpdateWorkflow(reopened, provider, Clock);
        provider.Scan = Available();
        await observer.ScanAsync(job.JobId, gate.DecisionId);
        Assert.Equal(WindowsUpdateDisposition.RestartRequired, WindowsUpdateHistory.Disposition(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        uptime = 1_000;
        await observer.ScanAsync(job.JobId, gate.DecisionId);
        Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdateHistory.Disposition(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        Assert.Equal(2, provider.Mutations.Count);
    }

    [Fact]
    public async Task OtherWindowScanCannotEraseInflightReceiptOrReuseApproval()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var first = await OpenAsync(workspace);
        await using var second = await OpenAsync(workspace);
        var (job, gate, policy) = await SeedAsync(first);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new FixtureAgent { HoldInstall = release.Task };
        var worker = Controlled(first, agent);
        var scan = await worker.ScanAsync(job.JobId, gate.DecisionId);
        var approval = await worker.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
        var operation = worker.ExecuteAsync(job.JobId, approval.Id);
        try
        {
            await agent.InstallEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var other = Controlled(second, new FixtureAgent());
            var concurrentScan = await other.ScanAsync(job.JobId, gate.DecisionId);
            Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(await second.LoadWindowsUpdatesAsync(job.JobId)));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => other.ApproveAsync(job.JobId, gate.DecisionId,
                concurrentScan.Id, Update, Operator, policy));
        }
        finally
        {
            release.TrySetResult();
        }

        await operation.WaitAsync(TimeSpan.FromSeconds(10));
        var history = await second.LoadWindowsUpdatesAsync(job.JobId);
        var intent = Assert.Single(history.Where(row => row.Stage == WindowsUpdateStage.InstallStarted));
        var receipt = Assert.Single(history.Where(row => row.Stage == WindowsUpdateStage.InstallCompleted));
        Assert.Equal(intent.Id, receipt.PreviousId);
        Assert.Equal(WindowsUpdateStatus.Succeeded, receipt.Operation!.Status);
        Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(history));
        Assert.Equal(2, agent.Mutations.Count);
    }

    [Fact]
    public async Task NewEvidenceAfterApprovalStopsEvenWhenItsSourceTimestampIsOlder()
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace);
        var (job, gate, policy) = await SeedAsync(store);
        var provider = new FixtureAgent();
        var workflow = Controlled(store, provider);
        var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
        var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, [PersistenceTestData.CreateObservation("newer")]));
        await Assert.ThrowsAsync<JobStoreConflictException>(() => workflow.ExecuteAsync(job.JobId, approval.Id));
        Assert.Empty(provider.Mutations);
    }

    private static WindowsUpdateScan Available(params WindowsUpdateCandidate[] updates) => new(WindowsUpdateStatus.Succeeded, updates, false);

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, Func<ulong?>? boot = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion, Clock, null, boot ?? (() => 10_000));

    private static WindowsUpdateWorkflow Controlled(SqliteJobStore store, FixtureAgent provider,
        Func<WindowsUpdateCandidate, CancellationToken, Task<bool>>? preflight = null) =>
        new(store, provider, preflight ?? ((_, _) => Task.FromResult(true)), Clock);

    private static async Task<(Job Job, SanitizationGateDecision Gate, string PolicyId)> SeedAsync(SqliteJobStore store)
    {
        var job = PersistenceTestData.CreateJob();
        var intake = PersistenceTestData.CreateSanitization();
        var facts = new[]
        {
            Fact("os", "operating_system.release", "windows_11"), Fact("support", "operating_system.support_state", "supported"),
            Fact("arch", "system.architecture", "x64"),
            new Observation(PersistenceTestData.CreateMetadata("evidence-memory", EvidenceClassification.Observed,
                ProvenanceKind.SyntheticFixture, ValueAvailability.Available), new("memory.installed_bytes"),
                EvidenceValue.FromInteger(16_000_000_000), "bytes", null),
        };
        await store.CreateJobAsync(job);
        await store.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, observations: facts, sanitizationEvidence: [intake]));
        var gate = await store.RecordSanitizationGateDecisionAsync(SanitizationGate.Evaluate(job.JobId, intake, Clock.GetUtcNow()));
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy.json")))!.AsObject();
        json["claim_scope"] = "windows_update_fixture";
        var memory = json["rules"]!.AsArray().Single(rule => (string?)rule!["id"] == "POL-MEM-001")!;
        json["rules"] = new JsonArray(memory.DeepClone());
        var snapshot = PolicySnapshot.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var evaluation = DispositionEngine.Evaluate(snapshot.Document, facts, []);
        Assert.Equal(Disposition.ReadyToPrepare, evaluation.Disposition);
        var decision = PolicyDecisionRecord.Create(snapshot, job.JobId, "assessment-update-fixture", evaluation,
            facts.Select(fact => fact.Metadata.EvidenceId).Append(intake.Metadata.EvidenceId), Clock.GetUtcNow());
        await store.AppendPolicyDecisionAsync(snapshot, decision);
        return (job, gate, decision.DecisionId);
    }

    private static Observation Fact(string id, string key, string value) =>
        new(PersistenceTestData.CreateMetadata("evidence-" + id, EvidenceClassification.Observed, ProvenanceKind.SyntheticFixture,
            ValueAvailability.Available), new(key), EvidenceValue.FromEnum(value), null, null);

    private sealed class FixtureAgent : IWindowsUpdateAgent
    {
        internal int Scans { get; private set; }
        internal WindowsUpdateScan Scan { get; set; } = Available(Update);
        internal List<WindowsUpdateCandidate> Mutations { get; } = [];
        internal WindowsUpdateOperation DownloadResult { get; set; } = new(WindowsUpdateStatus.Succeeded, false, 100);
        internal WindowsUpdateOperation InstallResult { get; set; } = new(WindowsUpdateStatus.Succeeded, false, 100);
        internal Action? AfterDownload { get; set; }
        internal Action? AfterInstall { get; set; }
        internal TaskCompletionSource InstallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task HoldInstall { get; init; } = Task.CompletedTask;

        public Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken)
        {
            Scans++;
            return Task.FromResult(Scan);
        }

        public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken)
        {
            Mutations.Add(exact);
            AfterDownload?.Invoke();
            return Task.FromResult(DownloadResult);
        }

        public async Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken)
        {
            Mutations.Add(exact);
            InstallEntered.TrySetResult();
            await HoldInstall.WaitAsync(cancellationToken);
            AfterInstall?.Invoke();
            return InstallResult;
        }
    }
}
