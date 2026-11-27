using System.Globalization;
using System.IO;
using ThirdLife.Core.Handover;
using ThirdLife.Reports.Storage;
using ThirdLife.UI.Features.Finalization;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private const string BackupReadinessInstructions = "Select a backup drive with the recipient to check the prerequisites.";
    private const string BackupSampleRootName = "FileHistorySamples";
    private const string DisplayedBackupSampleRoot = @"%LOCALAPPDATA%\ThirdLife\SetupCore\" + BackupSampleRootName;
    private readonly FileHistoryRestoreProbe? _backupSampleProbe;
    private WindowsFileHistoryInventory? _backupInventory;
    private FileHistoryReview? _backupReview;
    private string? _reviewedBackupDrive;
    private PreparedFileHistorySample? _preparedBackupSample;

    public string BackupReadinessStatus { get; private set; } = BackupReadinessInstructions;
    public bool ShowsBackupReadiness => !IsFixtureScenario && RecipientHandover is { Handover.Mode: HandoverMode.Assisted };
    public bool CanCheckBackupReadiness => ShowsBackupReadiness && CanEditJob && !_readingFinalization;
    public Guid? BackupReadinessReviewId => _backupReview?.ReviewId;
    public bool CanPrepareBackupSample => CanCheckBackupReadiness && _backupReview is not null;
    public bool ShowsBackupSampleCheck => ShowsBackupReadiness && _preparedBackupSample is not null;
    public bool CanCheckBackupSample => CanCheckBackupReadiness && ShowsBackupSampleCheck;
    public string BackupSampleRecoveryInstructions => _preparedBackupSample is { } prepared
        ? $"Use Windows File History to restore {prepared.Sample.FileName} from {DisplayedBackupSampleRoot}\\original to {DisplayedBackupSampleRoot}\\recovered."
        : string.Empty;

    public Task CheckBackupReadinessAsync(string volumeRoot, long requiredBytes, bool recipientControlsRecovery)
    {
        if (!CanCheckBackupReadiness || Job is not { } job || requiredBytes <= 0 || !recipientControlsRecovery)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            _backupReview = null;
            _reviewedBackupDrive = null;
            _preparedBackupSample = null;
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
            _backupReview = preview.Review;
            _reviewedBackupDrive = preview.Review is null ? null : volumeRoot;
            BackupReadinessStatus = preview.Readiness switch
            {
                FileHistoryReadiness.ReviewRequired => string.Create(CultureInfo.CurrentCulture,
                    $"Backup drive {volumeRoot} has space for the declared {requiredBytes / (1024 * 1024)} MiB. Prepare a harmless sample to plan a restore check."),
                FileHistoryReadiness.InsufficientCapacity => "The selected drive has less free space than the declared backup scope.",
                FileHistoryReadiness.DestinationUnavailable => "Choose a ready local drive root separate from the Windows system volume.",
                FileHistoryReadiness.RouteUnavailable => "Windows File History prerequisites are unavailable in this session.",
                _ => "Backup setup remains pending. Review the destination, scope and recovery ownership with the recipient.",
            };
        }, refreshFinalization: false);
    }

    public Task PrepareBackupSampleAsync(Guid reviewedId, bool recipientConfirmed)
    {
        if (!CanPrepareBackupSample || !recipientConfirmed || Job is not { } job ||
            _backupReview is not { } review || review.ReviewId != reviewedId ||
            _reviewedBackupDrive is not { } volumeRoot || _backupInventory is not { } inventory)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var handover = await ReadRecipientHandoverAsync(job);
            var available = await Task.Run(WindowsFileHistoryInventory.RouteAvailable);
            var destination = available ? await Task.Run(() => inventory.InspectDestination(volumeRoot)) : null;
            var approval = review.Approve(reviewedId, recipientConfirmed);
            if (handover is null || !approval.IsCurrent(handover, available, destination))
            {
                ClearBackupReadiness();
                BackupReadinessStatus = "The handover or destination changed. Check the current prerequisites again.";
                return;
            }

            var sample = approval.CreateRestoreSample(handover, available, destination);
            var probe = _backupSampleProbe ?? RegisteredBackupSampleProbe();
            var outcome = probe is null
                ? FileHistorySamplePreparation.Unavailable
                : await Task.Run(() => probe.Prepare(sample));

            _backupReview = null;
            _reviewedBackupDrive = null;
            _preparedBackupSample = outcome == FileHistorySamplePreparation.Prepared && probe is not null
                ? new PreparedFileHistorySample(approval, sample, probe, volumeRoot) : null;
            BackupReadinessStatus = outcome switch
            {
                FileHistorySamplePreparation.Prepared => "Harmless restore sample prepared. Backup remains pending.",
                FileHistorySamplePreparation.CapacityReached => "The protected sample folder is full. Backup remains pending.",
                _ => "The protected sample could not be prepared. Backup remains pending.",
            };
        }, refreshFinalization: false);
    }

    public Task CheckBackupSampleAsync()
    {
        if (!CanCheckBackupSample || Job is not { } job || _preparedBackupSample is not { } prepared ||
            _backupInventory is not { } inventory)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var handover = await ReadRecipientHandoverAsync(job);
            var available = await Task.Run(WindowsFileHistoryInventory.RouteAvailable);
            var destination = available ? await Task.Run(() => inventory.InspectDestination(prepared.VolumeRoot)) : null;
            if (handover is null || !prepared.Approval.IsCurrent(handover, available, destination))
            {
                ClearBackupReadiness();
                BackupReadinessStatus = "The handover or destination changed. Check the current prerequisites again.";
                return;
            }

            var observed = await Task.Run(() => prepared.Probe.Observe(prepared.Sample));
            if (observed is null)
            {
                BackupReadinessStatus = "The protected recovered copy is unavailable. Backup remains pending.";
                return;
            }

            var result = prepared.Approval.EvaluateRestore(handover, available, destination, prepared.Sample,
                observed, windowsRestoreAttributed: false);
            BackupReadinessStatus = result == FileHistoryRestoreOutcome.Pending
                ? "The separate copy matches the original sample. Backup remains pending until Windows File History restoration is recorded."
                : "The separate copy could not be verified against the prepared sample. Backup remains pending.";
        }, refreshFinalization: false);
    }

    private static FileHistoryRestoreProbe? RegisteredBackupSampleProbe()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!Path.IsPathFullyQualified(root)) { return null; }
        var registered = Path.Combine(root, "ThirdLife", "SetupCore", BackupSampleRootName);
        return new FileHistoryRestoreProbe(Path.Combine(registered, "original"), Path.Combine(registered, "recovered"));
    }

    public void InvalidateBackupReadiness()
    {
        if (BackupReadinessStatus == BackupReadinessInstructions) { return; }
        ClearBackupReadiness();
        Publish();
    }

    private void ClearBackupReadiness()
    {
        _backupInventory = null;
        _backupReview = null;
        _reviewedBackupDrive = null;
        _preparedBackupSample = null;
        BackupReadinessStatus = BackupReadinessInstructions;
    }

    private sealed record PreparedFileHistorySample(FileHistoryApproval Approval, FileHistoryRestoreSample Sample,
        FileHistoryRestoreProbe Probe, string VolumeRoot);
}
