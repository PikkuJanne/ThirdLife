using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Inventory.Windows;

internal sealed class PendingRebootSnapshot
{
    public PendingRebootSnapshot(
        SystemInventorySourceValue<bool> componentServicing,
        SystemInventorySourceValue<bool> windowsUpdate,
        SystemInventorySourceValue<bool> fileRename)
    {
        ComponentServicing = componentServicing ?? throw new ArgumentNullException(nameof(componentServicing));
        WindowsUpdate = windowsUpdate ?? throw new ArgumentNullException(nameof(windowsUpdate));
        FileRename = fileRename ?? throw new ArgumentNullException(nameof(fileRename));
    }

    public SystemInventorySourceValue<bool> ComponentServicing { get; }

    public SystemInventorySourceValue<bool> WindowsUpdate { get; }

    public SystemInventorySourceValue<bool> FileRename { get; }
}

internal interface IPendingRebootSource
{
    PendingRebootSnapshot Read(CancellationToken cancellationToken);
}

internal sealed class WindowsPendingRebootSource : IPendingRebootSource
{
    internal const uint MaximumRenameMetadataBytes = 1_048_576;

    private const string ComponentServicingKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string WindowsUpdateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string PendingRenameValue = "PendingFileRenameOperations";
    private const int QueryValueWith64BitView = 0x0001 | 0x0100;
    private static readonly nint LocalMachine = new(unchecked((int)0x80000002));

    public PendingRebootSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new PendingRebootSnapshot(
                SystemInventorySourceValue<bool>.Unavailable(),
                SystemInventorySourceValue<bool>.Unavailable(),
                SystemInventorySourceValue<bool>.Unavailable());
        }

        var componentServicing = ReadKeyPresence(ComponentServicingKey);
        cancellationToken.ThrowIfCancellationRequested();
        var windowsUpdate = ReadKeyPresence(WindowsUpdateKey);
        cancellationToken.ThrowIfCancellationRequested();
        var fileRename = ReadRenameMetadata();
        cancellationToken.ThrowIfCancellationRequested();
        return new PendingRebootSnapshot(componentServicing, windowsUpdate, fileRename);
    }

    private static SystemInventorySourceValue<bool> ReadKeyPresence(string fixedSubKey)
    {
        var result = NativeMethods.RegOpenKeyExW(
            LocalMachine, fixedSubKey, 0, QueryValueWith64BitView, out var key);
        using (key)
        {
            return MapKeyPresence(result);
        }
    }

    private static SystemInventorySourceValue<bool> ReadRenameMetadata()
    {
        var result = NativeMethods.RegOpenKeyExW(
            LocalMachine, SessionManagerKey, 0, QueryValueWith64BitView, out var key);
        using (key)
        {
            if (result != 0)
            {
                // A missing container is unavailable; it does not establish value absence.
                return MapFailure(result);
            }

            uint byteCount = 0;
            result = NativeMethods.RegQueryValueExW(
                key,
                PendingRenameValue,
                nint.Zero,
                out var valueType,
                nint.Zero,
                ref byteCount);
            return MapRenameMetadata(result, valueType, byteCount);
        }
    }

    internal static SystemInventorySourceValue<bool> MapKeyPresence(int result) => result switch
    {
        0 => SystemInventorySourceValue<bool>.Available(true),
        2 or 3 => SystemInventorySourceValue<bool>.Available(false),
        _ => MapFailure(result),
    };

    internal static SystemInventorySourceValue<bool> MapRenameMetadata(int result, uint valueType, uint byteCount)
    {
        if (result == 2)
        {
            return SystemInventorySourceValue<bool>.Available(false);
        }

        if (result != 0)
        {
            return MapFailure(result);
        }

        // Only type and length are read. No path strings or value buffer are allocated.
        // Plausible metadata indicates presence, including an empty-looking value; it
        // cannot establish that the list contains valid operations or that reboot is required.
        return valueType == 7 && byteCount >= 4 && byteCount <= MaximumRenameMetadataBytes && byteCount % 2 == 0
            ? SystemInventorySourceValue<bool>.Available(true)
            : SystemInventorySourceValue<bool>.InvalidData();
    }

    private static SystemInventorySourceValue<bool> MapFailure(int result) => result switch
    {
        5 => SystemInventorySourceValue<bool>.AccessDenied(),
        1 or 2 or 3 or 50 or 120 or 1168 => SystemInventorySourceValue<bool>.Unavailable(),
        _ => SystemInventorySourceValue<bool>.InvalidData(),
    };

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int RegOpenKeyExW(
            nint hKey,
            string lpSubKey,
            uint ulOptions,
            int samDesired,
            out SafeRegistryHandle phkResult);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int RegQueryValueExW(
            SafeRegistryHandle hKey,
            string lpValueName,
            nint lpReserved,
            out uint lpType,
            nint lpData,
            ref uint lpcbData);
    }
}
