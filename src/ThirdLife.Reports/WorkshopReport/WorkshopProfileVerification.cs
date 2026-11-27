using System.Globalization;
using System.Text;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Verification;

namespace ThirdLife.Reports.WorkshopReport;

public sealed record WorkshopProfileVerification(JobId JobId, ProfileVerificationResult? Result, bool IsCurrent,
    ColdBootCheckpoint? ColdBootCheckpoint = null, ColdBootObservation? ColdBootObservation = null,
    bool IsPostBoot = false, ColdBootVerificationState? ColdBootState = null);

public static partial class WorkshopReportRenderer
{
    public static string DescribeProfileVerification(WorkshopProfileVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.Result is not { } result)
        {
            return "No final profile checks have been saved.";
        }

        if (!verification.IsCurrent)
        {
            return verification.IsPostBoot
                ? "The saved post-boot verification is historical. Current readiness requires another cold-boot verification."
                : "The saved final profile verification is historical. Current readiness requires another verification.";
        }

        if (result.SyntheticData)
        {
            return "The final profile results come from a synthetic test. Device acceptance remains open.";
        }

        if (verification.IsPostBoot)
        {
            return verification.ColdBootObservation is { Kind: ColdBootKind.OrderlyPowerOff } && result.ProfileChecksPassed &&
                result.Capabilities.All(capability => !capability.Essential || capability.LaunchProbe == ProfileVerificationState.Passed)
                ? "The post-boot profile checks and required launch probes passed. Handover still requires applicable device checks and finalization."
                : "The post-boot checks found an unmet or unresolved requirement. Review the blocking results before handover.";
        }

        return result.ProfileChecksPassed
            ? "The final profile checks passed. Cold-boot verification and finalization are still required for handover."
            : "The final profile checks found an unmet or unresolved requirement. Review the blocking results before handover.";
    }

    public static string DescribeProfileVerificationDetails(WorkshopProfileVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return verification.Result is { } result
            ? string.Join(Environment.NewLine, ProfileVerificationLines(result, verification.IsPostBoot))
            : string.Empty;
    }

    public static string DescribeColdBootVerification(WorkshopProfileVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.ColdBootCheckpoint is null)
        {
            return "Save a cold-boot checkpoint on this job before shutting down Windows.";
        }

        if (verification.IsPostBoot && verification.IsCurrent && verification.Result is not null)
        {
            return DescribeProfileVerification(verification);
        }

        return (verification.ColdBootState, verification.ColdBootObservation?.Kind) switch
        {
            (ColdBootVerificationState.AwaitingShutdown, _) => "Checkpoint saved. Save your work. Press Windows+R, enter shutdown /s /t 0, and press Enter. Wait for the device to power off, turn it on, then reopen this job in Verify.",
            (ColdBootVerificationState.ReadyToVerify, ColdBootKind.OrderlyPowerOff) => "Windows recorded a full startup after an orderly shutdown. Post-boot verification queries the configured Windows Update service and runs the approved package launch checks.",
            (ColdBootVerificationState.Historical, _) => "The saved cold-boot checkpoint is historical. Save a new checkpoint before attempting final acceptance.",
            (_, ColdBootKind.Restart) => "Windows recorded a restart. Save a new checkpoint before attempting an orderly shutdown and full startup.",
            (_, ColdBootKind.HybridOrResume) => "Windows recorded a hybrid startup or resume. Save a new checkpoint before attempting a full shutdown and startup.",
            _ => "Windows could not confirm an orderly shutdown and full startup. Save a new checkpoint before trying again.",
        };
    }

    public static string DescribeColdBootVerificationDetails(WorkshopProfileVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return string.Join(Environment.NewLine, ColdBootVerificationLines(verification));
    }

    private static void WriteProfileVerification(StringBuilder report, JobId jobId,
        WorkshopProfileVerification verification, CancellationToken cancellationToken)
    {
        if (verification.JobId != jobId)
        {
            throw new ArgumentException("The final profile verification belongs to another job.", nameof(verification));
        }

        if (verification.ColdBootCheckpoint is not null)
        {
            Line(report, "");
            Line(report, "Cold-boot checkpoint");
            if (!verification.IsPostBoot || !verification.IsCurrent)
            {
                Line(report, DescribeColdBootVerification(verification));
            }
            foreach (var row in ColdBootVerificationLines(verification))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Line(report, row);
            }
        }

        if (verification.Result is not { } result) { return; }
        Line(report, "");
        Line(report, verification.IsPostBoot ? "Independent post-boot profile verification" : "Independent final profile verification");
        Line(report, DescribeProfileVerification(verification));
        foreach (var row in ProfileVerificationLines(result, verification.IsPostBoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Line(report, row);
        }

        Line(report, $"Reviewed profile SHA-256 {result.ProfileSha256}.");
        Line(report, $"Final verification policy SHA-256 {result.PolicySha256}.");
    }

    private static IEnumerable<string> ColdBootVerificationLines(WorkshopProfileVerification verification)
    {
        if (verification.ColdBootCheckpoint is not { } checkpoint) { yield break; }
        yield return string.Create(CultureInfo.InvariantCulture,
            $"Checkpoint saved at {Timestamp(checkpoint.RequestedAtUtc)} in Windows boot session {checkpoint.RequestedBootSession}.");
        if (verification.ColdBootObservation is { } observation)
        {
            var description = observation.Kind switch
            {
                ColdBootKind.OrderlyPowerOff => "Windows recorded an orderly shutdown and full startup.",
                ColdBootKind.Restart => "Windows recorded a restart. An orderly shutdown and full startup are required.",
                ColdBootKind.HybridOrResume => "Windows recorded a hybrid startup or resume. A full startup is required.",
                _ => "Windows could not establish how this session started.",
            };
            yield return string.Create(CultureInfo.InvariantCulture,
                $"Windows boot session {observation.BootSession} started at {Timestamp(observation.StartedAtUtc)}. {description}");
            yield return "These Windows records describe the observed operating-system session. A physical power-cycle claim requires separate evidence.";
        }
    }

    private static IEnumerable<string> ProfileVerificationLines(ProfileVerificationResult result, bool isPostBoot)
    {
        if (isPostBoot) { yield return "Checks and launch probes from the post-boot pass"; }
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
                if (isPostBoot)
                {
                    var probe = capability.LaunchProbe is { } state ? VerificationState(state) : "No result was recorded.";
                    var blocker = capability.Essential && capability.LaunchProbe != ProfileVerificationState.Passed
                        ? " Blocks post-boot verification." : "";
                    yield return $"  Post-boot launch probe. {probe}{blocker}";
                }
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
