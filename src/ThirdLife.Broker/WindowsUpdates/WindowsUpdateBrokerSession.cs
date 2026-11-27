using System.Text.Json;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;

namespace ThirdLife.Broker;

internal static class WindowsUpdateBrokerSession
{
    internal static async Task<BrokerSessionOutcome> RunAsync(Stream stream, BrokerLaunchTicket ticket,
        BrokerWindowsUpdateRequest request, Func<bool> authenticateClient, WindowsUpdateBrokerHandler handler, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(BrokerProtocolLimits.MaximumUpdateOperationLifetime + BrokerProtocolLimits.MaximumUpdateReceiptLifetime);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation.CancelAfter(BrokerProtocolLimits.MaximumUpdateOperationLifetime);
        var terminal = 0;
        var receive = ReceiveControlAsync();
        var outcome = BrokerSessionOutcome.Completed;
        BrokerSessionOutcome? controlOutcome;
        try
        {
            var result = await handler.RunAsync(request, authenticateClient, operation.Token).ConfigureAwait(false);
            Volatile.Write(ref terminal, 1);
            using var delivery = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            delivery.CancelAfter(BrokerProtocolLimits.MaximumUpdateReceiptLifetime);
            await BrokerPipeFrames.WriteAsync(stream, result, delivery.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException) { outcome = BrokerSessionOutcome.Interrupted; }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            controlOutcome = await receive.ConfigureAwait(false);
        }
        return controlOutcome ?? outcome;

        async Task<BrokerSessionOutcome?> ReceiveControlAsync()
        {
            var cancelled = false;
            BrokerSessionOutcome outcome;
            try
            {
                while (true)
                {
                    var message = await BrokerPipeFrames.ReadAsync(stream, lifetime.Token, ticket.ProtocolVersion).ConfigureAwait(false);
                    if (message is not BrokerCancel cancel || !ticket.MatchesEnvelope(cancel.Envelope) ||
                        cancel.ActionId != request.Action.ActionId || cancelled || !authenticateClient())
                    {
                        outcome = BrokerSessionOutcome.Rejected;
                        break;
                    }
                    cancelled = true;
                    await operation.CancelAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return null; }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { outcome = BrokerSessionOutcome.Rejected; }
            catch (IOException) when (Volatile.Read(ref terminal) != 0) { return null; }
            catch (IOException) { outcome = BrokerSessionOutcome.Interrupted; }

            await lifetime.CancelAsync().ConfigureAwait(false);
            return outcome;
        }
    }
}
