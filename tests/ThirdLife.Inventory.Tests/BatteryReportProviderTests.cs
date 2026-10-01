using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class BatteryReportProviderTests(ITestOutputHelper output)
{
    [Fact]
    public void DescriptorIsBoundedStandardUserOfflineAndHasNoCallerCommandOrPath()
    {
        var provider = Create(_ => Snapshot(Pack()));
        Assert.Equal("windows-battery-report", provider.Descriptor.ProviderId.Value);
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(TimeSpan.FromSeconds(30), provider.Descriptor.Timeout);
        Assert.Equal(23, provider.Descriptor.EvidenceDefinitions.Sum(static item => item.MaximumCardinality) + 1);
        Assert.Empty(Assert.Single(typeof(BatteryReportProvider).GetConstructors()).GetParameters());
        Assert.DoesNotContain(provider.Descriptor.EvidenceDefinitions, static item =>
            item.EvidenceKey.Value is "battery.swelling_inspection" or "battery.full_charge_ratio_percent");
    }

    [Fact]
    public async Task CapacityRatioIsInferredPerPackAndMayExceedOneHundred()
    {
        var result = await Run(Snapshot(Pack(), Pack(1) with { FullChargeCapacityMwh = Known(60_000) }));
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(2, Get(result, "count").Value!.IntegerValue);
        Assert.True(Get(result, "present").Value!.BooleanValue);
        Assert.Equal(80m, Get(result, "pack.full_charge_ratio_percent", 0).Value!.DecimalValue);
        Assert.Equal(120m, Get(result, "pack.full_charge_ratio_percent", 1).Value!.DecimalValue);
        Assert.Equal(EvidenceClassification.Inferred,
            Get(result, "pack.full_charge_ratio_percent", 0).Metadata.EvidenceClassification);
        Assert.All(result.Observations, static item =>
        {
            Assert.Equal(ProvenanceKind.SyntheticFixture, item.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, item.Metadata.PrivacyClassification);
            Assert.NotEqual(EvidenceValueKind.Text, item.Value?.Kind);
        });
    }

    [Theory]
    [InlineData("fi-FI")]
    [InlineData("en-US")]
    public async Task DecimalCalculationIsInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var result = await Run(Snapshot(Pack() with { DesignCapacityMwh = Known(3), FullChargeCapacityMwh = Known(1) }));
            Assert.Equal(33.33m, Get(result, "pack.full_charge_ratio_percent").Value!.DecimalValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public async Task InvalidInjectedCapacityCannotProduceRatio(uint capacity)
    {
        var result = await Run(Snapshot(Pack() with { DesignCapacityMwh = Known(capacity) }));
        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        AssertUnknown(result, "pack.design_capacity_mwh");
        AssertUnknown(result, "pack.full_charge_ratio_percent");
        Assert.Equal(40_000, Get(result, "pack.full_charge_capacity_mwh").Value!.IntegerValue);
    }

    [Fact]
    public async Task MissingCapacitySuppressesRatioWithoutLosingIndependentCycles()
    {
        var result = await Run(Snapshot(Pack() with { FullChargeCapacityMwh = SystemInventorySourceValue<uint>.Unavailable() }));
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "pack.full_charge_ratio_percent");
        Assert.Equal(125, Get(result, "pack.cycle_count").Value!.IntegerValue);
    }

    [Fact]
    public async Task DeniedFieldRetainsOnlyIndependentFacts()
    {
        var result = await Run(Snapshot(Pack() with { DesignCapacityMwh = SystemInventorySourceValue<uint>.AccessDenied() }));
        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        AssertUnknown(result, "pack.full_charge_ratio_percent");
        Assert.Equal(125, Get(result, "pack.cycle_count").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitAbsenceHasSourceSpecificProvenanceAndUnavailableMeasurements(bool xml)
    {
        var result = await Run(new(BatteryReportSourceStatus.NoBattery,
            xml ? new(BatteryReportParseStatus.NoBattery, []) : null));
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.False(Get(result, "present").Value!.BooleanValue);
        Assert.Equal(0, Get(result, "count").Value!.IntegerValue);
        Assert.Equal(xml ? "battery.powercfg.report" : "battery.kernel32.system_power_status",
            Get(result, "present").Metadata.Provenance.SourceReference);
        AssertUnknown(result, "pack.design_capacity_mwh");
        AssertUnknown(result, "pack.full_charge_ratio_percent");
    }

    [Theory]
    [InlineData((int)BatteryReportSourceStatus.Unavailable, ProviderRunOutcome.Unavailable)]
    [InlineData((int)BatteryReportSourceStatus.AccessDenied, ProviderRunOutcome.AccessDenied)]
    [InlineData((int)BatteryReportSourceStatus.InvalidData, ProviderRunOutcome.InvalidData)]
    [InlineData((int)BatteryReportSourceStatus.Failed, ProviderRunOutcome.Failed)]
    [InlineData((int)BatteryReportSourceStatus.TimedOut, ProviderRunOutcome.Unavailable)]
    [InlineData((int)BatteryReportSourceStatus.Cancelled, ProviderRunOutcome.Unavailable)]
    public async Task CollectionFailuresCannotClaimBatteryAbsence(int rawStatus, ProviderRunOutcome outcome)
    {
        var result = await Run(new((BatteryReportSourceStatus)rawStatus, NativeErrorCode: 5, ExitCode: uint.MaxValue));
        Assert.Equal(outcome, result.Outcome);
        AssertUnknown(result, "present");
        AssertUnknown(result, "count");
        AssertUnknown(result, "pack.full_charge_ratio_percent");
        Assert.Equal(5, Get(result, "report.native_error_code").Value!.IntegerValue);
        Assert.Equal((long)uint.MaxValue, Get(result, "report.exit_code").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteCleanupWithholdsMeasurementsAndBlocksSuccess(bool inconsistentFlag)
    {
        var result = await Run(Snapshot(Pack()) with
        {
            Status = inconsistentFlag ? BatteryReportSourceStatus.CleanupIncomplete : BatteryReportSourceStatus.Collected,
            CleanupComplete = inconsistentFlag,
        });
        Assert.Equal(ProviderRunOutcome.CleanupIncomplete, result.Outcome);
        Assert.False(Get(result, "report.cleanup_complete").Value!.BooleanValue);
        AssertUnknown(result, "present");
        AssertUnknown(result, "pack.design_capacity_mwh");
    }

    [Fact]
    public async Task FourPacksAndOneMalformedFieldFitDeclaredCeiling()
    {
        var entries = Enumerable.Range(0, 4).Select(index => Pack(index)).ToArray();
        entries[3] = entries[3] with { CycleCount = SystemInventorySourceValue<uint>.InvalidData() };
        var result = await Run(Snapshot(entries));
        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        Assert.Equal(23, result.Observations.Count);
        Assert.Equal(4, result.Observations.Count(static item => item.EvidenceKey.Value == "battery.pack.full_charge_ratio_percent"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task InvalidOrdinalsDoNotSilentlyReassignPackFacts(int ordinal)
    {
        var result = await Run(Snapshot(Pack(ordinal)));
        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        AssertUnknown(result, "present");
    }

    [Fact]
    public async Task CancellationKeepsCollectionSlotUntilSourceCleanupReturns()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancelled = new CancellationTokenSource();
        var provider = Create(_ =>
        {
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
            return Snapshot(Pack());
        });
        var first = provider.ObserveAsync(cancelled.Token).AsTask();
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            cancelled.Cancel();
            Assert.Equal(ProviderReadStatus.Unavailable, (await provider.ObserveAsync()).Status);
        }
        finally
        {
            release.Set();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
    }

    [Fact]
    public async Task DirectCallerRetainsCleanupFailureAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = Create(_ =>
        {
            cancellation.Cancel();
            return new(BatteryReportSourceStatus.CleanupIncomplete, CleanupComplete: false);
        });
        var result = await provider.ObserveAsync(cancellation.Token);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, result.Status);
        Assert.DoesNotContain(result.Evidence, static item => item.EvidenceKey.Value.StartsWith("battery.pack.", StringComparison.Ordinal) && item.Value is not null);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineBatteryCollectionRecordsOnlyAvailabilityAndBoundedMetrics()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10));
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        await VerifyActualCollectionAsync();
    }

    [Fact]
    [Trait("Category", "SandboxOnly")]
    public async Task SandboxBatteryCollectionRecordsOnlyAvailabilityAndBoundedMetrics()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10));
        output.WriteLine("Environment: governed same-machine Sandbox; guest capability only.");
        await VerifyActualCollectionAsync();
    }

    private async Task VerifyActualCollectionAsync()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var memory = process.PrivateMemorySize64;
        var handles = process.HandleCount;
        var cpu = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        var result = await new InventoryProviderRunner().RunAsync(new BatteryReportProvider());
        timer.Stop();
        process.Refresh();
        var memoryGrowth = Math.Max(0, process.PrivateMemorySize64 - memory);
        var handleGrowth = Math.Max(0, process.HandleCount - handles);
        var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
        Assert.NotEqual(ProviderRunOutcome.ContractInvalid, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.CleanupIncomplete, result.Outcome);
        Assert.True(Get(result, "report.cleanup_complete").Value!.BooleanValue);
        Assert.InRange(timer.Elapsed.TotalSeconds, 0, 30);
        Assert.InRange(memoryGrowth, 0, 128L * 1024 * 1024);
        Assert.InRange(handleGrowth, 0, 256);
        Assert.InRange(cpuSeconds, 0, 30);
        output.WriteLine(FormattableString.Invariant($"outcome={result.Outcome}; elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}; cpu_ms={cpuSeconds * 1000:F3}; private_growth_bytes={memoryGrowth}; handle_growth={handleGrowth}; observation_count={result.Observations.Count}"));
        foreach (var item in result.Observations)
        {
            output.WriteLine($"{item.EvidenceKey.Value}; available={item.Value is not null}; limitation={item.LimitationCode ?? "none"}");
        }
    }

    private static void AssertUnknown(ProviderRunResult result, string suffix)
    {
        var item = Get(result, suffix);
        Assert.Null(item.Value);
        Assert.Equal(EvidenceClassification.NotAvailable, item.Metadata.EvidenceClassification);
        Assert.Equal(ValueAvailability.Unknown, item.Metadata.ValueAvailability);
    }

    private static Observation Get(ProviderRunResult result, string suffix, int? ordinal = null) =>
        Assert.Single(result.Observations, item => item.EvidenceKey.Value == $"battery.{suffix}" &&
            (ordinal is null || item.Metadata.Provenance.SourceReference == string.Create(CultureInfo.InvariantCulture, $"battery.powercfg.report.pack.{ordinal:D2}")));

    private static SystemInventorySourceValue<uint> Known(uint value) => SystemInventorySourceValue<uint>.Available(value);
    private static BatteryReportEntry Pack(int ordinal = 0) => new(ordinal, Known(50_000), Known(40_000), Known(125));
    private static BatteryReportSourceSnapshot Snapshot(params BatteryReportEntry[] entries) =>
        new(BatteryReportSourceStatus.Collected, new(BatteryReportParseStatus.Collected, entries));
    private static BatteryReportProvider Create(Func<CancellationToken, BatteryReportSourceSnapshot> read) =>
        new(new DelegateSource(read), ProviderEvidenceOrigin.SyntheticFixture);
    private static ValueTask<ProviderRunResult> Run(BatteryReportSourceSnapshot snapshot) =>
        new InventoryProviderRunner().RunAsync(Create(_ => snapshot));
    private sealed class DelegateSource(Func<CancellationToken, BatteryReportSourceSnapshot> read) : IBatteryReportSource
    {
        public BatteryReportSourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
