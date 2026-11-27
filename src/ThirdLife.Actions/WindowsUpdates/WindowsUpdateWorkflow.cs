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
    private readonly IWindowsUpdateDispatchBackend? _backend;

    public WindowsUpdateWorkflow(IWindowsUpdateJournal journal, IWindowsUpdateAgent agent, TimeProvider? clock = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _clock = clock ?? TimeProvider.System;
    }

    // Only the controlled fixture can supply this capability. Ordinary composition exposes scan and review.
    internal WindowsUpdateWorkflow(IWindowsUpdateJournal journal, IWindowsUpdateAgent agent,
        IWindowsUpdateDispatchBackend backend, Func<WindowsUpdateCandidate, CancellationToken, Task<bool>> recheckQualifiedResources,
        TimeProvider clock) : this(journal, agent, clock)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _recheckResources = recheckQualifiedResources ?? throw new ArgumentNullException(nameof(recheckQualifiedResources));
    }

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
        WindowsUpdateVerification? verification = null;
        if (scan is { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false })
        {
            var history = await _journal.LoadWindowsUpdatesAsync(started.JobId, CancellationToken.None).ConfigureAwait(false);
            if (history.LastOrDefault()?.Id == started.Id && WindowsUpdateHistory.PendingVerification(history, scan) is { } receipt)
            {
                var exact = receipt.Approval!.Exact.Identity;
                WindowsUpdateInstalledObservation observed;
                try
                {
                    observed = await _agent.ObserveInstalledAsync(exact, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    observed = new(WindowsUpdateStatus.Cancelled, exact, null);
                }

                if (observed.Identity != exact)
                {
                    observed = new(WindowsUpdateStatus.ApprovalChanged, exact, null);
                }

                verification = new(receipt.Id, observed);
            }
        }

        var completed = new WindowsUpdateEntry(Guid.NewGuid(), started.Id, started.JobId, started.GateId,
            _clock.GetUtcNow(), WindowsUpdateStage.ScanCompleted, scan, null, null, Verification: verification);
        return await _journal.AppendWindowsUpdateAsync(completed, CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task<WindowsUpdateEntry> ApproveAsync(JobId jobId, SanitizationGateDecisionId gateId, Guid displayedScanId,
        WindowsUpdateCandidate exact, OperatorId operatorId, string policyDecisionId, CancellationToken token = default)
    {
        RequireQualifiedRoute();
        var approvalId = Guid.NewGuid();
        var actions = WindowsUpdatePolicy.IsSelectable(exact) ? WindowsUpdateApprovedActions.Create(approvalId, exact) : null;
        var scope = new WindowsUpdateApproval(displayedScanId, exact, operatorId, policyDecisionId, actions);
        return await _journal.AppendWindowsUpdateAsync(new WindowsUpdateEntry(approvalId, displayedScanId, jobId, gateId,
            _clock.GetUtcNow(), WindowsUpdateStage.Approved, null, scope, null), token).ConfigureAwait(false);
    }

    internal async Task<WindowsUpdateEntry> ExecuteAsync(JobId jobId, Guid approvalId, CancellationToken token = default)
    {
        RequireQualifiedRoute();
        var history = await _journal.LoadWindowsUpdatesAsync(jobId, token).ConfigureAwait(false);
        if (history.LastOrDefault() is not { Stage: WindowsUpdateStage.Approved, Approval: not null } approval ||
            approval.Id != approvalId || approval.Approval.Actions is not { } actions ||
            actions.ApprovalId != approvalId || !actions.MatchesCurrentActions(approval.Approval.Exact))
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

        var actions = prior.Approval.Actions!;
        var phase = download ? actions.Download : actions.Install;
        var preparation = await AppendAsync(check, download ? WindowsUpdateStage.DownloadPreparing : WindowsUpdateStage.InstallPreparing,
            token: token).ConfigureAwait(false);
        var dispatch = new WindowsUpdateDispatch(preparation.JobId, preparation.GateId, prior.Approval.ScanId, prior.Approval.PolicyDecisionId,
            actions.ApprovalId, exact, phase, preparation.OccurredAtUtc + WindowsUpdateHistory.DispatchWindow);
        var (started, outcome) = await DispatchAsync(preparation, dispatch, token).ConfigureAwait(false);
        var stage = started is null ? WindowsUpdateStage.DispatchNotSent :
            outcome is null ? WindowsUpdateStage.DispatchUnconfirmed :
                download ? WindowsUpdateStage.DownloadCompleted : WindowsUpdateStage.InstallCompleted;
        return await AppendAsync(started ?? preparation, stage, operation: outcome ?? new(WindowsUpdateStatus.Partial, null),
            token: CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<(WindowsUpdateEntry? Started, WindowsUpdateOperation? Outcome)> DispatchAsync(
        WindowsUpdateEntry preparation, WindowsUpdateDispatch dispatch, CancellationToken token)
    {
        // The preparing entry grants no send. The store claims the action ID once at handoff after
        // checking the latest predecessor, job directory, intake, policy and original phase scope.
        using var deadline = new CancellationTokenSource(WindowsUpdateHistory.DispatchWindow, _clock);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        WindowsUpdateEntry? started = null;
        try
        {
            waiting.Token.ThrowIfCancellationRequested();
            await using var channel = await _backend!.PrepareAsync(dispatch, waiting.Token).ConfigureAwait(false);
            waiting.Token.ThrowIfCancellationRequested();
            if (channel is null) { return (null, new(WindowsUpdateStatus.Unavailable, null)); }
            if (channel.Dispatch != dispatch) { return (null, new(WindowsUpdateStatus.ApprovalChanged, null)); }

            var offered = await ObserveAsync(waiting.Token).ConfigureAwait(false);
            waiting.Token.ThrowIfCancellationRequested();
            if (offered.Status != WindowsUpdateStatus.Succeeded) { return (null, new(offered.Status, offered.RebootRequired)); }
            if (!WindowsUpdatePolicy.Matches(dispatch.Exact, offered)) { return (null, new(WindowsUpdateStatus.ApprovalChanged, offered.RebootRequired)); }
            var resources = await _recheckResources!(dispatch.Exact, waiting.Token).ConfigureAwait(false);
            waiting.Token.ThrowIfCancellationRequested();
            if (!resources) { return (null, new(WindowsUpdateStatus.Unavailable, null)); }
            var now = _clock.GetUtcNow();
            if (now < preparation.OccurredAtUtc || now >= dispatch.ExpiresAtUtc) { return (null, new(WindowsUpdateStatus.TimedOut, null)); }
            var startStage = preparation.Stage == WindowsUpdateStage.DownloadPreparing ? WindowsUpdateStage.DownloadStarted : WindowsUpdateStage.InstallStarted;
            var intent = new WindowsUpdateEntry(dispatch.Action.ActionId, preparation.Id, preparation.JobId, preparation.GateId, now,
                startStage, null, preparation.Approval, null);
            started = await _journal.TryStartWindowsUpdateDispatchAsync(intent, waiting.Token).ConfigureAwait(false);
            if (started is null)
            {
                waiting.Token.ThrowIfCancellationRequested();
                return (null, new(WindowsUpdateStatus.ApprovalChanged, null));
            }
            var receipt = await channel.SendAsync(token).ConfigureAwait(false);
            return (started, receipt?.Dispatch == dispatch ? receipt.Outcome : null);
        }
        catch (OperationCanceledException)
        {
            // Losing a call after handoff cannot establish that Windows cancelled the action.
            return (started, started is not null ? null :
                new(token.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.TimedOut, null));
        }
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
