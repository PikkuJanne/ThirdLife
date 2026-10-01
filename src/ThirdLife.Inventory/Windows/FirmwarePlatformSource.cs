using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Inventory.Windows;

internal sealed record FirmwarePlatformSourceSnapshot(
    SystemInventorySourceValue<uint> FirmwareType,
    SystemInventorySourceValue<uint> SecureBootRegistryValue,
    SystemInventorySourceValue<byte> HardwareSecuritySettings,
    uint? FirmwareTypeNativeErrorCode = null,
    uint? SecureBootNativeErrorCode = null);

internal interface IFirmwarePlatformSource
{
    FirmwarePlatformSourceSnapshot Read(CancellationToken cancellationToken);
}

internal sealed record FirmwarePlatformNativeValue(SystemInventorySourceValue<uint> Value, uint? NativeErrorCode = null);

internal interface IFirmwarePlatformNativeApi
{
    FirmwarePlatformNativeValue ReadFirmwareType();
    FirmwarePlatformNativeValue ReadSecureBootRegistryValue();
    SystemInventorySourceValue<byte[]> ReadFirmwareTable();
}

internal sealed class WindowsFirmwarePlatformSource : IFirmwarePlatformSource
{
    private readonly IFirmwarePlatformNativeApi _native;

    public WindowsFirmwarePlatformSource() : this(new WindowsFirmwarePlatformNativeApi()) { }

    internal WindowsFirmwarePlatformSource(IFirmwarePlatformNativeApi native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    public FirmwarePlatformSourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = _native.ReadFirmwareType();
        cancellationToken.ThrowIfCancellationRequested();
        var secureBoot = _native.ReadSecureBootRegistryValue();
        cancellationToken.ThrowIfCancellationRequested();
        var table = _native.ReadFirmwareTable();
        var settings = ReadHardwareSecurityAndClear(table, cancellationToken);
        return new(mode.Value, secureBoot.Value, settings, mode.NativeErrorCode, secureBoot.NativeErrorCode);
    }

    internal static SystemInventorySourceValue<byte> ReadHardwareSecurityAndClear(
        SystemInventorySourceValue<byte[]> table, CancellationToken cancellationToken)
    {
        if (table.Status != SystemInventorySourceStatus.Available)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return table.Status switch
            {
                SystemInventorySourceStatus.AccessDenied => SystemInventorySourceValue<byte>.AccessDenied(),
                SystemInventorySourceStatus.InvalidData => SystemInventorySourceValue<byte>.InvalidData(),
                _ => SystemInventorySourceValue<byte>.Unavailable(),
            };
        }

        var bytes = table.GetRequiredValue();
        try
        {
            return SmbiosHardwareSecurityParser.Parse(bytes, cancellationToken);
        }
        finally
        {
            // The raw table can contain identifiers. No strings are decoded or retained.
            Array.Clear(bytes);
        }
    }
}

internal sealed class WindowsFirmwarePlatformNativeApi : IFirmwarePlatformNativeApi
{
    internal const string SecureBootKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    internal const string SecureBootValue = "UEFISecureBootEnabled";
    private const int QueryValueWith64BitView = 0x0001 | 0x0100;
    private static readonly nint LocalMachine = new(unchecked((int)0x80000002));

    public FirmwarePlatformNativeValue ReadFirmwareType()
    {
        if (!OperatingSystem.IsWindows()) { return new(SystemInventorySourceValue<uint>.Unavailable()); }
        try
        {
            return NativeMethods.GetFirmwareType(out var type)
                ? new(SystemInventorySourceValue<uint>.Available(type))
                : Failure(unchecked((uint)Marshal.GetLastPInvokeError()));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return new(SystemInventorySourceValue<uint>.Unavailable());
        }
    }

    public FirmwarePlatformNativeValue ReadSecureBootRegistryValue()
    {
        if (!OperatingSystem.IsWindows()) { return new(SystemInventorySourceValue<uint>.Unavailable()); }
        try
        {
            var status = NativeMethods.RegOpenKeyExW(LocalMachine, SecureBootKey, 0, QueryValueWith64BitView, out var key);
            using (key)
            {
                if (status != 0) { return Failure(unchecked((uint)status)); }
                uint bytes = sizeof(uint);
                status = NativeMethods.RegQueryValueExW(key, SecureBootValue, 0, out var type, out var value, ref bytes);
                return MapRegistryValue(status, type, bytes, value);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return new(SystemInventorySourceValue<uint>.Unavailable());
        }
    }

    public SystemInventorySourceValue<byte[]> ReadFirmwareTable() => OperatingSystem.IsWindows()
        ? WindowsSystemInventorySource.ReadFirmwareTable()
        : SystemInventorySourceValue<byte[]>.Unavailable();

    internal static FirmwarePlatformNativeValue MapRegistryValue(int status, uint type, uint bytes, uint value)
    {
        if (status != 0) { return Failure(unchecked((uint)status)); }
        return new(type == 4 && bytes == sizeof(uint) && value <= 1
            ? SystemInventorySourceValue<uint>.Available(value)
            : SystemInventorySourceValue<uint>.InvalidData());
    }

    internal static FirmwarePlatformNativeValue Failure(uint code) => new(code switch
    {
        5 or 1314 => SystemInventorySourceValue<uint>.AccessDenied(),
        1 or 2 or 3 or 50 or 120 or 1168 => SystemInventorySourceValue<uint>.Unavailable(),
        _ => SystemInventorySourceValue<uint>.InvalidData(),
    }, code);

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFirmwareType(out uint firmwareType);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int RegOpenKeyExW(nint root, string subKey, uint options, int access, out SafeRegistryHandle key);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int RegQueryValueExW(SafeRegistryHandle key, string valueName, nint reserved,
            out uint type, out uint data, ref uint bytes);
    }
}
