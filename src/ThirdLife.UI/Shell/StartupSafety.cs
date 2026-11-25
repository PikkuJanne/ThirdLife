using System.Security.Principal;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.UI.Shell;

public enum StartupAccess
{
    Unknown,
    StandardUser,
    Elevated,
}

public static class StartupSafety
{
    public const string RecoveryMessage = "ThirdLife could not confirm a standard-user session. Close this window and open ThirdLife normally from your desktop, without Run as administrator. No job was opened and no assessment was started.";

    public static bool AllowsShell(StartupAccess access) => access == StartupAccess.StandardUser;

    internal static string? OpenShell(WindowsPlatformEligibility platform, StartupAccess access, Action openShell)
    {
        switch (platform)
        {
            case WindowsPlatformEligibility.Supported:
                break;
            case WindowsPlatformEligibility.UnsupportedOperatingSystem:
                return "ThirdLife requires Windows 11 on an x64 PC. This operating system is outside that scope.";
            case WindowsPlatformEligibility.UnsupportedArchitecture:
                return "ThirdLife requires Windows 11 on an x64 PC. This PC uses a different system architecture.";
            default:
                return "ThirdLife could not verify that this PC is running Windows 11 on x64. Close ThirdLife and check Windows system information.";
        }

        if (!AllowsShell(access))
        {
            return RecoveryMessage;
        }

        openShell();
        return null;
    }

    public static StartupAccess CheckCurrentProcess()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                ? StartupAccess.Elevated : StartupAccess.StandardUser;
        }
        catch (Exception)
        {
            // Refuse startup without retaining identity, paths or exception text.
            return StartupAccess.Unknown;
        }
    }
}
