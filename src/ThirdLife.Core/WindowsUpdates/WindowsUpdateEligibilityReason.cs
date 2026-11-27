namespace ThirdLife.Core.WindowsUpdates;

/// <summary>The first observed offer property that determines class eligibility for servicing.</summary>
public enum WindowsUpdateEligibilityReason
{
    Eligible = 1,
    Driver,
    UnsupportedUpdateType,
    UnknownDownloadSize,
    UnsupportedHandler,
    Beta,
    SourceMedia,
    LicenceAcceptance,
    BrowseOnly,
    Mandatory,
    Inapplicable,
    Bundle,
    UnknownInstallationBehavior,
    Interactive,
    AmbiguousClassification,
    OutsideApprovedClasses,
}
