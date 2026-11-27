using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Broker.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Broker.Tests;

public sealed class WindowsUpdateBrokerTests
{
    private const int Version = BrokerProtocolLimits.WindowsUpdateVersion;
    private static readonly string Policy = new('a', 64);
    private static readonly SecurityIdentifier Subject = new("S-1-5-5-0-1234");
    private static readonly WindowsUpdateCandidate Exact = new(new(new("10000000-aaaa-bbbb-cccc-000000000001"), 17),
        WindowsUpdateClass.Security, 32000);

    [Fact]
    public void VersionAndBootstrapBindOnePhaseOfTheSameApproval()
    {
        var download = MakeRequest();
        var actions = download.ApprovedActions;
        var install = BrokerWindowsUpdateRequest.Create(download.Envelope, download.JobId, download.GateId, download.ScanId,
            download.PolicyDecisionId, download.PolicySha256, actions, Exact, actions.Install.ActionId);
        Assert.Equal(install.ApprovalSha256, download.ApprovalSha256);
        var ticket = BrokerLaunchTicket.TryParse(BrokerLaunchTicket.Create(Environment.ProcessId, download).CreateArguments())!;
        Assert.Equal(Version, ticket.ProtocolVersion);
        Assert.True(ticket.MatchesRequest(download));
        Assert.False(ticket.MatchesRequest(install));
        var bytes = BrokerProtocolCodec.Serialize(download);
        Assert.Throws<JsonException>(() => BrokerProtocolCodec.Parse(bytes));
        Assert.Equal(bytes, BrokerProtocolCodec.Serialize(BrokerProtocolCodec.Parse(bytes, Version)));
        var badApproval = actions with { Install = actions.Install with { Impact = actions.Download.Impact } };
        Assert.Throws<ArgumentException>(() => BrokerWindowsUpdateRequest.ComputeApprovalSha256(download.JobId, download.GateId,
            download.ScanId, download.PolicyDecisionId, Policy, badApproval, Exact));
    }

    [Theory]
    [InlineData("approval_id")]
    [InlineData("download_action_id")]
    [InlineData("install_action_id")]
    [InlineData("approval_sha256")]
    [InlineData("policy_sha256")]
    [InlineData("policy_decision_id")]
    [InlineData("job_id")]
    [InlineData("gate_id")]
    [InlineData("scan_id")]
    [InlineData("action_id")]
    [InlineData("type")]
    [InlineData("revision")]
    [InlineData("maximum_download_bytes")]
    [InlineData("command")]
    public void RequestDetectsChangedScopeAndClosedFields(string field)
    {
        var request = MakeRequest();
        var document = JsonNode.Parse(BrokerProtocolCodec.Serialize(request))!.AsObject();
        var action = document["action"]!.AsObject();
        switch (field)
        {
            case "type": action[field] = "windows_update_install"; break;
            case "revision": action["parameters"]![field] = 18; break;
            case "maximum_download_bytes": action["parameters"]![field] = 32001; break;
            case "action_id": action[field] = Guid.NewGuid(); break;
            case "approval_sha256" or "policy_sha256": document[field] = new string('c', 64); break;
            case "job_id" or "gate_id" or "command": document[field] = "other"; break;
            case "policy_decision_id": document[field] = "policy-decision-" + Guid.NewGuid().ToString("N"); break;
            default: document[field] = Guid.NewGuid(); break;
        }
        Assert.Throws<JsonException>(() => BrokerProtocolCodec.Parse(Encoding.UTF8.GetBytes(document.ToJsonString()), Version));
    }

    [Theory]
    [InlineData("success", BrokerWindowsUpdateDispatchState.Attempted, WindowsUpdateStatus.Succeeded, 1)]
    [InlineData("lost", BrokerWindowsUpdateDispatchState.Unconfirmed, WindowsUpdateStatus.Partial, 1)]
    [InlineData("exception", BrokerWindowsUpdateDispatchState.Unconfirmed, WindowsUpdateStatus.Partial, 1)]
    [InlineData("drift", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.ApprovalChanged, 0)]
    [InlineData("peer", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.AccessDenied, 0)]
    [InlineData("expired", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.TimedOut, 0)]
    [InlineData("absent", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.ApprovalChanged, 0)]
    [InlineData("policy", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.ApprovalChanged, 0)]
    [InlineData("cancel", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.Cancelled, 0)]
    [InlineData("rollback", BrokerWindowsUpdateDispatchState.NotStarted, WindowsUpdateStatus.TimedOut, 0)]
    public async Task BrokerSessionRequiresIndependentPolicyPeerAndOneGrantBeforeBegin(string scenario,
        BrokerWindowsUpdateDispatchState state, WindowsUpdateStatus status, int beginCount)
    {
        var request = MakeRequest();
        var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
        var calls = 0;
        var authorizations = 0;
        var grantCalls = 0;
        var backend = new FixtureBackend(scenario);
        var handler = new WindowsUpdateBrokerHandler(backend, (_, _) => scenario == "policy" ? null : Policy,
            (_, _, _, _, _) => ++authorizations == 2 && scenario == "drift" ? WindowsUpdateServicingDecision.TargetChanged :
                WindowsUpdateServicingDecision.Allowed,
            (_, authorize) =>
            {
                grantCalls++;
                var now = DateTimeOffset.UtcNow;
                if (scenario == "expired") { now = request.Envelope.ExpiresAtUtc; }
                if (scenario == "rollback") { now -= TimeSpan.FromMinutes(1); }
                return scenario != "absent" && authorize(now);
            }, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        BrokerMessage[] messages = scenario == "cancel"
            ? [new BrokerHello(request.Envelope), request, new BrokerCancel(request.Envelope, request.Action.ActionId)]
            : [new BrokerHello(request.Envelope), request];
        await using var stream = await IpcIntegrationTests.BufferedSessionStream.CreateAsync(timeout.Token, messages);
        var outcome = await BrokerSession.RunAsync(stream, ticket, () => ++calls < 4 || scenario != "peer", timeout.Token, handler);
        Assert.Equal(BrokerSessionOutcome.Completed, outcome);
        var replies = await stream.ReadResponsesAsync(timeout.Token, Version);
        Assert.Collection(replies, msg => Assert.IsType<BrokerHelloAck>(msg), msg =>
        {
            var receipt = Assert.IsType<BrokerWindowsUpdateResult>(msg);
            Assert.Equal(state, receipt.DispatchState);
            Assert.Equal(status, receipt.Operation.Status);
            Assert.Equal(request.ApprovalSha256, receipt.ApprovalSha256);
            Assert.Equal(request.Action.ActionId, receipt.ActionId);
        });
        Assert.Equal(beginCount, backend.Begins);
        Assert.Equal(scenario is "policy" or "cancel" ? 0 : 1, grantCalls);
    }

    [Fact]
    public async Task ProductionAdmissionRejectsAnUnreviewedOfferBeforeOpeningNativeService()
    {
        var backend = new FixtureBackend("success");
        var result = await WindowsUpdateBrokerHandler.Create(backend, Subject).RunAsync(MakeRequest(), () => true, CancellationToken.None);
        Assert.Equal(BrokerWindowsUpdateDispatchState.NotStarted, result.DispatchState);
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task LateNativeWorkerCannotBeginAfterReceiptEvenIfGrantWasBeingChecked()
    {
        var request = MakeRequest();
        using var resume = new ManualResetEventSlim(false);
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new DeferredBackend(checking.Task);
        var handler = new WindowsUpdateBrokerHandler(backend, (_, _) => Policy, (_, _, _, _, _) => WindowsUpdateServicingDecision.Allowed,
            (_, _) =>
            {
                checking.SetResult();
                return resume.Wait(TimeSpan.FromSeconds(5));
            }, TimeProvider.System);
        var result = await handler.RunAsync(request, () => true, CancellationToken.None);
        Assert.Equal(BrokerWindowsUpdateDispatchState.NotStarted, result.DispatchState);
        resume.Set();
        Assert.False(await backend.LatePermission!);
    }

    [Theory]
    [InlineData("not_started", 1, "null", "null")]
    [InlineData("not_started", 10, "false", "null")]
    [InlineData("attempted", 9, "null", "null")]
    [InlineData("unconfirmed", 7, "null", "null")]
    [InlineData("unconfirmed", 9, "null", "100")]
    [InlineData("attempted", 12, "null", "null")]
    [InlineData("attempted", 1, "null", "101")]
    [InlineData("attempted", 1, "null", "1.5")]
    public void RejectsContradictoryOrMalformedTerminalReceipts(string state, int status, string reboot, string progress)
    {
        var request = MakeRequest();
        var result = new BrokerWindowsUpdateResult(request.Envelope, request.Action.ActionId, request.ApprovalSha256,
            BrokerWindowsUpdateDispatchState.Attempted, new(WindowsUpdateStatus.Succeeded, false));
        var wire = JsonNode.Parse(BrokerProtocolCodec.Serialize(result))!.AsObject();
        wire["dispatch_state"] = state;
        wire["status"] = status;
        wire["reboot_required"] = JsonNode.Parse(reboot);
        wire["progress_percent"] = JsonNode.Parse(progress);
        Assert.Throws<JsonException>(() => BrokerProtocolCodec.Parse(Encoding.UTF8.GetBytes(wire.ToJsonString()), Version));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("wrong_action")]
    [InlineData("replay")]
    public async Task InvalidControlFramesCloseSessionBeforeGrantConsumption(string scenario)
    {
        var request = MakeRequest();
        var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
        var cancel = new BrokerCancel(request.Envelope, scenario == "wrong_action" ? Guid.NewGuid() : request.Action.ActionId);
        BrokerMessage[] messages = scenario switch
        {
            "duplicate" => [new BrokerHello(request.Envelope), request, cancel, cancel],
            "replay" => [new BrokerHello(request.Envelope), request, request],
            _ => [new BrokerHello(request.Envelope), request, cancel],
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var stream = await IpcIntegrationTests.BufferedSessionStream.CreateAsync(timeout.Token, messages);
        var backend = new FixtureBackend("success");
        var handler = WindowsUpdateBrokerHandler.Create(backend, Subject);
        Assert.Equal(BrokerSessionOutcome.Rejected, await BrokerSession.RunAsync(stream, ticket, () => true, timeout.Token, handler));
        Assert.IsType<BrokerHelloAck>(Assert.Single(await stream.ReadResponsesAsync(timeout.Token, Version)));
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public void ExactProtectedGrantFormatBindsSubjectPhaseEvidenceAndBothDeadlines()
    {
        var request = MakeRequest();
        var now = DateTimeOffset.UtcNow;
        var record = Grant(request, now, now.AddMinutes(1));
        Assert.True(WindowsUpdateQualificationGrant.Matches(record, request, Subject, now));
        Assert.False(WindowsUpdateQualificationGrant.Matches(record, request, new("S-1-5-5-0-4321"), now));
        Assert.False(WindowsUpdateQualificationGrant.Matches(record, MakeRequest(), Subject, now));
        Assert.False(WindowsUpdateQualificationGrant.Matches(record, request, Subject, now.AddTicks(-1)));
        Assert.False(WindowsUpdateQualificationGrant.Matches(record, request, Subject, now.AddMinutes(1)));
        Assert.False(WindowsUpdateQualificationGrant.Matches(Grant(request, now, now.AddMinutes(31)), request, Subject, now));
        Assert.False(WindowsUpdateQualificationGrant.Matches([.. record, 10], request, Subject, now));
        Assert.False(WindowsUpdateQualificationGrant.Matches(Grant(request, now, now.AddSeconds(3), '0'), request, Subject, now));
    }

    [Fact]
    public void DeniedGrantSurvivesAndApprovedGrantIsConsumedOnItsExclusiveHandle()
    {
        var request = MakeRequest();
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(Path.GetTempPath(), "thirdlife-grant-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, Grant(request, now.AddMinutes(-1), now.AddMinutes(2)));
            using (var denied = OpenGrantFixture(path))
            {
                Assert.False(WindowsUpdateQualificationGrant.ConsumeGuardedFile(denied, request, Subject, _ => false));
            }
            Assert.True(File.Exists(path));
            using (var first = OpenGrantFixture(path))
            {
                using var concurrent = OpenGrantFixture(path);
                Assert.False(first.IsInvalid);
                Assert.True(concurrent.IsInvalid);
                Assert.True(WindowsUpdateQualificationGrant.ConsumeGuardedFile(first, request, Subject, _ => true));
            }
            using var replay = OpenGrantFixture(path);
            Assert.True(replay.IsInvalid);
        }
        finally { File.Delete(path); }
    }

    private static SafeFileHandle OpenGrantFixture(string path) => NativeMethods.CreateFile(path, 0x80010000, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);

    private static byte[] Grant(BrokerWindowsUpdateRequest request, DateTimeOffset issued, DateTimeOffset expires, char proof = 'b')
    {
        const string format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
        var subject = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(Subject.Value)));
        return Encoding.ASCII.GetBytes(string.Join('\n', "TL-WUA-GRANT-1", request.ApprovalSha256, request.Action.ActionId.ToString("D"),
            new string(proof, 64), subject, issued.ToString(format, CultureInfo.InvariantCulture), expires.ToString(format, CultureInfo.InvariantCulture), ""));
    }

    private static BrokerWindowsUpdateRequest MakeRequest()
    {
        var envelope = BrokerEnvelope.ForWindowsUpdate(Guid.NewGuid(), BrokerNonce.Create(), DateTimeOffset.UtcNow.AddMinutes(1));
        var impacts = ActionRegistry.DescribeWindowsUpdateServicing(Exact);
        WindowsUpdateApprovedActions actions = new(Guid.NewGuid(), new(Guid.NewGuid(), impacts.Download), new(Guid.NewGuid(), impacts.Install));
        return BrokerWindowsUpdateRequest.Create(envelope, new JobId("job-update-fixture"), new SanitizationGateDecisionId("gate-update-fixture"), Guid.NewGuid(),
            "policy-decision-" + Guid.NewGuid().ToString("N"), Policy, actions, Exact, actions.Download.ActionId);
    }

    private sealed class FixtureBackend(string scenario) : IWindowsUpdateBrokerBackend
    {
        public int Calls { get; private set; }
        public int Begins { get; private set; }

        public Task<WindowsUpdateOperation> ApplyAsync(WindowsUpdateActionRequest action, Func<WindowsUpdatePreparation, bool> authorizeBegin, CancellationToken token)
        {
            Calls++;
            var observed = new WindowsUpdatePreparation(action.Exact!, WindowsUpdateStatus.Succeeded, DateTimeOffset.UtcNow);
            if (!authorizeBegin(observed)) { return Task.FromResult(new WindowsUpdateOperation(WindowsUpdateStatus.ApprovalChanged, null)); }
            Assert.False(authorizeBegin(observed));
            Begins++;
            if (scenario == "exception") { throw new IOException(); }
            return Task.FromResult(new WindowsUpdateOperation(scenario == "lost" ? WindowsUpdateStatus.Partial : WindowsUpdateStatus.Succeeded, false));
        }
    }

    private sealed class DeferredBackend(Task grantPending) : IWindowsUpdateBrokerBackend
    {
        internal Task<bool>? LatePermission { get; private set; }

        public async Task<WindowsUpdateOperation> ApplyAsync(WindowsUpdateActionRequest action,
            Func<WindowsUpdatePreparation, bool> authorizeBegin, CancellationToken token)
        {
            LatePermission = Task.Run(() => authorizeBegin(new(action.Exact!, WindowsUpdateStatus.Succeeded, DateTimeOffset.UtcNow)), token);
            await grantPending.WaitAsync(TimeSpan.FromSeconds(5), token);
            return new(WindowsUpdateStatus.TimedOut, null);
        }
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    }
}
