using System.Text.RegularExpressions;
using ThirdLife.Actions.Contracts;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.Registry;

public static partial class ActionRegistry
{
    private static readonly ActionDefinition NoOp = new(
        ActionKind.NoOp,
        ActionScope.Machine,
        ActionPrivilege.Elevated,
        ActionReboot.None,
        ActionNetworkUse.None,
        ActionVerification.NoMachineChange,
        ActionRollback.NoChanges,
        ActionIdempotency.Repeatable,
        ActionAvailability.BrokerNoOp);

    private static readonly ActionDefinition MachinePackage = new(
        ActionKind.InstallPackage,
        ActionScope.Machine,
        ActionPrivilege.Elevated,
        ActionReboot.MayRequire,
        ActionNetworkUse.SoftwareDownload,
        ActionVerification.ExactPackageIdentity,
        ActionRollback.ManualReview,
        ActionIdempotency.ReobserveBeforeRetry,
        ActionAvailability.PackageBackendRequired);

    private static readonly ActionDefinition UserPackage = MachinePackage with
    {
        Scope = ActionScope.User,
        Privilege = ActionPrivilege.Unelevated,
    };

    private static readonly ActionDefinition UpdateScan = new(
        ActionKind.ScanWindowsUpdate, ActionScope.Machine, ActionPrivilege.Unelevated, ActionReboot.None,
        ActionNetworkUse.WindowsUpdate, ActionVerification.WindowsUpdateScan, ActionRollback.NoChanges,
        ActionIdempotency.Repeatable, ActionAvailability.WindowsUpdateAgentRequired);

    private static readonly ActionDefinition UpdateDownload = UpdateScan with
    {
        Kind = ActionKind.DownloadWindowsUpdate,
        Availability = ActionAvailability.WindowsUpdateServicingBrokerRequired,
        Privilege = ActionPrivilege.Elevated,
        Verification = ActionVerification.ExactDownloadedUpdateRevision,
        Rollback = ActionRollback.ManualReview,
        Idempotency = ActionIdempotency.ReobserveBeforeRetry,
    };

    private static readonly ActionDefinition UpdateInstall = UpdateDownload with
    {
        Kind = ActionKind.InstallWindowsUpdate,
        Reboot = ActionReboot.MayRequire,
        Verification = ActionVerification.ExactInstalledUpdateRevision,
    };

    public static NoOpActionRequest CreateNoOp(Guid actionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        return new NoOpActionRequest(actionId);
    }

    /// <summary>Creates an install request for plan review. Package execution needs a separately authorized backend.</summary>
    public static InstallPackageActionRequest CreateInstallPackage(
        Guid actionId,
        string sourceId,
        string packageId,
        string exactVersion,
        string publisher,
        ActionScope scope,
        ActionArchitecture architecture)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        RequireToken(sourceId, SourceIdPattern(), 64, nameof(sourceId));
        RequireToken(packageId, PackageIdPattern(), 128, nameof(packageId));
        RequireToken(exactVersion, PackageVersionPattern(), 64, nameof(exactVersion));
        RequireToken(publisher, PublisherPattern(), 128, nameof(publisher));
        if (sourceId == "synthetic-fixture-source" ||
            packageId.StartsWith("generic.synthetic.", StringComparison.OrdinalIgnoreCase) ||
            publisher.Equals("synthetic-fixture-publisher", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Synthetic identities are reserved for no-op plans.");
        }

        if (!Enum.IsDefined(scope) || !Enum.IsDefined(architecture))
        {
            throw new ArgumentException("The package scope and architecture must be defined.");
        }

        return new InstallPackageActionRequest(actionId, sourceId, packageId, exactVersion, publisher, scope, architecture);
    }

    public static WindowsUpdateActionRequest CreateScanWindowsUpdate(Guid actionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        return new WindowsUpdateActionRequest(actionId, ActionKind.ScanWindowsUpdate, null);
    }

    public static WindowsUpdateActionRequest CreateDownloadWindowsUpdate(Guid actionId, WindowsUpdateCandidate exact) =>
        CreateUpdateAction(actionId, ActionKind.DownloadWindowsUpdate, exact);

    public static WindowsUpdateActionRequest CreateInstallWindowsUpdate(Guid actionId, WindowsUpdateCandidate exact) =>
        CreateUpdateAction(actionId, ActionKind.InstallWindowsUpdate, exact);

    public static (ActionDefinition Download, ActionDefinition Install) DescribeWindowsUpdateServicing(WindowsUpdateCandidate exact)
    {
        RequireSelectableUpdate(exact);
        return (UpdateDownload, UpdateInstall);
    }

    private static WindowsUpdateActionRequest CreateUpdateAction(Guid actionId, ActionKind kind, WindowsUpdateCandidate exact)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actionId, Guid.Empty);
        RequireSelectableUpdate(exact);
        return new WindowsUpdateActionRequest(actionId, kind, exact);
    }

    private static void RequireSelectableUpdate(WindowsUpdateCandidate exact)
    {
        if (!WindowsUpdatePolicy.IsSelectable(exact))
        {
            throw new ArgumentException("The update is outside the permitted software classifications.", nameof(exact));
        }
    }

    public static ActionDefinition Describe(ActionRequest request) => request switch
    {
        NoOpActionRequest => NoOp,
        InstallPackageActionRequest { Scope: ActionScope.User } => UserPackage,
        InstallPackageActionRequest { Scope: ActionScope.Machine } => MachinePackage,
        WindowsUpdateActionRequest { Kind: ActionKind.ScanWindowsUpdate } => UpdateScan,
        WindowsUpdateActionRequest { Kind: ActionKind.DownloadWindowsUpdate } => UpdateDownload,
        WindowsUpdateActionRequest { Kind: ActionKind.InstallWindowsUpdate } => UpdateInstall,
        _ => throw new ArgumentException("The action type is unavailable.", nameof(request)),
    };

    private static void RequireToken(string? value, Regex pattern, int maxLength, string parameter)
    {
        if (value is null || value.Length > maxLength || !pattern.IsMatch(value))
        {
            throw new ArgumentException("The package metadata is invalid.", parameter);
        }
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex SourceIdPattern();

    [GeneratedRegex(@"\A(?:[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)*\+*\.)+[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)*\+*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"\A[0-9][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PackageVersionPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?: [A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PublisherPattern();
}
