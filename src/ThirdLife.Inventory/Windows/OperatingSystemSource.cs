using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ThirdLife.Inventory.Windows;

internal sealed record OperatingSystemVersion(int Major, int Minor, int Build);

internal enum WindowsProductType : byte
{
    Workstation = 1,
    DomainController = 2,
    Server = 3,
}

internal sealed record OperatingSystemSnapshot(
    SystemInventorySourceValue<OperatingSystemVersion> Version,
    SystemInventorySourceValue<uint> Edition,
    SystemInventorySourceValue<ushort> Architecture,
    SystemInventorySourceValue<WindowsProductType> ProductType);

internal interface IOperatingSystemSource
{
    OperatingSystemSnapshot Read(CancellationToken cancellationToken);
}

internal sealed class WindowsOperatingSystemSource : IOperatingSystemSource
{
    public OperatingSystemSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new(
                SystemInventorySourceValue<OperatingSystemVersion>.Unavailable(),
                SystemInventorySourceValue<uint>.Unavailable(),
                SystemInventorySourceValue<ushort>.Unavailable(),
                SystemInventorySourceValue<WindowsProductType>.Unavailable());
        }

        // .NET 5+ reports the actual Windows version regardless of compatibility mode.
        // Only numeric version fields are copied; OS display strings are never collected.
        var version = Environment.OSVersion.Version;
        cancellationToken.ThrowIfCancellationRequested();
        var edition = NativeMethods.GetProductInfo(10, 0, 0, 0, out var sku)
            ? SystemInventorySourceValue<uint>.Available(sku)
            : SystemInventorySourceValue<uint>.Unavailable();
        cancellationToken.ThrowIfCancellationRequested();
        return new(
            SystemInventorySourceValue<OperatingSystemVersion>.Available(
                new(version.Major, version.Minor, version.Build)),
            edition,
            WindowsSystemInventorySource.MapOperatingSystemArchitecture(RuntimeInformation.OSArchitecture),
            ReadProductType());
    }

    internal static int? ReadBuildRevision()
    {
        // One fixed DWORD in the native Windows view. Never read a caller-supplied key or allocate registry data.
        const uint nativeDword = 0x00010010; // RRF_SUBKEY_WOW6464KEY | RRF_RT_REG_DWORD
        var localMachine = new nint(unchecked((int)0x80000002));
        uint size = sizeof(int);
        var result = NativeMethods.RegGetValueW(localMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", nativeDword,
            out var type, out var revision, ref size);
        return result == 0 && type == 4 && size == sizeof(int) && revision >= 0 ? revision : null;
    }

    [SupportedOSPlatform("windows")]
    private static SystemInventorySourceValue<WindowsProductType> ReadProductType()
    {
        const uint ProductTypeMask = 0x80; // VER_PRODUCT_TYPE
        const byte Equal = 1; // VER_EQUAL
        const int NoMatch = 1150; // ERROR_OLD_WIN_VERSION
        var condition = NativeMethods.VerSetConditionMask(0, ProductTypeMask, Equal);
        var version = new NativeOsVersionInfo
        {
            Size = (uint)Marshal.SizeOf<NativeOsVersionInfo>(),
        };
        // The Version Helper for IsWindowsServer uses this product-type comparison.
        // Check for positive identification of each role and treat API errors as unknown.
        // Version fields are intentionally excluded from the manifest-sensitive comparison.
        foreach (var productType in new[] { WindowsProductType.Workstation, WindowsProductType.Server, WindowsProductType.DomainController })
        {
            version.ProductType = productType;
            if (NativeMethods.VerifyVersionInfo(ref version, ProductTypeMask, condition))
            {
                return SystemInventorySourceValue<WindowsProductType>.Available(productType);
            }

            if (Marshal.GetLastPInvokeError() != NoMatch)
            {
                return SystemInventorySourceValue<WindowsProductType>.Unavailable();
            }
        }

        return SystemInventorySourceValue<WindowsProductType>.InvalidData();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeOsVersionInfo
    {
        internal uint Size;
        internal uint MajorVersion;
        internal uint MinorVersion;
        internal uint BuildNumber;
        internal uint PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string? ServicePackDescription;
        internal ushort ServicePackMajor;
        internal ushort ServicePackMinor;
        internal ushort SuiteMask;
        internal WindowsProductType ProductType;
        internal byte Reserved;
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int RegGetValueW(nint key, string subkey, string value, uint flags,
            out uint type, out int data, ref uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProductInfo(
            uint majorVersion,
            uint minorVersion,
            uint servicePackMajor,
            uint servicePackMinor,
            out uint productType);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", EntryPoint = "VerifyVersionInfoW", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VerifyVersionInfo(ref NativeOsVersionInfo version, uint typeMask, ulong conditionMask);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern ulong VerSetConditionMask(ulong conditionMask, uint typeMask, byte condition);
    }
}
