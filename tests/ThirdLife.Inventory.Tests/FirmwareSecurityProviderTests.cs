using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class FirmwareSecurityProviderTests(ITestOutputHelper output)
{
    private static SystemInventorySourceValue<T> Available<T>(T value) where T : notnull => SystemInventorySourceValue<T>.Available(value);
    private static readonly IReadOnlyList<uint> HealthyProperties = Array.AsReadOnly(new uint[] { 1, 2 });
    private static readonly FirmwareCollectionInfo Collected = new(FirmwareCollectionStatus.Collected);
    private static FirmwareSecuritySourceSnapshot Healthy() => new(
        new(Available(2U), Available(1U), Available((byte)0)),
        new(Available<IReadOnlyList<uint>>(HealthyProperties)),
        new(new(Collected, 0, new(1, 2, 3, 0)),
            new(Collected, true, Available("2.0, 0, 1.59"), Available("7.2.1.0"), Available(0x49465800U),
                Available(true), Available(true), Available(true),
                new(Collected, Available(0U), Available(0U), Available(true)))));

    private static FirmwareSecurityProvider Provider(FirmwareSecuritySourceSnapshot snapshot) =>
        new(new FakeSource(_ => snapshot), ProviderEvidenceOrigin.SyntheticFixture);
    private static NormalizedEvidence Fact(ProviderReadResult result, string key) => Assert.Single(result.Evidence, item => item.EvidenceKey.Value == key);
    private static void Unknown(ProviderReadResult result, string key, ProviderLimitation? limitation = null)
    {
        var fact = Fact(result, key);
        Assert.Null(fact.Value);
        Assert.Equal(ValueAvailability.Unknown, fact.ValueAvailability);
        if (limitation.HasValue) { Assert.Equal(limitation, fact.Limitation); }
    }

    [Fact]
    public async Task EnabledFactsKeepDistinctOwnersAndRawTpmDetails()
    {
        var provider = Provider(Healthy());
        Assert.Empty(Assert.Single(typeof(FirmwareSecurityProvider).GetConstructors()).GetParameters());
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(TimeSpan.FromSeconds(15), provider.Descriptor.Timeout);
        Assert.Equal(38, provider.Descriptor.EvidenceDefinitions.Count);
        Assert.Equal(38, provider.Descriptor.EvidenceDefinitions.Select(static field => field.EvidenceKey.Value).Distinct().Count());
        var read = await provider.ObserveAsync();
        Assert.Equal(ProviderReadStatus.Collected, read.Status);
        Assert.Equal("uefi", Fact(read, "firmware.mode").Value!.StringValue);
        Assert.True(Fact(read, "secure_boot.capable").Value!.BooleanValue);
        Assert.True(Fact(read, "secure_boot.enabled").Value!.BooleanValue);
        Assert.NotEqual(Fact(read, "secure_boot.capable").SourceReference, Fact(read, "secure_boot.enabled").SourceReference);
        Assert.Equal("2.0", Fact(read, "tpm.version").Value!.StringValue);
        Assert.True(Fact(read, "tpm.present").Value!.BooleanValue);
        Assert.True(Fact(read, "tpm.ready").Value!.BooleanValue);
        Assert.Equal("2.0, 0, 1.59", Fact(read, "tpm.wmi.spec_version").Value!.StringValue);
        Assert.Equal(0x49465800L, Fact(read, "tpm.wmi.manufacturer_id").Value!.IntegerValue);
        Unknown(read, "ownership.firmware_lock_indicator");
        Assert.DoesNotContain(read.Evidence, static fact => fact.EvidenceKey.Value == "ownership.lock_state");
        var run = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Completed, run.Outcome);
        Assert.All(run.Observations, static fact =>
        {
            Assert.Equal(ProvenanceKind.SyntheticFixture, fact.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, fact.Metadata.PrivacyClassification);
        });
    }

    [Fact]
    public async Task DisabledStateDoesNotMeanIncapableAndInitialFlagsDoNotMeanReady()
    {
        var snapshot = Healthy();
        snapshot = snapshot with
        {
            Platform = snapshot.Platform with { SecureBootRegistryValue = Available(0U) },
            Tpm = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { Readiness = snapshot.Tpm.Wmi.Readiness with { IsReady = Available(false), Information = Available(uint.MaxValue) } } },
        };
        var read = await Provider(snapshot).ObserveAsync();
        Assert.True(Fact(read, "secure_boot.capable").Value!.BooleanValue);
        Assert.False(Fact(read, "secure_boot.enabled").Value!.BooleanValue);
        Assert.True(Fact(read, "tpm.wmi.owned_initial").Value!.BooleanValue);
        Assert.False(Fact(read, "tpm.ready").Value!.BooleanValue);
        Assert.Equal(uint.MaxValue, Fact(read, "tpm.readiness.information").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(2, ProviderReadStatus.Unavailable)]
    [InlineData(3, ProviderReadStatus.AccessDenied)]
    [InlineData(4, ProviderReadStatus.InvalidData)]
    public async Task MissingDeniedMalformedCapabilityCannotPass(int status, ProviderReadStatus expected)
    {
        var snapshot = Healthy();
        var value = status == 3 ? SystemInventorySourceValue<IReadOnlyList<uint>>.AccessDenied() : status == 4 ?
            SystemInventorySourceValue<IReadOnlyList<uint>>.InvalidData() : SystemInventorySourceValue<IReadOnlyList<uint>>.Unavailable();
        var read = await Provider(snapshot with { DeviceGuard = new(value, 0x80070005) }).ObserveAsync();
        Assert.Equal(expected, read.Status);
        Unknown(read, "secure_boot.capable");
        Assert.True(Fact(read, "secure_boot.enabled").Value!.BooleanValue);
        Assert.Equal(0x80070005L, Fact(read, "secure_boot.device_guard_error_code").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(new uint[] { 1, 3 }, false)]
    [InlineData(new uint[0], false)]
    [InlineData(new uint[] { 2, 2 }, true)]
    [InlineData(new uint[] { 0, 2 }, true)]
    [InlineData(new uint[] { 2, 99 }, true)]
    public async Task UncertainOrMalformedCapabilityIsUnknown(uint[] codes, bool malformed)
    {
        var snapshot = Healthy();
        var read = await Provider(snapshot with { DeviceGuard = new(Available<IReadOnlyList<uint>>(codes)) }).ObserveAsync();
        Unknown(read, "secure_boot.capable", malformed ? ProviderLimitation.SourceValueMalformed : ProviderLimitation.SourceValueMissing);
        if (codes.Length != 0) { Assert.NotNull(Fact(read, "secure_boot.available_properties").Value); }
        Assert.Equal(malformed ? ProviderReadStatus.InvalidData : ProviderReadStatus.Collected, read.Status);
    }

    [Theory]
    [InlineData(1U, new uint[] { 2 })]
    [InlineData(2U, new uint[] { 0 })]
    public async Task ContradictorySecureBootSourcesPreserveRawEvidence(uint firmware, uint[] properties)
    {
        var snapshot = Healthy();
        var read = await Provider(snapshot with { Platform = snapshot.Platform with { FirmwareType = Available(firmware) }, DeviceGuard = new(Available<IReadOnlyList<uint>>(properties)) }).ObserveAsync();
        Unknown(read, "secure_boot.enabled", ProviderLimitation.SourceValuesConflict);
        Assert.Equal(1, Fact(read, "secure_boot.registry_value").Value!.IntegerValue);
        if (properties[0] == 0) { Unknown(read, "secure_boot.capable", ProviderLimitation.SourceValuesConflict); }
    }

    [Theory]
    [InlineData(0x40, "enabled", "disabled", true)]
    [InlineData(0x04, "disabled", "enabled", true)]
    [InlineData(0x00, "disabled", "disabled", false)]
    [InlineData(0xAA, "not_implemented", "not_implemented", false)]
    [InlineData(0xFF, null, null, false)]
    public async Task SyntheticFirmwareLocksAreIndicatorsWithoutOwnershipClearance(int settings, string? powerOn, string? administrator, bool locked)
    {
        var snapshot = Healthy();
        var read = await Provider(snapshot with { Platform = snapshot.Platform with { HardwareSecuritySettings = Available((byte)settings) } }).ObserveAsync();
        Assert.Equal(powerOn, Fact(read, "firmware.password.power_on_state").Value?.StringValue);
        Assert.Equal(administrator, Fact(read, "firmware.password.administrator_state").Value?.StringValue);
        var indicator = Fact(read, "ownership.firmware_lock_indicator");
        Assert.Equal(locked ? "present" : null, indicator.Value?.StringValue);
        if (locked) { Assert.Equal(EvidenceClassification.Inferred, indicator.EvidenceClassification); }
        Assert.DoesNotContain(read.Evidence, static fact => fact.EvidenceKey.Value == "ownership.lock_state");
    }

    [Theory]
    [InlineData(1U, "1.2")]
    [InlineData(2U, "2.0")]
    [InlineData(99U, null)]
    public async Task TbsVersionIsTypedAndRetainsUnknownRawCode(uint version, string? expected)
    {
        var snapshot = Healthy();
        var wmi = snapshot.Tpm.Wmi with { SpecVersion = SystemInventorySourceValue<string>.Unavailable() };
        var read = await Provider(snapshot with { Tpm = new(new(Collected, 0, new(1, version, 3, 0)), wmi) }).ObserveAsync();
        Assert.Equal(expected, Fact(read, "tpm.version").Value?.StringValue);
        Assert.Equal(version, Fact(read, "tpm.tbs.version_code").Value!.IntegerValue);
        if (expected is null) { Unknown(read, "tpm.present"); }
    }

    [Fact]
    public async Task TpmVersionAndPresenceContradictionsCannotBeOverwritten()
    {
        var snapshot = Healthy();
        var mismatch = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { SpecVersion = Available("1.2, 2, 3") } };
        var read = await Provider(snapshot with { Tpm = mismatch }).ObserveAsync();
        Unknown(read, "tpm.version", ProviderLimitation.SourceValuesConflict);
        Assert.Equal(2, Fact(read, "tpm.tbs.version_code").Value!.IntegerValue);
        var absent = new TpmDeviceInfoSnapshot(new(FirmwareCollectionStatus.NotFound), 0x8028400F);
        read = await Provider(snapshot with { Tpm = snapshot.Tpm with { Device = absent } }).ObserveAsync();
        Unknown(read, "tpm.present", ProviderLimitation.SourceValuesConflict);
        Unknown(read, "tpm.ready", ProviderLimitation.SourceValuesConflict);
    }

    [Fact]
    public async Task NoCompatibleTpmIsDistinctFromDeniedTbsAndMissingWmiInstance()
    {
        var snapshot = Healthy();
        var missing = WindowsTpmSource.EmptyWmi(new(FirmwareCollectionStatus.NotFound)) with { InstancePresent = false };
        var read = await Provider(snapshot with { Tpm = new(new(new(FirmwareCollectionStatus.NotFound), 0x8028400F), missing) }).ObserveAsync();
        Assert.False(Fact(read, "tpm.present").Value!.BooleanValue);
        Assert.False(Fact(read, "tpm.wmi.instance_present").Value!.BooleanValue);
        Unknown(read, "tpm.ready");
        read = await Provider(snapshot with { Tpm = new(new(new(FirmwareCollectionStatus.AccessDenied, 5), 5), missing) }).ObserveAsync();
        Assert.Equal(ProviderReadStatus.AccessDenied, read.Status);
        Unknown(read, "tpm.present");
    }

    [Theory]
    [InlineData(0U, true)]
    [InlineData(0xC0U, true)]
    [InlineData(0x20U, false)]
    [InlineData(0x200000U, false)]
    [InlineData(0x80000000U, false)]
    public async Task PositiveReadinessWithProvisioningNeedsOrUnknownBitsRequiresReview(uint information, bool usable)
    {
        var snapshot = Healthy();
        var readiness = snapshot.Tpm.Wmi.Readiness with { Information = Available(information) };
        var read = await Provider(snapshot with { Tpm = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { Readiness = readiness } } }).ObserveAsync();
        Assert.True(Fact(read, "tpm.readiness.is_ready").Value!.BooleanValue);
        Assert.Equal(information, Fact(read, "tpm.readiness.information").Value!.IntegerValue);
        if (usable) { Assert.True(Fact(read, "tpm.ready").Value!.BooleanValue); }
        else { Unknown(read, "tpm.ready", ProviderLimitation.SourceValuesConflict); }
    }

    [Theory]
    [InlineData("2.0,,,")]
    [InlineData("2.0, , 0")]
    [InlineData("2.0, 0, 1..59")]
    [InlineData("3.0, 0, 0")]
    public async Task UnrecognizedTpmSpecificationCannotConfirmVersion(string spec)
    {
        var snapshot = Healthy();
        var read = await Provider(snapshot with { Tpm = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { SpecVersion = Available(spec) } } }).ObserveAsync();
        Unknown(read, "tpm.version", ProviderLimitation.SourceValueMalformed);
        Assert.Equal(spec, Fact(read, "tpm.wmi.spec_version").Value!.StringValue);
    }

    [Theory]
    [InlineData(3, 0U, ProviderReadStatus.AccessDenied)]
    [InlineData(1, 1U, ProviderReadStatus.InvalidData)]
    public async Task ReadinessDeniedOrUnsuccessfulNeverUsesInitialFlags(int state, uint code, ProviderReadStatus expected)
    {
        var snapshot = Healthy();
        var info = state == 3 ? new FirmwareCollectionInfo(FirmwareCollectionStatus.AccessDenied, 0x80070005) : Collected;
        var readiness = snapshot.Tpm.Wmi.Readiness with { CollectionInfo = info, ReturnValue = Available(code) };
        var read = await Provider(snapshot with { Tpm = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { Readiness = readiness } } }).ObserveAsync();
        Assert.Equal(expected, read.Status);
        Unknown(read, "tpm.ready");
        Assert.Equal(code, Fact(read, "tpm.readiness.return_value").Value!.IntegerValue);
    }

    [Theory]
    [InlineData("C:\\Users\\private")]
    [InlineData("2.0\u0000secret")]
    [InlineData("<script>2</script>")]
    public async Task ArbitraryVersionTextNeverLeavesProvider(string raw)
    {
        var snapshot = Healthy();
        var read = await Provider(snapshot with { Tpm = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { SpecVersion = Available(raw) } } }).ObserveAsync();
        Assert.Equal(ProviderReadStatus.InvalidData, read.Status);
        Unknown(read, "tpm.wmi.spec_version", ProviderLimitation.SourceValueMalformed);
        Assert.DoesNotContain(raw, JsonSerializer.Serialize(read), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupFailureWinsCancellationAndPermanentlyBlocksThisInstance()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var snapshot = Healthy();
        var provider = new FirmwareSecurityProvider(new FakeSource(_ =>
        {
            calls++;
            cancellation.Cancel();
            return snapshot with { Tpm = snapshot.Tpm with { Wmi = snapshot.Tpm.Wmi with { CleanupComplete = false } } };
        }), ProviderEvidenceOrigin.SyntheticFixture);
        var read = await provider.ObserveAsync(cancellation.Token);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, read.Status);
        Assert.Empty(read.Evidence);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, (await provider.ObserveAsync()).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancelledNativeWorkerRetainsAdmissionUntilCleanupAndCanThenRetry()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new FirmwareSecurityProvider(new FakeSource(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            return Healthy();
        }), ProviderEvidenceOrigin.SyntheticFixture);
        var pending = provider.ObserveAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            for (var index = 0; index < 16; index++) { Assert.Equal(ProviderReadStatus.Unavailable, (await provider.ObserveAsync()).Status); }
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineReadOnlySmokePreservesActualStatesAndBoundedEvidence()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new InventoryProviderRunner().RunAsync(new FirmwareSecurityProvider(), cancellation.Token);
        Assert.NotEqual(ProviderRunOutcome.Failed, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.ContractInvalid, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.CleanupIncomplete, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.TimedOut, result.Outcome);
        Assert.InRange(result.Observations.Count, 38, 39);
        Assert.All(result.Observations, static fact =>
        {
            Assert.Equal(PrivacyClassification.WorkshopRestricted, fact.Metadata.PrivacyClassification);
            Assert.Equal(fact.Metadata.ValueAvailability == ValueAvailability.Available, fact.Value is not null);
        });
        string[] disclosed = ["firmware.mode", "secure_boot.capable", "secure_boot.enabled", "tpm.present", "tpm.version", "tpm.ready", "ownership.firmware_lock_indicator", "tpm.wmi.native_error_code", "tpm.readiness.native_error_code"];
        process.Refresh();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            task = "TL-0110",
            elevated = false,
            outcome = result.Outcome.ToString(),
            observations = result.Observations.Count,
            durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            processCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
            processAllocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
            processWorkingSetBytes = process.WorkingSet64,
            states = result.Observations.Where(fact => disclosed.Contains(fact.EvidenceKey.Value)).Select(static fact => new
            { key = fact.EvidenceKey.Value, value = fact.Value, availability = fact.Metadata.ValueAvailability.ToString(), limitation = fact.LimitationCode }),
        }));
    }

    private sealed class FakeSource(Func<CancellationToken, FirmwareSecuritySourceSnapshot> read) : IFirmwareSecuritySource
    {
        public FirmwareSecuritySourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
