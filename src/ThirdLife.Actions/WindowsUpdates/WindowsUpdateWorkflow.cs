using ThirdLife.Actions.Registry;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.WindowsUpdates;

public sealed class WindowsUpdateWorkflow
{
    private readonly IWindowsUpdateJournal _journal;
    private readonly IWindowsUpdateAgent _agent;
    private readonly TimeProvider _clock;
    private readonly Func<WindowsUpdateCandidate, CancellationToken, Task<bool>>? _recheckResources;

    public WindowsUpdateWorkflow(IWindowsUpdateJournal journal, IWindowsUpdateAgent agent, TimeProvider? clock = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _clock = clock ?? TimeProvider.System;
    }

    // Only the controlled fixture can supply this capability. Ordinary composition exposes scan and review.
    internal WindowsUpdateWorkflow(IWindowsUpdateJournal journal, IWindowsUpdateAgent agent,
        Func<WindowsUpdateCandidate, CancellationToken, Task<bool>> recheckQualifiedResources, TimeProvider clock)
        : this(journal, agent, clock) =>
        _recheckResources = recheckQualifiedResources ?? throw new ArgumentNullException(nameof(recheckQualifiedResources));

    public async Task<WindowsUpdateEntry> ScanAsync(JobId jobId, SanitizationGateDecisionId gateId, CancellationToken token = default)
    {
        var history = await _journal.LoadWindowsUpdatesAsync(jobId, token).ConfigureAwait(false);
        var request = ActionRegistry.CreateScanWindowsUpdate(Guid.NewGuid());
        var started = await _journal.AppendWindowsUpdateAsync(new WindowsUpdateEntry(request.ActionId,
            history.LastOrDefault()?.Id, jobId, gateId, _clock.GetUtcNow(), WindowsUpdateStage.ScanStarted, null, null, null), token)
            .ConfigureAwait(false);
        return await CompleteScanAsync(started, token).ConfigureAwait(false);
    }

    private async Task<WindowsUpdateEntry> CompleteScanAsync(WindowsUpdateEntry started, CancellationToken token)
    {
        var scan = await ObserveAsync(token).ConfigureAwait(false);
        return await AppendAsync(started, WindowsUpdateStage.ScanCompleted, scan: scan, token: CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task<WindowsUpdateEntry> ApproveAsync(JobId jobId, SanitizationGateDecisionId gateId, Guid displayedScanId,
        WindowsUpdateCandidate exact, OperatorId operatorId, string policyDecisionId, CancellationToken token = default)
    {
        RequireQualifiedRoute();
        var scope = new WindowsUpdateApproval(displayedScanId, exact, operatorId, policyDecisionId);
        return await _journal.AppendWindowsUpdateAsync(new WindowsUpdateEntry(Guid.NewGuid(), displayedScanId, jobId, gateId,
            _clock.GetUtcNow(), WindowsUpdateStage.Approved, null, scope, null), token).ConfigureAwait(false);
    }

    internal async Task<WindowsUpdateEntry> ExecuteAsync(JobId jobId, Guid approvalId, CancellationToken token = default)
    {
        RequireQualifiedRoute();
        var history = await _journal.LoadWindowsUpdatesAsync(jobId, token).ConfigureAwait(false);
        if (history.LastOrDefault() is not { Stage: WindowsUpdateStage.Approved, Approval: not null } approval ||
            approval.Id != approvalId)
        {
            throw new InvalidOperationException("A fresh, exact Windows Update approval is required.");
        }

        var download = await RunPhaseAsync(approval, download: true, token).ConfigureAwait(false);
        if (download is not { Stage: WindowsUpdateStage.DownloadCompleted, Operation: { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false } })
        {
            return download;
        }

        var install = await RunPhaseAsync(download, download: false, token).ConfigureAwait(false);
        if (install is not { Stage: WindowsUpdateStage.InstallCompleted, Operation: { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false } })
        {
            return install;
        }

        // Bind the rescan to this outcome. A concurrently opened workflow cannot acquire its approval.
        var request = ActionRegistry.CreateScanWindowsUpdate(Guid.NewGuid());
        var started = await _journal.AppendWindowsUpdateAsync(new WindowsUpdateEntry(request.ActionId, install.Id,
            jobId, approval.GateId, _clock.GetUtcNow(), WindowsUpdateStage.ScanStarted, null, null, null), token).ConfigureAwait(false);
        return await CompleteScanAsync(started, token).ConfigureAwait(false);
    }

    private async Task<WindowsUpdateEntry> RunPhaseAsync(WindowsUpdateEntry prior, bool download, CancellationToken token)
    {
        var exact = prior.Approval!.Exact;
        var fresh = await ObserveAsync(token).ConfigureAwait(false);
        var check = await AppendAsync(prior, download ? WindowsUpdateStage.DownloadChecked : WindowsUpdateStage.InstallChecked,
            scan: fresh, token: CancellationToken.None).ConfigureAwait(false);
        if (!WindowsUpdatePolicy.Matches(exact, fresh))
        {
            return check;
        }

        if (!await _recheckResources!(exact, token).ConfigureAwait(false))
        {
            return await AppendAsync(check, WindowsUpdateStage.ResourcesDeferred, token: CancellationToken.None).ConfigureAwait(false);
        }

        var request = download ? ActionRegistry.CreateDownloadWindowsUpdate(Guid.NewGuid(), exact)
            : ActionRegistry.CreateInstallWindowsUpdate(Guid.NewGuid(), exact);
        var intent = await AppendAsync(check, download ? WindowsUpdateStage.DownloadStarted : WindowsUpdateStage.InstallStarted,
            id: request.ActionId, token: token).ConfigureAwait(false);
        WindowsUpdateOperation outcome;
        try
        {
            token.ThrowIfCancellationRequested();
            outcome = download
                ? await _agent.DownloadAsync(exact, token).ConfigureAwait(false)
                : await _agent.InstallAsync(exact, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = new WindowsUpdateOperation(WindowsUpdateStatus.Cancelled, null);
        }

        return await AppendAsync(intent, download ? WindowsUpdateStage.DownloadCompleted : WindowsUpdateStage.InstallCompleted,
            operation: outcome, token: CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<WindowsUpdateScan> ObserveAsync(CancellationToken token)
    {
        try
        {
            return await _agent.ScanAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new WindowsUpdateScan(WindowsUpdateStatus.Cancelled, [], null);
        }
    }

    private Task<WindowsUpdateEntry> AppendAsync(WindowsUpdateEntry prior, WindowsUpdateStage stage,
        WindowsUpdateScan? scan = null, WindowsUpdateOperation? operation = null, Guid? id = null, CancellationToken token = default) =>
        _journal.AppendWindowsUpdateAsync(new WindowsUpdateEntry(id ?? Guid.NewGuid(), prior.Id, prior.JobId, prior.GateId,
            _clock.GetUtcNow(), stage, scan, prior.Approval, operation), token);

    private void RequireQualifiedRoute()
    {
        if (_recheckResources is null)
        {
            throw new InvalidOperationException("A qualified Windows Update mutation route and resource check are required.");
        }
    }
}
