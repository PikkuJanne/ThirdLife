using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core;
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

    [Fact]
    public async Task VersionNineFixtureDecisionCannotAuthorizeAServicingPolicyDigest()
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision gate;
        string decision;
        WindowsUpdateEntry scan;
        var provider = new FixtureAgent();
        await using (var before = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 9, Clock, null))
        {
            (job, gate, decision) = await SeedAsync(before);
            scan = await Controlled(before, provider).ScanAsync(job.JobId, gate.DecisionId);
        }
        const string unreviewed = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await using (var store = await OpenAsync(workspace))
        {
            Assert.Equal(decision, await store.FindWindowsUpdatePolicyAsync(job.JobId, gate.DecisionId, scan.Id, Update, null));
            Assert.Null(await store.FindWindowsUpdatePolicyAsync(job.JobId, gate.DecisionId, scan.Id, Update, unreviewed));
            await Assert.ThrowsAsync<JobStoreConflictException>(() => Controlled(store, provider)
                .ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, decision, unreviewed));
        }
        await Assert.ThrowsAsync<JobStoreVersionException>(() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 9, Clock, null));
        Assert.Empty(provider.Mutations);
    }

    [Theory]
    [InlineData(ActionKind.DownloadWindowsUpdate)]
    [InlineData(ActionKind.InstallWindowsUpdate)]
    public async Task AuthenticatedBrokerRefusalSettlesTheHandoffAndRequiresNewApproval(ActionKind refused)
    {
        using var workspace = new PersistenceTestWorkspace();
        var provider = new FixtureAgent { Refuse = refused };
        Job job;
        SanitizationGateDecision gate;
        string policy;
        await using (var store = await OpenAsync(workspace))
        {
            (job, gate, policy) = await SeedAsync(store);
            var workflow = Controlled(store, provider);
            var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
            var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
            Assert.Equal(WindowsUpdateStage.BrokerRefused, (await workflow.ExecuteAsync(job.JobId, approval.Id)).Stage);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(job.JobId, approval.Id));
        }
        await using var reopened = await OpenAsync(workspace);
        var next = Controlled(reopened, provider);
        var fresh = await next.ScanAsync(job.JobId, gate.DecisionId);
        Assert.Equal(WindowsUpdateDisposition.ApprovalRequired, WindowsUpdateHistory.Disposition(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        Assert.Equal(WindowsUpdateStage.Approved, (await next.ApproveAsync(job.JobId, gate.DecisionId, fresh.Id, Update, Operator, policy)).Stage);
        Assert.Equal(refused == ActionKind.DownloadWindowsUpdate ? 0 : 1, provider.Mutations.Count);
    }

    [Fact]
    public async Task MigrationKeepsOriginalApprovalBytesAndRequiresNewReviewBeforeDispatch()
    {
        using var workspace = new PersistenceTestWorkspace();
        var databasePath = Path.Combine(workspace.StoreRoot, "thirdlife-jobs.sqlite3");
        var provider = new FixtureAgent();
        WindowsUpdateEntry historical;
        await using (var version7 = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 7, Clock, null))
        {
            var (job, gate, policy) = await SeedAsync(version7);
            var scan = await Controlled(version7, provider).ScanAsync(job.JobId, gate.DecisionId);
            historical = new(Guid.NewGuid(), scan.Id, job.JobId, gate.DecisionId, Clock.GetUtcNow(),
                WindowsUpdateStage.Approved, null, new(scan.Id, Update, Operator, policy), null);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(historical, DomainJson.CreateStrictOptions());
        var json = Encoding.UTF8.GetString(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.DoesNotContain("Actions", json, StringComparison.Ordinal);
        await using (var connection = await SqliteTestControl.OpenAsync(databasePath))
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO windows_update_journal VALUES ($job, 3, $id, $gate, $payload, $hash);";
            insert.Parameters.AddWithValue("$job", historical.JobId.Value);
            insert.Parameters.AddWithValue("$id", historical.Id.ToString("D"));
            insert.Parameters.AddWithValue("$gate", historical.GateId.Value);
            insert.Parameters.AddWithValue("$payload", json);
            insert.Parameters.AddWithValue("$hash", hash);
            await insert.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InjectedPersistenceException>(() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            8, Clock, new ThrowingFaultInjector(JobStoreFaultPoint.BeforeMigrationCommit, 8)));
        Assert.Equal(7, await SqliteTestControl.ReadUserVersionAsync(databasePath));
        await using (var current = await OpenAsync(workspace))
        {
            Assert.Equal(historical, (await current.LoadWindowsUpdatesAsync(historical.JobId))[^1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Controlled(current, provider).ExecuteAsync(historical.JobId, historical.Id));
            Assert.Empty(provider.Prepared);
        }
        await Assert.ThrowsAsync<JobStoreVersionException>(() => SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, 7, Clock, null));
        await using var check = await SqliteTestControl.OpenAsync(databasePath);
        await using var read = check.CreateCommand();
        read.CommandText = "SELECT payload_json FROM windows_update_journal WHERE entry_id = $id AND payload_sha256 = $hash;";
        read.Parameters.AddWithValue("$id", historical.Id.ToString("D"));
        read.Parameters.AddWithValue("$hash", hash);
        Assert.Equal(json, await read.ExecuteScalarAsync());
    }

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
        Assert.Equal(3, provider.VerificationQueries.Count);
        Assert.Equal(history.Where(row => row.Stage == WindowsUpdateStage.InstallCompleted).Select(row => row.Id),
            history.Where(row => row.Verification is not null).Select(row => row.Verification!.InstallReceiptId));
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
            var afterRescan = await reopened.LoadWindowsUpdatesAsync(job.JobId);
            Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(afterRescan));
            Assert.Equal(UpdateRecovery.ReviewResult, WindowsUpdateHistory.RecoveryBeforePackages(afterRescan));
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
        Assert.Empty(provider.VerificationQueries);
        uptime = 1_000;
        await observer.ScanAsync(job.JobId, gate.DecisionId);
        var verifiedHistory = await reopened.LoadWindowsUpdatesAsync(job.JobId);
        Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdateHistory.Disposition(verifiedHistory));
        Assert.Equal(UpdateRecovery.None, WindowsUpdateHistory.RecoveryBeforePackages(verifiedHistory));
        Assert.Equal(Update.Identity, Assert.Single(provider.VerificationQueries));
        Assert.True(verifiedHistory[^1].Verification!.Observation.Installed);
        Assert.Equal(2, provider.Mutations.Count);
    }

    [Fact]
    public async Task FailedOrInterruptedScanRetainsReofferedInstalledRevisionUntilNewInventory()
    {
        using var workspace = new PersistenceTestWorkspace();
        var provider = new FixtureAgent();
        Job job;
        SanitizationGateDecision gate;
        await using (var store = await OpenAsync(workspace))
        {
            string policy;
            (job, gate, policy) = await SeedAsync(store);
            var workflow = Controlled(store, provider);
            var offered = await workflow.ScanAsync(job.JobId, gate.DecisionId);
            var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, offered.Id, Update, Operator, policy);
            provider.AfterInstall = () => provider.Scan = Available();
            await workflow.ExecuteAsync(job.JobId, approval.Id);
            Assert.Equal(UpdateRecovery.None, WindowsUpdateHistory.RecoveryBeforePackages(await store.LoadWindowsUpdatesAsync(job.JobId)));

            provider.Scan = Available(Update);
            var repeated = await workflow.ScanAsync(job.JobId, gate.DecisionId);
            var interrupted = new WindowsUpdateEntry(Guid.NewGuid(), repeated.Id, job.JobId, gate.DecisionId,
                Clock.GetUtcNow(), WindowsUpdateStage.ScanStarted, null, null, null);
            await store.AppendWindowsUpdateAsync(interrupted);
        }

        await using var reopened = await OpenAsync(workspace);
        Assert.Equal(UpdateRecovery.ReviewResult, WindowsUpdateHistory.RecoveryBeforePackages(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        var observer = new WindowsUpdateWorkflow(reopened, provider, Clock);
        provider.Scan = new(WindowsUpdateStatus.Offline, [], null);
        await observer.ScanAsync(job.JobId, gate.DecisionId);
        Assert.Equal(UpdateRecovery.ReviewResult, WindowsUpdateHistory.RecoveryBeforePackages(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        provider.Scan = Available();
        await observer.ScanAsync(job.JobId, gate.DecisionId);
        Assert.Equal(UpdateRecovery.None, WindowsUpdateHistory.RecoveryBeforePackages(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
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
            var pending = await second.LoadWindowsUpdatesAsync(job.JobId);
            Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(pending));
            Assert.Equal(UpdateRecovery.ReviewResult, WindowsUpdateHistory.RecoveryBeforePackages(pending));
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

    [Theory]
    [InlineData("scan", WindowsUpdateStatus.ApprovalChanged)]
    [InlineData("intake", WindowsUpdateStatus.ApprovalChanged)]
    [InlineData("policy", WindowsUpdateStatus.ApprovalChanged)]
    [InlineData("offer", WindowsUpdateStatus.ApprovalChanged)]
    [InlineData("resources", WindowsUpdateStatus.Unavailable)]
    [InlineData("cancel", WindowsUpdateStatus.Cancelled)]
    [InlineData("expired", WindowsUpdateStatus.TimedOut)]
    [InlineData("backwards", WindowsUpdateStatus.TimedOut)]
    [InlineData("channel", WindowsUpdateStatus.ApprovalChanged)]
    [InlineData("receipt", WindowsUpdateStatus.Partial)]
    public async Task LatePhaseChangeCannotSendOrAttributeTheWrongAction(string change, WindowsUpdateStatus expected)
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(Clock.GetUtcNow());
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot,
            SqliteJobStore.CurrentSchemaVersion, clock, null, () => 10_000);
        await using var other = await OpenAsync(workspace);
        var (job, gate, policy) = await SeedAsync(store);
        var agent = new FixtureAgent();
        using var cancel = new CancellationTokenSource();
        var resources = true;
        var worker = new WindowsUpdateWorkflow(store, agent, agent, (_, _) => Task.FromResult(resources), clock);
        var scan = await worker.ScanAsync(job.JobId, gate.DecisionId);
        var approval = await worker.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
        WindowsUpdateEntry? concurrentApproval = null;
        agent.OnPrepare = async (dispatch, token) =>
        {
            if (dispatch.Action.Impact.Kind != ActionKind.InstallWindowsUpdate) { return; }
            var observer = Controlled(other, new FixtureAgent());
            var preparing = (await other.LoadWindowsUpdatesAsync(job.JobId, token))[^1];
            Assert.Equal(WindowsUpdateStage.InstallPreparing, preparing.Stage);
            var forged = preparing with { Id = Guid.NewGuid(), PreviousId = preparing.Id, Stage = WindowsUpdateStage.InstallStarted };
            Assert.Null(await other.TryStartWindowsUpdateDispatchAsync(forged, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => observer.ExecuteAsync(job.JobId, approval.Id, token));

            switch (change)
            {
                case "scan":
                    var review = await observer.ScanAsync(job.JobId, gate.DecisionId, token);
                    concurrentApproval = await observer.ApproveAsync(job.JobId, gate.DecisionId, review.Id, Update, Operator, policy, token);
                    break;
                case "intake":
                    await other.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId,
                        sanitizationEvidence: [PersistenceTestData.CreateSanitization("replaced")]), token);
                    break;
                case "policy": await other.AppendEvidenceAsync(new JobEvidenceBatch(job.JobId, [PersistenceTestData.CreateObservation("drift")]), token); break;
                case "offer": agent.Scan = Available(); break;
                case "resources": resources = false; break;
                case "expired": clock.Advance(TimeSpan.FromMinutes(2)); break;
                case "backwards": clock.Advance(TimeSpan.FromSeconds(-2)); break;
                case "cancel": await cancel.CancelAsync(); break;
                case "channel": agent.ChangeChannel = true; break;
                case "receipt": agent.ChangeReceipt = true; break;
            }
        };

        var end = await worker.ExecuteAsync(job.JobId, approval.Id, cancel.Token);
        var history = await other.LoadWindowsUpdatesAsync(job.JobId);
        var sent = change == "receipt";
        Assert.Equal(concurrentApproval?.Id ?? end.Id, history[^1].Id);
        Assert.Equal(sent ? WindowsUpdateStage.DispatchUnconfirmed : WindowsUpdateStage.DispatchNotSent, end.Stage);
        Assert.Equal(expected, end.Operation!.Status);
        var approved = history.Single(row => row.Id == approval.Id).Approval!.Actions!;
        Assert.Equal(approved.ApprovalId, approval.Id);
        var anchor = history.Single(row => row.Id == end.PreviousId);
        Assert.Equal(sent ? WindowsUpdateStage.InstallStarted : WindowsUpdateStage.InstallPreparing, anchor.Stage);
        if (sent) { Assert.Equal(approved.Install.ActionId, anchor.Id); }
        else { Assert.DoesNotContain(history, row => row.Id == approved.Install.ActionId); }
        if (change == "backwards") { Assert.True(end.OccurredAtUtc < anchor.OccurredAtUtc); }
        Assert.Equal([approved.Download, approved.Install], agent.Prepared.Select(phase => phase.Action));
        Assert.All(agent.Prepared, phase =>
        {
            Assert.Equal((job.JobId, gate.DecisionId, scan.Id, approval.Id, policy, Update),
                (phase.JobId, phase.GateId, phase.ScanId, phase.ApprovalId, phase.PolicyDecisionId, phase.Exact));
        });
        Assert.Equal(sent ? 2 : 1, agent.Mutations.Count);
        Assert.Equal(2, agent.ClosedChannels);
        Assert.Empty(agent.VerificationQueries);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteAsync(job.JobId, approval.Id));
        if (change is "intake" or "policy") { return; } // Further approval first needs new qualifying policy evidence.
        if (change == "backwards") { clock.Advance(TimeSpan.FromSeconds(2)); }
        agent.OnPrepare = null;
        agent.ChangeChannel = false;
        agent.ChangeReceipt = false;
        agent.Scan = Available(Update);
        resources = true;
        var rescanned = await worker.ScanAsync(job.JobId, gate.DecisionId);
        var disposition = WindowsUpdateHistory.Disposition(await store.LoadWindowsUpdatesAsync(job.JobId));
        Assert.Equal(sent ? WindowsUpdateDisposition.ReviewRequired : WindowsUpdateDisposition.ApprovalRequired, disposition);
        if (sent)
        {
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                worker.ApproveAsync(job.JobId, gate.DecisionId, rescanned.Id, Update, Operator, policy));
        }
        else
        {
            var fresh = await worker.ApproveAsync(job.JobId, gate.DecisionId, rescanned.Id, Update, Operator, policy);
            Assert.NotEqual(approved.Install.ActionId, fresh.Approval!.Actions!.Install.ActionId);
            agent.AfterInstall = () => agent.Scan = Available();
            await worker.ExecuteAsync(job.JobId, fresh.Id);
            Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdateHistory.Disposition(await store.LoadWindowsUpdatesAsync(job.JobId)));
        }
    }

    [Theory]
    [InlineData("superseded")]
    [InlineData("empty")]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    [InlineData("wrong-revision")]
    public async Task DisappearedOfferRequiresPositiveExactInstalledStateAndReadOnlyRecovery(string scenario)
    {
        using var workspace = new PersistenceTestWorkspace();
        Job job;
        SanitizationGateDecision gate;
        Guid receiptId;
        var provider = new FixtureAgent();
        provider.AfterInstall = () => provider.Scan = scenario == "superseded"
            ? Available(new WindowsUpdateCandidate(new(Update.Identity.UpdateId, Update.Identity.Revision + 1), Update.Classification)) : Available();
        provider.InstalledObservation = exact => scenario switch
        {
            "cancelled" => throw new OperationCanceledException(),
            "unavailable" => new(WindowsUpdateStatus.Unavailable, exact, null),
            "wrong-revision" => new(WindowsUpdateStatus.Succeeded, new(exact.UpdateId, exact.Revision + 1), true),
            _ => new(WindowsUpdateStatus.Succeeded, exact, null),
        };
        await using (var store = await OpenAsync(workspace))
        {
            string policy;
            (job, gate, policy) = await SeedAsync(store);
            var workflow = Controlled(store, provider);
            var scan = await workflow.ScanAsync(job.JobId, gate.DecisionId);
            var approval = await workflow.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
            await workflow.ExecuteAsync(job.JobId, approval.Id);
            var history = await store.LoadWindowsUpdatesAsync(job.JobId);
            var receipt = Assert.Single(history.Where(row => row.Stage == WindowsUpdateStage.InstallCompleted));
            receiptId = receipt.Id;
            var verification = history[^1].Verification!;
            Assert.Equal(receiptId, verification.InstallReceiptId);
            Assert.Equal(Update.Identity, verification.Observation.Identity);
            Assert.Null(verification.Observation.Installed);
            Assert.Equal(scenario switch
            {
                "cancelled" => WindowsUpdateStatus.Cancelled,
                "unavailable" => WindowsUpdateStatus.Unavailable,
                "wrong-revision" => WindowsUpdateStatus.ApprovalChanged,
                _ => WindowsUpdateStatus.Succeeded,
            }, verification.Observation.Status);
            Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(history));
        }

        await using var reopened = await OpenAsync(workspace);
        var observer = new WindowsUpdateWorkflow(reopened, provider, Clock);
        provider.Scan = Available();
        provider.InstalledObservation = exact => new(WindowsUpdateStatus.Succeeded, exact, true);
        var confirmed = await observer.ScanAsync(job.JobId, gate.DecisionId);
        Assert.Equal(receiptId, confirmed.Verification!.InstallReceiptId);
        Assert.Equal(new(WindowsUpdateStatus.Succeeded, Update.Identity, true), confirmed.Verification.Observation);
        Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdateHistory.Disposition(await reopened.LoadWindowsUpdatesAsync(job.JobId)));
        Assert.Equal(2, provider.VerificationQueries.Count);
        Assert.Equal(2, provider.Mutations.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshScanAfterReopeningSupersedesOnlyAnUnsentPreparation(bool handoff)
    {
        using var workspace = new PersistenceTestWorkspace();
        var agent = new FixtureAgent();
        Job job;
        SanitizationGateDecision gate;
        string policy;
        await using (var store = await OpenAsync(workspace))
        {
            (job, gate, policy) = await SeedAsync(store);
            var worker = Controlled(store, agent);
            var scan = await worker.ScanAsync(job.JobId, gate.DecisionId);
            var approval = await worker.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
            if (handoff) { agent.AfterDownload = () => throw new InjectedPersistenceException(); }
            else { agent.OnPrepare = (_, _) => throw new InjectedPersistenceException(); }
            await Assert.ThrowsAsync<InjectedPersistenceException>(() => worker.ExecuteAsync(job.JobId, approval.Id));
            Assert.Equal(handoff ? WindowsUpdateStage.DownloadStarted : WindowsUpdateStage.DownloadPreparing,
                (await store.LoadWindowsUpdatesAsync(job.JobId))[^1].Stage);
        }
        await using var reopened = await OpenAsync(workspace);
        var recovery = Controlled(reopened, new FixtureAgent());
        var inventory = await recovery.ScanAsync(job.JobId, gate.DecisionId);
        var history = await reopened.LoadWindowsUpdatesAsync(job.JobId);
        if (handoff)
        {
            Assert.DoesNotContain(history, row => row.Stage == WindowsUpdateStage.DispatchNotSent);
            Assert.Equal(WindowsUpdateDisposition.ReviewRequired, WindowsUpdateHistory.Disposition(history));
            await Assert.ThrowsAsync<JobStoreConflictException>(() =>
                recovery.ApproveAsync(job.JobId, gate.DecisionId, inventory.Id, Update, Operator, policy));
        }
        else
        {
            var unsent = Assert.Single(history.Where(row => row.Stage == WindowsUpdateStage.DispatchNotSent));
            Assert.Equal(WindowsUpdateStage.DownloadPreparing, history.Single(row => row.Id == unsent.PreviousId).Stage);
            Assert.Equal(WindowsUpdateDisposition.ApprovalRequired, WindowsUpdateHistory.Disposition(history));
            var fresh = await recovery.ApproveAsync(job.JobId, gate.DecisionId, inventory.Id, Update, Operator, policy);
            Assert.NotEqual(unsent.Approval!.Actions!.Download.ActionId, fresh.Approval!.Actions!.Download.ActionId);
        }
        Assert.Equal(handoff ? 1 : 0, agent.Mutations.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InternalDeadlineDuringFinalObservationOrResourcesRemainsTimedOut(bool duringScan)
    {
        using var workspace = new PersistenceTestWorkspace();
        await using var store = await OpenAsync(workspace);
        var (job, gate, policy) = await SeedAsync(store);
        var timer = new DispatchDeadlineClock();
        var agent = new FixtureAgent();
        var armed = false;
        var worker = new WindowsUpdateWorkflow(store, agent, agent, (_, _) =>
        {
            if (!duringScan && armed) { timer.Fire(); return Task.FromResult(false); }
            return Task.FromResult(true);
        }, timer);
        var scan = await worker.ScanAsync(job.JobId, gate.DecisionId);
        var approval = await worker.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
        agent.OnPrepare = (_, _) =>
        {
            armed = true;
            if (duringScan) { agent.OnScan = () => { timer.Fire(); throw new OperationCanceledException(); }; }
            return Task.CompletedTask;
        };
        var end = await worker.ExecuteAsync(job.JobId, approval.Id);
        Assert.Equal(WindowsUpdateStage.DispatchNotSent, end.Stage);
        Assert.Equal(WindowsUpdateStatus.TimedOut, end.Operation!.Status);
        Assert.Empty(agent.Mutations);
    }

    private sealed class DispatchDeadlineClock : TimeProvider
    {
        internal Action Fire { get; private set; } = () => throw new InvalidOperationException();
        public override DateTimeOffset GetUtcNow() => Clock.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Fire = () => callback(state);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    [Fact]
    public async Task ACorrelatedSentReceiptSurvivesClockRollbackAndVerifies()
    {
        using var workspace = new PersistenceTestWorkspace();
        var clock = new FixedTimeProvider(Clock.GetUtcNow());
        await using var store = await SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion,
            clock, null, () => 10_000);
        var (job, gate, policy) = await SeedAsync(store);
        var agent = new FixtureAgent();
        var worker = new WindowsUpdateWorkflow(store, agent, agent, (_, _) => Task.FromResult(true), clock);
        var scan = await worker.ScanAsync(job.JobId, gate.DecisionId);
        var approval = await worker.ApproveAsync(job.JobId, gate.DecisionId, scan.Id, Update, Operator, policy);
        agent.AfterInstall = () => { agent.Scan = Available(); clock.Advance(TimeSpan.FromSeconds(-3)); };
        await worker.ExecuteAsync(job.JobId, approval.Id);
        var history = await store.LoadWindowsUpdatesAsync(job.JobId);
        var receipt = Assert.Single(history.Where(row => row.Stage == WindowsUpdateStage.InstallCompleted));
        Assert.True(receipt.OccurredAtUtc < history.Single(row => row.Id == receipt.PreviousId).OccurredAtUtc);
        Assert.Equal(receipt.Id, history[^1].Verification!.InstallReceiptId);
        Assert.Equal(WindowsUpdateDisposition.Current, WindowsUpdateHistory.Disposition(history));
    }

    private static WindowsUpdateScan Available(params WindowsUpdateCandidate[] updates) => new(WindowsUpdateStatus.Succeeded, updates, false);

    private static Task<SqliteJobStore> OpenAsync(PersistenceTestWorkspace workspace, Func<ulong?>? boot = null) =>
        SqliteJobStore.OpenForTestingAsync(workspace.StoreRoot, SqliteJobStore.CurrentSchemaVersion, Clock, null, boot ?? (() => 10_000));

    private static WindowsUpdateWorkflow Controlled(SqliteJobStore store, FixtureAgent provider,
        Func<WindowsUpdateCandidate, CancellationToken, Task<bool>>? preflight = null) =>
        new(store, provider, provider, preflight ?? ((_, _) => Task.FromResult(true)), Clock);

    internal static async Task<(Job Job, SanitizationGateDecision Gate, string PolicyId)> SeedAsync(SqliteJobStore store, Job? existing = null)
    {
        var job = existing ?? PersistenceTestData.CreateJob();
        var intake = PersistenceTestData.CreateSanitization();
        var facts = new[]
        {
            Fact("os", "operating_system.release", "windows_11"), Fact("support", "operating_system.support_state", "supported"),
            Fact("arch", "system.architecture", "x64"),
            new Observation(PersistenceTestData.CreateMetadata("evidence-memory", EvidenceClassification.Observed,
                ProvenanceKind.SyntheticFixture, ValueAvailability.Available), new("memory.installed_bytes"),
                EvidenceValue.FromInteger(16_000_000_000), "bytes", null),
        };
        if (existing is null) { await store.CreateJobAsync(job); }
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

    internal sealed class FixtureAgent : IWindowsUpdateAgent, IWindowsUpdateDispatchBackend
    {
        internal int Scans { get; private set; }
        internal WindowsUpdateScan Scan { get; set; } = Available(Update);
        internal List<WindowsUpdateCandidate> Mutations { get; } = [];
        internal List<WindowsUpdateIdentity> VerificationQueries { get; } = [];
        internal Func<WindowsUpdateIdentity, WindowsUpdateInstalledObservation> InstalledObservation { get; set; } =
            exact => new(WindowsUpdateStatus.Succeeded, exact, true);
        internal WindowsUpdateOperation DownloadResult { get; set; } = new(WindowsUpdateStatus.Succeeded, false, 100);
        internal WindowsUpdateOperation InstallResult { get; set; } = new(WindowsUpdateStatus.Succeeded, false, 100);
        internal Action? AfterDownload { get; set; }
        internal Action? AfterInstall { get; set; }
        internal Action? OnScan { get; set; }
        internal TaskCompletionSource InstallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task HoldInstall { get; init; } = Task.CompletedTask;
        internal Func<WindowsUpdateDispatch, CancellationToken, Task>? OnPrepare { get; set; }
        internal List<WindowsUpdateDispatch> Prepared { get; } = [];
        internal ActionKind? Refuse { get; init; }
        internal bool ChangeChannel { get; set; }
        internal bool ChangeReceipt { get; set; }
        internal int ClosedChannels { get; set; }

        public async Task<IWindowsUpdateDispatchChannel?> PrepareAsync(WindowsUpdateDispatch dispatch, CancellationToken token)
        {
            Prepared.Add(dispatch);
            if (OnPrepare is not null) { await OnPrepare(dispatch, token); }
            return new FixtureChannel(this, ChangeChannel ? dispatch with { ApprovalId = Guid.NewGuid() } : dispatch);
        }

        private sealed class FixtureChannel(FixtureAgent agent, WindowsUpdateDispatch dispatch) : IWindowsUpdateDispatchChannel
        {
            public WindowsUpdateDispatch Dispatch { get; } = dispatch;

            public async Task<WindowsUpdateDispatchReceipt?> SendAsync(CancellationToken token)
            {
                if (agent.Refuse == Dispatch.Action.Impact.Kind)
                {
                    return new(Dispatch, new(WindowsUpdateStatus.AccessDenied, null), RefusedBeforeOperation: true);
                }
                var outcome = Dispatch.Action.Impact.Kind == ActionKind.DownloadWindowsUpdate
                    ? await agent.DownloadAsync(Dispatch.Exact, token) : await agent.InstallAsync(Dispatch.Exact, token);
                return new(agent.ChangeReceipt ? Dispatch with { ScanId = Guid.NewGuid() } : Dispatch, outcome);
            }

            public ValueTask DisposeAsync()
            {
                agent.ClosedChannels++;
                return ValueTask.CompletedTask;
            }
        }

        public Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken)
        {
            Scans++;
            OnScan?.Invoke();
            return Task.FromResult(Scan);
        }

        public Task<WindowsUpdateInstalledObservation> ObserveInstalledAsync(WindowsUpdateIdentity exact, CancellationToken cancellationToken)
        {
            VerificationQueries.Add(exact);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InstalledObservation(exact));
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
