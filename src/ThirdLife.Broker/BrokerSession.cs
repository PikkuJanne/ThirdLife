using System.Collections.Immutable;
using System.Text.Json;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;

namespace ThirdLife.Broker;

internal enum BrokerSessionOutcome
{
    Completed,
    Rejected,
    Interrupted,
}

internal static class BrokerSession
{
    public static async Task<BrokerSessionOutcome> RunAsync(
        Stream stream, BrokerLaunchTicket ticket, Func<bool> authenticateClient, CancellationToken cancellationToken,
        WindowsUpdateBrokerHandler? updateHandler = null)
    {
        try
        {
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (ticket.ProtocolVersion == BrokerProtocolLimits.WindowsUpdateVersion)
            {
                var remaining = ticket.ExpiresAtUtc - TimeProvider.System.GetUtcNow();
                if (remaining <= TimeSpan.Zero || remaining > BrokerProtocolLimits.MaximumLifetime) { return BrokerSessionOutcome.Rejected; }
                admission.CancelAfter(remaining);
            }
            if (await BrokerPipeFrames.ReadAsync(stream, admission.Token, ticket.ProtocolVersion).ConfigureAwait(false) is not BrokerHello hello ||
                !IsCurrentEnvelope(ticket, hello.Envelope) || !authenticateClient())
            {
                return BrokerSessionOutcome.Rejected;
            }

            await BrokerPipeFrames.WriteAsync(stream, new BrokerHelloAck(hello.Envelope), admission.Token).ConfigureAwait(false);
            var message = await BrokerPipeFrames.ReadAsync(stream, admission.Token, ticket.ProtocolVersion).ConfigureAwait(false);
            if (message is BrokerWindowsUpdateRequest update)
            {
                if (updateHandler is null || !IsCurrentEnvelope(ticket, update.Envelope) || !ticket.MatchesRequest(update) || !authenticateClient())
                {
                    return BrokerSessionOutcome.Rejected;
                }
                return await WindowsUpdateBrokerSession.RunAsync(stream, ticket, update, authenticateClient, updateHandler, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (message is not BrokerRequest request ||
                !IsCurrentEnvelope(ticket, request.Envelope) || !ticket.MatchesRequest(request) ||
                !authenticateClient() || !IsAllowedBatch(request.Actions))
            {
                return BrokerSessionOutcome.Rejected;
            }

            return await RunNoOpBatchAsync(stream, ticket, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return BrokerSessionOutcome.Rejected;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return BrokerSessionOutcome.Interrupted;
        }
    }

    private static bool IsAllowedBatch(ImmutableArray<ActionRequest> actions) =>
        actions.All(action => action is NoOpActionRequest &&
            ActionRegistry.Describe(action) is
            {
                Kind: ActionKind.NoOp,
                Scope: ActionScope.Machine,
                Privilege: ActionPrivilege.Elevated,
                Reboot: ActionReboot.None,
                NetworkUse: ActionNetworkUse.None,
                Verification: ActionVerification.NoMachineChange,
                Rollback: ActionRollback.NoChanges,
                Idempotency: ActionIdempotency.Repeatable,
                Availability: ActionAvailability.BrokerNoOp,
            });

    private static async Task<BrokerSessionOutcome> RunNoOpBatchAsync(
        Stream stream, BrokerLaunchTicket ticket, BrokerRequest request, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var state = new CancellationState(request.Actions);
        var receive = ReceiveCancellationAsync(stream, ticket, state, lifetime);
        BrokerSessionOutcome? controlOutcome;
        var outcome = BrokerSessionOutcome.Completed;
        var sequence = 0;
        try
        {
            foreach (var action in request.Actions)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (!IsCurrentEnvelope(ticket, request.Envelope))
                {
                    outcome = BrokerSessionOutcome.Interrupted;
                    break;
                }

                await BrokerPipeFrames.WriteAsync(stream,
                    new BrokerProgress(request.Envelope, action.ActionId, ++sequence, BrokerProgressStage.Accepted),
                    lifetime.Token).ConfigureAwait(false);
                if (!IsCurrentEnvelope(ticket, request.Envelope))
                {
                    outcome = BrokerSessionOutcome.Interrupted;
                    break;
                }

                // The sole v1 backend is a no-op. Take the cancellation decision once before reporting its outcome.
                var status = state.CommitResult(action.ActionId);
                await BrokerPipeFrames.WriteAsync(stream, new BrokerResult(request.Envelope, action.ActionId, ++sequence, status),
                    lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            outcome = BrokerSessionOutcome.Interrupted;
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            controlOutcome = await receive.ConfigureAwait(false);
        }

        return controlOutcome ?? outcome;
    }

    private static async Task<BrokerSessionOutcome?> ReceiveCancellationAsync(
        Stream stream, BrokerLaunchTicket ticket, CancellationState state, CancellationTokenSource lifetime)
    {
        BrokerSessionOutcome outcome;
        try
        {
            while (true)
            {
                if (await BrokerPipeFrames.ReadAsync(stream, lifetime.Token).ConfigureAwait(false) is not BrokerCancel cancel ||
                    !IsCurrentEnvelope(ticket, cancel.Envelope) || !state.TryCancel(cancel.ActionId))
                {
                    outcome = BrokerSessionOutcome.Rejected;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            outcome = BrokerSessionOutcome.Rejected;
        }
        catch (IOException) when (state.AllResultsCommitted)
        {
            // A client may close after receiving the last result. The writer separately checks for a failed send.
            return null;
        }
        catch (IOException)
        {
            outcome = BrokerSessionOutcome.Interrupted;
        }

        await lifetime.CancelAsync().ConfigureAwait(false);
        return outcome;
    }

    private static bool IsCurrentEnvelope(BrokerLaunchTicket ticket, BrokerEnvelope envelope) =>
        ticket.MatchesEnvelope(envelope) &&
        BrokerProtocolLimits.IsWithinWindow(envelope.ExpiresAtUtc, TimeProvider.System.GetUtcNow());

    private sealed class CancellationState(ImmutableArray<ActionRequest> actions)
    {
        private readonly Lock _gate = new();
        private readonly HashSet<Guid> _allowed = actions.Select(action => action.ActionId).ToHashSet();
        private readonly HashSet<Guid> _cancelled = [];
        private int _committedResults;

        public bool AllResultsCommitted => Volatile.Read(ref _committedResults) == _allowed.Count;

        public bool TryCancel(Guid actionId)
        {
            lock (_gate)
            {
                return _allowed.Contains(actionId) && _cancelled.Add(actionId);
            }
        }

        public BrokerResultStatus CommitResult(Guid actionId)
        {
            lock (_gate)
            {
                Volatile.Write(ref _committedResults, _committedResults + 1);
                return _cancelled.Contains(actionId) ? BrokerResultStatus.Cancelled : BrokerResultStatus.Applied;
            }
        }
    }
}
