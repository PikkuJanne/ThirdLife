using System.Reflection;
using ThirdLife.Diagnostics.Logging;
using ThirdLife.Diagnostics.Redaction;

namespace ThirdLife.Diagnostics.Tests;

public sealed class PrivacyArchitectureTests
{
    [Fact]
    public void ProductionAssemblyHasNoPersistenceNetworkTelemetryOrUploadDependency()
    {
        var assembly = typeof(AssemblyMarker).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(static reference => reference.Name).ToArray();

        Assert.DoesNotContain("ThirdLife.Persistence", references);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", references);
        Assert.DoesNotContain(references, static name =>
            name?.StartsWith("System.Net.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(references, static name =>
            name?.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase) == true ||
            name?.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase) == true);

        Assert.DoesNotContain(
            assembly.GetTypes(),
            static type =>
                type.Name.Contains("Telemetry", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("Uploader", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("BackgroundWorker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RawFixturePolicyAndSupportConstructionAreNotPublicApis()
    {
        Assert.False(typeof(RedactionEngine).IsPublic);
        Assert.False(typeof(RedactionResult).IsPublic);
        Assert.False(typeof(SupportField).IsPublic);
        Assert.False(typeof(SupportValue).IsPublic);
        Assert.Null(
            typeof(SupportProjection).GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void OrdinaryEventEntryPointAcceptsNoRawStringObjectMapOrExceptionPayload()
    {
        var create = Assert.Single(
            typeof(StructuredDiagnosticEvent).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.Name == "Create");
        var parameterTypes = create.GetParameters().Select(static parameter => parameter.ParameterType).ToArray();

        Assert.DoesNotContain(typeof(string), parameterTypes);
        Assert.DoesNotContain(typeof(object), parameterTypes);
        Assert.DoesNotContain(typeof(Exception), parameterTypes);
        Assert.DoesNotContain(parameterTypes, static type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        Assert.DoesNotContain(
            typeof(DiagnosticEventField).GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(long)));
    }

    [Fact]
    public void PublicProductionContractsExposeNoRawPayloadMembers()
    {
        var prohibitedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Raw",
            "Payload",
            "Details",
            "Command",
            "Arguments",
            "Environment",
            "StackTrace",
            "InnerException",
        };
        var publicTypes = typeof(AssemblyMarker).Assembly
            .GetExportedTypes()
            .Where(static type => type.Namespace?.StartsWith("ThirdLife.Diagnostics", StringComparison.Ordinal) == true);

        foreach (var type in publicTypes)
        {
            Assert.DoesNotContain(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                property => prohibitedNames.Contains(property.Name));
            Assert.DoesNotContain(
                type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                field => prohibitedNames.Contains(field.Name));
        }
    }

    [Fact]
    public void SensitiveWrapperCannotSerializeOrStringifyItsRawValue()
    {
        const string seed = "SYNTHETIC-NETWORK-NOT-REAL";
        var wrapper = SensitiveDiagnosticValue.Create(RedactionField.WifiSsid, seed);
        var serialized = System.Text.Json.JsonSerializer.Serialize(wrapper);

        Assert.DoesNotContain(seed, wrapper.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(seed, serialized, StringComparison.Ordinal);
        Assert.Equal("[REDACTED:wifi-ssid]", wrapper.ToSafeOrdinaryLogRepresentation()!.ToString());
    }
}
