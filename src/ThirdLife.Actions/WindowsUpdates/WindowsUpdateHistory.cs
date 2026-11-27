using System.Collections.Immutable;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.WindowsUpdates;

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
}

public sealed record WindowsUpdateApproval(Guid ScanId, WindowsUpdateCandidate Exact, OperatorId OperatorId, string PolicyDecisionId);

public sealed record WindowsUpdateEntry(Guid Id, Guid? PreviousId, JobId JobId, SanitizationGateDecisionId GateId,
    DateTimeOffset OccurredAtUtc, WindowsUpdateStage Stage, WindowsUpdateScan? Scan, WindowsUpdateApproval? Approval,
    WindowsUpdateOperation? Operation, ulong? BootUptime = null);

public interface IWindowsUpdateJournal
{
    Task<ImmutableArray<WindowsUpdateEntry>> LoadWindowsUpdatesAsync(JobId jobId, CancellationToken token = default);

    // The store checks the predecessor, state transition, current job gate and any required
    // policy in one transaction. It stamps its own boot observation on scans and restart results.
    Task<WindowsUpdateEntry> AppendWindowsUpdateAsync(WindowsUpdateEntry entry, CancellationToken token = default);
}

public static class WindowsUpdateHistory
{
    public const int MaximumEntries = 128;

    public static WindowsUpdateDisposition Disposition(ImmutableArray<WindowsUpdateEntry> history)
    {
        var latest = history.LastOrDefault();
        if (latest is null)
        {
            return WindowsUpdateDisposition.NotCurrent;
        }

        // A rescan can report applicability after an interruption. It cannot prove what an
        // interrupted mutation installed, or release that action for replay.
        for (var index = 0; index < history.Length; index++)
        {
            var entry = history[index];
            if (entry.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted)
            {
                var completion = entry.Stage == WindowsUpdateStage.DownloadStarted
                    ? WindowsUpdateStage.DownloadCompleted : WindowsUpdateStage.InstallCompleted;
                if (!history.Skip(index + 1).Any(later => later.Stage == completion && later.PreviousId == entry.Id))
                {
                    return WindowsUpdateDisposition.ReviewRequired;
                }
            }

            if (entry.Stage == WindowsUpdateStage.InstallCompleted && entry.Operation?.Status != WindowsUpdateStatus.Succeeded)
            {
                return WindowsUpdateDisposition.ReviewRequired;
            }
        }

        var restart = history.LastOrDefault(entry => entry.Operation?.RebootRequired == true || entry.Scan?.RebootRequired == true);
        if (restart is not null && !history.SkipWhile(entry => entry.Id != restart.Id).Skip(1).Any(entry =>
                entry.Stage == WindowsUpdateStage.ScanCompleted && entry.Scan is { Status: WindowsUpdateStatus.Succeeded, RebootRequired: false } &&
                entry.BootUptime is { } current && restart.BootUptime is { } prior && current < prior))
        {
            return WindowsUpdateDisposition.RestartRequired;
        }

        if (latest.Scan is { Status: WindowsUpdateStatus.Succeeded } observed &&
            history.Any(entry => entry is { Stage: WindowsUpdateStage.InstallCompleted, Operation.Status: WindowsUpdateStatus.Succeeded } &&
                observed.Updates.Any(candidate => candidate.Identity == entry.Approval!.Exact.Identity)))
        {
            return WindowsUpdateDisposition.ReviewRequired;
        }

        return latest.Stage == WindowsUpdateStage.ScanCompleted && latest.Scan is { } scan
            ? WindowsUpdatePolicy.Evaluate(scan, history.Count(entry => entry.Stage == WindowsUpdateStage.DownloadStarted))
            : WindowsUpdateDisposition.NotCurrent;
    }

    public static bool CanAppend(ImmutableArray<WindowsUpdateEntry> history, WindowsUpdateEntry entry)
    {
        var terminal = entry.Stage is WindowsUpdateStage.DownloadCompleted or WindowsUpdateStage.InstallCompleted;
        // An operation receipt is keyed to its exact intent. A concurrent read-only scan can
        // supersede a displayed approval, but cannot discard an already dispatched result.
        var prior = terminal ? history.LastOrDefault(row => row.Id == entry.PreviousId) : history.LastOrDefault();
        if (history.Length >= MaximumEntries || entry.Id == Guid.Empty || entry.JobId is null || entry.GateId is null ||
            entry.OccurredAtUtc == default || entry.PreviousId != prior?.Id || history.Any(row => row.Id == entry.Id) ||
            (prior is not null && (entry.JobId != prior.JobId || entry.OccurredAtUtc < prior.OccurredAtUtc)) ||
            (entry.Stage != WindowsUpdateStage.ScanStarted && entry.GateId != prior?.GateId))
        {
            return false;
        }

        var scope = entry.Approval;
        if (entry.Stage is WindowsUpdateStage.ScanStarted or WindowsUpdateStage.ScanCompleted)
        {
            var outstandingIntent = history.LastOrDefault(row => row.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted);
            var reservedReceipt = outstandingIntent is not null &&
                !history.Any(row => row.PreviousId == outstandingIntent.Id &&
                    row.Stage is WindowsUpdateStage.DownloadCompleted or WindowsUpdateStage.InstallCompleted) ? 1 : 0;
            return scope is null && entry.Operation is null && (entry.Stage == WindowsUpdateStage.ScanStarted
                ? entry.Scan is null && history.Length <= MaximumEntries - 2 - reservedReceipt
                : entry.Scan is not null && prior?.Stage == WindowsUpdateStage.ScanStarted);
        }

        if (scope?.Exact is null || scope.OperatorId is null || scope.ScanId == Guid.Empty ||
            scope.PolicyDecisionId is null ||
            !scope.PolicyDecisionId.StartsWith("policy-decision-", StringComparison.Ordinal) || !Guid.TryParseExact(scope.PolicyDecisionId.AsSpan("policy-decision-".Length), "N", out _) ||
            !WindowsUpdatePolicy.IsSelectable(scope.Exact))
        {
            return false;
        }

        if (entry.Stage == WindowsUpdateStage.Approved)
        {
            // Reserve enough room for both mutation phases and the independent final scan.
            return history.Length <= MaximumEntries - 10 && prior?.Id == scope.ScanId &&
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
            WindowsUpdateStage.InstallStarted => history.Length <= MaximumEntries - 2 && CanStart(prior, WindowsUpdateStage.InstallChecked, entry),
            WindowsUpdateStage.DownloadCompleted => prior.Stage == WindowsUpdateStage.DownloadStarted && entry.Operation is not null && entry.Scan is null,
            WindowsUpdateStage.InstallCompleted => prior.Stage == WindowsUpdateStage.InstallStarted && entry.Operation is not null && entry.Scan is null,
            WindowsUpdateStage.ResourcesDeferred => prior.Stage is WindowsUpdateStage.DownloadChecked or WindowsUpdateStage.InstallChecked &&
                entry.Operation is null && entry.Scan is null,
            _ => false,
        };
    }

    private static bool CanStart(WindowsUpdateEntry previous, WindowsUpdateStage requiredStage, WindowsUpdateEntry entry) =>
        previous.Stage == requiredStage && previous.Scan is { } scan && WindowsUpdatePolicy.Matches(entry.Approval!.Exact, scan) &&
        entry.Operation is null && entry.Scan is null;
}
