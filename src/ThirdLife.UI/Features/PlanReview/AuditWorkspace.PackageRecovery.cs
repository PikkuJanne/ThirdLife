using System.IO;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Recovery;
using ThirdLife.Core.Jobs;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Packages;
using ThirdLife.Persistence;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private bool _packageRecoveryRunning;
    public bool ShowsPackageRecoveryCancellation => _packageRecoveryRunning;
    public bool CanCancelPackageRecovery => _packageRecoveryRunning && CanCancelPackage;

    private bool CanUsePackageRecovery => !IsFixtureScenario && !_hasSeparateEvidenceOrigin &&
        Job is { IsArchived: false } && _packageService is not null;

    public Task CheckPackageRecoveryAsync() =>
        CanCheckPackageRecovery && Job is { Job.JobId: var jobId }
            ? RunAsync(() => ReconcileSavedPackageAsync(jobId))
            : Task.CompletedTask;

    private async Task ReconcileSavedPackageAsync(JobId jobId)
    {
        if (!HasPackageHistory || !CanUsePackageRecovery || _packageService is not { } service || _store is not IActionJournalStore journal)
        {
            return;
        }

        var histories = await Task.Run(() => journal.LoadJobJournalsAsync(jobId));
        var attempts = new List<(JournalHistory History, InstallPackageActionRequest Request, JournalEntry Latest)>();
        foreach (var history in histories)
        {
            if (PackageHistoryRequest(history) is { } request && request.ExactVersion == PilotPackageService.Identity.ExactVersion)
            {
                attempts.Add((history, request, history.Entries.Last(entry => entry.ActionId == request.ActionId)));
            }
        }

        var saved = attempts.OrderByDescending(attempt => attempt.Latest.OccurredAtUtc).ToArray();
        var pending = saved.FirstOrDefault(attempt => attempt.Latest.State is
            JournalState.Started or JournalState.Applied or JournalState.RequiresReview);
        if (pending is not { History: { } savedHistory, Request: { } savedRequest, Latest: { } latest })
        {
            if (_canInspectSavedPackage)
            {
                ClearReport();
                await LoadPackageHistoryAsync(jobId);
            }

            PackageRecoveryStatus = saved.FirstOrDefault().Latest is
            { Checkpoint: JournalCheckpoint.PackageRecoveryVerification or JournalCheckpoint.PackageRebootVerification } verified
                ? DescribeRecoveryResult(verified)
                : string.Empty;
            return;
        }

        PackageRecoveryStatus = "Checking the saved package attempt and current installed state.";
        Publish();
        try
        {
            if (_store is IPackageRecoveryStore recovery && savedHistory.Plan.IsCurrentPolicyPlan &&
                savedHistory.Plan.GateDecisionId == Gate?.Decision?.DecisionId &&
                WorkshopPackageProfile.IncludesLaunchProbe(savedHistory.Plan) &&
                savedRequest.Architecture == ActionArchitecture.X64 && savedHistory.ActiveApproval?.ApprovalId == latest.ApprovalId &&
                latest is
                {
                    State: JournalState.Applied, Actor: JournalActor.PackageBackend,
                    Checkpoint: JournalCheckpoint.PackageLaunchPendingVerification or JournalCheckpoint.RestartRequired
                })
            {
                await RecoverAppliedPackageAsync(journal, recovery, service, savedRequest, latest);
                return;
            }

            var observation = await Task.Run(service.Inspect);
            var mayBeRunning = latest.State == JournalState.Started ||
                (latest.Actor == JournalActor.PackageBackend && latest.Result is
                    JournalResult.Cancelled or JournalResult.TimedOut or JournalResult.Interrupted);
            var previous = latest.Checkpoint is JournalCheckpoint.PackageRecoveryVerification or JournalCheckpoint.PackageRebootVerification
                ? DescribeRecoveryResult(latest) + " "
                : string.Empty;
            PackageRecoveryStatus = previous + DescribePackageRegistration(observation) + (mayBeRunning
                ? " Windows Installer may still be running. Keep this job in review."
                : " Keep this job in review.");
        }
        catch (JobStoreException)
        {
            PackageRecoveryStatus = "The saved recovery result could not be confirmed. Reopen the job to check its journal.";
        }
    }

    private async Task RecoverAppliedPackageAsync(IActionJournalStore journal, IPackageRecoveryStore recovery,
        PilotPackageService service, InstallPackageActionRequest request, JournalEntry prior)
    {
        using var lease = PackageVerificationLease.TryAcquire(prior.CorrelationId);
        if (lease is null)
        {
            PackageRecoveryStatus = "The launch check is in use or unavailable. Check the saved package state again later.";
            return;
        }

        var current = await journal.LoadJournalAsync(prior.Plan);
        if (current?.Entries.LastOrDefault(entry => entry.ActionId == prior.ActionId) != prior ||
            current.ActiveApproval?.ApprovalId != prior.ApprovalId)
        {
            await LoadPackageHistoryAsync(prior.Plan.JobId);
            PackageRecoveryStatus = "The saved package result changed. Check the current journal before continuing.";
            return;
        }

        var restart = prior.Checkpoint == JournalCheckpoint.RestartRequired;
        var candidate = restart ? await recovery.LoadPackageRebootCheckpointAsync(prior) : null;
        if (restart && candidate is not { CanVerify: true })
        {
            var reason = candidate switch
            {
                null => "The saved attempt has no usable restart checkpoint. Keep this job in review.",
                { IsExpired: true } => "The saved restart checkpoint has expired. Keep this job in review.",
                _ => "A Windows restart has not been confirmed. Save your work, use Restart in Windows, then promptly reopen this job.",
            };
            PackageRecoveryStatus = DescribePackageRegistration(await Task.Run(service.Inspect)) + " " + reason;
            return;
        }

        _packageRecoveryRunning = true;
        _packageCancellation = new CancellationTokenSource();
        PackageRecoveryStatus = restart
            ? "Windows restart confirmed. Checking the installed version and testing application launch."
            : "Checking the installed version and testing application launch for the saved attempt.";
        Publish();
        var token = _packageCancellation.Token;
        var status = await service.VerifyInstalledAsync(request, PilotPackageService.Identity.InstallerSha256, token);
        _packageRecoveryRunning = false;
        var (result, diagnostic) = DescribePackageVerification(status);
        var verified = result == JournalResult.PackageLaunchVerified;
        var entry = new JournalEntry(Guid.NewGuid(), prior.Plan, prior.ActionId, prior.ApprovalId, JournalState.Applied,
            verified ? JournalState.Verified : JournalState.RequiresReview, JournalActor.Verifier, PackageVerifier,
            prior.CorrelationId, _timeProvider.GetUtcNow(), result,
            restart ? JournalCheckpoint.PackageRebootVerification : JournalCheckpoint.PackageRecoveryVerification,
            verified ? ActionVerification.ExactPackageIdentity : null);
        var recorded = candidate is { Candidate: var reboot }
            ? await recovery.RecordPackageRebootVerificationAsync(entry, reboot.Token, reboot.Checkpoint.Id)
            : await recovery.RecordPackageRecoveryVerificationAsync(prior, entry);
        if (recorded)
        {
            ClearReport();
            PackageRecoveryStatus = DescribeRecoveryResult(entry);
            await RecordPackageDiagnosticAsync(DiagnosticPhase.Verify, diagnostic, DiagnosticCorrelationId.FromOperation(prior.CorrelationId));
        }
        else
        {
            PackageRecoveryStatus = "The saved recovery authorization or journal changed. Review the current saved result.";
        }

        await LoadPackageHistoryAsync(prior.Plan.JobId);
    }

    private static string DescribeRecoveryResult(JournalEntry entry) =>
        (entry.Checkpoint == JournalCheckpoint.PackageRebootVerification
            ? "Verification continued after Windows restarted. "
            : "Verification continued from the saved installer result. ") +
        WorkshopReportRenderer.DescribePackageOutcome(ReportOutcome(entry.State, entry.Result, entry.Checkpoint));

    private static string DescribePackageRegistration(PackageObservation observation) => observation.Status switch
    {
        PackageObservationStatus.Installed when observation.ExactVersion == PilotPackageService.Identity.ExactVersion =>
            "The approved version is currently registered machine-wide.",
        PackageObservationStatus.Absent => "The approved version was not found.",
        PackageObservationStatus.Conflict or PackageObservationStatus.Installed => "A conflicting installation was found.",
        _ => "Current package registration could not be checked.",
    };

    // Hold the same per-attempt lease across live and resumed verification. A zero-wait
    // contender closes its handle immediately, allowing Windows to discard the semaphore
    // when the last handle closes after process termination.
    private sealed class PackageVerificationLease(Semaphore semaphore) : IDisposable
    {
        internal static PackageVerificationLease? TryAcquire(Guid correlationId)
        {
            Semaphore? handle = null;
            try
            {
                handle = new Semaphore(1, 1, $@"Global\ThirdLife.SetupCore.PackageVerification.{correlationId:N}");
                if (!handle.WaitOne(0))
                {
                    return null;
                }

                var lease = new PackageVerificationLease(handle);
                handle = null;
                return lease;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
            {
                return null;
            }
            finally
            {
                handle?.Dispose();
            }
        }

        public void Dispose()
        {
            using (semaphore)
            {
                semaphore.Release();
            }
        }
    }
}
