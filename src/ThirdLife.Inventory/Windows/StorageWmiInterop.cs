using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

// Interface order follows wbemcli.h. Reserved slots are never called and deliberately
// expose no mutation signature. The only callable WMI operations are connection and reads.
[ComImport]
[Guid("DC12A687-737F-11CF-884D-00AA004B2E24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStorageWbemLocator
{
    [PreserveSig]
    int ConnectServer(
        [MarshalAs(UnmanagedType.BStr)] string resource,
        nint user,
        nint password,
        nint locale,
        int flags,
        nint authority,
        nint context,
        [MarshalAs(UnmanagedType.Interface)] out IStorageWbemServices? services);
}

[ComImport]
[Guid("9556DC99-828C-11CF-A37E-00AA003240C7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStorageWbemServices
{
    void Reserved01();
    void Reserved02();
    void Reserved03();
    void Reserved04();
    void Reserved05();
    void Reserved06();
    void Reserved07();
    void Reserved08();
    void Reserved09();
    void Reserved10();
    void Reserved11();
    void Reserved12();
    void Reserved13();
    void Reserved14();
    void Reserved15();
    void Reserved16();
    void Reserved17();

    [PreserveSig]
    int ExecQuery(
        [MarshalAs(UnmanagedType.BStr)] string language,
        [MarshalAs(UnmanagedType.BStr)] string query,
        int flags,
        nint context,
        [MarshalAs(UnmanagedType.Interface)] out IStorageWbemEnumerator? enumerator);
}

[ComImport]
[Guid("027947E1-D731-11CE-A357-000000000001")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStorageWbemEnumerator
{
    void Reserved01();

    [PreserveSig]
    int Next(
        int timeoutMilliseconds,
        uint count,
        [MarshalAs(UnmanagedType.Interface)] out IStorageWbemObject? value,
        out uint returned);
}

[ComImport]
[Guid("DC12A681-737F-11CF-884D-00AA004B2E24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStorageWbemObject
{
    void Reserved01();

    [PreserveSig]
    int Get(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        int flags,
        nint variant,
        out int cimType,
        nint flavor);
}

internal static class StorageWmiNativeMethods
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int CoInitializeEx(nint reserved, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern void CoUninitialize();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int CoCreateInstance(
        in Guid classId,
        nint outer,
        uint context,
        in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IStorageWbemLocator? locator);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int CoSetProxyBlanket(
        [MarshalAs(UnmanagedType.IUnknown)] object proxy,
        uint authenticationService,
        uint authorizationService,
        nint principal,
        uint authenticationLevel,
        uint impersonationLevel,
        nint authenticationInfo,
        uint capabilities);

    // WMI security applies to each proxy interface. The boot storage queries must
    // set packet privacy on the service and enumerator interfaces they actually call.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true, EntryPoint = "CoSetProxyBlanket")]
    internal static extern int SetServicesProxyBlanket(
        [MarshalAs(UnmanagedType.Interface)] IStorageWbemServices proxy,
        uint authenticationService,
        uint authorizationService,
        nint principal,
        uint authenticationLevel,
        uint impersonationLevel,
        nint authenticationInfo,
        uint capabilities);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("ole32.dll", ExactSpelling = true, EntryPoint = "CoSetProxyBlanket")]
    internal static extern int SetEnumeratorProxyBlanket(
        [MarshalAs(UnmanagedType.Interface)] IStorageWbemEnumerator proxy,
        uint authenticationService,
        uint authorizationService,
        nint principal,
        uint authenticationLevel,
        uint impersonationLevel,
        nint authenticationInfo,
        uint capabilities);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern uint SysStringLen(nint value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern int VariantClear(nint variant);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern uint SafeArrayGetDim(nint array);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern int SafeArrayGetVartype(nint array, out ushort variantType);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern uint SafeArrayGetElemsize(nint array);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern int SafeArrayGetLBound(nint array, uint dimension, out int lowerBound);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern int SafeArrayGetUBound(nint array, uint dimension, out int upperBound);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    internal static extern int SafeArrayGetElement(nint array, in int index, out int value);
}
