using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Inventory.Windows;

// This adapter has one compiled operation. No caller supplies an executable, command,
// option set, environment, or arbitrary output path.
internal sealed class BatteryReportProcess : IBatteryReportProcess
{
    private const uint InfiniteFailure = uint.MaxValue;
    private readonly SafeFileHandle _job;
    private readonly SafeFileHandle _process;

    private BatteryReportProcess(SafeFileHandle job, SafeFileHandle process)
    {
        _job = job;
        _process = process;
    }

    internal static BatteryReportProcess LaunchFixedReport(string workingDirectory, string reportFileName)
    {
        if (!BatteryReportWorkspace.IsReportFileName(reportFileName))
        {
            throw new BatteryReportWorkspaceException();
        }

        var systemPath = new char[32768];
        var length = NativeMethods.GetSystemDirectoryW(systemPath, (uint)systemPath.Length);
        if (length == 0 || length >= systemPath.Length)
        {
            throw LastError();
        }

        var executable = Path.Combine(new string(systemPath, 0, checked((int)length)), "powercfg.exe");
        var job = NativeMethods.CreateJobObjectW(0, 0);
        if (job.IsInvalid)
        {
            var error = LastError();
            job.Dispose();
            throw error;
        }

        SafeFileHandle? process = null;
        SafeFileHandle? thread = null;
        var assigned = false;
        try
        {
            var limits = new JobExtendedLimitInformation
            {
                BasicLimitInformation = new JobBasicLimitInformation
                {
                    // Kill owned processes when the last job handle closes. Child breakaway
                    // is not enabled; assigning while suspended closes the child-start race.
                    LimitFlags = 0x2000,
                },
            };
            Check(NativeMethods.SetInformationJobObject(job, 9, in limits,
                (uint)Marshal.SizeOf<JobExtendedLimitInformation>()));
            var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
            var command = BuildCommandLine(reportFileName).ToCharArray();
            // SUSPENDED | NO_WINDOW. No handle inheritance, shell, stdout capture, or elevation.
            Check(NativeMethods.CreateProcessW(executable, command, 0, 0, false, 0x08000004,
                0, workingDirectory, in startup, out var created));
            process = new(created.Process, ownsHandle: true);
            thread = new(created.Thread, ownsHandle: true);
            Check(NativeMethods.AssignProcessToJobObject(job, process));
            assigned = true;
            var result = new BatteryReportProcess(job, process);
            if (NativeMethods.ResumeThread(thread) == InfiniteFailure)
            {
                throw LastError();
            }

            process = null;
            return result;
        }
        catch
        {
            var cleanupComplete = true;
            if (process is not null)
            {
                // The suspended process is still ours even if job assignment failed.
                var terminated = assigned ? NativeMethods.TerminateJobObject(job, 1) : NativeMethods.TerminateProcess(process, 1);
                cleanupComplete = terminated && NativeMethods.WaitForSingleObject(process, 5000) == 0;
            }

            job.Dispose();
            if (!cleanupComplete)
            {
                throw new BatteryReportWorkspaceException(true);
            }

            throw;
        }
        finally
        {
            thread?.Dispose();
            process?.Dispose();
        }
    }

    internal static string BuildCommandLine(string reportFileName)
    {
        if (!BatteryReportWorkspace.IsReportFileName(reportFileName))
        {
            throw new BatteryReportWorkspaceException();
        }

        return string.Concat("powercfg.exe /batteryreport /xml /output ", reportFileName, " /duration 1\0");
    }

    public bool WaitForExit(int milliseconds)
    {
        if (milliseconds is < 0 or > WindowsBatteryReportSource.PollMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        var status = NativeMethods.WaitForSingleObject(_process, (uint)milliseconds);
        if (status == 258)
        {
            return false;
        }

        if (status != 0)
        {
            throw LastError();
        }

        Check(NativeMethods.QueryInformationJobObject(_job, 1, out var accounting,
            (uint)Marshal.SizeOf<JobBasicAccountingInformation>(), 0));
        if (accounting.ActiveProcesses != 0)
        {
            Thread.Sleep(milliseconds);
            return false;
        }

        return true;
    }

    public uint ExitCode
    {
        get
        {
            Check(NativeMethods.GetExitCodeProcess(_process, out var code));
            return code;
        }
    }

    public BatteryReportCleanup TerminateAndWait(TimeSpan timeout)
    {
        if (!NativeMethods.TerminateJobObject(_job, 1))
        {
            return new(false, unchecked((uint)Marshal.GetLastPInvokeError()));
        }

        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < timeout)
        {
            if (WaitForExit(WindowsBatteryReportSource.PollMilliseconds))
            {
                return new(true);
            }
        }

        return new(false);
    }

    public void Dispose()
    {
        _job.Dispose();
        _process.Dispose();
    }

    private static void Check(bool success)
    {
        if (!success)
        {
            throw LastError();
        }
    }

    private static BatteryReportNativeException LastError() =>
        new(unchecked((uint)Marshal.GetLastPInvokeError()));

    private static class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern uint GetSystemDirectoryW([Out] char[] buffer, uint size);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern SafeFileHandle CreateJobObjectW(nint attributes, nint name);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
            in JobExtendedLimitInformation information, uint length);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessW(string applicationName, [In, Out] char[] commandLine,
            nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint flags, nint environment, string currentDirectory, in StartupInfo startupInfo, out ProcessInformation processInformation);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint ResumeThread(SafeFileHandle thread);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass,
            out JobBasicAccountingInformation information, uint length, nint returnLength);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(SafeFileHandle process, uint code);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort ReservedSize;
        public nint ReservedBytes;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimitInformation
    {
        public JobBasicLimitInformation BasicLimitInformation;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodUserTime;
        public long ThisPeriodKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }
}
