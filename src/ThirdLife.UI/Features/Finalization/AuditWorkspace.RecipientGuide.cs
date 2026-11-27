using System.Collections.Immutable;
using System.IO;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.Reports.RecipientGuide;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.Verification;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private const string RecipientGuideLocation = "%LOCALAPPDATA%\\ThirdLife\\SetupCore\\RecipientGuides";
    private readonly RecipientGuideStore? _recipientGuideStore;
    private FrozenRecipientGuide? _recipientGuide;

    public string RecipientGuideStatus { get; private set; } = "Open a job and choose its handover mode to preview a guide.";
    public string RecipientGuidePreview => _recipientGuide?.Content.PreviewText ?? string.Empty;
    public bool CanPreviewRecipientGuide => CanCloseJob && !_hasSeparateEvidenceOrigin && !_readingFinalization &&
        RecipientHandover is { Handover: not null };
    public bool CanSaveRecipientGuide => CanPreviewRecipientGuide && _recipientGuideStore is not null &&
        _recipientGuide is { } preview && preview.JobId == Job?.Job.JobId &&
        preview.HandoverSequence == RecipientHandover?.Sequence;

    public Task PreviewRecipientGuideAsync()
    {
        if (!CanPreviewRecipientGuide || Job is not { } job) { return Task.CompletedTask; }
        return RunAsync(async () =>
        {
            _recipientGuide = null;
            RecipientGuideStatus = "Preparing the preview from the saved job.";
            Publish();
            try
            {
                _recipientGuide = await BuildRecipientGuideAsync(job);
                RecipientGuideStatus = _recipientGuide is null ? "The saved handover mode is unavailable. Review the job before previewing."
                    : "Review the guide before saving a printable copy for the recipient.";
            }
            catch (Exception exception) when (exception is JobStoreException or ArgumentException)
            {
                RecipientGuideStatus = "The saved job could not provide a guide preview. Reopen the job and try again.";
            }
        });
    }

    public Task SavePrintableRecipientGuideAsync()
    {
        if (!CanSaveRecipientGuide || Job is not { } job || _recipientGuide is not { } expected)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            RecipientGuideStatus = "Checking the saved facts before saving the preview.";
            Publish();
            try
            {
                var current = await BuildRecipientGuideAsync(job);
                if (current is null || current.HandoverSequence != expected.HandoverSequence ||
                    !string.Equals(current.Content.PrintableHtml, expected.Content.PrintableHtml, StringComparison.Ordinal))
                {
                    _recipientGuide = null;
                    RecipientGuideStatus = "The job changed. Preview the guide again before saving.";
                    return;
                }

                var name = await Task.Run(() => _recipientGuideStore!.Save(job.Job.JobId, expected.Content));
                RecipientGuideStatus = $"Printable guide saved as {name} in {RecipientGuideLocation}.";
            }
            catch (Exception exception) when (exception is JobStoreException or WorkshopReportStoreException or ArgumentException)
            {
                RecipientGuideStatus = "The printable guide could not be saved. Previously saved files are available in the guide folder.";
            }
        });
    }

    private async Task<FrozenRecipientGuide?> BuildRecipientGuideAsync(StoredJob job)
    {
        var handover = await ReadRecipientHandoverAsync(job);
        if (handover?.Handover is not { } mode) { return null; }
        var verification = await ReadProfileVerificationForReportAsync(job.Job.JobId, CancellationToken.None);
        var journals = verification is null ? ImmutableArray<JournalHistory>.Empty
            : await ReadPreparationJournalsAsync(job.Job.JobId, CancellationToken.None);
        var entries = ProjectRecipientGuideSoftware(verification, journals);
        var verifiedOn = verification is { IsCurrent: true }
            ? DateOnly.FromDateTime(verification.Record.Result.FinishedAtUtc.UtcDateTime) : (DateOnly?)null;
        var content = new RecipientGuideContent(mode, verifiedOn, entries,
            SyntheticExample: IsFixtureScenario || verification?.Record.Result.SyntheticData == true);
        return new(job.Job.JobId, handover.Sequence, RecipientGuideRenderer.Render(content));
    }

    private static RecipientGuideSoftware[] ProjectRecipientGuideSoftware(SavedProfileVerification? verification,
        ImmutableArray<JournalHistory> journals)
    {
        if (verification is null) { return []; }
        var history = journals.SingleOrDefault(candidate => candidate.Plan.Key == verification.Record.Checkpoint.Plan);
        var approvedProgram = history is null ? null : PackageHistoryRequest(history);
        return verification.Record.Result.Capabilities
            .Where(result => result.State != ProfileVerificationState.NotApplicable)
            .Select(result => ProjectRecipientCapability(result, verification.IsCurrent, approvedProgram))
            .OfType<RecipientGuideSoftware>()
            .ToArray();
    }

    private static RecipientGuideSoftware? ProjectRecipientCapability(ProfileCapabilityResult result, bool current,
        InstallPackageActionRequest? approvedProgram)
    {
        var capability = result.CapabilityId switch
        {
            "web_browsing" => RecipientGuideCapability.WebBrowsing,
            "document_editing" => RecipientGuideCapability.DocumentEditing,
            "pdf_reading" => RecipientGuideCapability.PdfReading,
            "video_calling" => RecipientGuideCapability.VideoCalling,
            _ => (RecipientGuideCapability?)null,
        };
        if (capability is not { } kind) { return null; }
        if (!current || result is not { State: ProfileVerificationState.Passed, PackageStatus: ProfilePackageStatus.ExactVersion })
        {
            return new(kind, RecipientGuideSoftwareState.AwaitingVerification);
        }

        return approvedProgram is not null && kind == RecipientGuideCapability.DocumentEditing &&
            result.ActionId == approvedProgram.ActionId && result.ObservedVersion == approvedProgram.ExactVersion
            ? new(kind, RecipientGuideSoftwareState.VerifiedInstalled, RecipientGuideProgram.NotepadPlusPlus, result.ObservedVersion)
            : new(kind, RecipientGuideSoftwareState.VerifiedInstalled);
    }

    private void ClearRecipientGuide(string status = "Open a job and choose its handover mode to preview a guide.")
    {
        _recipientGuide = null;
        RecipientGuideStatus = status;
    }

    private static RecipientGuideStore? RegisteredRecipientGuideStore()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.IsPathFullyQualified(localData)
            ? new RecipientGuideStore(Path.Combine(localData, "ThirdLife", "SetupCore", "RecipientGuides")) : null;
    }

    private sealed record FrozenRecipientGuide(JobId JobId, long HandoverSequence, RenderedRecipientGuide Content);
}
