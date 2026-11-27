using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Registry;
using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Actions.Tests;

public sealed class ActionRegistryTests
{
    private const string ContractError = "Action request does not satisfy the ThirdLife action v1 contract.";
    private const string SourceId = "fixture-repository";
    private const string PackageId = "Fictional.Tests.WebBrowser";
    private const string ExactVersion = "2.15.236.8";
    private const string Publisher = "Fictional Publisher";
    private static readonly Guid ActionId = new("6a39bdd2-f365-44e3-945b-cfae18bc77aa");
    private const string NoOpJson = """
        {"contract_version":1,"action_id":"6a39bdd2-f365-44e3-945b-cfae18bc77aa","type":"no_op","parameters":{}}
        """;
    private const string PackageJson = """
        {
          "contract_version": 1,
          "action_id": "6a39bdd2-f365-44e3-945b-cfae18bc77aa",
          "type": "install_package",
          "parameters": {
            "source_id": "fixture-repository",
            "package_id": "Fictional.Tests.WebBrowser",
            "exact_version": "2.15.236.8",
            "publisher": "Fictional Publisher",
            "scope": "machine",
            "architecture": "x64"
          }
        }
        """;

    [Fact]
    public void DescribesTheElevatedNoOpWithoutMachineSideEffects()
    {
        var request = ActionRegistry.CreateNoOp(ActionId);
        var definition = ActionRegistry.Describe(request);

        Assert.Equal(ActionId, request.ActionId);
        Assert.Equal(
            new ActionDefinition(
                ActionKind.NoOp, ActionScope.Machine, ActionPrivilege.Elevated, ActionReboot.None,
                ActionNetworkUse.None, ActionVerification.NoMachineChange, ActionRollback.NoChanges,
                ActionIdempotency.Repeatable, ActionAvailability.BrokerNoOp),
            definition);
    }

    [Theory]
    [InlineData(ActionScope.User, ActionPrivilege.Unelevated)]
    [InlineData(ActionScope.Machine, ActionPrivilege.Elevated)]
    public void DescribesPackageScopeAndConservativeRecovery(ActionScope scope, ActionPrivilege privilege)
    {
        var request = CreatePackage(scope, ActionArchitecture.X64);
        var definition = ActionRegistry.Describe(request);

        Assert.Equal(
            new ActionDefinition(
                ActionKind.InstallPackage, scope, privilege, ActionReboot.MayRequire,
                ActionNetworkUse.SoftwareDownload, ActionVerification.ExactPackageIdentity, ActionRollback.ManualReview,
                ActionIdempotency.ReobserveBeforeRetry, ActionAvailability.PackageBackendRequired),
            definition);
    }

    [Fact]
    public void RoundTripsTheVersionedNoOpContract()
    {
        var request = Assert.IsType<NoOpActionRequest>(Parse(NoOpJson));
        Assert.Equal(ActionId, request.ActionId);
        Assert.Equal(ActionKind.NoOp, request.Kind);
        Assert.Equal(ActionRequestCodec.Serialize(ActionRegistry.CreateNoOp(ActionId)), ActionRequestCodec.Serialize(request));
        var wire = JsonNode.Parse(ActionRequestCodec.Serialize(request))!;
        Assert.Equal(ActionRequestCodec.ContractVersion, wire["contract_version"]!.GetValue<int>());
        Assert.Equal("no_op", wire["type"]!.GetValue<string>());
        Assert.Empty(wire["parameters"]!.AsObject());
    }

    [Theory]
    [InlineData("machine", ActionScope.Machine, "x64", ActionArchitecture.X64)]
    [InlineData("user", ActionScope.User, "arm64", ActionArchitecture.Arm64)]
    [InlineData("user", ActionScope.User, "x86", ActionArchitecture.X86)]
    public void RoundTripsOnlyTheExactTypedPackageIdentity(
        string scopeToken,
        ActionScope scope,
        string architectureToken,
        ActionArchitecture architecture)
    {
        var input = Package();
        var parameters = input["parameters"]!.AsObject();
        parameters["scope"] = scopeToken;
        parameters["architecture"] = architectureToken;

        var request = Assert.IsType<InstallPackageActionRequest>(Parse(input.ToJsonString()));
        Assert.Equal(ActionId, request.ActionId);
        Assert.Equal(ActionKind.InstallPackage, request.Kind);
        Assert.Equal(SourceId, request.SourceId);
        Assert.Equal(PackageId, request.PackageId);
        Assert.Equal(ExactVersion, request.ExactVersion);
        Assert.Equal(Publisher, request.Publisher);
        Assert.Equal(scope, request.Scope);
        Assert.Equal(architecture, request.Architecture);
        Assert.Equal(ActionRequestCodec.Serialize(CreatePackage(scope, architecture)), ActionRequestCodec.Serialize(request));
    }

    [Theory]
    [InlineData("contract_version", "2")]
    [InlineData("contract_version", "\"1\"")]
    [InlineData("type", "\"unknown_action\"")]
    [InlineData("type", "1")]
    [InlineData("action_id", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("action_id", "\"not-an-action-id\"")]
    [InlineData("parameters", "[]")]
    public void RejectsUnknownOrMalformedEnvelopeValues(string field, string jsonValue)
    {
        var input = Package();
        input[field] = JsonNode.Parse(jsonValue);

        Reject(input.ToJsonString());
    }

    [Theory]
    [InlineData("source_id", "https://example.invalid/source")]
    [InlineData("source_id", "synthetic-fixture-source")]
    [InlineData("package_id", "Generic.Synthetic.Package")]
    [InlineData("package_id", "../unapproved-package")]
    [InlineData("package_id", "C:/fixtures/artifact")]
    [InlineData("exact_version", "latest")]
    [InlineData("exact_version", "1.*")]
    [InlineData("publisher", "HKLM\\Fixture")]
    [InlineData("publisher", "Fictional Publisher | Extra")]
    [InlineData("publisher", "Fictional\nPublisher")]
    [InlineData("scope", "all")]
    [InlineData("architecture", "unknown")]
    public void RejectsInvalidPackageParameters(string field, string value)
    {
        var input = Package();
        input["parameters"]![field] = value;

        Reject(input.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiresOnlyTheFieldsDeclaredForTheAction(bool noOp)
    {
        var input = noOp ? JsonNode.Parse(NoOpJson)!.AsObject() : Package();
        var parameters = input["parameters"]!.AsObject();

        RejectRemovedFields(input, input);
        RejectRemovedFields(input, parameters);

        input["extra"] = true;
        Reject(input.ToJsonString());
        input.Remove("extra");
        parameters["installer_arguments"] = "unrecognized";
        Reject(input.ToJsonString());
    }

    [Fact]
    public void ValidatesProgrammaticRequestsBeforeTheyCanBeSerialized()
    {
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateNoOp(Guid.Empty));
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateInstallPackage(
            Guid.Empty, SourceId, PackageId, ExactVersion, Publisher, ActionScope.User, ActionArchitecture.X64));
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateInstallPackage(
            ActionId, "https://example.invalid/source", PackageId, ExactVersion, Publisher, ActionScope.User, ActionArchitecture.X64));
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateInstallPackage(
            ActionId, SourceId, "../unapproved-package", ExactVersion, Publisher, ActionScope.User, ActionArchitecture.X64));
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateInstallPackage(
            ActionId, SourceId, PackageId, "latest", Publisher, ActionScope.User, ActionArchitecture.X64));
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateInstallPackage(
            ActionId, SourceId, PackageId, ExactVersion, "Fictional | Extra", ActionScope.User, ActionArchitecture.X64));
        Assert.ThrowsAny<ArgumentException>(() => CreatePackage((ActionScope)99, ActionArchitecture.X64));
        Assert.ThrowsAny<ArgumentException>(() => CreatePackage(ActionScope.User, (ActionArchitecture)99));
    }

    [Theory]
    [InlineData("source_id", "s", 64)]
    [InlineData("package_id", "F.", 128)]
    [InlineData("exact_version", "1", 64)]
    [InlineData("publisher", "F", 128)]
    public void EnforcesTheBoundsOnTypedIdentityFields(string field, string prefix, int maximumLength)
    {
        var input = Package();
        var valueAtLimit = prefix + new string('a', maximumLength - prefix.Length);
        input["parameters"]![field] = valueAtLimit;
        Assert.IsType<InstallPackageActionRequest>(Parse(input.ToJsonString()));

        input["parameters"]![field] = valueAtLimit + "a";
        Reject(input.ToJsonString());
    }

    [Fact]
    public void RejectsOversizedMalformedAndRepeatedJson()
    {
        var padding = ActionRequestCodec.MaximumDocumentBytes - Encoding.UTF8.GetByteCount(NoOpJson);
        var atLimit = NoOpJson + new string(' ', padding);
        Assert.IsType<NoOpActionRequest>(Parse(atLimit));
        Reject(atLimit + " ");
        Reject(string.Empty);

        Reject(NoOpJson.Insert(1, """
            "\u0074ype":"no_op",
            """));
        Reject(PackageJson.Replace("\"scope\": \"machine\"", """
            "\u0073cope": "machine", "scope": "machine"
            """, StringComparison.Ordinal));

        var invalidUtf8 = Encoding.UTF8.GetBytes(PackageJson.Replace(Publisher, "~", StringComparison.Ordinal));
        invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'~')] = 0xFF;
        Reject(invalidUtf8);
    }

    [Fact]
    public void RequestsExposeOnlyTheCompiledGetterOnlyInputs()
    {
        var baseType = typeof(ActionRequest);
        var requestTypes = baseType.Assembly.GetTypes()
            .Where(type => !type.IsAbstract && baseType.IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal);
        Assert.Equal([typeof(InstallPackageActionRequest), typeof(NoOpActionRequest), typeof(WindowsUpdateActionRequest)], requestTypes);
        Assert.All(
            baseType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => Assert.True(constructor.IsFamilyAndAssembly || constructor.IsPrivate || constructor.IsAssembly));

        AssertRequestShape(typeof(NoOpActionRequest),
            ("ActionId", typeof(Guid)),
            ("Kind", typeof(ActionKind)));
        AssertRequestShape(typeof(InstallPackageActionRequest),
            ("ActionId", typeof(Guid)),
            ("Architecture", typeof(ActionArchitecture)),
            ("ExactVersion", typeof(string)),
            ("Kind", typeof(ActionKind)),
            ("PackageId", typeof(string)),
            ("Publisher", typeof(string)),
            ("Scope", typeof(ActionScope)),
            ("SourceId", typeof(string)));
        AssertRequestShape(typeof(WindowsUpdateActionRequest),
            ("ActionId", typeof(Guid)),
            ("Exact", typeof(WindowsUpdateCandidate)),
            ("Kind", typeof(ActionKind)));
    }

    [Theory]
    [InlineData(ActionKind.ScanWindowsUpdate, ActionVerification.WindowsUpdateScan)]
    [InlineData(ActionKind.DownloadWindowsUpdate, ActionVerification.ExactDownloadedUpdateRevision)]
    [InlineData(ActionKind.InstallWindowsUpdate, ActionVerification.ExactInstalledUpdateRevision)]
    public void UpdateActionsBindOnlyExactAllowlistedFieldsAndUseASeparateBackend(ActionKind kind, ActionVerification verification)
    {
        var exact = new WindowsUpdateCandidate(new WindowsUpdateIdentity(ActionId, 4), WindowsUpdateClass.Security, 700);
        var request = kind switch
        {
            ActionKind.ScanWindowsUpdate => ActionRegistry.CreateScanWindowsUpdate(ActionId),
            ActionKind.DownloadWindowsUpdate => ActionRegistry.CreateDownloadWindowsUpdate(ActionId, exact),
            ActionKind.InstallWindowsUpdate => ActionRegistry.CreateInstallWindowsUpdate(ActionId, exact),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var decoded = Assert.IsType<WindowsUpdateActionRequest>(ActionRequestCodec.Parse(ActionRequestCodec.Serialize(request)));
        Assert.Equal(request.Kind, decoded.Kind);
        Assert.Equal(request.Exact, decoded.Exact);
        Assert.Equal(ActionAvailability.WindowsUpdateAgentRequired, ActionRegistry.Describe(decoded).Availability);
        Assert.Equal(verification, ActionRegistry.Describe(decoded).Verification);
        var envelope = JsonNode.Parse(ActionRequestCodec.Serialize(request))!.AsObject();
        var parameters = envelope["parameters"]!.AsObject();
        RejectRemovedFields(envelope, parameters);
        parameters["service_url"] = "https://example.invalid/";
        Reject(envelope.ToJsonString());
        parameters.Remove("service_url");
        parameters["classification"] = "driver";
        Reject(envelope.ToJsonString());
        Assert.ThrowsAny<ArgumentException>(() => ActionRegistry.CreateInstallWindowsUpdate(ActionId,
            new WindowsUpdateCandidate(exact.Identity, WindowsUpdateClass.Restricted)));
    }

    private static void AssertRequestShape(Type requestType, params (string Name, Type Type)[] expectedProperties)
    {
        Assert.True(requestType.IsSealed);
        Assert.Empty(requestType.GetConstructors());
        var properties = requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedProperties, properties.Select(property => (property.Name, property.PropertyType)));
        Assert.All(properties, property => Assert.False(property.CanWrite));
    }

    private static void RejectRemovedFields(JsonObject root, JsonObject selected)
    {
        foreach (var name in selected.Select(property => property.Key).ToArray())
        {
            var original = selected[name];
            selected.Remove(name);
            Reject(root.ToJsonString());
            selected[name] = original;
        }
    }

    private static JsonObject Package() => JsonNode.Parse(PackageJson)!.AsObject();

    private static InstallPackageActionRequest CreatePackage(ActionScope scope, ActionArchitecture architecture) =>
        ActionRegistry.CreateInstallPackage(ActionId, SourceId, PackageId, ExactVersion, Publisher, scope, architecture);

    private static ActionRequest Parse(string json) => ActionRequestCodec.Parse(Encoding.UTF8.GetBytes(json));

    private static void Reject(string json) => Reject(Encoding.UTF8.GetBytes(json));

    private static void Reject(byte[] utf8Json)
    {
        var exception = Assert.Throws<JsonException>(() => ActionRequestCodec.Parse(utf8Json));
        Assert.Equal(ContractError, exception.Message);
    }
}
