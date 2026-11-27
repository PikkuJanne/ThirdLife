namespace ThirdLife.Inventory.Windows;

internal sealed class WindowsBatteryReportSource : IBatteryReportSource
{
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    internal const int PollMilliseconds = 100;
    private static readonly BatteryReportExecutionGate SharedGate = new();
    private readonly BatteryReportExecutionGate _gate;
    private readonly IBatteryReportEnvironment _environment;
    private readonly TimeProvider _time;

    public WindowsBatteryReportSource()
        : this(new WindowsBatteryReportEnvironment(), TimeProvider.System, SharedGate)
    {
    }

    internal WindowsBatteryReportSource(IBatteryReportEnvironment environment, TimeProvider time,
        BatteryReportExecutionGate? gate = null)
    {
        _environment = environment;
        _time = time;
        _gate = gate ?? new();
    }

    public BatteryReportSourceSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _gate.CleanupBlocked) != 0)
        {
            return new(BatteryReportSourceStatus.CleanupIncomplete, CleanupComplete: false);
        }

        if (Interlocked.CompareExchange(ref _gate.Active, 1, 0) != 0)
        {
            return new(BatteryReportSourceStatus.Unavailable);
        }

        if (Volatile.Read(ref _gate.CleanupBlocked) != 0)
        {
            Volatile.Write(ref _gate.Active, 0);
            return new(BatteryReportSourceStatus.CleanupIncomplete, CleanupComplete: false);
        }

        IBatteryReportWorkspace? workspace = null;
        IBatteryReportProcess? process = null;
        var processExited = false;
        var cleanup = new BatteryReportCleanup(true);
        BatteryReportSourceSnapshot result;
        try
        {
            workspace = _environment.CreateWorkspace();
            cancellationToken.ThrowIfCancellationRequested();
            if (_environment.HasNoSystemBattery())
            {
                result = new(BatteryReportSourceStatus.NoBattery);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                process = _environment.StartProcess(workspace);
                var start = _time.GetTimestamp();
                while (!processExited && _time.GetElapsedTime(start) < CommandTimeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (workspace.ReportLength > BatteryReportParser.MaximumReportBytes)
                    {
                        break;
                    }

                    processExited = process.WaitForExit(PollMilliseconds);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (workspace.ReportLength > BatteryReportParser.MaximumReportBytes)
                {
                    result = new(BatteryReportSourceStatus.InvalidData);
                }
                else if (!processExited)
                {
                    result = new(BatteryReportSourceStatus.TimedOut);
                }
                else if (process.ExitCode != 0)
                {
                    result = new(BatteryReportSourceStatus.Failed, ExitCode: process.ExitCode);
                }
                else
                {
                    using var report = workspace.OpenReport();
                    var parsed = BatteryReportParser.Parse(report, cancellationToken);
                    result = new(parsed.Status switch
                    {
                        BatteryReportParseStatus.Collected => BatteryReportSourceStatus.Collected,
                        BatteryReportParseStatus.NoBattery => BatteryReportSourceStatus.NoBattery,
                        _ => BatteryReportSourceStatus.InvalidData,
                    }, parsed, ExitCode: 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            result = new(BatteryReportSourceStatus.Cancelled);
        }
        catch (BatteryReportWorkspaceException error)
        {
            result = new(error.CleanupIncomplete ? BatteryReportSourceStatus.CleanupIncomplete :
                error.NativeErrorCode is 5 or 0x80070005 ? BatteryReportSourceStatus.AccessDenied : BatteryReportSourceStatus.Unavailable,
                NativeErrorCode: error.NativeErrorCode, CleanupComplete: !error.CleanupIncomplete);
            cleanup = new(!error.CleanupIncomplete, error.NativeErrorCode);
        }
        catch (BatteryReportNativeException error)
        {
            result = new(error.NativeErrorCode is 5 or 0x80070005 ? BatteryReportSourceStatus.AccessDenied : BatteryReportSourceStatus.Failed,
                NativeErrorCode: error.NativeErrorCode);
        }
        catch (UnauthorizedAccessException)
        {
            result = new(BatteryReportSourceStatus.AccessDenied);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            result = new(BatteryReportSourceStatus.Unavailable);
        }
        catch (Exception)
        {
            result = new(BatteryReportSourceStatus.Failed);
        }
        finally
        {
            try
            {
                try
                {
                    if (process is not null && !processExited)
                    {
                        cleanup = process.TerminateAndWait(CleanupTimeout);
                    }
                }
                finally
                {
                    process?.Dispose();
                }

                if (cleanup.Complete && workspace is not null)
                {
                    cleanup = workspace.Cleanup();
                }
            }
            catch (BatteryReportNativeException error)
            {
                cleanup = new(false, error.NativeErrorCode);
            }
            catch (Exception)
            {
                cleanup = new(false);
            }
            finally
            {
                try
                {
                    workspace?.Dispose();
                }
                catch (Exception)
                {
                    cleanup = new(false, cleanup.NativeErrorCode);
                }

                if (!cleanup.Complete)
                {
                    Volatile.Write(ref _gate.CleanupBlocked, 1);
                }

                Volatile.Write(ref _gate.Active, 0);
            }
        }

        return cleanup.Complete
            ? result
            : result with
            {
                Status = BatteryReportSourceStatus.CleanupIncomplete,
                Report = null,
                NativeErrorCode = cleanup.NativeErrorCode ?? result.NativeErrorCode,
                CleanupComplete = false
            };
    }
}

internal sealed class BatteryReportExecutionGate
{
    internal int Active;
    internal int CleanupBlocked;
}

internal sealed class WindowsBatteryReportEnvironment : IBatteryReportEnvironment
{
    public bool HasNoSystemBattery() =>
        WindowsSystemPowerSource.Read() is { BatteryFlag: var batteryFlag } && IsExplicitNoBattery(batteryFlag);

    internal static bool IsExplicitNoBattery(byte batteryFlag) => batteryFlag == 128;

    public IBatteryReportWorkspace CreateWorkspace() => new BatteryReportWorkspace();

    public IBatteryReportProcess StartProcess(IBatteryReportWorkspace workspace) =>
        BatteryReportProcess.LaunchFixedReport(workspace.DirectoryPath, workspace.ReportFileName);
}
