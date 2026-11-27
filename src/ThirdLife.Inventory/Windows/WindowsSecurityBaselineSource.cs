using System.Runtime.InteropServices;
using SourceValue = ThirdLife.Inventory.Windows.SystemInventorySourceValue<string>;

namespace ThirdLife.Inventory.Windows;

internal static class WindowsSecurityBaselineSource
{
    internal static SourceValue Read(SecurityCheck check, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) { return SourceValue.Unavailable(); }
        try
        {
            return check switch
            {
                SecurityCheck.DefenderHealth or SecurityCheck.SystemVolumeProtection =>
                    SecurityWmiSource.Read(check == SecurityCheck.DefenderHealth, cancellationToken),
                SecurityCheck.FirewallHealth => ReadWsc(0x01),
                SecurityCheck.AntivirusHealth => ReadWsc(0x04),
                SecurityCheck.UacHealth => ReadWsc(0x20),
                SecurityCheck.UacEnabled => ReadSwitch(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", "disabled", "enabled"),
                SecurityCheck.DomainJoined => ReadDomainJoin(),
                SecurityCheck.MdmRegistered => ReadMdmRegistration(),
                SecurityCheck.CloudJoin => ReadCloudJoin(),
                SecurityCheck.RemoteDesktopConnections => ReadSwitch(
                    @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", "allowed", "denied"),
                SecurityCheck.NtpClientEnabled => ReadSwitch(
                    @"SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient", "Enabled", "disabled", "enabled"),
                _ => throw new ArgumentOutOfRangeException(nameof(check)),
            };
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return SourceValue.Unavailable();
        }
    }

    private static SourceValue ReadWsc(uint category)
    {
        int status = Native.WscGetSecurityProviderHealth(category, out int health);
        return MapHealth(status, health);
    }

    internal static SourceValue MapHealth(int status, int health) => status != 0
        ? Failure(status) : health switch
        {
            0 => SourceValue.Available("good"),
            1 => SourceValue.Available("not_monitored"),
            2 => SourceValue.Available("poor"),
            3 => SourceValue.Available("snoozed"),
            _ => SourceValue.InvalidData(),
        };

    private static SourceValue ReadSwitch(string subkey, string name, string zero, string one)
    {
        // A single DWORD from the native 64-bit view. Never size a buffer from registry content.
        var localMachine = new nint(unchecked((int)0x80000002));
        uint size = sizeof(int);
        int status = Native.RegGetValueW(localMachine, subkey, name, 0x00010010, out var type, out int value, ref size);
        if (status != 0) { return Failure(status); }
        return type == 4 && size == sizeof(int) && value is 0 or 1
            ? SourceValue.Available(value == 0 ? zero : one) : SourceValue.InvalidData();
    }

    private static SourceValue ReadDomainJoin()
    {
        int status = Native.NetGetJoinInformation(0, out nint name, out int join);
        // The API allocates the name. Release it without reading or converting it.
        if (name != 0 && Native.NetApiBufferFree(name) != 0)
        {
            throw new SecuritySourceCleanupException();
        }
        return status != 0 ? Failure(status) : join switch
        {
            1 => SourceValue.Available("unjoined"),
            2 => SourceValue.Available("workgroup"),
            3 => SourceValue.Available("domain_joined"),
            0 => SourceValue.Unavailable(),
            _ => SourceValue.InvalidData(),
        };
    }

    private static SourceValue ReadCloudJoin()
    {
        int status = Native.NetGetAadJoinInformation(0, out nint info);
        try
        {
            if (status != 0) { return Failure(status); }
            if (info == 0) { return SourceValue.Available("no_join_reported"); }
            // DSREG_JOIN_INFO starts with DSREG_JOIN_TYPE. No embedded identity is read.
            return Marshal.ReadInt32(info) switch
            {
                1 => SourceValue.Available("device_joined"),
                2 => SourceValue.Available("workplace_joined"),
                0 => SourceValue.Unavailable(),
                _ => SourceValue.InvalidData(),
            };
        }
        finally
        {
            if (info != 0) { Native.NetFreeAadJoinInformation(info); }
        }
    }

    private static SourceValue ReadMdmRegistration()
    {
        int status = Native.IsDeviceRegisteredWithManagement(out int registered, 0, 0);
        return status != 0 ? Failure(status) : SourceValue.Available(registered == 0 ? "not_registered" : "registered");
    }

    private static SourceValue Failure(int status) => status switch
    {
        5 or unchecked((int)0x80070005) => SourceValue.AccessDenied(),
        1630 or 234 => SourceValue.InvalidData(),
        _ => SourceValue.Unavailable(),
    };

    private static class Native
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("wscapi.dll", ExactSpelling = true)]
        internal static extern int WscGetSecurityProviderHealth(uint providers, out int health);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true)]
        internal static extern int NetGetJoinInformation(nint local, out nint name, out int status);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true)]
        internal static extern int NetApiBufferFree(nint buffer);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true)]
        internal static extern int NetGetAadJoinInformation(nint tenant, out nint info);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("netapi32.dll", ExactSpelling = true)]
        internal static extern void NetFreeAadJoinInformation(nint info);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("MDMRegistration.dll", ExactSpelling = true)]
        internal static extern int IsDeviceRegisteredWithManagement(out int registered, uint characters, nint user);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int RegGetValueW(nint key, string subkey, string value, uint flags,
            out uint type, out int data, ref uint size);
    }
}

internal sealed class SecuritySourceCleanupException : Exception;
