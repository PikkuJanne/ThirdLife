using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;

namespace ThirdLife.Actions.Planning;

public static class ImpactEstimator
{
    /// <summary>Estimates one immutable proposal using only its compiled actions and pinned catalog metadata.</summary>
    public static PlanImpact Estimate(ProposedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var actions = plan.Actions.Select(EstimateAction).ToImmutableArray();
        return new PlanImpact(
            plan.ContentSha256,
            actions,
            Sum(actions.Select(impact => impact.DownloadBytes)),
            Sum(actions.Select(impact => impact.InstalledBytes)));
    }

    private static ActionImpact EstimateAction(PlannedAction action)
    {
        if (action.Request is NoOpActionRequest)
        {
            // The synthetic catalog row describes a placeholder package. The no-op has no package effects.
            return new ActionImpact(action, 0, 0, RestartEstimate.None, NetworkEstimate.None);
        }

        if (action.Request is not InstallPackageActionRequest)
        {
            throw new InvalidOperationException("The proposed action has no impact contract.");
        }

        var behavior = action.CatalogEntry.Behavior;
        var restart = behavior.Reboot switch
        {
            CatalogReboot.None => RestartEstimate.None,
            CatalogReboot.Possible => RestartEstimate.Possible,
            CatalogReboot.Required => RestartEstimate.Required,
            _ => RestartEstimate.Unknown,
        };
        var network = behavior.NetworkUse switch
        {
            CatalogNetworkUse.Required => NetworkEstimate.Required,
            CatalogNetworkUse.Optional => NetworkEstimate.Optional,
            // No offline artifact has been resolved. Preserve the compiled download category.
            CatalogNetworkUse.None or CatalogNetworkUse.NotApplicable => NetworkEstimate.Conflicting,
            _ => NetworkEstimate.Unknown,
        };
        return new ActionImpact(action, behavior.DownloadBytes, behavior.InstalledBytes, restart, network);
    }

    private static ByteEstimateTotal Sum(IEnumerable<long?> estimates)
    {
        long knownBytes = 0;
        var unknownCount = 0;
        foreach (var estimate in estimates)
        {
            if (estimate is { } bytes)
            {
                knownBytes = checked(knownBytes + bytes);
            }
            else
            {
                unknownCount++;
            }
        }

        return new ByteEstimateTotal(knownBytes, unknownCount);
    }
}

public sealed class PlanImpact
{
    public const int ContractVersion = 1;

    internal PlanImpact(string planSha256, ImmutableArray<ActionImpact> actions, ByteEstimateTotal download, ByteEstimateTotal installation)
    {
        PlanSha256 = planSha256;
        Actions = actions;
        Download = download;
        Installation = installation;
    }

    public int Version { get; } = ContractVersion;
    public string PlanSha256 { get; }
    public ImmutableArray<ActionImpact> Actions { get; }
    public ByteEstimateTotal Download { get; }
    public ByteEstimateTotal Installation { get; }
}

public sealed record ActionImpact(
    PlannedAction Action,
    long? DownloadBytes,
    long? InstalledBytes,
    RestartEstimate Restart,
    NetworkEstimate Network)
{
    public Guid ActionId => Action.Request.ActionId;
    public string CapabilityId => Action.CapabilityId;
    public CapabilityIntent Intent => Action.Intent;
    public string ReasonCode => Action.ReasonCode;
    public ActionScope Scope => Action.Definition.Scope;
    public ActionPrivilege Privilege => Action.Definition.Privilege;
    public ActionNetworkUse NetworkCategory => Action.Definition.NetworkUse;
    public ActionRollback Rollback => Action.Definition.Rollback;
    public ActionVerification Verification => Action.Definition.Verification;
}

public readonly record struct ByteEstimateTotal(long KnownBytes, int UnknownActionCount)
{
    public long? TotalBytes => UnknownActionCount == 0 ? KnownBytes : null;
}

public enum RestartEstimate
{
    Unknown,
    None,
    Possible,
    Required,
}

public enum NetworkEstimate
{
    Unknown,
    None,
    Optional,
    Required,
    Conflicting,
}
