using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.Jobs;
using ThirdLife.UI.BrokerClient;

namespace ThirdLife.UI.Tests;

public sealed class BrokerBatchClientTests
{
    [Fact]
    public async Task DevelopmentApphostDoesNotRequestElevation()
    {
        var result = await new BrokerBatchClient().RunAsync(Request());

        Assert.Equal(BrokerBatchStatus.Unavailable, result.Status);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotCallTheLauncher()
    {
        var attempts = 0;
        var client = new BrokerBatchClient(_ =>
        {
            attempts++;
            return Task.FromResult<Process?>(null);
        });

        var result = await client.RunAsync(Request(), cancellationToken: new CancellationToken(canceled: true));

        Assert.Equal(BrokerBatchStatus.Cancelled, result.Status);
        Assert.Empty(result.Results);
        Assert.Equal(0, attempts);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task WindowsConsentDeclineIsReturnedWithoutTerminalActions()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var attempts = 0;
        var client = new BrokerBatchClient(_ =>
        {
            attempts++;
            return Task.FromException<Process?>(new Win32Exception(1223));
        });

        var result = await client.RunAsync(Request());

        Assert.Equal(BrokerBatchStatus.Declined, result.Status);
        Assert.Empty(result.Results);
        Assert.Equal(1, attempts);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task CancellingPendingConsentReleasesTheCallerAndPreventsASecondLaunch()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var pendingLaunch = new TaskCompletionSource<Process?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var client = new BrokerBatchClient(_ =>
        {
            attempts++;
            return pendingLaunch.Task;
        });
        using var cancellation = new CancellationTokenSource();
        var request = Request();
        var running = client.RunAsync(request, cancellationToken: cancellation.Token);

        try
        {
            Assert.Equal(1, attempts);
            await cancellation.CancelAsync();
            var cancelled = await running.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await client.RunAsync(request).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(BrokerBatchStatus.Cancelled, cancelled.Status);
            Assert.Empty(cancelled.Results);
            Assert.Equal(BrokerBatchStatus.Unavailable, duplicate.Status);
            Assert.Equal(1, attempts);
        }
        finally
        {
            pendingLaunch.TrySetResult(null);
        }
    }

    [Fact]
    public async Task ReportsOnlyAnExactOrderedTerminalForEachAction()
    {
        var request = Request(ActionRegistry.CreateNoOp(Guid.NewGuid()), ActionRegistry.CreateNoOp(Guid.NewGuid()));
        var first = request.Actions[0].ActionId;
        var second = request.Actions[1].ActionId;
        var results = ImmutableArray.CreateBuilder<BrokerResult>();
        using var pipe = await ReplyStreamAsync(
            new BrokerProgress(request.Envelope, first, 1, BrokerProgressStage.Accepted),
            new BrokerResult(request.Envelope, first, 2, BrokerResultStatus.Applied),
            new BrokerProgress(request.Envelope, second, 3, BrokerProgressStage.Accepted),
            new BrokerResult(request.Envelope, second, 4, BrokerResultStatus.Cancelled));

        await BrokerBatchClient.ReceiveBatchAsync(pipe, Ticket(request), request, results, null,
            CancellationToken.None, CancellationToken.None);

        Assert.Equal([first, second], results.Select(result => result.ActionId));
        Assert.Equal([BrokerResultStatus.Applied, BrokerResultStatus.Cancelled], results.Select(result => result.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsOutOfOrderOrDuplicateTerminalReplies(bool repeatFirst)
    {
        var request = Request(ActionRegistry.CreateNoOp(Guid.NewGuid()), ActionRegistry.CreateNoOp(Guid.NewGuid()));
        var first = new BrokerResult(request.Envelope, request.Actions[0].ActionId, 1, BrokerResultStatus.Applied);
        var wrong = repeatFirst
            ? new BrokerResult(request.Envelope, request.Actions[0].ActionId, 2, BrokerResultStatus.Applied)
            : new BrokerResult(request.Envelope, request.Actions[1].ActionId, 1, BrokerResultStatus.Applied);
        var results = ImmutableArray.CreateBuilder<BrokerResult>();
        using var pipe = await ReplyStreamAsync(repeatFirst ? [first, wrong] : [wrong]);

        await Assert.ThrowsAsync<InvalidDataException>(() => BrokerBatchClient.ReceiveBatchAsync(
            pipe, Ticket(request), request, results, null, CancellationToken.None, CancellationToken.None));

        Assert.Equal(repeatFirst ? 1 : 0, results.Count);
    }

    [Fact]
    public async Task BrokerLossPreservesAcceptedPartialResults()
    {
        var request = Request(ActionRegistry.CreateNoOp(Guid.NewGuid()), ActionRegistry.CreateNoOp(Guid.NewGuid()));
        var first = request.Actions[0].ActionId;
        var results = ImmutableArray.CreateBuilder<BrokerResult>();
        using var pipe = await ReplyStreamAsync(new BrokerResult(request.Envelope, first, 1, BrokerResultStatus.Applied));

        await Assert.ThrowsAsync<EndOfStreamException>(() => BrokerBatchClient.ReceiveBatchAsync(
            pipe, Ticket(request), request, results, null, CancellationToken.None, CancellationToken.None));

        Assert.Equal(first, Assert.Single(results).ActionId);
    }

    private static BrokerRequest Request(params ActionRequest[] actions) => new(
        new BrokerEnvelope(Guid.NewGuid(), BrokerNonce.Create(), TimeProvider.System.GetUtcNow().AddSeconds(30)),
        new JobId("job-broker-fixture"), Guid.NewGuid(), new string('a', 64), new string('b', 64),
        actions.Length == 0 ? [ActionRegistry.CreateNoOp(Guid.NewGuid())] : [.. actions]);

    private static BrokerLaunchTicket Ticket(BrokerRequest request) => BrokerLaunchTicket.Create(Environment.ProcessId, request);

    private static async Task<MemoryStream> ReplyStreamAsync(params BrokerMessage[] messages)
    {
        var stream = new MemoryStream();
        foreach (var message in messages)
        {
            await BrokerPipeFrames.WriteAsync(stream, message, CancellationToken.None);
        }

        stream.Position = 0;
        return stream;
    }
}
