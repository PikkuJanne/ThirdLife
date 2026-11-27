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
    public string WindowsUpdateCancelLabel => _servicingUpdate ? "Cancel update operation" : _checkingUpdateOffer ? "Cancel offer check" : "Cancel update scan";

    public string WindowsUpdateStatusText
    {
        get
        {
            if (_updateCancellation is not null)
            {
                if (_servicingUpdate)
                {
                    return _updateCancellation.IsCancellationRequested ? "Cancellation requested. Waiting for the servicing outcome." :
                        "Checking approval and servicing this exact revision. Windows may ask for elevation.";
                }
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
            var outcome = latest.GateId != Gate?.Decision?.DecisionId
                ? "Saved under an earlier intake decision. Scan again after completing the current intake."
                : DescribeSavedWindowsUpdate(_updateHistory);

            var capacity = _updateHistory.Length > WindowsUpdateHistory.MaximumEntries - 2
                ? " This job's update journal is full. Its saved history remains available for review." : string.Empty;
            return $"{when} · {outcome}{capacity}";
        }
    }

    internal static string DescribeSavedWindowsUpdate(ImmutableArray<WindowsUpdateEntry> history)
    {
        var latest = history[^1];
        if (latest is { Stage: WindowsUpdateStage.Approved, Approval: { } scope })
        {
            return $"Approval saved for {DescribeUpdate(scope.Exact)}. Review the saved scope before starting servicing.";
        }
        if (latest.Stage == WindowsUpdateStage.ResourcesDeferred)
        {
            return "Resource checks deferred servicing. Review power, storage and recovery requirements before scanning again.";
        }
        if (latest.Stage == WindowsUpdateStage.ScanStarted) { return "The scan has no saved result. Run a fresh scan."; }
        if (latest.Stage == WindowsUpdateStage.BrokerRefused)
        {
            return "The broker confirmed that Windows servicing did not start. Review the authorization and run a fresh scan.";
        }
        if (latest.Stage == WindowsUpdateStage.DispatchUnconfirmed)
        {
            return "ThirdLife handed off the update request. Its outcome is unknown and needs workshop review before further preparation.";
        }
        if (latest.Stage == WindowsUpdateStage.DispatchNotSent)
        {
            var phase = history.LastOrDefault(entry => entry.Id == latest.PreviousId && entry.Approval == latest.Approval);
            var action = phase?.Stage switch
            {
                WindowsUpdateStage.DownloadPreparing => "download",
                WindowsUpdateStage.InstallPreparing => "install",
                _ => null,
            };
            if (action is null) { return "The saved update action needs workshop review."; }
            var reason = latest.Operation?.Status switch
            {
                WindowsUpdateStatus.ApprovalChanged => "The offer or approval changed.",
                WindowsUpdateStatus.Cancelled => "Preparation was cancelled.",
                WindowsUpdateStatus.TimedOut => "The time allowed for preparation expired.",
                WindowsUpdateStatus.ConsentDeclined => "Windows elevation consent was declined.",
                WindowsUpdateStatus.Offline => "Windows Update was offline.",
                WindowsUpdateStatus.AccessDenied => "Windows denied access.",
                WindowsUpdateStatus.ServiceUnavailable or WindowsUpdateStatus.Unavailable => "A required service or resource was unavailable.",
                _ => "Required checks did not complete.",
            };
            var next = latest.Operation?.RebootRequired == true
                ? "Windows reported a pending restart. After the next confirmed restart, rescan and review the offer in Prepare."
                : "Run a fresh scan and review the offer in Prepare.";
            return $"ThirdLife stopped before asking Windows to {action} this revision. {reason} {next}";
        }
        if (latest.Scan is { Status: not WindowsUpdateStatus.Succeeded } scan) { return DescribeScanProblem(scan.Status); }
        return WindowsUpdateHistory.Disposition(history) switch
        {
            WindowsUpdateDisposition.Current => "The configured service offered no applicable, nonhidden updates. Windows reported no pending restart.",
            WindowsUpdateDisposition.RestartRequired => "Windows reported a required restart. Rescan after the next confirmed restart.",
            WindowsUpdateDisposition.ApprovalRequired => "The configured service offered applicable updates. Review the saved inventory in Prepare.",
            WindowsUpdateDisposition.PassLimitReached => "The three-pass update limit was reached. Review remaining updates.",
            WindowsUpdateDisposition.NoEligibleOffers when latest.Scan is { } excluded => DescribeIneligibleInventory(excluded),
            WindowsUpdateDisposition.ReviewRequired => "Update evidence needs workshop review before further preparation.",
            _ => "Update currency is unknown. Run a fresh scan and review the saved evidence.",
        };
    }

    private static string DescribeIneligibleInventory(WindowsUpdateScan scan)
    {
        var count = scan.Updates.Count;
        var offered = count == 1 ? "The configured service offered one applicable revision."
            : $"The configured service offered {count} applicable revisions.";
        var detail = count == 1 && scan.EligibilityReasons is { } reasons
            ? DescribeEligibility(reasons[0]) : "Review the saved inventory in Prepare.";
        return $"{offered} None met the servicing requirements in this scan. {detail}";
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
            _updateRecovery = WindowsUpdateHistory.RecoveryBeforePackages(_updateHistory);
            var lastInventory = _updateHistory.LastOrDefault(entry => entry.Scan is { Status: WindowsUpdateStatus.Succeeded });
            if (lastInventory?.Scan is not { } scan) { return; }
            var rows = scan.Updates.Select((update, index) => scan.EligibilityReasons is { } reasons && update.Classification == WindowsUpdateClass.Restricted
                ? $"{DescribeUpdate(update)}. {DescribeEligibility(reasons[index])}" : DescribeUpdate(update));
            var eligibility = scan.Updates.Count > 0 && !scan.Updates.Any(WindowsUpdatePolicy.IsSelectable)
                ? " None met the servicing requirements in this scan." : string.Empty;
            WindowsUpdateInventory = $"Inventory saved at {lastInventory.OccurredAtUtc:u} with {scan.Updates.Count} applicable, nonhidden offers.{eligibility}" +
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
        finally
        {
            await LoadProfileVerificationAsync(jobId);
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
        _updateRecovery = UpdateRecovery.None;
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
