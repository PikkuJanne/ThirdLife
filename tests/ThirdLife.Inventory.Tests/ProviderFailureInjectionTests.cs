using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Tests;

public sealed class ProviderFailureInjectionTests
{
    [Fact]
    public async Task FI001SmcProviderUnavailableRemainsFailClosed()
    {
        const string sensitiveSeed = "C:\\Users\\NamedPerson\\provider-raw-secret.txt";
        var definitions = ProviderTestData.DefaultDefinitions();
        var retained = NormalizedEvidence.Observed(
            definitions[0].EvidenceKey,
            EvidenceValue.FromBoolean(true));
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var cases = new (FakeInventoryProvider Provider, ProviderRunOutcome Outcome, bool RetainsUnrelated)[]
        {
            (
                new FakeInventoryProvider(
                    ProviderTestData.Descriptor(definitions),
                    _ => ValueTask.FromResult(ProviderReadResult.Unavailable([retained]))),
                ProviderRunOutcome.Unavailable,
                true),
            (
                new FakeInventoryProvider(
                    ProviderTestData.Descriptor(definitions),
                    _ => ValueTask.FromResult(ProviderReadResult.AccessDenied())),
                ProviderRunOutcome.AccessDenied,
                false),
            (
                new FakeInventoryProvider(
                    ProviderTestData.Descriptor(definitions),
                    _ => ValueTask.FromResult(ProviderReadResult.Collected(
                    [
                        NormalizedEvidence.Observed(
                            definitions[0].EvidenceKey,
                            EvidenceValue.FromInteger(1)),
                        NormalizedEvidence.Observed(
                            definitions[1].EvidenceKey,
                            EvidenceValue.FromInteger(2)),
                    ]))),
                ProviderRunOutcome.ContractInvalid,
                false),
            (
                new FakeInventoryProvider(
                    ProviderTestData.Descriptor(definitions),
                    _ => ValueTask.FromException<ProviderReadResult>(
                        new InvalidOperationException(sensitiveSeed))),
                ProviderRunOutcome.Failed,
                false),
            (
                new FakeInventoryProvider(
                    ProviderTestData.Descriptor(definitions, timeout: TimeSpan.FromMilliseconds(50)),
                    async cancellationToken =>
                    {
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                            return ProviderReadResult.Failed();
                        }
                        finally
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                cancellationObserved.TrySetResult(true);
                            }
                        }
                    }),
                ProviderRunOutcome.TimedOut,
                false),
        };

        foreach (var item in cases)
        {
            var result = await new InventoryProviderRunner().RunAsync(item.Provider);
            var serialized = JsonSerializer.Serialize(result);
            var affected = result.Observations
                .Where(observation => observation.EvidenceKey == definitions[1].EvidenceKey)
                .ToArray();
            var providerStatus = Assert.Single(
                result.Observations,
                observation => observation.EvidenceKey == item.Provider.Descriptor.FailureDefinition.EvidenceKey);

            Assert.Equal(item.Outcome, result.Outcome);
            Assert.NotNull(result.Error);
            Assert.True(Enum.IsDefined(result.Error.RecoveryAction));
            Assert.Equal(1, item.Provider.InvocationCount);
            Assert.NotEmpty(affected);
            Assert.All(affected, observation =>
            {
                Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
                Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
                Assert.Null(observation.Value);
                Assert.NotNull(observation.LimitationCode);
            });
            Assert.Equal(EvidenceClassification.NotAvailable, providerStatus.Metadata.EvidenceClassification);
            Assert.Equal(ValueAvailability.Unknown, providerStatus.Metadata.ValueAvailability);
            Assert.Equal(result.Error.ErrorCode, providerStatus.LimitationCode);
            Assert.DoesNotContain(sensitiveSeed, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("NamedPerson", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("provider-raw-secret", serialized, StringComparison.Ordinal);

            var unrelated = result.Observations
                .Where(observation => observation.EvidenceKey == definitions[0].EvidenceKey)
                .ToArray();
            if (item.RetainsUnrelated)
            {
                Assert.True(Assert.Single(unrelated).Value!.BooleanValue!.Value);
            }
            else
            {
                Assert.All(unrelated, observation => Assert.Null(observation.Value));
            }
        }

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(ValueAvailability.Available, retained.ValueAvailability);
    }
}
