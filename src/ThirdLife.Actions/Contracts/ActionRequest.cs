namespace ThirdLife.Actions.Contracts;

public abstract class ActionRequest
{
    private protected ActionRequest(Guid actionId)
    {
        ActionId = actionId;
    }

    public Guid ActionId { get; }

    public abstract ActionKind Kind { get; }
}

public sealed class NoOpActionRequest : ActionRequest
{
    internal NoOpActionRequest(Guid actionId) : base(actionId)
    {
    }

    public override ActionKind Kind => ActionKind.NoOp;
}

public sealed class InstallPackageActionRequest : ActionRequest
{
    internal InstallPackageActionRequest(
        Guid actionId,
        string sourceId,
        string packageId,
        string exactVersion,
        string publisher,
        ActionScope scope,
        ActionArchitecture architecture) : base(actionId)
    {
        SourceId = sourceId;
        PackageId = packageId;
        ExactVersion = exactVersion;
        Publisher = publisher;
        Scope = scope;
        Architecture = architecture;
    }

    public override ActionKind Kind => ActionKind.InstallPackage;

    public string SourceId { get; }

    public string PackageId { get; }

    public string ExactVersion { get; }

    public string Publisher { get; }

    public ActionScope Scope { get; }

    public ActionArchitecture Architecture { get; }
}

public enum ActionKind
{
    NoOp = 1,
    InstallPackage,
}

public enum ActionScope
{
    User = 1,
    Machine,
}

public enum ActionArchitecture
{
    X64 = 1,
    Arm64,
    X86,
}
