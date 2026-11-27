using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;

namespace ThirdLife.Actions.Planning;

public static class PlanDiff
{
    /// <summary>Compares the retained review snapshot with the current proposal before dispatch.</summary>
    /// <remarks>A match establishes material equivalence. The caller still needs an active recorded approval.</remarks>
    public static PlanDiffResult Compare(ProposedPlan approved, PlanImpact approvedImpact, ProposedPlan current, PlanImpact currentImpact)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(approvedImpact);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(currentImpact);
        if (approvedImpact.PlanSha256 != approved.ContentSha256 || currentImpact.PlanSha256 != current.ContentSha256)
        {
            return Result(PlanChangeKind.InvalidImpact, "The impact estimate does not match its plan. Review the current plan again.");
        }

        if (approved.BlockedReason is not null || current.BlockedReason is not null ||
            approved.Actions.IsEmpty || current.Actions.IsEmpty)
        {
            return Result(PlanChangeKind.CurrentPlanBlocked, "This comparison includes a blocked or empty plan. Preparation cannot proceed.");
        }

        var changes = ImmutableArray.CreateBuilder<PlanChange>();
        if (approved.JobId != current.JobId || approved.GateDecisionId != current.GateDecisionId ||
            approved.ProfileSha256 != current.ProfileSha256 || approved.PolicySha256 != current.PolicySha256 ||
            approved.EvidenceSha256 != current.EvidenceSha256 || approved.SyntheticData != current.SyntheticData ||
            (approved.FullMaintenance && current.FullMaintenance && approved.ReviewedEvidenceCount != current.ReviewedEvidenceCount))
        {
            changes.Add(new(PlanChangeKind.ReviewContext, "The job, approved intake, profile, policy or evidence context changed."));
        }

        if (approvedImpact.Version != currentImpact.Version)
        {
            changes.Add(new(PlanChangeKind.Impact, "The impact estimate format changed."));
        }

        var previousRows = approvedImpact.Actions.ToDictionary(row => row.CapabilityId, StringComparer.Ordinal);
        var currentRows = currentImpact.Actions.ToDictionary(row => row.CapabilityId, StringComparer.Ordinal);
        foreach (var previous in approvedImpact.Actions)
        {
            if (currentRows.TryGetValue(previous.CapabilityId, out var next))
            {
                CompareAction(previous, next, changes);
            }
            else
            {
                changes.Add(new(PlanChangeKind.ActionSelection, $"The plan removed {Label(previous.CapabilityId)}."));
            }
        }

        foreach (var next in currentImpact.Actions.Where(row => !previousRows.ContainsKey(row.CapabilityId)))
        {
            changes.Add(new(PlanChangeKind.ActionSelection, $"The plan added {Label(next.CapabilityId)}."));
        }

        if (previousRows.Count == currentRows.Count && previousRows.Keys.All(currentRows.ContainsKey) &&
            !approvedImpact.Actions.Select(row => row.CapabilityId)
                .SequenceEqual(currentImpact.Actions.Select(row => row.CapabilityId), StringComparer.Ordinal))
        {
            changes.Add(new(PlanChangeKind.ActionOrder, "The order of the preparation actions changed."));
        }

        if (approved.FullMaintenance != current.FullMaintenance || !ActionSchedule.SameSteps(approved.Schedule, current.Schedule))
        {
            changes.Add(new(PlanChangeKind.ActionSchedule, "The preparation phases, dependencies or scheduled actions changed."));
        }

        // The digest covers pinned content and exceptions as well as the named comparison fields.
        if (changes.Count == 0 && approved.ContentSha256 != current.ContentSha256)
        {
            changes.Add(new(PlanChangeKind.ReviewContext, "The exact plan content changed. Review its configuration and evidence again."));
        }

        return new PlanDiffResult(changes.ToImmutable());
    }

    private static void CompareAction(ActionImpact previous, ActionImpact current, ImmutableArray<PlanChange>.Builder changes)
    {
        var before = previous.Action;
        var after = current.Action;
        var label = Label(before.CapabilityId);
        var oldPackage = before.CatalogEntry.Package;
        var newPackage = after.CatalogEntry.Package;
        var oldRequest = before.Request as InstallPackageActionRequest;
        var newRequest = after.Request as InstallPackageActionRequest;
        var oldSource = oldRequest?.SourceId ?? oldPackage.SourceId;
        var newSource = newRequest?.SourceId ?? newPackage.SourceId;
        var oldVersion = oldRequest?.ExactVersion ?? oldPackage.ExactVersion;
        var newVersion = newRequest?.ExactVersion ?? newPackage.ExactVersion;
        var oldPublisher = oldRequest?.Publisher ?? before.CatalogEntry.Review.Publisher;
        var newPublisher = newRequest?.Publisher ?? after.CatalogEntry.Review.Publisher;
        var oldBehavior = before.CatalogEntry.Behavior;
        var newBehavior = after.CatalogEntry.Behavior;
        Add(before.Request.Kind != after.Request.Kind || before.Intent != after.Intent || before.ReasonCode != after.ReasonCode ||
            before.Definition.Availability != after.Definition.Availability || before.Definition.Idempotency != after.Definition.Idempotency,
            PlanChangeKind.Action, $"The action or its required intent changed for {label}.");
        Add((oldRequest?.PackageId ?? oldPackage.PackageId) != (newRequest?.PackageId ?? newPackage.PackageId) ||
            oldRequest?.Architecture != newRequest?.Architecture ||
            !oldPackage.Architectures.SequenceEqual(newPackage.Architectures) || oldPackage.MinimumOs != newPackage.MinimumOs,
            PlanChangeKind.PackageIdentity, $"The package identity or supported platform changed for {label}.");
        Add(oldSource != newSource, PlanChangeKind.PackageSource,
            $"The package source for {label} changed from {oldSource} to {newSource}.");
        Add(oldPublisher != newPublisher, PlanChangeKind.PackagePublisher,
            $"The publisher for {label} changed from {oldPublisher} to {newPublisher}.");
        Add(oldVersion != newVersion, PlanChangeKind.PackageVersion,
            $"The exact package version for {label} changed from {oldVersion} to {newVersion}.");
        Add(oldPackage.Scope != newPackage.Scope || previous.Scope != current.Scope || oldRequest?.Scope != newRequest?.Scope,
            PlanChangeKind.Scope, $"The installation scope changed for {label}.");
        Add(previous.Privilege != current.Privilege, PlanChangeKind.Privilege, $"The elevation requirement changed for {label}.");
        Add(oldBehavior.Reboot != newBehavior.Reboot || previous.Restart != current.Restart || before.Definition.Reboot != after.Definition.Reboot,
            PlanChangeKind.Restart, $"The restart requirement changed for {label}.");
        Add(before.CatalogEntry.Verification != after.CatalogEntry.Verification || previous.Verification != current.Verification,
            PlanChangeKind.Verification, $"The verification requirement changed for {label}.");
        Add(previous.DownloadBytes != current.DownloadBytes || previous.InstalledBytes != current.InstalledBytes ||
            oldBehavior.DownloadBytes != newBehavior.DownloadBytes || oldBehavior.InstalledBytes != newBehavior.InstalledBytes ||
            oldBehavior.BackgroundService != newBehavior.BackgroundService || oldBehavior.AutoUpdate != newBehavior.AutoUpdate,
            PlanChangeKind.Impact, $"The declared impact changed for {label}.");
        Add(previous.Rollback != current.Rollback, PlanChangeKind.Rollback, $"The rollback declaration changed for {label}.");
        Add(oldBehavior.NetworkUse != newBehavior.NetworkUse || previous.Network != current.Network ||
            previous.NetworkCategory != current.NetworkCategory, PlanChangeKind.Network, $"The network declaration changed for {label}.");

        void Add(bool changed, PlanChangeKind kind, string message)
        {
            if (changed)
            {
                changes.Add(new(kind, message));
            }
        }
    }

    private static string Label(string id) => id.Replace('_', ' ');

    private static PlanDiffResult Result(PlanChangeKind kind, string message) =>
        new([new PlanChange(kind, message)]);
}

public sealed record PlanChange(PlanChangeKind Kind, string Message);

public sealed class PlanDiffResult
{
    internal PlanDiffResult(ImmutableArray<PlanChange> changes) => Changes = changes;

    public ImmutableArray<PlanChange> Changes { get; }
    public bool RequiresReapproval => !Changes.IsEmpty;
    public string PlainLanguage => RequiresReapproval
        ? string.Join('\n', Changes.Select(change => change.Message))
        : "The approved plan and impact are unchanged.";
}

public enum PlanChangeKind
{
    InvalidImpact = 1,
    CurrentPlanBlocked,
    ReviewContext,
    ActionSelection,
    ActionOrder,
    Action,
    PackageIdentity,
    PackageSource,
    PackagePublisher,
    PackageVersion,
    Scope,
    Privilege,
    Restart,
    Verification,
    Impact,
    Rollback,
    Network,
    ActionSchedule,
}
