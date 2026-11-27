using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Persistence;

namespace ThirdLife.UI.Shell;

internal sealed record WindowsUpdateOffer(Guid ScanId, WindowsUpdateCandidate Exact, string Label);

internal sealed partial class AuditWorkspace
{
    private WindowsUpdateOffer? _selectedUpdateOffer;
    public IReadOnlyList<WindowsUpdateOffer> WindowsUpdateOffers { get; private set; } = [];
    public bool HasWindowsUpdateOffers => ShowsWindowsUpdates && Job is { IsArchived: false } &&
        WindowsUpdateOffers.Count > 0 && Gate is { AllowsAssessment: true } gate && gate.Decision!.DecisionId == _updateHistory[^1].GateId;

    public WindowsUpdateOffer? SelectedUpdateOffer
    {
        get => _selectedUpdateOffer;
        set
        {
            if (_selectedUpdateOffer == value || (value is not null && !WindowsUpdateOffers.Contains(value))) { return; }
            _selectedUpdateOffer = value;
            WindowsUpdatePreview = string.Empty;
            Publish();
        }
    }

    public bool CanCheckUpdateOffer => CanAssess && HasWindowsUpdateOffers && _selectedUpdateOffer is not null;
    public string WindowsUpdatePreview { get; private set; } = string.Empty;
    public bool HasWindowsUpdatePreview => ShowsWindowsUpdates && WindowsUpdatePreview.Length != 0 &&
        !Job!.IsArchived && Gate is { AllowsAssessment: true } gate && _updateHistory.LastOrDefault()?.GateId == gate.Decision!.DecisionId;

    public Task CheckUpdateOfferAsync()
    {
        if (!CanCheckUpdateOffer || _selectedUpdateOffer is not { } offer || UpdateAgent is not { } agent ||
            _store is not IWindowsUpdateJournal journal || Job is not { } job || Gate?.Decision is not { } gate)
        {
            return Task.CompletedTask;
        }

        var store = _store;
        return RunAsync(async () =>
        {
            WindowsUpdatePreview = string.Empty;
            _checkingUpdateOffer = true;
            _updateCancellation = new CancellationTokenSource();
            var token = _updateCancellation.Token;
            Publish();
            try
            {
                var result = await Task.Run(async () =>
                {
                    if (!await IsCurrentOfferAsync(store, journal, job.Job.JobId, gate.DecisionId, offer, token)) { return null; }
                    var check = await agent.AssessPreparationAsync(offer.Exact, token);
                    token.ThrowIfCancellationRequested();
                    return await IsCurrentOfferAsync(store, journal, job.Job.JobId, gate.DecisionId, offer, token) ? check : null;
                }, token);
                if (result is null)
                {
                    DiscardStaleOffers();
                }
                else if (_selectedUpdateOffer == offer)
                {
                    WindowsUpdatePreview = DescribeCheckedOffer(offer, result);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                WindowsUpdatePreview = "Offer check cancelled. You can check the offer again.";
            }
            catch (Exception error) when (error is JobLifecycleException or JobStoreException)
            {
                DiscardStaleOffers();
            }
            finally { _checkingUpdateOffer = false; }
        });
    }

    private static async Task<bool> IsCurrentOfferAsync(IJobStore store, IWindowsUpdateJournal journal, JobId jobId,
        SanitizationGateDecisionId gateId, WindowsUpdateOffer offer, CancellationToken token)
    {
        await store.RequireSanitizationGateAsync(jobId, gateId, token).ConfigureAwait(false);
        var history = await journal.LoadWindowsUpdatesAsync(jobId, token).ConfigureAwait(false);
        return history.LastOrDefault() is { Stage: WindowsUpdateStage.ScanCompleted, Scan.Status: WindowsUpdateStatus.Succeeded } saved &&
            saved.JobId == jobId && saved.GateId == gateId && saved.Id == offer.ScanId && saved.Scan.Updates.Contains(offer.Exact);
    }

    private void DiscardStaleOffers()
    {
        ClearWindowsUpdateOffers();
        WindowsUpdatePreview = "The saved scan or intake could not be confirmed. Reopen this job to review its latest scan.";
    }

    private void ClearWindowsUpdateOffers()
    {
        WindowsUpdateOffers = [];
        _selectedUpdateOffer = null;
        WindowsUpdatePreview = string.Empty;
    }

    private string DescribeCheckedOffer(WindowsUpdateOffer offer, WindowsUpdatePreparation result)
    {
        var header = $"{offer.Label}{Environment.NewLine}";
        if (result.Exact != offer.Exact || result.Status == WindowsUpdateStatus.ApprovalChanged)
        {
            return header + "The exact offer changed or is no longer available. Scan again to review the current offers.";
        }

        if (result.Status != WindowsUpdateStatus.Succeeded)
        {
            var reason = result.Status switch
            {
                WindowsUpdateStatus.Cancelled => "The offer check was cancelled.",
                WindowsUpdateStatus.TimedOut => "The offer check timed out.",
                WindowsUpdateStatus.Offline => "Windows Update is offline. Check the network connection.",
                WindowsUpdateStatus.AccessDenied => "Windows denied access to the update service.",
                WindowsUpdateStatus.Partial => "The update service returned an incomplete result.",
                _ => "The configured Windows Update service could not confirm preparation details.",
            };
            return header + reason + " Check the offer again after reviewing the service.";
        }

        var details = new List<string>
        {
            offer.Label,
            $"{(IsFixtureScenario ? "Synthetic offer check" : "Read-only offer check")} at {result.ObservedAtUtc:u}. {DescribeEligibility(result.EligibilityReason)}",
            result.RebootRequired switch { true => "Windows reports a pending restart.", false => "Windows reports no pending restart.", _ => "Restart state is unknown." },
            result.ServicingSupported switch { true => "This Windows release is supported for servicing.", false => "This Windows installation is outside the supported servicing scope.", _ => "Windows servicing support is unknown." },
            result.PowerReady switch { true => "Power check passed.", false => "Connect reliable external power and check again.", _ => "Power state is unknown." },
            DescribeSpace(result),
        };
        if (WindowsUpdatePolicy.IsSelectable(offer.Exact) && result.EligibilityReason == WindowsUpdateEligibilityReason.Eligible)
        {
            details.Add(DescribeServicingImpact(offer.Exact));
            details.Add("Servicing requires a qualified rollback reserve, update policy, broker and exact approval.");
        }
        return string.Join(Environment.NewLine, details);
    }

    private static string DescribeSpace(WindowsUpdatePreparation result)
    {
        static string Bytes(ulong? bytes) => bytes is { } known ? $"{known:N0} bytes" : "unknown";
        var recommendation = result.RecommendedInstallationBytes is { } recommended ? $"{recommended:N0} bytes" : "unknown";
        var check = result.HasRecommendedSpace switch
        {
            true => "Meets the Windows Update space recommendation.",
            false => "Insufficient space for the Windows Update recommendation.",
            _ => "The Windows Update space requirement could not be confirmed.",
        };
        return $"System space available {Bytes(result.SystemAvailableBytes)}. Update cache space available {Bytes(result.CacheAvailableBytes)}. " +
            $"Recommended installation space {recommendation}. {check}";
    }

    private static string DescribeServicingImpact(WindowsUpdateCandidate exact)
    {
        var download = ActionRegistry.Describe(ActionRegistry.CreateDownloadWindowsUpdate(Guid.NewGuid(), exact));
        var install = ActionRegistry.Describe(ActionRegistry.CreateInstallWindowsUpdate(Guid.NewGuid(), exact));
        if (download is
            {
                Scope: ActionScope.Machine, Privilege: ActionPrivilege.Elevated, NetworkUse: ActionNetworkUse.WindowsUpdate,
                Verification: ActionVerification.ExactDownloadedUpdateRevision
            } &&
            install is
            {
                Scope: ActionScope.Machine, Privilege: ActionPrivilege.Elevated, Reboot: ActionReboot.MayRequire,
                Rollback: ActionRollback.ManualReview, Verification: ActionVerification.ExactInstalledUpdateRevision
            })
        {
            return "The download would transfer this revision from Windows Update to the machine cache and verify the downloaded revision. " +
                "Installation would change Windows system components. Both actions require elevation. A restart may be required. " +
                "Rollback requires workshop review. Verification uses a fresh scan and an independent check of the installed revision.";
        }
        return "The compiled servicing impact could not be confirmed.";
    }
}
