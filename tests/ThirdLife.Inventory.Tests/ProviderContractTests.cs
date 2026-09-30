using System.Reflection;
using System.Text.Json;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;

namespace ThirdLife.Inventory.Tests;

public sealed class ProviderContractTests
{
    [Fact]
    public void ProviderDescriptorDeclaresRequiredMetadataAndDefensivelyCopiesCollections()
    {
        var operatingSystems = new[]
        {
            ProviderOperatingSystem.Windows11,
            ProviderOperatingSystem.Windows10,
        };
        var definitions = new[]
        {
            ProviderTestData.Definition("system.zeta", EvidenceValueKind.Boolean),
            ProviderTestData.Definition("system.alpha", EvidenceValueKind.WholeNumber, "bytes"),
        };

        var descriptor = new InventoryProviderDescriptor(
            new ProviderId("provider-test"),
            ProviderPrivilegeRequirement.AdministratorReadOnly,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            ProviderNetworkUse.None,
            ProviderEvidenceOrigin.ActiveMachine,
            operatingSystems,
            new ProviderFailureDefinition(new EvidenceKey("provider.test.failure"), "provider.test.status"),
            definitions);

        operatingSystems[0] = ProviderOperatingSystem.Windows10;
        definitions[0] = ProviderTestData.Definition("system.replaced", EvidenceValueKind.Text);

        Assert.Equal("provider-test", descriptor.ProviderId.Value);
        Assert.Equal(ProviderPrivilegeRequirement.AdministratorReadOnly, descriptor.MinimumPrivilege);
        Assert.Equal(TimeSpan.FromSeconds(2), descriptor.ExpectedDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), descriptor.Timeout);
        Assert.Equal(ProviderNetworkUse.None, descriptor.NetworkUse);
        Assert.Equal(ProviderEvidenceOrigin.ActiveMachine, descriptor.EvidenceOrigin);
        Assert.Equal(
            [ProviderOperatingSystem.Windows10, ProviderOperatingSystem.Windows11],
            descriptor.SupportedOperatingSystems);
        Assert.Equal(
            ["system.alpha", "system.zeta"],
            descriptor.EvidenceDefinitions.Select(static value => value.EvidenceKey.Value));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ProviderOperatingSystem>)descriptor.SupportedOperatingSystems)
                .Add(ProviderOperatingSystem.Windows11));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ProviderEvidenceDefinition>)descriptor.EvidenceDefinitions)
                .Add(ProviderTestData.Definition("system.extra", EvidenceValueKind.Boolean)));
    }

    [Fact]
    public void ProviderDescriptorRejectsInvalidOrUnboundedDeclarations()
    {
        var validDefinition = ProviderTestData.Definition("system.valid", EvidenceValueKind.Boolean);
        var duplicateDefinition = ProviderTestData.Definition("system.valid", EvidenceValueKind.Boolean);
        var failure = new ProviderFailureDefinition(new EvidenceKey("provider.test.failure"), "provider.test.status");

        var invalidDeclarations = new Action[]
        {
            () => CreateDescriptor(expectedDuration: TimeSpan.Zero),
            () => CreateDescriptor(expectedDuration: TimeSpan.FromSeconds(2), timeout: TimeSpan.FromSeconds(1)),
            () => CreateDescriptor(timeout: InventoryProviderDescriptor.MaximumTimeout + TimeSpan.FromTicks(1)),
            () => CreateDescriptor(minimumPrivilege: (ProviderPrivilegeRequirement)0),
            () => CreateDescriptor(networkUse: (ProviderNetworkUse)2),
            () => CreateDescriptor(evidenceOrigin: (ProviderEvidenceOrigin)0),
            () => CreateDescriptor(supportedOperatingSystems: []),
            () => CreateDescriptor(supportedOperatingSystems:
                [ProviderOperatingSystem.Windows11, ProviderOperatingSystem.Windows11]),
            () => CreateDescriptor(supportedOperatingSystems: [(ProviderOperatingSystem)0]),
            () => CreateDescriptor(evidenceDefinitions: []),
            () => CreateDescriptor(evidenceDefinitions: [validDefinition, duplicateDefinition]),
            () => CreateDescriptor(
                failureDefinition: new ProviderFailureDefinition(
                    validDefinition.EvidenceKey,
                    "provider.test.status")),
            () => CreateDescriptor(evidenceDefinitions:
            [
                ProviderTestData.Definition(
                    "system.first",
                    EvidenceValueKind.Boolean,
                    maximumCardinality: ProviderEvidenceDefinition.MaximumAllowedCardinality),
                ProviderTestData.Definition(
                    "system.second",
                    EvidenceValueKind.Boolean,
                    maximumCardinality: ProviderEvidenceDefinition.MaximumAllowedCardinality),
            ]),
        };

        foreach (var declaration in invalidDeclarations)
        {
            Assert.ThrowsAny<ArgumentException>(declaration);
        }

        InventoryProviderDescriptor CreateDescriptor(
            ProviderPrivilegeRequirement minimumPrivilege = ProviderPrivilegeRequirement.StandardUser,
            TimeSpan? expectedDuration = null,
            TimeSpan? timeout = null,
            ProviderNetworkUse networkUse = ProviderNetworkUse.None,
            ProviderEvidenceOrigin evidenceOrigin = ProviderEvidenceOrigin.SyntheticFixture,
            IEnumerable<ProviderOperatingSystem>? supportedOperatingSystems = null,
            ProviderFailureDefinition? failureDefinition = null,
            IEnumerable<ProviderEvidenceDefinition>? evidenceDefinitions = null) =>
            new(
                new ProviderId("provider-test"),
                minimumPrivilege,
                expectedDuration ?? TimeSpan.FromSeconds(1),
                timeout ?? TimeSpan.FromSeconds(2),
                networkUse,
                evidenceOrigin,
                supportedOperatingSystems ?? [ProviderOperatingSystem.Windows11],
                failureDefinition ?? failure,
                evidenceDefinitions ?? [validDefinition]);
    }

    [Fact]
    public void EvidenceDefinitionsBoundKindUnitSourceAndCardinality()
    {
        var definition = new ProviderEvidenceDefinition(
            new EvidenceKey("storage.disk.bytes"),
            EvidenceValueKind.WholeNumber,
            "bytes",
            "storage.fixed_api",
            maximumCardinality: 4);

        Assert.Equal(EvidenceValueKind.WholeNumber, definition.ValueKind);
        Assert.Equal("bytes", definition.Unit);
        Assert.Equal("storage.fixed_api", definition.SourceReference);
        Assert.Equal(4, definition.MaximumCardinality);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderEvidenceDefinition(
            new EvidenceKey("storage.disk.bytes"),
            (EvidenceValueKind)0,
            "bytes",
            "storage.fixed_api"));
        Assert.Throws<ArgumentException>(() => new ProviderEvidenceDefinition(
            new EvidenceKey("storage.disk.bytes"),
            EvidenceValueKind.WholeNumber,
            "raw bytes",
            "storage.fixed_api"));
        Assert.Throws<ArgumentException>(() => new ProviderEvidenceDefinition(
            new EvidenceKey("storage.disk.bytes"),
            EvidenceValueKind.WholeNumber,
            "bytes",
            "C:\\Users\\Person\\raw.txt"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderEvidenceDefinition(
            new EvidenceKey("storage.disk.bytes"),
            EvidenceValueKind.WholeNumber,
            "bytes",
            "storage.fixed_api",
            maximumCardinality: 0));
    }

    [Fact]
    public void ProviderInterfaceIsReadOnlyCancellableAndExposesNoWindowsApiTypes()
    {
        var operation = Assert.Single(
            typeof(IInventoryProvider).GetMethods(),
            static method => !method.IsSpecialName);

        Assert.Equal("ObserveAsync", operation.Name);
        Assert.Equal(typeof(ValueTask<ProviderReadResult>), operation.ReturnType);
        Assert.Equal(typeof(CancellationToken), Assert.Single(operation.GetParameters()).ParameterType);
        Assert.Null(typeof(IInventoryProvider).GetProperty(nameof(IInventoryProvider.Descriptor))!.SetMethod);
        Assert.Single(Enum.GetValues<ProviderNetworkUse>());

        var forbiddenTypeFragments = new[]
        {
            "System.Diagnostics.Process",
            "System.Management",
            "System.Management.Automation",
            "Microsoft.Win32",
            "System.Net.Http",
            "System.Net.Sockets",
        };
        var forbiddenOperationPrefixes = new[]
        {
            "Set", "Write", "Apply", "Delete", "Install", "Execute", "Launch", "Start", "Stop",
        };
        var forbiddenMemberFragments = new[]
        {
            "Command", "Arguments", "Script", "Executable", "RegistryPath", "Url", "Uri",
        };
        var assembly = typeof(IInventoryProvider).Assembly;

        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.Null(property.SetMethod);
                Assert.DoesNotContain(
                    forbiddenMemberFragments,
                    fragment =>
                        property.Name.StartsWith(fragment, StringComparison.OrdinalIgnoreCase) ||
                        property.Name.EndsWith(fragment, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(
                    forbiddenOperationPrefixes,
                    prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal));
                Assert.DoesNotContain(
                    forbiddenMemberFragments,
                    fragment =>
                        method.Name.StartsWith(fragment, StringComparison.OrdinalIgnoreCase) ||
                        method.Name.EndsWith(fragment, StringComparison.OrdinalIgnoreCase));
                Assert.All(method.GetParameters(), parameter => Assert.DoesNotContain(
                    forbiddenMemberFragments,
                    fragment =>
                        parameter.Name?.StartsWith(fragment, StringComparison.OrdinalIgnoreCase) == true ||
                        parameter.Name?.EndsWith(fragment, StringComparison.OrdinalIgnoreCase) == true));

                var signature = string.Join(
                    '|',
                    method.ReturnType.FullName,
                    string.Join(',', method.GetParameters().Select(static value => value.ParameterType.FullName)));
                foreach (var forbidden in forbiddenTypeFragments)
                {
                    Assert.DoesNotContain(forbidden, signature, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void InventoryContractSourcesUseNoMutationShellOrNetworkApis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src", "ThirdLife.Inventory");
        var contractFiles = new[]
        {
            Path.Combine(sourceRoot, "Providers", "IInventoryProvider.cs"),
            Path.Combine(sourceRoot, "Providers", "InventoryProviderDescriptor.cs"),
            Path.Combine(sourceRoot, "Providers", "ProviderEvidenceDefinition.cs"),
            Path.Combine(sourceRoot, "Providers", "ProviderFailureDefinition.cs"),
            Path.Combine(sourceRoot, "Providers", "ProviderReadResult.cs"),
        };
        var normalizationFiles = Directory.EnumerateFiles(
            Path.Combine(sourceRoot, "Normalization"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var source = string.Join(
            '\n',
            contractFiles
                .Concat(normalizationFiles)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var forbiddenFragments = new[]
        {
            "System.Diagnostics.Process",
            "ProcessStartInfo",
            "System.Management",
            "System.Management.Automation",
            "Microsoft.Win32",
            "PowerShell",
            "pwsh.exe",
            "cmd.exe",
            "Process.Start",
            "RegistryKey",
            "Registry.Create",
            "Registry.Delete",
            "Registry.SetValue",
            "File.Append",
            "File.Create",
            "File.Move",
            "File.Replace",
            "File.Write",
            "File.Delete",
            "Directory.CreateDirectory",
            "Directory.Move",
            "Directory.Delete",
            "InvokeMethod",
            "ServiceController",
            "Environment.Set",
            "System.Net.Http",
            "HttpClient",
            "System.Net.Sockets",
            "\"runas\"",
        };

        foreach (var forbidden in forbiddenFragments)
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ManagedFakePreservesClassificationUncertaintyAndAttribution()
    {
        var timestamp = new DateTimeOffset(2032, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var definitions = new[]
        {
            ProviderTestData.Definition("system.observed", EvidenceValueKind.Boolean),
            ProviderTestData.Definition("system.inferred", EvidenceValueKind.WholeNumber, "count"),
            ProviderTestData.Definition("system.unknown", EvidenceValueKind.Text),
            ProviderTestData.Definition("system.not_applicable", EvidenceValueKind.Boolean),
        };
        var descriptor = ProviderTestData.Descriptor(definitions);
        var provider = new FakeInventoryProvider(
            descriptor,
            _ => ValueTask.FromResult(ProviderReadResult.Collected(
            [
                NormalizedEvidence.NotApplicable(
                    definitions[3].EvidenceKey,
                    ProviderLimitation.CapabilityNotPresent),
                NormalizedEvidence.NotAvailable(
                    definitions[2].EvidenceKey,
                    ProviderLimitation.SourceValueMissing),
                NormalizedEvidence.Inferred(
                    definitions[1].EvidenceKey,
                    EvidenceValue.FromInteger(2),
                    ProviderLimitation.SourceValuesConflict),
                NormalizedEvidence.Observed(
                    definitions[0].EvidenceKey,
                    EvidenceValue.FromBoolean(true)),
            ])));

        var result = await new InventoryProviderRunner(new FixedTimeProvider(timestamp)).RunAsync(provider);
        var observations = result.Observations.ToDictionary(static value => value.EvidenceKey.Value);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(4, observations.Count);
        Assert.Equal(EvidenceClassification.Observed, observations["system.observed"].Metadata.EvidenceClassification);
        Assert.Equal(ValueAvailability.Available, observations["system.observed"].Metadata.ValueAvailability);
        Assert.Equal(EvidenceClassification.Inferred, observations["system.inferred"].Metadata.EvidenceClassification);
        Assert.Equal("provider_values_conflict", observations["system.inferred"].LimitationCode);
        Assert.Equal("count", observations["system.inferred"].Unit);
        Assert.Equal(EvidenceClassification.NotAvailable, observations["system.unknown"].Metadata.EvidenceClassification);
        Assert.Equal(ValueAvailability.Unknown, observations["system.unknown"].Metadata.ValueAvailability);
        Assert.Equal("provider_value_missing", observations["system.unknown"].LimitationCode);
        Assert.Equal(ValueAvailability.NotApplicable, observations["system.not_applicable"].Metadata.ValueAvailability);
        Assert.Equal("capability_not_present", observations["system.not_applicable"].LimitationCode);

        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(descriptor.ProviderId, observation.Metadata.ProviderId);
            Assert.Equal(timestamp, observation.Metadata.CollectedAtUtc);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
            Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
        });
    }

    [Theory]
    [InlineData(
        ProviderEvidenceOrigin.ActiveMachine,
        ProvenanceKind.ProviderObservation,
        PrivacyClassification.WorkshopRestricted)]
    [InlineData(
        ProviderEvidenceOrigin.CapturedSample,
        ProvenanceKind.ImportedRecord,
        PrivacyClassification.WorkshopRestricted)]
    [InlineData(
        ProviderEvidenceOrigin.SyntheticFixture,
        ProvenanceKind.SyntheticFixture,
        PrivacyClassification.WorkshopRestricted)]
    public async Task EvidenceOriginCannotMasqueradeAsAnotherProvenanceClass(
        ProviderEvidenceOrigin origin,
        ProvenanceKind expectedProvenance,
        PrivacyClassification expectedPrivacy)
    {
        var definition = ProviderTestData.Definition("system.origin", EvidenceValueKind.Boolean);
        var descriptor = ProviderTestData.Descriptor([definition], evidenceOrigin: origin);
        var provider = new FakeInventoryProvider(
            descriptor,
            _ => ValueTask.FromResult(ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(definition.EvidenceKey, EvidenceValue.FromBoolean(true))])));

        var result = await new InventoryProviderRunner().RunAsync(provider);
        var observation = Assert.Single(result.Observations);

        Assert.Equal(expectedProvenance, observation.Metadata.Provenance.Kind);
        Assert.Equal(expectedPrivacy, observation.Metadata.PrivacyClassification);
    }

    [Fact]
    public async Task RepeatedEvidenceUsesDistinctSourcesAndCanonicalOrder()
    {
        var definition = ProviderTestData.Definition(
            "storage.disk.bytes",
            EvidenceValueKind.WholeNumber,
            "bytes",
            maximumCardinality: 2);
        var descriptor = ProviderTestData.Descriptor([definition]);
        var provider = new FakeInventoryProvider(
            descriptor,
            _ => ValueTask.FromResult(ProviderReadResult.Collected(
            [
                NormalizedEvidence.Observed(
                    definition.EvidenceKey,
                    EvidenceValue.FromInteger(200),
                    sourceReference: "storage.disk.2"),
                NormalizedEvidence.Observed(
                    definition.EvidenceKey,
                    EvidenceValue.FromInteger(100),
                    sourceReference: "storage.disk.1"),
            ])));

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(
            ["storage.disk.1", "storage.disk.2"],
            result.Observations.Select(static value => value.Metadata.Provenance.SourceReference));
        Assert.Equal(
            [100L, 200L],
            result.Observations.Select(static value => value.Value!.IntegerValue!.Value));
    }

    [Fact]
    public async Task MalformedNormalizedEvidenceFailsTheContractClosed()
    {
        var definitions = new[]
        {
            ProviderTestData.Definition("system.first", EvidenceValueKind.Boolean),
            ProviderTestData.Definition("system.second", EvidenceValueKind.WholeNumber),
        };
        var descriptor = ProviderTestData.Descriptor(definitions);
        var malformedResults = new[]
        {
            ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(true))]),
            ProviderReadResult.Collected(
            [
                NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(true)),
                NormalizedEvidence.Observed(new EvidenceKey("system.extra"), EvidenceValue.FromBoolean(true)),
            ]),
            ProviderReadResult.Collected(
            [
                NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromInteger(1)),
                NormalizedEvidence.Observed(definitions[1].EvidenceKey, EvidenceValue.FromInteger(2)),
            ]),
            ProviderReadResult.Collected(
            [
                NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(true)),
                NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(false)),
                NormalizedEvidence.Observed(definitions[1].EvidenceKey, EvidenceValue.FromInteger(2)),
            ]),
        };

        foreach (var malformed in malformedResults)
        {
            var provider = new FakeInventoryProvider(descriptor, _ => ValueTask.FromResult(malformed));
            var result = await new InventoryProviderRunner().RunAsync(provider);

            Assert.Equal(ProviderRunOutcome.ContractInvalid, result.Outcome);
            Assert.Equal("provider_contract_invalid", result.Error!.ErrorCode);
            Assert.All(result.Observations, observation =>
            {
                Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
                Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
                Assert.Null(observation.Value);
            });
        }

        Assert.Throws<ArgumentException>(() => ProviderReadResult.Collected([null!]));
    }

    [Fact]
    public async Task PartialFailurePreservesUnrelatedEvidenceAndMarksAffectedEvidenceUnknown()
    {
        var definitions = new[]
        {
            ProviderTestData.Definition("system.available", EvidenceValueKind.Boolean),
            ProviderTestData.Definition("system.affected", EvidenceValueKind.WholeNumber),
        };
        var descriptor = ProviderTestData.Descriptor(definitions);
        var provider = new FakeInventoryProvider(
            descriptor,
            _ => ValueTask.FromResult(ProviderReadResult.Unavailable(
            [
                NormalizedEvidence.Observed(
                    definitions[0].EvidenceKey,
                    EvidenceValue.FromBoolean(true)),
            ])));

        var result = await new InventoryProviderRunner().RunAsync(provider);
        var observations = result.Observations.ToDictionary(static value => value.EvidenceKey.Value);

        Assert.Equal(ProviderRunOutcome.Unavailable, result.Outcome);
        Assert.True(observations["system.available"].Value!.BooleanValue);
        Assert.Equal(ValueAvailability.Available, observations["system.available"].Metadata.ValueAvailability);
        Assert.Equal(ValueAvailability.Unknown, observations["system.affected"].Metadata.ValueAvailability);
        Assert.Equal("provider_unavailable", observations["system.affected"].LimitationCode);
        Assert.Equal(
            "provider_unavailable",
            observations[descriptor.FailureDefinition.EvidenceKey.Value].LimitationCode);
        Assert.Equal("provider_unavailable", result.Error!.ErrorCode);
        Assert.Equal(ProviderRecoveryAction.RetryCollection, result.Error.RecoveryAction);
    }

    [Fact]
    public async Task CleanupFailureRetainsAllCollectedFactsAndAddsProviderStatusEvidence()
    {
        var definitions = ProviderTestData.DefaultDefinitions();
        var descriptor = ProviderTestData.Descriptor(definitions);
        var retained = new[]
        {
            NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(true)),
            NormalizedEvidence.Observed(definitions[1].EvidenceKey, EvidenceValue.FromInteger(16)),
        };
        var provider = new FakeInventoryProvider(
            descriptor,
            _ => ValueTask.FromResult(ProviderReadResult.CleanupIncomplete(retained)));

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.CleanupIncomplete, result.Outcome);
        Assert.Equal(2, result.Observations.Count(static value => value.Value is not null));
        var providerStatus = Assert.Single(
            result.Observations,
            value => value.EvidenceKey == descriptor.FailureDefinition.EvidenceKey);
        Assert.Equal(ValueAvailability.Unknown, providerStatus.Metadata.ValueAvailability);
        Assert.Equal("provider_cleanup_incomplete", providerStatus.LimitationCode);
        Assert.Equal(ProviderRecoveryAction.ReviewCleanup, result.Error!.RecoveryAction);
    }

    [Fact]
    public async Task NonCleanupFailureCannotRetainEveryDeclaredRequirementAsAvailable()
    {
        var definitions = ProviderTestData.DefaultDefinitions();
        var descriptor = ProviderTestData.Descriptor(definitions);
        var provider = new FakeInventoryProvider(
            descriptor,
            _ => ValueTask.FromResult(ProviderReadResult.Failed(
            [
                NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(true)),
                NormalizedEvidence.Observed(definitions[1].EvidenceKey, EvidenceValue.FromInteger(16)),
            ])));

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.ContractInvalid, result.Outcome);
        Assert.Equal("provider_contract_invalid", result.Error!.ErrorCode);
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
            Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
            Assert.Null(observation.Value);
        });
    }

    [Fact]
    public async Task ProviderExceptionsAreClassifiedByTypeWithoutRetainingRawDetails()
    {
        const string sensitiveSeed = "C:\\Users\\NamedPerson\\secret-provider-output.txt";
        var cases = new (Exception Exception, ProviderRunOutcome Outcome, string Code)[]
        {
            (new UnauthorizedAccessException(sensitiveSeed), ProviderRunOutcome.AccessDenied, "provider_access_denied"),
            (new ArgumentException(sensitiveSeed), ProviderRunOutcome.InvalidData, "provider_data_invalid"),
            (new TimeoutException(sensitiveSeed), ProviderRunOutcome.TimedOut, "provider_collection_timed_out"),
            (new InvalidOperationException(sensitiveSeed), ProviderRunOutcome.Failed, "provider_failed"),
            (new HostileProviderException(), ProviderRunOutcome.Failed, "provider_failed"),
        };

        foreach (var item in cases)
        {
            var provider = new FakeInventoryProvider(
                ProviderTestData.Descriptor(),
                _ => ValueTask.FromException<ProviderReadResult>(item.Exception));

            var result = await new InventoryProviderRunner().RunAsync(provider);
            var serialized = JsonSerializer.Serialize(result);

            Assert.Equal(item.Outcome, result.Outcome);
            Assert.Equal(item.Code, result.Error!.ErrorCode);
            Assert.DoesNotContain(sensitiveSeed, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-provider-output", serialized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CallerCancellationBeforeStartDoesNotInvokeProvider()
    {
        var provider = new FakeInventoryProvider(
            ProviderTestData.Descriptor(),
            _ => ValueTask.FromResult(ProviderReadResult.Failed()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, provider.InvocationCount);
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
            Assert.Equal(ProvenanceKind.SystemGenerated, observation.Metadata.Provenance.Kind);
        });
    }

    [Fact]
    public async Task CallerCancellationCannotPublishCollectedEvidence()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var definitions = ProviderTestData.DefaultDefinitions();
        var provider = new FakeInventoryProvider(
            ProviderTestData.Descriptor(definitions),
            async _ =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return ProviderReadResult.Collected(
                [
                    NormalizedEvidence.Observed(definitions[0].EvidenceKey, EvidenceValue.FromBoolean(true)),
                    NormalizedEvidence.Observed(definitions[1].EvidenceKey, EvidenceValue.FromInteger(16)),
                ]);
            });
        using var cancellation = new CancellationTokenSource();

        var pending = new InventoryProviderRunner().RunAsync(provider, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        release.TrySetResult(true);
        var result = await pending;

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.All(result.Observations, observation => Assert.Null(observation.Value));
    }

    [Fact]
    public async Task TimeoutCancelsCooperativeProviderAndReturnsUnknownEvidence()
    {
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeInventoryProvider(
            ProviderTestData.Descriptor(timeout: TimeSpan.FromMilliseconds(50)),
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
            });

        var result = await new InventoryProviderRunner().RunAsync(provider);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ProviderRunOutcome.TimedOut, result.Outcome);
        Assert.Equal("provider_collection_timed_out", result.Error!.ErrorCode);
        Assert.Equal(1, provider.InvocationCount);
        Assert.All(result.Observations, observation =>
            Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability));
    }

    [Fact]
    public async Task ThrowingCancellationCallbackCannotEscapeSanitizedTimeout()
    {
        const string sensitiveSeed = "secret-cancellation-callback";
        var provider = new FakeInventoryProvider(
            ProviderTestData.Descriptor(timeout: TimeSpan.FromMilliseconds(50)),
            async cancellationToken =>
            {
                using var registration = cancellationToken.Register(
                    static value => throw new InvalidOperationException((string)value!),
                    sensitiveSeed);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return ProviderReadResult.Failed();
            });

        var result = await new InventoryProviderRunner().RunAsync(provider);
        var serialized = JsonSerializer.Serialize(result);

        Assert.Equal(ProviderRunOutcome.TimedOut, result.Outcome);
        Assert.DoesNotContain(sensitiveSeed, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void NotApplicableAndAvailableLimitationsRemainSemanticallyBounded()
    {
        var key = new EvidenceKey("system.capability");
        var notApplicable = NormalizedEvidence.NotApplicable(
            key,
            ProviderLimitation.CapabilityNotPresent);
        var conflicted = NormalizedEvidence.Observed(
            key,
            EvidenceValue.FromBoolean(true),
            ProviderLimitation.SourceValuesConflict);

        Assert.Equal(ValueAvailability.NotApplicable, notApplicable.ValueAvailability);
        Assert.Equal(ProviderLimitation.SourceValuesConflict, conflicted.Limitation);
        Assert.Throws<ArgumentException>(() => NormalizedEvidence.NotApplicable(
            key,
            ProviderLimitation.ProviderUnavailable));
        Assert.Throws<ArgumentException>(() => NormalizedEvidence.Observed(
            key,
            EvidenceValue.FromBoolean(true),
            ProviderLimitation.AccessDenied));
        Assert.Throws<ArgumentException>(() => NormalizedEvidence.NotAvailable(
            key,
            ProviderLimitation.CollectionTimedOut));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ThirdLife.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root could not be located from the test output path.");
    }

    private sealed class HostileProviderException : Exception
    {
        public override string Message => throw new InvalidOperationException("The raw message was inspected.");

        public override string ToString() => throw new InvalidOperationException("The raw exception was formatted.");
    }
}

internal static class ProviderTestData
{
    public static ProviderEvidenceDefinition[] DefaultDefinitions() =>
    [
        Definition("system.available", EvidenceValueKind.Boolean),
        Definition("system.count", EvidenceValueKind.WholeNumber, "count"),
    ];

    public static ProviderEvidenceDefinition Definition(
        string key,
        EvidenceValueKind kind,
        string? unit = null,
        int maximumCardinality = 1) =>
        new(
            new EvidenceKey(key),
            kind,
            unit,
            string.Concat("source.", key),
            maximumCardinality);

    public static InventoryProviderDescriptor Descriptor(
        IEnumerable<ProviderEvidenceDefinition>? definitions = null,
        TimeSpan? timeout = null,
        ProviderEvidenceOrigin evidenceOrigin = ProviderEvidenceOrigin.SyntheticFixture) =>
        new(
            new ProviderId("provider-fake"),
            ProviderPrivilegeRequirement.StandardUser,
            TimeSpan.FromMilliseconds(10),
            timeout ?? TimeSpan.FromSeconds(1),
            ProviderNetworkUse.None,
            evidenceOrigin,
            [ProviderOperatingSystem.Windows10, ProviderOperatingSystem.Windows11],
            new ProviderFailureDefinition(new EvidenceKey("provider.fake.failure"), "source.provider.fake.status"),
            definitions ?? DefaultDefinitions());
}

internal sealed class FakeInventoryProvider : IInventoryProvider
{
    private readonly Func<CancellationToken, ValueTask<ProviderReadResult>> _observe;
    private int _invocationCount;

    public FakeInventoryProvider(
        InventoryProviderDescriptor descriptor,
        Func<CancellationToken, ValueTask<ProviderReadResult>> observe)
    {
        Descriptor = descriptor;
        _observe = observe;
    }

    public InventoryProviderDescriptor Descriptor { get; }

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _invocationCount);
        return _observe(cancellationToken);
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
