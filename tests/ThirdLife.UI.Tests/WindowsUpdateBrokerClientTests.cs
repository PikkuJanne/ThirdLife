using System.IO;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.UI.BrokerClient;

namespace ThirdLife.UI.Tests;

public sealed class WindowsUpdateBrokerClientTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("refused")]
    [InlineData("unconfirmed")]
    [InlineData("other-phase")]
    [InlineData("other-approval")]
    [InlineData("other-session")]
    public async Task OnlyCorrelatedConfirmedBrokerReceiptsReachTheJournal(string scenario)
    {
        var exact = new WindowsUpdateCandidate(new(Guid.NewGuid(), 1), WindowsUpdateClass.Security, 1024);
        var phases = WindowsUpdateApprovedActions.Create(Guid.NewGuid(), exact);
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        var envelope = BrokerEnvelope.ForWindowsUpdate(Guid.NewGuid(), BrokerNonce.Create(), deadline);
        var request = BrokerWindowsUpdateRequest.Create(envelope, new("fixture-job"), new("fixture-gate"), Guid.NewGuid(),
            $"policy-decision-{Guid.NewGuid():N}", new string('a', 64), phases, exact, phases.Download.ActionId);
        var replyEnvelope = scenario == "other-session" ? BrokerEnvelope.ForWindowsUpdate(Guid.NewGuid(), BrokerNonce.Create(), deadline) : envelope;
        var state = scenario switch
        {
            "unconfirmed" => BrokerWindowsUpdateDispatchState.Unconfirmed,
            "refused" => BrokerWindowsUpdateDispatchState.NotStarted,
            _ => BrokerWindowsUpdateDispatchState.Attempted,
        };
        var outcome = state switch
        {
            BrokerWindowsUpdateDispatchState.Unconfirmed => new WindowsUpdateOperation(WindowsUpdateStatus.Partial, null),
            BrokerWindowsUpdateDispatchState.NotStarted => new WindowsUpdateOperation(WindowsUpdateStatus.AccessDenied, null),
            _ => new WindowsUpdateOperation(WindowsUpdateStatus.Succeeded, false, 100),
        };
        var reply = new BrokerWindowsUpdateResult(replyEnvelope,
            scenario == "other-phase" ? phases.Install.ActionId : phases.Download.ActionId,
            scenario == "other-approval" ? new string('b', 64) : request.ApprovalSha256, state, outcome);
        using var stream = new MemoryStream();
        await BrokerPipeFrames.WriteAsync(stream, reply, CancellationToken.None);
        stream.Position = 0;
        var receipt = await ProtectedWindowsUpdateBackend.ReadResultAsync(stream, BrokerLaunchTicket.Create(Environment.ProcessId, request),
            request, phases.Download.ActionId, CancellationToken.None, CancellationToken.None);
        if (scenario is "success" or "refused")
        {
            Assert.Equal(state, receipt!.DispatchState);
            Assert.Equal(outcome, receipt.Operation);
        }
        else { Assert.Null(receipt); }
    }
}
