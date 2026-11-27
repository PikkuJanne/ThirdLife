using System.Diagnostics;
using System.IO.Pipes;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;

namespace ThirdLife.UI.BrokerClient;

internal sealed class AuthenticatedBrokerConnection : IDisposable
{
    private readonly WindowsBrokerPeer _peer;
    internal NamedPipeClientStream Pipe { get; }

    private AuthenticatedBrokerConnection(WindowsBrokerPeer peer, NamedPipeClientStream pipe)
    {
        _peer = peer;
        Pipe = pipe;
    }

    internal static async Task<AuthenticatedBrokerConnection?> ConnectAsync(Process process, BrokerPeerIdentity client,
        BrokerLaunchTicket ticket, BrokerEnvelope envelope, CancellationToken token)
    {
        var broker = WindowsBrokerPeer.Open(process.Id);
        NamedPipeClientStream? pipe = null;
        var transferred = false;
        try
        {
            if (!broker.IsAlive || !broker.Identity.IsElevated || !broker.Identity.UserSid.Equals(client.UserSid) ||
                broker.Identity.SessionId != client.SessionId) { return null; }

            pipe = WindowsBrokerPipe.CreateClient(ticket.PipeId);
            await pipe.ConnectAsync(token).ConfigureAwait(false);
            if (process.HasExited || !broker.IsAlive || !WindowsBrokerPipe.HasServerProcess(pipe, broker.ProcessId, client.SessionId))
            {
                return null;
            }

            await BrokerPipeFrames.WriteAsync(pipe, new BrokerHello(envelope), token).ConfigureAwait(false);
            if (await BrokerPipeFrames.ReadAsync(pipe, token, ticket.ProtocolVersion).ConfigureAwait(false) is not BrokerHelloAck ack ||
                !ticket.MatchesEnvelope(ack.Envelope) || !BrokerProtocolLimits.IsWithinWindow(envelope.ExpiresAtUtc, TimeProvider.System.GetUtcNow()))
            {
                return null;
            }
            transferred = true;
            return new(broker, pipe);
        }
        finally
        {
            if (!transferred) { pipe?.Dispose(); broker.Dispose(); }
        }
    }

    public void Dispose()
    {
        Pipe.Dispose();
        _peer.Dispose();
    }
}
