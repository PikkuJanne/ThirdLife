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
            ("kernel32.dll", "GetSystemWindowsDirectoryW"),
            ("kernel32.dll", "GetVolumePathNameW"),
            ("kernel32.dll", "GetDriveTypeW"),
            ("kernel32.dll", "GetDiskFreeSpaceExW"),
            ("ole32.dll", "CoInitializeEx"),
            ("ole32.dll", "CoUninitialize"),
            ("ole32.dll", "CoCreateInstance"),
            ("ole32.dll", "CoSetProxyBlanket"),
            ("oleaut32.dll", "SysStringLen"),
            ("oleaut32.dll", "VariantClear"),
            ("oleaut32.dll", "SafeArrayGetDim"),
            ("oleaut32.dll", "SafeArrayGetVartype"),
            ("oleaut32.dll", "SafeArrayGetElemsize"),
            ("oleaut32.dll", "SafeArrayGetLBound"),
            ("oleaut32.dll", "SafeArrayGetUBound"),
            ("oleaut32.dll", "SafeArrayGetElement"),
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
    public void ComInterfacesExposeOnlyTheReviewedConnectionAndReadSlots()
    {
        var allowed = new Dictionary<Guid, (int ReservedSlots, string Method, Type[] Parameters)>
        {
            [new("DC12A687-737F-11CF-884D-00AA004B2E24")] = (0, "ConnectServer",
                [typeof(string), typeof(nint), typeof(nint), typeof(nint), typeof(int), typeof(nint), typeof(nint), typeof(IStorageWbemServices).MakeByRefType()]),
            [new("9556DC99-828C-11CF-A37E-00AA003240C7")] = (17, "ExecQuery",
                [typeof(string), typeof(string), typeof(int), typeof(nint), typeof(IStorageWbemEnumerator).MakeByRefType()]),
            [new("027947E1-D731-11CE-A357-000000000001")] = (1, "Next",
                [typeof(int), typeof(uint), typeof(IStorageWbemObject).MakeByRefType(), typeof(uint).MakeByRefType()]),
            [new("DC12A681-737F-11CF-884D-00AA004B2E24")] = (1, "Get",
                [typeof(string), typeof(int), typeof(nint), typeof(int).MakeByRefType(), typeof(nint)]),
        };
        var interfaces = typeof(StorageProvider).Assembly.GetTypes().Where(type => type.IsImport).ToArray();
        Assert.Equal(allowed.Count, interfaces.Length);
        foreach (var type in interfaces)
        {
            Assert.True(allowed.TryGetValue(type.GUID, out var contract));
            Assert.True(type.IsInterface && !type.IsPublic);
            Assert.Equal(ComInterfaceType.InterfaceIsIUnknown, type.GetCustomAttribute<InterfaceTypeAttribute>()!.Value);
            var methods = type.GetMethods().OrderBy(method => method.MetadataToken).ToArray();
            Assert.Equal(contract.ReservedSlots + 1, methods.Length);
            for (var index = 0; index < contract.ReservedSlots; index++)
            {
                Assert.Equal($"Reserved{index + 1:00}", methods[index].Name);
                Assert.Equal(typeof(void), methods[index].ReturnType);
                Assert.Empty(methods[index].GetParameters());
            }

            var read = methods[^1];
            Assert.Equal(contract.Method, read.Name);
            Assert.Equal(typeof(int), read.ReturnType);
            Assert.True((read.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0);
            Assert.Equal(contract.Parameters, read.GetParameters().Select(parameter => parameter.ParameterType));
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
            ".Reserved", "ExecMethod", "PutInstance", "DeleteInstance",
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
