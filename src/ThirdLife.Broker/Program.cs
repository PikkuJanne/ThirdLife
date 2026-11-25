using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Broker;

internal static class Program
{
    private const int Rejected = 2;
    private const int Interrupted = 3;

    private static Task<int> Main(string[] args)
    {
        var startup = Stopwatch.GetTimestamp();
        return RunOnSupportedPlatform(WindowsPlatform.CheckCurrent(), () => RunAsync(args, startup));
    }

    internal static Task<int> RunOnSupportedPlatform(WindowsPlatformEligibility platform, Func<Task<int>> run) =>
        platform == WindowsPlatformEligibility.Supported ? run() : Task.FromResult(Rejected);

    private static async Task<int> RunAsync(string[] args, long startup)
    {
        var ticket = BrokerLaunchTicket.TryParse(args);
        if (ticket is null || ticket.ClientProcessId == Environment.ProcessId ||
            !BrokerProtocolLimits.IsWithinWindow(ticket.ExpiresAtUtc, TimeProvider.System.GetUtcNow()))
        {
            return Rejected;
        }

        try
        {
            var elevated = WindowsBrokerPeer.CurrentIdentity();
            if (!elevated.IsElevated || elevated.SessionId <= 0)
            {
                return Rejected;
            }

            // Keep the client process object open through teardown so its identifier remains pinned.
            using var client = WindowsBrokerPeer.Open(ticket.ClientProcessId);
            if (!client.IsAlive || client.Identity.IsElevated || !SameUserSession(elevated, client.Identity))
            {
                return Rejected;
            }

            var remainingUntilExpiry = ticket.ExpiresAtUtc - TimeProvider.System.GetUtcNow();
            var remainingFromStartup = BrokerProtocolLimits.MaximumLifetime - Stopwatch.GetElapsedTime(startup);
            var remaining = remainingUntilExpiry < remainingFromStartup ? remainingUntilExpiry : remainingFromStartup;
            if (remaining <= TimeSpan.Zero)
            {
                return Interrupted;
            }

            using var lifetime = new CancellationTokenSource(remaining);
            using var pipe = WindowsBrokerPipe.CreateServer(ticket.PipeId, client.Identity.LogonSid);
            await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
            if (!IsExpectedConnection(pipe, client))
            {
                return Rejected;
            }

            var outcome = await BrokerSession.RunAsync(pipe, ticket, () => AuthenticateClient(pipe, client), lifetime.Token)
                .ConfigureAwait(false);
            return outcome switch
            {
                BrokerSessionOutcome.Completed => 0,
                BrokerSessionOutcome.Rejected => Rejected,
                _ => Interrupted,
            };
        }
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException or IOException or InvalidDataException or
            ArgumentException or InvalidOperationException or SecurityException or OperationCanceledException)
        {
            // A fixed exit code keeps native details, identifiers and session values out of output.
            return Interrupted;
        }
    }

    private static bool IsExpectedConnection(NamedPipeServerStream pipe, WindowsBrokerPeer client) =>
        client.IsAlive && WindowsBrokerPipe.HasClientProcess(pipe, client.ProcessId, client.Identity.SessionId);

    private static bool AuthenticateClient(NamedPipeServerStream pipe, WindowsBrokerPeer client)
    {
        if (!IsExpectedConnection(pipe, client))
        {
            return false;
        }

        var observed = WindowsBrokerPipe.ReadImpersonatedClient(pipe);
        return !observed.IsElevated && SameUserSession(client.Identity, observed) &&
            client.Identity.LogonSid.Equals(observed.LogonSid);
    }

    private static bool SameUserSession(BrokerPeerIdentity expected, BrokerPeerIdentity observed) =>
        expected.SessionId == observed.SessionId && expected.UserSid.Equals(observed.UserSid);
}
