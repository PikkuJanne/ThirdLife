using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.WindowsUpdates;

public enum UpdateRecovery
{
    None,
    ReviewResult,
    Restart,
    VerifyInstallation,
    Servicing,
}

public enum WindowsUpdateStage
{
    ScanStarted = 1,
    ScanCompleted,
    Approved,
    DownloadChecked,
    DownloadStarted,
    DownloadCompleted,
    InstallChecked,
    InstallStarted,
    InstallCompleted,
    ResourcesDeferred,
    DownloadPreparing,
    InstallPreparing,
    DispatchNotSent,
    DispatchUnconfirmed,
    BrokerRefused,
}

public sealed record WindowsUpdateApproval(Guid ScanId, WindowsUpdateCandidate Exact, OperatorId OperatorId, string PolicyDecisionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WindowsUpdateApprovedActions? Actions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ServicingPolicySha256 = null);

public sealed record WindowsUpdateVerification(Guid InstallReceiptId, WindowsUpdateInstalledObservation Observation);

public sealed record WindowsUpdateEntry(Guid Id, Guid? PreviousId, JobId JobId, SanitizationGateDecisionId GateId,
    DateTimeOffset OccurredAtUtc, WindowsUpdateStage Stage, WindowsUpdateScan? Scan, WindowsUpdateApproval? Approval,
    WindowsUpdateOperation? Operation, ulong? BootUptime = null, WindowsUpdateVerification? Verification = null);

public interface IWindowsUpdateJournal
{
    Task<ImmutableArray<WindowsUpdateEntry>> LoadWindowsUpdatesAsync(JobId jobId, CancellationToken token = default);

    Task<string?> FindWindowsUpdatePolicyAsync(JobId jobId, SanitizationGateDecisionId gateId, Guid scanId,
        WindowsUpdateCandidate exact, string? servicingPolicySha256, CancellationToken token = default) => Task.FromResult<string?>(null);

    // The store checks the predecessor, state transition, current job gate and any required
    // policy in one transaction. It stamps its own boot observation on scans and restart results.
    Task<WindowsUpdateEntry> AppendWindowsUpdateAsync(WindowsUpdateEntry entry, CancellationToken token = default);

    // Atomically revalidate scope and claim the exact, once-only send intent after channel setup.
    Task<WindowsUpdateEntry?> TryStartWindowsUpdateDispatchAsync(WindowsUpdateEntry intent, CancellationToken token = default) =>
        Task.FromResult<WindowsUpdateEntry?>(null);
}

public static class WindowsUpdateHistory
{
    public const int MaximumEntries = 128;
    public static readonly TimeSpan DispatchWindow = TimeSpan.FromMinutes(2);

    public static bool IsPhaseTerminal(WindowsUpdateStage stage) =>
        stage is WindowsUpdateStage.DownloadCompleted or WindowsUpdateStage.InstallCompleted or WindowsUpdateStage.DispatchNotSent
            or WindowsUpdateStage.DispatchUnconfirmed or WindowsUpdateStage.BrokerRefused;

    public static WindowsUpdateDisposition Disposition(ImmutableArray<WindowsUpdateEntry> history)
    {
        var latest = history.LastOrDefault();
        if (latest is null)
        {
            return WindowsUpdateDisposition.NotCurrent;
        }

        var recovery = InspectRecovery(history, out var downloadAttempts);
        if (recovery == UpdateRecovery.Restart) { return WindowsUpdateDisposition.RestartRequired; }
        if (recovery != UpdateRecovery.None) { return WindowsUpdateDisposition.ReviewRequired; }

        return latest.Stage == WindowsUpdateStage.ScanCompleted && latest.Scan is { } scan
            ? WindowsUpdatePolicy.Evaluate(scan, downloadAttempts)
            : WindowsUpdateDisposition.NotCurrent;
    }

    // An outstanding mutation or restart holds the package phase. Merely finding offers
    // outside the update policy does not block independent package preparation.
    public static UpdateRecovery RecoveryBeforePackages(ImmutableArray<WindowsUpdateEntry> history)
    {
        var recovery = InspectRecovery(history, out _);
        if (recovery != UpdateRecovery.None) { return recovery; }
        var latest = history.LastOrDefault();
        var betweenPhases = latest is
        { Stage: WindowsUpdateStage.DownloadCompleted, Operation: { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false } };
        var checkedPhase = latest is
        { Stage: WindowsUpdateStage.DownloadChecked or WindowsUpdateStage.InstallChecked, Scan: { } scan, Approval: { } approval } &&
            WindowsUpdatePolicy.Matches(approval.Exact, scan);
        return betweenPhases || checkedPhase ? UpdateRecovery.Servicing : UpdateRecovery.None;
    }

    private static UpdateRecovery InspectRecovery(ImmutableArray<WindowsUpdateEntry> history, out int downloadAttempts)
    {
        // A rescan can report applicability after an interruption. It cannot prove what an
        // interrupted mutation installed, or release that action for replay.
        downloadAttempts = 0;
        for (var index = 0; index < history.Length; index++)
        {
            var entry = history[index];
            if (entry.Stage is WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.InstallPreparing)
            {
                // Preparing a channel can never send an action. A later send claim or a known
                // refusal must settle this exact preparation before a fresh approval is admitted.
                if (!history.Skip(index + 1).Any(later => later.PreviousId == entry.Id && later.Stage is
                    WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted or WindowsUpdateStage.DispatchNotSent))
                {
                    return UpdateRecovery.ReviewResult;
                }
            }
            if (entry.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted)
            {
                var completion = entry.Stage == WindowsUpdateStage.DownloadStarted
                    ? WindowsUpdateStage.DownloadCompleted : WindowsUpdateStage.InstallCompleted;
                var receipt = history.Skip(index + 1).FirstOrDefault(later => later.PreviousId == entry.Id &&
                    (later.Stage == completion || later.Stage == WindowsUpdateStage.BrokerRefused));
                if (receipt is null)
                {
                    return UpdateRecovery.ReviewResult;
                }
                if (entry.Stage == WindowsUpdateStage.DownloadStarted && receipt.Stage != WindowsUpdateStage.BrokerRefused) { downloadAttempts++; }
            }

            if (entry.Stage == WindowsUpdateStage.DispatchUnconfirmed ||
                (entry.Stage == WindowsUpdateStage.InstallCompleted && entry.Operation?.Status != WindowsUpdateStatus.Succeeded))
            {
                return UpdateRecovery.ReviewResult;
            }
        }

        var restart = LatestRestartRequirement(history);
        if (restart is not null && !HasPostRestartScan(history, restart))
        {
            return UpdateRecovery.Restart;
        }

        if (history.Any(entry => entry.Stage == WindowsUpdateStage.InstallCompleted && !HasInstalledEvidence(history, entry)))
        {
            return UpdateRecovery.VerifyInstallation;
        }

        // A later interrupted or unsuccessful scan cannot clear a known contradiction.
        // Compare receipts only with a completed inventory that followed them in journal order.
        var inventory = history.LastOrDefault(entry => entry is
        { Stage: WindowsUpdateStage.ScanCompleted, Scan.Status: WindowsUpdateStatus.Succeeded });
        if (inventory?.Scan is { } observed && history.TakeWhile(entry => entry.Id != inventory.Id).Any(entry =>
                entry is { Stage: WindowsUpdateStage.InstallCompleted, Operation.Status: WindowsUpdateStatus.Succeeded } &&
                observed.Updates.Any(candidate => candidate.Identity == entry.Approval!.Exact.Identity)))
        {
            return UpdateRecovery.ReviewResult;
        }

        return UpdateRecovery.None;
    }

    private static bool HasInstalledEvidence(ImmutableArray<WindowsUpdateEntry> history, WindowsUpdateEntry receipt) =>
        history.Any(entry => entry.Verification is { Observation: { Status: WindowsUpdateStatus.Succeeded, Installed: true } } verification &&
            verification.InstallReceiptId == receipt.Id && verification.Observation.Identity == receipt.Approval!.Exact.Identity);

    private static WindowsUpdateEntry? LatestRestartRequirement(ImmutableArray<WindowsUpdateEntry> history) =>
        history.LastOrDefault(entry => entry.Operation?.RebootRequired == true || entry.Scan?.RebootRequired == true);

    private static bool HasPostRestartScan(ImmutableArray<WindowsUpdateEntry> history, WindowsUpdateEntry restart) =>
        history.SkipWhile(entry => entry.Id != restart.Id).Skip(1).Any(entry =>
            entry.Stage == WindowsUpdateStage.ScanCompleted && entry.Scan is { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false } &&
            entry.BootUptime < restart.BootUptime);

    internal static WindowsUpdateEntry? PendingVerification(ImmutableArray<WindowsUpdateEntry> history, WindowsUpdateScan scan)
    {
        // A scan opens under its own current intake and commits one inventory plus at most
        // one exact installed-state observation. A different intake cannot adopt an old receipt.
        if (history.LastOrDefault() is not { Stage: WindowsUpdateStage.ScanStarted } started ||
            scan is not { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false })
        {
            return null;
        }

        var receipt = history.LastOrDefault(entry => entry.Stage == WindowsUpdateStage.InstallCompleted);
        if (receipt is not { Operation.Status: WindowsUpdateStatus.Succeeded, Approval: not null } ||
            receipt.GateId != started.GateId || HasInstalledEvidence(history, receipt))
        {
            return null;
        }

        var restart = LatestRestartRequirement(history);
        // Capture uptime before the network lookup. Only a lower observed uptime establishes
        // a later boot. A prior qualifying post-boot scan can also supply that observation.
        if (restart is not null && !(started.BootUptime < restart.BootUptime) &&
            !HasPostRestartScan(history, restart))
        {
            return null;
        }

        return receipt;
    }

    public static bool CanAppend(ImmutableArray<WindowsUpdateEntry> history, WindowsUpdateEntry entry)
    {
        var terminal = IsPhaseTerminal(entry.Stage);
        // An operation receipt is keyed to its exact intent. A concurrent read-only scan can
        // supersede a displayed approval, but cannot discard an already dispatched result.
        var prior = terminal ? history.LastOrDefault(row => row.Id == entry.PreviousId) : history.LastOrDefault();
        if (history.Length >= MaximumEntries || entry.Id == Guid.Empty || entry.JobId is null || entry.GateId is null ||
            entry.OccurredAtUtc == default || entry.PreviousId != prior?.Id || history.Any(row => row.Id == entry.Id) ||
            (prior is not null && (entry.JobId != prior.JobId || (!terminal && entry.OccurredAtUtc < prior.OccurredAtUtc))) ||
            (entry.Stage != WindowsUpdateStage.ScanStarted && entry.GateId != prior?.GateId))
        {
            return false;
        }

        if (entry.Verification is { } verification && (entry.Stage != WindowsUpdateStage.ScanCompleted ||
                entry.Scan is null || PendingVerification(history, entry.Scan) is not { } receipt ||
                verification.InstallReceiptId != receipt.Id || verification.Observation?.Identity != receipt.Approval!.Exact.Identity))
        {
            return false;
        }

        var scope = entry.Approval;
        if (entry.Stage is WindowsUpdateStage.ScanStarted or WindowsUpdateStage.ScanCompleted)
        {
            var outstandingIntent = history.LastOrDefault(row => row.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted
                or WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.InstallPreparing);
            var awaitingReceipt = outstandingIntent is not null &&
                !history.Any(row => row.PreviousId == outstandingIntent.Id && IsPhaseTerminal(row.Stage));
            var reserved = awaitingReceipt ? 1 : 0;
            // A concurrent scan cannot consume the two slots needed for the successful
            // install's final inventory and exact verification after its receipt lands.
            if (awaitingReceipt && outstandingIntent!.Stage == WindowsUpdateStage.InstallStarted)
            {
                reserved += 2;
            }
            return scope is null && entry.Operation is null && (entry.Stage == WindowsUpdateStage.ScanStarted
                ? entry.Scan is null && history.Length <= MaximumEntries - 2 - reserved
                : entry.Scan is not null && prior?.Stage == WindowsUpdateStage.ScanStarted);
        }

        if (scope?.Exact is null || scope.OperatorId is null || scope.ScanId == Guid.Empty ||
            scope.ServicingPolicySha256 is { } digest && (scope.Actions is null || digest.Length != 64 ||
                !digest.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
            scope.PolicyDecisionId is null ||
            !scope.PolicyDecisionId.StartsWith("policy-decision-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(scope.PolicyDecisionId.AsSpan("policy-decision-".Length), "N", out _) ||
            !WindowsUpdatePolicy.IsSelectable(scope.Exact))
        {
            return false;
        }

        if (entry.Stage == WindowsUpdateStage.Approved)
        {
            // Reserve enough room for both mutation phases and the independent final scan.
            return (scope.Actions is null || (scope.Actions.HasDistinctIds() && scope.Actions.ApprovalId == entry.Id)) &&
                history.Length <= MaximumEntries - 10 && prior?.Id == scope.ScanId &&
                prior is { Stage: WindowsUpdateStage.ScanCompleted, Scan: { } scanned } &&
                Disposition(history) == WindowsUpdateDisposition.ApprovalRequired &&
                WindowsUpdatePolicy.Matches(scope.Exact, scanned) && entry.Scan is null && entry.Operation is null;
        }

        if (terminal && history.SkipWhile(row => row.Id != entry.PreviousId).Skip(1)
                .Any(row => row.Stage is not (WindowsUpdateStage.ScanStarted or WindowsUpdateStage.ScanCompleted)))
        {
            return false;
        }

        if (prior?.Approval != scope)
        {
            return false;
        }

        return entry.Stage switch
        {
            WindowsUpdateStage.DownloadChecked => prior.Stage == WindowsUpdateStage.Approved && entry.Scan is not null && entry.Operation is null,
            WindowsUpdateStage.InstallChecked => prior is
            {
                Stage: WindowsUpdateStage.DownloadCompleted,
                Operation: { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false }
            } && entry.Scan is not null && entry.Operation is null,
            WindowsUpdateStage.DownloadStarted => history.Length <= MaximumEntries - 2 && CanStart(prior, WindowsUpdateStage.DownloadChecked, entry),
            WindowsUpdateStage.InstallStarted => history.Length <= MaximumEntries - 4 && CanStart(prior, WindowsUpdateStage.InstallChecked, entry),
            WindowsUpdateStage.DownloadPreparing => history.Length <= MaximumEntries - 4 && CanPrepare(prior, WindowsUpdateStage.DownloadChecked, entry),
            WindowsUpdateStage.InstallPreparing => history.Length <= MaximumEntries - 5 && CanPrepare(prior, WindowsUpdateStage.InstallChecked, entry),
            WindowsUpdateStage.DispatchNotSent => prior.Stage is WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.InstallPreparing &&
                entry.Operation is { ProgressPercent: null, Status: not WindowsUpdateStatus.Succeeded } && entry.Scan is null,
            WindowsUpdateStage.BrokerRefused => prior.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted &&
                entry.Operation is { RebootRequired: null, ProgressPercent: null, Status: not (WindowsUpdateStatus.Succeeded or WindowsUpdateStatus.Partial) } && entry.Scan is null,
            WindowsUpdateStage.DispatchUnconfirmed => prior.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted &&
                entry.Operation is { ProgressPercent: null, RebootRequired: null, Status: WindowsUpdateStatus.Partial } && entry.Scan is null,
            WindowsUpdateStage.DownloadCompleted => prior.Stage == WindowsUpdateStage.DownloadStarted && entry.Operation is not null && entry.Scan is null,
            WindowsUpdateStage.InstallCompleted => prior.Stage == WindowsUpdateStage.InstallStarted && entry.Operation is not null && entry.Scan is null,
            WindowsUpdateStage.ResourcesDeferred => prior.Stage is WindowsUpdateStage.DownloadChecked or WindowsUpdateStage.InstallChecked &&
                entry.Operation is null && entry.Scan is null,
            _ => false,
        };
    }

    private static bool CanPrepare(WindowsUpdateEntry previous, WindowsUpdateStage requiredStage, WindowsUpdateEntry entry) =>
        previous.Stage == requiredStage && previous.Scan is { } scan && WindowsUpdatePolicy.Matches(entry.Approval!.Exact, scan) &&
        entry.Approval.Actions is not null && entry.Scan is null && entry.Operation is null;

    private static bool CanStart(WindowsUpdateEntry previous, WindowsUpdateStage requiredStage, WindowsUpdateEntry entry) =>
        // Schema 7/8 recorded the send intent directly after the check. Keep those rows readable.
        (previous.Stage == requiredStage && previous.Scan is { } scan && WindowsUpdatePolicy.Matches(entry.Approval!.Exact, scan) ||
            previous.Stage == (requiredStage == WindowsUpdateStage.DownloadChecked ? WindowsUpdateStage.DownloadPreparing : WindowsUpdateStage.InstallPreparing)) &&
        (entry.Approval!.Actions is not { } actions ||
            entry.Id == (requiredStage == WindowsUpdateStage.DownloadChecked ? actions.Download.ActionId : actions.Install.ActionId)) &&
        entry.Operation is null && entry.Scan is null;
}
