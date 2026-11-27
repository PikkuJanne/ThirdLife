using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class OperatingSystemProviderTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset CurrentDate = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DescriptorIsLocalUnelevatedAndOwnsDistinctArchitecture()
    {
        var provider = new OperatingSystemProvider();
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal([ProviderOperatingSystem.Windows11], provider.Descriptor.SupportedOperatingSystems);
        Assert.Equal(TimeSpan.FromSeconds(2), provider.Descriptor.Timeout);
        Assert.Equal(14, provider.Descriptor.EvidenceDefinitions.Count);
        Assert.All(provider.Descriptor.EvidenceDefinitions, definition => Assert.Equal(1, definition.MaximumCardinality));
        Assert.Empty(new SystemInventoryProvider().Descriptor.EvidenceDefinitions.Select(d => d.EvidenceKey)
            .Intersect(provider.Descriptor.EvidenceDefinitions.Select(d => d.EvidenceKey)));
        Assert.Contains(provider.Descriptor.EvidenceDefinitions, d => d.EvidenceKey.Value == "os.architecture");
        Assert.DoesNotContain(provider.Descriptor.EvidenceDefinitions, d => d.EvidenceKey.Value == "system.architecture");
        Assert.Empty(Assert.Single(typeof(OperatingSystemProvider).GetConstructors()).GetParameters());
    }

    [Theory]
    [InlineData(26200, 48u, (ushort)9, "windows_11", "supported", "None")]
    [InlineData(19045, 48u, (ushort)9, "windows_10", "unsupported", "UnsupportedOperatingSystem")]
    [InlineData(22000, 48u, (ushort)9, "windows_11", "unsupported", "SupportEnded")]
    [InlineData(26200, 48u, (ushort)12, "windows_11", "unsupported", "UnsupportedArchitecture")]
    public async Task SyntheticWindowsVersionsKeepSupportTargetsDistinct(
        int build, uint edition, ushort architecture, string family, string support, string reason)
    {
        var result = await Run(Snapshot(build: build, edition: edition, architecture: architecture));
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(family, Value(result, "os.family"));
        Assert.Equal(support, Value(result, "os.support_target"));
        Assert.Equal(reason, Value(result, "os.lifecycle.reason"));
        Assert.Equal(build, Find(result, "os.build").Value!.IntegerValue);
        Assert.Equal(EvidenceClassification.Inferred, Find(result, "os.support_target").Metadata.EvidenceClassification);
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
            Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
            Assert.Equal(CurrentDate, observation.Metadata.CollectedAtUtc);
        });
    }

    [Fact]
    public async Task ServerRoleAndClientSkusAreReconciledBeforeMappingASharedBuild()
    {
        var server = Snapshot(build: 26100, edition: 7, productType: WindowsProductType.Server);
        var result = await Run(server);
        Assert.Equal("windows_server", Value(result, "os.family"));
        Assert.Equal("unsupported", Value(result, "os.support_target"));
        Assert.Equal("UnsupportedOperatingSystem", Value(result, "os.lifecycle.reason"));

        var conflictingSku = await Run(server with { Edition = SystemInventorySourceValue<uint>.Available(48) });
        AssertUnknown(conflictingSku, "os.family");
        AssertUnknown(conflictingSku, "os.support_target");
        Assert.Equal("UnverifiedIdentity", Value(conflictingSku, "os.lifecycle.reason"));

        var missingProductType = await Run(server with { ProductType = SystemInventorySourceValue<WindowsProductType>.Unavailable() });
        Assert.Equal(ProviderRunOutcome.Unavailable, missingProductType.Outcome);
        Assert.Equal(26100, Find(missingProductType, "os.build").Value!.IntegerValue);
        AssertUnknown(missingProductType, "os.family");
        AssertUnknown(missingProductType, "os.support_target");
    }

    [Theory]
    [InlineData(10, 0, 99999, 48u, (ushort)9)]
    [InlineData(10, 0, 26200, 8u, (ushort)9)]
    [InlineData(10, 0, 26200, 0u, (ushort)9)]
    [InlineData(10, 0, 26200, 48u, (ushort)999)]
    [InlineData(11, 0, 26200, 48u, (ushort)9)]
    [InlineData(10, 1, 26200, 48u, (ushort)9)]
    [InlineData(-1, 0, 26200, 48u, (ushort)9)]
    [InlineData(10, 0, -1, 48u, (ushort)9)]
    [InlineData(10, 0, 1000001, 48u, (ushort)9)]
    public async Task UnknownServerMalformedAndFutureInputsCannotPass(int major, int minor, int build, uint edition, ushort architecture)
    {
        var result = await Run(Snapshot(major, minor, build, edition, architecture));
        AssertUnknown(result, "os.support_target");
        Assert.DoesNotContain(result.Observations, observation => observation.Value?.StringValue == "supported");
    }

    [Fact]
    public async Task MissingStaleAndFutureCatalogDatesCannotPassAndPreserveRevision()
    {
        var missing = new OperatingSystemProvider(new Source(_ => Snapshot()), null,
            new FixedClock(CurrentDate), ProviderEvidenceOrigin.SyntheticFixture);
        AssertUnknown(await new InventoryProviderRunner(new FixedClock(CurrentDate)).RunAsync(missing), "os.support_target");
        var current = await Run(Snapshot());
        var oldJson = JsonSerializer.Serialize(current.Observations);
        var stale = await Run(Snapshot(), CurrentDate.AddDays(31));
        var future = await Run(Snapshot(), CurrentDate.AddDays(-1));
        AssertUnknown(stale, "os.support_target");
        Assert.Equal("provider_value_stale", Find(stale, "os.support_target").LimitationCode);
        Assert.Equal("windows_11", Value(stale, "os.family"));
        Assert.Equal("StaleData", Value(stale, "os.lifecycle.reason"));
        AssertUnknown(future, "os.support_target");
        Assert.Equal(Value(current, "os.lifecycle.revision"), Value(stale, "os.lifecycle.revision"));
        Assert.Equal("2026-11-25", Value(current, "os.lifecycle.as_of"));
        Assert.Equal("2026-11-25", Value(current, "os.lifecycle.evaluated_on"));
        Assert.NotNull(Value(current, "os.lifecycle.source"));
        Assert.Equal(oldJson, JsonSerializer.Serialize(current.Observations));
    }

    [Fact]
    public async Task PartialSourceFailureRetainsKnownFactsAndCannotPass()
    {
        var source = Snapshot() with { Edition = SystemInventorySourceValue<uint>.AccessDenied() };
        var result = await Run(source);
        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        Assert.Equal("x64", Value(result, "os.architecture"));
        AssertUnknown(result, "os.edition");
        AssertUnknown(result, "os.support_target");
        var unavailable = await Run(Snapshot() with
        {
            Version = SystemInventorySourceValue<OperatingSystemVersion>.Unavailable(),
            Architecture = SystemInventorySourceValue<ushort>.Unavailable(),
        });
        Assert.Equal(ProviderRunOutcome.Unavailable, unavailable.Outcome);
        AssertUnknown(unavailable, "os.support_target");
    }

    [Fact]
    public async Task FailuresAndCancellationNeverSerializeSensitiveExceptionText()
    {
        const string canary = "TEST-ONLY-KEY-ABCDE product-id-TEST account@example.invalid raw-licensing-output";
        var provider = new OperatingSystemProvider(new Source(_ => throw new InvalidOperationException(canary)),
            WindowsLifecycleCatalog.Current, new FixedClock(CurrentDate), ProviderEvidenceOrigin.SyntheticFixture);
        var result = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.All(result.Observations, observation => Assert.Null(observation.Value));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);
        Assert.Equal(ProviderRunOutcome.Cancelled, cancelled.Outcome);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(cancelled), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationRetainsSingleWorkerUntilSourceReturnsAndAllowsRetry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var provider = new OperatingSystemProvider(new Source(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None), "The bounded synthetic source was not released.");
            return Snapshot();
        }), WindowsLifecycleCatalog.Current, new FixedClock(CurrentDate), ProviderEvidenceOrigin.SyntheticFixture);
        using var cancellation = new CancellationTokenSource();
        var first = provider.ObserveAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.Equal(ProviderReadStatus.Unavailable, (await provider.ObserveAsync()).Status);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.Set();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConflictingArchitectureFactsRemainDistinctRegardlessOfProviderOrder()
    {
        var os = new OperatingSystemProvider(new Source(_ => Snapshot(architecture: 12)),
            WindowsLifecycleCatalog.Current, new FixedClock(CurrentDate), ProviderEvidenceOrigin.SyntheticFixture);
        var system = new SystemInventoryProvider(new SystemSource(), ProviderEvidenceOrigin.SyntheticFixture);
        foreach (var providers in new IInventoryProvider[][] { [os, system], [system, os] })
        {
            var observations = new List<Observation>();
            foreach (var provider in providers)
            {
                observations.AddRange((await new InventoryProviderRunner(new FixedClock(CurrentDate)).RunAsync(provider)).Observations);
            }

            var osArchitecture = Assert.Single(observations, o => o.EvidenceKey.Value == "os.architecture");
            var systemArchitecture = Assert.Single(observations, o => o.EvidenceKey.Value == "system.architecture");
            Assert.Equal("arm64", osArchitecture.Value!.StringValue);
            Assert.Equal("x64", systemArchitecture.Value!.StringValue);
            Assert.NotEqual(osArchitecture.Metadata.Provenance, systemArchitecture.Metadata.Provenance);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateOsArchitectureIsRejectedInsteadOfSelected(bool reverse)
    {
        var result = await new InventoryProviderRunner().RunAsync(new DuplicateProvider(reverse));
        Assert.Equal(ProviderRunOutcome.ContractInvalid, result.Outcome);
        AssertUnknown(result, "os.architecture");
    }

    [Fact]
    public async Task SanitizedReferenceCaptureReplaysOnlyPublishedOsFacts()
    {
        var profile = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "testing", "reference-machine-profile.md"));
        // This published capture contains no activation or restart observation. Neither is invented.
        Assert.Contains("Microsoft Windows 11 Pro", profile, StringComparison.Ordinal);
        Assert.Contains("10.0.26200 / 26200", profile, StringComparison.Ordinal);
        Assert.Contains("64-bit x64", profile, StringComparison.Ordinal);
        var source = new Source(_ => Snapshot());
        var provider = new OperatingSystemProvider(source, WindowsLifecycleCatalog.Current,
            new FixedClock(CurrentDate), ProviderEvidenceOrigin.CapturedSample);
        var result = await new InventoryProviderRunner(new FixedClock(CurrentDate)).RunAsync(provider);
        Assert.Equal("10.0.26200", Value(result, "os.version"));
        Assert.Equal("x64", Value(result, "os.architecture"));
        Assert.All(result.Observations, o => Assert.Equal(ProvenanceKind.ImportedRecord, o.Metadata.Provenance.Kind));
    }

    [Fact]
    public void NativeImportsAreRestrictedToReadOnlySystemProductQueries()
    {
        var imports = typeof(WindowsOperatingSystemSource).GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            .Select(m => (Method: m, Import: m.GetCustomAttribute<DllImportAttribute>()))
            .Where(item => item.Import is not null).ToArray();
        Assert.Equal(["GetProductInfo", "RegGetValueW", "VerSetConditionMask", "VerifyVersionInfo"],
            imports.Select(item => item.Method.Name).Order(StringComparer.Ordinal));
        Assert.All(imports, import =>
        {
            Assert.Equal(import.Method.Name == "RegGetValueW" ? "advapi32.dll" : "kernel32.dll", import.Import!.Value);
            Assert.Equal(DllImportSearchPath.System32,
                import.Method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()!.Paths);
        });
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineOsSmokeIsUnelevatedBoundedAndSilent()
    {
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.CheckCurrent());
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        var result = await new InventoryProviderRunner().RunAsync(new OperatingSystemProvider(), cancellation.Token);
        timer.Stop();
        process.Refresh();
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.NotNull(Find(result, "os.build").Value);
        Assert.NotNull(Find(result, "os.edition_code").Value);
        Assert.NotNull(Find(result, "os.architecture").Value);
        Assert.Equal("windows_11", Value(result, "os.family"));
        Assert.InRange(result.Observations.Count, 1, 15);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
        Assert.All(result.Observations, o => Assert.Equal(PrivacyClassification.WorkshopRestricted, o.Metadata.PrivacyClassification));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "TL-0107-active-os",
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
            processCpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            testHostPeakWorkingSetBytes = process.PeakWorkingSet64,
            normalizedOutputBytes = System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result.Observations)),
            observations = result.Observations.Count,
            providerWorkers = 1,
            providerFileWrites = 0,
            limitation = "Test-host process metrics; no hardware certification or approved performance budget.",
        }));
    }

    private static OperatingSystemSnapshot Snapshot(
        int major = 10, int minor = 0, int build = 26200, uint edition = 48, ushort architecture = 9,
        WindowsProductType productType = WindowsProductType.Workstation) =>
        new(SystemInventorySourceValue<OperatingSystemVersion>.Available(new(major, minor, build)),
            SystemInventorySourceValue<uint>.Available(edition), SystemInventorySourceValue<ushort>.Available(architecture),
            SystemInventorySourceValue<WindowsProductType>.Available(productType));

    private static async Task<ProviderRunResult> Run(OperatingSystemSnapshot snapshot, DateTimeOffset? date = null)
    {
        var clock = new FixedClock(date ?? CurrentDate);
        return await new InventoryProviderRunner(clock).RunAsync(new OperatingSystemProvider(new Source(_ => snapshot),
            WindowsLifecycleCatalog.Current, clock, ProviderEvidenceOrigin.SyntheticFixture));
    }

    private static Observation Find(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key);

    private static string? Value(ProviderRunResult result, string key) => Find(result, key).Value?.StringValue;

    private static void AssertUnknown(ProviderRunResult result, string key)
    {
        var observation = Find(result, key);
        Assert.Null(observation.Value);
        Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TASKS.yaml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
    }

    private sealed class Source(Func<CancellationToken, OperatingSystemSnapshot> read) : IOperatingSystemSource
    {
        public OperatingSystemSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SystemSource : ISystemInventorySource
    {
        public SystemInventorySourceSnapshot Read(CancellationToken cancellationToken) => new(
            SystemInventorySourceValue<byte[]>.Unavailable(), SystemInventorySourceValue<ushort>.Available(9),
            SystemInventorySourceValue<ulong>.Unavailable(), SystemInventorySourceValue<uint>.Unavailable());
    }

    private sealed class DuplicateProvider(bool reverse) : IInventoryProvider
    {
        public InventoryProviderDescriptor Descriptor { get; } = new OperatingSystemProvider().Descriptor;

        public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            var valid = await new OperatingSystemProvider(new Source(_ => Snapshot()), WindowsLifecycleCatalog.Current,
                new FixedClock(CurrentDate), ProviderEvidenceOrigin.SyntheticFixture).ObserveAsync(cancellationToken);
            var evidence = valid.Evidence.Append(NormalizedEvidence.Observed(
                new EvidenceKey("os.architecture"), EvidenceValue.FromEnum("arm64")));
            return ProviderReadResult.Collected(reverse ? evidence.Reverse() : evidence);
        }
    }
}
