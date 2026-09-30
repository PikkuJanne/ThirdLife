using System.Reflection;
using System.Runtime.InteropServices;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class InventoryReadOnlyBoundaryTests
{
    [Fact]
    public void AllWindowsProviderImportsAreReviewedReadsOrResourceCleanup()
    {
        var allowed = new HashSet<(string Library, string EntryPoint)>
        {
            ("kernel32.dll", "GetSystemFirmwareTable"),
            ("kernel32.dll", "GetPhysicallyInstalledSystemMemory"),
            ("kernel32.dll", "GetActiveProcessorCount"),
            ("kernel32.dll", "GetProductInfo"),
            ("kernel32.dll", "LocalFree"),
            ("slc.dll", "SLOpen"),
            ("slc.dll", "SLGetLicensingStatusInformation"),
            ("slc.dll", "SLClose"),
            ("advapi32.dll", "RegOpenKeyExW"),
            ("advapi32.dll", "RegQueryValueExW"),
        };
        var imports = typeof(OperatingSystemProvider).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => (Method: method, Import: method.GetCustomAttribute<DllImportAttribute>()))
            .Where(item => item.Import is not null).ToArray();
        Assert.Equal(allowed.Count, imports.Length);
        foreach (var (method, import) in imports)
        {
            Assert.Contains((import!.Value, import.EntryPoint ?? method.Name), allowed);
            Assert.Equal(DllImportSearchPath.System32, method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()!.Paths);
        }
    }

    [Fact]
    public void AllWindowsSourceFilesExcludeExecutionNetworkMutationAndRawLogging()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TASKS.yaml")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var files = Directory.GetFiles(Path.Combine(root.FullName, "src", "ThirdLife.Inventory", "Windows"), "*.cs");
        Assert.NotEmpty(files);
        var forbidden = new[]
        {
            "System.Diagnostics.Process", "System.Management", "Process.Start", "PowerShell", "cmd.exe",
            "pwsh.exe", "File.Write", "File.Create", "File.Delete", "Directory.Create", "Directory.Delete",
            "System.Net", "HttpClient", "ThirdLife.Diagnostics", "Console.Write", "Trace.Write", "Debug.Write",
            "RegistryKey", "Registry.Set", "NativeLibrary.Load", "GetProcAddress", "Marshal.GetDelegateForFunctionPointer",
            "PartialProductKey", "ProductId", "InstallProductKey", "SLActivate", "SLSet", "RegSetValue", "RegCreateKey",
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (var fragment in forbidden)
            {
                Assert.DoesNotContain(fragment, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
