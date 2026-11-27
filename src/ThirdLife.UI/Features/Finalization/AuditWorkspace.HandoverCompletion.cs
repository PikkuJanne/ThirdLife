using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace : IHandoverFileVerifier
{
    private HandoverEligibility? _reviewedHandover;
    private bool _handoverCompleted;

    public string HandoverCompletionStatus { get; private set; } = "Review the saved evidence before marking the device handed over.";
    public IReadOnlyList<string> HandoverBlockers { get; private set; } = [];
    public IReadOnlyList<string> HandoverOutputSummary { get; private set; } = [];
    public bool CanReviewHandover => CanCloseJob && !_hasSeparateEvidenceOrigin && _store is IHandoverCompletionStore;
    public bool CanRecordSupportOmission => CanEditJob && CanReviewHandover && !IsFixtureScenario &&
        _reviewedHandover?.Scope.Origin == HandoverEvidenceOrigin.ActiveDevice;
    public bool CanSignOffHandover => CanRecordSupportOmission && !_handoverCompleted &&
        _reviewedHandover is { CanRequestSignOff: true };

    public Task ReviewHandoverAsync()
    {
        if (!CanReviewHandover || _store is not IHandoverCompletionStore store || Job is not { } job)
        {
            return Task.CompletedTask;
        }
        return RunAsync(() => RefreshHandoverReviewAsync(store, job.Job.JobId), refreshFinalization: false);
    }

    public Task RecordSupportOmissionAsync(string operatorCode, SupportBundleOmissionReason reason)
    {
        if (!CanRecordSupportOmission || _store is not IHandoverCompletionStore store || _reviewedHandover is not { } reviewed)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            try
            {
                var actor = new OperatorId(operatorCode.Trim());
                await Task.Run(() => store.SaveHandoverSupportOmissionAsync(reviewed.Scope, actor, reason));
                await RefreshHandoverReviewAsync(store, reviewed.Scope.JobId);
            }
            catch (JobStoreConflictException)
            {
                ClearHandoverReview("The saved job changed. Review handover again before recording a decision.");
            }
        }, refreshFinalization: false);
    }

    public Task SignOffHandoverAsync(string operatorCode, bool confirmed)
    {
        if (!confirmed || !CanSignOffHandover || _store is not IHandoverCompletionStore store || _reviewedHandover is not { } reviewed)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            try
            {
                var actor = new OperatorId(operatorCode.Trim());
                await Task.Run(() => store.CompleteHandoverAsync(reviewed, actor, this));
                await RefreshHandoverReviewAsync(store, reviewed.Scope.JobId);
            }
            catch (JobStoreConflictException)
            {
                ClearHandoverReview("The saved job or its outputs changed. Review handover again before signing off.");
            }
        }, refreshFinalization: false);
    }

    private async Task RefreshHandoverReviewAsync(IHandoverCompletionStore store, JobId jobId)
    {
        ClearHandoverReview("Reading the current handover evidence.");
        Publish();
        try
        {
            _reviewedHandover = await Task.Run(() => store.LoadHandoverReviewAsync(jobId, this));
            var saved = await Task.Run(() => store.ReadHandoverCompletionAsync(jobId, this));
            _handoverCompleted = saved is { IsCurrent: true } && _reviewedHandover is { } reviewed && saved.Latest.IsCurrent(reviewed);
            HandoverBlockers = _reviewedHandover?.Blockers.Select(DescribeHandoverBlocker).ToArray() ?? [];
            HandoverOutputSummary = _reviewedHandover?.Evidence.Select(DescribeHandoverOutput).OfType<string>().ToArray() ?? [];
            HandoverCompletionStatus = _handoverCompleted && saved is not null
                ? $"Handed over. Sign-off recorded by {saved.Latest.OperatorId.Value} at {saved.Latest.RecordedAtUtc:g} UTC."
                : saved is not null ? "A prior sign-off is in the job history. Review the current evidence before a new sign-off."
                : _reviewedHandover is { CanRequestSignOff: true } ? "Ready for operator sign-off. Confirm the completed handover below."
                : "Handover is blocked. Review the outstanding requirements below.";
        }
        catch (JobStoreException)
        {
            ClearHandoverReview("The saved handover evidence could not be checked. Reopen the job and try again.");
        }
    }

    private void ClearHandoverReview(string text = "Review the saved evidence before marking the device handed over.")
    {
        _reviewedHandover = null;
        _handoverCompleted = false;
        HandoverBlockers = [];
        HandoverOutputSummary = [];
        HandoverCompletionStatus = text;
    }

    public bool ShowsArchivedOutputReview =>
        ShowsJobArchive && Job is { IsArchived: true } && _store is IHandoverCompletionStore;

    public bool CanReviewArchivedOutputs => ShowsArchivedOutputReview && CanCloseJob;

    public Task ReviewArchivedOutputsAsync()
    {
        if (!CanReviewArchivedOutputs || Job is not { Checkpoints.Count: > 0 } job ||
            _store is not IHandoverCompletionStore store)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            Status = await Task.Run(async () =>
            {
                var files = await store.ReadArchivedHandoverFilesAsync(job.Job.JobId, job.Checkpoints[^1].CheckpointId);
                if (files is null)
                {
                    return "The archived job has changed. Reopen it before reviewing its saved copies.";
                }
                if (files.Count == 0)
                {
                    return "This archived job has no recorded local report or guide copies. Earlier unrecorded copies may remain.";
                }

                long remainingBytes = 2 * WorkshopReportStore.MaximumRetainedBytes;
                int checkedCopies = 0;
                int unverifiedCopies = 0;
                foreach (var copies in files.GroupBy(file => (file.Output.Kind, file.FileName)))
                {
                    var first = copies.First();
                    if (copies.Any(file => file.Output.Sha256 != first.Output.Sha256 || file.ByteLength != first.ByteLength) ||
                        first.ByteLength > remainingBytes)
                    {
                        unverifiedCopies++;
                        continue;
                    }

                    remainingBytes -= first.ByteLength;
                    if (((IHandoverFileVerifier)this).MatchesPublishedFile(first, CancellationToken.None))
                    {
                        checkedCopies++;
                    }
                    else
                    {
                        unverifiedCopies++;
                    }
                }

                return $"Local report and guide copies that match this job's records: {checkedCopies}. " +
                    $"Recorded copies that could not be verified: {unverifiedCopies}. " +
                    "Earlier unrecorded copies may remain. Exported copies and backup samples are outside this review.";
            });
        }, refreshFinalization: false);
    }

    private async Task<HandoverReviewScope?> ReadPublicationScopeAsync(JobId jobId, CancellationToken token)
    {
        if (IsFixtureScenario || Job is not { IsArchived: false } || _store is not IHandoverCompletionStore store)
        {
            return null;
        }
        var review = await Task.Run(() => store.LoadHandoverReviewAsync(jobId, token), token);
        return review?.Scope is { Origin: HandoverEvidenceOrigin.ActiveDevice } scope ? scope : null;
    }

    private async Task<bool> RecordPublishedHandoverFileAsync(HandoverReviewScope? scope, HandoverOutputKind kind,
        WorkshopReportArtifact artifact, CancellationToken token)
    {
        if (scope is null || _store is not IHandoverCompletionStore store) { return true; }
        var receipt = artifact.Receipt;
        var reference = new HandoverOutputReference(kind, scope, receipt.FileName, receipt.Sha256);
        try
        {
            await Task.Run(() => store.SaveHandoverFileAsync(new(reference, receipt.FileName, receipt.ByteLength), this, token), token);
            return true;
        }
        catch (JobStoreException)
        {
            return false;
        }
    }

    bool IHandoverFileVerifier.MatchesPublishedFile(HandoverFileReceipt input, CancellationToken token)
    {
        var receipt = new WorkshopReportReceipt(input.FileName, input.ByteLength, input.Output.Sha256);
        try
        {
            if (input.Output.Kind == HandoverOutputKind.WorkshopRecord && _reportStore is not null)
            {
                _reportStore.VerifyReceipt(receipt, token);
                return true;
            }
            if (input.Output.Kind == HandoverOutputKind.RecipientGuide && _recipientGuideStore is not null)
            {
                _recipientGuideStore.VerifyReceipt(receipt, token);
                return true;
            }
            return false;
        }
        catch (WorkshopReportStoreException) { return false; }
    }

    private static string? DescribeHandoverOutput(HandoverRequirementEvidence evidence)
    {
        if (evidence.SupportBundleOmission is { } omission)
        {
            var reason = omission.Reason == SupportBundleOmissionReason.PrivacyChoice ? "Privacy choice" : "No support request";
            return $"Support archive omitted. {reason}. Recorded by {omission.OperatorId.Value}.";
        }

        return evidence.Output is { } output
            ? $"{DescribeRequirement(evidence.Requirement)}. {output.Version}. SHA-256 {output.Sha256}."
            : null;
    }

    private static string DescribeRequirement(HandoverRequirement requirement) => requirement switch
    {
        HandoverRequirement.OpenJob => "Open job",
        HandoverRequirement.SupportedPlatform => "Supported Windows platform",
        HandoverRequirement.Sanitization => "Verified sanitization",
        HandoverRequirement.Ownership => "Resolved ownership",
        HandoverRequirement.PolicyDisposition => "Current policy disposition",
        HandoverRequirement.AuthorizedExceptions => "Policy-authorized exceptions",
        HandoverRequirement.EssentialActionVerification => "Essential actions",
        HandoverRequirement.FinalProfileVerification => "Independent profile verification",
        HandoverRequirement.ColdBootVerification => "Post-cold-boot verification",
        HandoverRequirement.PendingRestart => "Restart and update recovery",
        HandoverRequirement.Finalization => "Finalization checklist",
        HandoverRequirement.RecipientHandoverMode => "Recipient handover mode",
        HandoverRequirement.WorkshopRecord => "Current workshop report",
        HandoverRequirement.RecipientGuide => "Current recipient guide",
        HandoverRequirement.SupportBundleDecision => "Support export or recorded omission",
        _ => "Handover evidence",
    };

    private static string DescribeHandoverBlocker(HandoverBlocker blocker)
    {
        var status = blocker.Reason switch
        {
            HandoverBlockReason.JobArchived => "job is archived",
            HandoverBlockReason.EvidenceConflict or HandoverBlockReason.ScopeMismatch => "evidence conflicts",
            HandoverBlockReason.EvidenceStale => "review again after the change",
            HandoverBlockReason.CheckFailed => "check failed",
            HandoverBlockReason.AuthorizationMissing => "authorization is required",
            HandoverBlockReason.PolicyRequiresReview => "policy review required",
            HandoverBlockReason.FinalizationIncomplete => "required checks remain open",
            HandoverBlockReason.OutputUnbound => "save the exact current output",
            _ => "current evidence is unavailable",
        };
        return $"{DescribeRequirement(blocker.Requirement)}. {status}.";
    }
}
