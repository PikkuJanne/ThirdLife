using System.ComponentModel;
using System.Security;
using System.Security.Principal;
using ThirdLife.Actions.Contracts;
using ThirdLife.Broker.Protocol;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Broker;

internal interface IWindowsUpdateBrokerBackend
{
    Task<WindowsUpdateOperation> ApplyAsync(WindowsUpdateActionRequest action,
        Func<WindowsUpdatePreparation, bool> authorizeBegin, CancellationToken token);
}

// An implementation obtains its own native offer and keeps it alive through authorizeBegin
// and the one Begin call. All decisions at the boundary are broker-owned.
internal sealed class WindowsUpdateBrokerHandler(
    IWindowsUpdateBrokerBackend backend,
    Func<WindowsUpdateCandidate, DateTimeOffset, string?> currentPolicy,
    Func<WindowsUpdateCandidate, string, WindowsUpdatePreparation, DateTimeOffset, DateTimeOffset, WindowsUpdateServicingDecision> evaluate,
    Func<BrokerWindowsUpdateRequest, Func<DateTimeOffset, bool>, bool> consumeGrant,
    TimeProvider clock)
{
    internal static WindowsUpdateBrokerHandler Create(IWindowsUpdateBrokerBackend backend, SecurityIdentifier logonSid) =>
        new(backend, WindowsUpdateServicingPolicy.CurrentQualificationPolicySha256, WindowsUpdateServicingPolicy.EvaluateQualification,
            (request, authorize) => WindowsUpdates.WindowsUpdateQualificationGrant.TryConsume(request, logonSid, authorize), TimeProvider.System);

    internal async Task<BrokerWindowsUpdateResult> RunAsync(BrokerWindowsUpdateRequest request, Func<bool> authenticateClient,
        CancellationToken token)
    {
        var startedAt = clock.GetUtcNow();
        var exact = request.Action.Exact!;
        const int open = 0, checking = 1, authorized = 2, closed = 3;
        var admission = open;
        var startedTick = clock.GetTimestamp();
        var refusal = WindowsUpdateStatus.ApprovalChanged;
        try
        {
            if (token.IsCancellationRequested) { return NotStarted(WindowsUpdateStatus.Cancelled); }
            if (!BrokerProtocolLimits.IsWithinWindow(request.Envelope.ExpiresAtUtc, startedAt)) { return NotStarted(WindowsUpdateStatus.TimedOut); }
            if (currentPolicy(exact, startedAt) != request.PolicySha256) { return NotStarted(refusal); }

            var operation = await backend.ApplyAsync(request.Action, AuthorizeBegin, token).ConfigureAwait(false);
            // A cancelled native worker can finish its lookup after the caller stopped
            // waiting. Close the same gate it must win before returning permission to Begin.
            var boundary = Interlocked.Exchange(ref admission, closed);
            if (boundary != authorized)
            {
                var status = token.IsCancellationRequested ? WindowsUpdateStatus.Cancelled :
                    boundary == checking ? refusal : operation.Status;
                return NotStarted(status is WindowsUpdateStatus.Succeeded or WindowsUpdateStatus.Partial ? WindowsUpdateStatus.Unavailable : status);
            }
            return operation.Status is WindowsUpdateStatus.Succeeded or WindowsUpdateStatus.Failed or WindowsUpdateStatus.Cancelled
                ? Result(BrokerWindowsUpdateDispatchState.Attempted, operation) : Unconfirmed();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or
            Win32Exception or InvalidOperationException or ArgumentException or OperationCanceledException or System.Runtime.InteropServices.ExternalException)
        {
            return Interlocked.Exchange(ref admission, closed) == authorized ? Unconfirmed() :
                NotStarted(token.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.Unavailable);
        }
        finally { Interlocked.Exchange(ref admission, closed); }

        bool AuthorizeBegin(WindowsUpdatePreparation observation)
        {
            if (Interlocked.CompareExchange(ref admission, checking, open) != open || !MayConsume(clock.GetUtcNow())) { return false; }
            if (!consumeGrant(request, MayConsume)) { return false; }
            return Interlocked.CompareExchange(ref admission, authorized, checking) == checking;

            bool MayConsume(DateTimeOffset now)
            {
                if (Volatile.Read(ref admission) != checking || token.IsCancellationRequested) { refusal = WindowsUpdateStatus.Cancelled; return false; }
                if (now < startedAt || !BrokerProtocolLimits.IsWithinWindow(request.Envelope.ExpiresAtUtc, now) ||
                    clock.GetElapsedTime(startedTick) >= request.Envelope.ExpiresAtUtc - startedAt)
                {
                    refusal = WindowsUpdateStatus.TimedOut;
                    return false;
                }
                if (!authenticateClient()) { refusal = WindowsUpdateStatus.AccessDenied; return false; }
                return evaluate(exact, request.PolicySha256, observation, startedAt, now) == WindowsUpdateServicingDecision.Allowed;
            }
        }

        BrokerWindowsUpdateResult Result(BrokerWindowsUpdateDispatchState state, WindowsUpdateOperation operation) =>
            new(request.Envelope, request.Action.ActionId, request.ApprovalSha256, state, operation);
        BrokerWindowsUpdateResult NotStarted(WindowsUpdateStatus status) => Result(BrokerWindowsUpdateDispatchState.NotStarted, new(status, null));
        BrokerWindowsUpdateResult Unconfirmed() => Result(BrokerWindowsUpdateDispatchState.Unconfirmed, new(WindowsUpdateStatus.Partial, null));
    }
}
