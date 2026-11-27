using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace ThirdLife.Persistence;

[SupportedOSPlatform("windows")]
internal static class WindowsBootUptime
{
    // Windows increments this local sequence at boot. It is independent of elapsed uptime,
    // wall-clock adjustments and the time at which the operator returns to the job.
    internal static uint? ReadBootSession()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
            return key?.GetValueKind("BootId") == RegistryValueKind.DWord && key.GetValue("BootId") is int sequence
                ? unchecked((uint)sequence) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

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
