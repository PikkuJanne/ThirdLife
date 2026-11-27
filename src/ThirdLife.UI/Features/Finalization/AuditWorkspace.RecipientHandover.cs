using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private string _handoverModeFeedback = string.Empty;

    public SavedRecipientHandover? RecipientHandover { get; private set; }
    public bool CanRecordHandoverMode => CanEditJob && !_readingFinalization && RecipientHandover is { CanRecord: true };

    public string HandoverModeStatus
    {
        get
        {
            var status = RecipientHandover switch
            {
                null => "Open a job and refresh Handover to see its saved mode.",
                { IsArchived: true } => "The archived handover record is read-only.",
                { CanRecord: false } => "This job has reached its saved handover history limit.",
                { Handover.Mode: HandoverMode.Sealed } => "Sealed handover. Leave recipient setup pending and finish the machine-level checks.",
                { Handover.Mode: HandoverMode.Assisted } => "Assisted handover. Record guidance choices with the recipient present.",
                _ => "Choose a handover mode for this job.",
            };
            return _handoverModeFeedback.Length == 0 ? status : $"{_handoverModeFeedback} {status}";
        }
    }

    public Task SaveHandoverModeAsync(HandoverMode mode, string operatorCode) =>
        SaveHandoverRecordAsync(operatorCode, (store, expected, actor) => store.SaveHandoverModeAsync(expected.JobId, expected, mode, actor),
            "Handover mode saved.");

    public Task SaveRecipientSetupChoiceAsync(RecipientSetupArea area, RecipientSetupChoice choice,
        bool recipientPresent, string operatorCode) =>
        SaveHandoverRecordAsync(operatorCode, (store, expected, actor) =>
            store.SaveRecipientChoiceAsync(expected.JobId, expected, area, choice, recipientPresent, actor),
            "Recipient guidance choice saved.");

    private Task SaveHandoverRecordAsync(string operatorCode, Func<IRecipientHandoverStore, SavedRecipientHandover, OperatorId, Task> save,
        string feedback)
    {
        if (!CanRecordHandoverMode || RecipientHandover is not { } expected || _store is not IRecipientHandoverStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            _handoverModeFeedback = string.Empty;
            var actor = new OperatorId(operatorCode.Trim());
            try
            {
                await Task.Run(() => save(store, expected, actor));
                _handoverModeFeedback = feedback;
            }
            catch (JobStoreConflictException)
            {
                _handoverModeFeedback = "The saved handover changed. Review its current mode and try again.";
            }
        });
    }

    private async Task<SavedRecipientHandover?> ReadRecipientHandoverAsync(StoredJob job)
    {
        if (_store is not IRecipientHandoverStore store)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => store.LoadRecipientHandoverAsync(job.Job.JobId));
        }
        catch (JobStoreException)
        {
            return null;
        }
    }

    private void ClearHandoverMode()
    {
        ClearRecipientGuide();
        RecipientHandover = null;
        _handoverModeFeedback = string.Empty;
    }
}
