using System.Security.Principal;

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
