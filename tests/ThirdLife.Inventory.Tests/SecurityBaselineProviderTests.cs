using System.Runtime.InteropServices;
using System.Security.Principal;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using SourceValue = ThirdLife.Inventory.Windows.SystemInventorySourceValue<string>;
using SyncTime = ThirdLife.Inventory.Windows.SystemInventorySourceValue<long>;

namespace ThirdLife.Inventory.Tests;

public sealed class SecurityBaselineProviderTests
{
    [Theory]
    [InlineData(0, 0, "good")]
    [InlineData(0, 1, "not_monitored")]
    [InlineData(0, 2, "poor")]
    [InlineData(0, 3, "snoozed")]
    [InlineData(1, 2, null)] // Security Center stopped, even when it supplies a poor out-value.
    [InlineData(unchecked((int)0x80070005), 0, null)]
    [InlineData(0, 4, null)]
    public void WscUsesOnlySuccessfulDefinedResults(int status, int health, string? expected) =>
        Assert.Equal(expected, WindowsSecurityBaselineSource.MapHealth(status, health).Value);

    [Theory]
    [InlineData("DomainJoined", "domain_joined")]
    [InlineData("MdmRegistered", "registered")]
    [InlineData("CloudJoin", "device_joined")]
    [InlineData("CloudJoin", "workplace_joined")]
    public async Task AnyManagementIndicatorSurvivesAnUnavailableOtherCheck(string name, string token)
    {
        var check = Enum.Parse<SecurityCheck>(name);
        var result = await Run(checkedItem => checkedItem == check ? SourceValue.Available(token) : SourceValue.AccessDenied());
        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        var management = Fact(result, "ownership.lock_state");
        Assert.Equal("management_indicated", management.Value!.StringValue);
        Assert.Equal(EvidenceClassification.Inferred, management.Metadata.EvidenceClassification);
        Assert.Equal(ProvenanceKind.SyntheticFixture, management.Metadata.Provenance.Kind);
    }

    [Theory]
    [InlineData("enabled", "good", "enabled", null)]
    [InlineData("disabled", "poor", "requires_review", null)]
    [InlineData("disabled", "good", null, "provider_values_conflict")]
    [InlineData("enabled", "not_monitored", null, "provider_value_missing")]
    public async Task UacRequiresAgreementAndAbsenceOfJoinNeverClearsOwnership(
        string configured, string health, string? expected, string? limitation)
    {
        var result = await Run(check => SourceValue.Available(check switch
        {
            SecurityCheck.DomainJoined => "workgroup",
            SecurityCheck.MdmRegistered => "not_registered",
            SecurityCheck.CloudJoin => "no_join_reported",
            SecurityCheck.UacHealth => health,
            SecurityCheck.UacEnabled => configured,
            _ => "good",
        }));
        var uac = Fact(result, "security.uac_state");
        Assert.Equal(expected, uac.Value?.StringValue);
        Assert.Equal(limitation, uac.LimitationCode);
        Assert.Null(Fact(result, "ownership.lock_state").Value);
    }

    [Theory]
    [InlineData(true, true, true, false, "real_time_active_signatures_current")]
    [InlineData(true, true, false, false, "requires_review")]
    [InlineData(true, true, true, true, "requires_review")]
    [InlineData(false, true, true, false, "requires_review")]
    [InlineData(true, false, true, false, "requires_review")]
    public void DefenderHealthRequiresAllFourSourceValues(bool service, bool antivirus, bool realtime, bool stale, string expected)
    {
        Assert.Equal(expected, SecurityWmiSource.DefenderState(Flag(service), Flag(antivirus), Flag(realtime), Flag(stale)).Value);
        Assert.Null(SecurityWmiSource.DefenderState(Flag(service), Flag(antivirus), Flag(realtime),
            SystemInventorySourceValue<bool>.AccessDenied()).Value);

        static SystemInventorySourceValue<bool> Flag(bool value) => SystemInventorySourceValue<bool>.Available(value);
    }

    [Theory]
    [InlineData("2026-11-25T12:34:56.123Z", 0, 0, "Available")]
    [InlineData("2026-11-25T12:34:56Z", 0, 0, "Available")]
    [InlineData("2026-11-25T12:34:56.123Z", 0, 1, "Unavailable")]
    [InlineData(null, 0, 0, "InvalidData")]
    [InlineData("2026-11-25", 0, 0, "InvalidData")]
    [InlineData("2026-11-25T12:34:56", 0, 0, "InvalidData")]
    [InlineData("1601-01-01T00:00:00.000Z", 0, 0, "InvalidData")]
    [InlineData("2026-11-27T00:00:01.000Z", 0, 0, "InvalidData")]
    [InlineData("2026-11-25T12:34:56.123Z", 2, 0, "InvalidData")]
    public void TimeStatusRequiresASuccessfulUtcTimestampAtOrBeforeTheRecordAndCollection(
        string? timestamp, int recordDays, int lastError, string expected)
    {
        var collected = new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);
        var recorded = collected.AddDays(recordDays - 1);
        var result = WindowsTimeSyncSource.ParseStatus(timestamp, recorded.ToFileTime(), lastError, collected);
        Assert.Equal(expected, result.Status.ToString());
        if (result.Status == SystemInventorySourceStatus.Available)
        {
            Assert.Equal(new DateTimeOffset(2026, 11, 25, 12, 34, 56, TimeSpan.Zero).ToUnixTimeSeconds(), result.GetRequiredValue());
        }
    }

    [Theory]
    [InlineData(3, 98, 1, 0, "Available")]
    [InlineData(3, 98, 1, 1, "Unavailable")]
    [InlineData(3, 98, 0, 0, "Unavailable")]
    [InlineData(3, 98, 10, 0, "InvalidData")]
    [InlineData(3, 96, 1, 0, "InvalidData")] // Terminator lies beyond the returned bytes.
    [InlineData(2, 98, 1, 0, "InvalidData")]
    public void TimeEventDecodesThreeBoundedNativeVariants(int count, int used, int timestampType, int error, string expected)
    {
        const string timestamp = "2026-11-25T12:34:56.123Z";
        var record = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            // Three 16-byte EVT_VARIANTs followed by the UTF-16 timestamp.
            var characters = (timestamp + '\0').ToCharArray();
            Marshal.Copy(characters, 0, buffer + 48, characters.Length);
            Marshal.WriteInt64(buffer, record.ToFileTime());
            Marshal.WriteInt32(buffer, 12, 17);
            Marshal.WriteIntPtr(buffer, 16, buffer + 48);
            Marshal.WriteInt32(buffer, 28, timestampType);
            Marshal.WriteInt64(buffer, 32, error);
            Marshal.WriteInt32(buffer, 44, 10);
            var result = WindowsTimeSyncSource.DecodeFields(buffer, used, count, record.AddHours(1));
            Assert.Equal(expected, result.Status.ToString());
            if (result.Status == SystemInventorySourceStatus.Available)
            {
                Assert.Equal(new DateTimeOffset(2026, 11, 25, 12, 34, 56, TimeSpan.Zero).ToUnixTimeSeconds(), result.GetRequiredValue());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Theory]
    [InlineData(5, "AccessDenied")]
    [InlineData(122, "InvalidData")]
    [InlineData(259, "Unavailable")] // No matching event.
    [InlineData(1460, "Unavailable")] // Timed out.
    public void TimeEventErrorsPreserveUnavailableAndMalformedStates(int error, string expected) =>
        Assert.Equal(expected, WindowsTimeSyncSource.MapFailure(error).Status.ToString());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncEvidenceComesOnlyFromTheWindowsTimeStatus(bool hasTimestamp)
    {
        const long timestamp = 1795649312;
        var result = await Run(_ => SourceValue.Available("enabled"),
            _ => hasTimestamp ? SyncTime.Available(timestamp) : SyncTime.Unavailable());
        var sync = Fact(result, "security.last_successful_sync_utc");
        Assert.Equal("security.w32time.last_successful_sync", sync.Metadata.Provenance.SourceReference);
        Assert.Equal("unix_seconds", sync.Unit);
        Assert.Equal(hasTimestamp ? EvidenceClassification.Observed : EvidenceClassification.NotAvailable, sync.Metadata.EvidenceClassification);
        Assert.Equal(hasTimestamp ? timestamp : (long?)null, sync.Value?.IntegerValue);
    }

    [Fact]
    public async Task FailedCleanupPreventsFurtherNativeReads()
    {
        var calls = 0;
        var provider = new SecurityBaselineProvider((_, _) =>
        {
            calls++;
            throw new SecuritySourceCleanupException();
        }, _ => SyncTime.Unavailable(), ProviderEvidenceOrigin.SyntheticFixture);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(ProviderRunOutcome.CleanupIncomplete, (await new InventoryProviderRunner().RunAsync(provider)).Outcome);
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineReadsClosedStatesWithoutElevationOrIdentity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        var result = await new InventoryProviderRunner().RunAsync(new SecurityBaselineProvider());
        Assert.Contains(result.Outcome, new[] { ProviderRunOutcome.Completed, ProviderRunOutcome.AccessDenied, ProviderRunOutcome.Unavailable });
        Assert.InRange(result.Observations.Count, 14, 15);
        string[] allowed =
        [
            "good", "not_monitored", "snoozed", "poor", "enabled", "disabled", "allowed", "denied", "requires_review",
            "domain_joined", "workgroup", "unjoined", "registered", "not_registered", "no_join_reported",
            "device_joined", "workplace_joined", "management_indicated", "real_time_active_signatures_current",
            "protection_off", "protection_on",
        ];
        foreach (var item in result.Observations)
        {
            Assert.Equal(ProvenanceKind.ProviderObservation, item.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, item.Metadata.PrivacyClassification);
            if (item.EvidenceKey.Value == "security.last_successful_sync_utc")
            {
                if (item.Value is not null) { Assert.InRange(item.Value.IntegerValue!.Value, 1L, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
            }
            else if (item.Value is not null) { Assert.Contains(item.Value.StringValue, allowed); }
        }
    }

    private static ValueTask<ProviderRunResult> Run(Func<SecurityCheck, SourceValue> read,
        Func<CancellationToken, SyncTime>? readTime = null) =>
        new InventoryProviderRunner().RunAsync(new SecurityBaselineProvider((check, _) => read(check),
            readTime ?? (_ => SyncTime.Unavailable()), ProviderEvidenceOrigin.SyntheticFixture));

    private static Observation Fact(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key);
}
