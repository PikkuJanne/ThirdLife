using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Verification.Tests;

public sealed class ProfileVerificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Observed = Start.AddSeconds(1);
    private static readonly DateTimeOffset Finish = Start.AddSeconds(2);
    private static readonly CatalogDocument Catalog = CatalogSchema.Parse(ReadFixture("catalog"), Start);
    private static readonly ProfileDocument Basic = ProfileSchema.Parse(ReadFixture("basic"), Catalog);
    private static readonly ProfileDocument JobSeeker = ProfileSchema.Parse(ReadFixture("job-seeker"), Catalog);
    private static readonly PolicyDocument Policy = PolicySchema.Parse(ReadFixture("community-laptop-policy"));
    private static readonly Guid PackageAction = Guid.NewGuid();
    private const long EnoughSpace = 64L * 1024 * 1024 * 1024;

    [Fact]
    public void RechecksChangedMachineAndPackageStateInsteadOfRetainingEarlierSuccess()
    {
        var previous = Evaluate(EnoughSpace, "good", "clear", new(new(WindowsUpdateStatus.Succeeded, [], false), Observed),
            new ProfilePackageCheck("document_editing", "8.9.9.1", "8.9.9.1", ProfilePackageStatus.ExactVersion, Observed, PackageAction));
        var changed = Evaluate(5, "poor", "pending_indicated", new(new(WindowsUpdateStatus.Succeeded, [], true), Observed),
            new ProfilePackageCheck("document_editing", "8.9.9.1", null, ProfilePackageStatus.Absent, Observed, PackageAction));

        Assert.Equal(ProfileVerificationState.Passed, Check(previous, ProfileCheckKind.Firewall));
        Assert.Equal(ProfileVerificationState.Passed, Check(previous, ProfileCheckKind.SystemVolumeFreeSpace));
        Assert.Equal(ProfileVerificationState.Passed, Check(previous, ProfileCheckKind.PendingRestart));
        Assert.Equal(ProfileVerificationState.Passed, Capability(previous, "document_editing").State);
        Assert.Equal(ProfileVerificationState.Failed, Check(changed, ProfileCheckKind.Firewall));
        Assert.Equal(ProfileVerificationState.Failed, Check(changed, ProfileCheckKind.SystemVolumeFreeSpace));
        Assert.Equal(ProfileVerificationState.Failed, Check(changed, ProfileCheckKind.PendingRestart));
        Assert.Equal(ProfileVerificationState.Failed, Check(changed, ProfileCheckKind.WindowsUpdate));
        var package = Capability(changed, "document_editing");
        Assert.Equal(ProfileVerificationState.Failed, package.State);
        Assert.Equal(ProfilePackageStatus.Absent, package.PackageStatus);
        Assert.Equal("8.9.9.1", package.ExpectedVersion);
        Assert.Null(package.ObservedVersion);
        Assert.Equal(PackageAction, package.ActionId);
        Assert.Equal(5, changed.FreeBytes);
        Assert.False(changed.ProfileChecksPassed);
    }

    [Fact]
    public void RejectsPrePassFactsEvenWhenTheyWouldOtherwiseSatisfyChecks()
    {
        var old = Start.AddSeconds(-1);
        var result = ProfileVerification.Evaluate(Start, Finish, Basic,
            [Build(old), Volume(EnoughSpace, old)],
            [Fact("security.firewall_health", EvidenceValue.FromEnum("good"), old),
             Fact("storage.system.free_bytes", EvidenceValue.FromInteger(EnoughSpace), old, unit: "bytes")],
            Policy, new(new(WindowsUpdateStatus.Succeeded, [], false), old),
            [new("document_editing", "8.9.9.1", "8.9.9.1", ProfilePackageStatus.ExactVersion, old, PackageAction)]);

        Assert.Equal(ProfileVerificationState.Stale, Check(result, ProfileCheckKind.WindowsBuild));
        Assert.Equal(ProfileVerificationState.Stale, Check(result, ProfileCheckKind.SystemVolumeFreeSpace));
        Assert.Equal(ProfileVerificationState.Stale, Check(result, ProfileCheckKind.Firewall));
        Assert.Equal(ProfileVerificationState.Stale, Check(result, ProfileCheckKind.WindowsUpdate));
        Assert.Null(result.WindowsBuild);
        Assert.Null(result.FreeBytes);
        Assert.Null(result.UpdateOfferCount);
        var package = Capability(result, "document_editing");
        Assert.Equal(ProfileVerificationState.Stale, package.State);
        Assert.Null(package.ObservedVersion);
        Assert.Null(package.PackageStatus);
        var rule = Assert.Single(result.PolicyFindings, finding => finding.RuleId.Value == "POL-FIREWALL-001");
        Assert.Equal(PolicyRuleReason.EvidenceStale, rule.Reason);
        Assert.False(result.ProfileChecksPassed);
    }

    [Fact]
    public void PreservesObservedDowngradeWithoutTreatingItAsTheApprovedVersion()
    {
        var result = Evaluate(EnoughSpace, "good", "clear", null,
            new ProfilePackageCheck("document_editing", "8.9.9.1", "8.9.8", ProfilePackageStatus.Mismatch, Observed, PackageAction));

        var package = Capability(result, "document_editing");
        Assert.Equal(ProfileVerificationState.Failed, package.State);
        Assert.Equal(ProfilePackageStatus.Mismatch, package.PackageStatus);
        Assert.Equal("8.9.9.1", package.ExpectedVersion);
        Assert.Equal("8.9.8", package.ObservedVersion);
        var invalid = new ProfilePackageCheck("document_editing", "8.9.9.1", "8.9.8", ProfilePackageStatus.ExactVersion,
            Observed, PackageAction);
        Assert.Throws<ArgumentException>(() => Evaluate(EnoughSpace, "good", "clear", null, invalid));
    }

    [Fact]
    public void RequiresEveryEssentialCapabilityAndItsActivePolicyChecks()
    {
        var basic = ProfileVerification.Evaluate(Start, Finish, Basic, [], [], Policy, null, []);
        var jobSeeker = ProfileVerification.Evaluate(Start, Finish, JobSeeker, [], [], Policy, null, []);

        Assert.Collection(basic.Capabilities,
            row => Assert.Equal(("web_browsing", true, ProfileVerificationState.Unavailable), (row.CapabilityId, row.Essential, row.State)),
            row => Assert.Equal(("document_editing", true, ProfileVerificationState.Unavailable), (row.CapabilityId, row.Essential, row.State)),
            row => Assert.Equal(("pdf_reading", true, ProfileVerificationState.Unavailable), (row.CapabilityId, row.Essential, row.State)),
            row => Assert.Equal(("video_calling", false, ProfileVerificationState.NotApplicable), (row.CapabilityId, row.Essential, row.State)));
        Assert.True(Capability(jobSeeker, "video_calling").Essential);
        Assert.Equal(ProfileVerificationState.Unavailable, Capability(jobSeeker, "video_calling").State);
        Assert.Equal(ProfileVerificationState.Unavailable, Check(jobSeeker, ProfileCheckKind.Camera));
        Assert.Equal(PolicyRuleState.Inactive, CameraRule(basic).State);
        Assert.Equal(PolicyRuleState.Unavailable, CameraRule(jobSeeker).State);
        Assert.False(jobSeeker.ProfileChecksPassed);

        static ProfilePolicyFinding CameraRule(ProfileVerificationResult row) =>
            Assert.Single(row.PolicyFindings, finding => finding.RuleId.Value == "POL-FUNCTION-006");
    }

    [Fact]
    public void DistinguishesAnUnsupportedOfferEmptyScanAndUnresolvedServicingRecovery()
    {
        var restricted = new WindowsUpdateCandidate(new(Guid.NewGuid(), 1), WindowsUpdateClass.Restricted);
        var excluded = Evaluate(EnoughSpace, "good", null, new(new(WindowsUpdateStatus.Succeeded, [restricted], false), Observed));
        var empty = Evaluate(EnoughSpace, "good", null, new(new(WindowsUpdateStatus.Succeeded, [], false), Observed));
        var recovering = Evaluate(EnoughSpace, "good", null, new(new(WindowsUpdateStatus.Succeeded, [], false), Observed, true));
        var offline = Evaluate(EnoughSpace, "good", null, new(new(WindowsUpdateStatus.Offline, [], null), Observed));

        Assert.Equal(1, excluded.UpdateOfferCount);
        Assert.Equal(0, excluded.EligibleUpdateOfferCount);
        Assert.Equal(WindowsUpdateDisposition.NoEligibleOffers, excluded.UpdateDisposition);
        Assert.Equal(ProfileVerificationState.Unavailable, Check(excluded, ProfileCheckKind.WindowsUpdate));
        Assert.Equal(0, empty.UpdateOfferCount);
        Assert.Equal(WindowsUpdateDisposition.Current, empty.UpdateDisposition);
        Assert.Equal(ProfileVerificationState.Passed, Check(empty, ProfileCheckKind.WindowsUpdate));
        Assert.Equal(ProfileVerificationState.Unavailable, Check(empty, ProfileCheckKind.PendingRestart));
        Assert.True(recovering.UpdateRecoveryPending);
        Assert.Equal(ProfileVerificationState.Unavailable, Check(recovering, ProfileCheckKind.WindowsUpdate));
        Assert.Null(offline.UpdateOfferCount);
        Assert.Null(offline.UpdateDisposition);
        Assert.Equal(ProfileVerificationState.Unavailable, Check(offline, ProfileCheckKind.WindowsUpdate));
    }

    [Fact]
    public void KeepsPhysicalStorageEvidenceSeparateFromUnmappedSystemDiskHealth()
    {
        var result = ProfileVerification.Evaluate(Start, Finish, Basic,
            [Volume(EnoughSpace, Observed),
             Fact("storage.physical_disk_count", EvidenceValue.FromInteger(1), Observed,
                "windows-storage", "storage.msft.physical_disk", "count"),
             Fact("storage.physical_disk.reported_health", EvidenceValue.FromEnum("healthy"), Observed,
                "windows-storage", "storage.msft.physical_disk.00")], [], Policy, null, []);

        Assert.Equal(ProfileVerificationState.Passed, Check(result, ProfileCheckKind.PhysicalStorageEvidence));
        Assert.Equal(ProfileVerificationState.Unavailable, Check(result, ProfileCheckKind.SystemStorageHealth));
        Assert.Equal(PolicyRuleReason.EvidenceMissing,
            Assert.Single(result.PolicyFindings, finding => finding.RuleId.Value == "POL-STORAGE-003").Reason);
    }

    [Fact]
    public void DurableRecordRoundTripsAndRejectsReclassifyingRequiredChecks()
    {
        var result = Evaluate(EnoughSpace, "good", "clear", new(new(WindowsUpdateStatus.Succeeded, [], false), Observed),
            new ProfilePackageCheck("document_editing", "8.9.9.1", "8.9.9.1", ProfilePackageStatus.ExactVersion, Observed, PackageAction));
        var json = JsonSerializer.Serialize(result);

        var restored = JsonSerializer.Deserialize<ProfileVerificationResult>(json);
        Assert.NotNull(restored);
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal(Capability(result, "document_editing"), Capability(restored, "document_editing"));
        var altered = JsonNode.Parse(json)!;
        altered["Checks"]![(int)ProfileCheckKind.PendingRestart - 1]!["Blocking"] = false;
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<ProfileVerificationResult>(altered.ToJsonString()));
    }

    private static ProfileVerificationResult Evaluate(long free, string firewall, string? restart,
        ProfileUpdateCheck? update, params ProfilePackageCheck[] packages)
    {
        var facts = new List<Observation>
        {
            Fact("security.firewall_health", EvidenceValue.FromEnum(firewall), Observed),
            Fact("storage.system.free_bytes", EvidenceValue.FromInteger(free), Observed, unit: "bytes"),
        };
        if (restart is not null)
        {
            facts.Add(Fact("reboot.pending_status", EvidenceValue.FromEnum(restart), Observed));
        }

        return ProfileVerification.Evaluate(Start, Finish, Basic, [Build(Observed), Volume(free, Observed)], facts,
            Policy, update, packages);
    }

    private static ProfileVerificationState Check(ProfileVerificationResult result, ProfileCheckKind kind) =>
        Assert.Single(result.Checks, item => item.Kind == kind).State;

    private static ProfileCapabilityResult Capability(ProfileVerificationResult result, string capability) =>
        Assert.Single(result.Capabilities, item => item.CapabilityId == capability);

    private static Observation Build(DateTimeOffset observed) =>
        Fact("os.build", EvidenceValue.FromInteger(26200), observed, "windows-operating-system", "os.dotnet.environment.version");

    private static Observation Volume(long free, DateTimeOffset observed) =>
        Fact("storage.system_volume.caller_free_bytes", EvidenceValue.FromInteger(free), observed,
            "windows-storage", "storage.kernel32.system_volume", "bytes");

    private static Observation Fact(string key, EvidenceValue value, DateTimeOffset observed, string provider = "fixture",
        string source = "verification-fixture", string? unit = null) =>
        new(new(new EvidenceId($"fact-{Guid.NewGuid():N}"), PrivacyClassification.PublicReference,
            EvidenceClassification.Observed, new(provider), observed, new(ProvenanceKind.SyntheticFixture, source),
            ValueAvailability.Available), new(key), value, unit, limitationCode: null);

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name + ".json"));
}
