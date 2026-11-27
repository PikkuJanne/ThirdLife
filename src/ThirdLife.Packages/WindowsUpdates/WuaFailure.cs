using ThirdLife.Core.WindowsUpdates;

namespace ThirdLife.Packages.WindowsUpdates;

internal static class WuaFailure
{
    internal static WindowsUpdateStatus FromHResult(int code) => unchecked((uint)code) switch
    {
        0x80070005 or 0x80240044 => WindowsUpdateStatus.AccessDenied,
        0x80070422 or 0x8024001e or 0x8024002e or 0x8024a000 => WindowsUpdateStatus.ServiceUnavailable,
        0x80072ee7 or 0x80072efd or 0x8024001f or 0x8024402c => WindowsUpdateStatus.Offline,
        0x80072ee2 => WindowsUpdateStatus.TimedOut,
        0x800704c7 or 0x8024000b => WindowsUpdateStatus.Cancelled,
        _ => WindowsUpdateStatus.Failed,
    };
}
