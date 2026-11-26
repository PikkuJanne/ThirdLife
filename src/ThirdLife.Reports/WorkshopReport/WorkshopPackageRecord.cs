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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Count > 128 || (savedDecision is not null && assessment is null))
        {
            throw new ArgumentException("The workshop report requires bounded package records and a matching assessment.");
        }

        return RenderCore(job, assessment, savedDecision, cancellationToken, packages);
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
        WorkshopPackageOutcome.RestartRequired => "The installer reported that a restart is required. Independent verification remains pending.",
        WorkshopPackageOutcome.Verified => "Independent verification confirmed the requested version is installed machine-wide.",
        WorkshopPackageOutcome.LaunchVerified => "Independent verification confirmed the requested version is installed machine-wide and passed the launch check.",
        WorkshopPackageOutcome.Failed => "The installer reported failure. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.Cancelled => "Cancellation was recorded. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.CancelledBeforeDispatch => "The attempt was cancelled before Windows Installer started. To retry, build and approve a new plan.",
        WorkshopPackageOutcome.NetworkUnavailable => "The installer download lost its connection before Windows Installer started. When the source is reachable, build and approve a new plan.",
        WorkshopPackageOutcome.TimedOut => "The package operation reached its time limit. Windows Installer may still be running. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.NoFinalResult => "No final result was saved. Windows Installer may still be running. Review actual installed state before taking further action.",
        WorkshopPackageOutcome.AuthorizationDeclined => "Windows installation consent was declined.",
        WorkshopPackageOutcome.BackendUnavailable => "The installation backend was unavailable.",
        WorkshopPackageOutcome.TrustChanged => "The installer failed its package trust check.",
        WorkshopPackageOutcome.PreconditionChanged => "The approval, plan or installed state changed before dispatch. Review the job and build a new plan.",
        WorkshopPackageOutcome.VerificationUnconfirmed => "Independent verification could not confirm the requested version and machine-wide scope.",
        WorkshopPackageOutcome.ProbeUnavailable => "The application could not be started for verification. Review the installation.",
        WorkshopPackageOutcome.ProbeExitedEarly => "The application closed before the launch check completed. Review the installation.",
        WorkshopPackageOutcome.ProbeTimedOut => "The application launch check reached its time limit. Review the installation.",
        WorkshopPackageOutcome.ProbeCancelled => "Independent verification was cancelled. Review the installation.",
        WorkshopPackageOutcome.ProbeCleanupUnconfirmed => "Cleanup of the launch check could not be confirmed. Review any remaining check process or temporary files.",
        WorkshopPackageOutcome.RolledBack => "Rollback was independently verified.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
