using System.Collections.Immutable;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Verification;

public enum ProfilePackageStatus
{
    ExactVersion = 1,
    Absent,
    Mismatch,
    Unavailable,
}

public sealed record ProfilePackageCheck(string CapabilityId, string ExpectedVersion, string? ObservedVersion,
    ProfilePackageStatus Status, DateTimeOffset ObservedAtUtc, Guid? ActionId = null);

public sealed record ProfileUpdateCheck(WindowsUpdateScan Scan, DateTimeOffset ObservedAtUtc, bool RecoveryPending = false);

/// <summary>Evaluates evidence gathered during one independent, read-only machine verification pass.</summary>
public static class ProfileVerification
{
    private const string StaleLimitation = "provider_value_stale";
    private const string FreeSpaceKey = "storage.system.free_bytes";
    private const string StorageHealthKey = "storage.system.health_state";
    private const string RestartKey = "reboot.pending_status";
    private const string StorageProvider = "windows-storage";
    private const string VolumeSource = "storage.kernel32.system_volume";
    private const string PhysicalDiskSource = "storage.msft.physical_disk";
    private const string DeviceProvider = "windows-device-enumeration";

    public static ProfileVerificationResult Evaluate(DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc,
        ProfileDocument profile, IReadOnlyList<Observation> observations, IReadOnlyList<Observation> policyEvidence,
        PolicyDocument policy, ProfileUpdateCheck? updates, IReadOnlyList<ProfilePackageCheck> packages,
        IReadOnlyList<string>? selectedOptionalCapabilityIds = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(policyEvidence);
        if (startedAtUtc == default || finishedAtUtc < startedAtUtc || !policy.IsCurrentSchema)
        {
            throw new ArgumentException("Verification requires an ordered observation window and a current policy schema.");
        }

        var optional = SelectOptionalCapabilities(profile, selectedOptionalCapabilityIds);
        var active = profile.WorkshopCapabilities.Where(capability =>
            capability.Intent == CapabilityIntent.Essential || optional.Contains(capability.CapabilityId)).ToArray();
        ValidatePackages(packages, active);
        var capabilities = profile.WorkshopCapabilities.Select(capability => EvaluateCapability(capability, optional,
            packages.SingleOrDefault(package => package.CapabilityId == capability.CapabilityId), startedAtUtc, finishedAtUtc))
            .ToImmutableArray();

        var raw = Index(observations, profile.SyntheticData);
        var facts = Index(policyEvidence, profile.SyntheticData);
        var ruleEvidence = facts.SelectMany(group => group)
            .Select(fact => RuleObservation(fact, startedAtUtc, finishedAtUtc)).ToArray();
        var decision = DispositionEngine.Evaluate(policy, ruleEvidence, active.Select(capability => capability.CapabilityId));
        var policyFindings = decision.RuleDetails.Select(detail => new ProfilePolicyFinding(
            detail.Outcome.Rule.Requirement.RequirementId, detail.Outcome.State, detail.Outcome.Reason,
            detail.Outcome.Rule.Decision.BlocksReady)).ToImmutableArray();

        var build = ReadRaw("os.build", "windows-operating-system", "os.dotnet.environment.version");
        var windowsBuild = build.Value?.IntegerValue is > 0 and <= int.MaxValue and var number ? (int?)number : null;
        var free = ReadRaw("storage.system_volume.caller_free_bytes", StorageProvider, VolumeSource, unit: "bytes");
        var freeBytes = free.Value?.IntegerValue is >= 0 and var measured ? measured : (long?)null;
        var freeSpace = Worse(NumberCheck(free, freeBytes > 0),
            PolicyCheck(FreeSpaceKey, ProfileVerificationState.Passed));
        var capacity = ReadRaw("storage.system_volume.caller_capacity_bytes", StorageProvider, VolumeSource, unit: "bytes");
        var capacityBytes = capacity.Value?.IntegerValue is > 0 and var measuredCapacity ? measuredCapacity : (long?)null;
        var update = EvaluateUpdates(updates, startedAtUtc, finishedAtUtc);
        var restartCheck = ReadFact(RestartKey);
        var restart = RestartState(restartCheck, update);
        var video = active.SingleOrDefault(capability => capability.CapabilityId == "video_calling");
        var videoRequired = video?.Intent == CapabilityIntent.Essential;

        var checks = ImmutableArray.Create(
            new ProfileVerificationCheck(ProfileCheckKind.OperatingSystemSupport, Worse(
                PolicyCheck("operating_system.release", ProfileVerificationState.Unavailable),
                Worse(PolicyCheck("operating_system.support_state", ProfileVerificationState.Unavailable),
                    PolicyCheck("system.architecture", ProfileVerificationState.Unavailable))), true),
            new(ProfileCheckKind.WindowsBuild, NumberCheck(build, windowsBuild.HasValue), true),
            new(ProfileCheckKind.Activation, TokenCheck(ReadFact("activation.state"), "activated"), true),
            new(ProfileCheckKind.WindowsUpdate, update.State, true),
            new(ProfileCheckKind.PendingRestart, restart, true),
            new(ProfileCheckKind.Firewall, TokenCheck(ReadFact("security.firewall_health"), "good"), true),
            new(ProfileCheckKind.Antivirus, TokenCheck(ReadFact("security.antivirus_health"), "good"), true),
            new(ProfileCheckKind.Defender, TokenCheck(ReadFact("security.defender_health"), "real_time_active_signatures_current"), true),
            new(ProfileCheckKind.UserAccountControl, TokenCheck(ReadFact("security.uac_state"), "enabled"), true),
            new(ProfileCheckKind.TimeSynchronization, TokenCheck(ReadFact("security.time_sync_recency"), "within_seven_days"), true),
            new(ProfileCheckKind.SystemVolumeFreeSpace, freeSpace, true),
            new(ProfileCheckKind.SystemVolumeCapacity, NumberCheck(capacity, capacityBytes.HasValue), false),
            new(ProfileCheckKind.SystemStorageHealth, TokenCheck(ReadFact(StorageHealthKey), "healthy"), true),
            new(ProfileCheckKind.PhysicalStorageEvidence, PhysicalStorageState(raw, startedAtUtc, finishedAtUtc), false),
            new(ProfileCheckKind.Display, Device("display", "monitor_present"), true),
            new(ProfileCheckKind.Keyboard, Device("keyboard", "present"), true),
            new(ProfileCheckKind.PointingDevice, Device("pointing", "present"), true),
            new(ProfileCheckKind.Network, AnyAvailable(Device("wifi", "present"), Device("ethernet", "present")), true),
            new(ProfileCheckKind.AudioOutput, VideoDevice("audio", "render_endpoint_present"), videoRequired),
            new(ProfileCheckKind.Camera, VideoDevice("camera", "video_interface_present"), videoRequired),
            new(ProfileCheckKind.Microphone, VideoDevice("microphone", "capture_endpoint_present"), videoRequired));

        return new(startedAtUtc, finishedAtUtc, profile.SyntheticData, profile.ContentSha256, policy.ContentSha256,
            checks, capabilities, policyFindings, windowsBuild, freeBytes, capacityBytes, update.OfferCount, update.EligibleCount,
            update.Disposition, update.RecoveryPending);

        Fact ReadFact(string key) => Read(facts[key], startedAtUtc, finishedAtUtc, expectedUnit: null);

        Fact ReadRaw(string key, string provider, string source, string? unit = null) =>
            Read(raw[key].Where(fact => IsOwned(fact, provider, source)), startedAtUtc, finishedAtUtc, unit);

        ProfileVerificationState PolicyCheck(string key, ProfileVerificationState fallback)
        {
            var states = decision.RuleDetails.Where(detail => detail.Outcome.Rule.Requirement.EvidenceKey.Value == key &&
                    detail.Outcome.State != PolicyRuleState.Inactive)
                .Select(detail => RuleState(detail.Outcome)).ToArray();
            return states.Length == 0 ? fallback : states.Aggregate(Worse);
        }

        ProfileVerificationState Device(string name, string field)
        {
            var source = name is "wifi" or "ethernet" ? "device_enumeration.net_adapter.numeric.v1" :
                "device_enumeration.local." + name + ".v1";
            var fact = ReadRaw("devices." + name + "." + field, DeviceProvider, source);
            return fact.Value?.BooleanValue switch
            {
                true => ProfileVerificationState.Passed,
                false => ProfileVerificationState.Failed,
                null => fact.State == ProfileVerificationState.Passed ? ProfileVerificationState.Unavailable : fact.State,
            };
        }

        ProfileVerificationState VideoDevice(string name, string field) =>
            video is null ? ProfileVerificationState.NotApplicable : Device(name, field);
    }

    private static HashSet<string> SelectOptionalCapabilities(ProfileDocument profile, IReadOnlyList<string>? selected)
    {
        if (selected is null) { return new(StringComparer.Ordinal); }
        var options = profile.WorkshopCapabilities.Where(capability => capability.Intent == CapabilityIntent.Optional)
            .Select(capability => capability.CapabilityId).ToHashSet(StringComparer.Ordinal);
        if (selected.Count > options.Count || selected.Any(id => !options.Contains(id)) ||
            selected.Distinct(StringComparer.Ordinal).Count() != selected.Count)
        {
            throw new ArgumentException("Every selected option must belong to the verified profile.", nameof(selected));
        }

        return selected.ToHashSet(StringComparer.Ordinal);
    }

    private static void ValidatePackages(IReadOnlyList<ProfilePackageCheck> packages, ProfileCapability[] active)
    {
        if (packages.Count > active.Length ||
            packages.Any(package => package is null || package.ObservedAtUtc == default || package.ActionId == Guid.Empty ||
                !Enum.IsDefined(package.Status) || !ProfileVerificationResult.IsPackageVersion(package.ExpectedVersion) ||
                package.ObservedVersion is { } observed && !ProfileVerificationResult.IsPackageVersion(observed) ||
                package.Status == ProfilePackageStatus.ExactVersion &&
                    (package.ExpectedVersion != package.ObservedVersion || !package.ActionId.HasValue) ||
                package.Status is ProfilePackageStatus.Absent or ProfilePackageStatus.Unavailable &&
                    package.ObservedVersion is not null ||
                !active.Any(capability => capability.CapabilityId == package.CapabilityId)) ||
            packages.Select(package => package.CapabilityId).Distinct(StringComparer.Ordinal).Count() != packages.Count ||
            packages.Where(package => package.ActionId.HasValue).Select(package => package.ActionId).Distinct().Count() !=
                packages.Count(package => package.ActionId.HasValue))
        {
            throw new ArgumentException("The package checks do not match the selected profile capabilities.", nameof(packages));
        }
    }

    private static ProfileCapabilityResult EvaluateCapability(ProfileCapability capability, HashSet<string> optional,
        ProfilePackageCheck? package, DateTimeOffset start, DateTimeOffset finish)
    {
        var essential = capability.Intent == CapabilityIntent.Essential;
        var selected = essential || optional.Contains(capability.CapabilityId);
        var state = package switch
        {
            null when !selected => ProfileVerificationState.NotApplicable,
            null => ProfileVerificationState.Unavailable,
            { ObservedAtUtc: var observed } when !InWindow(observed, start, finish) => ProfileVerificationState.Stale,
            { Status: ProfilePackageStatus.ExactVersion } => ProfileVerificationState.Passed,
            { Status: ProfilePackageStatus.Absent or ProfilePackageStatus.Mismatch } => ProfileVerificationState.Failed,
            _ => ProfileVerificationState.Unavailable,
        };
        return new(capability.CapabilityId, essential, state, package?.ExpectedVersion,
            state == ProfileVerificationState.Stale ? null : package?.ObservedVersion, package?.ActionId,
            state == ProfileVerificationState.Stale ? null : package?.Status);
    }

    private static ILookup<string, Observation> Index(IReadOnlyList<Observation> observations, bool synthetic)
    {
        if (observations.Count > PolicyEvaluator.MaximumObservations || observations.Any(observation => observation is null))
        {
            throw new ArgumentException("The verification observation set exceeds its limit or contains invalid records.", nameof(observations));
        }

        return observations.Where(observation =>
            observation.Metadata.Provenance.Kind != ProvenanceKind.SyntheticFixture || synthetic)
            .ToLookup(observation => observation.EvidenceKey.Value, StringComparer.Ordinal);
    }

    private static Observation RuleObservation(Observation fact, DateTimeOffset start, DateTimeOffset finish)
    {
        // Intake remains attributable to the current sanitization gate. The caller supplies its validated gate fact.
        if (InWindow(fact.Metadata.CollectedAtUtc, start, finish) ||
            fact.EvidenceKey.Value == "sanitization.state" && fact.Metadata.CollectedAtUtc <= finish)
        {
            return fact;
        }

        return new(fact.Metadata, fact.EvidenceKey, fact.Value, fact.Unit, StaleLimitation, fact.OperatorId);
    }

    private static bool InWindow(DateTimeOffset time, DateTimeOffset start, DateTimeOffset finish) =>
        time >= start && time <= finish;

    private static bool IsOwned(Observation fact, string provider, string source) =>
        fact.Metadata.ProviderId.Value == provider && fact.Metadata.Provenance.SourceReference == source &&
        fact.Metadata.Provenance.Kind is ProvenanceKind.ProviderObservation or ProvenanceKind.SyntheticFixture;

    private static Fact Read(IEnumerable<Observation> observations, DateTimeOffset start, DateTimeOffset finish, string? expectedUnit)
    {
        if (observations.Take(2).ToArray() is not [var fact]) { return new(ProfileVerificationState.Unavailable); }
        if (!InWindow(fact.Metadata.CollectedAtUtc, start, finish) || fact.LimitationCode == StaleLimitation)
        {
            return new(ProfileVerificationState.Stale);
        }

        return fact is { Value: { } value, LimitationCode: null } && fact.Unit == expectedUnit
            ? new(ProfileVerificationState.Passed, value) : new(ProfileVerificationState.Unavailable);
    }

    private static ProfileVerificationState TokenCheck(Fact fact, string expected) => fact.Value switch
    {
        { Kind: EvidenceValueKind.Enum, StringValue: { } value } =>
            value == expected ? ProfileVerificationState.Passed : ProfileVerificationState.Failed,
        _ => fact.State == ProfileVerificationState.Passed ? ProfileVerificationState.Unavailable : fact.State,
    };

    private static ProfileVerificationState NumberCheck(Fact fact, bool valid) =>
        fact.State != ProfileVerificationState.Passed ? fact.State :
        valid ? ProfileVerificationState.Passed : ProfileVerificationState.Failed;

    private static ProfileVerificationState RuleState(PolicyRuleOutcome outcome) => outcome.State switch
    {
        PolicyRuleState.Met => ProfileVerificationState.Passed,
        PolicyRuleState.Unmet => ProfileVerificationState.Failed,
        PolicyRuleState.Unavailable when outcome.Reason == PolicyRuleReason.EvidenceStale => ProfileVerificationState.Stale,
        _ => ProfileVerificationState.Unavailable,
    };

    private static ProfileVerificationState Worse(ProfileVerificationState first, ProfileVerificationState second)
    {
        if (first == ProfileVerificationState.Failed || second == ProfileVerificationState.Failed)
        { return ProfileVerificationState.Failed; }
        if (first == ProfileVerificationState.Stale || second == ProfileVerificationState.Stale)
        { return ProfileVerificationState.Stale; }
        return first == ProfileVerificationState.Unavailable || second == ProfileVerificationState.Unavailable
            ? ProfileVerificationState.Unavailable : ProfileVerificationState.Passed;
    }

    private static ProfileVerificationState AnyAvailable(ProfileVerificationState first, ProfileVerificationState second)
    {
        if (first == ProfileVerificationState.Passed || second == ProfileVerificationState.Passed)
        { return ProfileVerificationState.Passed; }
        if (first == ProfileVerificationState.Stale || second == ProfileVerificationState.Stale)
        { return ProfileVerificationState.Stale; }
        return first == ProfileVerificationState.Unavailable || second == ProfileVerificationState.Unavailable
            ? ProfileVerificationState.Unavailable : ProfileVerificationState.Failed;
    }

    private static ProfileVerificationState PhysicalStorageState(ILookup<string, Observation> raw,
        DateTimeOffset start, DateTimeOffset finish)
    {
        var count = Read(raw["storage.physical_disk_count"].Where(fact =>
            IsOwned(fact, StorageProvider, PhysicalDiskSource)), start, finish, "count");
        if (count.Value?.IntegerValue is not (>= 1 and <= 4 and var diskCount))
        {
            return count.State == ProfileVerificationState.Passed ? ProfileVerificationState.Unavailable : count.State;
        }

        var state = ProfileVerificationState.Passed;
        for (var index = 0; index < diskCount; index++)
        {
            var source = PhysicalDiskSource + ".0" + (char)('0' + index);
            var observed = Read(raw["storage.physical_disk.reported_health"].Where(fact =>
                IsOwned(fact, StorageProvider, source)), start, finish, null);
            state = Worse(state, TokenCheck(observed, "healthy"));
        }

        return state;
    }

    private static UpdateResult EvaluateUpdates(ProfileUpdateCheck? update, DateTimeOffset start, DateTimeOffset finish)
    {
        if (update?.Scan is not { } scan) { return new(ProfileVerificationState.Unavailable); }
        if (!InWindow(update.ObservedAtUtc, start, finish)) { return new(ProfileVerificationState.Stale); }
        if (scan.Status != WindowsUpdateStatus.Succeeded) { return new(ProfileVerificationState.Unavailable); }

        var disposition = WindowsUpdatePolicy.Evaluate(scan, completedPasses: 0);
        var state = disposition switch
        {
            WindowsUpdateDisposition.Current when !update.RecoveryPending => ProfileVerificationState.Passed,
            WindowsUpdateDisposition.ApprovalRequired or WindowsUpdateDisposition.RestartRequired =>
                ProfileVerificationState.Failed,
            _ => ProfileVerificationState.Unavailable,
        };
        return new(state, scan.Updates.Count, scan.Updates.Count(WindowsUpdatePolicy.IsSelectable), disposition,
            scan.RebootRequired, update.RecoveryPending);
    }

    private static ProfileVerificationState RestartState(Fact restart, UpdateResult update)
    {
        if (update.RebootRequired == true || restart.Value?.StringValue == "pending_indicated")
        {
            return ProfileVerificationState.Failed;
        }

        if (restart.State == ProfileVerificationState.Stale || update.State == ProfileVerificationState.Stale)
        {
            return ProfileVerificationState.Stale;
        }

        return update.RebootRequired == false && restart.Value?.StringValue == "clear"
            ? ProfileVerificationState.Passed : ProfileVerificationState.Unavailable;
    }

    private sealed record Fact(ProfileVerificationState State, EvidenceValue? Value = null);

    private sealed record UpdateResult(ProfileVerificationState State, int? OfferCount = null,
        int? EligibleCount = null, WindowsUpdateDisposition? Disposition = null, bool? RebootRequired = null,
        bool RecoveryPending = false);
}
