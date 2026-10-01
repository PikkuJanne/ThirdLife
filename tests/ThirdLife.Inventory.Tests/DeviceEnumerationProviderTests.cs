using System.Collections;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class DeviceEnumerationProviderTests(ITestOutputHelper output)
{
    private static readonly string[] PresenceKeys =
    [
        "devices.wifi.present", "devices.ethernet.present", "devices.bluetooth.radio_interface_present",
        "devices.audio.render_endpoint_present", "devices.camera.video_interface_present", "devices.microphone.capture_endpoint_present",
        "devices.display.monitor_present", "devices.keyboard.present", "devices.pointing.present",
        "devices.touch.session_present", "devices.usb_controller.host_interface_present",
    ];

    public static IEnumerable<object[]> ClassesAndOutcomes =>
        from deviceClass in Enum.GetValues<DeviceClass>()
        from status in Enum.GetValues<DeviceEnumerationStatus>()
        select new object[] { (int)deviceClass, (int)status };

    [Fact]
    public async Task PresenceHasOneOwnerPerClassAndNeverCompletesAFunctionalTest()
    {
        var provider = Provider(Healthy());
        Assert.Empty(Assert.Single(typeof(DeviceEnumerationProvider).GetConstructors()).GetParameters());
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(TimeSpan.FromSeconds(15), provider.Descriptor.Timeout);
        Assert.Equal(45, provider.Descriptor.EvidenceDefinitions.Count);
        Assert.Equal(45, provider.Descriptor.EvidenceDefinitions.Select(static fact => fact.EvidenceKey.Value).Distinct().Count());
        var read = await provider.ObserveAsync();
        Assert.Equal(ProviderReadStatus.Collected, read.Status);
        foreach (var key in PresenceKeys)
        {
            var fact = Fact(read, key);
            Assert.True(fact.Value!.BooleanValue);
            Assert.Equal(EvidenceClassification.Observed, fact.EvidenceClassification);
        }

        Assert.Equal("not_performed", Fact(read, "devices.functional_test").Value!.StringValue);
        Assert.DoesNotContain(read.Evidence, static fact => fact.EvidenceKey.Value.Contains("ready", StringComparison.Ordinal));
        var run = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Completed, run.Outcome);
        Assert.Equal(45, run.Observations.Count);
        Assert.All(run.Observations, static fact =>
        {
            Assert.Equal(ProvenanceKind.SyntheticFixture, fact.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, fact.Metadata.PrivacyClassification);
            Assert.Equal(fact.Metadata.ValueAvailability == ValueAvailability.Available, fact.Value is not null);
        });
        Assert.Equal(11, read.Evidence.Select(static fact => fact.SourceReference).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(ClassesAndOutcomes))]
    public async Task AbsenceAndClassErrorsRemainDistinctWithoutErasingOtherClasses(int classValue, int statusValue)
    {
        var deviceClass = (DeviceClass)classValue;
        var status = (DeviceEnumerationStatus)statusValue;
        var snapshot = Healthy();
        snapshot[classValue - 1] = status == DeviceEnumerationStatus.Available
            ? new(deviceClass, status, false, 0)
            : new(deviceClass, status, NativeErrorCode: 0x80070005);

        var read = await Provider(snapshot).ObserveAsync();

        var expectedStatus = status switch
        {
            DeviceEnumerationStatus.Available => ProviderReadStatus.Collected,
            DeviceEnumerationStatus.Unavailable => ProviderReadStatus.Unavailable,
            DeviceEnumerationStatus.AccessDenied => ProviderReadStatus.AccessDenied,
            _ => ProviderReadStatus.InvalidData,
        };
        Assert.Equal(expectedStatus, read.Status);
        var key = PresenceKeys[classValue - 1];
        var prefix = key[..(key.LastIndexOf('.') + 1)];
        if (status == DeviceEnumerationStatus.Available)
        {
            Assert.False(Fact(read, key).Value!.BooleanValue);
            Assert.Equal("collected", Fact(read, prefix + "collection_status").Value!.StringValue);
            Assert.Equal(0, Fact(read, prefix + "problem_count").Value!.IntegerValue);
        }
        else
        {
            Unknown(read, key);
            Unknown(read, prefix + "problem_count");
            Assert.Equal(0x80070005L, Fact(read, prefix + "native_error_code").Value!.IntegerValue);
            Assert.Equal(status switch
            {
                DeviceEnumerationStatus.Unavailable => "unavailable",
                DeviceEnumerationStatus.AccessDenied => "access_denied",
                _ => "invalid_data",
            }, Fact(read, prefix + "collection_status").Value!.StringValue);
        }

        foreach (var unaffected in PresenceKeys.Where(other => other != key))
        {
            Assert.True(Fact(read, unaffected).Value!.BooleanValue);
        }
        Assert.Equal("not_performed", Fact(read, "devices.functional_test").Value!.StringValue);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SyntheticCameraAndTouchVariantsDoNotClaimFunctionality(bool camera, bool touch)
    {
        var snapshot = Healthy();
        snapshot[(int)DeviceClass.Camera - 1] = new(DeviceClass.Camera, DeviceEnumerationStatus.Available, camera, 0);
        snapshot[(int)DeviceClass.Touch - 1] = new(DeviceClass.Touch, DeviceEnumerationStatus.Available, touch);
        var read = await Provider(snapshot).ObserveAsync();

        Assert.Equal(ProviderReadStatus.Collected, read.Status);
        Assert.Equal(camera, Fact(read, "devices.camera.video_interface_present").Value!.BooleanValue);
        Assert.Equal(touch, Fact(read, "devices.touch.session_present").Value!.BooleanValue);
        Unknown(read, "devices.touch.problem_count");
        Assert.Equal("not_performed", Fact(read, "devices.functional_test").Value!.StringValue);
    }

    [Theory]
    [InlineData(5, "devices.camera.video_interface_present", "devices.camera.present")]
    [InlineData(3, "devices.bluetooth.radio_interface_present", "devices.bluetooth.present")]
    [InlineData(11, "devices.usb_controller.host_interface_present", "devices.usb_controller.present")]
    public async Task EmptyInterfaceEnumerationPublishesOnlyScopedAbsence(int classValue, string scopedKey, string genericKey)
    {
        var deviceClass = (DeviceClass)classValue;
        var native = new EmptyInterfaceNative(deviceClass);
        var snapshot = Healthy();
        snapshot[classValue - 1] = new WindowsDeviceClassSource(native).Read(deviceClass, CancellationToken.None);
        var provider = Provider(snapshot);

        var read = await provider.ObserveAsync();

        Assert.Equal(ProviderReadStatus.Collected, read.Status);
        Assert.False(Fact(read, scopedKey).Value!.BooleanValue);
        Assert.Equal(ValueAvailability.Available, Fact(read, scopedKey).ValueAvailability);
        Assert.DoesNotContain(read.Evidence, fact => fact.EvidenceKey.Value == genericKey);
        Assert.DoesNotContain(provider.Descriptor.EvidenceDefinitions, fact => fact.EvidenceKey.Value == genericKey);
        Assert.Equal(scopedKey, Assert.Single(read.Evidence,
            static fact => fact.Value?.BooleanValue == false).EvidenceKey.Value);
        Assert.Equal("not_performed", Fact(read, "devices.functional_test").Value!.StringValue);
        Assert.Equal(1, native.NextCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData(1, null, null)]
    [InlineData(2, true, null)]
    [InlineData(3, false, null)]
    [InlineData(4, null, 0)]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 257)]
    [InlineData(99, null, null)]
    public async Task ContradictoryOrMalformedClassCannotPass(int status, bool? present, int? problems)
    {
        var snapshot = Healthy();
        snapshot[(int)DeviceClass.Camera - 1] = new(DeviceClass.Camera, (DeviceEnumerationStatus)status,
            present, problems.HasValue ? (uint)problems.Value : null);
        var read = await Provider(snapshot).ObserveAsync();

        Assert.Equal(ProviderReadStatus.InvalidData, read.Status);
        Unknown(read, "devices.camera.video_interface_present", ProviderLimitation.SourceValueMalformed);
        Unknown(read, "devices.camera.problem_count", ProviderLimitation.SourceValueMalformed);
        Assert.Equal("invalid_data", Fact(read, "devices.camera.collection_status").Value!.StringValue);
        Assert.True(Fact(read, "devices.keyboard.present").Value!.BooleanValue);
    }

    [Fact]
    public async Task ProblemAndNativeErrorCodesRemainBoundedNumericEvidence()
    {
        var snapshot = Healthy();
        snapshot[(int)DeviceClass.Camera - 1] = new(DeviceClass.Camera, DeviceEnumerationStatus.Available, true,
            WindowsDeviceEnumerationSource.MaximumDevicesPerClass, uint.MaxValue);
        var read = await Provider(snapshot).ObserveAsync();

        Assert.Equal(ProviderReadStatus.Collected, read.Status);
        Assert.Equal(256, Fact(read, "devices.camera.problem_count").Value!.IntegerValue);
        Assert.Equal(uint.MaxValue, Fact(read, "devices.camera.native_error_code").Value!.IntegerValue);
        Assert.Equal("not_performed", Fact(read, "devices.functional_test").Value!.StringValue);
    }

    [Fact]
    public async Task MissingClassIsUnknownAndMixedErrorsUseDeterministicSeverity()
    {
        var snapshot = Healthy().Where(static item => item.Class != DeviceClass.Camera).ToArray();
        var read = await Provider(snapshot).ObserveAsync();
        Assert.Equal(ProviderReadStatus.Unavailable, read.Status);
        Unknown(read, "devices.camera.video_interface_present", ProviderLimitation.SourceValueMissing);
        snapshot[0] = new(DeviceClass.Wifi, DeviceEnumerationStatus.AccessDenied, NativeErrorCode: 5);
        read = await Provider(snapshot).ObserveAsync();
        Assert.Equal(ProviderReadStatus.AccessDenied, read.Status);
        snapshot[1] = new(DeviceClass.Ethernet, DeviceEnumerationStatus.InvalidData);
        read = await Provider(snapshot).ObserveAsync();
        Assert.Equal(ProviderReadStatus.InvalidData, read.Status);
        Assert.Equal(JsonSerializer.Serialize(read), JsonSerializer.Serialize(await Provider(snapshot.Reverse().ToArray()).ObserveAsync()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateClassIsAConflictEvenWhenValuesMatch(bool duplicatePresent)
    {
        var snapshot = Healthy();
        snapshot[(int)DeviceClass.Keyboard - 1] = new(DeviceClass.Camera, DeviceEnumerationStatus.Available, duplicatePresent, 0);
        var forward = await Provider(snapshot).ObserveAsync();
        var reverse = await Provider(snapshot.Reverse().ToArray()).ObserveAsync();

        Assert.Equal(ProviderReadStatus.InvalidData, forward.Status);
        Unknown(forward, "devices.camera.video_interface_present", ProviderLimitation.SourceValuesConflict);
        Unknown(forward, "devices.keyboard.present", ProviderLimitation.SourceValueMissing);
        Assert.Equal(JsonSerializer.Serialize(forward), JsonSerializer.Serialize(reverse));
    }

    [Fact]
    public async Task UnknownClassAndNullEntriesRejectTheWholeMalformedSnapshot()
    {
        var snapshot = Healthy();
        snapshot[0] = new((DeviceClass)999, DeviceEnumerationStatus.Available, true);
        var result = await Provider(snapshot).ObserveAsync();
        Assert.Equal(ProviderReadStatus.InvalidData, result.Status);
        Assert.Empty(result.Evidence);
        snapshot[0] = null!;
        result = await Provider(snapshot).ObserveAsync();
        Assert.Equal(ProviderReadStatus.InvalidData, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task OversizedInputIsStoppedAfterTwelveItems()
    {
        var source = new OversizedSnapshot();
        var result = await Provider(source).ObserveAsync();

        Assert.Equal(ProviderReadStatus.InvalidData, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Equal(12, source.ItemsRead);
    }

    [Fact]
    public void SourceSeamCannotCarryNetworkIdentitiesOrPersonalDeviceNames()
    {
        Type[] allowed = [typeof(DeviceClass), typeof(DeviceEnumerationStatus), typeof(bool?), typeof(uint?), typeof(bool)];
        Assert.All(typeof(DeviceClassSnapshot).GetProperties(), property => Assert.Contains(property.PropertyType, allowed));
        Assert.DoesNotContain(typeof(DeviceClassSnapshot).GetProperties(), static property => property.PropertyType == typeof(string));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineReadOnlySmokeEmitsOnlyClassStatesAndProcessMetrics()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await new InventoryProviderRunner().RunAsync(new DeviceEnumerationProvider(), cancellation.Token);
        Assert.NotEqual(ProviderRunOutcome.Failed, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.ContractInvalid, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.CleanupIncomplete, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.NotEqual(ProviderRunOutcome.TimedOut, result.Outcome);
        Assert.InRange(result.Observations.Count, 45, 46);
        Assert.All(result.Observations, static fact =>
        {
            Assert.Equal(ProvenanceKind.ProviderObservation, fact.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, fact.Metadata.PrivacyClassification);
            Assert.Equal(fact.Metadata.ValueAvailability == ValueAvailability.Available, fact.Value is not null);
        });
        Assert.Equal("not_performed", Assert.Single(result.Observations,
            static fact => fact.EvidenceKey.Value == "devices.functional_test").Value!.StringValue);
        process.Refresh();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            task = "TL-0111",
            elevated = false,
            outcome = result.Outcome.ToString(),
            observations = result.Observations.Count,
            durationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            processCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
            processAllocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
            processWorkingSetBytes = process.WorkingSet64,
            processPeakWorkingSetBytes = process.PeakWorkingSet64,
            normalizedEvidenceBytes = JsonSerializer.SerializeToUtf8Bytes(result).Length,
            states = result.Observations.Where(static fact => fact.EvidenceKey.Value != "devices.provider_status")
                .Select(static fact => new
                {
                    key = fact.EvidenceKey.Value,
                    value = fact.Value,
                    availability = fact.Metadata.ValueAvailability.ToString(),
                    limitation = fact.LimitationCode,
                }),
        }));
    }

    private static DeviceClassSnapshot[] Healthy() => Enum.GetValues<DeviceClass>()
        .Select(static deviceClass => new DeviceClassSnapshot(deviceClass, DeviceEnumerationStatus.Available, true, 0)).ToArray();

    private static DeviceEnumerationProvider Provider(IReadOnlyList<DeviceClassSnapshot> snapshot) =>
        new(new Source(_ => snapshot), ProviderEvidenceOrigin.SyntheticFixture);

    private static NormalizedEvidence Fact(ProviderReadResult result, string key) =>
        Assert.Single(result.Evidence, fact => fact.EvidenceKey.Value == key);

    private static void Unknown(ProviderReadResult result, string key, ProviderLimitation? limitation = null)
    {
        var fact = Fact(result, key);
        Assert.Null(fact.Value);
        Assert.Equal(ValueAvailability.Unknown, fact.ValueAvailability);
        if (limitation.HasValue) { Assert.Equal(limitation, fact.Limitation); }
    }

    private sealed class Source(Func<CancellationToken, IReadOnlyList<DeviceClassSnapshot>> read) : IDeviceEnumerationSource
    {
        public IReadOnlyList<DeviceClassSnapshot> Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class EmptyInterfaceNative(DeviceClass expectedClass) : IDeviceClassNativeApi
    {
        public bool IsSupported => true;
        public int NextCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public DeviceClassNativeOpen Open(DeviceClass deviceClass)
        {
            Assert.Equal(expectedClass, deviceClass);
            Assert.True(WindowsDeviceClassNativeApi.Selection(deviceClass).IsInterface);
            return new(new nint(1));
        }

        public DeviceClassNativeRow Next(nint handle, uint index)
        {
            Assert.Equal(new nint(1), handle);
            Assert.Equal(0U, index);
            NextCalls++;
            return new(259);
        }

        public DeviceClassNativeProblem ReadProblem(uint deviceNode) => throw new InvalidOperationException("An empty interface set has no device node.");
        public int ReadDigitizerFlags() => throw new InvalidOperationException("Interface enumeration must not query touch flags.");
        public uint Close(nint handle)
        {
            Assert.Equal(new nint(1), handle);
            CloseCalls++;
            return 0;
        }
    }

    private sealed class OversizedSnapshot : IReadOnlyList<DeviceClassSnapshot>
    {
        public int ItemsRead { get; private set; }
        public int Count => int.MaxValue;
        public DeviceClassSnapshot this[int index] => new(DeviceClass.Camera, DeviceEnumerationStatus.Available, true);
        public IEnumerator<DeviceClassSnapshot> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                Assert.True(++ItemsRead <= 12, "Provider read past its bounded class input.");
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
