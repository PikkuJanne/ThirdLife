using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ThirdLife.Core.WindowsUpdates;

public sealed record WindowsUpdateServicingTarget(int Build, int BuildRevision, uint Edition);

public sealed record WindowsUpdateQualificationSummary(string PolicySha256, string OrganizationPolicySha256,
    long InstallWorkingBytes, long RollbackReserveBytes, DateTimeOffset EffectiveUntil);

internal enum WindowsUpdateServicingRoute
{
    SupervisedQualification = 1,
    OrdinaryDispatch = 2,
}

public enum WindowsUpdateServicingDecision
{
    NoEffectivePolicy,
    PolicyChanged,
    ObservationUnavailable,
    TargetChanged,
    RecoveryUnavailable,
    SpaceUnavailable,
    InsufficientSpace,
    Allowed,
}

/// <summary>The compiled Windows servicing reviews. Operator approval and dispatch intent are checked separately.</summary>
public static class WindowsUpdateServicingPolicy
{
    public const string OrganizationClaimScope = "windows_update_servicing";
    public static readonly TimeSpan ObservationWindow = TimeSpan.FromMinutes(2);

    // Each row requires review of the exact organization policy, recovery procedure and space
    // bounds. Its authority evidence covers supervised qualification for that scope, or the
    // effective TL-0314 privilege decision for ordinary dispatch, according to its route.
    // No offer has qualified. Runtime data and fixture policies cannot add a review.
    private static readonly ImmutableArray<WindowsUpdateServicingReview> EffectiveReviews = [];

    public static WindowsUpdateQualificationSummary? CurrentQualificationSummary(WindowsUpdateCandidate exact, DateTimeOffset now) =>
        FindQualification(exact, now)?.QualificationSummary();

    public static string? CurrentQualificationPolicySha256(WindowsUpdateCandidate exact, DateTimeOffset now) =>
        FindQualification(exact, now)?.PolicySha256;

    public static string? CurrentQualificationOrganizationPolicySha256(WindowsUpdateCandidate exact, DateTimeOffset now) =>
        FindQualification(exact, now)?.OrganizationPolicySha256;

    public static bool MatchesQualificationOrganizationPolicy(WindowsUpdateCandidate exact, string servicingPolicySha256,
        string organizationPolicySha256, DateTimeOffset now) =>
        FindQualification(exact, now) is { } review &&
        review.PolicySha256 == servicingPolicySha256 && review.OrganizationPolicySha256 == organizationPolicySha256;

    public static WindowsUpdateServicingDecision EvaluateQualification(WindowsUpdateCandidate exact, string policySha256,
        WindowsUpdatePreparation observation, DateTimeOffset observationRequestedAtUtc, DateTimeOffset now) =>
        FindQualification(exact, now)?.Evaluate(policySha256, observation, observationRequestedAtUtc, now)
            ?? WindowsUpdateServicingDecision.NoEffectivePolicy;

    private static WindowsUpdateServicingReview? FindQualification(WindowsUpdateCandidate exact, DateTimeOffset now) =>
        FindEffective(EffectiveReviews, WindowsUpdateServicingRoute.SupervisedQualification, exact, now);

    internal static WindowsUpdateServicingReview? FindEffective(ImmutableArray<WindowsUpdateServicingReview> reviews,
        WindowsUpdateServicingRoute route, WindowsUpdateCandidate exact, DateTimeOffset now)
    {
        WindowsUpdateServicingReview? match = null;
        foreach (var row in reviews)
        {
            if (row.Route != route || row.Exact != exact || now < row.EffectiveFrom || now >= row.EffectiveUntil) { continue; }
            if (match is not null || !row.IsComplete) { return null; }
            match = row;
        }
        return match;
    }
}

// The review covers Microsoft Windows Update, a single security/critical CBS offer,
// Windows 11 x64, and a documented Windows uninstall procedure qualified for the target.
// The evidence digest covers the procedure and both peak storage requirements. An uninstall
// flag, policy decision, or reserve by itself cannot establish that recovery has been reviewed.
internal sealed record WindowsUpdateServicingReview(WindowsUpdateCandidate Exact, WindowsUpdateServicingTarget Target,
    long InstallWorkingBytes, long RollbackReserveBytes, string OrganizationPolicySha256, string RecoveryEvidenceSha256,
    WindowsUpdateServicingRoute Route, string RouteAuthorityEvidenceSha256, DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveUntil)
{
    internal bool IsComplete => WindowsUpdatePolicy.IsSelectable(Exact) && Target is { Build: >= 22000, BuildRevision: >= 0, Edition: > 0 } &&
        InstallWorkingBytes > 0 && RollbackReserveBytes > 0 && EffectiveFrom != default && EffectiveUntil > EffectiveFrom &&
        Route is WindowsUpdateServicingRoute.SupervisedQualification or WindowsUpdateServicingRoute.OrdinaryDispatch &&
        IsDigest(OrganizationPolicySha256) && IsDigest(RecoveryEvidenceSha256) && IsDigest(RouteAuthorityEvidenceSha256);

    internal WindowsUpdateQualificationSummary? QualificationSummary() => IsComplete && Route == WindowsUpdateServicingRoute.SupervisedQualification
        ? new(PolicySha256, OrganizationPolicySha256, InstallWorkingBytes, RollbackReserveBytes, EffectiveUntil) : null;

    internal string PolicySha256
    {
        get
        {
            // Fixed-order ASCII fields and UTC ticks give the broker and journal the same identity.
            // Digests are lowercase hex. No free text enters this encoding.
            var canonical = string.Create(CultureInfo.InvariantCulture,
                $"thirdlife.windows-update.windows11-x64-cbs-uninstall.v2|{Exact.Identity.UpdateId:D}|{Exact.Identity.Revision}|" +
                $"{(int)Exact.Classification}|{Exact.MaximumDownloadBytes}|{Target.Build}|{Target.BuildRevision}|{Target.Edition}|" +
                $"{InstallWorkingBytes}|{RollbackReserveBytes}|{OrganizationPolicySha256}|{RecoveryEvidenceSha256}|" +
                $"{(int)Route}|{RouteAuthorityEvidenceSha256}|{EffectiveFrom.UtcTicks}|{EffectiveUntil.UtcTicks}");
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(canonical)));
        }
    }

    internal WindowsUpdateServicingDecision Evaluate(string policySha256, WindowsUpdatePreparation observation,
        DateTimeOffset requestedAt, DateTimeOffset now)
    {
        if (!IsComplete || now < EffectiveFrom || now >= EffectiveUntil) { return WindowsUpdateServicingDecision.NoEffectivePolicy; }
        if (PolicySha256 != policySha256) { return WindowsUpdateServicingDecision.PolicyChanged; }
        if (observation is not
            {
                Status: WindowsUpdateStatus.Succeeded, EligibilityReason: WindowsUpdateEligibilityReason.Eligible,
                ServicingSupported: true, PowerReady: true, RebootRequired: false
            } ||
            observation.Exact != Exact || requestedAt < EffectiveFrom || observation.ObservedAtUtc < requestedAt ||
            observation.ObservedAtUtc > now || now - requestedAt >= WindowsUpdateServicingPolicy.ObservationWindow)
        {
            return WindowsUpdateServicingDecision.ObservationUnavailable;
        }

        if (observation.Target != Target) { return WindowsUpdateServicingDecision.TargetChanged; }
        if (observation.RemovalSupported != true) { return WindowsUpdateServicingDecision.RecoveryUnavailable; }
        if (observation.SystemAvailableBytes is not { } system || observation.CacheAvailableBytes is not { } cache ||
            observation.CacheSharesSystemVolume is not { } shared || observation.RecommendedInstallationBytes is <= 0)
        {
            return WindowsUpdateServicingDecision.SpaceUnavailable;
        }

        // The exact reviewed installation peak applies when WUA omits its optional recommendation.
        // Always reserve the larger when both are known. Retain the transfer budget for both phases.
        // Subtract each demand from available space before checking the next to avoid overflow.
        var install = (ulong)Math.Max(InstallWorkingBytes, observation.RecommendedInstallationBytes.GetValueOrDefault());
        var reserve = (ulong)RollbackReserveBytes;
        var transfer = (ulong)Exact.MaximumDownloadBytes!.Value;
        var available = shared ? Math.Min(system, cache) : system;
        if (available < install || available - install < reserve || (shared ? available - install - reserve : cache) < transfer)
        {
            return WindowsUpdateServicingDecision.InsufficientSpace;
        }
        return WindowsUpdateServicingDecision.Allowed;
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}
