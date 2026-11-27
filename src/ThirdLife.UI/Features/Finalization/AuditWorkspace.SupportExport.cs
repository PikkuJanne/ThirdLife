using System.IO;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Support;
using ThirdLife.Diagnostics;
using ThirdLife.Diagnostics.Redaction;
using ThirdLife.Inventory;
using ThirdLife.Persistence;
using ThirdLife.Reports.Support;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private const string SupportFolderName = "ThirdLifeSupport";
    private CancellationTokenSource? _supportCancellation;
    private FrozenSupportPreview? _supportPreview;
    private PendingSupportCompletion? _supportCompletion;
    private int _pendingSupportExports;

    public string SupportExportStatus { get; private set; } = "Open a workshop job to preview a support archive.";
    public string SupportHistoryStatus => _pendingSupportExports == 0 ? string.Empty :
        $"There are {_pendingSupportExports} saved support export intents with unconfirmed outcomes. Review them before sharing any related copy.";
    public SupportArchivePreview? SupportPreview => _supportPreview?.Archive;
    public string SupportPreviewSummary => SupportPreview is { } preview
        ? $"{preview.ArchiveByteCount} ZIP bytes. SHA-256 {preview.ArchiveSha256}." : string.Empty;
    public bool CanPreviewSupport => CanCloseJob && !_hasSeparateEvidenceOrigin && !_readingFinalization;
    public bool CanExportSupport => CanEditJob && CanPreviewSupport && _store is ISupportExportAuditStore &&
        _supportCompletion is null && _supportPreview?.JobId == Job?.Job.JobId;
    public bool CanRetrySupportAudit => CanCloseJob && !_hasSeparateEvidenceOrigin &&
        _store is ISupportExportAuditStore && _supportCompletion?.JobId == Job?.Job.JobId;
    public bool CanCancelSupport => _supportCancellation is { IsCancellationRequested: false };

    public Task CreateSupportPreviewAsync()
    {
        if (!CanPreviewSupport || Job is not { } job) { return Task.CompletedTask; }
        var jobId = job.Job.JobId;
        return RunSupportAsync("Preparing the sanitized support preview.", async token =>
        {
            _supportPreview = null;
            var inputs = await ReadSupportInputsAsync(jobId, token);
            var now = _timeProvider.GetUtcNow();
            var archive = await Task.Run(() => SupportArchivePreview.Create(inputs.Facts, inputs.Checks, inputs.PackageOutcomes,
                now, token), token);
            _supportPreview = new(jobId, inputs.Version, archive);
            SupportExportStatus = "Review both files and confirm this exact archive before choosing a protected local folder.";
        });
    }

    public Task ExportSupportAsync(SupportArchivePreview preview, bool approved, string operatorCode, string directory)
    {
        if (!approved || !CanExportSupport || _supportPreview is not { } frozen ||
            !ReferenceEquals(frozen.Archive, preview) || _store is not ISupportExportAuditStore auditStore)
        {
            return Task.CompletedTask;
        }

        return RunSupportAsync("Checking the saved facts before exporting the approved ZIP.", async token =>
        {
            var actor = new OperatorId(operatorCode.Trim());
            var current = await ReadSupportInputsAsync(frozen.JobId, token);
            if (current.Version != frozen.Version ||
                !await Task.Run(() => preview.Matches(current.Facts, current.Checks, current.PackageOutcomes, token), token))
            {
                _supportPreview = null;
                SupportExportStatus = "The saved facts changed. Review a fresh preview before exporting.";
                return;
            }

            var destination = new SupportArchiveStore(Path.Combine(directory, SupportFolderName));
            var intent = SupportAudit(preview, actor, SupportExportState.Pending);
            await Task.Run(() => auditStore.StageSupportExportAsync(frozen.JobId, intent, token), token);
            _supportPreview = null;
            await PublishSupportExportAsync(frozen, destination, intent, auditStore, token);
        });
    }

    public Task RetrySupportAuditAsync()
    {
        if (!CanRetrySupportAudit || _supportCompletion is not { } pending || _store is not ISupportExportAuditStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () => await RecordPublishedSupportAsync(store, pending), refreshFinalization: false);
    }

    public void CancelSupport()
    {
        if (!CanCancelSupport) { return; }
        _cancellationRequest = _supportCancellation!.CancelAsync();
        SupportExportStatus = "Support operation cancellation requested.";
        Publish();
    }

    private Task RunSupportAsync(string progress, Func<CancellationToken, Task> command) => RunAsync(async () =>
    {
        var cancellation = _supportCancellation = new CancellationTokenSource();
        SupportExportStatus = progress;
        Publish();
        try
        {
            await command(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SupportExportStatus = "Support operation cancelled.";
        }
        catch (JobStoreConflictException)
        {
            _supportPreview = null;
            SupportExportStatus = "The saved job changed. Review a fresh preview before exporting.";
        }
        catch (Exception exception) when (exception is JobStoreException or DiagnosticContractException or
            ArgumentException or InvalidOperationException or AssessmentException)
        {
            SupportExportStatus = "The support operation could not finish. Check the saved job and review the preview.";
        }
    }, refreshFinalization: false);

    private async Task PublishSupportExportAsync(FrozenSupportPreview frozen, SupportArchiveStore destination,
        SupportExportAudit intent, ISupportExportAuditStore auditStore, CancellationToken token)
    {
        SupportArchiveArtifact artifact;
        try
        {
            artifact = await Task.Run(() => destination.Export(frozen.Archive, intent.ContentSha256, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await RecordUnpublishedSupportAsync(auditStore, frozen.JobId, frozen.Archive, intent.OperatorId,
                "Export cancelled before publication.");
            return;
        }
        catch (DiagnosticContractException error)
        {
            if (error.ResultCode is "support_destination_unavailable" or "support_preview_changed" or
                "support_capacity" or "support_file_exists")
            {
                await RecordUnpublishedSupportAsync(auditStore, frozen.JobId, frozen.Archive, intent.OperatorId, error.Message);
            }
            else
            {
                SupportExportStatus = "Archive publication could not be confirmed. The saved intent needs review.";
                await RefreshSupportAuditAsync(frozen.JobId);
            }

            return;
        }

        var completion = SupportAudit(frozen.Archive, intent.OperatorId, SupportExportState.Published);
        _supportCompletion = new(frozen.JobId, completion, artifact.FileName);
        await RecordPublishedSupportAsync(auditStore, _supportCompletion);
    }

    private async Task RecordPublishedSupportAsync(ISupportExportAuditStore store, PendingSupportCompletion pending)
    {
        try
        {
            await Task.Run(() => store.RecordSupportExportAsync(pending.JobId, pending.Audit));
            _supportCompletion = null;
            ClearReport();
            SupportExportStatus = $"Saved {pending.FileName} in {SupportFolderName} and recorded the support export.";
        }
        catch (JobStoreException)
        {
            SupportExportStatus = $"Saved {pending.FileName}. Retry recording this export in the job history.";
        }

        await RefreshSupportAuditAsync(pending.JobId);
    }

    private async Task RecordUnpublishedSupportAsync(ISupportExportAuditStore store, JobId jobId,
        SupportArchivePreview preview, OperatorId actor, string status)
    {
        try
        {
            var receipt = SupportAudit(preview, actor, SupportExportState.NotPublished);
            await Task.Run(() => store.RecordSupportExportAsync(jobId, receipt));
            SupportExportStatus = $"{status} Create a fresh preview to try again.";
        }
        catch (JobStoreException)
        {
            SupportExportStatus = $"{status} The saved intent needs review.";
        }

        await RefreshSupportAuditAsync(jobId);
    }

    private async Task RefreshSupportAuditAsync(JobId jobId)
    {
        if (_store is not ISupportExportAuditStore store || _hasSeparateEvidenceOrigin) { return; }
        try
        {
            var history = await Task.Run(() => store.LoadSupportExportsAsync(jobId));
            if (Job?.Job.JobId == jobId)
            {
                _pendingSupportExports = history.Count(row => row.State == SupportExportState.Pending);
            }
        }
        catch (JobStoreException)
        {
            // A failed refresh cannot clear an earlier unconfirmed status.
        }
    }

    private SupportExportAudit SupportAudit(SupportArchivePreview preview, OperatorId actor, SupportExportState state) =>
        new(preview.InternalSupportId, SupportArchivePreview.SchemaVersion, SupportArchivePreview.ManifestVersion,
            preview.ArchiveSha256, _timeProvider.GetUtcNow(), actor, state);

    private void ClearSupportExport()
    {
        _supportPreview = null;
        _supportCompletion = null;
        _pendingSupportExports = 0;
        SupportExportStatus = "Open a workshop job to preview a support archive.";
    }

    private sealed record FrozenSupportPreview(JobId JobId, SupportInputVersion Version, SupportArchivePreview Archive);
    private sealed record PendingSupportCompletion(JobId JobId, SupportExportAudit Audit, string FileName);
}
