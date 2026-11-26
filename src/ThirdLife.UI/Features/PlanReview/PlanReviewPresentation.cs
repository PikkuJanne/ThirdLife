using System.ComponentModel;
using System.Globalization;
using System.Text;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Planning;
using ThirdLife.Catalog.Profiles;

namespace ThirdLife.UI.Features.PlanReview;

internal sealed class PlanCapabilityChoice(ProfileCapability capability, Func<bool> canChange, Action changed) : INotifyPropertyChanged
{
    private bool _included = true;

    public string CapabilityId => capability.CapabilityId;
    public bool IsEssential => capability.Intent == CapabilityIntent.Essential;
    public string Label => $"{PlanReviewPresentation.CapabilityName(CapabilityId)} ({(IsEssential ? "essential" : "optional")})";

    public bool IsIncluded
    {
        get => _included;
        set
        {
            if (_included == value || !canChange())
            {
                return;
            }

            _included = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
            changed();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed record PlanReviewItem(string Label, string Value);

internal sealed record PlanReviewSnapshot(ProposedPlan Plan, PlanImpact Impact, string Title, string Details,
    string Identity, string Scope, IReadOnlyList<PlanReviewItem> MaterialImpact);

internal enum PlanReviewOutcome
{
    Approved,
    Declined,
}

internal sealed record PlanReviewDecision(PlanReviewOutcome Outcome, PlanReviewSnapshot Snapshot, DateTimeOffset DecidedAtUtc)
{
    public string PlanSha256 => Snapshot.Plan.ContentSha256;
}

internal static class PlanReviewPresentation
{
    public static PlanReviewSnapshot Describe(ProposedPlan plan)
    {
        var impact = ImpactEstimator.Estimate(plan);
        var title = plan.BlockedReason is null
            ? plan.SyntheticData ? "Synthetic no-op proposal" : "Machine-wide package proposal"
            : "Preparation blocked";
        var details = new StringBuilder();
        details.AppendLine(CultureInfo.CurrentCulture, $"Plan SHA-256 {plan.ContentSha256}");
        if (plan.BlockedReason is { } blocked)
        {
            details.AppendLine(BlockedMessage(blocked, plan));
            if (!plan.SyntheticData && blocked == PlanBlockReason.PolicyBlocksPreparation && plan.PolicyDecision is { } decision)
            {
                details.AppendLine(decision.Explanation);
            }
        }
        else
        {
            details.AppendLine(plan.SyntheticData
                ? "Preparation uses synthetic no-op actions. Expected readiness requires independent verification."
                : "Installation changes this computer for all users and may prompt for Windows consent. Device readiness needs a separate assessment.");
            details.AppendLine(CultureInfo.CurrentCulture, $"Estimated download {Total(impact.Download)}");
            details.AppendLine(CultureInfo.CurrentCulture, $"Estimated installation {Total(impact.Installation)}");
            if (plan.Schedule.IsEmpty)
            {
                foreach (var row in impact.Actions)
                {
                    AppendAction(details, row, WorkshopPackageProfile.IncludesLaunchProbe(plan));
                }
            }
            else
            {
                AppendSchedule(details, plan, impact);
            }
        }

        if (!plan.RecipientChoices.IsEmpty)
        {
            details.AppendLine().AppendLine("Deferred recipient choices");
            foreach (var choice in plan.RecipientChoices)
            {
                details.AppendLine(choice.Type switch
                {
                    RecipientChoiceType.BrowserPreference => "Browser preference",
                    RecipientChoiceType.AccessibilityPreferences => "Accessibility preferences",
                    RecipientChoiceType.CloudAccountSignIn => "Cloud account sign-in",
                    RecipientChoiceType.BackupOnboarding => "Backup onboarding",
                    _ => "Unavailable choice",
                });
            }
        }

        var package = impact.Actions.Length == 1 ? impact.Actions[0].Action.Request as InstallPackageActionRequest : null;
        var identity = package is null ? string.Empty :
            $"{package.PackageId} · {package.ExactVersion} · {Architecture(package.Architecture)}";
        var scope = package is null ? string.Empty :
            $"{(package.Scope == ActionScope.Machine ? "All users" : "Current user")} · {package.SourceId}";
        return new PlanReviewSnapshot(plan, impact, title, details.ToString(), identity, scope,
            package is null ? [] : PackageImpact(impact.Actions[0], WorkshopPackageProfile.IncludesLaunchProbe(plan)));
    }

    private static void AppendSchedule(StringBuilder details, ProposedPlan plan, PlanImpact impact)
    {
        var rows = impact.Actions.ToDictionary(row => row.ActionId);
        details.AppendLine("Actions run one at a time. Later phases wait for verification of every earlier phase.");
        foreach (var step in plan.Schedule)
        {
            AppendAction(details, rows[step.ActionId], WorkshopPackageProfile.IncludesLaunchProbe(plan));
            details.AppendLine(CultureInfo.CurrentCulture, $"Phase {step.Phase}");
            var dependencies = step.Dependencies.IsEmpty ? "None" :
                string.Join(", ", step.Dependencies.Select(id => CapabilityName(rows[id].CapabilityId)));
            details.AppendLine(CultureInfo.CurrentCulture, $"Requires verified actions {dependencies}");
        }
    }

    private static PlanReviewItem[] PackageImpact(ActionImpact impact, bool includesLaunchProbe) =>
    [
        new("Actions", $"Install the reviewed {impact.Action.CatalogEntry.Package.PackageId} package"),
        new("Data touched", "Installer staging files, application files and Windows Installer registration"),
        new("Windows permission", impact.Privilege == ActionPrivilege.Elevated ?
            "Windows may request administrator consent" : "Unelevated"),
        new("Network", $"{NetworkCategory(impact.NetworkCategory)} · {NetworkRequirement(impact.Network)}"),
        new("Disk", $"Installer download {Bytes(impact.DownloadBytes)} · Installed size {Bytes(impact.InstalledBytes)}. Staging checks available space."),
        new("Restart", $"Restart {Restart(impact.Restart).ToLower(CultureInfo.CurrentCulture)}. Use Windows to restart when required."),
        new("Recovery", "Review actual installed state before another attempt. Changes are not undone automatically."),
        new("Independent verification", includesLaunchProbe ?
            "Exact product, version and scope, followed by an owned document-free launch and exit check" :
            "Exact installed package identity"),
    ];

    internal static string CapabilityName(string id) => id switch
    {
        "web_browsing" => "Web browsing",
        "document_editing" => "Document editing",
        "pdf_reading" => "PDF reading",
        "video_calling" => "Video calling",
        _ => "Unavailable capability",
    };

    private static void AppendAction(StringBuilder details, ActionImpact impact, bool includesLaunchProbe)
    {
        var entry = impact.Action.CatalogEntry;
        var purpose = impact.ReasonCode.Replace('_', ' ');
        var intent = impact.Intent == CapabilityIntent.Essential ? "essential" : "optional";
        details.AppendLine().AppendLine(CultureInfo.CurrentCulture, $"{CapabilityName(impact.CapabilityId)} ({intent})");
        details.AppendLine(CultureInfo.CurrentCulture, $"Reason {purpose}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Package {entry.Package.PackageId}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Source {entry.Package.SourceId}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Publisher {entry.Review.Publisher}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Version {entry.Package.ExactVersion}");
        if (impact.Action.Request is InstallPackageActionRequest request)
        {
            details.AppendLine(CultureInfo.CurrentCulture, $"Architecture {Architecture(request.Architecture)}");
            details.AppendLine(CultureInfo.CurrentCulture, $"Licence {entry.Review.DeclaredLicense}");
        }

        if (includesLaunchProbe)
        {
            details.AppendLine("Installer route Windows Installer");
        }

        if (entry.Package.InstallerSha256 is { } installer)
        {
            details.AppendLine(CultureInfo.CurrentCulture, $"Installer SHA-256 {installer}");
        }
        details.AppendLine(CultureInfo.CurrentCulture, $"Scope {(impact.Scope == ActionScope.Machine ? "Machine" : "Current user")}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Privilege {(impact.Privilege == ActionPrivilege.Elevated ? "Elevation required" : "Unelevated")}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Download {Bytes(impact.DownloadBytes)}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Installed size {Bytes(impact.InstalledBytes)}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Restart {Restart(impact.Restart)}");
        details.AppendLine(CultureInfo.CurrentCulture, $"Network {NetworkCategory(impact.NetworkCategory)} ({NetworkRequirement(impact.Network)})");
        details.AppendLine(CultureInfo.CurrentCulture, $"Rollback {(impact.Rollback == ActionRollback.NoChanges ? "No changes to reverse" : "Manual review")}");
        var verification = impact.Verification == ActionVerification.NoMachineChange ? "Confirm no machine change" :
            includesLaunchProbe ? "Check the exact installed package identity and briefly launch the application with no documents" :
                "Check exact installed package identity";
        details.AppendLine(CultureInfo.CurrentCulture, $"Verification {verification}");
    }

    private static string Bytes(long? bytes) => bytes is { } known
        ? known.ToString("N0", CultureInfo.CurrentCulture) + " bytes"
        : "Unknown";

    private static string Total(ByteEstimateTotal estimate) => estimate.TotalBytes is { } total
        ? Bytes(total)
        : string.Create(CultureInfo.CurrentCulture, $"{Bytes(estimate.KnownBytes)} declared, {estimate.UnknownActionCount} unknown");

    private static string NetworkCategory(ActionNetworkUse category) =>
        category == ActionNetworkUse.None ? "None" : "Software download";

    private static string Architecture(ActionArchitecture architecture) => architecture switch
    {
        ActionArchitecture.X64 => "x64",
        ActionArchitecture.Arm64 => "ARM64",
        ActionArchitecture.X86 => "x86",
        _ => "Unknown architecture",
    };

    private static string Restart(RestartEstimate estimate) => estimate switch
    {
        RestartEstimate.None => "None",
        RestartEstimate.Possible => "Possible",
        RestartEstimate.Required => "Required",
        _ => "Unknown",
    };

    private static string NetworkRequirement(NetworkEstimate estimate) => estimate switch
    {
        NetworkEstimate.None => "no network effect",
        NetworkEstimate.Optional => "optional",
        NetworkEstimate.Required => "required",
        NetworkEstimate.Conflicting => "catalog and action declarations conflict",
        _ => "unknown",
    };

    private static string BlockedMessage(PlanBlockReason reason, ProposedPlan plan) => reason switch
    {
        PlanBlockReason.SanitizationRequired => "Review the current saved external sanitization result.",
        PlanBlockReason.ProfileCatalogMismatch or PlanBlockReason.ProfilePolicyMismatch or PlanBlockReason.PolicyNotApplicable =>
            "The selected profile, catalog or policy cannot be used together.",
        PlanBlockReason.CapabilitySelectionInvalid => "Review the selected workshop capabilities.",
        PlanBlockReason.EvidenceInvalid => "The assessment evidence is unavailable or inconsistent.",
        PlanBlockReason.PolicyBlocksPreparation => plan.PolicyDecision?.Title ?? "The policy outcome blocks preparation.",
        PlanBlockReason.PackageNotApplicable => "A selected capability has no applicable catalog entry.",
        _ => "The plan needs review.",
    };
}
