using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.Json;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.UI.BrokerClient;

public enum BrokerBatchStatus
{
    // The channel closed cleanly with all terminal replies. The caller still evaluates each outcome.
    Completed = 1,
    Declined,
    Cancelled,
    Unavailable,
    RequiresReview,
}

public sealed record BrokerBatchResult(BrokerBatchStatus Status, ImmutableArray<BrokerResult> Results);

/// <summary>Coordinates one explicitly requested broker batch from the unelevated application.</summary>
public sealed class BrokerBatchClient
{
    private const int WindowsConsentDeclined = 1223;
    private readonly Func<BrokerLaunchTicket, CancellationToken, Task<Process?>> _launch;
    private readonly Func<WindowsPlatformEligibility> _checkPlatform;
    private int _active;

    public BrokerBatchClient() : this(TrustedBrokerLauncher.LaunchAsync, checkPlatform: null) { }

    internal BrokerBatchClient(
        Func<BrokerLaunchTicket, Task<Process?>> launch,
        Func<WindowsPlatformEligibility>? checkPlatform = null) : this((ticket, _) => launch(ticket), checkPlatform)
    {
        ArgumentNullException.ThrowIfNull(launch);
    }

    private BrokerBatchClient(
        Func<BrokerLaunchTicket, CancellationToken, Task<Process?>> launch,
        Func<WindowsPlatformEligibility>? checkPlatform)
    {
        _launch = launch;
        _checkPlatform = checkPlatform ?? WindowsPlatform.CheckCurrent;
    }

    public Task<BrokerBatchResult> RunAsync(BrokerRequest request, IProgress<BrokerProgress>? progress = null,
        CancellationToken cancellationToken = default) => RunAsync(request, progress, canDispatch: null, cancellationToken);

    internal async Task<BrokerBatchResult> RunAsync(BrokerRequest request, IProgress<BrokerProgress>? progress,
        Func<CancellationToken, Task<bool>>? canDispatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return new BrokerBatchResult(BrokerBatchStatus.Cancelled, []);
        }

        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
        }

        var results = ImmutableArray.CreateBuilder<BrokerResult>(request.Actions.Length);
        var dispatchAttempted = false;
        var releaseOnLaunchCompletion = false;
        try
        {
            var now = TimeProvider.System.GetUtcNow();
            var envelope = request.Envelope;
            if (!BrokerProtocolLimits.IsWithinWindow(envelope.ExpiresAtUtc, now) ||
                _checkPlatform() != WindowsPlatformEligibility.Supported)
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            var client = WindowsBrokerPeer.CurrentIdentity();
            if (client.IsElevated || client.SessionId <= 0)
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            var ticket = BrokerLaunchTicket.Create(Environment.ProcessId, request);
            using var deadline = new CancellationTokenSource(envelope.ExpiresAtUtc - now);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
            var launch = _launch(ticket, startup.Token);
            Process? started;
            try
            {
                started = await launch.WaitAsync(startup.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                releaseOnLaunchCompletion = true;
                _ = ReleasePendingLaunchAsync(launch);
                return new BrokerBatchResult(cancellationToken.IsCancellationRequested
                    ? BrokerBatchStatus.Cancelled : BrokerBatchStatus.Unavailable, []);
            }

            using var process = started;
            if (process is null || process.HasExited)
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            using var broker = WindowsBrokerPeer.Open(process.Id);
            if (!broker.IsAlive || !broker.Identity.IsElevated || !broker.Identity.UserSid.Equals(client.UserSid) ||
                broker.Identity.SessionId != client.SessionId)
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            using var pipe = WindowsBrokerPipe.CreateClient(ticket.PipeId);
            await pipe.ConnectAsync(startup.Token).ConfigureAwait(false);
            if (process.HasExited || !broker.IsAlive || !WindowsBrokerPipe.HasServerProcess(pipe, broker.ProcessId, client.SessionId))
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            await BrokerPipeFrames.WriteAsync(pipe, new BrokerHello(envelope), startup.Token).ConfigureAwait(false);
            if (await BrokerPipeFrames.ReadAsync(pipe, startup.Token).ConfigureAwait(false) is not BrokerHelloAck ack ||
                !ticket.MatchesEnvelope(ack.Envelope) || !BrokerProtocolLimits.IsWithinWindow(envelope.ExpiresAtUtc, TimeProvider.System.GetUtcNow()))
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            if (canDispatch is not null && !await canDispatch(startup.Token).ConfigureAwait(false))
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            startup.Token.ThrowIfCancellationRequested();
            if (!BrokerProtocolLimits.IsWithinWindow(envelope.ExpiresAtUtc, TimeProvider.System.GetUtcNow()))
            {
                return new BrokerBatchResult(BrokerBatchStatus.Unavailable, []);
            }

            dispatchAttempted = true;
            await BrokerPipeFrames.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
            await ReceiveBatchAsync(pipe, ticket, request, results, progress, cancellationToken, deadline.Token).ConfigureAwait(false);
            await RequireCleanCloseAsync(pipe, deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return new BrokerBatchResult(BrokerBatchStatus.RequiresReview, results.ToImmutable());
            }

            return new BrokerBatchResult(results.Any(result => result.Status == BrokerResultStatus.RequiresReview)
                ? BrokerBatchStatus.RequiresReview : results.Any(result => result.Status == BrokerResultStatus.Cancelled)
                    ? BrokerBatchStatus.Cancelled : BrokerBatchStatus.Completed, results.ToImmutable());
        }
        catch (Win32Exception exception) when (!dispatchAttempted && exception.NativeErrorCode == WindowsConsentDeclined)
        {
            return new BrokerBatchResult(BrokerBatchStatus.Declined, []);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or Win32Exception or
            UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or SecurityException)
        {
            var status = dispatchAttempted ? BrokerBatchStatus.RequiresReview : cancellationToken.IsCancellationRequested
                ? BrokerBatchStatus.Cancelled : BrokerBatchStatus.Unavailable;
            return new BrokerBatchResult(status, results.ToImmutable());
        }
        finally
        {
            if (!releaseOnLaunchCompletion) { Volatile.Write(ref _active, 0); }
        }
    }

    private async Task ReleasePendingLaunchAsync(Task<Process?> launch)
    {
        try
        {
            using var process = await launch.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException or
            InvalidOperationException or OperationCanceledException or SecurityException)
        {
            // The caller already left the consent wait. The original launch result carries no journal authority.
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }

    internal static async Task ReceiveBatchAsync(Stream pipe, BrokerLaunchTicket ticket, BrokerRequest request,
        ImmutableArray<BrokerResult>.Builder results, IProgress<BrokerProgress>? progress,
        CancellationToken cancellationToken, CancellationToken deadline)
    {
        var previousStage = (BrokerProgressStage)0;
        var cancelRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), cancelRequested);
        var cancellationSent = false;
        var expectedSequence = 1;
        while (results.Count < request.Actions.Length)
        {
            var read = BrokerPipeFrames.ReadAsync(pipe, deadline);
            if (!cancellationSent && !read.IsCompleted &&
                await Task.WhenAny(read, cancelRequested.Task).ConfigureAwait(false) == cancelRequested.Task && !read.IsCompleted)
            {
                cancellationSent = true;
                try
                {
                    foreach (var action in request.Actions.Skip(results.Count))
                    {
                        await BrokerPipeFrames.WriteAsync(pipe, new BrokerCancel(request.Envelope, action.ActionId), deadline).ConfigureAwait(false);
                    }
                }
                catch (IOException) when (read.IsCompletedSuccessfully)
                {
                    // A final reply may arrive while a best-effort cancel is being sent.
                }
            }

            var message = await read.ConfigureAwait(false);
            if (!ticket.MatchesEnvelope(message.Envelope)) { throw InvalidReply(); }

            var actionId = request.Actions[results.Count].ActionId;
            switch (message)
            {
                case BrokerProgress update when update.Sequence == expectedSequence && update.ActionId == actionId &&
                    (int)update.Stage > (int)previousStage:
                    previousStage = update.Stage;
                    progress?.Report(update);
                    break;
                case BrokerResult result when result.Sequence == expectedSequence && result.ActionId == actionId:
                    results.Add(result);
                    previousStage = 0;
                    break;
                default:
                    throw InvalidReply();
            }

            expectedSequence++;
        }
    }

    private static InvalidDataException InvalidReply() => new("The broker reply does not match the approved batch.");

    private static async Task RequireCleanCloseAsync(Stream pipe, CancellationToken cancellationToken)
    {
        try
        {
            _ = await BrokerPipeFrames.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
            throw InvalidReply();
        }
        catch (EndOfStreamException)
        {
            // The broker closed without extra replies or a partial trailing frame.
        }
    }
}
