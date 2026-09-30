using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal sealed record OperatingSystemVersion(int Major, int Minor, int Build);

internal sealed record OperatingSystemSnapshot(
    SystemInventorySourceValue<OperatingSystemVersion> Version,
    SystemInventorySourceValue<uint> Edition,
    SystemInventorySourceValue<ushort> Architecture);

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
                SystemInventorySourceValue<ushort>.Unavailable());
        }

        // .NET 5+ reports the actual Windows version regardless of compatibility mode.
        // Only numeric version fields are copied; OS display strings are never collected.
        var version = Environment.OSVersion.Version;
        cancellationToken.ThrowIfCancellationRequested();
        var edition = NativeMethods.GetProductInfo(10, 0, 0, 0, out var productType)
            ? SystemInventorySourceValue<uint>.Available(productType)
            : SystemInventorySourceValue<uint>.Unavailable();
        cancellationToken.ThrowIfCancellationRequested();
        return new(
            SystemInventorySourceValue<OperatingSystemVersion>.Available(
                new(version.Major, version.Minor, version.Build)),
            edition,
            WindowsSystemInventorySource.MapOperatingSystemArchitecture(RuntimeInformation.OSArchitecture));
    }

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProductInfo(
            uint majorVersion,
            uint minorVersion,
            uint servicePackMajor,
            uint servicePackMinor,
            out uint productType);
    }
}
