using ThirdLife.Actions.WindowsUpdates;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private bool _packagePhaseNeedsRecovery;
    private UpdateRecovery _updateRecovery;
    private bool HoldsPackageDispatch => _updateHistoryUnavailable || _updateRecovery != UpdateRecovery.None;
    public bool HasMaintenancePhaseStatus => ShowsWindowsUpdates && (HoldsPackageDispatch || _packagePhaseNeedsRecovery);

    public string MaintenancePhaseStatus => _updateHistoryUnavailable
        ? "Package installation is paused because the saved update journal could not be read. Reopen the job to review it."
        : _updateRecovery switch
        {
            UpdateRecovery.Restart => "Package installation is paused for a Windows Update restart. Save your work, restart Windows, reopen this job and scan again in Prepare.",
            UpdateRecovery.VerifyInstallation => "Package installation is paused until Windows Update verifies the installed revision. Scan again in Prepare.",
            UpdateRecovery.ReviewResult => "Package installation is paused. Review the saved update operation in Verify. A fresh scan can reconcile an interrupted preparation. An unconfirmed handoff needs workshop review.",
            UpdateRecovery.Servicing => "Windows Update is between servicing steps. If servicing was interrupted, review the saved result and run a fresh scan before starting packages.",
            _ when _packagePhaseNeedsRecovery => "Windows servicing is paused. Open Verify and reconcile the saved package using its installed state and any required restart.",
            _ => string.Empty,
        };
}
