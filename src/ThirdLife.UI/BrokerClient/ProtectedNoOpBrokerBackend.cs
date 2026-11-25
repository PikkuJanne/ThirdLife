using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Execution;
using ThirdLife.Broker.Protocol;

namespace ThirdLife.UI.BrokerClient;

internal sealed class ProtectedNoOpBrokerBackend(BrokerBatchClient client) : INoOpBrokerBackend
{
    public async Task<NoOpBrokerBatchResult> RunAsync(NoOpExecutionBatch batch, Func<CancellationToken, Task<bool>> canDispatch,
        IProgress<NoOpExecutionProgress>? progress, CancellationToken cancellationToken)
    {
        var envelope = new BrokerEnvelope(batch.CorrelationId, BrokerNonce.Create(), batch.ExpiresAtUtc);
        var request = new BrokerRequest(envelope, batch.Plan.JobId, batch.ApprovalId, batch.Plan.PlanSha256, batch.Plan.ImpactSha256,
            batch.Actions.Cast<ActionRequest>().ToImmutableArray());
        var result = await client.RunAsync(request, progress is null ? null : new ProgressAdapter(progress), canDispatch,
            cancellationToken).ConfigureAwait(false);
        var status = result.Status switch
        {
            BrokerBatchStatus.Completed => NoOpBrokerBatchStatus.Finished,
            BrokerBatchStatus.Declined => NoOpBrokerBatchStatus.Declined,
            BrokerBatchStatus.Cancelled when result.Results.IsEmpty => NoOpBrokerBatchStatus.CancelledBeforeDispatch,
            BrokerBatchStatus.Cancelled => NoOpBrokerBatchStatus.Finished,
            BrokerBatchStatus.Unavailable => NoOpBrokerBatchStatus.Unavailable,
            _ => NoOpBrokerBatchStatus.RequiresReview,
        };
        return new NoOpBrokerBatchResult(status, result.Results.Select(action => new NoOpBrokerActionResult(
            action.Envelope.CorrelationId, action.ActionId, action.Status switch
            {
                BrokerResultStatus.Applied => NoOpBrokerActionStatus.Applied,
                BrokerResultStatus.Failed => NoOpBrokerActionStatus.Failed,
                BrokerResultStatus.Cancelled => NoOpBrokerActionStatus.Cancelled,
                _ => NoOpBrokerActionStatus.RequiresReview,
            })).ToImmutableArray());
    }

    private sealed class ProgressAdapter(IProgress<NoOpExecutionProgress> target) : IProgress<BrokerProgress>
    {
        public void Report(BrokerProgress value) => target.Report(new NoOpExecutionProgress(value.Envelope.CorrelationId, value.ActionId,
            value.Stage == BrokerProgressStage.Accepted ? NoOpProgressStage.Accepted : NoOpProgressStage.Running));
    }
}
