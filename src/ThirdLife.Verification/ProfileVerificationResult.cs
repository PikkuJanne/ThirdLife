using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;

namespace ThirdLife.Verification;

public enum ProfileVerificationState
{
    Passed = 1,
    Failed,
    Unavailable,
    Stale,
    NotApplicable,
}

public enum ProfileCheckKind
{
    OperatingSystemSupport = 1,
    WindowsBuild,
    Activation,
    WindowsUpdate,
    PendingRestart,
    Firewall,
    Antivirus,
    Defender,
    UserAccountControl,
    TimeSynchronization,
    SystemVolumeFreeSpace,
    SystemVolumeCapacity,
    SystemStorageHealth,
    PhysicalStorageEvidence,
    Display,
    Keyboard,
    PointingDevice,
    Network,
    AudioOutput,
    Camera,
    Microphone,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileVerificationCheck(ProfileCheckKind Kind, ProfileVerificationState State, bool Blocking);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileCapabilityResult(string CapabilityId, bool Essential, ProfileVerificationState State,
    string? ExpectedVersion, string? ObservedVersion, Guid? ActionId, ProfilePackageStatus? PackageStatus = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfilePolicyFinding(RequirementId RuleId, PolicyRuleState State, PolicyRuleReason Reason, bool Blocking);

/// <summary>A bounded workshop record of one pass. Product acceptance requires the separate cold-boot checkpoint.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed partial class ProfileVerificationResult
{
    internal const int MaximumCapabilities = 4;
    private const int MaximumPolicyFindings = 131;
    private static readonly ProfileCheckKind[] CheckKinds = Enum.GetValues<ProfileCheckKind>();

    [JsonConstructor]
    public ProfileVerificationResult(DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc, bool syntheticData,
        string profileSha256, string policySha256, ImmutableArray<ProfileVerificationCheck> checks,
        ImmutableArray<ProfileCapabilityResult> capabilities, ImmutableArray<ProfilePolicyFinding> policyFindings,
        int? windowsBuild, long? freeBytes, long? volumeCapacityBytes, int? updateOfferCount, int? eligibleUpdateOfferCount,
        WindowsUpdateDisposition? updateDisposition, bool updateRecoveryPending)
    {
        if (startedAtUtc == default || finishedAtUtc < startedAtUtc)
        {
            throw new ArgumentException("A verification result requires an ordered observation window.");
        }

        if (checks.IsDefault || checks.Length != CheckKinds.Length ||
            checks.Any(check => check is null || !Enum.IsDefined(check.State) ||
                check.Blocking != IsBlocking(check.Kind, capabilities) ||
                check is { Blocking: true, State: ProfileVerificationState.NotApplicable }) ||
            !checks.Select(check => check.Kind).SequenceEqual(CheckKinds))
        {
            throw new ArgumentException("The verification checks are incomplete or invalid.", nameof(checks));
        }

        if (capabilities.IsDefault || capabilities.Length is < 1 or > MaximumCapabilities ||
            capabilities.Any(capability => !IsValid(capability)) ||
            !capabilities.Any(capability => capability.Essential) ||
            capabilities.Select(capability => capability.CapabilityId).Distinct(StringComparer.Ordinal).Count() != capabilities.Length)
        {
            throw new ArgumentException("The profile capability records are incomplete or invalid.", nameof(capabilities));
        }

        if (policyFindings.IsDefaultOrEmpty || policyFindings.Length > MaximumPolicyFindings ||
            policyFindings.Any(finding => finding?.RuleId is null || !IsValid(finding)) ||
            policyFindings.Select(finding => finding.RuleId).Distinct().Count() != policyFindings.Length)
        {
            throw new ArgumentException("The verification policy findings are incomplete or invalid.", nameof(policyFindings));
        }

        if (windowsBuild is <= 0 || freeBytes is < 0 || volumeCapacityBytes is <= 0 ||
            updateOfferCount is < 0 or > WindowsUpdatePolicy.MaximumUpdates ||
            eligibleUpdateOfferCount is < 0 || eligibleUpdateOfferCount > updateOfferCount ||
            updateOfferCount.HasValue != eligibleUpdateOfferCount.HasValue ||
            updateDisposition.HasValue != updateOfferCount.HasValue ||
            updateDisposition.HasValue && !Enum.IsDefined(updateDisposition.Value) ||
            updateRecoveryPending && updateDisposition is null ||
            checks[(int)ProfileCheckKind.WindowsBuild - 1].State == ProfileVerificationState.Passed && windowsBuild is null ||
            checks[(int)ProfileCheckKind.SystemVolumeFreeSpace - 1].State == ProfileVerificationState.Passed && freeBytes is null ||
            checks[(int)ProfileCheckKind.SystemVolumeCapacity - 1].State == ProfileVerificationState.Passed && volumeCapacityBytes is null ||
            checks[(int)ProfileCheckKind.WindowsUpdate - 1].State == ProfileVerificationState.Passed &&
                (updateDisposition != WindowsUpdateDisposition.Current || updateOfferCount != 0 || updateRecoveryPending))
        {
            throw new ArgumentException("The measured verification facts are invalid.");
        }

        StartedAtUtc = startedAtUtc.ToUniversalTime();
        FinishedAtUtc = finishedAtUtc.ToUniversalTime();
        SyntheticData = syntheticData;
        ProfileSha256 = RequireDigest(profileSha256, nameof(profileSha256));
        PolicySha256 = RequireDigest(policySha256, nameof(policySha256));
        Checks = checks;
        Capabilities = capabilities;
        PolicyFindings = policyFindings;
        WindowsBuild = windowsBuild;
        FreeBytes = freeBytes;
        VolumeCapacityBytes = volumeCapacityBytes;
        UpdateOfferCount = updateOfferCount;
        EligibleUpdateOfferCount = eligibleUpdateOfferCount;
        UpdateDisposition = updateDisposition;
        UpdateRecoveryPending = updateRecoveryPending;
    }

    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset FinishedAtUtc { get; }
    public bool SyntheticData { get; }
    public string ProfileSha256 { get; }
    public string PolicySha256 { get; }
    public ImmutableArray<ProfileVerificationCheck> Checks { get; }
    public ImmutableArray<ProfileCapabilityResult> Capabilities { get; }
    public ImmutableArray<ProfilePolicyFinding> PolicyFindings { get; }
    public int? WindowsBuild { get; }
    public long? FreeBytes { get; }
    public long? VolumeCapacityBytes { get; }
    public int? UpdateOfferCount { get; }
    public int? EligibleUpdateOfferCount { get; }
    public WindowsUpdateDisposition? UpdateDisposition { get; }
    public bool UpdateRecoveryPending { get; }

    [JsonIgnore]
    public bool ProfileChecksPassed => !SyntheticData &&
        Checks.All(check => !check.Blocking || check.State == ProfileVerificationState.Passed) &&
        Capabilities.All(capability => !capability.Essential || capability.State == ProfileVerificationState.Passed) &&
        PolicyFindings.All(finding => !finding.Blocking || finding.State is PolicyRuleState.Met or PolicyRuleState.Inactive);

    internal static bool IsKnownCapability(string? value) =>
        value is "web_browsing" or "document_editing" or "pdf_reading" or "video_calling";

    internal static bool IsPackageVersion(string? value) =>
        value is { Length: > 0 and <= 64 } && PackageVersionPattern().IsMatch(value);

    private static bool IsValid(ProfileCapabilityResult? capability) =>
        capability is not null && IsKnownCapability(capability.CapabilityId) &&
        Enum.IsDefined(capability.State) && capability.ActionId != Guid.Empty &&
        (capability.ExpectedVersion is null || IsPackageVersion(capability.ExpectedVersion)) &&
        (capability.ObservedVersion is null || IsPackageVersion(capability.ObservedVersion)) &&
        (capability.State switch
        {
            ProfileVerificationState.Passed => capability.PackageStatus == ProfilePackageStatus.ExactVersion &&
                capability.ExpectedVersion is not null && capability.ObservedVersion == capability.ExpectedVersion &&
                capability.ActionId.HasValue,
            ProfileVerificationState.Failed => capability.ExpectedVersion is not null &&
                (capability.PackageStatus == ProfilePackageStatus.Mismatch ||
                    capability is { PackageStatus: ProfilePackageStatus.Absent, ObservedVersion: null }),
            ProfileVerificationState.Stale => capability is { ExpectedVersion: not null, ObservedVersion: null, PackageStatus: null },
            ProfileVerificationState.Unavailable => capability is
            { PackageStatus: ProfilePackageStatus.Unavailable, ExpectedVersion: not null, ObservedVersion: null } ||
                capability is { ExpectedVersion: null, ObservedVersion: null, ActionId: null, PackageStatus: null },
            ProfileVerificationState.NotApplicable => capability is
            { Essential: false, ExpectedVersion: null, ObservedVersion: null, ActionId: null, PackageStatus: null },
            _ => false,
        });

    private static bool IsBlocking(ProfileCheckKind kind, ImmutableArray<ProfileCapabilityResult> capabilities) => kind switch
    {
        ProfileCheckKind.SystemVolumeCapacity or ProfileCheckKind.PhysicalStorageEvidence => false,
        ProfileCheckKind.AudioOutput or ProfileCheckKind.Camera or ProfileCheckKind.Microphone =>
            !capabilities.IsDefaultOrEmpty && capabilities.Any(capability => capability is
            { CapabilityId: "video_calling", Essential: true }),
        _ => true,
    };

    private static bool IsValid(ProfilePolicyFinding finding) => finding.State switch
    {
        PolicyRuleState.Met => finding.Reason == PolicyRuleReason.ConditionMet,
        PolicyRuleState.Unmet => finding.Reason == PolicyRuleReason.ConditionUnmet,
        PolicyRuleState.PendingConfirmation => finding.Reason == PolicyRuleReason.HumanConfirmationRequired,
        PolicyRuleState.Inactive => finding.Reason == PolicyRuleReason.CapabilityInactive,
        PolicyRuleState.Unavailable => finding.Reason is PolicyRuleReason.EvidenceMissing or
            PolicyRuleReason.EvidenceUnavailable or PolicyRuleReason.EvidenceConflict or PolicyRuleReason.EvidenceStale or
            PolicyRuleReason.EvidenceTypeOrUnitMismatch or PolicyRuleReason.CapabilitySelectionUnavailable or
            PolicyRuleReason.EvidenceValueUnrecognized,
        _ => false,
    };

    private static string RequireDigest(string value, string parameter) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value : throw new ArgumentException("A lowercase SHA-256 digest is required.", parameter);

    [GeneratedRegex(@"\A[0-9][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex PackageVersionPattern();
}
