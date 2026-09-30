using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class PendingRebootProviderTests
{
    [Fact]
    public void DescriptorDeclaresBoundedStandardUserLocalIndicators()
    {
        var provider = CreateProvider(_ => Snapshot(false, false, false));

        Assert.Equal(PendingRebootProvider.ProviderIdentity, provider.Descriptor.ProviderId.Value);
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(TimeSpan.FromSeconds(2), provider.Descriptor.Timeout);
        Assert.Equal(13, provider.Descriptor.EvidenceDefinitions.Count);
        Assert.All(provider.Descriptor.EvidenceDefinitions, definition =>
        {
            Assert.Equal(EvidenceValueKind.Enum, definition.ValueKind);
            Assert.Equal(1, definition.MaximumCardinality);
            Assert.Null(definition.Unit);
        });
        Assert.Empty(Assert.Single(typeof(PendingRebootProvider).GetConstructors()).GetParameters());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task EachPresentIndicatorIsAttributedAndOnlyInfersPending(bool cbs, bool update, bool rename)
    {
        var result = await Run(Snapshot(cbs, update, rename));

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal("pending_indicated", Get(result, "reboot.pending_status").Value!.StringValue);
        AssertIndicator(result, "cbs", cbs, "pending_reboot.registry64.cbs_key");
        AssertIndicator(result, "windows_update", update, "pending_reboot.registry64.windows_update_key");
        AssertIndicator(result, "file_rename", rename, "pending_reboot.registry64.rename_metadata");
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
            Assert.DoesNotContain("system.architecture", observation.EvidenceKey.Value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AllAbsentIndicatorsCannotEstablishThatNoRebootIsNeeded()
    {
        var result = await Run(Snapshot(false, false, false));

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "reboot.pending_status", "provider_value_missing");
        Assert.Equal("absent", Get(result, "reboot.cbs.indicator").Value!.StringValue);
        Assert.Equal("absent", Get(result, "reboot.windows_update.indicator").Value!.StringValue);
        Assert.Equal("absent", Get(result, "reboot.file_rename.indicator").Value!.StringValue);
        Assert.DoesNotContain(result.Observations, observation => observation.Value?.BooleanValue is not null);
    }

    [Fact]
    public async Task MixedSourceFailuresRetainTheirOwnStatusesRecoveryAndPositiveEvidence()
    {
        var result = await Run(new PendingRebootSnapshot(
            SystemInventorySourceValue<bool>.AccessDenied(),
            SystemInventorySourceValue<bool>.Available(true),
            SystemInventorySourceValue<bool>.InvalidData()));

        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        Assert.Equal("pending_indicated", Get(result, "reboot.pending_status").Value!.StringValue);
        Assert.Equal("access_denied", Get(result, "reboot.cbs.collection_status").Value!.StringValue);
        Assert.Equal("review_access", Get(result, "reboot.cbs.recovery").Value!.StringValue);
        AssertUnknown(result, "reboot.cbs.indicator", "provider_value_missing");
        Assert.Equal("invalid_data", Get(result, "reboot.file_rename.collection_status").Value!.StringValue);
        Assert.Equal("review_provider_data", Get(result, "reboot.file_rename.recovery").Value!.StringValue);
        AssertUnknown(result, "reboot.file_rename.indicator", "provider_value_malformed");
        Assert.Equal("present", Get(result, "reboot.windows_update.indicator").Value!.StringValue);
    }

    [Fact]
    public async Task OneAbsentAndTwoUnavailableSourcesRemainUnknownAndCanRecover()
    {
        var attempts = 0;
        var provider = CreateProvider(_ => Interlocked.Increment(ref attempts) == 1
            ? new PendingRebootSnapshot(
                SystemInventorySourceValue<bool>.Available(false),
                SystemInventorySourceValue<bool>.Unavailable(),
                SystemInventorySourceValue<bool>.Unavailable())
            : Snapshot(false, false, true));

        var first = await new InventoryProviderRunner().RunAsync(provider);
        var recovered = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Unavailable, first.Outcome);
        AssertUnknown(first, "reboot.pending_status", "provider_value_missing");
        Assert.Equal("retry_collection", Get(first, "reboot.windows_update.recovery").Value!.StringValue);
        AssertUnknown(first, "reboot.file_rename.confidence", "provider_value_missing");
        Assert.Equal(ProviderRunOutcome.Completed, recovered.Outcome);
        Assert.Equal("pending_indicated", Get(recovered, "reboot.pending_status").Value!.StringValue);
    }

    [Theory]
    [InlineData(0, (int)SystemInventorySourceStatus.Available, true)]
    [InlineData(2, (int)SystemInventorySourceStatus.Available, false)]
    [InlineData(3, (int)SystemInventorySourceStatus.Available, false)]
    [InlineData(5, (int)SystemInventorySourceStatus.AccessDenied, false)]
    [InlineData(50, (int)SystemInventorySourceStatus.Unavailable, false)]
    [InlineData(87, (int)SystemInventorySourceStatus.InvalidData, false)]
    public void KeyPresenceResultDoesNotConflateDenialWithAbsence(
        int status, int expectedStatus, bool present)
    {
        var value = WindowsPendingRebootSource.MapKeyPresence(status);
        var expected = (SystemInventorySourceStatus)expectedStatus;

        Assert.Equal(expected, value.Status);
        if (expected == SystemInventorySourceStatus.Available)
        {
            Assert.Equal(present, value.GetRequiredValue());
        }
    }

    [Theory]
    [InlineData(0, 7U, 4U, (int)SystemInventorySourceStatus.Available, true)]
    [InlineData(0, 7U, 1_048_576U, (int)SystemInventorySourceStatus.Available, true)]
    [InlineData(0, 7U, 0U, (int)SystemInventorySourceStatus.InvalidData, false)]
    [InlineData(0, 7U, 2U, (int)SystemInventorySourceStatus.InvalidData, false)]
    [InlineData(0, 7U, 5U, (int)SystemInventorySourceStatus.InvalidData, false)]
    [InlineData(0, 7U, 1_048_578U, (int)SystemInventorySourceStatus.InvalidData, false)]
    [InlineData(0, 1U, 100U, (int)SystemInventorySourceStatus.InvalidData, false)]
    [InlineData(2, 0U, 0U, (int)SystemInventorySourceStatus.Available, false)]
    [InlineData(5, 0U, 0U, (int)SystemInventorySourceStatus.AccessDenied, false)]
    [InlineData(50, 0U, 0U, (int)SystemInventorySourceStatus.Unavailable, false)]
    [InlineData(234, 7U, 16U, (int)SystemInventorySourceStatus.InvalidData, false)]
    public void RenameMetadataIsBoundedAndMalformedOrFailedReadsNeverBecomeAbsent(
        int status, uint type, uint bytes, int expectedStatus, bool present)
    {
        var value = WindowsPendingRebootSource.MapRenameMetadata(status, type, bytes);
        var expected = (SystemInventorySourceStatus)expectedStatus;

        Assert.Equal(expected, value.Status);
        if (expected == SystemInventorySourceStatus.Available)
        {
            Assert.Equal(present, value.GetRequiredValue());
        }
    }

    [Fact]
    public async Task PreCancelledReadDoesNotInvokeSource()
    {
        var invoked = false;
        var provider = CreateProvider(_ =>
        {
            invoked = true;
            return Snapshot(false, false, false);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.False(invoked);
        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.All(result.Observations, static observation => Assert.Null(observation.Value));
    }

    [Fact]
    public async Task TimeoutKeepsSingleFlightGuardUntilSourceReturnsThenAllowsRecovery()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var provider = CreateProvider(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.TrySetResult(true);
                try
                {
                    if (!release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None))
                    {
                        throw new TimeoutException("The synthetic source release was not signalled.");
                    }
                }
                finally
                {
                    exited.TrySetResult(true);
                }
            }

            return Snapshot(false, false, true);
        });

        var pending = new InventoryProviderRunner().RunAsync(provider).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var timedOut = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProviderRunOutcome.TimedOut, timedOut.Outcome);
            var busy = await new InventoryProviderRunner().RunAsync(provider);
            Assert.Equal(ProviderRunOutcome.Unavailable, busy.Outcome);
            Assert.Equal(1, Volatile.Read(ref attempts));
            Assert.All(busy.Observations, static observation => Assert.Null(observation.Value));
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
    public async Task MidReadCancellationPublishesNoIndicators()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var provider = CreateProvider(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                cancellation.Cancel();
            }

            return Snapshot(true, true, true);
        });

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.All(result.Observations, static observation => Assert.Null(observation.Value));
    }

    [Fact]
    public async Task SourceExceptionIsSanitizedAndDoesNotPoisonTheNextRead()
    {
        const string syntheticSensitiveDetail = "SYNTHETIC-PRIVATE-PATH-MUST-NOT-ESCAPE";
        var attempts = 0;
        var provider = CreateProvider(_ => Interlocked.Increment(ref attempts) == 1
            ? throw new InvalidOperationException(syntheticSensitiveDetail)
            : Snapshot(false, false, false));

        var failed = await new InventoryProviderRunner().RunAsync(provider);
        var recovered = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Failed, failed.Outcome);
        Assert.Equal(ProviderRecoveryAction.ReviewUnexpectedFailure, failed.Error!.RecoveryAction);
        Assert.DoesNotContain(syntheticSensitiveDetail, JsonSerializer.Serialize(failed), StringComparison.Ordinal);
        Assert.Equal(ProviderRunOutcome.Completed, recovered.Outcome);
    }

    [Fact]
    public void NativeBoundaryExposesOnlyOpenAndMetadataQueryWithoutPayloadBuffer()
    {
        var native = typeof(WindowsPendingRebootSource).GetNestedType("NativeMethods", BindingFlags.NonPublic);
        Assert.NotNull(native);
        var imports = native.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<DllImportAttribute>() is not null)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["RegOpenKeyExW", "RegQueryValueExW"], imports.Select(method => method.Name));
        Assert.All(imports, method =>
        {
            Assert.Equal("advapi32.dll", method.GetCustomAttribute<DllImportAttribute>()!.Value);
            Assert.Equal(DllImportSearchPath.System32, method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()!.Paths);
        });
        var query = imports[1].GetParameters();
        Assert.Equal(typeof(nint), Assert.Single(query, parameter => parameter.Name == "lpData").ParameterType);
        Assert.DoesNotContain(query, parameter => parameter.ParameterType == typeof(byte[]));
        Assert.DoesNotContain(typeof(WindowsPendingRebootSource).GetMethods(), method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineRebootSmokeIsUnelevatedBoundedAndDoesNotExposePaths()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var provider = new PendingRebootProvider();

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Contains(result.Outcome, new[]
        {
            ProviderRunOutcome.Completed,
            ProviderRunOutcome.AccessDenied,
            ProviderRunOutcome.Unavailable,
            ProviderRunOutcome.InvalidData,
        });
        Assert.InRange(result.Observations.Count, 13, 14);
        var allowedValues = new HashSet<string>(StringComparer.Ordinal)
        {
            "present", "absent", "indicator_only", "pending_indicated", "available", "access_denied",
            "unavailable", "invalid_data", "none", "review_access", "retry_collection", "review_provider_data",
        };
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(ProvenanceKind.ProviderObservation, observation.Metadata.Provenance.Kind);
            if (observation.Value is not null)
            {
                Assert.Equal(EvidenceValueKind.Enum, observation.Value.Kind);
                Assert.Contains(observation.Value.StringValue!, allowedValues);
            }
        });
        Assert.True(Get(result, "reboot.pending_status").Value?.StringValue is null or "pending_indicated");
    }

    private static void AssertIndicator(ProviderRunResult result, string source, bool present, string provenance)
    {
        var indicator = Get(result, $"reboot.{source}.indicator");
        Assert.Equal(present ? "present" : "absent", indicator.Value!.StringValue);
        Assert.Equal(EvidenceClassification.Inferred, indicator.Metadata.EvidenceClassification);
        Assert.Equal(provenance, indicator.Metadata.Provenance.SourceReference);
        Assert.Equal("indicator_only", Get(result, $"reboot.{source}.confidence").Value!.StringValue);
    }

    private static void AssertUnknown(ProviderRunResult result, string key, string limitation)
    {
        var observation = Get(result, key);
        Assert.Null(observation.Value);
        Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
        Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
        Assert.Equal(limitation, observation.LimitationCode);
    }

    private static Observation Get(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key);

    private static PendingRebootProvider CreateProvider(Func<CancellationToken, PendingRebootSnapshot> read) =>
        new(new DelegateSource(read), ProviderEvidenceOrigin.SyntheticFixture);

    private static ValueTask<ProviderRunResult> Run(PendingRebootSnapshot snapshot) =>
        new InventoryProviderRunner().RunAsync(CreateProvider(_ => snapshot));

    private static PendingRebootSnapshot Snapshot(bool cbs, bool update, bool rename) =>
        new(
            SystemInventorySourceValue<bool>.Available(cbs),
            SystemInventorySourceValue<bool>.Available(update),
            SystemInventorySourceValue<bool>.Available(rename));

    private sealed class DelegateSource(Func<CancellationToken, PendingRebootSnapshot> read) : IPendingRebootSource
    {
        public PendingRebootSnapshot Read(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
