using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Windows;
using ThirdLife.UI.BrokerClient;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class ProtectedBrokerRehearsalTests
{
    private const string TraceVariable = "THIRDLIFE_TL0317_TRACE_PATH";
    private const string RetainObservationScenario = "retainobservation";
    private static readonly JsonSerializerOptions TraceOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void InstalledCandidateMatchesTheReviewedDigestAndRejectsDirectWriteAccess()
    {
        VerifyInstalledCandidate();
        Trace(new("InstalledCandidateVerified", "verify", ClientUnelevated: true));
    }

    private static void VerifyInstalledCandidate()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());

        var reviewed = Environment.GetEnvironmentVariable("THIRDLIFE_TL0311_REVIEWED_BROKER_SHA256");
        Assert.NotNull(reviewed);
        Assert.Matches("^[0-9a-f]{64}$", reviewed);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ThirdLife", "Broker");
        var binary = Path.Combine(folder, "ThirdLife.Broker.exe");
        var metadata = Path.Combine(folder, "broker.sha256");
        using (var data = File.OpenRead(binary))
        {
            Assert.Equal(reviewed, Convert.ToHexStringLower(SHA256.HashData(data)));
        }

        Assert.Equal(Encoding.ASCII.GetBytes(reviewed), File.ReadAllBytes(metadata));
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var write = File.OpenHandle(binary, FileMode.Open, FileAccess.Write);
        });
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            using var write = File.OpenHandle(metadata, FileMode.Open, FileAccess.Write);
        });

        using var launchGuard = InstalledBroker.TryOpen();
        Assert.NotNull(launchGuard);
        Assert.Equal(binary, launchGuard.Executable, ignoreCase: true);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void InstalledBrokerCompletesOneNoOpThroughExplicitConsent() =>
        Rehearse("accept", BrokerBatchStatus.Completed);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void DecliningTheInstalledBrokerLeavesNoTerminalAction() =>
        Rehearse("decline", BrokerBatchStatus.Declined);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public void TerminatingTheInstalledBrokerBeforeDispatchLeavesNoTerminalAction() =>
        Rehearse("interrupt", BrokerBatchStatus.Unavailable);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public void InstalledBrokerTimesOutAfterAuthenticatedHello() =>
        RehearseTransport("timeout", disconnect: false);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public void InstalledBrokerExitsWhenAuthenticatedClientDisconnects() =>
        RehearseTransport("disconnect", disconnect: true);

    [Fact]
    [Trait("Category", "ActiveMachine")]
    [Trait("Category", "Extended")]
    public void InstalledBrokerClientRetainsAnObservationWhenItsReceiveLoopIsInterrupted() =>
        RunInteractive(RetainObservationScenario, RehearseRetainedObservationAsync);

    private static void Rehearse(string scenario, BrokerBatchStatus expected) =>
        RunInteractive(scenario, async cancellationToken =>
        {
            var request = CreateRequest(TimeSpan.FromSeconds(70));
            var client = scenario == "interrupt"
                ? new BrokerBatchClient(ticket => LaunchUntilTerminatedAsync(ticket, cancellationToken))
                : new BrokerBatchClient();
            Trace(new("RequestingConsent", scenario));
            var result = await client.RunAsync(request, cancellationToken: cancellationToken);

            var clientUnelevated = !WindowsBrokerPeer.CurrentIdentity().IsElevated;
            Trace(new("Result", scenario, result.Status.ToString(), result.Results.Length, clientUnelevated));
            Assert.True(clientUnelevated);
            Assert.Equal(expected, result.Status);
            if (expected == BrokerBatchStatus.Completed)
            {
                var terminal = Assert.Single(result.Results);
                Assert.Equal(request.Actions[0].ActionId, terminal.ActionId);
                Assert.Equal(BrokerResultStatus.Applied, terminal.Status);
            }
            else
            {
                Assert.Empty(result.Results);
            }
        });

    private static async Task<Process?> LaunchUntilTerminatedAsync(BrokerLaunchTicket ticket, CancellationToken cancellationToken)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TraceVariable)));
        var process = Assert.IsType<Process>(await TrustedBrokerLauncher.LaunchAsync(ticket, cancellationToken));
        try
        {
            using var peer = WindowsBrokerPeer.Open(process.Id);
            VerifyElevatedPeer(peer);
            var terminationLimit = TimeSpan.FromSeconds(20);
            Assert.True(ticket.ExpiresAtUtc - TimeProvider.System.GetUtcNow() > terminationLimit,
                "The broker ticket must outlast the forced-exit observation window.");

            // The client cannot connect while its launch is pending. The host can stop only this
            // exact broker after verifying the private rendezvous and the process's binding.
            Trace(new("AwaitingTermination", "interrupt", ClientProcessId: Environment.ProcessId,
                BrokerProcessId: process.Id, SessionId: peer.Identity.SessionId, BrokerStartedUtc: process.StartTime.ToUniversalTime()));
            await process.WaitForExitAsync(cancellationToken).WaitAsync(terminationLimit, cancellationToken);
            Trace(new("BrokerExited", "interrupt", ExitCode: process.ExitCode));
            Assert.Equal(-1, process.ExitCode);
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static void RehearseTransport(string scenario, bool disconnect) =>
        RunInteractive(scenario, async cancellationToken =>
        {
            // Authenticate the actual protected broker, then intentionally leave the approved
            // request unsent while the broker is waiting for its first action batch.
            var request = CreateRequest(TimeSpan.FromSeconds(disconnect ? 70 : 45));
            var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
            Trace(new("RequestingConsent", scenario));
            using var process = Assert.IsType<Process>(await TrustedBrokerLauncher.LaunchAsync(ticket, cancellationToken));
            using var peer = WindowsBrokerPeer.Open(process.Id);
            VerifyElevatedPeer(peer);
            using var pipe = WindowsBrokerPipe.CreateClient(ticket.PipeId);
            await pipe.ConnectAsync(cancellationToken);
            Assert.True(WindowsBrokerPipe.HasServerProcess(pipe, peer.ProcessId, peer.Identity.SessionId));
            await BrokerPipeFrames.WriteAsync(pipe, new BrokerHello(request.Envelope), cancellationToken);
            var ack = Assert.IsType<BrokerHelloAck>(await BrokerPipeFrames.ReadAsync(pipe, cancellationToken));
            Assert.True(ticket.MatchesEnvelope(ack.Envelope));
            var remaining = ticket.ExpiresAtUtc - TimeProvider.System.GetUtcNow();
            Assert.True(remaining > TimeSpan.FromSeconds(10), "The broker must have time to wait for the action batch.");
            Trace(new("AuthenticatedHello", scenario));

            if (disconnect)
            {
                pipe.Dispose();
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            else
            {
                var wait = Stopwatch.StartNew();
                await Assert.ThrowsAsync<EndOfStreamException>(() => BrokerPipeFrames.ReadAsync(pipe, cancellationToken));
                await process.WaitForExitAsync(cancellationToken);
                Assert.True(wait.Elapsed + TimeSpan.FromSeconds(1) >= remaining,
                    "The authenticated channel closed before the broker deadline.");
            }

            var clientUnelevated = !WindowsBrokerPeer.CurrentIdentity().IsElevated;
            Trace(new("Result", scenario, disconnect ? "ClientDisconnected" : "BrokerDeadlineElapsed", 0,
                clientUnelevated, ExitCode: process.ExitCode));
            Assert.True(clientUnelevated);
            Assert.Equal(3, process.ExitCode);
        });

    private static async Task RehearseRetainedObservationAsync(CancellationToken cancellationToken)
    {
        var firstActionId = Guid.NewGuid();
        var secondActionId = Guid.NewGuid();
        var request = CreateRequest(TimeSpan.FromSeconds(70), firstActionId, secondActionId);
        var progress = new InterruptAtSecondProgress(firstActionId, secondActionId);
        WindowsBrokerPeer? peer = null;
        Process? observedExit = null;
        var client = new BrokerBatchClient(async ticket =>
        {
            var launched = Assert.IsType<Process>(await TrustedBrokerLauncher.LaunchAsync(ticket, cancellationToken));
            try
            {
                peer = WindowsBrokerPeer.Open(launched.Id);
                VerifyElevatedPeer(peer);
                observedExit = Process.GetProcessById(launched.Id);
                return launched;
            }
            catch
            {
                launched.Dispose();
                throw;
            }
        });

        try
        {
            Trace(new("RequestingConsent", RetainObservationScenario));
            var result = await client.RunAsync(request, progress, cancellationToken);
            var broker = Assert.IsType<Process>(observedExit);
            await broker.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            var clientUnelevated = !WindowsBrokerPeer.CurrentIdentity().IsElevated;
            Trace(new("Result", RetainObservationScenario, result.Status.ToString(), result.Results.Length,
                clientUnelevated, ExitCode: broker.ExitCode));
            Assert.True(clientUnelevated);
            Assert.True(progress.Interrupted);
            Assert.Equal(BrokerBatchStatus.RequiresReview, result.Status);
            var terminal = Assert.Single(result.Results);
            Assert.Equal(firstActionId, terminal.ActionId);
            Assert.Equal(2, terminal.Sequence);
            Assert.Equal(BrokerResultStatus.Applied, terminal.Status);
            Assert.True(broker.ExitCode is 0 or 3);
        }
        finally
        {
            observedExit?.Dispose();
            peer?.Dispose();
        }
    }

    private static void VerifyElevatedPeer(WindowsBrokerPeer peer)
    {
        var initiatingUser = WindowsBrokerPeer.CurrentIdentity();
        Assert.True(peer.IsAlive);
        Assert.True(peer.Identity.IsElevated);
        Assert.True(initiatingUser.UserSid.Equals(peer.Identity.UserSid), "Elevated process changed the initiating user.");
        Assert.Equal(initiatingUser.SessionId, peer.Identity.SessionId);
    }

    private static BrokerRequest CreateRequest(TimeSpan lifetime, params Guid[] actionIds) => new(
        new BrokerEnvelope(Guid.NewGuid(), BrokerNonce.Create(), TimeProvider.System.GetUtcNow().Add(lifetime)),
        new JobId("job-tl0311-rehearsal"), Guid.NewGuid(), new string('a', 64), new string('b', 64),
        actionIds.Length == 0 ? [ActionRegistry.CreateNoOp(Guid.NewGuid())] : [.. actionIds.Select(ActionRegistry.CreateNoOp)]);

    private static void RunInteractive(string scenario, Func<CancellationToken, Task> run)
    {
        Assert.Equal(scenario, Environment.GetEnvironmentVariable("THIRDLIFE_TL0311_CONSENT_REHEARSAL"));
        Assert.True(Environment.UserInteractive);
        Assert.True(WindowsBrokerPeer.CurrentIdentity().SessionId > 0);
        VerifyInstalledCandidate();

        ShellTestHost.Run(() =>
        {
            var completed = false;
            ExceptionDispatchInfo? failure = null;
            var window = new Window
            {
                Title = "ThirdLife broker verification",
                Width = 420,
                Height = 150,
                Content = new TextBlock
                {
                    Text = "Windows will ask for consent to run one test action that makes no machine changes.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20),
                },
            };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(75));
            window.Closing += (_, args) => args.Cancel = !completed && failure is null;
            window.ContentRendered += RunBrokerAsync;
            window.ShowDialog();
            failure?.Throw();
            Assert.True(completed, "The interactive verification closed before completing.");

            async void RunBrokerAsync(object? sender, EventArgs args)
            {
                window.ContentRendered -= RunBrokerAsync;
                try
                {
                    await run(cancellation.Token);
                    completed = true;
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(85));
    }

    private static void Trace(RehearsalEvent entry)
    {
        var path = Environment.GetEnvironmentVariable(TraceVariable);
        if (string.IsNullOrWhiteSpace(path)) { return; }

        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var output = new StreamWriter(file);
        output.WriteLine(JsonSerializer.Serialize(entry, TraceOptions));
    }

    private sealed record RehearsalEvent(string Stage, string Scenario, string? Status = null, int? TerminalCount = null,
        bool? ClientUnelevated = null, int? ClientProcessId = null, int? BrokerProcessId = null, int? SessionId = null,
        DateTime? BrokerStartedUtc = null, int? ExitCode = null)
    {
        public DateTimeOffset TimestampUtc { get; } = TimeProvider.System.GetUtcNow();
    }

    private sealed class InterruptAtSecondProgress(Guid firstActionId, Guid secondActionId) : IProgress<BrokerProgress>
    {
        private bool _acceptedFirst;

        public bool Interrupted { get; private set; }

        public void Report(BrokerProgress update)
        {
            Assert.Equal(BrokerProgressStage.Accepted, update.Stage);
            if (!_acceptedFirst)
            {
                Assert.Equal(firstActionId, update.ActionId);
                Assert.Equal(1, update.Sequence);
                _acceptedFirst = true;
                return;
            }

            Assert.Equal(secondActionId, update.ActionId);
            Assert.Equal(3, update.Sequence);
            Interrupted = true;
            throw new IOException("The rehearsal interrupted delivery of the second action's progress to the UI.");
        }
    }
}
