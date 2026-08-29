using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThirdLife.Core;
using ThirdLife.Core.Evidence;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class SystemInventoryProviderTests
{
    private static readonly DateTimeOffset FixtureTimestamp =
        new(2034, 2, 3, 4, 5, 6, TimeSpan.Zero);

    private static readonly JsonSerializerOptions FixtureJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [Fact]
    public void DescriptorDeclaresTheClosedUnelevatedLocalContract()
    {
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(BuildMinimalFirmwareTable())),
            ProviderEvidenceOrigin.SyntheticFixture);
        var definitions = provider.Descriptor.EvidenceDefinitions.ToDictionary(
            static definition => definition.EvidenceKey.Value,
            StringComparer.Ordinal);

        Assert.Equal(SystemInventoryProvider.ProviderIdentity, provider.Descriptor.ProviderId.Value);
        Assert.Equal(ProviderPrivilegeRequirement.StandardUser, provider.Descriptor.MinimumPrivilege);
        Assert.Equal(ProviderNetworkUse.None, provider.Descriptor.NetworkUse);
        Assert.Equal(ProviderEvidenceOrigin.SyntheticFixture, provider.Descriptor.EvidenceOrigin);
        Assert.Equal(TimeSpan.FromMilliseconds(250), provider.Descriptor.ExpectedDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), provider.Descriptor.Timeout);
        Assert.Equal(
            [ProviderOperatingSystem.Windows10, ProviderOperatingSystem.Windows11],
            provider.Descriptor.SupportedOperatingSystems);
        Assert.Equal("system_inventory.provider_status", provider.Descriptor.FailureDefinition.EvidenceKey.Value);
        Assert.Equal(9, definitions.Count);
        AssertDefinition(definitions, "device.manufacturer", EvidenceValueKind.Text);
        AssertDefinition(definitions, "device.model", EvidenceValueKind.Text);
        AssertDefinition(definitions, "device.serial_number", EvidenceValueKind.Text);
        AssertDefinition(definitions, "device.type", EvidenceValueKind.Enum);
        AssertDefinition(
            definitions,
            "processor.manufacturer",
            EvidenceValueKind.Text,
            maximumCardinality: SmbiosInventoryParser.MaximumProcessorRecords);
        AssertDefinition(
            definitions,
            "processor.model",
            EvidenceValueKind.Text,
            maximumCardinality: SmbiosInventoryParser.MaximumProcessorRecords);
        AssertDefinition(definitions, "processor.logical_count", EvidenceValueKind.WholeNumber, "count");
        AssertDefinition(definitions, "system.architecture", EvidenceValueKind.Enum);
        AssertDefinition(definitions, "memory.installed_bytes", EvidenceValueKind.WholeNumber, "bytes");
        Assert.DoesNotContain("processor.windows_11_eligibility", definitions.Keys);

        var publicConstructor = Assert.Single(typeof(SystemInventoryProvider).GetConstructors());
        Assert.Empty(publicConstructor.GetParameters());
    }

    [Fact]
    public async Task SyntheticFixtureMatrixNormalizesInvariantAttributedEvidence()
    {
        var fixture = LoadSyntheticFixture();

        Assert.Equal("thirdlife.system-inventory-fixtures.v1", fixture.SchemaVersion);
        Assert.Equal("SYNTHETIC-SYSTEM-INVENTORY-CASES-001", fixture.FixtureId);
        Assert.True(fixture.SyntheticData);
        Assert.Equal("PUBLIC_REFERENCE", fixture.Classification);
        Assert.Equal(3, fixture.Cases.Count);
        Assert.Equal(3, fixture.Cases.Select(static item => item.Manufacturer).Distinct().Count());

        foreach (var item in fixture.Cases)
        {
            var firmwareTable = BuildFirmwareTable(item);
            var source = new DelegateSystemInventorySource(_ => CompleteSnapshot(
                firmwareTable,
                item.NativeArchitecture,
                item.InstalledMemoryKib,
                item.LogicalProcessorCount));
            var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);
            var result = await new InventoryProviderRunner(
                new FixedTimeProvider(FixtureTimestamp)).RunAsync(provider);

            Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
            Assert.Null(result.Error);
            Assert.Equal(item.Manufacturer, GetSingleText(result, "device.manufacturer"));
            Assert.Equal(item.Model, GetSingleText(result, "device.model"));
            Assert.Equal(item.SerialNumber, GetSingleText(result, "device.serial_number"));
            Assert.Equal(item.ExpectedDeviceType, GetSingleEnum(result, "device.type"));
            Assert.Equal(item.ExpectedArchitecture, GetSingleEnum(result, "system.architecture"));
            Assert.Equal(item.ExpectedMemoryBytes, GetSingleInteger(result, "memory.installed_bytes"));
            Assert.Equal(item.LogicalProcessorCount, GetSingleInteger(result, "processor.logical_count"));
            Assert.Equal(
                item.Processors.OrderBy(static processor => processor.Handle).Select(static processor => processor.Model),
                result.Observations
                    .Where(static observation => observation.EvidenceKey.Value == "processor.model")
                    .Select(static observation => observation.Value!.StringValue));
            Assert.Equal(
                Enumerable.Range(0, item.Processors.Count)
                    .Select(index => $"system_inventory.smbios.type4.{index:D2}"),
                result.Observations
                    .Where(static observation => observation.EvidenceKey.Value == "processor.model")
                    .Select(static observation => observation.Metadata.Provenance.SourceReference));

            Assert.All(result.Observations, observation =>
            {
                Assert.Equal(provider.Descriptor.ProviderId, observation.Metadata.ProviderId);
                Assert.Equal(FixtureTimestamp, observation.Metadata.CollectedAtUtc);
                Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
                Assert.Equal(ProvenanceKind.SyntheticFixture, observation.Metadata.Provenance.Kind);
            });
            Assert.True(firmwareTable.All(static value => value == 0), "The transient SMBIOS buffer was not cleared.");

            var nonSerialEvidence = result.Observations
                .Where(static observation => observation.EvidenceKey.Value != "device.serial_number")
                .Select(static observation => JsonSerializer.Serialize(observation));
            Assert.DoesNotContain(nonSerialEvidence, json => json.Contains(item.SerialNumber, StringComparison.Ordinal));
            Assert.DoesNotContain(
                result.Observations.Select(static observation => observation.Metadata.Provenance.SourceReference),
                sourceReference => sourceReference.Contains(item.SerialNumber, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task FullSerialObservationCannotBeImplicitlyRendered()
    {
        const string syntheticSerial = "TEST-ONLY-RENDER-0001";
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(BuildFirmwareTable(
                manufacturer: "Example Devices Render",
                model: "Synthetic Model Render",
                serialNumber: syntheticSerial,
                chassisTypes: [3],
                processors: [DefaultProcessor()]))),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);
        var serialObservation = Assert.Single(
            result.Observations,
            static observation => observation.EvidenceKey.Value == "device.serial_number");

        Assert.Equal(syntheticSerial, serialObservation.Value!.StringValue);
        Assert.Contains("[evidence_value]", serialObservation.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(syntheticSerial, serialObservation.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(syntheticSerial, $"{serialObservation.Value}", StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceholderAndAbsentSmbiosValuesRemainNotAvailable()
    {
        var firmwareTable = BuildFirmwareTable(
            manufacturer: "To Be Filled By O.E.M.",
            model: "Default string",
            serialNumber: "0000-0000",
            chassisTypes: [],
            processors: []);
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "device.manufacturer", "provider_value_missing");
        AssertUnknown(result, "device.model", "provider_value_missing");
        AssertUnknown(result, "device.serial_number", "provider_value_missing");
        AssertUnknown(result, "device.type", "provider_value_missing");
        AssertUnknown(result, "processor.manufacturer", "provider_value_missing");
        AssertUnknown(result, "processor.model", "provider_value_missing");
        Assert.True(firmwareTable.All(static value => value == 0), "The transient SMBIOS buffer was not cleared.");
    }

    [Theory]
    [InlineData("0000/0000")]
    [InlineData("FFFF/FFFF")]
    [InlineData("////")]
    [InlineData("####")]
    public async Task SeparatorOnlyAndRepeatedSerialPlaceholdersRemainNotAvailable(string placeholder)
    {
        var firmwareTable = BuildFirmwareTable(
            manufacturer: "Example Devices Placeholder",
            model: "Synthetic Model Placeholder",
            serialNumber: placeholder,
            chassisTypes: [3],
            processors: [DefaultProcessor()]);
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "device.serial_number", "provider_value_missing");
    }

    [Fact]
    public async Task UnrelatedLongSmbiosStringDoesNotDiscardReviewedFields()
    {
        var unrelated = AppendStrings(
            CreateFormattedStructure(type: 11, length: 5, handle: 0x0b00),
            new string('X', SmbiosInventoryParser.MaximumTextBytes + 1));
        var firmwareTable = BuildRawFirmwareTable(
            CreateType1Structure(
                "Example Devices Long",
                "Synthetic Model Long",
                "TEST-ONLY-LONG-0001",
                handle: 0x0100),
            unrelated,
            CreateType3Structure(chassisType: 3, handle: 0x0300),
            CreateType4Structure("Example Silicon Long", "Synthetic CPU Long", handle: 0x0400),
            CreateEndStructure());
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal("Example Devices Long", GetSingleText(result, "device.manufacturer"));
        Assert.Equal("Synthetic CPU Long", GetSingleText(result, "processor.model"));
    }

    [Fact]
    public async Task NonCentralType4RecordsAreNotPublishedAsProcessors()
    {
        var firmwareTable = BuildRawFirmwareTable(
            CreateType1Structure(
                "Example Devices Cpu",
                "Synthetic Model Cpu",
                "TEST-ONLY-CPU-0001",
                handle: 0x0100),
            CreateType3Structure(chassisType: 3, handle: 0x0300),
            CreateType4Structure("Example Silicon Cpu", "Synthetic Central CPU", handle: 0x0400),
            CreateType4Structure(
                "Must Not Publish",
                "Synthetic Video Processor",
                handle: 0x0401,
                processorType: 0x05),
            CreateEndStructure());
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(
            ["Example Silicon Cpu"],
            result.Observations
                .Where(static observation => observation.EvidenceKey.Value == "processor.manufacturer")
                .Select(static observation => observation.Value!.StringValue));
        Assert.Equal(
            ["Synthetic Central CPU"],
            result.Observations
                .Where(static observation => observation.EvidenceKey.Value == "processor.model")
                .Select(static observation => observation.Value!.StringValue));
    }

    [Fact]
    public async Task UnpopulatedSocketsDoNotConsumeTheProcessorEvidenceBound()
    {
        var structures = new List<byte[]>
        {
            CreateType1Structure(
                "Example Devices Socket",
                "Synthetic Model Socket",
                "TEST-ONLY-SOCKET-0001",
                handle: 0x0100),
            CreateType3Structure(chassisType: 3, handle: 0x0300),
        };
        structures.AddRange(Enumerable.Range(0, SmbiosInventoryParser.MaximumProcessorRecords)
            .Select(index => CreateType4Structure(
                "Example Silicon Socket",
                $"Synthetic CPU {index}",
                checked((ushort)(0x0400 + index)))));
        structures.Add(CreateType4Structure(
            "Example Silicon Empty Socket",
            "Must Not Publish",
            handle: 0x0500,
            populated: false));
        structures.Add(CreateEndStructure());
        var firmwareTable = BuildRawFirmwareTable(structures.ToArray());
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Equal(
            SmbiosInventoryParser.MaximumProcessorRecords,
            result.Observations.Count(
                static observation => observation.EvidenceKey.Value == "processor.model"));
        Assert.DoesNotContain(
            result.Observations,
            static observation => observation.Value?.StringValue == "Must Not Publish");
    }

    [Fact]
    public async Task FieldLocalMalformedValuesDoNotReplaceValidUnrelatedFacts()
    {
        var firmwareTable = BuildFirmwareTable(
            manufacturer: "Example Devices Delta",
            model: "Synthetic Model D4",
            serialNumber: "TEST-ONLY-DELTA-0004",
            chassisTypes: [0],
            processors:
            [
                new ProcessorFixture
                {
                    Handle = 1024,
                    Manufacturer = "Example Silicon Delta",
                    Model = "Synthetic Core 4",
                },
            ]);
        var type1Offset = FindStructureOffset(firmwareTable, structureType: 1);
        firmwareTable[type1Offset + 4] = 99;
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(
                firmwareTable,
                nativeArchitecture: 99,
                installedMemoryKilobytes: 0,
                logicalProcessorCount: 0)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "device.manufacturer", "provider_value_malformed");
        Assert.Equal("Synthetic Model D4", GetSingleText(result, "device.model"));
        AssertUnknown(result, "device.type", "provider_value_malformed");
        AssertUnknown(result, "system.architecture", "provider_value_malformed");
        AssertUnknown(result, "memory.installed_bytes", "provider_value_malformed");
        AssertUnknown(result, "processor.logical_count", "provider_value_malformed");
    }

    [Fact]
    public async Task ConflictingChassisTypesRemainUnknownRatherThanFirstWins()
    {
        var firmwareTable = BuildFirmwareTable(
            manufacturer: "Example Devices Epsilon",
            model: "Synthetic Model E5",
            serialNumber: "TEST-ONLY-EPSILON-0005",
            chassisTypes: [3, 10],
            processors: [DefaultProcessor()]);
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "device.type", "provider_values_conflict");
        Assert.Equal("Synthetic Model E5", GetSingleText(result, "device.model"));
    }

    [Fact]
    public async Task AccessDeniedRetainsIndependentArchitectureMemoryAndCount()
    {
        var source = new DelegateSystemInventorySource(_ => new SystemInventorySourceSnapshot(
            SystemInventorySourceValue<byte[]>.AccessDenied(),
            SystemInventorySourceValue<ushort>.Available(9),
            SystemInventorySourceValue<ulong>.Available(16_777_216),
            SystemInventorySourceValue<uint>.Available(12)));
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.AccessDenied, result.Outcome);
        Assert.Equal(ProviderRecoveryAction.ReviewAccess, result.Error!.RecoveryAction);
        Assert.Equal("x64", GetSingleEnum(result, "system.architecture"));
        Assert.Equal(17_179_869_184, GetSingleInteger(result, "memory.installed_bytes"));
        Assert.Equal(12, GetSingleInteger(result, "processor.logical_count"));
        AssertUnknown(result, "device.manufacturer", "provider_access_denied");
        AssertUnknown(result, "processor.model", "provider_access_denied");
        Assert.Equal(1, source.InvocationCount);
    }

    [Fact]
    public async Task FI001SmcSystemInventoryFailuresRemainFailClosed()
    {
        var malformedFirmware = BuildMinimalFirmwareTable();
        BinaryPrimitives.WriteUInt32LittleEndian(malformedFirmware.AsSpan(4, 4), 1);
        var cases = new[]
        {
            new
            {
                Snapshot = new Func<SystemInventorySourceSnapshot>(() => new SystemInventorySourceSnapshot(
                    SystemInventorySourceValue<byte[]>.Unavailable(),
                    SystemInventorySourceValue<ushort>.Available(9),
                    SystemInventorySourceValue<ulong>.Available(16_777_216),
                    SystemInventorySourceValue<uint>.Available(12))),
                Outcome = ProviderRunOutcome.Unavailable,
                ErrorCode = "provider_unavailable",
            },
            new
            {
                Snapshot = new Func<SystemInventorySourceSnapshot>(() => new SystemInventorySourceSnapshot(
                    SystemInventorySourceValue<byte[]>.AccessDenied(),
                    SystemInventorySourceValue<ushort>.Available(9),
                    SystemInventorySourceValue<ulong>.Available(16_777_216),
                    SystemInventorySourceValue<uint>.Available(12))),
                Outcome = ProviderRunOutcome.AccessDenied,
                ErrorCode = "provider_access_denied",
            },
            new
            {
                Snapshot = new Func<SystemInventorySourceSnapshot>(() => CompleteSnapshot(malformedFirmware)),
                Outcome = ProviderRunOutcome.InvalidData,
                ErrorCode = "provider_data_invalid",
            },
        };

        foreach (var item in cases)
        {
            var source = new DelegateSystemInventorySource(_ => item.Snapshot());
            var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);
            var result = await new InventoryProviderRunner().RunAsync(provider);

            Assert.Equal(item.Outcome, result.Outcome);
            Assert.Equal(item.ErrorCode, result.Error!.ErrorCode);
            Assert.Equal("x64", GetSingleEnum(result, "system.architecture"));
            AssertUnknown(result, "device.serial_number", item.ErrorCode);
            Assert.Equal(1, source.InvocationCount);
            Assert.DoesNotContain(
                result.Observations,
                static observation => observation.Value?.StringValue?.Contains("TEST-ONLY", StringComparison.Ordinal) == true);
        }

        Assert.True(
            malformedFirmware.All(static value => value == 0),
            "The malformed SMBIOS buffer was not cleared.");
    }

    [Fact]
    public async Task UnavailableSourcesLeaveEveryFactUnknown()
    {
        var source = new DelegateSystemInventorySource(_ => new SystemInventorySourceSnapshot(
            SystemInventorySourceValue<byte[]>.Unavailable(),
            SystemInventorySourceValue<ushort>.Unavailable(),
            SystemInventorySourceValue<ulong>.Unavailable(),
            SystemInventorySourceValue<uint>.Unavailable()));
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.CapturedSample);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Unavailable, result.Outcome);
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
            Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
            Assert.Null(observation.Value);
            Assert.Equal(ProvenanceKind.ImportedRecord, observation.Metadata.Provenance.Kind);
        });
    }

    [Fact]
    public async Task SanitizedReferenceProfileReplaysCapturedNonIdentityFacts()
    {
        var profilePath = Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "testing",
            "reference-machine-profile.md");
        var profileBytes = File.ReadAllBytes(profilePath);
        var profile = Encoding.UTF8.GetString(profileBytes);
        var profileDigest = Convert.ToHexString(SHA256.HashData(profileBytes)).ToLowerInvariant();
        var capturedAt = DateTimeOffset.Parse(
            GetProfileMetadata(profile, "Captured"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var architecture = GetProfileTableValue(profile, "Architecture");
        var logicalProcessorCount = uint.Parse(
            GetProfileTableValue(profile, "Logical processor count"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var nativeArchitecture = architecture.Contains("x64", StringComparison.Ordinal)
            ? (ushort)9
            : throw new InvalidDataException("The sanitized reference profile architecture is unsupported.");
        var source = new DelegateSystemInventorySource(_ => new SystemInventorySourceSnapshot(
            SystemInventorySourceValue<byte[]>.Unavailable(),
            SystemInventorySourceValue<ushort>.Available(nativeArchitecture),
            SystemInventorySourceValue<ulong>.Unavailable(),
            SystemInventorySourceValue<uint>.Available(logicalProcessorCount)));
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.CapturedSample);

        var result = await new InventoryProviderRunner(
            new FixedTimeProvider(FixtureTimestamp)).RunAsync(provider);

        Assert.Contains("Profile ID:** `REF-CODEX-001`", profile, StringComparison.Ordinal);
        Assert.Equal(64, profileDigest.Length);
        Assert.True(capturedAt < FixtureTimestamp);
        Assert.Equal(ProviderRunOutcome.Unavailable, result.Outcome);
        Assert.Equal("x64", GetSingleEnum(result, "system.architecture"));
        Assert.Equal(12, GetSingleInteger(result, "processor.logical_count"));
        AssertUnknown(result, "device.serial_number", "provider_unavailable");
        AssertUnknown(result, "memory.installed_bytes", "provider_unavailable");
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(ProvenanceKind.ImportedRecord, observation.Metadata.Provenance.Kind);
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
            Assert.Equal(FixtureTimestamp, observation.Metadata.CollectedAtUtc);
        });
    }

    [Fact]
    public async Task StructurallyMalformedTablesFailClosedAndClearTransientBytes()
    {
        var valid = BuildMinimalFirmwareTable();
        var lengthMismatch = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(lengthMismatch.AsSpan(4, 4), 1);

        var invalidStructureLength = (byte[])valid.Clone();
        invalidStructureLength[RawTableOffset + 1] = 3;

        var missingFinalTerminator = valid[..^1];
        WriteDeclaredLength(missingFinalTerminator);

        var duplicateType1 = BuildRawFirmwareTable(
            CreateType1Structure("Example A", "Model A", "TEST-ONLY-A-0001", handle: 0x0100),
            CreateType1Structure("Example B", "Model B", "TEST-ONLY-B-0002", handle: 0x0101),
            CreateEndStructure());

        var duplicateHandle = BuildRawFirmwareTable(
            CreateType1Structure("Example A", "Model A", "TEST-ONLY-A-0001", handle: 0x0100),
            CreateType3Structure(chassisType: 3, handle: 0x0100),
            CreateEndStructure());

        var excessiveProcessorStructures = new List<byte[]>
        {
            CreateType1Structure("Example A", "Model A", "TEST-ONLY-A-0001", handle: 0x0100),
            CreateType3Structure(chassisType: 3, handle: 0x0300),
        };
        excessiveProcessorStructures.AddRange(
            Enumerable.Range(0, SmbiosInventoryParser.MaximumProcessorRecords + 1)
                .Select(index => CreateType4Structure(
                    "Example Silicon",
                    "Synthetic Core",
                    checked((ushort)(0x0400 + index)))));
        excessiveProcessorStructures.Add(CreateEndStructure());
        var tooManyProcessors = BuildRawFirmwareTable(excessiveProcessorStructures.ToArray());

        var oversized = new byte[WindowsSystemInventorySource.MaximumFirmwareTableBytes + 1];

        foreach (var malformed in new[]
                 {
                     lengthMismatch,
                     invalidStructureLength,
                     missingFinalTerminator,
                     duplicateType1,
                     duplicateHandle,
                     tooManyProcessors,
                     oversized,
                 })
        {
            var provider = new SystemInventoryProvider(
                new DelegateSystemInventorySource(_ => CompleteSnapshot(malformed)),
                ProviderEvidenceOrigin.SyntheticFixture);
            var result = await new InventoryProviderRunner().RunAsync(provider);

            Assert.Equal(ProviderRunOutcome.InvalidData, result.Outcome);
            Assert.Equal(ProviderRecoveryAction.ReviewProviderData, result.Error!.RecoveryAction);
            Assert.Equal("x64", GetSingleEnum(result, "system.architecture"));
            AssertUnknown(result, "device.manufacturer", "provider_data_invalid");
            Assert.True(malformed.All(static value => value == 0), "The malformed SMBIOS buffer was not cleared.");
        }
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedWithoutRawFallback()
    {
        var firmwareTable = BuildMinimalFirmwareTable();
        var type1Offset = FindStructureOffset(firmwareTable, structureType: 1);
        firmwareTable[type1Offset + firmwareTable[type1Offset + 1]] = 0xff;
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(firmwareTable)),
            ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        AssertUnknown(result, "device.manufacturer", "provider_value_malformed");
        Assert.DoesNotContain(
            result.Observations,
            observation => observation.Value?.StringValue?.Contains('\ufffd') == true);
    }

    [Fact]
    public async Task ControlOverboundAndOverflowValuesRemainUnknownWithoutTruncation()
    {
        var controlTable = BuildFirmwareTable(
            manufacturer: "Example\nDevices",
            model: "Synthetic Model F6",
            serialNumber: "TEST-ONLY-F6",
            chassisTypes: [3],
            processors: [DefaultProcessor()]);
        var controlProvider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(controlTable)),
            ProviderEvidenceOrigin.SyntheticFixture);
        var controlResult = await new InventoryProviderRunner().RunAsync(controlProvider);

        Assert.Equal(ProviderRunOutcome.Completed, controlResult.Outcome);
        AssertUnknown(controlResult, "device.manufacturer", "provider_value_malformed");

        var longSerial = new string('S', SmbiosInventoryParser.MaximumSerialCharacters + 1);
        var serialTable = BuildFirmwareTable(
            manufacturer: "Example Devices Eta",
            model: "Synthetic Model G7",
            serialNumber: longSerial,
            chassisTypes: [3],
            processors: [DefaultProcessor()]);
        var serialProvider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(
                serialTable,
                installedMemoryKilobytes:
                    checked((ulong)(SystemInventoryProvider.MaximumInstalledMemoryBytes / 1024) + 1),
                logicalProcessorCount: SystemInventoryProvider.MaximumLogicalProcessorCount + 1)),
            ProviderEvidenceOrigin.SyntheticFixture);
        var serialResult = await new InventoryProviderRunner().RunAsync(serialProvider);

        Assert.Equal(ProviderRunOutcome.Completed, serialResult.Outcome);
        AssertUnknown(serialResult, "device.serial_number", "provider_value_malformed");
        AssertUnknown(serialResult, "memory.installed_bytes", "provider_value_malformed");
        AssertUnknown(serialResult, "processor.logical_count", "provider_value_malformed");
        Assert.DoesNotContain(
            serialResult.Observations,
            observation => observation.Value?.StringValue?.Contains(longSerial, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task MemorySerializationIsCultureInvariantAndStrictlyRoundTrips()
    {
        var fixtureCase = LoadSyntheticFixture().Cases[0];
        var provider = new SystemInventoryProvider(
            new DelegateSystemInventorySource(_ => CompleteSnapshot(
                BuildFirmwareTable(fixtureCase),
                fixtureCase.NativeArchitecture,
                fixtureCase.InstalledMemoryKib,
                fixtureCase.LogicalProcessorCount)),
            ProviderEvidenceOrigin.SyntheticFixture);
        var priorCulture = CultureInfo.CurrentCulture;
        var priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fi-FI");
            var result = await new InventoryProviderRunner(
                new FixedTimeProvider(FixtureTimestamp)).RunAsync(provider);
            var memory = Assert.Single(
                result.Observations,
                static observation => observation.EvidenceKey.Value == "memory.installed_bytes");
            var json = JsonSerializer.Serialize(memory, DomainJson.CreateStrictOptions());
            var roundTrip = JsonSerializer.Deserialize<Observation>(json, DomainJson.CreateStrictOptions());

            Assert.NotNull(roundTrip);
            Assert.Equal("bytes", roundTrip.Unit);
            Assert.Equal(fixtureCase.ExpectedMemoryBytes, roundTrip.Value!.IntegerValue);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                fixtureCase.ExpectedMemoryBytes,
                document.RootElement.GetProperty("value").GetProperty("data").GetInt64());
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    [Fact]
    public async Task CallerCancellationBeforeStartDoesNotInvokeTheWindowsSource()
    {
        var source = new DelegateSystemInventorySource(_ => CompleteSnapshot(BuildMinimalFirmwareTable()));
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, source.InvocationCount);
        Assert.All(result.Observations, observation =>
            Assert.Equal(ProvenanceKind.SystemGenerated, observation.Metadata.Provenance.Kind));
    }

    [Fact]
    public async Task InFlightCallerCancellationIsObservedWithoutPublishingValues()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DelegateSystemInventorySource(cancellationToken =>
        {
            entered.TrySetResult(true);
            try
            {
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return CompleteSnapshot(BuildMinimalFirmwareTable());
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult(true);
                }
            }
        });
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);
        using var cancellation = new CancellationTokenSource();

        var pending = new InventoryProviderRunner().RunAsync(provider, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        var result = await pending;
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, source.InvocationCount);
        Assert.All(result.Observations, static observation => Assert.Null(observation.Value));
    }

    [Fact]
    public async Task CancellationAfterAcquisitionClearsTheOwnedFirmwareBuffer()
    {
        var firmwareTable = BuildMinimalFirmwareTable();
        using var cancellation = new CancellationTokenSource();
        var source = new DelegateSystemInventorySource(_ =>
        {
            var snapshot = CompleteSnapshot(firmwareTable);
            cancellation.Cancel();
            return snapshot;
        });
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);

        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Cancelled, result.Outcome);
        Assert.All(result.Observations, static observation => Assert.Null(observation.Value));
        Assert.True(
            SpinWait.SpinUntil(
                () => firmwareTable.All(static value => value == 0),
                TimeSpan.FromSeconds(1)),
            "The cancelled SMBIOS buffer was not cleared within the bounded cleanup interval.");
    }

    [Fact]
    public async Task ObserveAsyncYieldsBeforeABlockingSourceCompletes()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DelegateSystemInventorySource(cancellationToken =>
        {
            entered.TrySetResult(true);
            release.Wait(cancellationToken);
            return CompleteSnapshot(BuildMinimalFirmwareTable());
        });
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);

        var pending = provider.ObserveAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        var result = await pending;

        Assert.Equal(ProviderReadStatus.Collected, result.Status);
    }

    [Fact]
    public async Task ConcreteProviderTimeoutSignalsTheCooperativeSource()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DelegateSystemInventorySource(cancellationToken =>
        {
            entered.TrySetResult(true);
            try
            {
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return CompleteSnapshot(BuildMinimalFirmwareTable());
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult(true);
                }
            }
        });
        var provider = new SystemInventoryProvider(source, ProviderEvidenceOrigin.SyntheticFixture);
        var stopwatch = Stopwatch.StartNew();

        var result = await new InventoryProviderRunner().RunAsync(provider);
        stopwatch.Stop();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ProviderRunOutcome.TimedOut, result.Outcome);
        Assert.Equal("provider_collection_timed_out", result.Error!.ErrorCode);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
        Assert.All(result.Observations, static observation => Assert.Null(observation.Value));
    }

    [Fact]
    public void ConcreteWindowsSourceUsesOnlyTheReviewedReadApis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var windowsSourceRoot = Path.Combine(repositoryRoot, "src", "ThirdLife.Inventory", "Windows");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(windowsSourceRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var forbiddenFragments = new[]
        {
            "System.Diagnostics.Process",
            "System.Management",
            "System.Management.Automation",
            "Microsoft.Win32",
            "PowerShell",
            "pwsh.exe",
            "cmd.exe",
            "Process.Start",
            "RegistryKey",
            "File.Write",
            "File.Create",
            "File.Delete",
            "Directory.CreateDirectory",
            "Directory.Delete",
            "System.Net",
            "HttpClient",
            "ThirdLife.Diagnostics",
            "\"runas\"",
        };

        foreach (var forbidden in forbiddenFragments)
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(3, CountOccurrences(source, "[DllImport(\"kernel32.dll\""));
        Assert.Equal(3, CountOccurrences(source, " static extern "));
        Assert.Contains("GetSystemFirmwareTable", source, StringComparison.Ordinal);
        Assert.Contains("GetPhysicallyInstalledSystemMemory", source, StringComparison.Ordinal);
        Assert.Contains("GetActiveProcessorCount", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.OSArchitecture", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeInformation.ProcessArchitecture", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNativeSystemInfo", source, StringComparison.Ordinal);

        var project = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "ThirdLife.Inventory",
            "ThirdLife.Inventory.csproj"));
        Assert.DoesNotContain("PackageReference", project, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatingSystemArchitectureRemainsNativeUnderProcessEmulation()
    {
        const Architecture emulatedProcessArchitecture = Architecture.X64;

        var nativeArchitecture = WindowsSystemInventorySource.MapOperatingSystemArchitecture(
            Architecture.Arm64);
        var processArchitecture = WindowsSystemInventorySource.MapOperatingSystemArchitecture(
            emulatedProcessArchitecture);

        Assert.Equal(SystemInventorySourceStatus.Available, nativeArchitecture.Status);
        Assert.Equal((ushort)12, nativeArchitecture.GetRequiredValue());
        Assert.NotEqual(
            processArchitecture.GetRequiredValue(),
            nativeArchitecture.GetRequiredValue());
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public async Task ActiveMachineSmokeIsUnelevatedBoundedAndIdentitySilent()
    {
        Assert.True(
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000),
            "The active TL-0106 smoke requires the supported Windows 11 host.");
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        Assert.False(
            principal.IsInRole(WindowsBuiltInRole.Administrator),
            "The active TL-0106 smoke must run with a standard-user token.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var provider = new SystemInventoryProvider();
        var result = await new InventoryProviderRunner().RunAsync(provider, cancellation.Token);

        Assert.Equal(ProviderRunOutcome.Completed, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(SystemInventoryProvider.ProviderIdentity, result.ProviderId.Value);
        Assert.Contains(result.Observations, static observation =>
            observation.EvidenceKey.Value == "system.architecture" &&
            observation.Metadata.ValueAvailability == ValueAvailability.Available);
        Assert.Contains(result.Observations, static observation =>
            observation.EvidenceKey.Value == "memory.installed_bytes" &&
            observation.Metadata.ValueAvailability == ValueAvailability.Available &&
            observation.Unit == "bytes");
        Assert.Contains(result.Observations, static observation =>
            observation.EvidenceKey.Value == "processor.logical_count" &&
            observation.Metadata.ValueAvailability == ValueAvailability.Available &&
            observation.Unit == "count");
        Assert.True(
            GetSingleEnum(result, "system.architecture") is "x86" or "arm32" or "x64" or "arm64",
            "The active operating-system architecture was not in the reviewed Windows set.");
        Assert.InRange(
            GetSingleInteger(result, "memory.installed_bytes"),
            1,
            SystemInventoryProvider.MaximumInstalledMemoryBytes);
        Assert.InRange(
            GetSingleInteger(result, "processor.logical_count"),
            1,
            SystemInventoryProvider.MaximumLogicalProcessorCount);
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal(PrivacyClassification.WorkshopRestricted, observation.Metadata.PrivacyClassification);
            Assert.Equal(ProvenanceKind.ProviderObservation, observation.Metadata.Provenance.Kind);
            Assert.Equal(provider.Descriptor.ProviderId, observation.Metadata.ProviderId);
            Assert.True(
                observation.Value?.StringValue is null or { Length: <= SmbiosInventoryParser.MaximumTextCharacters },
                "An active-machine string exceeded the reviewed provider bound.");
        });
        Assert.InRange(
            result.Observations.Count,
            provider.Descriptor.EvidenceDefinitions.Count,
            InventoryProviderDescriptor.MaximumEvidenceKeyCount);
    }

    private const int RawTableOffset = SmbiosInventoryParser.RawHeaderBytes;

    private static void AssertDefinition(
        Dictionary<string, ProviderEvidenceDefinition> definitions,
        string key,
        EvidenceValueKind kind,
        string? unit = null,
        int maximumCardinality = 1)
    {
        var definition = definitions[key];
        Assert.Equal(kind, definition.ValueKind);
        Assert.Equal(unit, definition.Unit);
        Assert.Equal(maximumCardinality, definition.MaximumCardinality);
    }

    private static void AssertUnknown(ProviderRunResult result, string key, string limitationCode)
    {
        var observations = result.Observations
            .Where(observation => observation.EvidenceKey.Value == key)
            .ToArray();
        Assert.NotEmpty(observations);
        Assert.All(observations, observation =>
        {
            Assert.Equal(EvidenceClassification.NotAvailable, observation.Metadata.EvidenceClassification);
            Assert.Equal(ValueAvailability.Unknown, observation.Metadata.ValueAvailability);
            Assert.Null(observation.Value);
            Assert.Equal(limitationCode, observation.LimitationCode);
        });
    }

    private static string GetSingleText(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key)
            .Value!.StringValue!;

    private static string GetSingleEnum(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key)
            .Value!.StringValue!;

    private static long GetSingleInteger(ProviderRunResult result, string key) =>
        Assert.Single(result.Observations, observation => observation.EvidenceKey.Value == key)
            .Value!.IntegerValue!.Value;

    private static SystemInventoryFixture LoadSyntheticFixture()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ThirdLife.Inventory.Tests",
            "TestData",
            "system-inventory-cases.v1.json");
        return JsonSerializer.Deserialize<SystemInventoryFixture>(File.ReadAllBytes(path), FixtureJsonOptions)
            ?? throw new InvalidDataException("The system-inventory fixture is empty.");
    }

    private static SystemInventorySourceSnapshot CompleteSnapshot(
        byte[] firmwareTable,
        ushort nativeArchitecture = 9,
        ulong installedMemoryKilobytes = 16_777_216,
        uint logicalProcessorCount = 12) =>
        new(
            SystemInventorySourceValue<byte[]>.Available(firmwareTable),
            SystemInventorySourceValue<ushort>.Available(nativeArchitecture),
            SystemInventorySourceValue<ulong>.Available(installedMemoryKilobytes),
            SystemInventorySourceValue<uint>.Available(logicalProcessorCount));

    private static byte[] BuildFirmwareTable(SystemInventoryFixtureCase fixtureCase) =>
        BuildFirmwareTable(
            fixtureCase.Manufacturer,
            fixtureCase.Model,
            fixtureCase.SerialNumber,
            [fixtureCase.ChassisType],
            fixtureCase.Processors);

    private static byte[] BuildMinimalFirmwareTable() =>
        BuildFirmwareTable(
            "Example Devices Alpha",
            "Synthetic Workstation A1",
            "TEST-ONLY-ALPHA-0001",
            [3],
            [DefaultProcessor()]);

    private static ProcessorFixture DefaultProcessor() => new()
    {
        Handle = 0x0400,
        Manufacturer = "Example Silicon Alpha",
        Model = "Synthetic Core 6",
    };

    private static byte[] BuildFirmwareTable(
        string manufacturer,
        string model,
        string serialNumber,
        IReadOnlyList<byte> chassisTypes,
        IReadOnlyList<ProcessorFixture> processors)
    {
        var structures = new List<byte[]>
        {
            CreateType1Structure(manufacturer, model, serialNumber, handle: 0x0100),
        };
        for (var index = 0; index < chassisTypes.Count; index++)
        {
            structures.Add(CreateType3Structure(
                chassisTypes[index],
                checked((ushort)(0x0300 + index))));
        }

        structures.AddRange(processors.Select(processor => CreateType4Structure(
            processor.Manufacturer,
            processor.Model,
            processor.Handle)));
        structures.Add(CreateEndStructure());
        return BuildRawFirmwareTable(structures.ToArray());
    }

    private static byte[] CreateType1Structure(
        string manufacturer,
        string model,
        string serialNumber,
        ushort handle)
    {
        var formatted = CreateFormattedStructure(type: 1, length: 0x1b, handle);
        formatted[4] = 1;
        formatted[5] = 2;
        formatted[6] = 3;
        formatted[7] = 4;
        return AppendStrings(formatted, manufacturer, model, "Synthetic Version", serialNumber);
    }

    private static byte[] CreateType3Structure(byte chassisType, ushort handle)
    {
        var formatted = CreateFormattedStructure(type: 3, length: 0x09, handle);
        formatted[5] = chassisType;
        return AppendStrings(formatted);
    }

    private static byte[] CreateType4Structure(
        string manufacturer,
        string model,
        ushort handle,
        byte processorType = 0x03,
        bool populated = true)
    {
        var formatted = CreateFormattedStructure(type: 4, length: 0x1a, handle);
        formatted[5] = processorType;
        formatted[7] = 1;
        formatted[16] = 2;
        formatted[24] = populated ? (byte)0x41 : (byte)0x01;
        return AppendStrings(formatted, manufacturer, model);
    }

    private static byte[] CreateEndStructure() =>
        AppendStrings(CreateFormattedStructure(type: 127, length: 4, handle: 0x7f00));

    private static byte[] CreateFormattedStructure(byte type, byte length, ushort handle)
    {
        var formatted = new byte[length];
        formatted[0] = type;
        formatted[1] = length;
        BinaryPrimitives.WriteUInt16LittleEndian(formatted.AsSpan(2, 2), handle);
        return formatted;
    }

    private static byte[] AppendStrings(byte[] formatted, params string[] values)
    {
        using var stream = new MemoryStream();
        stream.Write(formatted);
        foreach (var value in values)
        {
            stream.Write(Encoding.UTF8.GetBytes(value));
            stream.WriteByte(0);
        }

        if (values.Length == 0)
        {
            stream.WriteByte(0);
        }

        stream.WriteByte(0);
        return stream.ToArray();
    }

    private static byte[] BuildRawFirmwareTable(params byte[][] structures)
    {
        var tableLength = structures.Sum(static structure => structure.Length);
        var raw = new byte[RawTableOffset + tableLength];
        raw[0] = 0;
        raw[1] = 3;
        raw[2] = 6;
        raw[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4, 4), checked((uint)tableLength));
        var offset = RawTableOffset;
        foreach (var structure in structures)
        {
            structure.CopyTo(raw, offset);
            offset += structure.Length;
        }

        return raw;
    }

    private static int FindStructureOffset(byte[] raw, byte structureType)
    {
        var offset = RawTableOffset;
        while (offset < raw.Length)
        {
            if (raw[offset] == structureType)
            {
                return offset;
            }

            var stringSetStart = offset + raw[offset + 1];
            var stringSetEnd = stringSetStart;
            while (stringSetEnd < raw.Length - 1 &&
                   (raw[stringSetEnd] != 0 || raw[stringSetEnd + 1] != 0))
            {
                stringSetEnd++;
            }

            offset = stringSetEnd + 2;
        }

        throw new InvalidDataException("The requested synthetic SMBIOS structure was not found.");
    }

    private static void WriteDeclaredLength(byte[] raw) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            raw.AsSpan(4, 4),
            checked((uint)(raw.Length - RawTableOffset)));

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
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

    private static string GetProfileMetadata(string profile, string field)
    {
        var prefix = $"**{field}:** `";
        var line = profile.Split('\n').Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        var valueStart = prefix.Length;
        var valueEnd = line.IndexOf('`', valueStart);
        return valueEnd > valueStart
            ? line[valueStart..valueEnd]
            : throw new InvalidDataException($"The sanitized profile field '{field}' is malformed.");
    }

    private static string GetProfileTableValue(string profile, string field)
    {
        var prefix = $"| {field} |";
        var line = profile.Split('\n').Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        var cells = line.Split('|', StringSplitOptions.TrimEntries);
        return cells.Length == 4 && cells[2].Length > 0
            ? cells[2]
            : throw new InvalidDataException($"The sanitized profile row '{field}' is malformed.");
    }

    private sealed class DelegateSystemInventorySource : ISystemInventorySource
    {
        private readonly Func<CancellationToken, SystemInventorySourceSnapshot> _read;
        private int _invocationCount;

        public DelegateSystemInventorySource(
            Func<CancellationToken, SystemInventorySourceSnapshot> read)
        {
            _read = read;
        }

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public SystemInventorySourceSnapshot Read(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return _read(cancellationToken);
        }
    }

    private sealed class SystemInventoryFixture
    {
        [JsonPropertyName("schema_version")]
        public required string SchemaVersion { get; init; }

        [JsonPropertyName("fixture_id")]
        public required string FixtureId { get; init; }

        [JsonPropertyName("synthetic_data")]
        public required bool SyntheticData { get; init; }

        [JsonPropertyName("classification")]
        public required string Classification { get; init; }

        [JsonPropertyName("cases")]
        public required List<SystemInventoryFixtureCase> Cases { get; init; }
    }

    private sealed class SystemInventoryFixtureCase
    {
        [JsonPropertyName("case_id")]
        public required string CaseId { get; init; }

        [JsonPropertyName("manufacturer")]
        public required string Manufacturer { get; init; }

        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("serial_number")]
        public required string SerialNumber { get; init; }

        [JsonPropertyName("chassis_type")]
        public required byte ChassisType { get; init; }

        [JsonPropertyName("processors")]
        public required List<ProcessorFixture> Processors { get; init; }

        [JsonPropertyName("native_architecture")]
        public required ushort NativeArchitecture { get; init; }

        [JsonPropertyName("installed_memory_kib")]
        public required ulong InstalledMemoryKib { get; init; }

        [JsonPropertyName("logical_processor_count")]
        public required uint LogicalProcessorCount { get; init; }

        [JsonPropertyName("expected_device_type")]
        public required string ExpectedDeviceType { get; init; }

        [JsonPropertyName("expected_architecture")]
        public required string ExpectedArchitecture { get; init; }

        [JsonPropertyName("expected_memory_bytes")]
        public required long ExpectedMemoryBytes { get; init; }
    }

    private sealed class ProcessorFixture
    {
        [JsonPropertyName("handle")]
        public required ushort Handle { get; init; }

        [JsonPropertyName("manufacturer")]
        public required string Manufacturer { get; init; }

        [JsonPropertyName("model")]
        public required string Model { get; init; }
    }
}
