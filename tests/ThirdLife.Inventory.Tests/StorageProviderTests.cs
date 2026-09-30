using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class StorageProviderTests(ITestOutputHelper output)
{
    [Fact]
    public void DescriptorFitsExistingEvidenceCeilingAndDeclaresUnitsAndLocalStandardUserCollection()
    {
        var provider = Create(_ => Snapshot(Disk()));

        Assert.Equal("windows-storage", provider.Descriptor.ProviderId.Value);
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.Descriptor.Timeout);
        Assert.Equal(21, provider.Descriptor.EvidenceDefinitions.Count);
        Assert.Equal(64, provider.Descriptor.EvidenceDefinitions.Sum(static field => field.MaximumCardinality) + 1);
        Assert.Equal(64, InventoryProviderDescriptor.MaximumEvidenceKeyCount);
        Assert.Equal(14, provider.Descriptor.EvidenceDefinitions.Count(static field => field.MaximumCardinality == 4));
        Assert.Empty(Assert.Single(typeof(StorageProvider).GetConstructors()).GetParameters());
        Assert.Equal("bytes", Definition(provider, "storage.physical_disk.capacity_bytes").Unit);
        Assert.Equal("percent_consumed", Definition(provider, "storage.physical_disk.wear").Unit);
        Assert.Equal("degrees_celsius", Definition(provider, "storage.physical_disk.temperature").Unit);
        Assert.Equal("hours", Definition(provider, "storage.physical_disk.power_on_hours").Unit);
        Assert.Equal("count", Definition(provider, "storage.physical_disk.read_errors_uncorrected").Unit);
        Assert.DoesNotContain(provider.Descriptor.EvidenceDefinitions, static field => field.EvidenceKey.Value.StartsWith("storage.system.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(3, 11, "hdd", "sata")]
    [InlineData(4, 11, "ssd", "sata")]
    [InlineData(4, 17, "ssd", "nvme")]
    [InlineData(5, 10, "scm", "sas")]
    public async Task DiskFixturesPreserveTypedFieldsAndKeepSystemVolumeSeparate(int media, int bus, string mediaName, string busName)
    {
        var result = await Run(Snapshot(Disk() with { MediaType = Available((ushort)media), BusType = Available((ushort)bus) }));

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(21, result.Observations.Count);
        Assert.Equal(mediaName, Get(result, "storage.physical_disk.media_type").Value!.StringValue);
        Assert.Equal(busName, Get(result, "storage.physical_disk.bus_type").Value!.StringValue);
        Assert.Equal(1_000_000_000_000, Get(result, "storage.physical_disk.capacity_bytes").Value!.IntegerValue);
        Assert.Equal(500, Get(result, "storage.system_volume.caller_capacity_bytes").Value!.IntegerValue);
        Assert.Equal(100, Get(result, "storage.system_volume.caller_free_bytes").Value!.IntegerValue);
        Assert.Equal(200, Get(result, "storage.system_volume.total_free_bytes").Value!.IntegerValue);
        AssertUnknown(result, "storage.physical_disk.reliability_native_error_code");
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
        });
    }

    [Fact]
    public async Task MultipleDisksKeepTheirOwnReliabilityAndNormalizeBySnapshotOrdinal()
    {
        var disks = new[]
        {
            Disk(0) with { MediaType = Available<ushort>(3), Reliability = Reliability() with { Wear = Available<byte>(0) } },
            Disk(1) with { MediaType = Available<ushort>(4), Reliability = Reliability() with { Wear = Available<byte>(24) } },
            Disk(2) with { BusType = Available<ushort>(17), Reliability = Reliability() with { Wear = Available<byte>(101) } },
        };
        var ordered = await Run(Snapshot(disks));
        var reversed = await Run(Snapshot(disks.Reverse().ToArray()));

        Assert.Equal(3, Get(ordered, "storage.physical_disk_count").Value!.IntegerValue);
        Assert.Equal(49, ordered.Observations.Count);
        Assert.Equal(Fingerprint(ordered), Fingerprint(reversed));
        Assert.Equal(0, Get(ordered, "storage.physical_disk.wear", 0).Value!.IntegerValue);
        Assert.Equal(24, Get(ordered, "storage.physical_disk.wear", 1).Value!.IntegerValue);
        Assert.Equal(101, Get(ordered, "storage.physical_disk.wear", 2).Value!.IntegerValue);
        Assert.DoesNotContain(ordered.Observations, static item => item.EvidenceKey.Value.Contains("remaining", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FourDisksAndAProviderFailureFitExactlySixtyFourObservations()
    {
        var disks = Enumerable.Range(0, 4).Select(index => Disk(index) with
        {
            Reliability = Reliability() with { CollectionInfo = new(StorageCollectionStatus.AccessDenied, 5) },
        }).ToArray();
        var result = await Run(Snapshot(disks));

        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        Assert.Equal(64, result.Observations.Count);
        Assert.Equal(4, Get(result, "storage.physical_disk_count").Value!.IntegerValue);
        Assert.All(result.Observations.Where(static item => item.EvidenceKey.Value == "storage.physical_disk.reliability_native_error_code"),
            static item => Assert.Equal(5, item.Value!.IntegerValue));
    }

    [Fact]
    public async Task CriticalReportedStatusCountsAndWearRemainObservedWithoutDiagnosis()
    {
        var disk = Disk() with
        {
            HealthStatus = Available<ushort>(2),
            OperationalStatus = Available<IReadOnlyList<ushort>>([7, 2, 65535, 7]),
            Reliability = Reliability() with
            {
                Wear = Available(byte.MaxValue),
                Temperature = Available(byte.MaxValue),
                ReadErrorsTotal = Available<ulong>(1),
                ReadErrorsUncorrected = Available<ulong>(99),
                WriteErrorsTotal = Available<ulong>(0),
                WriteErrorsUncorrected = Available<ulong>(3),
                PowerOnHours = Available(ushort.MaxValue),
            },
        };
        var result = await Run(Snapshot(disk));

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal("unhealthy", Get(result, "storage.physical_disk.reported_health").Value!.StringValue);
        Assert.Equal("7,2,65535,7", Get(result, "storage.physical_disk.operational_status_codes").Value!.StringValue);
        Assert.Equal(255, Get(result, "storage.physical_disk.wear").Value!.IntegerValue);
        Assert.Equal(255, Get(result, "storage.physical_disk.temperature").Value!.IntegerValue);
        Assert.Equal(99, Get(result, "storage.physical_disk.read_errors_uncorrected").Value!.IntegerValue);
        Assert.Equal(3, Get(result, "storage.physical_disk.write_errors_uncorrected").Value!.IntegerValue);
        Assert.Equal(65535, Get(result, "storage.physical_disk.power_on_hours").Value!.IntegerValue);
        Assert.All(result.Observations.Where(static item => item.Value is not null),
            static item => Assert.Equal(EvidenceClassification.Observed, item.Metadata.EvidenceClassification));
    }

    [Fact]
    public async Task MissingWearAndHealthRemainUnknownWhileOtherCountersSurvive()
    {
        var disk = Disk() with
        {
            HealthStatus = SystemInventorySourceValue<ushort>.Unavailable(),
            Reliability = Reliability() with
            {
                Wear = SystemInventorySourceValue<byte>.Unavailable(),
                Temperature = SystemInventorySourceValue<byte>.Unavailable(),
                ReadErrorsUncorrected = Available<ulong>(8),
            },
        };
        var result = await Run(Snapshot(disk));

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "storage.physical_disk.reported_health");
        AssertUnknown(result, "storage.physical_disk.wear");
        AssertUnknown(result, "storage.physical_disk.temperature");
        Assert.Equal(8, Get(result, "storage.physical_disk.read_errors_uncorrected").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(65535, 14, 65535)]
    [InlineData(2, 18, 3)]
    public async Task UnknownOrReservedEnumStatesNeverBecomeHealthy(int media, int bus, int health)
    {
        var result = await Run(Snapshot(Disk() with
        {
            MediaType = Available((ushort)media),
            BusType = Available((ushort)bus),
            HealthStatus = Available((ushort)health),
        }));

        AssertUnknown(result, "storage.physical_disk.media_type");
        AssertUnknown(result, "storage.physical_disk.bus_type");
        AssertUnknown(result, "storage.physical_disk.reported_health");
        Assert.Equal("2", Get(result, "storage.physical_disk.operational_status_codes").Value!.StringValue);
    }

    [Fact]
    public async Task UnrepresentableNumbersAreUnavailableAndDoNotEraseCriticalIndependentCounts()
    {
        var disk = Disk() with
        {
            CapacityBytes = Available(ulong.MaxValue),
            Reliability = Reliability() with { ReadErrorsTotal = Available(ulong.MaxValue), ReadErrorsUncorrected = Available<ulong>(2) },
        };
        var result = await Run(Snapshot(disk));

        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        AssertUnknown(result, "storage.physical_disk.capacity_bytes", "provider_value_malformed");
        AssertUnknown(result, "storage.physical_disk.read_errors_total", "provider_value_malformed");
        Assert.Equal(2, Get(result, "storage.physical_disk.read_errors_uncorrected").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task EmptyAndOversizedOperationalArraysRemainUnavailable(int count)
    {
        var result = await Run(Snapshot(Disk() with
        {
            OperationalStatus = Available<IReadOnlyList<ushort>>(Enumerable.Repeat((ushort)2, count).ToArray()),
        }));

        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        AssertUnknown(result, "storage.physical_disk.operational_status_codes", "provider_value_malformed");
        Assert.Equal("healthy", Get(result, "storage.physical_disk.reported_health").Value!.StringValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ExcessDuplicateNegativeAndNoncontiguousDisksRejectAllPhysicalFacts(int variant)
    {
        StorageDiskSnapshot[] disks = variant switch
        {
            0 => Enumerable.Range(0, 5).Select(Disk).ToArray(),
            1 => [Disk(), Disk() with { CapacityBytes = Available<ulong>(3) }],
            2 => [Disk(-1)],
            _ => [Disk(1)],
        };
        var result = await Run(Snapshot(disks));

        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        AssertUnknown(result, "storage.physical_disk_count", "provider_value_malformed");
        Assert.All(result.Observations.Where(static item => item.EvidenceKey.Value.StartsWith("storage.physical_disk.", StringComparison.Ordinal) &&
            item.EvidenceKey.Value is not "storage.physical_disk.collection_status" and not "storage.physical_disk.native_error_code"),
            static item => Assert.Null(item.Value));
        Assert.Equal(100, Get(result, "storage.system_volume.caller_free_bytes").Value!.IntegerValue);
        Assert.InRange(result.Observations.Count, 21, 64);
    }

    [Fact]
    public async Task NoEnumeratedDisksIsZeroCountAndNeverInventsHealthyDisk()
    {
        var result = await Run(Snapshot());

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(0, Get(result, "storage.physical_disk_count").Value!.IntegerValue);
        AssertUnknown(result, "storage.physical_disk.reported_health");
        AssertUnknown(result, "storage.physical_disk.capacity_bytes");
        Assert.Equal(500, Get(result, "storage.system_volume.caller_capacity_bytes").Value!.IntegerValue);
    }

    [Theory]
    [InlineData((int)StorageCollectionStatus.Unavailable, (int)ProviderRunOutcome.Unavailable, "unavailable")]
    [InlineData((int)StorageCollectionStatus.AccessDenied, (int)ProviderRunOutcome.AccessDenied, "access_denied")]
    [InlineData((int)StorageCollectionStatus.InvalidData, (int)ProviderRunOutcome.InvalidData, "invalid_data")]
    [InlineData((int)StorageCollectionStatus.Failed, (int)ProviderRunOutcome.Failed, "failed")]
    [InlineData((int)StorageCollectionStatus.TimedOut, (int)ProviderRunOutcome.Unavailable, "timed_out")]
    [InlineData((int)StorageCollectionStatus.CleanupIncomplete, (int)ProviderRunOutcome.CleanupIncomplete, "cleanup_incomplete")]
    public async Task ReliabilityFailuresRetainNativeErrorAndBaseDiskFacts(int status, int outcome, string code)
    {
        var result = await Run(Snapshot(Disk() with
        {
            HealthStatus = Available<ushort>(2),
            Reliability = Reliability() with { CollectionInfo = new((StorageCollectionStatus)status, uint.MaxValue) },
        }));

        Assert.Equal((ProviderRunOutcome)outcome, result.Outcome);
        Assert.Equal(code, Get(result, "storage.physical_disk.reliability_collection_status").Value!.StringValue);
        Assert.Equal(uint.MaxValue, Get(result, "storage.physical_disk.reliability_native_error_code").Value!.IntegerValue);
        Assert.Equal("unhealthy", Get(result, "storage.physical_disk.reported_health").Value!.StringValue);
        Assert.Null(Get(result, "storage.physical_disk.wear").Value);
        Assert.Equal(500, Get(result, "storage.system_volume.caller_capacity_bytes").Value!.IntegerValue);
    }

    [Fact]
    public async Task SeparateFailuresKeepDiskEnumerationAndVolumeNativeErrorsWithoutSelectingOne()
    {
        var result = await Run(Snapshot(Disk()) with
        {
            DiskEnumeration = new(StorageCollectionStatus.AccessDenied, 5),
            SystemVolume = Volume() with { CollectionInfo = new(StorageCollectionStatus.Failed, 87) },
        });

        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        Assert.Equal("access_denied", Get(result, "storage.physical_disk.collection_status").Value!.StringValue);
        Assert.Equal(5, Get(result, "storage.physical_disk.native_error_code").Value!.IntegerValue);
        Assert.Equal(87, Get(result, "storage.system_volume.native_error_code").Value!.IntegerValue);
        AssertUnknown(result, "storage.physical_disk_count");
        AssertUnknown(result, "storage.system_volume.caller_free_bytes");
    }

    [Fact]
    public async Task FieldDenialAndMalformedMeasurementsPreserveOtherMeasuredData()
    {
        var result = await Run(Snapshot(Disk() with
        {
            CapacityBytes = SystemInventorySourceValue<ulong>.AccessDenied(),
            Reliability = Reliability() with { Temperature = SystemInventorySourceValue<byte>.InvalidData(), ReadErrorsUncorrected = Available<ulong>(7) },
        }));

        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        AssertUnknown(result, "storage.physical_disk.capacity_bytes");
        AssertUnknown(result, "storage.physical_disk.temperature", "provider_value_malformed");
        Assert.Equal(7, Get(result, "storage.physical_disk.read_errors_uncorrected").Value!.IntegerValue);
    }

    [Theory]
    [InlineData(100UL, 40UL, 200UL, true)]
    [InlineData(0UL, 0UL, 0UL, true)]
    [InlineData(100UL, 101UL, 200UL, false)]
    [InlineData(100UL, 40UL, 39UL, false)]
    public async Task VolumeChecksRespectQuotasAndRejectImpossibleCallerFree(ulong capacity, ulong free, ulong totalFree, bool valid)
    {
        var result = await Run(Snapshot(Disk()) with { SystemVolume = new(new(StorageCollectionStatus.Collected), Available(capacity), Available(free), Available(totalFree)) });

        if (valid)
        {
            Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
            Assert.Equal((long)totalFree, Get(result, "storage.system_volume.total_free_bytes").Value!.IntegerValue);
        }
        else
        {
            Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
            AssertUnknown(result, "storage.system_volume.caller_capacity_bytes", "provider_values_conflict");
            AssertUnknown(result, "storage.system_volume.caller_free_bytes", "provider_values_conflict");
            AssertUnknown(result, "storage.system_volume.total_free_bytes", "provider_values_conflict");
        }

        Assert.Equal(1, Get(result, "storage.physical_disk_count").Value!.IntegerValue);
    }

    [Fact]
    public async Task VolumeMissingAndOverflowValuesRemainIndependent()
    {
        var result = await Run(Snapshot(Disk()) with
        {
            SystemVolume = Volume() with
            {
                CallerTotalBytes = Available(ulong.MaxValue),
                CallerAvailableBytes = SystemInventorySourceValue<ulong>.Unavailable(),
            },
        });

        AssertUnknown(result, "storage.system_volume.caller_capacity_bytes", "provider_value_malformed");
        AssertUnknown(result, "storage.system_volume.caller_free_bytes");
        Assert.Equal(200, Get(result, "storage.system_volume.total_free_bytes").Value!.IntegerValue);
    }

    [Fact]
    public async Task NullSourcePartsAndInvalidCollectionStatusFailClosed()
    {
        var result = await Run(Snapshot(Disk() with { Reliability = null!, CapacityBytes = null! }) with
        {
            SystemVolume = null!,
        });

        Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
        AssertUnknown(result, "storage.physical_disk.capacity_bytes", "provider_value_malformed");
        AssertUnknown(result, "storage.system_volume.caller_free_bytes", "provider_value_malformed");
        var invalidStatus = await Run(Snapshot(Disk()) with { DiskEnumeration = new((StorageCollectionStatus)999, 87) });
        Assert.Equal(ProviderRunOutcome.InvalidData, invalidStatus.Outcome);
        Assert.Equal(87, Get(invalidStatus, "storage.physical_disk.native_error_code").Value!.IntegerValue);
        AssertUnknown(invalidStatus, "storage.physical_disk_count", "provider_value_malformed");
    }

    [Fact]
    public async Task PreCancellationDoesNotInvokeSourceAndMidReadCancellationDiscardsLateFacts()
    {
        var invoked = 0;
        using var before = new CancellationTokenSource();
        before.Cancel();
        var provider = Create(_ => { Interlocked.Increment(ref invoked); return Snapshot(Disk()); });
        var first = await new InventoryProviderRunner().RunAsync(provider, before.Token);
        Assert.Equal(ProviderRunOutcome.Cancelled, first.Outcome);
        Assert.Equal(0, invoked);

        using var during = new CancellationTokenSource();
        var second = await new InventoryProviderRunner().RunAsync(Create(_ =>
        {
            during.Cancel();
            return Snapshot(Disk());
        }), during.Token);
        Assert.Equal(ProviderRunOutcome.Cancelled, second.Outcome);
        Assert.All(second.Observations, static item => Assert.Null(item.Value));
        var retry = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Completed, retry.Outcome);
    }

    [Fact]
    public async Task TimeoutRetainsSingleFlightUntilSourceCleanupAndThenAllowsRetry()
    {
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var provider = Create(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.TrySetResult(true);
                try
                {
                    if (!release.Wait(TimeSpan.FromSeconds(12), CancellationToken.None))
                    {
                        throw new TimeoutException("Synthetic cleanup release was not signalled.");
                    }
                }
                finally
                {
                    exited.TrySetResult(true);
                }
            }

            return Snapshot(Disk());
        });
        var pending = new InventoryProviderRunner().RunAsync(provider).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var timeout = await pending.WaitAsync(TimeSpan.FromSeconds(7));
            Assert.Equal(ProviderRunOutcome.TimedOut, timeout.Outcome);
            Assert.All(timeout.Observations, static item => Assert.Null(item.Value));
            var busy = await new InventoryProviderRunner().RunAsync(provider);
            Assert.Equal(ProviderRunOutcome.Unavailable, busy.Outcome);
            Assert.Equal(1, Volatile.Read(ref attempts));
        }
        finally
        {
            release.Set();
        }

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ProviderRunResult? recovered = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            recovered = await new InventoryProviderRunner().RunAsync(provider);
            if (recovered.Outcome == ProviderRunOutcome.Completed)
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.NotNull(recovered);
        Assert.Equal(ProviderRunOutcome.Completed, recovered.Outcome);
        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task ExceptionDetailsNeverEscapeAndTheProviderCanRecover()
    {
        const string privateCanary = "SYNTHETIC-SERIAL-USERNAME-PATH-MUST-NOT-ESCAPE";
        var attempts = 0;
        var provider = Create(_ => Interlocked.Increment(ref attempts) == 1
            ? throw new InvalidOperationException(privateCanary)
            : Snapshot(Disk()));

        var failed = await new InventoryProviderRunner().RunAsync(provider);
        var retry = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Failed, failed.Outcome);
        Assert.DoesNotContain(privateCanary, JsonSerializer.Serialize(failed), StringComparison.Ordinal);
        Assert.Equal(ProviderRunOutcome.Completed, retry.Outcome);
        Assert.All(retry.Observations, static item => Assert.DoesNotContain("\\", item.Metadata.Provenance.SourceReference, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineReadOnlyStorageSmokeRecordsBoundedMetricsAndTruthfulUnavailableFields()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10));
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var startMemory = process.PrivateMemorySize64;
        var startHandles = process.HandleCount;
        var startCpu = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = await new InventoryProviderRunner().RunAsync(new StorageProvider(), cancellation.Token);
        stopwatch.Stop();
        process.Refresh();
        var memoryGrowth = Math.Max(0, process.PrivateMemorySize64 - startMemory);
        var handleGrowth = Math.Max(0, process.HandleCount - startHandles);
        var cpu = process.TotalProcessorTime - startCpu;

        Assert.NotEqual(ProviderRunOutcome.ContractInvalid, result.Outcome);
        Assert.InRange(result.Observations.Count, 21, 64);
        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0, 8);
        Assert.InRange(memoryGrowth, 0, 128L * 1024 * 1024);
        Assert.InRange(handleGrowth, 0, 256);
        Assert.InRange(cpu.TotalSeconds, 0, 8);
        Assert.All(result.Observations, static observation =>
        {
            Assert.Equal(ProvenanceKind.ProviderObservation, observation.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
            if (observation.Value?.Kind == EvidenceValueKind.Text)
            {
                Assert.Equal("storage.physical_disk.operational_status_codes", observation.EvidenceKey.Value);
                Assert.All(observation.Value.StringValue!, static character => Assert.True(char.IsAsciiDigit(character) || character == ','));
            }

            if (observation.Value is null)
            {
                Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
                Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
            }
        });
        output.WriteLine(FormattableString.Invariant($"outcome={result.Outcome}; elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F3}; cpu_ms={cpu.TotalMilliseconds:F3}; private_growth_bytes={memoryGrowth}; handle_growth={handleGrowth}; observation_count={result.Observations.Count}"));
        foreach (var item in result.Observations)
        {
            output.WriteLine($"{item.EvidenceKey.Value}; source={item.Metadata.Provenance.SourceReference}; available={item.Value is not null}; limitation={item.LimitationCode ?? "none"}");
        }
    }

    private static ProviderEvidenceDefinition Definition(StorageProvider provider, string key) =>
        Assert.Single(provider.Descriptor.EvidenceDefinitions, field => field.EvidenceKey.Value == key);

    private static void AssertUnknown(ProviderRunResult result, string key, string limitation = "provider_value_missing")
    {
        var item = Get(result, key);
        Assert.Null(item.Value);
        Assert.Equal(EvidenceClassification.NotAvailable, item.Metadata.EvidenceClassification);
        Assert.Equal(ValueAvailability.Unknown, item.Metadata.ValueAvailability);
        Assert.Equal(limitation, item.LimitationCode);
    }

    private static Observation Get(ProviderRunResult result, string key, int? ordinal = null) =>
        Assert.Single(result.Observations, item => item.EvidenceKey.Value == key &&
            (ordinal is null || item.Metadata.Provenance.SourceReference == string.Create(CultureInfo.InvariantCulture, $"storage.msft.physical_disk.{ordinal:D2}")));

    private static string Fingerprint(ProviderRunResult result) => JsonSerializer.Serialize(result.Observations.Select(static item =>
        new { Key = item.EvidenceKey.Value, item.Value, item.Unit, item.LimitationCode, Source = item.Metadata.Provenance.SourceReference }));

    private static StorageProvider Create(Func<CancellationToken, StorageSourceSnapshot> read) =>
        new(new DelegateSource(read), ProviderEvidenceOrigin.SyntheticFixture);

    private static ValueTask<ProviderRunResult> Run(StorageSourceSnapshot snapshot) =>
        new InventoryProviderRunner().RunAsync(Create(_ => snapshot));

    private static SystemInventorySourceValue<T> Available<T>(T value) where T : notnull =>
        SystemInventorySourceValue<T>.Available(value);

    private static StorageSourceSnapshot Snapshot(params StorageDiskSnapshot[] disks) =>
        new(new(StorageCollectionStatus.Collected), disks, Volume());

    private static StorageVolumeSnapshot Volume() =>
        new(new(StorageCollectionStatus.Collected), Available<ulong>(500), Available<ulong>(100), Available<ulong>(200));

    private static StorageDiskSnapshot Disk(int ordinal = 0) =>
        new(ordinal, Available<ulong>(1_000_000_000_000), Available<ushort>(4), Available<ushort>(11), Available<ushort>(0),
            Available<IReadOnlyList<ushort>>([2]), Reliability());

    private static StorageReliabilitySnapshot Reliability() =>
        new(new(StorageCollectionStatus.Collected), Available<byte>(0), Available<byte>(30), Available<ulong>(0),
            Available<ulong>(0), Available<ulong>(0), Available<ulong>(0), Available<ushort>(100));

    private sealed class DelegateSource(Func<CancellationToken, StorageSourceSnapshot> read) : IStorageSource
    {
        public StorageSourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
