using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Broker.Protocol;
using ThirdLife.Broker.Protocol.Transport;

namespace ThirdLife.UI.BrokerClient;

internal static class TrustedBrokerLauncher
{
    internal static Task<Process?> LaunchAsync(BrokerLaunchTicket ticket, CancellationToken cancellationToken) =>
        LaunchAsync(ticket, InstalledBroker.TryOpen, StartExactImage, cancellationToken);

    internal static Task<Process?> LaunchAsync(BrokerLaunchTicket ticket,
        Func<InstalledBroker?> openInstallation, Func<ProcessStartInfo, Process?> startProcess,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsExpired(ticket)) { return null; }
        using var broker = openInstallation();
        cancellationToken.ThrowIfCancellationRequested();
        return broker is null || IsExpired(ticket) ? null : startProcess(CreateStartInfo(ticket, broker.Executable));
    }, cancellationToken);

    internal static ProcessStartInfo CreateStartInfo(BrokerLaunchTicket ticket, string executable)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        foreach (var argument in ticket.CreateArguments()) { info.ArgumentList.Add(argument); }
        return info;
    }

    private static Process? StartExactImage(ProcessStartInfo info)
    {
        var process = Process.Start(info);
        if (process is null) { return null; }

        var exactImage = false;
        try
        {
            var path = new char[512];
            var length = (uint)path.Length;
            exactImage = QueryFullProcessImageName(process.SafeHandle, 0, path, ref length) &&
                length < path.Length &&
                path.AsSpan(0, (int)length).Equals(info.FileName.AsSpan(), StringComparison.OrdinalIgnoreCase);
            return exactImage ? process : null;
        }
        finally
        {
            if (!exactImage) { process.Dispose(); }
        }
    }

    private static bool IsExpired(BrokerLaunchTicket ticket) =>
        !BrokerProtocolLimits.IsWithinWindow(ticket.ExpiresAtUtc, TimeProvider.System.GetUtcNow());

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] path, ref uint size);
}
