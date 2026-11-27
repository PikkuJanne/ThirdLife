using System.Runtime.InteropServices;

namespace ThirdLife.Inventory.Windows;

internal static class WindowsSystemPowerSource
{
    internal static SystemPowerStatus? Read() =>
        OperatingSystem.IsWindows() && NativeMethods.GetSystemPowerStatus(out var status) ? status : null;

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
