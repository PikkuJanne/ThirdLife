using System.Runtime.InteropServices;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Packages.WindowsUpdates;

/// <summary>The unelevated product surface for the Windows-configured update service.</summary>
public sealed class WindowsUpdateAgent : IWindowsUpdateAgent
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(2);
    private static int workerActive;

    public Task<WindowsUpdateScan> ScanAsync(CancellationToken cancellationToken) =>
        RunAsync(WuaSession.Scan, status => new WindowsUpdateScan(status, [], null), ScanTimeout, cancellationToken);

    // WUA restricts downloading and installing. The ordinary UI has no qualified
    // privileged route. Keep both entry points closed even if the process is elevated.
    public Task<WindowsUpdateOperation> DownloadAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
        Unavailable(exact, cancellationToken);

    public Task<WindowsUpdateOperation> InstallAsync(WindowsUpdateCandidate exact, CancellationToken cancellationToken) =>
        Unavailable(exact, cancellationToken);

    private static Task<WindowsUpdateOperation> Unavailable(WindowsUpdateCandidate exact, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(exact);
        return Task.FromResult(new WindowsUpdateOperation(
            token.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.Unavailable, null));
    }

    // A future qualified broker must verify a durable approval, journal intent and
    // recheck resources before wiring the protected operation into its dispatch table.
    internal static Task<WindowsUpdateOperation> RunPreparedOperationAsync(
        WindowsUpdateCandidate exact, bool install, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exact);
        return RunAsync(token => WuaSession.Apply(exact, install, token),
            status => new WindowsUpdateOperation(status, null), TimeSpan.FromMinutes(30), cancellationToken);
    }

    private static async Task<T> RunAsync<T>(Func<CancellationToken, T> read, Func<WindowsUpdateStatus, T> failure,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) { return failure(WindowsUpdateStatus.Cancelled); }
        if (Interlocked.CompareExchange(ref workerActive, 1, 0) != 0) { return failure(WindowsUpdateStatus.Unavailable); }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout);
        var token = lifetime.Token;
        var worker = Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
                    WindowsPlatform.CheckCurrent() != WindowsPlatformEligibility.Supported)
                {
                    return failure(WindowsUpdateStatus.Unavailable);
                }

                return read(token);
            }
            catch (OperationCanceledException)
            {
                return failure(cancellationToken.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.TimedOut);
            }
            catch (Exception error)
            {
                return failure(WuaSession.Failure(error));
            }
            finally
            {
                // A stalled native activation, abort or cleanup retains the one worker.
                lifetime.Dispose();
                Volatile.Write(ref workerActive, 0);
            }
        }, CancellationToken.None);
        try { return await worker.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            return failure(cancellationToken.IsCancellationRequested ? WindowsUpdateStatus.Cancelled : WindowsUpdateStatus.TimedOut);
        }
    }
}
