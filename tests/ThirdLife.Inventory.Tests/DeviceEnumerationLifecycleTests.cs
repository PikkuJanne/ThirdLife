using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class DeviceEnumerationLifecycleTests
{
    public static IEnumerable<object[]> EveryClass => Enum.GetValues<DeviceClass>()
        .Select(static deviceClass => new object[] { (int)deviceClass });

    [Fact]
    public async Task ManualTimeoutWithholdsLateEvidenceAndRetainsOneWorkerUntilCleanup()
    {
        using var release = new ManualResetEventSlim();
        using var clock = new ManualTimeoutTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new DeviceEnumerationProvider(new Source(token =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15), CancellationToken.None),
                    "The synthetic native worker was not released.");
            }
            return Healthy();
        }), ProviderEvidenceOrigin.SyntheticFixture);
        var captured = new CapturingProvider(provider);
        var pending = new InventoryProviderRunner(clock).RunAsync(captured).AsTask();
        ProviderRunResult? timedOut = null;
        string? original = null;
        Exception? workerError = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireTimeout();
            timedOut = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProviderRunOutcome.TimedOut, timedOut.Outcome);
            Assert.Equal("provider_collection_timed_out", timedOut.Error!.ErrorCode);
            Assert.All(timedOut.Observations, static fact => Assert.Null(fact.Value));
            original = JsonSerializer.Serialize(timedOut);
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var busy = await provider.ObserveAsync();
                Assert.Equal(ProviderReadStatus.Unavailable, busy.Status);
                Assert.Empty(busy.Evidence);
            }
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
            try { await captured.Collection!.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException error) { workerError = error; }
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.IsAssignableFrom<OperationCanceledException>(workerError);
        Assert.Equal(original, JsonSerializer.Serialize(timedOut));
        Assert.All(timedOut!.Observations, static fact => Assert.Null(fact.Value));
        var retry = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Completed, retry.Outcome);
        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.True(Assert.Single(retry.Observations, static fact => fact.EvidenceKey.Value == "devices.camera.video_interface_present").Value!.BooleanValue);
    }

    [Theory]
    [MemberData(nameof(EveryClass))]
    public async Task AggregateCleanupFailurePrecedesCancellationAndStopsLaterReads(int failedClass)
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new List<DeviceClass>();
        var source = new ClassSource((deviceClass, _) =>
        {
            calls.Add(deviceClass);
            if ((int)deviceClass == failedClass)
            {
                cancellation.Cancel();
                return new(deviceClass, DeviceEnumerationStatus.Available, true, CleanupComplete: false);
            }
            return new(deviceClass, DeviceEnumerationStatus.Available, true);
        });
        var provider = new DeviceEnumerationProvider(new WindowsDeviceEnumerationSource(source, source),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await provider.ObserveAsync(cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Equal(Enum.GetValues<DeviceClass>().Take(failedClass), calls);
        var retry = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.CleanupIncomplete, retry.Outcome);
        Assert.All(retry.Observations, static fact => Assert.Null(fact.Value));
        Assert.Equal(failedClass, calls.Count);
    }

    [Theory]
    [MemberData(nameof(EveryClass))]
    public async Task AggregateCancellationStopsLaterReadsAndWithholdsPartialEvidence(int cancelledClass)
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new List<DeviceClass>();
        var source = new ClassSource((deviceClass, _) =>
        {
            calls.Add(deviceClass);
            if ((int)deviceClass == cancelledClass) { cancellation.Cancel(); }
            return new(deviceClass, DeviceEnumerationStatus.Available, true);
        });
        var provider = new DeviceEnumerationProvider(new WindowsDeviceEnumerationSource(source, source),
            ProviderEvidenceOrigin.SyntheticFixture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ObserveAsync(cancellation.Token).AsTask());

        Assert.Equal(Enum.GetValues<DeviceClass>().Take(cancelledClass), calls);
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
        Assert.Equal(cancelledClass + 11, calls.Count);
    }

    [Fact]
    public async Task SnapshotCleanupFailurePermanentlyBlocksProviderEvenWithCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var provider = new DeviceEnumerationProvider(new Source(_ =>
        {
            calls++;
            cancellation.Cancel();
            var snapshot = Healthy();
            snapshot[4] = snapshot[4] with { CleanupComplete = false };
            return snapshot;
        }), ProviderEvidenceOrigin.SyntheticFixture);

        var result = await provider.ObserveAsync(cancellation.Token);

        Assert.Equal(ProviderReadStatus.CleanupIncomplete, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, (await provider.ObserveAsync()).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CleanupExceptionPermanentlyBlocksProvider()
    {
        var calls = 0;
        var provider = new DeviceEnumerationProvider(new Source(_ =>
        {
            calls++;
            throw new DeviceEnumerationCleanupException();
        }), ProviderEvidenceOrigin.SyntheticFixture);

        Assert.Equal(ProviderReadStatus.CleanupIncomplete, (await provider.ObserveAsync()).Status);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, (await provider.ObserveAsync()).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PreCancellationInvokesNoSourceAndStillAllowsFreshCollection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var provider = new DeviceEnumerationProvider(new Source(_ => { calls++; return Healthy(); }),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, calls);
        Assert.All(result.Observations, static fact =>
        {
            Assert.Null(fact.Value);
            Assert.Equal(ProvenanceKind.SystemGenerated, fact.Metadata.Provenance.Kind);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ObserveAsync(cancellation.Token).AsTask());
        Assert.Equal(0, calls);
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SourceExceptionCannotExposeItsPrivateMessageAndAllowsRecovery()
    {
        const string canary = "SYNTHETIC-PRIVATE-SSID-IP-MAC-DEVICE-NAME";
        var calls = 0;
        var provider = new DeviceEnumerationProvider(new Source(_ => ++calls == 1
            ? throw new InvalidOperationException(canary) : Healthy()), ProviderEvidenceOrigin.SyntheticFixture);

        var failed = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Failed, failed.Outcome);
        Assert.All(failed.Observations, static fact => Assert.Null(fact.Value));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(failed), StringComparison.Ordinal);
        Assert.Equal(ProviderRunOutcome.Completed, (await new InventoryProviderRunner().RunAsync(provider)).Outcome);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(1, ProviderReadStatus.AccessDenied, "access_denied")]
    [InlineData(2, ProviderReadStatus.Unavailable, "unavailable")]
    [InlineData(3, ProviderReadStatus.Unavailable, "unavailable")]
    [InlineData(4, ProviderReadStatus.InvalidData, "invalid_data")]
    public async Task NativeClassExceptionRemainsLocalAndCannotExposeIdentities(int exceptionType,
        ProviderReadStatus expected, string classStatus)
    {
        const string canary = "SYNTHETIC-SSID-PRIVATE-DEVICE-192.0.2.1-00:11:22:33:44:55";
        var calls = new List<DeviceClass>();
        var source = new ClassSource((deviceClass, _) =>
        {
            calls.Add(deviceClass);
            if (deviceClass == DeviceClass.Camera)
            {
                throw exceptionType switch
                {
                    1 => new UnauthorizedAccessException(canary),
                    2 => new DllNotFoundException(canary),
                    3 => new EntryPointNotFoundException(canary),
                    _ => new InvalidOperationException(canary),
                };
            }
            return new(deviceClass, DeviceEnumerationStatus.Available, true);
        });
        var provider = new DeviceEnumerationProvider(new WindowsDeviceEnumerationSource(source, source),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await provider.ObserveAsync();

        Assert.Equal(expected, result.Status);
        Assert.Equal(Enum.GetValues<DeviceClass>(), calls);
        Assert.Null(Assert.Single(result.Evidence, static fact => fact.EvidenceKey.Value == "devices.camera.video_interface_present").Value);
        Assert.Equal(classStatus, Assert.Single(result.Evidence,
            static fact => fact.EvidenceKey.Value == "devices.camera.collection_status").Value!.StringValue);
        Assert.True(Assert.Single(result.Evidence, static fact => fact.EvidenceKey.Value == "devices.usb_controller.host_interface_present").Value!.BooleanValue);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void AggregateUsesNetworkSourceOnlyForWifiAndEthernet()
    {
        var nativeClasses = new List<DeviceClass>();
        var networkClasses = new List<DeviceClass>();
        var aggregate = new WindowsDeviceEnumerationSource(
            new ClassSource((deviceClass, _) => { nativeClasses.Add(deviceClass); return new(deviceClass, DeviceEnumerationStatus.Available, true); }),
            new ClassSource((deviceClass, _) => { networkClasses.Add(deviceClass); return new(deviceClass, DeviceEnumerationStatus.Available, true); }));

        var result = aggregate.Read(CancellationToken.None);

        Assert.Equal(new[] { DeviceClass.Wifi, DeviceClass.Ethernet }, networkClasses);
        Assert.Equal(Enum.GetValues<DeviceClass>().Skip(2), nativeClasses);
        Assert.Equal(11, result.Count);
    }

    private static DeviceClassSnapshot[] Healthy() => Enum.GetValues<DeviceClass>()
        .Select(static deviceClass => new DeviceClassSnapshot(deviceClass, DeviceEnumerationStatus.Available, true, 0)).ToArray();

    private sealed class Source(Func<CancellationToken, IReadOnlyList<DeviceClassSnapshot>> read) : IDeviceEnumerationSource
    {
        public IReadOnlyList<DeviceClassSnapshot> Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class ClassSource(Func<DeviceClass, CancellationToken, DeviceClassSnapshot> read) : IDeviceClassSource
    {
        public DeviceClassSnapshot Read(DeviceClass deviceClass, CancellationToken cancellationToken) => read(deviceClass, cancellationToken);
    }

    private sealed class CapturingProvider(DeviceEnumerationProvider provider) : IInventoryProvider
    {
        public InventoryProviderDescriptor Descriptor => provider.Descriptor;
        public Task<ProviderReadResult>? Collection { get; private set; }
        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            Collection = provider.ObserveAsync(cancellationToken).AsTask();
            return new(Collection);
        }
    }

    private sealed class ManualTimeoutTimeProvider : TimeProvider, IDisposable
    {
        private ManualTimeoutTimer? _timer;
        public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(15), dueTime);
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            _timer = new(callback, state);
            TimerCreated.TrySetResult();
            return _timer;
        }

        public void FireTimeout() => _timer!.Fire();
        public void Dispose() => _timer?.Dispose();
    }

    private sealed class ManualTimeoutTimer(TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;
        public void Fire()
        {
            if (Volatile.Read(ref _disposed) == 0) { callback(state); }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _disposed) == 0;
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
