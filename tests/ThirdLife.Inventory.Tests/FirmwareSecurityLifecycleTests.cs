using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class FirmwareSecurityLifecycleTests
{
    private static readonly string[] SourceOrder = ["platform", "guard", "tpm"];

    [Fact]
    public async Task ManualTimeoutWithholdsLateEvidenceAndRetainsOneWorkerUntilCleanup()
    {
        using var release = new ManualResetEventSlim();
        using var clock = new ManualTimeoutTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new FirmwareSecurityProvider(new Source(token =>
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
        Assert.Equal(TimeSpan.FromSeconds(15), provider.Descriptor.Timeout);
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
            Assert.All(timedOut.Observations, static value => Assert.Null(value.Value));
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
            try
            {
                await captured.Collection!.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException error)
            {
                workerError = error;
            }

            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.IsAssignableFrom<OperationCanceledException>(workerError);
        Assert.Equal(original, JsonSerializer.Serialize(timedOut));
        Assert.All(timedOut!.Observations, static value => Assert.Null(value.Value));
        var retry = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Completed, retry.Outcome);
        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.True(Assert.Single(retry.Observations, static value => value.EvidenceKey.Value == "tpm.ready").Value!.BooleanValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AggregateCleanupFailurePrecedesCancellationAndStopsLaterReads(int failure)
    {
        using var cancellation = new CancellationTokenSource();
        var healthy = Healthy();
        var calls = new List<string>();
        var aggregate = new WindowsFirmwareSecuritySource(
            new PlatformSource(_ => { calls.Add("platform"); return healthy.Platform; }),
            new GuardSource(_ =>
            {
                calls.Add("guard");
                if (failure != 0) { return healthy.DeviceGuard; }
                cancellation.Cancel();
                return healthy.DeviceGuard with { CleanupComplete = false };
            }),
            new TpmSource(_ =>
            {
                calls.Add("tpm");
                cancellation.Cancel();
                var wmi = failure switch
                {
                    1 => healthy.Tpm.Wmi with { CleanupComplete = false },
                    2 => healthy.Tpm.Wmi with { CollectionInfo = new(FirmwareCollectionStatus.CleanupIncomplete) },
                    _ => healthy.Tpm.Wmi with
                    {
                        Readiness = healthy.Tpm.Wmi.Readiness with { CollectionInfo = new(FirmwareCollectionStatus.CleanupIncomplete) },
                    },
                };
                return healthy.Tpm with { Wmi = wmi };
            }));
        var provider = new FirmwareSecurityProvider(aggregate, ProviderEvidenceOrigin.SyntheticFixture);

        var result = await provider.ObserveAsync(cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(ProviderReadStatus.CleanupIncomplete, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Equal(SourceOrder.Take(failure == 0 ? 2 : 3), calls);
        var count = calls.Count;
        var retry = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.CleanupIncomplete, retry.Outcome);
        Assert.All(retry.Observations, static value => Assert.Null(value.Value));
        Assert.Equal(count, calls.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AggregateCancellationBetweenSourcesStopsEveryLaterRead(int cancelAfter)
    {
        using var cancellation = new CancellationTokenSource();
        var healthy = Healthy();
        var calls = new List<string>();
        var aggregate = new WindowsFirmwareSecuritySource(
            new PlatformSource(_ =>
            {
                calls.Add("platform");
                if (cancelAfter == 0) { cancellation.Cancel(); }
                return healthy.Platform;
            }),
            new GuardSource(_ =>
            {
                calls.Add("guard");
                if (cancelAfter == 1) { cancellation.Cancel(); }
                return healthy.DeviceGuard;
            }),
            new TpmSource(_ =>
            {
                calls.Add("tpm");
                cancellation.Cancel();
                return healthy.Tpm;
            }));

        Assert.Throws<OperationCanceledException>(() => aggregate.Read(cancellation.Token));

        Assert.Equal(SourceOrder.Take(cancelAfter + 1), calls);
    }

    [Fact]
    public async Task PreCancellationInvokesNoSourceAndStillAllowsAFreshRequest()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var provider = new FirmwareSecurityProvider(new Source(_ => { calls++; return Healthy(); }),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, calls);
        Assert.All(result.Observations, static value =>
        {
            Assert.Null(value.Value);
            Assert.Equal(ProvenanceKind.SystemGenerated, value.Metadata.Provenance.Kind);
        });
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SourceExceptionCannotExposeItsPrivateMessageAndAllowsRecovery()
    {
        const string canary = "SYNTHETIC-PRIVATE-FIRMWARE-OWNER-RAW-PATH";
        var calls = 0;
        var provider = new FirmwareSecurityProvider(new Source(_ => ++calls == 1
            ? throw new InvalidOperationException(canary) : Healthy()), ProviderEvidenceOrigin.SyntheticFixture);

        var failed = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Failed, failed.Outcome);
        Assert.All(failed.Observations, static value => Assert.Null(value.Value));
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(failed), StringComparison.Ordinal);
        Assert.Equal(ProviderRunOutcome.Completed, (await new InventoryProviderRunner().RunAsync(provider)).Outcome);
        Assert.Equal(2, calls);
    }

    private static FirmwareSecuritySourceSnapshot Healthy()
    {
        var collected = new FirmwareCollectionInfo(FirmwareCollectionStatus.Collected);
        return new(new(Available(2U), Available(1U), Available((byte)0)),
            new(Available<IReadOnlyList<uint>>(Array.AsReadOnly(new uint[] { 1, 2 }))),
            new(new(collected, 0, new(1, 2, 3, 0)),
                new(collected, true, Available("2.0, 0, 1.59"), Available("7.2.1.0"), Available(0x49465800U),
                    Available(true), Available(true), Available(true),
                    new(collected, Available(0U), Available(0U), Available(true)))));
    }

    private static SystemInventorySourceValue<T> Available<T>(T value) where T : notnull => SystemInventorySourceValue<T>.Available(value);

    private sealed class Source(Func<CancellationToken, FirmwareSecuritySourceSnapshot> read) : IFirmwareSecuritySource
    {
        public FirmwareSecuritySourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class PlatformSource(Func<CancellationToken, FirmwarePlatformSourceSnapshot> read) : IFirmwarePlatformSource
    {
        public FirmwarePlatformSourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class GuardSource(Func<CancellationToken, DeviceGuardSourceSnapshot> read) : IDeviceGuardSource
    {
        public DeviceGuardSourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class TpmSource(Func<CancellationToken, TpmSourceSnapshot> read) : ITpmSource
    {
        public TpmSourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class CapturingProvider(FirmwareSecurityProvider provider) : IInventoryProvider
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
