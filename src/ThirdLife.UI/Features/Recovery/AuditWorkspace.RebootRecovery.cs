using System.Globalization;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;

namespace ThirdLife.UI.Shell;

internal sealed record RebootRecoveryChoice(RebootResumeCandidate Candidate)
{
    public string Label => string.Format(CultureInfo.CurrentCulture, "Resume by {0:g} (action {1:D})",
        Candidate.Checkpoint.ExpiresAtUtc.ToLocalTime(), Candidate.Checkpoint.ActionId);
}

internal sealed partial class AuditWorkspace
{
    private RebootRecoveryChoice? _selectedRebootRecovery;

    public IReadOnlyList<RebootRecoveryChoice> RebootRecoveryChoices { get; private set; } = [];
    public string RebootRecoveryStatus { get; private set; } = string.Empty;
    public bool HasPendingRebootRecovery => RebootRecoveryChoices.Count > 0;
    public bool HasRebootRecovery => HasPendingRebootRecovery || RebootRecoveryStatus.Length > 0;
    public bool CanAbandonRebootRecovery => CanEditJob && SelectedRebootRecovery is not null;
    public bool CanResumeRebootRecovery => CanAbandonRebootRecovery &&
        SelectedRebootRecovery!.Candidate.Checkpoint.IsCurrent(_timeProvider.GetUtcNow());

    public RebootRecoveryChoice? SelectedRebootRecovery
    {
        get => _selectedRebootRecovery;
        set
        {
            if (ReferenceEquals(_selectedRebootRecovery, value)) { return; }
            _selectedRebootRecovery = value is not null && RebootRecoveryChoices.Contains(value) ? value : null;
            PropertyChanged?.Invoke(this, new(nameof(CanResumeRebootRecovery)));
            PropertyChanged?.Invoke(this, new(nameof(CanAbandonRebootRecovery)));
        }
    }

    public Task DecideRebootRecoveryAsync(RebootDisposition disposition)
    {
        if (_store is not IRebootCheckpointStore store || SelectedRebootRecovery is not { Candidate: var candidate } ||
            Job?.Job.JobId != candidate.Checkpoint.Plan.JobId || !CanAbandonRebootRecovery ||
            (disposition == RebootDisposition.Resume && !CanResumeRebootRecovery))
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            var jobId = candidate.Checkpoint.Plan.JobId;
            string status;
            try
            {
                var result = await Task.Run(() => store.DecideRebootCheckpointAsync(jobId,
                    candidate.Checkpoint.Id, candidate.Token, disposition));
                status = result is null
                    ? "This checkpoint cannot be used. Review the saved job before continuing."
                    : disposition == RebootDisposition.Resume
                        ? "Resume recorded. Review the saved action. Fresh verification is still required."
                        : "Resume abandoned. The saved action and workshop job are still available for review.";
            }
            catch (JobStoreException)
            {
                status = "The resume decision could not be confirmed. Review the saved job before trying again.";
            }

            await LoadRebootRecoveryAsync(jobId);
            RebootRecoveryStatus = status;
        });
    }

    private async Task LoadRebootRecoveryAsync(JobId jobId)
    {
        if (_store is not IRebootCheckpointStore store || Job is not { IsArchived: false } || _hasSeparateEvidenceOrigin)
        {
            ClearRebootRecovery();
            return;
        }

        _selectedRebootRecovery = null;
        try
        {
            var (candidates, lastDecision) = await Task.Run(async () =>
                (await store.ListPendingRebootCheckpointsAsync(jobId).ConfigureAwait(false),
                    await store.LoadLatestRebootDecisionAsync(jobId).ConfigureAwait(false)));
            RebootRecoveryChoices = candidates.Select(candidate => new RebootRecoveryChoice(candidate)).ToArray();
            RebootRecoveryStatus = lastDecision is null ? string.Empty : DescribeRebootDecision(lastDecision);
        }
        catch (JobStoreException)
        {
            RebootRecoveryChoices = [];
            RebootRecoveryStatus = "Saved restart checkpoints could not be read safely. Reopen the job to check again.";
        }
    }

    private static string DescribeRebootDecision(RebootRecoveryDecision decision) =>
        string.Format(CultureInfo.CurrentCulture, decision.Disposition == RebootDisposition.Resume
            ? "Resume recorded on {0:g} for action {1:D}. Fresh verification is still required."
            : "Resume abandoned on {0:g} for action {1:D}. The workshop job remains available for review.",
            decision.DecidedAtUtc.ToLocalTime(), decision.Checkpoint.ActionId);

    private void ClearRebootRecovery()
    {
        _selectedRebootRecovery = null;
        RebootRecoveryChoices = [];
        RebootRecoveryStatus = string.Empty;
    }
}
