using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.Json;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.UI.BrokerClient;

// One owned elevated process per exact phase. The authenticated handshake grants no permission to send.
internal sealed class ProtectedWindowsUpdateBackend : IWindowsUpdateDispatchBackend
{
    private static int active;

    public async Task<IWindowsUpdateDispatchChannel?> PrepareAsync(WindowsUpdateDispatch dispatch, CancellationToken token)
    {
        if (dispatch.ServicingPolicySha256 is not { } digest ||
            WindowsUpdateServicingPolicy.CurrentQualificationPolicySha256(dispatch.Exact, DateTimeOffset.UtcNow) != digest ||
            dispatch.ApprovedActions.ApprovalId != dispatch.ApprovalId || !dispatch.ApprovedActions.MatchesCurrentActions(dispatch.Exact) ||
            (dispatch.Action != dispatch.ApprovedActions.Download && dispatch.Action != dispatch.ApprovedActions.Install) ||
            !BrokerProtocolLimits.IsWithinWindow(dispatch.ExpiresAtUtc, DateTimeOffset.UtcNow) ||
            WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported ||
            Interlocked.CompareExchange(ref active, 1, 0) != 0) { return null; }

        Process? process = null;
        var release = true;
        try
        {
            var identity = WindowsBrokerPeer.CurrentIdentity();
            if (identity.IsElevated || identity.SessionId <= 0) { return null; }
            token.ThrowIfCancellationRequested();
            var envelope = BrokerEnvelope.ForWindowsUpdate(Guid.NewGuid(), BrokerNonce.Create(), dispatch.ExpiresAtUtc);
            var request = BrokerWindowsUpdateRequest.Create(envelope, dispatch.JobId, dispatch.GateId, dispatch.ScanId,
                dispatch.PolicyDecisionId, digest, dispatch.ApprovedActions, dispatch.Exact, dispatch.Action.ActionId);
            var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
            var launch = TrustedBrokerLauncher.LaunchAsync(ticket, token);
            try { process = await launch.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                release = false;
                _ = ReleasePendingLaunchAsync(launch);
                throw;
            }

            if (process is null || process.HasExited) { return null; }
            var connection = await AuthenticatedBrokerConnection.ConnectAsync(process, identity, ticket, envelope, token).ConfigureAwait(false);
            if (connection is null) { return null; }
            var channel = new Channel(dispatch, ticket, request, process, connection);
            process = null;
            release = false;
            return channel;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            throw new WindowsUpdateConsentDeclinedException();
        }
        catch (Exception error) when (IsTransportFailure(error)) { return null; }
        finally
        {
            process?.Dispose();
            if (release) { Volatile.Write(ref active, 0); }
        }
    }

    private static async Task ReleasePendingLaunchAsync(Task<Process?> launch)
    {
        try { using var completed = await launch.ConfigureAwait(false); }
        catch (Exception error) when (IsTransportFailure(error) || error is OperationCanceledException) { }
        finally { Volatile.Write(ref active, 0); }
    }

    private static bool IsTransportFailure(Exception error) => error is IOException or InvalidDataException or JsonException
        or Win32Exception or UnauthorizedAccessException or InvalidOperationException or SecurityException or ArgumentException;

    private sealed class Channel(WindowsUpdateDispatch dispatch, BrokerLaunchTicket ticket, BrokerWindowsUpdateRequest request,
        Process process, AuthenticatedBrokerConnection connection) : IWindowsUpdateDispatchChannel
    {
        private int _used;
        public WindowsUpdateDispatch Dispatch { get; } = dispatch;

        public async Task<WindowsUpdateDispatchReceipt?> SendAsync(CancellationToken token)
        {
            if (Interlocked.Exchange(ref _used, 1) != 0) { return null; }
            try
            {
                using var deadline = new CancellationTokenSource(BrokerProtocolLimits.MaximumUpdateOperationLifetime +
                    BrokerProtocolLimits.MaximumUpdateReceiptLifetime);
                // The request must enter the pipe within the original approval window. An admitted operation
                // may run longer. A late or missing reply cannot establish that Windows cancelled servicing.
                var remaining = Dispatch.ExpiresAtUtc - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero || remaining > WindowsUpdateHistory.DispatchWindow) { return null; }
                using var sending = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
                sending.CancelAfter(remaining);
                await BrokerPipeFrames.WriteAsync(connection.Pipe, request, sending.Token).ConfigureAwait(false);
                var result = await ReadResultAsync(connection.Pipe, ticket, request, Dispatch.Action.ActionId, token, deadline.Token)
                    .ConfigureAwait(false);
                if (result is null) { return null; }
                using var closing = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                closing.CancelAfter(BrokerProtocolLimits.MaximumUpdateReceiptLifetime);
                try
                {
                    _ = await BrokerPipeFrames.ReadAsync(connection.Pipe, closing.Token, ticket.ProtocolVersion).ConfigureAwait(false);
                    return null;
                }
                catch (EndOfStreamException) { }
                await process.WaitForExitAsync(closing.Token).ConfigureAwait(false);
                return process.ExitCode == 0
                    ? new(Dispatch, result.Operation, result.DispatchState == BrokerWindowsUpdateDispatchState.NotStarted) : null;
            }
            catch (Exception error) when (IsTransportFailure(error) || error is OperationCanceledException) { return null; }
        }

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            process.Dispose();
            Volatile.Write(ref active, 0);
            return ValueTask.CompletedTask;
        }
    }

    internal static async Task<BrokerWindowsUpdateResult?> ReadResultAsync(Stream pipe, BrokerLaunchTicket ticket,
        BrokerWindowsUpdateRequest request, Guid actionId, CancellationToken cancel, CancellationToken deadline)
    {
        var read = BrokerPipeFrames.ReadAsync(pipe, deadline, ticket.ProtocolVersion);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancel.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), cancelled);
        if (!read.IsCompleted && await Task.WhenAny(read, cancelled.Task).ConfigureAwait(false) == cancelled.Task && !read.IsCompleted)
        {
            try { await BrokerPipeFrames.WriteAsync(pipe, new BrokerCancel(request.Envelope, actionId), deadline).ConfigureAwait(false); }
            catch (IOException) when (read.IsCompletedSuccessfully) { }
        }
        if (await read.ConfigureAwait(false) is not BrokerWindowsUpdateResult result || !ticket.MatchesEnvelope(result.Envelope) ||
            result.ActionId != actionId || result.ApprovalSha256 != request.ApprovalSha256 ||
            result.DispatchState == BrokerWindowsUpdateDispatchState.Unconfirmed) { return null; }
        return result;
    }
}
