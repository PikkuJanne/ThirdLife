using ThirdLife.Core.Handover;
using ThirdLife.UI.Features.Finalization;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private WindowsFileHistoryInventory? _backupInventory;

    public string BackupReadinessStatus { get; private set; } = "Select a backup drive with the recipient to check the prerequisites.";
    public bool ShowsBackupReadiness => !IsFixtureScenario && RecipientHandover is { Handover.Mode: HandoverMode.Assisted };
    public bool CanCheckBackupReadiness => ShowsBackupReadiness && CanEditJob && !_readingFinalization;

    public Task CheckBackupReadinessAsync(string volumeRoot, long requiredBytes, bool recipientControlsRecovery)
    {
        if (!CanCheckBackupReadiness || Job is not { } job || requiredBytes <= 0 || !recipientControlsRecovery)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            var handover = await ReadRecipientHandoverAsync(job);
            if (handover is not { IsArchived: false, Handover.Mode: HandoverMode.Assisted })
            {
                BackupReadinessStatus = "Recipient backup setup remains pending. Review the saved handover mode.";
                return;
            }

            var inventory = _backupInventory ??= new WindowsFileHistoryInventory();
            var routeAvailable = await Task.Run(WindowsFileHistoryInventory.RouteAvailable);
            var observation = routeAvailable ? await Task.Run(() => inventory.InspectDestination(volumeRoot)) : null;
            var preview = FileHistoryReview.Inspect(handover, routeAvailable, observation, requiredBytes, recipientControlsRecovery);
            BackupReadinessStatus = preview.Readiness switch
            {
                FileHistoryReadiness.ReviewRequired => "The drive has enough free space for the declared scope. Backup setup remains pending until a restore is verified.",
                FileHistoryReadiness.InsufficientCapacity => "The selected drive has less free space than the declared backup scope.",
                FileHistoryReadiness.DestinationUnavailable => "Choose a ready local drive root separate from the Windows system volume.",
                FileHistoryReadiness.RouteUnavailable => "Windows File History prerequisites are unavailable in this session.",
                _ => "Backup setup remains pending. Review the destination, scope and recovery ownership with the recipient.",
            };
        }, refreshFinalization: false);
    }

    private void ClearBackupReadiness()
    {
        _backupInventory = null;
        BackupReadinessStatus = "Select a backup drive with the recipient to check the prerequisites.";
    }
}
