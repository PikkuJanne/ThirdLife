using System.Security.Principal;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using SourceValue = ThirdLife.Inventory.Windows.SystemInventorySourceValue<string>;

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

    [Fact]
    public async Task FailedCleanupPreventsFurtherNativeReads()
    {
        var calls = 0;
        var provider = new SecurityBaselineProvider((_, _) =>
        {
            calls++;
            throw new SecuritySourceCleanupException();
        }, ProviderEvidenceOrigin.SyntheticFixture);
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
        Assert.InRange(result.Observations.Count, 13, 14);
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
            if (item.Value is not null) { Assert.Contains(item.Value.StringValue, allowed); }
        }
    }

    private static ValueTask<ProviderRunResult> Run(Func<SecurityCheck, SourceValue> read) =>
        new InventoryProviderRunner().RunAsync(new SecurityBaselineProvider((check, _) => read(check), ProviderEvidenceOrigin.SyntheticFixture));

    private static Observation Fact(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key);
}
