using System.Globalization;
using System.Text;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Reports.WorkshopReport;

public enum WorkshopPackageOutcome
{
    Approved,
    PendingReview,
    Applied,
    RestartRequired,
    Verified,
    Failed,
    Cancelled,
    AuthorizationDeclined,
    BackendUnavailable,
    TrustChanged,
    PreconditionChanged,
    VerificationUnconfirmed,
    RolledBack,
    CancelledBeforeDispatch,
    NetworkUnavailable,
    TimedOut,
    NoFinalResult,
    LaunchVerified,
    ProbeUnavailable,
    ProbeExitedEarly,
    ProbeTimedOut,
    ProbeCancelled,
    ProbeCleanupUnconfirmed,
    LaunchVerificationPending,
    DownloadUnavailable,
    DownloadTimedOut,
    ElevationUnavailable,
    DetectionAbsent,
    DetectionMismatch,
    DetectionUnavailable,
    ProbeExecutableMissing,
    ProbeExecutableUntrusted,
    ProbeLaunchFailed,
    BackendUnavailableBeforeDispatch,
    ExistingTargetVerified,
    Skipped,
    ResourcePreflightDeferred,
}

public sealed record WorkshopPackageRecord(
    string PackageId,
    string SourceId,
    string RequestedVersion,
    string PlanSha256,
    WorkshopPackageOutcome Outcome,
    DateTimeOffset OccurredAtUtc);

public static partial class WorkshopReportRenderer
{
    public static string RenderWithPackageRecords(
        StoredJob job,
        WorkshopAssessmentSummary? assessment,
        StoredPolicyDecision? savedDecision,
        IReadOnlyList<WorkshopPackageRecord> packages,
        CancellationToken cancellationToken = default) =>
        RenderWithPackageRecords(job, assessment, savedDecision, packages, null, cancellationToken);

    public static string RenderWithPackageRecords(
        StoredJob job,
        WorkshopAssessmentSummary? assessment,
        StoredPolicyDecision? savedDecision,
        IReadOnlyList<WorkshopPackageRecord> packages,
        IReadOnlyList<WorkshopPreparationPlan>? preparation,
        CancellationToken cancellationToken = default) =>
        RenderWithPackageRecords(job, assessment, savedDecision, packages, preparation, null, cancellationToken);

    public static string RenderWithPackageRecords(
        StoredJob job,
        WorkshopAssessmentSummary? assessment,
        StoredPolicyDecision? savedDecision,
        IReadOnlyList<WorkshopPackageRecord> packages,
        IReadOnlyList<WorkshopPreparationPlan>? preparation,
        WorkshopProfileVerification? profileVerification,
        CancellationToken cancellationToken = default)
    {
        ValidatePackageRecords(packages, savedDecision, assessment);
        return RenderCore(job, assessment, savedDecision, packages, preparation, profileVerification, cancellationToken: cancellationToken);
    }

    private static void ValidatePackageRecords(IReadOnlyList<WorkshopPackageRecord> packages,
        StoredPolicyDecision? savedDecision, WorkshopAssessmentSummary? assessment)
    {
        ArgumentNullException.ThrowIfNull(packages);
        // Each projected attempt or unused approval represents one bounded journal entry.
        if (packages.Count > 1_024 || (savedDecision is not null && assessment is null))
        {
            throw new ArgumentException("The workshop report requires bounded package records and a matching assessment.");
        }
    }

    private static void WritePackageRecords(
        StringBuilder report, IReadOnlyList<WorkshopPackageRecord> packages, CancellationToken cancellationToken)
    {
        Line(report, "");
        Line(report, "Machine-wide setup journal");
        Line(report, "These are saved transaction results. Device readiness requires separate verification.");
        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Line(report, string.Create(CultureInfo.InvariantCulture,
                $"- {SafeText(package.PackageId)} {PublicPackageVersion(package.RequestedVersion)} from {SafeCode(package.SourceId)}. Requested scope machine-wide."));
            Line(report, $"  {DescribePackageOutcome(package.Outcome)}");
            Line(report, $"  Recorded at {Timestamp(package.OccurredAtUtc)}. Plan SHA-256 {SafeCode(package.PlanSha256)}.");
        }
    }

    private static string PublicPackageVersion(string version) =>
        version.Length <= 32 && Version.TryParse(version, out _) ? version : Withheld;

    public static string DescribePackageOutcome(WorkshopPackageOutcome outcome) => outcome switch
    {
        WorkshopPackageOutcome.Approved => "Approved. No installation start was recorded.",
        WorkshopPackageOutcome.PendingReview => "The dispatch was interrupted or its outcome needs review. Check actual installed state before taking further action.",
        WorkshopPackageOutcome.Applied => "The installer reported success. Independent verification has not been recorded.",
        WorkshopPackageOutcome.LaunchVerificationPending => "The installer reported success. The verification result and cleanup of any temporary launch check files have not been confirmed.",
        WorkshopPackageOutcome.RestartRequired => "The installer reported that a restart is required. Save open work before restarting through Windows. Independent verification remains pending.",
        WorkshopPackageOutcome.Verified => "Independent verification confirmed the requested version is installed machine-wide.",
        WorkshopPackageOutcome.LaunchVerified => "Independent verification confirmed the requested version is installed machine-wide and passed the launch check.",
        WorkshopPackageOutcome.ExistingTargetVerified => "Independent verification confirmed the requested version is already installed machine-wide and passed the launch check. ThirdLife skipped Windows Installer for this attempt.",
        WorkshopPackageOutcome.Failed => "The installer reported failure. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.Cancelled => "Cancellation was recorded. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.CancelledBeforeDispatch => "The attempt was cancelled before Windows Installer started.",
        WorkshopPackageOutcome.NetworkUnavailable => "The installer download lost its connection before Windows Installer started.",
        WorkshopPackageOutcome.TimedOut => "The package operation reached its time limit. Windows Installer may still be running. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.NoFinalResult => "No final result was saved. Windows Installer may still be running. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.AuthorizationDeclined => "Windows installation consent was declined. Review the saved attempt with the authorized operator.",
        WorkshopPackageOutcome.Skipped => "The operator skipped this package action.",
        WorkshopPackageOutcome.BackendUnavailable => "The installation backend was unavailable.",
        WorkshopPackageOutcome.BackendUnavailableBeforeDispatch => "The package backend was unavailable before Windows Installer started.",
        WorkshopPackageOutcome.TrustChanged => "The installer failed its package trust check. Stop and review the approved source and package identity.",
        WorkshopPackageOutcome.PreconditionChanged => "The approval, plan or installed state changed before dispatch. Review the job and build a new plan.",
        WorkshopPackageOutcome.ResourcePreflightDeferred => "Resource requirements changed before dispatch. Windows Installer did not start. Check preparation requirements before retrying.",
        WorkshopPackageOutcome.VerificationUnconfirmed => "Independent verification could not confirm the requested version and machine-wide scope.",
        WorkshopPackageOutcome.ProbeUnavailable => "The application could not be started for verification. Review the installation.",
        WorkshopPackageOutcome.ProbeExitedEarly => "The application closed before the launch check completed. Review the installation.",
        WorkshopPackageOutcome.ProbeTimedOut => "The application launch check reached its time limit. Review the installation.",
        WorkshopPackageOutcome.ProbeCancelled => "Independent verification was cancelled. Review the installation.",
        WorkshopPackageOutcome.ProbeCleanupUnconfirmed => "Cleanup of the launch check could not be confirmed. Review any remaining check process or temporary files.",
        WorkshopPackageOutcome.DownloadUnavailable => "The reviewed installer could not be downloaded or staged. Check the approved source and available storage, then review the saved attempt.",
        WorkshopPackageOutcome.DownloadTimedOut => "Preparing the installer reached its time limit before dispatch. Check the connection and review the saved attempt.",
        WorkshopPackageOutcome.ElevationUnavailable => "Windows installation authorization was unavailable. Review the saved attempt with the authorized operator and keep ThirdLife unelevated.",
        WorkshopPackageOutcome.DetectionAbsent => "The requested machine-wide package registration was not found. Check the current registration and review the installation.",
        WorkshopPackageOutcome.DetectionMismatch => "The installed registration did not match the approved package identity, version or scope. Check the current registration and review the installation.",
        WorkshopPackageOutcome.DetectionUnavailable => "The installed package registration could not be read. Check the current registration and review the installation.",
        WorkshopPackageOutcome.ProbeExecutableMissing => "The reviewed executable was missing during the launch check. Review the installation.",
        WorkshopPackageOutcome.ProbeExecutableUntrusted => "The executable failed its launch trust check. Keep it closed and review the installation.",
        WorkshopPackageOutcome.ProbeLaunchFailed => "The reviewed executable could not be started for verification. Review the installation.",
        WorkshopPackageOutcome.RolledBack => "Rollback was independently verified.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
