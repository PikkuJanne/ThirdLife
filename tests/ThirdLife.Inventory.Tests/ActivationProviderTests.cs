using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class ActivationProviderTests
{
    private static readonly string[] AllowedNativeLibraries = ["slc.dll", "kernel32.dll"];
    private static readonly string[] AllowedActivationStates = ["activated", "unactivated", "grace_period", "notification"];
    private static readonly string[] AllowedUnknownLimitations = ["provider_values_conflict", "provider_value_missing", "provider_value_malformed"];

    [Fact]
    public void DescriptorDeclaresOneReadOnlyWindowsFactWithBoundedStandardUserCollection()
    {
        var provider = Provider([1]);
        var definition = Assert.Single(provider.Descriptor.EvidenceDefinitions);
        Assert.Equal("activation.state", definition.EvidenceKey.Value);
        Assert.Equal(EvidenceValueKind.Enum, definition.ValueKind);
        Assert.Equal(1, definition.MaximumCardinality);
        Assert.Equal("windows-activation", provider.Descriptor.ProviderId.Value);
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.Descriptor.Timeout);
        Assert.Equal(
            [ProviderOperatingSystem.Windows10, ProviderOperatingSystem.Windows11],
            provider.Descriptor.SupportedOperatingSystems);
        Assert.Empty(Assert.Single(typeof(ActivationProvider).GetConstructors()).GetParameters());
    }

    [Theory]
    [InlineData(0, "unactivated")]
    [InlineData(1, "activated")]
    [InlineData(2, "grace_period")]
    [InlineData(3, "notification")]
    public async Task KnownNumericStatusProducesOnlyActivationEvidence(int rawStatus, string expected)
    {
        var result = await new InventoryProviderRunner().RunAsync(Provider([rawStatus]));
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        var observation = Assert.Single(result.Observations);
        Assert.Equal(expected, observation.Value!.StringValue);
        Assert.Equal(EvidenceClassification.Observed, observation.Metadata.EvidenceClassification);
        Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
        Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
        Assert.Equal("activation.slc.windows_application_status", observation.Metadata.Provenance.SourceReference);
        Assert.Null(observation.LimitationCode);
        Assert.DoesNotContain("ownership", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedIdenticalStatusesAreIdempotent()
    {
        var result = await new InventoryProviderRunner().RunAsync(
            Provider(Enumerable.Repeat(1, WindowsActivationSource.MaximumStatusCount).ToArray()));
        Assert.Equal("activated", Assert.Single(result.Observations).Value!.StringValue);
    }

    [Theory]
    [InlineData(new[] { 1, 0 })]
    [InlineData(new[] { 0, 1 })]
    [InlineData(new[] { 1, 2, 1 })]
    [InlineData(new[] { 3, 0 })]
    public async Task ContradictoryRowsNeverSelectFirstLastOrAnyActivatedRow(int[] statuses)
    {
        var result = await new InventoryProviderRunner().RunAsync(Provider(statuses));
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "provider_values_conflict");
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { -1 })]
    [InlineData(new[] { 4 })]
    [InlineData(new[] { 1, int.MaxValue })]
    [InlineData(new[] { int.MinValue, 1 })]
    [InlineData(new[] { 1, 0, 99 })]
    [InlineData(new[] { 99, 0, 1 })]
    public async Task MissingAndUnrecognizedStatusesStayUnknown(int[] statuses)
    {
        var result = await new InventoryProviderRunner().RunAsync(Provider(statuses));
        AssertUnknown(result, statuses.Length == 0 ? "provider_value_missing" : "provider_value_malformed");
    }

    [Fact]
    public async Task ExcessStatusRowsFailClosed()
    {
        var result = await new InventoryProviderRunner().RunAsync(
            Provider(new int[WindowsActivationSource.MaximumStatusCount + 1]));
        AssertUnknown(result, "provider_value_malformed");
    }

    [Theory]
    [InlineData(2, ProviderRunOutcome.Unavailable, "provider_unavailable")]
    [InlineData(3, ProviderRunOutcome.AccessDenied, "provider_access_denied")]
    [InlineData(4, ProviderRunOutcome.InvalidData, "provider_data_invalid")]
    public async Task SourceFailuresProduceSanitizedUnknownState(
        int rawStatus,
        ProviderRunOutcome expected,
        string limitation)
    {
        var status = (SystemInventorySourceStatus)rawStatus;
        var value = status switch
        {
            SystemInventorySourceStatus.AccessDenied => SystemInventorySourceValue<int[]>.AccessDenied(),
            SystemInventorySourceStatus.InvalidData => SystemInventorySourceValue<int[]>.InvalidData(),
            _ => SystemInventorySourceValue<int[]>.Unavailable(),
        };
        var provider = new ActivationProvider(
            new DelegateActivationSource(_ => new ActivationSourceSnapshot(value)),
            ProviderEvidenceOrigin.SyntheticFixture);
        var result = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(expected, result.Outcome);
        AssertUnknown(result, limitation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionMessagesNeverReachEvidenceOrDiagnostics(bool denied)
    {
        const string seed = "PARTIAL-FAKE-KEY PRODUCT-ID-TEST account@example.invalid RAW-LICENSING-OUTPUT";
        var source = new DelegateActivationSource(_ => throw (denied
            ? new UnauthorizedAccessException(seed)
            : new InvalidOperationException(seed)));
        var provider = new ActivationProvider(source, ProviderEvidenceOrigin.SyntheticFixture);
        var result = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(denied ? ProviderRunOutcome.AccessDenied : ProviderRunOutcome.Failed, result.Outcome);
        AssertUnknown(result, denied ? "provider_access_denied" : "provider_failed");
        var serialized = JsonSerializer.Serialize(result);
        foreach (var marker in seed.Split(' '))
        {
            Assert.DoesNotContain(marker, serialized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PreCancellationDoesNotInvokeSource()
    {
        var called = false;
        var provider = new ActivationProvider(new DelegateActivationSource(_ =>
        {
            called = true;
            return Snapshot([1]);
        }), ProviderEvidenceOrigin.SyntheticFixture);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);
        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.False(called);
    }

    [Fact]
    public async Task InFlightNativeCallRemainsSingleUntilCancellationCleanupCompletes()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new ActivationProvider(new DelegateActivationSource(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None), "The synthetic native call was not released.");
            return Snapshot([1]);
        }), ProviderEvidenceOrigin.SyntheticFixture);
        var first = provider.ObserveAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            for (var index = 0; index < 16; index++)
            {
                Assert.Equal(ProviderReadStatus.Unavailable, (await provider.ObserveAsync()).Status);
            }

            Assert.Equal(1, Volatile.Read(ref calls));
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
    public async Task RunnerTimeoutDoesNotPublishLateEvidenceOrStartAnotherNativeCall()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new ActivationProvider(new DelegateActivationSource(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None), "The synthetic native call was not released.");
            return Snapshot([1]);
        }), ProviderEvidenceOrigin.SyntheticFixture);
        using var clock = new ManualTimeoutTimeProvider();
        var captured = new CapturingProvider(provider);
        var running = new InventoryProviderRunner(clock).RunAsync(captured).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.FireTimeout();
            var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProviderRunOutcome.TimedOut, result.Outcome);
            AssertUnknown(result, "provider_collection_timed_out");
            Assert.Equal(ProviderReadStatus.Unavailable, (await provider.ObserveAsync()).Status);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => captured.Collection!);
        Assert.Equal(ProviderReadStatus.Collected, (await provider.ObserveAsync()).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void NativeSourceCopiesOnlyStatusNumbersAndFreesBufferAndContext()
    {
        using var native = new FakeActivationNativeApi([1, 1]);
        var snapshot = new WindowsActivationSource(native).Read(CancellationToken.None);
        Assert.Equal([1, 1], snapshot.Statuses.GetRequiredValue());
        Assert.True(snapshot.CleanupComplete);
        Assert.Equal(["open", "read", "free", "close"], native.Calls);
        Assert.Equal(new Guid("55c92734-d682-4d71-983e-d6ec3f16059f"), native.ApplicationId);
        Assert.DoesNotContain("SYNTHETIC-SKU-ID", JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070005), 3)]
    [InlineData(unchecked((int)0xc004f025), 3)]
    [InlineData(unchecked((int)0xc004f002), 2)]
    [InlineData(unchecked((int)0x80004005), 2)]
    [InlineData(1, 2)]
    public void NativeFailureHresultsNeverBecomeActivated(int hresult, int expectedStatus)
    {
        using var native = new FakeActivationNativeApi([1]) { ReadResult = hresult };
        var snapshot = new WindowsActivationSource(native).Read(CancellationToken.None);
        Assert.Equal((SystemInventorySourceStatus)expectedStatus, snapshot.Statuses.Status);
        Assert.Equal(["open", "read", "free", "close"], native.Calls);
    }

    [Theory]
    [InlineData(129U, false)]
    [InlineData(uint.MaxValue, false)]
    [InlineData(1U, true)]
    public void NativeCountBoundsAreCheckedBeforeReadingMemory(uint count, bool nullBuffer)
    {
        using var native = new FakeActivationNativeApi([1]) { CountOverride = count, NullBuffer = nullBuffer };
        var snapshot = new WindowsActivationSource(native).Read(CancellationToken.None);
        Assert.Equal(SystemInventorySourceStatus.InvalidData, snapshot.Statuses.Status);
        Assert.Contains("close", native.Calls);
    }

    [Fact]
    public void NativeEmptyResultRemainsAnAbsentObservation()
    {
        using var native = new FakeActivationNativeApi([]);
        var snapshot = new WindowsActivationSource(native).Read(CancellationToken.None);
        Assert.Empty(snapshot.Statuses.GetRequiredValue());
        Assert.True(snapshot.CleanupComplete);
    }

    [Fact]
    public async Task NativeExceptionStillFreesOwnedResourcesAndSanitizesTheFailure()
    {
        using var native = new FakeActivationNativeApi([1])
        {
            OnRead = () => throw new InvalidOperationException("SYNTHETIC-RAW-LICENSING-OUTPUT"),
        };
        var provider = new ActivationProvider(new WindowsActivationSource(native), ProviderEvidenceOrigin.SyntheticFixture);
        var result = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.Failed, result.Outcome);
        Assert.Equal(["open", "read", "free", "close"], native.Calls);
        Assert.DoesNotContain("SYNTHETIC-RAW-LICENSING-OUTPUT", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCleanupFailureCannotPublishAnActivatedResult(bool closeFails)
    {
        using var native = new FakeActivationNativeApi([1])
        {
            FreeFails = !closeFails,
            CloseResult = closeFails ? -1 : 0,
        };
        var provider = new ActivationProvider(new WindowsActivationSource(native), ProviderEvidenceOrigin.SyntheticFixture);
        var result = await new InventoryProviderRunner().RunAsync(provider);
        Assert.Equal(ProviderRunOutcome.CleanupIncomplete, result.Outcome);
        AssertUnknown(result, "provider_cleanup_incomplete");
        Assert.Equal(["open", "read", "free", "close"], native.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCleanupRunsWhenCancellationOccursAtEitherCallBoundary(bool afterRead)
    {
        using var cancellation = new CancellationTokenSource();
        using var native = new FakeActivationNativeApi([1]);
        if (afterRead)
        {
            native.OnRead = cancellation.Cancel;
        }
        else
        {
            native.OnOpen = cancellation.Cancel;
        }

        Assert.ThrowsAny<OperationCanceledException>(() => new WindowsActivationSource(native).Read(cancellation.Token));
        Assert.Equal(afterRead ? ["open", "read", "free", "close"] : ["open", "close"], native.Calls);
    }

    [Fact]
    public void NativeImportsAllowOnlyReadAndResourceLifecycleFunctions()
    {
        var methods = typeof(WindowsActivationNativeApi).GetNestedType("NativeMethods", BindingFlags.NonPublic)!
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
        Assert.Equal(
            ["LocalFree", "SLClose", "SLGetLicensingStatusInformation", "SLOpen"],
            methods.Select(static method => method.Name).Order(StringComparer.Ordinal));
        Assert.All(methods, method =>
        {
            Assert.Equal(DllImportSearchPath.System32, method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()!.Paths);
            Assert.Contains(method.GetCustomAttribute<DllImportAttribute>()!.Value, AllowedNativeLibraries);
        });
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineSmokeIsUnelevatedReadOnlyAndIdentitySilent()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new InventoryProviderRunner().RunAsync(new ActivationProvider(), cancellation.Token);
        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        var observation = Assert.Single(result.Observations);
        Assert.Equal("activation.state", observation.EvidenceKey.Value);
        Assert.Equal(ProvenanceKind.ProviderObservation, observation.Metadata.Provenance.Kind);
        Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
        if (observation.Metadata.ValueAvailability == ValueAvailability.Available)
        {
            Assert.Contains(observation.Value!.StringValue, AllowedActivationStates);
        }
        else
        {
            Assert.Null(observation.Value);
            Assert.Contains(observation.LimitationCode, AllowedUnknownLimitations);
        }
    }

    private static ActivationProvider Provider(int[] statuses) =>
        new(new DelegateActivationSource(_ => Snapshot(statuses)), ProviderEvidenceOrigin.SyntheticFixture);

    private static ActivationSourceSnapshot Snapshot(int[] statuses) =>
        new(SystemInventorySourceValue<int[]>.Available(statuses));

    private static void AssertUnknown(ProviderRunResult result, string limitation)
    {
        var state = Assert.Single(result.Observations, static item => item.EvidenceKey.Value == "activation.state");
        Assert.Null(state.Value);
        Assert.Equal(ValueAvailability.Unknown, state.Metadata.ValueAvailability);
        Assert.Equal(EvidenceClassification.NotAvailable, state.Metadata.EvidenceClassification);
        Assert.Equal(limitation, state.LimitationCode);
    }

    private sealed class DelegateActivationSource(Func<CancellationToken, ActivationSourceSnapshot> read) : IActivationSource
    {
        public ActivationSourceSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class CapturingProvider(ActivationProvider provider) : IInventoryProvider
    {
        public InventoryProviderDescriptor Descriptor => provider.Descriptor;
        public Task<ProviderReadResult>? Collection { get; private set; }

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            Collection = provider.ObserveAsync(cancellationToken).AsTask();
            return new ValueTask<ProviderReadResult>(Collection);
        }
    }

    private sealed class ManualTimeoutTimeProvider : TimeProvider, IDisposable
    {
        private ManualTimeoutTimer? _timer;
        public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(5), dueTime);
            _timer = new ManualTimeoutTimer(callback, state);
            TimerCreated.TrySetResult();
            return _timer;
        }

        public void FireTimeout() => _timer!.Fire();

        public void Dispose() => _timer?.Dispose();
    }

    private sealed class ManualTimeoutTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeActivationNativeApi(int[] statuses) : IActivationNativeApi, IDisposable
    {
        private nint _ownedBuffer;

        public List<string> Calls { get; } = [];
        public Guid ApplicationId { get; private set; }
        public int ReadResult { get; init; }
        public int CloseResult { get; init; }
        public bool FreeFails { get; init; }
        public bool NullBuffer { get; init; }
        public uint? CountOverride { get; init; }
        public Action? OnOpen { get; set; }
        public Action? OnRead { get; set; }

        public int Open(out nint context)
        {
            Calls.Add("open");
            context = 1;
            OnOpen?.Invoke();
            return 0;
        }

        public int ReadStatuses(nint context, in Guid applicationId, out uint count, out nint buffer)
        {
            Assert.Equal(1, context);
            Calls.Add("read");
            ApplicationId = applicationId;
            count = CountOverride ?? (uint)statuses.Length;
            if (NullBuffer || statuses.Length == 0)
            {
                buffer = 0;
            }
            else
            {
                _ownedBuffer = Marshal.AllocHGlobal(statuses.Length * WindowsActivationSource.NativeStatusBytes);
                for (var index = 0; index < statuses.Length; index++)
                {
                    var offset = index * WindowsActivationSource.NativeStatusBytes;
                    Marshal.Copy("SYNTHETIC-SKU-ID!"u8.ToArray(), 0, _ownedBuffer + offset, 16);
                    Marshal.WriteInt32(_ownedBuffer, offset + WindowsActivationSource.NativeStatusOffset, statuses[index]);
                    Marshal.WriteInt32(_ownedBuffer, offset + 20, 0x12345678);
                    Marshal.WriteInt32(_ownedBuffer, offset + 24, 0x12345678);
                    Marshal.WriteInt32(_ownedBuffer, offset + 28, 0x12345678);
                    Marshal.WriteInt64(_ownedBuffer, offset + 32, 0x1234567812345678);
                }

                buffer = _ownedBuffer;
            }

            OnRead?.Invoke();
            return ReadResult;
        }

        public nint Free(nint buffer)
        {
            Calls.Add("free");
            Assert.Equal(_ownedBuffer, buffer);
            Dispose();
            return FreeFails ? 1 : 0;
        }

        public int Close(nint context)
        {
            Assert.Equal(1, context);
            Calls.Add("close");
            return CloseResult;
        }

        public void Dispose()
        {
            if (_ownedBuffer != 0)
            {
                Marshal.FreeHGlobal(_ownedBuffer);
                _ownedBuffer = 0;
            }
        }
    }
}
