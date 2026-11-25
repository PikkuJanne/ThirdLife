using System.Collections.Immutable;
using System.Security.Cryptography;
using ThirdLife.Actions.Contracts;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Broker.Protocol;

public static class BrokerProtocolLimits
{
    public const int CurrentVersion = 1;
    public const int MaximumMessageBytes = 4_096;
    public const int MaximumJsonDepth = 4;
    public const int MaximumActions = 4;
    public const int MaximumSequence = 32;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(2);

    public static bool IsWithinWindow(DateTimeOffset expiresAtUtc, DateTimeOffset now) =>
        now != default && expiresAtUtc > now && expiresAtUtc - now <= MaximumLifetime;
}

public sealed record BrokerNonce
{
    public BrokerNonce(string value)
    {
        Value = BrokerMessageValidation.RequireLowerHex32(value, nameof(value));
    }

    public string Value { get; }

    public static BrokerNonce Create() => new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));

    public override string ToString() => "[broker_nonce]";
}

public sealed record BrokerEnvelope
{
    public BrokerEnvelope(Guid correlationId, BrokerNonce nonce, DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(correlationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(expiresAtUtc, default);
        CorrelationId = correlationId;
        Nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }

    public int ProtocolVersion { get; } = BrokerProtocolLimits.CurrentVersion;
    public Guid CorrelationId { get; }
    public BrokerNonce Nonce { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
}

public enum BrokerMessageKind
{
    Hello = 1,
    HelloAck,
    Request,
    Progress,
    Result,
    Cancel,
    Error,
}

public abstract class BrokerMessage
{
    private protected BrokerMessage(BrokerEnvelope envelope)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
    }

    public BrokerEnvelope Envelope { get; }
    public abstract BrokerMessageKind Kind { get; }
}

public sealed class BrokerHello(BrokerEnvelope envelope) : BrokerMessage(envelope)
{
    public override BrokerMessageKind Kind => BrokerMessageKind.Hello;
}

public sealed class BrokerHelloAck(BrokerEnvelope envelope) : BrokerMessage(envelope)
{
    public override BrokerMessageKind Kind => BrokerMessageKind.HelloAck;
}

public sealed class BrokerRequest : BrokerMessage
{
    public BrokerRequest(
        BrokerEnvelope envelope,
        JobId jobId,
        Guid approvalId,
        string planSha256,
        string impactSha256,
        ImmutableArray<ActionRequest> actions) : base(envelope)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(approvalId, Guid.Empty);
        if (actions.IsDefaultOrEmpty || actions.Length > BrokerProtocolLimits.MaximumActions)
        {
            throw new ArgumentException("The broker request has an invalid action count.", nameof(actions));
        }

        var actionIds = new HashSet<Guid>();
        foreach (var action in actions)
        {
            if (action is not NoOpActionRequest || action.ActionId == Guid.Empty || !actionIds.Add(action.ActionId))
            {
                throw new ArgumentException("The broker request has an unavailable or repeated action.", nameof(actions));
            }
        }

        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        ApprovalId = approvalId;
        PlanSha256 = BrokerMessageValidation.RequireLowerHex32(planSha256, nameof(planSha256));
        ImpactSha256 = BrokerMessageValidation.RequireLowerHex32(impactSha256, nameof(impactSha256));
        Actions = actions;
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.Request;
    public JobId JobId { get; }
    public Guid ApprovalId { get; }
    public string PlanSha256 { get; }
    public string ImpactSha256 { get; }
    public ImmutableArray<ActionRequest> Actions { get; }
}

public enum BrokerProgressStage
{
    Accepted = 1,
    Running,
}

public sealed class BrokerProgress : BrokerMessage
{
    public BrokerProgress(BrokerEnvelope envelope, Guid actionId, int sequence, BrokerProgressStage stage) : base(envelope)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        ActionId = actionId;
        Sequence = BrokerMessageValidation.RequireSequence(sequence);
        Stage = BrokerMessageValidation.RequireDefined(stage, nameof(stage));
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.Progress;
    public Guid ActionId { get; }
    public int Sequence { get; }
    public BrokerProgressStage Stage { get; }
}

public enum BrokerResultStatus
{
    Applied = 1,
    Failed,
    Cancelled,
    RequiresReview,
}

public sealed class BrokerResult : BrokerMessage
{
    public BrokerResult(BrokerEnvelope envelope, Guid actionId, int sequence, BrokerResultStatus status) : base(envelope)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        ActionId = actionId;
        Sequence = BrokerMessageValidation.RequireSequence(sequence);
        Status = BrokerMessageValidation.RequireDefined(status, nameof(status));
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.Result;
    public Guid ActionId { get; }
    public int Sequence { get; }
    public BrokerResultStatus Status { get; }
}

public sealed class BrokerCancel : BrokerMessage
{
    public BrokerCancel(BrokerEnvelope envelope, Guid actionId) : base(envelope)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        ActionId = actionId;
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.Cancel;
    public Guid ActionId { get; }
}

public enum BrokerErrorCode
{
    InvalidMessage = 1,
    Unauthorized,
    Expired,
    Replay,
    InvalidState,
    Unavailable,
}

public sealed class BrokerError : BrokerMessage
{
    public BrokerError(BrokerEnvelope envelope, BrokerErrorCode code) : base(envelope)
    {
        Code = BrokerMessageValidation.RequireDefined(code, nameof(code));
    }

    public override BrokerMessageKind Kind => BrokerMessageKind.Error;
    public BrokerErrorCode Code { get; }
}

internal static class BrokerMessageValidation
{
    public static string RequireLowerHex32(string? value, string parameterName) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw new ArgumentException("The broker message requires a lowercase 32-byte hexadecimal value.", parameterName);

    public static int RequireSequence(int sequence) =>
        sequence is >= 1 and <= BrokerProtocolLimits.MaximumSequence
            ? sequence
            : throw new ArgumentOutOfRangeException(nameof(sequence));

    public static T RequireDefined<T>(T value, string parameterName) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName);
}
