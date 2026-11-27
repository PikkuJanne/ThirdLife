using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.WindowsUpdates;

public sealed record WindowsUpdatePhaseAction(Guid ActionId, ActionDefinition Impact);

public sealed record WindowsUpdateApprovedActions(Guid ApprovalId, WindowsUpdatePhaseAction Download, WindowsUpdatePhaseAction Install)
{
    internal static WindowsUpdateApprovedActions Create(Guid approvalId, WindowsUpdateCandidate exact)
    {
        var impact = ActionRegistry.DescribeWindowsUpdateServicing(exact);
        return new(approvalId, new(Guid.NewGuid(), impact.Download), new(Guid.NewGuid(), impact.Install));
    }

    internal bool HasDistinctIds() => ApprovalId != Guid.Empty &&
        Download is { ActionId: var downloadId, Impact.Kind: ActionKind.DownloadWindowsUpdate } &&
        Install is { ActionId: var installId, Impact.Kind: ActionKind.InstallWindowsUpdate } &&
        downloadId != Guid.Empty && installId != Guid.Empty && downloadId != installId && downloadId != ApprovalId && installId != ApprovalId;

    public bool MatchesCurrentActions(WindowsUpdateCandidate exact) => HasDistinctIds() && WindowsUpdatePolicy.IsSelectable(exact) &&
        (Download.Impact, Install.Impact) == ActionRegistry.DescribeWindowsUpdateServicing(exact);
}

// Only these typed bindings travel to a prepared backend. Operator attribution stays in the job.
internal sealed record WindowsUpdateDispatch(JobId JobId, SanitizationGateDecisionId GateId, Guid ScanId, string PolicyDecisionId,
    Guid ApprovalId, WindowsUpdateCandidate Exact, WindowsUpdatePhaseAction Action, DateTimeOffset ExpiresAtUtc);

internal interface IWindowsUpdateDispatchBackend
{
    // Preparing the channel may wait for consent and authenticate a peer. It cannot send the mutation request.
    Task<IWindowsUpdateDispatchChannel?> PrepareAsync(WindowsUpdateDispatch dispatch, CancellationToken token);
}

internal interface IWindowsUpdateDispatchChannel : IAsyncDisposable
{
    WindowsUpdateDispatch Dispatch { get; }
    Task<WindowsUpdateDispatchReceipt> SendAsync(CancellationToken token);
}

internal sealed record WindowsUpdateDispatchReceipt(WindowsUpdateDispatch Dispatch, WindowsUpdateOperation Outcome);
