using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.Jobs;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Broker.Tests;

[SupportedOSPlatform("windows")]
public sealed class IpcIntegrationTests
{
    private static readonly Guid Correlation = new("bba5bd40-b128-4dd5-b7cb-b96d43e42d78");
    private static readonly Guid Approval = new("12e39924-33b4-4357-ae11-5a6ba9e4fd6b");
    private static readonly Guid ActionOne = new("41a3d5f7-628d-4f6a-9c58-f810ae85ca36");
    private static readonly Guid ActionTwo = new("d803e9e6-8c10-48a7-94c8-5ba1fd532a40");
    private static readonly Guid Other = new("2362250f-a1ea-475f-8f01-5ba481763cbd");
    private static readonly BrokerNonce Nonce = new(new string('a', 64));
    private static readonly JobId Job = new("job-broker-session-fixture");
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public void LaunchTicketCarriesOnlyBoundedBootstrapValuesAndPinsTheRequest()
    {
        var request = CreateRequest();
        var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
        var arguments = ticket.CreateArguments();
        var parsed = Assert.IsType<BrokerLaunchTicket>(BrokerLaunchTicket.TryParse(arguments));

        Assert.NotEqual(Guid.Empty, parsed.PipeId);
        Assert.Equal(ticket.PipeId, parsed.PipeId);
        Assert.Equal(Environment.ProcessId, parsed.ClientProcessId);
        Assert.Equal(Correlation, parsed.CorrelationId);
        Assert.Equal(request.Envelope.ExpiresAtUtc, parsed.ExpiresAtUtc);
        Assert.Equal(BrokerProtocolCodec.ComputeRequestSha256(request), parsed.RequestSha256);
        Assert.True(parsed.MatchesRequest(request));
        Assert.DoesNotContain(Nonce.Value, arguments);
        Assert.DoesNotContain(Job.Value, arguments);

        var differentApproval = CreateRequest(request.Envelope, approval: Other);
        Assert.True(parsed.MatchesEnvelope(differentApproval.Envelope));
        Assert.False(parsed.MatchesRequest(differentApproval));
        Assert.False(parsed.MatchesRequest(CreateRequest(request.Envelope, job: new JobId("job-other-fixture"))));
        Assert.False(parsed.MatchesEnvelope(new BrokerEnvelope(Other, Nonce, request.Envelope.ExpiresAtUtc)));
    }

    [Theory]
    [InlineData(0, "--unrecognized")]
    [InlineData(1, "00000000-0000-0000-0000-000000000000")]
    [InlineData(2, "0")]
    [InlineData(2, "01")]
    [InlineData(2, "2147483648")]
    [InlineData(3, "BBA5BD40-B128-4DD5-B7CB-B96D43E42D78")]
    [InlineData(4, "3155378976000000000")]
    [InlineData(5, "unknown")]
    [InlineData(6, "unknown")]
    public void LaunchTicketRejectsUnrecognizedOrOutOfBoundsArguments(int offset, string replacement)
    {
        var arguments = BrokerLaunchTicket.Create(Environment.ProcessId, CreateRequest()).CreateArguments();
        arguments[offset] = replacement;
        Assert.Null(BrokerLaunchTicket.TryParse(arguments));
    }

    [Fact]
    public async Task FramesRoundTripMessagesWithoutReadingPastTheirBoundary()
    {
        var request = CreateRequest();
        await using var stream = new MemoryStream();
        await BrokerPipeFrames.WriteAsync(stream, new BrokerHello(request.Envelope), CancellationToken.None);
        await BrokerPipeFrames.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        var hello = Assert.IsType<BrokerHello>(await BrokerPipeFrames.ReadAsync(stream, CancellationToken.None));
        Assert.Equal(request.Envelope, hello.Envelope);
        var decoded = Assert.IsType<BrokerRequest>(await BrokerPipeFrames.ReadAsync(stream, CancellationToken.None));
        Assert.Equal(BrokerProtocolCodec.ComputeRequestSha256(request), BrokerProtocolCodec.ComputeRequestSha256(decoded));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(BrokerProtocolLimits.MaximumMessageBytes + 1)]
    [InlineData(int.MaxValue)]
    public async Task FrameRejectsOversizeAndInvalidLengthBeforeReadingPayload(int length)
    {
        var bytes = new byte[sizeof(int) + 1];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        await using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => BrokerPipeFrames.ReadAsync(stream, CancellationToken.None));
        Assert.Equal(sizeof(int), stream.Position);
    }

    [Fact]
    public async Task FrameDistinguishesCleanEofFromTruncation()
    {
        await using var eof = new MemoryStream();
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrokerPipeFrames.ReadAsync(eof, CancellationToken.None));
        await using var shortHeader = new MemoryStream(new byte[] { 5, 0, 0 });
        await Assert.ThrowsAsync<InvalidDataException>(() => BrokerPipeFrames.ReadAsync(shortHeader, CancellationToken.None));

        var bytes = new byte[sizeof(int) + 1];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        await using var shortPayload = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => BrokerPipeFrames.ReadAsync(shortPayload, CancellationToken.None));
    }

    [Fact]
    public async Task WindowsPipeHasAProtectedLogonAclAndKernelIdentifiesBothPeers()
    {
        var current = WindowsBrokerPeer.CurrentIdentity();
        using var pinned = WindowsBrokerPeer.Open(Environment.ProcessId);
        Assert.True(pinned.IsAlive);
        Assert.Equal(Environment.ProcessId, pinned.ProcessId);
        Assert.Equal(current.IsElevated, pinned.Identity.IsElevated);
        Assert.True(current.UserSid.Equals(pinned.Identity.UserSid), "The pinned process user must match.");
        Assert.True(current.LogonSid.Equals(pinned.Identity.LogonSid), "The pinned process logon must match.");
        Assert.Equal(current.SessionId, pinned.Identity.SessionId);
        var pipeId = Guid.NewGuid();
        await using var server = WindowsBrokerPipe.CreateServer(pipeId, current.LogonSid);
        await using var client = WindowsBrokerPipe.CreateClient(pipeId);
        var security = server.GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .OfType<PipeAccessRule>().ToArray();
        Assert.Equal(2, rules.Length);
        Assert.All(rules.Select(rule => (rule.IsInherited, rule.AccessControlType)), access =>
        {
            Assert.False(access.IsInherited);
            Assert.Equal(AccessControlType.Allow, access.AccessControlType);
        });
        var logonRights = Assert.Single(rules.Where(rule => rule.IdentityReference == current.LogonSid)
            .Select(rule => rule.PipeAccessRights));
        Assert.Equal(
            PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize,
            logonRights);
        var ownerRights = new SecurityIdentifier(WellKnownSidType.WinCreatorOwnerRightsSid, null);
        var grantedOwnerRights = Assert.Single(rules.Where(rule => rule.IdentityReference == ownerRights)
            .Select(rule => rule.PipeAccessRights));
        Assert.Equal(PipeAccessRights.ReadPermissions, grantedOwnerRights);
        Assert.Throws<IOException>(() =>
        {
            using var unexpected = WindowsBrokerPipe.CreateServer(pipeId, current.LogonSid);
        });

        using var timeout = new CancellationTokenSource(Deadline);
        var accepting = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await accepting;
        Assert.True(WindowsBrokerPipe.HasClientProcess(server, Environment.ProcessId, current.SessionId));
        Assert.True(WindowsBrokerPipe.HasServerProcess(client, Environment.ProcessId, current.SessionId));
        Assert.False(WindowsBrokerPipe.HasClientProcess(server, Environment.ProcessId, current.SessionId + 1));
        Assert.False(WindowsBrokerPipe.HasServerProcess(client, Environment.ProcessId, current.SessionId + 1));
        var request = CreateRequest();
        await BrokerPipeFrames.WriteAsync(client, new BrokerHello(request.Envelope), timeout.Token);
        Assert.IsType<BrokerHello>(await BrokerPipeFrames.ReadAsync(server, timeout.Token));
        var impersonated = WindowsBrokerPipe.ReadImpersonatedClient(server);
        Assert.True(current.UserSid.Equals(impersonated.UserSid), "The observed pipe user must match.");
        Assert.True(current.LogonSid.Equals(impersonated.LogonSid), "The observed pipe logon must match.");
        Assert.Equal(current.SessionId, impersonated.SessionId);
        Assert.Equal(current.IsElevated, impersonated.IsElevated);
    }

    [Fact]
    public async Task NativePipeDeliversQueuedFinalFramesAfterServerDisposalAndThenCleanEof()
    {
        var identity = WindowsBrokerPeer.CurrentIdentity();
        var pipeId = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(Deadline);
        await using var client = WindowsBrokerPipe.CreateClient(pipeId);
        var envelope = CreateRequest().Envelope;
        await using (var server = WindowsBrokerPipe.CreateServer(pipeId, identity.LogonSid))
        {
            var accepting = server.WaitForConnectionAsync(timeout.Token);
            await client.ConnectAsync(timeout.Token);
            await accepting;
            await BrokerPipeFrames.WriteAsync(server,
                new BrokerProgress(envelope, ActionOne, 1, BrokerProgressStage.Accepted), timeout.Token);
            await BrokerPipeFrames.WriteAsync(server,
                new BrokerResult(envelope, ActionOne, 2, BrokerResultStatus.Applied), timeout.Token);
        }

        var progress = Assert.IsType<BrokerProgress>(await BrokerPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(ActionOne, progress.ActionId);
        Assert.Equal(1, progress.Sequence);
        var result = Assert.IsType<BrokerResult>(await BrokerPipeFrames.ReadAsync(client, timeout.Token));
        Assert.Equal(envelope, result.Envelope);
        Assert.Equal(ActionOne, result.ActionId);
        Assert.Equal(2, result.Sequence);
        Assert.Equal(BrokerResultStatus.Applied, result.Status);
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrokerPipeFrames.ReadAsync(client, timeout.Token));
    }

    [Fact]
    public async Task OneAuthenticatedSessionEmitsOneOrderedNoOpBatchAndFinishes()
    {
        var authCalls = 0;
        await using var fixture = await SessionFixture.ConnectAsync(() => Interlocked.Increment(ref authCalls) <= 2);
        await fixture.HandshakeAsync();
        await fixture.SendAsync(fixture.Request);
        var results = new List<Guid>();
        var previousSequence = 0;
        while (results.Count < fixture.Request.Actions.Length)
        {
            var response = await fixture.ReadAsync();
            Assert.True(fixture.Ticket.MatchesEnvelope(response.Envelope));
            switch (response)
            {
                case BrokerProgress progress:
                    Assert.InRange(progress.Sequence, previousSequence + 1, BrokerProtocolLimits.MaximumSequence);
                    Assert.Contains(fixture.Request.Actions, action => action.ActionId == progress.ActionId);
                    previousSequence = progress.Sequence;
                    break;
                case BrokerResult result:
                    Assert.InRange(result.Sequence, previousSequence + 1, BrokerProtocolLimits.MaximumSequence);
                    Assert.Equal(BrokerResultStatus.Applied, result.Status);
                    results.Add(result.ActionId);
                    previousSequence = result.Sequence;
                    break;
                default:
                    Assert.Fail("The no-op batch returned an unexpected message.");
                    break;
            }
        }

        Assert.Equal(fixture.Request.Actions.Select(action => action.ActionId), results);
        Assert.Equal(BrokerSessionOutcome.Completed, await fixture.Outcome);
        Assert.Equal(2, authCalls);
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("correlation")]
    [InlineData("expired")]
    [InlineData("request-without-hello")]
    public async Task InvalidInitialMessagesFailBeforePeerAuthentication(string scenario)
    {
        var authCalls = 0;
        var request = scenario == "expired"
            ? CreateRequest(new BrokerEnvelope(Correlation, Nonce, DateTimeOffset.UtcNow.AddSeconds(-1)))
            : CreateRequest();
        await using var fixture = await SessionFixture.ConnectAsync(() => Interlocked.Increment(ref authCalls) == 1, request);
        var envelope = request.Envelope;
        BrokerMessage initial = scenario switch
        {
            "nonce" => new BrokerHello(new BrokerEnvelope(Correlation, new BrokerNonce(new string('f', 64)), envelope.ExpiresAtUtc)),
            "correlation" => new BrokerHello(new BrokerEnvelope(Other, Nonce, envelope.ExpiresAtUtc)),
            "expired" => new BrokerHello(envelope),
            "request-without-hello" => request,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        await fixture.SendAsync(initial);

        Assert.Equal(BrokerSessionOutcome.Rejected, await fixture.Outcome);
        Assert.Equal(0, authCalls);
    }

    [Fact]
    public async Task UnauthenticatedPeerCannotEnterTheBatch()
    {
        var observed = 0;
        await using var fixture = await SessionFixture.ConnectAsync(() =>
        {
            Interlocked.Increment(ref observed);
            return false;
        });
        await fixture.SendAsync(new BrokerHello(fixture.Request.Envelope));
        Assert.Equal(BrokerSessionOutcome.Rejected, await fixture.Outcome);
        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task SessionRechecksPeerAuthenticationBeforeDispatch()
    {
        var request = CreateRequest();
        var authenticationChecks = 0;
        using var timeout = new CancellationTokenSource(Deadline);
        await using var stream = await BufferedSessionStream.CreateAsync(timeout.Token,
            new BrokerHello(request.Envelope), request);

        var outcome = await BrokerSession.RunAsync(
            stream, BrokerLaunchTicket.Create(Environment.ProcessId, request),
            () => ++authenticationChecks == 1, timeout.Token);

        Assert.Equal(BrokerSessionOutcome.Rejected, outcome);
        Assert.Equal(2, authenticationChecks);
        var responses = await stream.ReadResponsesAsync(timeout.Token);
        Assert.True(responses is [BrokerHelloAck], "A rejected batch may only acknowledge the initial hello.");
    }

    [Fact]
    public async Task ACurrentEnvelopeCannotAuthorizeDifferentRequestContent()
    {
        await using var fixture = await SessionFixture.ConnectAsync(static () => true);
        await fixture.HandshakeAsync();
        var changed = CreateRequest(fixture.Request.Envelope, approval: Other);
        await fixture.SendAsync(changed);

        Assert.Equal(BrokerSessionOutcome.Rejected, await fixture.Outcome);
    }

    [Fact]
    public async Task WaitingForTheBatchTimesOutAsInterrupted()
    {
        await using var fixture = await SessionFixture.ConnectAsync(static () => true);
        await fixture.HandshakeAsync();
        fixture.ExpireTestWait();

        Assert.Equal(BrokerSessionOutcome.Interrupted, await fixture.Outcome);
    }

    [Fact]
    public async Task ClientDisconnectBeforeRequestReturnsInterrupted()
    {
        await using var fixture = await SessionFixture.ConnectAsync(static () => true);
        await fixture.HandshakeAsync();
        await fixture.CloseClientAsync();

        Assert.Equal(BrokerSessionOutcome.Interrupted, await fixture.Outcome);
    }

    [Fact]
    public async Task BufferedCancellationProducesOnlyItsScopedNoOpResult()
    {
        var request = CreateRequest();
        using var timeout = new CancellationTokenSource(Deadline);
        await using var stream = await BufferedSessionStream.CreateAsync(timeout.Token,
            new BrokerHello(request.Envelope), request, new BrokerCancel(request.Envelope, ActionOne));
        var outcome = await BrokerSession.RunAsync(
            stream, BrokerLaunchTicket.Create(Environment.ProcessId, request), static () => true, timeout.Token);
        Assert.Equal(BrokerSessionOutcome.Completed, outcome);
        var responses = await stream.ReadResponsesAsync(timeout.Token);
        Assert.Collection(responses.OfType<BrokerResult>(),
            first =>
            {
                Assert.Equal(ActionOne, first.ActionId);
                Assert.Equal(BrokerResultStatus.Cancelled, first.Status);
            },
            second =>
            {
                Assert.Equal(ActionTwo, second.ActionId);
                Assert.Equal(BrokerResultStatus.Applied, second.Status);
            });
    }

    [Fact]
    public async Task ASecondRequestInTheSameSessionIsRejected()
    {
        var request = CreateRequest();
        using var timeout = new CancellationTokenSource(Deadline);
        await using var stream = await BufferedSessionStream.CreateAsync(timeout.Token,
            new BrokerHello(request.Envelope), request, request);
        var outcome = await BrokerSession.RunAsync(
            stream, BrokerLaunchTicket.Create(Environment.ProcessId, request), static () => true, timeout.Token);

        Assert.Equal(BrokerSessionOutcome.Rejected, outcome);
        var responses = await stream.ReadResponsesAsync(timeout.Token);
        Assert.IsType<BrokerHelloAck>(Assert.Single(responses));
    }

    [Theory]
    [InlineData(WindowsPlatformEligibility.UnsupportedOperatingSystem)]
    [InlineData(WindowsPlatformEligibility.UnsupportedArchitecture)]
    [InlineData(WindowsPlatformEligibility.UnverifiedIdentity)]
    public async Task PlatformRefusalStopsBeforeBrokerStartup(WindowsPlatformEligibility eligibility)
    {
        var startupCalls = 0;
        var result = await Program.RunOnSupportedPlatform(eligibility, () =>
        {
            startupCalls++;
            return Task.FromResult(0);
        });

        Assert.Equal(2, result);
        Assert.Equal(0, startupCalls);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task UnelevatedApphostRejectsAValidTicketWithoutStartingAPipe()
    {
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var apphost = Path.Combine(AppContext.BaseDirectory, "ThirdLife.Broker.exe");
        Assert.True(File.Exists(apphost), "The built broker apphost is required.");
        var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, CreateRequest());
        var start = new ProcessStartInfo(apphost)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in ticket.CreateArguments())
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = new CancellationTokenSource(Deadline);
        using var process = Assert.IsType<Process>(Process.Start(start));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await process.StandardOutput.ReadToEndAsync(timeout.Token));
            Assert.Empty(await process.StandardError.ReadToEndAsync(timeout.Token));
            using var client = WindowsBrokerPipe.CreateClient(ticket.PipeId);
            await Assert.ThrowsAsync<TimeoutException>(() => client.ConnectAsync(25));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                using var cleanup = new CancellationTokenSource(Deadline);
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    private static BrokerRequest CreateRequest(BrokerEnvelope? envelope = null, Guid? approval = null, JobId? job = null) =>
        new(envelope ?? new BrokerEnvelope(Correlation, Nonce, DateTimeOffset.UtcNow.AddSeconds(30)),
            job ?? Job, approval ?? Approval, new string('b', 64), new string('c', 64),
            [ActionRegistry.CreateNoOp(ActionOne), ActionRegistry.CreateNoOp(ActionTwo)]);

    private sealed class BufferedSessionStream(MemoryStream input) : Stream
    {
        private readonly MemoryStream _responses = new();

        public static async Task<BufferedSessionStream> CreateAsync(CancellationToken token, params BrokerMessage[] messages)
        {
            var input = new MemoryStream();
            try
            {
                foreach (var message in messages)
                {
                    await BrokerPipeFrames.WriteAsync(input, message, token);
                }

                input.Position = 0;
                return new BufferedSessionStream(input);
            }
            catch
            {
                await input.DisposeAsync();
                throw;
            }
        }

        public async Task<IReadOnlyList<BrokerMessage>> ReadResponsesAsync(CancellationToken token)
        {
            _responses.Position = 0;
            var result = new List<BrokerMessage>();
            while (_responses.Position < _responses.Length)
            {
                result.Add(await BrokerPipeFrames.ReadAsync(_responses, token));
            }

            return result;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Position == input.Length)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return input.Read(buffer.Span);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _responses.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                input.Dispose();
                _responses.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SessionFixture : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _server;
        private readonly NamedPipeClientStream _client;
        private readonly CancellationTokenSource _timeout;

        private SessionFixture(NamedPipeServerStream server, NamedPipeClientStream client, CancellationTokenSource timeout,
            BrokerRequest request, BrokerLaunchTicket ticket, Func<bool> authenticate)
        {
            _server = server;
            _client = client;
            _timeout = timeout;
            Request = request;
            Ticket = ticket;
            Outcome = BrokerSession.RunAsync(server, ticket, authenticate, timeout.Token);
        }

        public BrokerRequest Request { get; }
        public BrokerLaunchTicket Ticket { get; }
        public Task<BrokerSessionOutcome> Outcome { get; }

        public static async Task<SessionFixture> ConnectAsync(Func<bool> authenticate, BrokerRequest? request = null)
        {
            request ??= CreateRequest();
            var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
            var name = "thirdlife-test-session-" + ticket.PipeId.ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var timeout = new CancellationTokenSource(Deadline);
            try
            {
                var accepting = server.WaitForConnectionAsync(timeout.Token);
                await client.ConnectAsync(timeout.Token);
                await accepting;
                return new SessionFixture(server, client, timeout, request, ticket, authenticate);
            }
            catch
            {
                await client.DisposeAsync();
                await server.DisposeAsync();
                timeout.Dispose();
                throw;
            }
        }

        public Task SendAsync(BrokerMessage message) => BrokerPipeFrames.WriteAsync(_client, message, _timeout.Token);

        public Task<BrokerMessage> ReadAsync() => BrokerPipeFrames.ReadAsync(_client, _timeout.Token);

        public async Task HandshakeAsync()
        {
            await SendAsync(new BrokerHello(Request.Envelope));
            var ack = Assert.IsType<BrokerHelloAck>(await ReadAsync());
            Assert.True(Ticket.MatchesEnvelope(ack.Envelope));
        }

        public void ExpireTestWait() => _timeout.CancelAfter(TimeSpan.FromMilliseconds(25));

        public ValueTask CloseClientAsync() => _client.DisposeAsync();

        public async ValueTask DisposeAsync()
        {
            await _timeout.CancelAsync();
            await _client.DisposeAsync();
            await _server.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
