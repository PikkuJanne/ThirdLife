namespace ThirdLife.Core.Jobs;

public enum ColdBootKind
{
    OrderlyPowerOff = 1,
    Restart,
    HybridOrResume,
    Ambiguous,
}

public enum ColdBootVerificationState
{
    AwaitingShutdown = 1,
    AwaitingEvidence,
    ReadyToVerify,
    Historical,
}

public sealed record ColdBootCheckpoint(Guid Id, DateTimeOffset RequestedAtUtc, uint RequestedBootSession)
{
    public bool CanObserve(uint bootSession, DateTimeOffset now) =>
        RequestedBootSession < uint.MaxValue && bootSession == RequestedBootSession + 1 &&
        now >= RequestedAtUtc && now - RequestedAtUtc < TimeSpan.FromHours(24);

    public bool Matches(ColdBootObservation? observation, uint bootSession, DateTimeOffset now) =>
        CanObserve(bootSession, now) && observation is { Kind: ColdBootKind.OrderlyPowerOff } &&
        observation.BootSession == bootSession && observation.StartedAtUtc > RequestedAtUtc &&
        observation.StartedAtUtc <= now;
}

public sealed record ColdBootObservation(uint BootSession, DateTimeOffset StartedAtUtc, ColdBootKind Kind);
