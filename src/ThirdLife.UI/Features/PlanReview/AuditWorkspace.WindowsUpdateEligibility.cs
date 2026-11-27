using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private static string DescribeEligibility(WindowsUpdateEligibilityReason? reason) => reason switch
    {
        WindowsUpdateEligibilityReason.Eligible => "The offer meets the class and installer requirements for review.",
        WindowsUpdateEligibilityReason.Driver => "Windows classifies this offer as a driver. Driver servicing is outside this workflow.",
        WindowsUpdateEligibilityReason.UnsupportedUpdateType => "Windows reported an unsupported update type.",
        WindowsUpdateEligibilityReason.UnknownDownloadSize => "Windows could not supply a usable download size.",
        WindowsUpdateEligibilityReason.UnsupportedHandler => "Windows did not identify the required component-based servicing handler.",
        WindowsUpdateEligibilityReason.Beta => "Windows marks this update as beta.",
        WindowsUpdateEligibilityReason.SourceMedia => "This update can require additional source media.",
        WindowsUpdateEligibilityReason.LicenceAcceptance => "The update licence terms require acceptance.",
        WindowsUpdateEligibilityReason.BrowseOnly => "Windows marks this offer for optional selection.",
        WindowsUpdateEligibilityReason.Mandatory => "Windows marks this update as mandatory. This workflow excludes mandatory updates.",
        WindowsUpdateEligibilityReason.Inapplicable => "Windows no longer offers this revision for installation.",
        WindowsUpdateEligibilityReason.Bundle => "This offer contains bundled updates. The workflow requires a single update.",
        WindowsUpdateEligibilityReason.UnknownInstallationBehavior => "Windows did not report how this update installs.",
        WindowsUpdateEligibilityReason.Interactive => "Installing this update can require user input.",
        WindowsUpdateEligibilityReason.AmbiguousClassification => "Windows returned an ambiguous or incomplete update classification.",
        WindowsUpdateEligibilityReason.OutsideApprovedClasses => "The offer falls outside the security and critical update classes.",
        _ => "Offer eligibility could not be established.",
    };
}
