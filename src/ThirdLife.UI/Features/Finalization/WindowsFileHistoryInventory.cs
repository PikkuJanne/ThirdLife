using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using ThirdLife.Core.Handover;
using ThirdLife.Inventory.Windows;
using ThirdLife.UI.Shell;

namespace ThirdLife.UI.Features.Finalization;

internal sealed class WindowsFileHistoryInventory
{
    private Guid? _volumeIdentity;
    private Guid _binding;

    public static bool RouteAvailable() => WindowsPlatform.CheckCurrent() == WindowsPlatformEligibility.Supported &&
        StartupSafety.AllowsShell(StartupSafety.CheckCurrentProcess()) &&
        File.Exists(Path.Combine(Environment.SystemDirectory, "control.exe")) &&
        File.Exists(Path.Combine(Environment.SystemDirectory, "fhcpl.dll"));

    public FileHistoryDestinationObservation? InspectDestination(string volumeRoot)
    {
        if (!RouteAvailable() || volumeRoot is not { Length: 3 } || !char.IsAsciiLetter(volumeRoot[0]) ||
            volumeRoot[1] != ':' || volumeRoot[2] != Path.DirectorySeparatorChar)
        {
            return null;
        }

        try
        {
            var drive = new DriveInfo(volumeRoot);
            var systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady ||
                systemVolume is null || ReadVolumeIdentity(systemVolume) is not { } systemIdentity ||
                ReadVolumeIdentity(volumeRoot) is not { } destinationIdentity || destinationIdentity == systemIdentity)
            {
                return null;
            }
            if (_volumeIdentity != destinationIdentity)
            {
                _volumeIdentity = destinationIdentity;
                _binding = Guid.NewGuid();
            }
            return new(_binding, eligible: true, drive.AvailableFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    private static Guid? ReadVolumeIdentity(string volumeRoot)
    {
        var result = new char[64];
        if (!GetVolumeNameForVolumeMountPoint(volumeRoot, result, (uint)result.Length)) { return null; }
        const string prefix = @"\\?\Volume{";
        var length = Array.IndexOf(result, '\0');
        if (length <= 0) { return null; }
        var name = result.AsSpan(0, length);
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length == prefix.Length + 38 &&
            Guid.TryParseExact(name.Slice(prefix.Length, 36), "D", out var identity) ? identity : null;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string volumeRoot, [Out] char[] volumeName, uint volumeNameLength);
}
