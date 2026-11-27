using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Persistence;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private const string SavedFinalizationMessage = "Checklist result saved.";
    private Task _finalizationRead = Task.CompletedTask;
    private int _finalizationReadVersion;
    private int _completedFinalizationReadVersion = -1;
    private bool _showFinalization;
    private bool _readingFinalization;
    private string _finalizationFeedback = string.Empty;
    private bool _finalizationReadFailed;

    public FinalizationChecklist? Finalization { get; private set; }
    public bool CanRefreshFinalization => CanCloseJob && !_readingFinalization && !_hasSeparateEvidenceOrigin &&
        _store is IFinalizationStore;
    public bool CanRecordFinalization => CanEditJob && !_readingFinalization && Finalization is { CanRecord: true };

    public string FinalizationStatus
    {
        get
        {
            var status = Job is null ? "Open a workshop job to review the checklist."
                : IsBusy || _readingFinalization ? "Reviewing the saved checklist."
                : _hasSeparateEvidenceOrigin ? "This job belongs to a different live or test scenario."
                : _finalizationReadFailed || Finalization is null
                    ? "The saved checklist is unavailable. Refresh it or reopen the job."
                : Job.IsArchived ? "This archived checklist is read-only."
                : Finalization.History.Count >= FinalizationChecklist.MaximumHistoryPerJob
                    ? "This job has reached its saved confirmation limit. Earlier records remain available."
                : Finalization.EvidenceVersion is null
                    ? "Current system evidence is unavailable. Refresh the checklist before recording results."
                : Finalization.HasBlockingChecks
                    ? "Required checks still need attention. Device handover remains open."
                    : "The checklist has no open required checks. Review the remaining handover requirements.";
            return _finalizationFeedback.Length == 0 ? status : $"{_finalizationFeedback} {status}";
        }
    }

    public void ShowFinalization(bool show)
    {
        if (_showFinalization == show) { return; }
        _showFinalization = show;
        _completedFinalizationReadVersion = -1;
        Publish();
    }

    public Task RefreshFinalizationAsync()
    {
        if (!CanRefreshFinalization)
        {
            return Task.CompletedTask;
        }

        _finalizationFeedback = string.Empty;
        _handoverModeFeedback = string.Empty;
        return _finalizationRead = ReviewFinalizationAsync();
    }

    public Task SaveFinalizationAsync(FinalizationRuleId rule, FinalizationOutcome outcome,
        FinalizationReason? reason, string operatorCode)
    {
        if (!CanRecordFinalization || Finalization is not { } expected ||
            _store is not IFinalizationStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var job = RequireWritableJob();
            _finalizationFeedback = string.Empty;
            var actor = new OperatorId(operatorCode.Trim());
            try
            {
                await Task.Run(() => store.SaveFinalizationConfirmationAsync(job.Job.JobId, expected, rule, outcome,
                    reason, actor));
                ClearReport();
                _finalizationFeedback = SavedFinalizationMessage;
            }
            catch (JobStoreConflictException)
            {
                _finalizationFeedback = "The saved job changed. Review the current checklist before recording a result.";
            }
        });
    }

    private async Task LoadFinalizationAsync()
    {
        if (_store is not IFinalizationStore store || Job is not { } job || _hasSeparateEvidenceOrigin)
        {
            ClearFinalization();
            return;
        }

        var version = _finalizationReadVersion;
        FinalizationChecklist? checklist = null;
        bool readFailed = false;
        try
        {
            checklist = await Task.Run(() => store.LoadFinalizationAsync(job.Job.JobId));
            if (IsSeparateArtifactOrigin(checklist))
            {
                checklist = null;
                readFailed = true;
            }
        }
        catch (JobStoreException)
        {
            readFailed = true;
        }

        var handover = await ReadRecipientHandoverAsync(job);
        if (ReferenceEquals(Job, job) && version == _finalizationReadVersion)
        {
            if (readFailed || job.IsArchived || checklist?.EvidenceVersion != Finalization?.EvidenceVersion &&
                _finalizationFeedback == SavedFinalizationMessage)
            {
                _finalizationFeedback = string.Empty;
            }

            RecipientHandover = handover;
            Finalization = checklist;
            _completedFinalizationReadVersion = version;
            _finalizationReadFailed = readFailed;
        }
    }

    private async Task ReviewFinalizationAsync()
    {
        _readingFinalization = true;
        Publish();
        try
        {
            await LoadFinalizationAsync();
        }
        finally
        {
            _readingFinalization = false;
            Publish();
        }
    }

    private void ClearFinalization()
    {
        ClearHandoverMode();
        Finalization = null;
        _completedFinalizationReadVersion = -1;
        _finalizationFeedback = string.Empty;
        _artifactScanFeedback = string.Empty;
        _finalizationReadFailed = false;
    }
}
