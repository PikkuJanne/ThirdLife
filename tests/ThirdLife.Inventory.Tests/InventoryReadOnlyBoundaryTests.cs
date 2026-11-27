using System.Reflection;
using System.Runtime.InteropServices;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class InventoryReadOnlyBoundaryTests
{
    [Fact]
    public void AllWindowsProviderImportsHaveAnExactReviewedOwnerAndOperation()
    {
        var allowed = new HashSet<(Type Owner, string Library, string EntryPoint)>
        {
            (typeof(WindowsDeviceClassNativeApi), "setupapi.dll", "SetupDiGetClassDevsW"),
            (typeof(WindowsDeviceClassNativeApi), "setupapi.dll", "SetupDiEnumDeviceInfo"),
            (typeof(WindowsDeviceClassNativeApi), "setupapi.dll", "SetupDiDestroyDeviceInfoList"),
            (typeof(WindowsDeviceClassNativeApi), "cfgmgr32.dll", "CM_Get_DevNode_Status"),
            (typeof(WindowsDeviceClassNativeApi), "cfgmgr32.dll", "CM_MapCrToWin32Err"),
            (typeof(WindowsDeviceClassNativeApi), "user32.dll", "GetSystemMetrics"),
            (typeof(WindowsSystemInventorySource), "kernel32.dll", "GetSystemFirmwareTable"),
            (typeof(WindowsSystemInventorySource), "kernel32.dll", "GetPhysicallyInstalledSystemMemory"),
            (typeof(WindowsSystemInventorySource), "kernel32.dll", "GetActiveProcessorCount"),
            (typeof(WindowsOperatingSystemSource), "kernel32.dll", "GetProductInfo"),
            (typeof(WindowsOperatingSystemSource), "advapi32.dll", "RegGetValueW"),
            (typeof(WindowsOperatingSystemSource), "kernel32.dll", "VerSetConditionMask"),
            (typeof(WindowsOperatingSystemSource), "kernel32.dll", "VerifyVersionInfoW"),
            (typeof(WindowsActivationNativeApi), "kernel32.dll", "LocalFree"),
            (typeof(WindowsStorageVolumeSource), "kernel32.dll", "GetSystemWindowsDirectoryW"),
            (typeof(WindowsStorageVolumeSource), "kernel32.dll", "GetVolumePathNameW"),
            (typeof(WindowsStorageVolumeSource), "kernel32.dll", "GetVolumeNameForVolumeMountPointW"),
            (typeof(WindowsStorageVolumeSource), "kernel32.dll", "GetDriveTypeW"),
            (typeof(WindowsStorageVolumeSource), "kernel32.dll", "GetDiskFreeSpaceExW"),
            (typeof(StorageWmiNativeMethods), "ole32.dll", "CoInitializeEx"),
            (typeof(StorageWmiNativeMethods), "ole32.dll", "CoUninitialize"),
            (typeof(StorageWmiNativeMethods), "ole32.dll", "CoCreateInstance"),
            (typeof(StorageWmiNativeMethods), "ole32.dll", "CoSetProxyBlanket"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SysStringLen"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "VariantClear"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SafeArrayGetDim"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SafeArrayGetVartype"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SafeArrayGetElemsize"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SafeArrayGetLBound"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SafeArrayGetUBound"),
            (typeof(StorageWmiNativeMethods), "oleaut32.dll", "SafeArrayGetElement"),
            (typeof(WindowsActivationNativeApi), "slc.dll", "SLOpen"),
            (typeof(WindowsActivationNativeApi), "slc.dll", "SLGetLicensingStatusInformation"),
            (typeof(WindowsActivationNativeApi), "slc.dll", "SLClose"),
            (typeof(WindowsSecurityBaselineSource), "wscapi.dll", "WscGetSecurityProviderHealth"),
            (typeof(WindowsSecurityBaselineSource), "netapi32.dll", "NetGetJoinInformation"),
            (typeof(WindowsSecurityBaselineSource), "netapi32.dll", "NetApiBufferFree"),
            (typeof(WindowsSecurityBaselineSource), "netapi32.dll", "NetGetAadJoinInformation"),
            (typeof(WindowsSecurityBaselineSource), "netapi32.dll", "NetFreeAadJoinInformation"),
            (typeof(WindowsSecurityBaselineSource), "MDMRegistration.dll", "IsDeviceRegisteredWithManagement"),
            (typeof(WindowsSecurityBaselineSource), "advapi32.dll", "RegGetValueW"),
            (typeof(WindowsTimeSyncSource), "wevtapi.dll", "EvtQuery"),
            (typeof(WindowsTimeSyncSource), "wevtapi.dll", "EvtNext"),
            (typeof(WindowsTimeSyncSource), "wevtapi.dll", "EvtCreateRenderContext"),
            (typeof(WindowsTimeSyncSource), "wevtapi.dll", "EvtRender"),
            (typeof(WindowsTimeSyncSource), "wevtapi.dll", "EvtClose"),
            (typeof(WindowsPendingRebootSource), "advapi32.dll", "RegOpenKeyExW"),
            (typeof(WindowsPendingRebootSource), "advapi32.dll", "RegQueryValueExW"),
            (typeof(WindowsFirmwarePlatformNativeApi), "kernel32.dll", "GetFirmwareType"),
            (typeof(WindowsFirmwarePlatformNativeApi), "advapi32.dll", "RegOpenKeyExW"),
            (typeof(WindowsFirmwarePlatformNativeApi), "advapi32.dll", "RegQueryValueExW"),
            (typeof(WindowsTpmSource), "tbs.dll", "Tbsi_GetDeviceInfo"),
            (typeof(WindowsSystemPowerSource), "kernel32.dll", "GetSystemPowerStatus"),
            (typeof(BatteryReportProcess), "kernel32.dll", "GetSystemDirectoryW"),
            (typeof(BatteryReportProcess), "kernel32.dll", "CreateJobObjectW"),
            (typeof(BatteryReportProcess), "kernel32.dll", "SetInformationJobObject"),
            (typeof(BatteryReportProcess), "kernel32.dll", "CreateProcessW"),
            (typeof(BatteryReportProcess), "kernel32.dll", "AssignProcessToJobObject"),
            (typeof(BatteryReportProcess), "kernel32.dll", "ResumeThread"),
            (typeof(BatteryReportProcess), "kernel32.dll", "WaitForSingleObject"),
            (typeof(BatteryReportProcess), "kernel32.dll", "QueryInformationJobObject"),
            (typeof(BatteryReportProcess), "kernel32.dll", "GetExitCodeProcess"),
            (typeof(BatteryReportProcess), "kernel32.dll", "TerminateJobObject"),
            (typeof(BatteryReportProcess), "kernel32.dll", "TerminateProcess"),
            (typeof(BatteryReportFiles), "kernel32.dll", "CreateFileW"),
            (typeof(BatteryReportFiles), "kernel32.dll", "GetFileInformationByHandle"),
            (typeof(BatteryReportFiles), "kernel32.dll", "GetFinalPathNameByHandleW"),
            (typeof(BatteryReportFiles), "kernel32.dll", "SetFileInformationByHandle"),
            (typeof(BatteryReportFiles), "kernel32.dll", "GetDiskFreeSpaceExW"),
            (typeof(BatteryReportFiles), "kernel32.dll", "GetDriveTypeW"),
            (typeof(BatteryReportFiles), "shell32.dll", "SHGetKnownFolderPath"),
            (typeof(BatteryReportFiles), "ole32.dll", "CoTaskMemFree"),
        };
        var imports = typeof(OperatingSystemProvider).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => (Method: method, Import: method.GetCustomAttribute<DllImportAttribute>()))
            .Where(item => item.Import is not null).ToArray();
        Assert.Equal(allowed.Count, imports.Length);
        foreach (var (method, import) in imports)
        {
            var declaringType = method.DeclaringType!;
            var owner = declaringType.DeclaringType ?? declaringType;
            Assert.Contains((owner, import!.Value, import.EntryPoint ?? method.Name), allowed);
            Assert.False(method.IsPublic);
            Assert.False(declaringType.IsVisible);
            Assert.True(import.ExactSpelling);
            Assert.Equal(DllImportSearchPath.System32, method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()!.Paths);
        }
    }

    [Fact]
    public void ComInterfacesExposeOnlyTheReviewedConnectionAndReadSlots()
    {
        var allowed = new Dictionary<Type, (Guid Guid, int ReservedSlots, string Method, Type[] Parameters)>
        {
            [typeof(IStorageWbemLocator)] = (new("DC12A687-737F-11CF-884D-00AA004B2E24"), 0, "ConnectServer",
                [typeof(string), typeof(nint), typeof(nint), typeof(nint), typeof(int), typeof(nint), typeof(nint), typeof(IStorageWbemServices).MakeByRefType()]),
            [typeof(IStorageWbemServices)] = (new("9556DC99-828C-11CF-A37E-00AA003240C7"), 17, "ExecQuery",
                [typeof(string), typeof(string), typeof(int), typeof(nint), typeof(IStorageWbemEnumerator).MakeByRefType()]),
            [typeof(IStorageWbemEnumerator)] = (new("027947E1-D731-11CE-A357-000000000001"), 1, "Next",
                [typeof(int), typeof(uint), typeof(IStorageWbemObject).MakeByRefType(), typeof(uint).MakeByRefType()]),
            [typeof(IStorageWbemObject)] = (new("DC12A681-737F-11CF-884D-00AA004B2E24"), 1, "Get",
                [typeof(string), typeof(int), typeof(nint), typeof(int).MakeByRefType(), typeof(nint)]),
            // Three implicit IUnknown slots plus these 21 inert slots place the
            // fixed synchronous read at IWbemServices native slot 24.
            [typeof(ITpmWbemReadinessServices)] = (new("9556DC99-828C-11CF-A37E-00AA003240C7"), 21, "ExecMethod",
                [typeof(string), typeof(string), typeof(int), typeof(nint), typeof(nint), typeof(IStorageWbemObject).MakeByRefType(), typeof(nint)]),
        };
        var interfaces = typeof(StorageProvider).Assembly.GetTypes().Where(type => type.IsImport).ToArray();
        Assert.Equal(allowed.Count, interfaces.Length);
        foreach (var type in interfaces)
        {
            Assert.True(allowed.TryGetValue(type, out var contract));
            Assert.Equal(contract.Guid, type.GUID);
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
            if (type == typeof(ITpmWbemReadinessServices))
            {
                var parameters = read.GetParameters();
                Assert.Equal(UnmanagedType.BStr, parameters[0].GetCustomAttribute<MarshalAsAttribute>()!.Value);
                Assert.Equal(UnmanagedType.BStr, parameters[1].GetCustomAttribute<MarshalAsAttribute>()!.Value);
                Assert.True(parameters[5].IsOut);
                Assert.Equal(UnmanagedType.Interface, parameters[5].GetCustomAttribute<MarshalAsAttribute>()!.Value);
            }
        }
    }

    [Fact]
    public void DeviceEnumerationHasNoPublicIdentityQueryOrMutationSurface()
    {
        Type[] helpers =
        [
            typeof(IDeviceClassSource), typeof(IDeviceEnumerationSource), typeof(DeviceClassSnapshot),
            typeof(WindowsDeviceEnumerationSource), typeof(WindowsDeviceClassSource),
            typeof(WindowsDeviceClassNativeApi), typeof(WindowsNetworkDeviceSource),
        ];
        Assert.All(helpers, static type => Assert.False(type.IsVisible));
        Assert.Empty(Assert.Single(typeof(DeviceEnumerationProvider).GetConstructors()).GetParameters());
        var methods = typeof(DeviceEnumerationProvider).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(2, methods.Length);
        var observe = Assert.Single(methods, static method => method.Name == nameof(DeviceEnumerationProvider.ObserveAsync));
        Assert.Equal(typeof(CancellationToken), Assert.Single(observe.GetParameters()).ParameterType);
        Assert.Single(methods, static method => method.Name == "get_Descriptor" && method.GetParameters().Length == 0);
        Assert.All(typeof(DeviceClassSnapshot).GetProperties(), static property =>
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Assert.True(type.IsEnum || type == typeof(bool) || type == typeof(uint));
        });
    }

    [Fact]
    public void FirmwareSourcesQueriesAndNativeHelpersAreNotPublicExtensionPoints()
    {
        Type[] helpers =
        [
            typeof(IFirmwareSecuritySource), typeof(IFirmwarePlatformSource), typeof(IFirmwarePlatformNativeApi),
            typeof(IDeviceGuardSource), typeof(ITpmSource), typeof(WindowsFirmwareSecuritySource),
            typeof(WindowsFirmwarePlatformSource), typeof(WindowsFirmwarePlatformNativeApi),
            typeof(WindowsDeviceGuardSource), typeof(WindowsTpmSource), typeof(FirmwareWmiValueReader),
            typeof(SmbiosHardwareSecurityParser), typeof(ITpmWbemReadinessServices),
        ];
        Assert.All(helpers, static type => Assert.False(type.IsVisible));
        var constructor = Assert.Single(typeof(FirmwareSecurityProvider).GetConstructors());
        Assert.Empty(constructor.GetParameters());
        var methods = typeof(FirmwareSecurityProvider).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(2, methods.Length);
        var observe = Assert.Single(methods, static method => method.Name == nameof(FirmwareSecurityProvider.ObserveAsync));
        Assert.Equal(typeof(CancellationToken), Assert.Single(observe.GetParameters()).ParameterType);
        Assert.Single(methods, static method => method.Name == "get_Descriptor" && method.GetParameters().Length == 0);
    }

    [Fact]
    public void BatteryPathsCommandsAndNativeHelpersAreNotPublicExtensionPoints()
    {
        Type[] helpers =
        [
            typeof(IBatteryReportSource), typeof(IBatteryReportEnvironment), typeof(IBatteryReportWorkspace),
            typeof(IBatteryReportProcess), typeof(WindowsBatteryReportSource), typeof(WindowsBatteryReportEnvironment),
            typeof(WindowsSystemPowerSource),
            typeof(BatteryReportWorkspace), typeof(BatteryReportFiles), typeof(BatteryReportProcess),
            typeof(BatteryReportParser), typeof(BatteryReportSourceSnapshot), typeof(BatteryReportFileIdentity),
        ];
        Assert.All(helpers, static type => Assert.False(type.IsVisible));
        var constructor = Assert.Single(typeof(BatteryReportProvider).GetConstructors());
        Assert.Empty(constructor.GetParameters());
        var methods = typeof(BatteryReportProvider).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(2, methods.Length);
        var observe = Assert.Single(methods, static method => method.Name == nameof(BatteryReportProvider.ObserveAsync));
        Assert.Equal(typeof(CancellationToken), Assert.Single(observe.GetParameters()).ParameterType);
        Assert.Single(methods, static method => method.Name == "get_Descriptor" && method.GetParameters().Length == 0);
    }

    [Fact]
    public void AllWindowsSourceFilesExcludeGeneralExecutionNetworkMutationAndRawLogging()
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
            "ProcessStartInfo", "ShellExecute", "WinExec", "CreateProcessWith", "File.Move", "Directory.Move",
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var guardedSource = RemoveExactlyReviewedTpmRead(Path.GetFileName(file), source);
            foreach (var fragment in forbidden)
            {
                Assert.DoesNotContain(fragment, guardedSource, StringComparison.OrdinalIgnoreCase);
            }

            if (Path.GetFileName(file) != "BatteryReportProcess.cs")
            {
                foreach (var fragment in new[]
                {
                    "CreateProcessW(", "CreateJobObjectW(", "SetInformationJobObject(", "AssignProcessToJobObject(",
                    "ResumeThread(", "TerminateProcess(", "TerminateJobObject(", "powercfg.exe",
                })
                {
                    Assert.DoesNotContain(fragment, source, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (Path.GetFileName(file) != "BatteryReportWorkspace.cs")
            {
                foreach (var fragment in new[]
                {
                    "FileMode.Create", "FileMode.Append", "FileAccess.Write", "FileAccess.ReadWrite",
                    "FileOptions.DeleteOnClose", "new BinaryWriter(", "SetAccessControl(", "SetAccessRuleProtection(",
                    "DirectorySecurity", "FileSecurity", ").Create(",
                })
                {
                    Assert.DoesNotContain(fragment, source, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (Path.GetFileName(file) != "BatteryReportFiles.cs")
            {
                Assert.DoesNotContain("SetFileInformationByHandle(", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("CreateFileW(", source, StringComparison.OrdinalIgnoreCase);
            }

            if (Path.GetFileName(file) is not "BatteryReportFiles.cs" and not "BatteryReportWorkspace.cs")
            {
                Assert.DoesNotContain("MarkForDeletion(", source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string RemoveExactlyReviewedTpmRead(string fileName, string source)
    {
        var reviewed = fileName switch
        {
            "TpmWmiInterop.cs" => "int ExecMethod(",
            "WindowsTpmSource.cs" => "((ITpmWbemReadinessServices)services).ExecMethod(\"Win32_Tpm=@\", \"IsReadyInformation\", 0, 0, 0, out result, 0)",
            _ => null,
        };
        if (reviewed is null) { return source; }

        // Require one exact declaration/call before excluding it from the general
        // method-execution ban. Any extra call, input, object path, method or flag fails.
        Assert.Equal(2, source.Split(reviewed, StringSplitOptions.None).Length);
        return source.Replace(reviewed, "ReviewedTpmRead", StringComparison.Ordinal);
    }
}
