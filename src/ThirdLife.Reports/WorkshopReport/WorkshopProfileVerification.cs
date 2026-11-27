using System.Globalization;
using System.Text;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Verification;

namespace ThirdLife.Reports.WorkshopReport;

public sealed record WorkshopProfileVerification(JobId JobId, ProfileVerificationResult Result, bool IsCurrent);

public static partial class WorkshopReportRenderer
{
    public static string DescribeProfileVerification(WorkshopProfileVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (!verification.IsCurrent)
        {
            return "The saved final profile verification is historical. Current readiness requires another verification.";
        }

        if (verification.Result.SyntheticData)
        {
            return "The final profile results come from a synthetic test. Device acceptance remains open.";
        }

        return verification.Result.ProfileChecksPassed
            ? "The final profile checks passed. Cold-boot verification and finalization are still required for handover."
            : "The final profile checks found an unmet or unresolved requirement. Review the blocking results before handover.";
    }

    public static string DescribeProfileVerificationDetails(WorkshopProfileVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return string.Join(Environment.NewLine, ProfileVerificationLines(verification.Result));
    }

    private static void WriteProfileVerification(StringBuilder report, JobId jobId,
        WorkshopProfileVerification verification, CancellationToken cancellationToken)
    {
        if (verification.JobId != jobId)
        {
            throw new ArgumentException("The final profile verification belongs to another job.", nameof(verification));
        }

        Line(report, "");
        Line(report, "Independent final profile verification");
        Line(report, DescribeProfileVerification(verification));
        foreach (var row in ProfileVerificationLines(verification.Result))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Line(report, row);
        }

        Line(report, $"Reviewed profile SHA-256 {verification.Result.ProfileSha256}.");
        Line(report, $"Final verification policy SHA-256 {verification.Result.PolicySha256}.");
    }

    private static IEnumerable<string> ProfileVerificationLines(ProfileVerificationResult result)
    {
        yield return $"Checks ran from {Timestamp(result.StartedAtUtc)} to {Timestamp(result.FinishedAtUtc)}.";
        yield return "";
        yield return "Profile capabilities and exact package versions";
        foreach (var capability in result.Capabilities)
        {
            var name = capability.CapabilityId switch
            {
                "web_browsing" => "Web browsing",
                "document_editing" => "Document editing",
                "pdf_reading" => "PDF reading",
                "video_calling" => "Video calling",
                _ => SafeCode(capability.CapabilityId),
            };
            var importance = capability.Essential ? "essential" : "optional";
            yield return $"- {name} ({importance}). {CapabilityState(capability)}";
            if (capability.ExpectedVersion is { } expected)
            {
                var installed = capability.ObservedVersion is { } version ? ProfilePackageVersion(version) : "unconfirmed";
                yield return $"  Resolved version {ProfilePackageVersion(expected)}. Installed version {installed}.";
            }

            if (capability.ActionId is { } actionId)
            {
                yield return $"  Reviewed action {actionId:D}.";
            }
        }

        yield return "";
        yield return "Machine checks recorded during this pass";
        foreach (var check in result.Checks)
        {
            var blocking = check.Blocking && check.State != ProfileVerificationState.Passed ? " Blocks profile verification." : "";
            yield return $"- {ProfileCheckName(check.Kind)}. {VerificationState(check.State)}{blocking}";
        }

        if (result.WindowsBuild is { } build)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"Windows build {build}.");
        }

        if (result.FreeBytes is { } free)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"System volume free space {free} bytes.");
        }

        if (result.VolumeCapacityBytes is { } capacity)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"System volume capacity {capacity} bytes.");
        }

        if (result.UpdateDisposition is { } updates)
        {
            yield return string.Create(CultureInfo.InvariantCulture,
                $"Windows Update reported {result.UpdateOfferCount} applicable, nonhidden offers, of which {result.EligibleUpdateOfferCount} met the servicing requirements.");
            yield return updates switch
            {
                WindowsUpdateDisposition.Current => "The configured update service reported no pending restart.",
                WindowsUpdateDisposition.NoEligibleOffers => "The offered revisions require workshop review.",
                WindowsUpdateDisposition.RestartRequired => "Windows Update reported a pending restart.",
                WindowsUpdateDisposition.ApprovalRequired => "Eligible revisions require a separate update review.",
                WindowsUpdateDisposition.PassLimitReached => "Remaining updates require review after the servicing pass limit.",
                _ => "Windows Update verification remains unresolved.",
            };
        }

        if (result.UpdateRecoveryPending)
        {
            yield return "A saved update operation still needs reconciliation.";
        }

        yield return "";
        yield return "Unresolved final verification policy findings";
        var warnings = result.PolicyFindings.Where(finding => finding.State is not (PolicyRuleState.Met or PolicyRuleState.Inactive));
        var count = 0;
        foreach (var finding in warnings)
        {
            count++;
            var blocking = finding.Blocking ? " Blocks profile verification." : "";
            yield return $"- {SafeCode(finding.RuleId.Value)}. {PolicyFindingReason(finding.Reason)}{blocking}";
        }

        if (count == 0)
        {
            yield return "No unresolved policy findings were recorded in this pass.";
        }
    }

    private static string ProfilePackageVersion(string version) =>
        Version.TryParse(version, out _) ? PublicPackageVersion(version) : SafeCode(version);

    private static string CapabilityState(ProfileCapabilityResult capability) => (capability.State, capability.PackageStatus) switch
    {
        (ProfileVerificationState.Failed, ProfilePackageStatus.Absent) => "The approved package registration was absent.",
        (ProfileVerificationState.Failed, ProfilePackageStatus.Mismatch) => "The installed package did not match the approved version or scope.",
        (ProfileVerificationState.Unavailable, ProfilePackageStatus.Unavailable) => "The installed package could not be verified.",
        _ => VerificationState(capability.State),
    };

    private static string VerificationState(ProfileVerificationState state) => state switch
    {
        ProfileVerificationState.Passed => "Verified.",
        ProfileVerificationState.Failed => "Requirement unmet.",
        ProfileVerificationState.Unavailable => "Evidence unavailable.",
        ProfileVerificationState.Stale => "Evidence was too old for this pass.",
        ProfileVerificationState.NotApplicable => "Outside the selected checks.",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string ProfileCheckName(ProfileCheckKind kind) => kind switch
    {
        ProfileCheckKind.OperatingSystemSupport => "Windows support",
        ProfileCheckKind.WindowsBuild => "Windows build",
        ProfileCheckKind.Activation => "Windows activation",
        ProfileCheckKind.WindowsUpdate => "Windows Update",
        ProfileCheckKind.PendingRestart => "Pending restart",
        ProfileCheckKind.Firewall => "Firewall",
        ProfileCheckKind.Antivirus => "Antivirus",
        ProfileCheckKind.Defender => "Microsoft Defender",
        ProfileCheckKind.UserAccountControl => "User Account Control",
        ProfileCheckKind.TimeSynchronization => "Recent time synchronization",
        ProfileCheckKind.SystemVolumeCapacity => "System volume capacity",
        ProfileCheckKind.SystemVolumeFreeSpace => "System volume free space",
        ProfileCheckKind.SystemStorageHealth => "System storage health",
        ProfileCheckKind.PhysicalStorageEvidence => "Physical storage evidence",
        ProfileCheckKind.Display => "Display evidence",
        ProfileCheckKind.Keyboard => "Keyboard evidence",
        ProfileCheckKind.PointingDevice => "Pointing device evidence",
        ProfileCheckKind.Network => "Network evidence",
        ProfileCheckKind.AudioOutput => "Audio output evidence",
        ProfileCheckKind.Camera => "Camera evidence",
        ProfileCheckKind.Microphone => "Microphone evidence",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string PolicyFindingReason(PolicyRuleReason reason) => reason switch
    {
        PolicyRuleReason.ConditionUnmet => "Policy requirement unmet.",
        PolicyRuleReason.EvidenceMissing => "Required evidence is missing.",
        PolicyRuleReason.EvidenceUnavailable => "Required evidence is unavailable.",
        PolicyRuleReason.EvidenceConflict => "Evidence sources disagree.",
        PolicyRuleReason.EvidenceStale => "Evidence was too old for this pass.",
        PolicyRuleReason.EvidenceTypeOrUnitMismatch => "Evidence has an unexpected type or unit.",
        PolicyRuleReason.HumanConfirmationRequired => "A guided confirmation is required.",
        PolicyRuleReason.CapabilitySelectionUnavailable => "Capability selection is unavailable.",
        PolicyRuleReason.EvidenceValueUnrecognized => "The reported evidence value requires review.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
