namespace ThirdLife.Actions.Contracts;

public sealed record ActionDefinition(
    ActionKind Kind,
    ActionScope Scope,
    ActionPrivilege Privilege,
    ActionReboot Reboot,
    ActionNetworkUse NetworkUse,
    ActionVerification Verification,
    ActionRollback Rollback,
    ActionIdempotency Idempotency,
    ActionAvailability Availability);

public enum ActionPrivilege
{
    Unelevated = 1,
    Elevated,
}

public enum ActionReboot
{
    None = 1,
    MayRequire,
}

public enum ActionNetworkUse
{
    None = 1,
    SoftwareDownload,
    WindowsUpdate,
}

public enum ActionVerification
{
    NoMachineChange = 1,
    ExactPackageIdentity,
    WindowsUpdateScan,
}

public enum ActionRollback
{
    NoChanges = 1,
    ManualReview,
}

public enum ActionIdempotency
{
    Repeatable = 1,
    ReobserveBeforeRetry,
}

public enum ActionAvailability
{
    BrokerNoOp = 1,
    PackageBackendRequired,
    WindowsUpdateAgentRequired,
}
