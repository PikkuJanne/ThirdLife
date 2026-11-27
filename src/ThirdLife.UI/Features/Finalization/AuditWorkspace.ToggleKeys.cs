using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.UI.Features.Finalization;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private IRecipientToggleKeys? _toggleKeys;
    private ToggleKeysChange? _toggleKeysChange;
    private ToggleKeysAuditEntry? _latestToggleKeysAudit;

    public string ToggleKeysStatus { get; private set; } = "Review the current setting with the recipient before choosing a change.";
    public ToggleKeysChange? ToggleKeysChangeReview => _toggleKeysChange;
    public bool ShowsToggleKeys => RecipientHandover is { Handover.Mode: HandoverMode.Assisted } &&
        _store is IToggleKeysAuditStore && (!IsFixtureScenario || _toggleKeys is not null);
    public bool CanPreviewToggleKeys => ShowsToggleKeys && CanEditJob && !_readingFinalization;
    public bool CanApplyToggleKeys => CanPreviewToggleKeys && _toggleKeysChange is { } change &&
        change.JobId == Job?.Job.JobId && change.HandoverSequence == RecipientHandover?.Sequence &&
        _latestToggleKeysAudit?.Outcome != ToggleKeysAuditOutcome.ApprovedPending;
    public bool CanReviewToggleKeysRestoration => CanPreviewToggleKeys &&
        _latestToggleKeysAudit?.Outcome == ToggleKeysAuditOutcome.Verified;

    public Task PreviewToggleKeysAsync(bool requested)
    {
        if (!CanPreviewToggleKeys || Job is not { } job || _store is not IToggleKeysAuditStore store)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            _toggleKeysChange = null;
            var handover = await ReadRecipientHandoverAsync(job);
            if (handover is not { Handover.Mode: HandoverMode.Assisted })
            {
                ToggleKeysStatus = "The saved handover mode changed. Review the current mode before choosing a setting.";
                return;
            }

            await RefreshToggleKeysAuditAsync(store, job.Job.JobId);
            _toggleKeys ??= new WindowsToggleKeys();
            var observed = await Task.Run(_toggleKeys.Read);
            var preview = ToggleKeysChange.Preview(handover, observed, requested);
            _toggleKeysChange = preview.Change;
            ToggleKeysStatus = _latestToggleKeysAudit?.Outcome == ToggleKeysAuditOutcome.ApprovedPending
                ? "An interrupted setting attempt remains unconfirmed. Review its outcome before applying another change."
                : DescribeToggleKeysPreview(preview);
        });
    }

    public Task PreviewToggleKeysRestorationAsync() => CanReviewToggleKeysRestoration && _latestToggleKeysAudit is { } prior
        ? PreviewToggleKeysAsync(prior.Before) : Task.CompletedTask;

    public Task ApplyToggleKeysAsync(ToggleKeysChange expected, bool recipientConfirmed, string operatorCode)
    {
        if (!recipientConfirmed || !CanApplyToggleKeys || !ReferenceEquals(_toggleKeysChange, expected) ||
            Job is not { } job || _toggleKeys is not { } adapter || _store is not IToggleKeysAuditStore store)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            _toggleKeysChange = null;
            var approval = expected.Approve(expected.ChangeId, recipientConfirmed);
            var actor = new OperatorId(operatorCode.Trim());
            try
            {
                var staged = await Task.Run(() => store.StageToggleKeysAsync(approval, actor));
                _latestToggleKeysAudit = staged;
                var handover = await ReadRecipientHandoverAsync(job);
                var dispatch = handover is null ? ToggleKeysDispatchResult.PreconditionsChanged
                    : await Task.Run(() => adapter.Dispatch(approval, handover));
                var (outcome, independentlyObserved) = await MeasureToggleKeysOutcomeAsync(approval, adapter, job, dispatch);
                await Task.Run(() => store.RecordToggleKeysOutcomeAsync(job.Job.JobId, staged, outcome, independentlyObserved));
                await RefreshToggleKeysAuditAsync(store, job.Job.JobId);
                ToggleKeysStatus = DescribeToggleKeysOutcome(outcome);
            }
            catch (JobStoreException)
            {
                ToggleKeysStatus = "The job could not record a confirmed result. Review the saved attempt before another change.";
            }
        });
    }

    public Task KeepToggleKeysAttemptUnconfirmedAsync()
    {
        if (!CanPreviewToggleKeys || Job is not { } job || _store is not IToggleKeysAuditStore store ||
            _latestToggleKeysAudit is not { Outcome: ToggleKeysAuditOutcome.ApprovedPending } pending)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            await Task.Run(() => store.RecordToggleKeysOutcomeAsync(job.Job.JobId, pending,
                ToggleKeysAuditOutcome.VerificationUnavailable, null));
            _toggleKeysChange = null;
            await RefreshToggleKeysAuditAsync(store, job.Job.JobId);
            ToggleKeysStatus = "The interrupted attempt remains unverified in history. Preview the current setting before a new request.";
        });
    }

    public bool CanKeepToggleKeysAttemptUnconfirmed => CanPreviewToggleKeys &&
        _latestToggleKeysAudit?.Outcome == ToggleKeysAuditOutcome.ApprovedPending;

    private async Task<(ToggleKeysAuditOutcome, bool?)> MeasureToggleKeysOutcomeAsync(ToggleKeysApproval approval,
        IRecipientToggleKeys adapter, StoredJob job, ToggleKeysDispatchResult dispatch)
    {
        if (dispatch == ToggleKeysDispatchResult.PreconditionsChanged) { return (ToggleKeysAuditOutcome.PreconditionChanged, null); }
        if (dispatch == ToggleKeysDispatchResult.WindowsDeclined) { return (ToggleKeysAuditOutcome.WindowsDeclined, null); }
        if (await ReadRecipientHandoverAsync(job) is not { } handover) { return (ToggleKeysAuditOutcome.VerificationUnavailable, null); }
        var observed = await Task.Run(adapter.Read);
        return approval.Verify(handover, observed) switch
        {
            RecipientSettingVerification.Verified => (ToggleKeysAuditOutcome.Verified, observed.Enabled),
            RecipientSettingVerification.Mismatch => (ToggleKeysAuditOutcome.ObservedMismatch, observed.Enabled),
            _ => (ToggleKeysAuditOutcome.VerificationUnavailable, null),
        };
    }

    private async Task RefreshToggleKeysAuditAsync(IToggleKeysAuditStore store, JobId jobId)
    {
        var records = await Task.Run(() => store.LoadToggleKeysAttemptsAsync(jobId));
        _latestToggleKeysAudit = records.Count == 0 ? null : records[^1];
    }

    private void ClearToggleKeysReview()
    {
        _toggleKeysChange = null;
        _latestToggleKeysAudit = null;
        ToggleKeysStatus = "Review the current setting with the recipient before choosing a change.";
    }

    private static string DescribeToggleKeysPreview(ToggleKeysPreview preview) => preview.Availability switch
    {
        RecipientSettingAvailability.ReviewRequired when preview.Change is { } change =>
            $"Current Windows user only. Toggle Keys is {(change.Before ? "on" : "off")}. The requested setting is {(change.Requested ? "on" : "off")}.",
        RecipientSettingAvailability.NoChange => "The current Windows user already has the selected setting.",
        RecipientSettingAvailability.Deferred => "Recipient accessibility setup is pending.",
        _ => "Windows could not provide the current Toggle Keys setting.",
    };

    private static string DescribeToggleKeysOutcome(ToggleKeysAuditOutcome outcome) => outcome switch
    {
        ToggleKeysAuditOutcome.Verified => "Windows independently confirmed the requested setting for the current user.",
        ToggleKeysAuditOutcome.ObservedMismatch => "Windows reported a different setting after the attempt. Review the actual state.",
        ToggleKeysAuditOutcome.PreconditionChanged => "The job, user or setting changed before dispatch. Preview the current state again.",
        ToggleKeysAuditOutcome.WindowsDeclined => "Windows declined the requested setting change.",
        _ => "The setting attempt has no confirmed result. Review the current setting with the recipient.",
    };
}
