using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private ArchivedJobDeletionReview? _jobDeletionReview;
    private IReadOnlyList<JobLocalFile>? _retainedJobFiles;

    public bool ShowsJobDeletion => ShowsJobArchive && Job is { IsArchived: true, Checkpoints.Count: > 0 } &&
        _store is IJobDataLifecycleStore;
    public bool CanReviewJobDeletion => ShowsJobDeletion && CanCloseJob;
    public bool ShowsJobDeletionReview => ShowsJobDeletion && _jobDeletionReview is not null;
    public bool JobDeletionPending => _jobDeletionReview?.ApprovedBy is not null;
    public bool CanApproveJobDeletion => CanReviewJobDeletion && _jobDeletionReview is { ApprovedBy: null };
    public bool CanResumeJobDeletion => CanReviewJobDeletion && JobDeletionPending;
    public string JobDeletionJobCode => _jobDeletionReview?.JobId.Value ?? string.Empty;

    public string JobDeletionFilesLabel => _retainedJobFiles is not null
        ? "Recorded local files still needing attention"
        : "Recorded local files included in this review";

    public string JobDeletionFiles => (_retainedJobFiles ?? _jobDeletionReview?.Files) is { Count: > 0 } files
        ? string.Join(Environment.NewLine, files.Select(file => file.FileName))
        : "This review has no recorded local files.";

    public Task ReviewJobDeletionAsync()
    {
        if (!CanReviewJobDeletion || Job is not { } job || _store is not IJobDataLifecycleStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            _retainedJobFiles = null;
            _jobDeletionReview = await Task.Run(() => store.ReviewArchivedJobDeletionAsync(
                job.Job.JobId, job.Checkpoints[^1].CheckpointId));
            Status = _jobDeletionReview switch
            {
                null => "Deletion is unavailable. Reopen the archived job to check its saved state and resolve any pending action recovery.",
                { ApprovedBy: not null } => "This archived job has an approved deletion waiting to finish. Review its recorded files before continuing.",
                _ => "Review the archived job and its recorded local files before approving permanent deletion.",
            };
        }, refreshFinalization: false);
    }

    public Task ApproveJobDeletionAsync(string operatorCode, bool confirmed)
    {
        if (!CanApproveJobDeletion || !confirmed || _jobDeletionReview is not { } review ||
            _store is not IJobDataLifecycleStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var actor = new OperatorId(operatorCode.Trim());
            var approved = await Task.Run(() => store.ApproveArchivedJobDeletionAsync(review, actor));
            _jobDeletionReview = approved;
            await FinishJobDeletionAsync(store, approved);
        }, refreshFinalization: false);
    }

    public Task ResumeJobDeletionAsync()
    {
        if (!CanResumeJobDeletion || _jobDeletionReview is not { } approved ||
            _store is not IJobDataLifecycleStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(() => FinishJobDeletionAsync(store, approved), refreshFinalization: false);
    }

    private async Task FinishJobDeletionAsync(IJobDataLifecycleStore store, ArchivedJobDeletionReview approved)
    {
        var result = await Task.Run(() => store.ExecuteArchivedJobDeletionAsync(
            approved.JobId, approved.ArchiveCheckpointId, RemoveRecordedJobFile));
        if (result.Completed)
        {
            ClearOpenJob();
            Status = result.EmptyDirectoryRetained
                ? "The archived job and its recorded local files are absent. Its job folder remains for a later ownership check."
                : "The archived job and its recorded local files are absent. You can open another job.";
            return;
        }

        _retainedJobFiles = result.RetainedFiles;
        Status = result.RetainedFiles.Count == 1
            ? "Deletion is still pending. One recorded local file needs attention. The job stays available while you review and retry."
            : string.Create(CultureInfo.CurrentCulture,
                $"Deletion is still pending. {result.RetainedFiles.Count} recorded local files need attention. The job stays available while you review and retry.");
    }

    private JobLocalFileRemoval RemoveRecordedJobFile(JobLocalFile file, CancellationToken cancellationToken) =>
        file.Kind switch
        {
            JobLocalFileKind.WorkshopReport => _reportStore?.RemoveRecordedFile(file, cancellationToken)
                ?? JobLocalFileRemoval.Retained,
            JobLocalFileKind.RecipientGuide => _recipientGuideStore?.RemoveRecordedFile(file, cancellationToken)
                ?? JobLocalFileRemoval.Retained,
            JobLocalFileKind.FileHistoryOriginal => (_backupSampleProbe ?? RegisteredBackupSampleProbe())
                ?.RemoveRecordedOriginal(file, cancellationToken) ?? JobLocalFileRemoval.Retained,
            _ => JobLocalFileRemoval.Retained,
        };

    private Task PublishOwnedJobFileAsync(JobLocalFileKind kind, WorkshopReportArtifact artifact,
        Action<CancellationToken> publish, CancellationToken cancellationToken)
    {
        if (_store is not IJobDataLifecycleStore store)
        {
            throw new InvalidOperationException("The local job store cannot record ownership before publication.");
        }

        var receipt = artifact.Receipt;
        return store.PublishJobLocalFileAsync(artifact.JobId,
            new(kind, receipt.FileName, receipt.ByteLength, receipt.Sha256), publish, cancellationToken);
    }

    private async Task<bool> LoadPendingJobDeletionAsync(StoredJob job)
    {
        ClearJobDeletionReview();
        if (!ShowsJobDeletion || _store is not IJobDataLifecycleStore store)
        {
            return false;
        }

        var review = await Task.Run(() => store.ReviewArchivedJobDeletionAsync(
            job.Job.JobId, job.Checkpoints[^1].CheckpointId));
        if (review is not { ApprovedBy: not null })
        {
            return false;
        }

        _jobDeletionReview = review;
        return true;
    }

    private void ClearJobDeletionReview()
    {
        _jobDeletionReview = null;
        _retainedJobFiles = null;
    }
}
