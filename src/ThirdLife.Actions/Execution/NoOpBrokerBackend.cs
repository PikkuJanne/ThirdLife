using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;

namespace ThirdLife.Actions.Execution;

public interface INoOpBrokerBackend
{
    /// <summary>Obtains consent and sends the batch only if the one-time dispatch check succeeds just before sending.</summary>
    Task<NoOpBrokerBatchResult> RunAsync(NoOpExecutionBatch batch, Func<CancellationToken, Task<bool>> canDispatch,
        IProgress<NoOpExecutionProgress>? progress, CancellationToken cancellationToken);
}

public sealed record NoOpExecutionBatch(JournalPlanKey Plan, Guid ApprovalId, Guid CorrelationId,
    DateTimeOffset ExpiresAtUtc, ImmutableArray<NoOpActionRequest> Actions);

public enum NoOpProgressStage
{
    Accepted = 1,
    Running,
}

public sealed record NoOpExecutionProgress(Guid CorrelationId, Guid ActionId, NoOpProgressStage Stage);

public enum NoOpBrokerActionStatus
{
    Applied = 1,
    Failed,
    Cancelled,
    RequiresReview,
}

public sealed record NoOpBrokerActionResult(Guid CorrelationId, Guid ActionId, NoOpBrokerActionStatus Status);

public enum NoOpBrokerBatchStatus
{
    Finished = 1,
    Declined,
    CancelledBeforeDispatch,
    Unavailable,
    RequiresReview,
}

public sealed record NoOpBrokerBatchResult(NoOpBrokerBatchStatus Status, ImmutableArray<NoOpBrokerActionResult> Results);
