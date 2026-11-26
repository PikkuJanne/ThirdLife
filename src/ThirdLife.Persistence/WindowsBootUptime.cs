using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ThirdLife.Persistence;

[SupportedOSPlatform("windows")]
internal static class WindowsBootUptime
{
    internal static ulong? Read()
    {
        try
        {
            QueryInterruptTimePrecise(out var uptime);
            return uptime;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    // Biased interrupt time includes sleep and hibernation and is independent of wall-clock adjustments.
    [DllImport("api-ms-win-core-realtime-l1-1-1.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void QueryInterruptTimePrecise(out ulong interruptTime);
}
