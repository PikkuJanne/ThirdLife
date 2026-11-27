using System.Collections.Immutable;
using System.Globalization;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Persistence;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private IWindowsUpdateAgent? UpdateAgent { get; init; }
    private CancellationTokenSource? _updateCancellation;
    private ImmutableArray<WindowsUpdateEntry> _updateHistory = [];
    private bool _updateHistoryUnavailable;
    private bool _checkingUpdateOffer;

    public bool ShowsWindowsUpdates => UpdateAgent is not null && Job is not null && !_hasSeparateEvidenceOrigin;
    public bool HasWindowsUpdateHistory => ShowsWindowsUpdates && (!_updateHistory.IsEmpty || _updateHistoryUnavailable);
    public bool CanScanWindowsUpdates => ShowsWindowsUpdates && CanAssess && _store is IWindowsUpdateJournal &&
        !_updateHistoryUnavailable && _updateHistory.Length <= WindowsUpdateHistory.MaximumEntries - 2;
    public bool CanCancelWindowsUpdate => _updateCancellation is { IsCancellationRequested: false };
    public bool IsWindowsUpdateRunning => _updateCancellation is not null;
    public string WindowsUpdateCancelLabel => _checkingUpdateOffer ? "Cancel offer check" : "Cancel update scan";

    public string WindowsUpdateStatusText
    {
        get
        {
            if (_updateCancellation is not null)
            {
                if (_checkingUpdateOffer)
                {
                    return _updateCancellation.IsCancellationRequested ? "Stopping the offer check." :
                        "Checking the selected update and current resources. You can cancel this check.";
                }
                return _updateCancellation.IsCancellationRequested ? "Stopping the scan and saving the result." :
                    "Searching the configured Windows Update service. You can cancel this scan.";
            }

            if (_updateHistoryUnavailable) { return "Update history could not be read. Reopen the job to try again."; }
            if (_updateHistory.IsEmpty)
            {
                return Gate is { AllowsAssessment: true } ? "No update scan has been saved for this job." : GateStatus;
            }

            var latest = _updateHistory[^1];
            var when = latest.OccurredAtUtc.ToString("u", CultureInfo.CurrentCulture);
            string outcome;
            if (latest.GateId != Gate?.Decision?.DecisionId)
            {
                outcome = "Saved under an earlier intake decision. Scan again after completing the current intake.";
            }
            else if (latest.Stage == WindowsUpdateStage.ScanStarted)
            {
                outcome = "The scan has no saved result. Run a fresh scan.";
            }
            else if (latest.Scan is { Status: not WindowsUpdateStatus.Succeeded } scan)
            {
                outcome = DescribeScanProblem(scan.Status);
            }
            else
            {
                outcome = WindowsUpdateHistory.Disposition(_updateHistory) switch
                {
                    WindowsUpdateDisposition.Current => "The configured service offered no applicable, nonhidden updates. Windows reported no pending restart.",
                    WindowsUpdateDisposition.RestartRequired => "Windows reported a required restart. Rescan after the next confirmed restart.",
                    WindowsUpdateDisposition.ApprovalRequired => "The configured service offered applicable updates. Review the saved inventory in Prepare.",
                    WindowsUpdateDisposition.PassLimitReached => "The three-pass update limit was reached. Review remaining updates.",
                    WindowsUpdateDisposition.ReviewRequired => "Update evidence needs workshop review before further preparation.",
                    _ => "Update currency is unknown. Run a fresh scan and review the saved evidence.",
                };
            }

            var capacity = _updateHistory.Length > WindowsUpdateHistory.MaximumEntries - 2
                ? " This job's update journal is full. Its saved history remains available for review." : string.Empty;
            return $"{when} · {outcome}{capacity}";
        }
    }

    public string WindowsUpdateInventory { get; private set; } = string.Empty;
    public bool HasWindowsUpdateInventory => WindowsUpdateInventory.Length != 0;

    public string WindowsUpdateVerificationText
    {
        get
        {
            var last = _updateHistory.LastOrDefault(entry => entry.Verification is not null);
            if (last?.Verification?.Observation is not { } check) { return string.Empty; }
            var observation = check is { Status: WindowsUpdateStatus.Succeeded, Installed: true }
                ? "Windows reported this exact revision installed."
                : "Windows could not confirm installation of this exact revision.";
            return $"Installed-state check at {last.OccurredAtUtc:u} for {check.Identity.UpdateId:D}, revision {check.Identity.Revision}. {observation}";
        }
    }

    public bool HasWindowsUpdateVerification => _updateHistory.Any(entry => entry.Verification is not null);

    public Task ScanWindowsUpdatesAsync()
    {
        if (!CanScanWindowsUpdates || UpdateAgent is not { } agent || _store is not IWindowsUpdateJournal journal ||
            Gate?.Decision is not { } decision || Job is not { } job)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            WindowsUpdatePreview = string.Empty;
            _updateCancellation = new CancellationTokenSource();
            var token = _updateCancellation.Token;
            Publish();
            try
            {
                var workflow = new WindowsUpdateWorkflow(journal, agent, _timeProvider);
                await Task.Run(() => workflow.ScanAsync(job.Job.JobId, decision.DecisionId, token));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Status = "The update scan was cancelled.";
            }
            finally
            {
                await LoadWindowsUpdateHistoryAsync(job.Job.JobId);
            }
        });
    }

    public void CancelWindowsUpdate()
    {
        if (!CanCancelWindowsUpdate) { return; }
        _cancellationRequest = _updateCancellation!.CancelAsync();
        Publish();
    }

    private async Task LoadWindowsUpdateHistoryAsync(JobId jobId)
    {
        ClearWindowsUpdateHistory();
        if (!ShowsWindowsUpdates || _store is not IWindowsUpdateJournal journal) { return; }
        try
        {
            _updateHistory = await Task.Run(() => journal.LoadWindowsUpdatesAsync(jobId));
            var lastInventory = _updateHistory.LastOrDefault(entry => entry.Scan is { Status: WindowsUpdateStatus.Succeeded });
            if (lastInventory?.Scan is not { } scan) { return; }
            var rows = scan.Updates.Select(DescribeUpdate);
            WindowsUpdateInventory = $"Inventory saved at {lastInventory.OccurredAtUtc:u} with {scan.Updates.Count} applicable, nonhidden offers." +
                (scan.Updates.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, rows));
            if (lastInventory.Stage == WindowsUpdateStage.ScanCompleted && lastInventory.Id == _updateHistory[^1].Id)
            {
                WindowsUpdateOffers = scan.Updates.Select(update =>
                    new WindowsUpdateOffer(lastInventory.Id, update, DescribeUpdate(update))).ToArray();
                _selectedUpdateOffer = WindowsUpdateOffers.Count > 0 ? WindowsUpdateOffers[0] : null;
            }
        }
        catch (JobStoreException)
        {
            _updateHistoryUnavailable = true;
        }
    }

    private static string DescribeUpdate(WindowsUpdateCandidate update)
    {
        var classification = update.Classification switch
        {
            WindowsUpdateClass.Security => "Security update",
            WindowsUpdateClass.Critical => "Critical update",
            WindowsUpdateClass.Driver => "Driver update · outside the update policy",
            WindowsUpdateClass.Restricted => "Restricted update · workshop review required",
            _ => "Other classification · outside the update policy",
        };
        var size = update.MaximumDownloadBytes is { } bytes ? $"up to {bytes:N0} bytes" : "download size unknown";
        return $"{classification} · {update.Identity.UpdateId:D} · revision {update.Identity.Revision} · {size}";
    }

    private void ClearWindowsUpdateHistory()
    {
        _updateHistory = [];
        _updateHistoryUnavailable = false;
        WindowsUpdateInventory = string.Empty;
        ClearWindowsUpdateOffers();
    }

    private static string DescribeScanProblem(WindowsUpdateStatus status) => status switch
    {
        WindowsUpdateStatus.Offline => "Update currency is unknown. Check the network connection and scan again.",
        WindowsUpdateStatus.ServiceUnavailable => "The configured update service is unavailable. Check access to the service and scan again.",
        WindowsUpdateStatus.AccessDenied => "Windows denied the scan. Ask the workshop administrator to check update access.",
        WindowsUpdateStatus.TimedOut => "The scan timed out. Check connectivity to the update service and scan again.",
        WindowsUpdateStatus.Cancelled => "Scan cancelled. Run a fresh scan to check update currency.",
        WindowsUpdateStatus.LimitExceeded => "The response exceeded the inspection limit. Review the device's update inventory.",
        WindowsUpdateStatus.Partial => "Windows returned an incomplete scan. Review the update service and scan again.",
        _ => "Windows could not complete the scan. Review the configured update service and try again.",
    };
}
