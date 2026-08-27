using System.Reflection;

namespace ThirdLife.Core.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Core.AssemblyMarker);

        Assert.Equal("ThirdLife.Core", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Core", marker.Namespace);
    }

    [Fact]
    public void DomainAssemblyHasNoPlatformInfrastructureOrSiblingReferences()
    {
        var forbiddenFragments = new[]
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Diagnostics.Process",
            "System.Management",
            "Microsoft.Win32.Registry",
            "Automation",
            "Sqlite",
            "SQLite",
            "WinGet",
            "PowerShell",
        };
        var references = typeof(global::ThirdLife.Core.AssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            references,
            reference => forbiddenFragments.Any(
                fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            references,
            reference => reference.StartsWith("ThirdLife.", StringComparison.Ordinal));

        var platformImports = typeof(global::ThirdLife.Core.AssemblyMarker)
            .Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.Instance))
            .Where(method =>
                method.Attributes.HasFlag(MethodAttributes.PinvokeImpl) ||
                method.CustomAttributes.Any(attribute =>
                    attribute.AttributeType.FullName is
                        "System.Runtime.InteropServices.DllImportAttribute" or
                        "System.Runtime.InteropServices.LibraryImportAttribute"))
            .ToArray();

        Assert.Empty(platformImports);
    }
}
