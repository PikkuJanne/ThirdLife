using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal static class WindowsEventLogNative
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wevtapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint EvtQuery(nint session, string path, string query, int flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wevtapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EvtNext(nint query, int capacity, out nint record, int timeout, int flags, out int returned);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wevtapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint EvtCreateRenderContext(int count,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] paths, int flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wevtapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EvtRender(nint context, nint record, int flags, int capacity,
        nint buffer, out int used, out int count);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wevtapi.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EvtClose(nint handle);
}
