namespace ThirdLife.Inventory.Windows;

internal enum BatteryReportSourceStatus
{
    Collected = 1,
    NoBattery,
    Unavailable,
    AccessDenied,
    InvalidData,
    Failed,
    TimedOut,
    Cancelled,
    CleanupIncomplete,
}

internal sealed record BatteryReportSourceSnapshot(
    BatteryReportSourceStatus Status,
    BatteryReportParseResult? Report = null,
    uint? NativeErrorCode = null,
    uint? ExitCode = null,
    bool CleanupComplete = true);

internal interface IBatteryReportSource
{
    BatteryReportSourceSnapshot Read(CancellationToken cancellationToken);
}

internal sealed record BatteryReportCleanup(bool Complete, uint? NativeErrorCode = null);

internal interface IBatteryReportWorkspace : IDisposable
{
    string DirectoryPath { get; }
    string ReportFileName { get; }
    long ReportLength { get; }
    Stream OpenReport();
    BatteryReportCleanup Cleanup();
}

internal interface IBatteryReportProcess : IDisposable
{
    bool WaitForExit(int milliseconds);
    uint ExitCode { get; }
    BatteryReportCleanup TerminateAndWait(TimeSpan timeout);
}

internal interface IBatteryReportEnvironment
{
    bool HasNoSystemBattery();
    IBatteryReportWorkspace CreateWorkspace();
    IBatteryReportProcess StartProcess(IBatteryReportWorkspace workspace);
}

internal sealed class BatteryReportNativeException(uint nativeError)
    : Exception("The battery report operation failed.")
{
    internal uint NativeErrorCode { get; } = nativeError;
}

internal sealed class BatteryReportWorkspaceException(bool cleanupIncomplete = false, uint? nativeErrorCode = null)
    : Exception("The battery report workspace is unavailable or unsafe.")
{
    internal bool CleanupIncomplete { get; } = cleanupIncomplete;
    internal uint? NativeErrorCode { get; } = nativeErrorCode;
}
