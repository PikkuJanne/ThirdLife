using System.Globalization;
using System.IO;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
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
    private FileHistorySampleAudit? _latestBackupSampleAudit;
    private bool _backupSampleAuditUnavailable;

    public string BackupReadinessStatus { get; private set; } = BackupReadinessInstructions;
    public bool ShowsBackupReadiness => !IsFixtureScenario && RecipientHandover is { Handover.Mode: HandoverMode.Assisted };
    public bool CanCheckBackupReadiness => ShowsBackupReadiness && CanEditJob && !_readingFinalization;
    public Guid? BackupReadinessReviewId => _backupReview?.ReviewId;
    public bool CanPrepareBackupSample => CanCheckBackupReadiness && _backupReview is not null &&
        _store is IFileHistorySampleAuditStore and IJobDataLifecycleStore;
    public bool CanReopenBackupSample => CanPrepareBackupSample && _latestBackupSampleAudit is { } audit &&
        audit.HandoverSequence == _backupReview?.HandoverSequence && audit.CanRecord(FileHistorySampleAuditState.CopyMatches);
    public bool ShowsBackupSampleCheck => ShowsBackupReadiness && _preparedBackupSample is not null;
    public bool CanCheckBackupSample => CanCheckBackupReadiness && ShowsBackupSampleCheck;
    public string BackupSampleRecoveryInstructions => _preparedBackupSample is { } prepared
        ? $"With the recipient's backup account, use File Explorer to include only {DisplayedBackupSampleRoot}\\original in a library. " +
            $"After File History has backed it up to the confirmed drive, open Previous Versions for the original folder, " +
            $"find {prepared.Sample.FileName} in File History and choose Restore to for {DisplayedBackupSampleRoot}\\recovered. " +
            "Keep the original file in place."
        : string.Empty;
    public string BackupSampleJournalStatus
    {
        get
        {
            if (_backupSampleAuditUnavailable) { return "The saved sample history is unavailable. Backup remains pending."; }
            if (_latestBackupSampleAudit is not { } audit) { return string.Empty; }
            var prefix = audit.HandoverSequence == RecipientHandover?.Sequence ? "This handover recorded" : "An earlier handover recorded";
            var state = audit.State switch
            {
                FileHistorySampleAuditState.Preparing => "an unconfirmed sample preparation",
                FileHistorySampleAuditState.Prepared => "a prepared harmless sample",
                FileHistorySampleAuditState.PreparationUnavailable => "that sample preparation was unavailable",
                FileHistorySampleAuditState.CopyUnavailable => "that the recovered sample was unavailable",
                FileHistorySampleAuditState.CopyMismatch => "a recovered sample that did not match",
                FileHistorySampleAuditState.CopyMatches => "a matching separate sample copy",
                _ => "an unavailable sample status",
            };
            return $"{prefix} {state}. Backup remains pending.";
        }
    }

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
            var (routeAvailable, observation) = await InspectBackupDestinationAsync(inventory, volumeRoot);
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

    public Task PrepareBackupSampleAsync(Guid reviewedId, bool recipientConfirmed, string operatorCode)
    {
        if (!CanPrepareBackupSample || !recipientConfirmed || Job is not { } job ||
            _backupReview is not { } review || review.ReviewId != reviewedId ||
            _reviewedBackupDrive is not { } volumeRoot || _backupInventory is not { } inventory ||
            _store is not IFileHistorySampleAuditStore store || _store is not IJobDataLifecycleStore lifecycle)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var handover = await ReadRecipientHandoverAsync(job);
            var (available, destination) = await InspectBackupDestinationAsync(inventory, volumeRoot);
            var approval = review.Approve(reviewedId, recipientConfirmed);
            if (handover is null || !approval.IsCurrent(handover, available, destination))
            {
                ClearBackupReadiness();
                BackupReadinessStatus = "The handover or destination changed. Check the current prerequisites again.";
                return;
            }

            var sample = approval.CreateRestoreSample(handover, available, destination);
            _backupReview = null;
            _reviewedBackupDrive = null;
            try
            {
                var actor = new OperatorId(operatorCode.Trim());
                var staged = await Task.Run(() => store.StageFileHistorySampleAsync(approval, sample, actor));
                _latestBackupSampleAudit = staged;
                ClearReport();
                _backupSampleAuditUnavailable = false;
                var probe = _backupSampleProbe ?? RegisteredBackupSampleProbe();
                var outcome = probe is null
                    ? FileHistorySamplePreparation.Unavailable
                    : await Task.Run(() => lifecycle.PublishFileHistoryOriginalAsync(job.Job.JobId, staged,
                        token => probe.Prepare(sample, token)));
                var auditState = outcome == FileHistorySamplePreparation.Prepared
                    ? FileHistorySampleAuditState.Prepared : FileHistorySampleAuditState.PreparationUnavailable;
                var saved = await Task.Run(() => store.RecordFileHistorySampleAsync(job.Job.JobId, staged, auditState, actor));
                _latestBackupSampleAudit = saved;
                _preparedBackupSample = outcome == FileHistorySamplePreparation.Prepared && probe is not null
                    ? new PreparedFileHistorySample(approval, sample, probe, volumeRoot, saved, actor) : null;
                BackupReadinessStatus = outcome switch
                {
                    FileHistorySamplePreparation.Prepared => "Harmless restore sample prepared. Backup remains pending.",
                    FileHistorySamplePreparation.CapacityReached => "The protected sample folder is full. Backup remains pending.",
                    _ => "The protected sample could not be prepared. Backup remains pending.",
                };
            }
            catch (JobStoreException)
            {
                BackupReadinessStatus = "The job could not confirm sample preparation. Review the saved result before preparing another sample.";
            }
        }, refreshFinalization: false);
    }

    public Task ReopenBackupSampleAsync(Guid reviewedId, bool recipientConfirmed, string operatorCode)
    {
        if (!CanReopenBackupSample || !recipientConfirmed || Job is not { } job ||
            _backupReview is not { } review || review.ReviewId != reviewedId ||
            _reviewedBackupDrive is not { } volumeRoot || _backupInventory is not { } inventory ||
            _latestBackupSampleAudit is not { } recorded)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var handover = await ReadRecipientHandoverAsync(job);
            var (available, destination) = await InspectBackupDestinationAsync(inventory, volumeRoot);
            var (latest, unavailable) = await ReadBackupSampleAuditAsync(job.Job.JobId);
            _latestBackupSampleAudit = latest;
            _backupSampleAuditUnavailable = unavailable;
            var approval = review.Approve(reviewedId, recipientConfirmed);
            if (handover is null || latest != recorded ||
                approval.ReopenRestoreSample(handover, available, destination, recorded) is not { } sample)
            {
                ClearBackupReadiness();
                BackupReadinessStatus = "The saved sample or review changed. Check the current prerequisites again.";
                return;
            }
            var probe = _backupSampleProbe ?? RegisteredBackupSampleProbe();
            if (probe is null || !await Task.Run(() => probe.VerifyOriginal(sample)))
            {
                BackupReadinessStatus = "The saved original sample is unavailable or changed. Backup remains pending.";
                return;
            }

            var actor = new OperatorId(operatorCode.Trim());
            _backupReview = null;
            _reviewedBackupDrive = null;
            _preparedBackupSample = new PreparedFileHistorySample(approval, sample, probe, volumeRoot, recorded, actor);
            BackupReadinessStatus = "Reopened the saved harmless sample. Backup remains pending.";
        }, refreshFinalization: false);
    }

    public Task CheckBackupSampleAsync()
    {
        if (!CanCheckBackupSample || Job is not { } job || _preparedBackupSample is not { } prepared ||
            _backupInventory is not { } inventory || _store is not IFileHistorySampleAuditStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var handover = await ReadRecipientHandoverAsync(job);
            var (available, destination) = await InspectBackupDestinationAsync(inventory, prepared.VolumeRoot);
            if (handover is null || !prepared.Approval.IsCurrent(handover, available, destination))
            {
                ClearBackupReadiness();
                BackupReadinessStatus = "The handover or destination changed. Check the current prerequisites again.";
                return;
            }

            var observed = await Task.Run(() => prepared.Probe.Observe(prepared.Sample));
            var check = prepared.Approval.EvaluateRestore(handover, available, destination, prepared.Sample,
                observed, windowsRestoreAttributed: false);
            var outcome = observed is null ? FileHistorySampleAuditState.CopyUnavailable
                : check == FileHistoryRestoreOutcome.Pending ? FileHistorySampleAuditState.CopyMatches
                : FileHistorySampleAuditState.CopyMismatch;
            try
            {
                var saved = await Task.Run(() => store.RecordFileHistorySampleAsync(job.Job.JobId, prepared.Audit, outcome, prepared.OperatorId));
                _latestBackupSampleAudit = saved;
                if (saved != prepared.Audit) { ClearReport(); }
                _preparedBackupSample = prepared with { Audit = saved };
                BackupReadinessStatus = outcome switch
                {
                    FileHistorySampleAuditState.CopyUnavailable => "The protected recovered copy is unavailable. Backup remains pending.",
                    FileHistorySampleAuditState.CopyMatches => "The separate copy matches the original sample. Backup remains pending until Windows File History restoration is recorded.",
                    _ => "The separate copy could not be verified against the prepared sample. Backup remains pending.",
                };
            }
            catch (JobStoreException)
            {
                ClearBackupReadiness();
                BackupReadinessStatus = "The job could not save the copy result. Backup remains pending.";
            }
        }, refreshFinalization: false);
    }

    private static Task<(bool Available, FileHistoryDestinationObservation? Destination)> InspectBackupDestinationAsync(
        WindowsFileHistoryInventory inventory, string volumeRoot) => Task.Run(() =>
    {
        var available = WindowsFileHistoryInventory.RouteAvailable();
        return (available, available ? inventory.InspectDestination(volumeRoot) : null);
    });

    private async Task<(FileHistorySampleAudit? Audit, bool Unavailable)> ReadBackupSampleAuditAsync(JobId jobId,
        CancellationToken token = default)
    {
        if (_store is not IFileHistorySampleAuditStore store) { return (null, false); }
        try
        {
            var history = await Task.Run(() => store.LoadFileHistorySampleHistoryAsync(jobId, token), token);
            return (history.Count == 0 ? null : history[^1], false);
        }
        catch (JobStoreException)
        {
            return (null, true);
        }
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
        FileHistoryRestoreProbe Probe, string VolumeRoot, FileHistorySampleAudit Audit, OperatorId OperatorId);
}
