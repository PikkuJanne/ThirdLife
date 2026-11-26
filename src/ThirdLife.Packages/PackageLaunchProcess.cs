using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ThirdLife.Packages;

// Accepts only the private workspace prepared by the package verifier. The executable,
// startup arguments, environment and desktop are fixed by the compiled adapter.
internal static unsafe partial class PackageLaunchProcess
{
    private const uint PollMilliseconds = 50;
    private const uint Signaled = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = uint.MaxValue;
    private const int JobBasicAccounting = 1;
    private const int JobExtendedLimits = 9;
    private const uint JobActiveProcessLimit = 0x00000008;
    private const uint JobCommitLimit = 0x00000200;
    private const uint JobKillOnClose = 0x00002000;
    private const uint ExtendedStartupInfoAndUnicodeEnvironment = 0x00080400;
    private const uint DesktopReadObjectsAndCreateWindow = 0x0003;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StabilityWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);

    internal static PackageVerificationStatus Run(string workspacePath, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return PackageVerificationStatus.Cancelled;
        }

        using var job = CreateJobObject(0, 0);
        var limits = new JobExtendedLimitInformation
        {
            Basic = new JobBasicLimitInformation
            {
                LimitFlags = JobActiveProcessLimit | JobCommitLimit | JobKillOnClose,
                ActiveProcessLimit = 1,
            },
            JobMemoryLimit = 256 * 1024 * 1024,
        };
        if (job.IsInvalid || !SetInformationJobObject(job, JobExtendedLimits, &limits, (uint)sizeof(JobExtendedLimitInformation)))
        {
            return PackageVerificationStatus.LaunchFailed;
        }

        var threadId = GetCurrentThreadId();
        var originalDesktop = GetThreadDesktop(threadId);
        if (originalDesktop == 0)
        {
            return PackageVerificationStatus.LaunchFailed;
        }

        var desktopName = $"ThirdLifePackage-{Guid.NewGuid():N}";
        // This desktop is never switched into view, even if the application shows a startup dialog.
        var desktop = CreateDesktop(desktopName, null, 0, 0, DesktopReadObjectsAndCreateWindow, 0);
        if (desktop == 0)
        {
            return PackageVerificationStatus.LaunchFailed;
        }

        PackageVerificationStatus result;
        bool desktopClosed;
        try
        {
            // Preserve the calling thread's desktop even if Windows associated the new one.
            result = GetThreadDesktop(threadId) == originalDesktop || SetThreadDesktop(originalDesktop)
                ? RunOnDesktop(job, desktop, desktopName, workspacePath, cancellationToken)
                : PackageVerificationStatus.CleanupUnconfirmed;
        }
        finally
        {
            desktopClosed = CloseDesktop(desktop);
        }

        return desktopClosed ? result : PackageVerificationStatus.CleanupUnconfirmed;
    }

    private static PackageVerificationStatus RunOnDesktop(SafeFileHandle job, nint desktop, string desktopName,
        string workspacePath, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return PackageVerificationStatus.Cancelled;
        }

        if (!Start(job, desktopName, workspacePath, out var created))
        {
            return PackageVerificationStatus.LaunchFailed;
        }

        using var process = new SafeFileHandle(created.Process, ownsHandle: true);
        using var thread = new SafeFileHandle(created.Thread, ownsHandle: true);
        PackageVerificationStatus result;
        bool cleaned;
        try
        {
            result = WaitForReady(process, desktop, created, cancellationToken);
        }
        finally
        {
            cleaned = TerminateAndConfirm(job, process);
        }

        return cleaned ? result : PackageVerificationStatus.CleanupUnconfirmed;
    }

    private static bool Start(SafeFileHandle job, string desktopName, string workspacePath, out ProcessInformation created)
    {
        const uint jobListAttribute = 0x0002000D;
        created = default;
        nuint size = 0;
        _ = InitializeProcThreadAttributeList(0, 1, 0, &size);
        if (size is 0 or > 4096)
        {
            return false;
        }

        var attributes = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, &size))
            {
                return false;
            }

            try
            {
                // Assign the job during process creation so no runnable process can be left outside it.
                var jobHandle = job.DangerousGetHandle();
                if (!UpdateProcThreadAttribute(attributes, 0, jobListAttribute, &jobHandle, (nuint)sizeof(nint), 0, 0))
                {
                    return false;
                }

                const string arguments = WindowsPackageLaunchProbe.ExecutableName + " -multiInst -nosession -noPlugin\0";
                var image = Path.Combine(workspacePath, WindowsPackageLaunchProbe.ExecutableName);
                Span<char> commandLine = stackalloc char[arguments.Length];
                arguments.CopyTo(commandLine);
                var system = Environment.SystemDirectory;
                var systemRoot = Path.GetDirectoryName(system);
                var environment = $"PATH={system}\0SystemRoot={systemRoot}\0TEMP={workspacePath}\0TMP={workspacePath}\0\0";
                fixed (char* desktop = desktopName, env = environment, command = commandLine)
                {
                    var startup = new StartupInfoEx
                    {
                        Startup = new StartupInfo { Size = (uint)sizeof(StartupInfoEx), Desktop = desktop },
                        AttributeList = attributes,
                    };
                    return CreateProcess(image, command, 0, 0, false, ExtendedStartupInfoAndUnicodeEnvironment,
                        env, workspacePath, &startup, out created);
                }
            }
            finally
            {
                DeleteProcThreadAttributeList(attributes);
                GC.KeepAlive(job);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(attributes);
        }
    }

    private static PackageVerificationStatus WaitForReady(SafeFileHandle process, nint desktop, ProcessInformation created,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        long? readySince = null;
        var window = new ProbeWindow { ProcessId = created.ProcessId, ThreadId = created.ThreadId };
        while (Stopwatch.GetElapsedTime(started) < StartupTimeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return PackageVerificationStatus.Cancelled;
            }

            switch (WaitForSingleObject(process, PollMilliseconds))
            {
                case Signaled:
                    return PackageVerificationStatus.ExitedEarly;
                case WaitTimeout:
                    break;
                default:
                    return PackageVerificationStatus.LaunchFailed;
            }

            var idle = WaitForInputIdle(process, PollMilliseconds);
            if (idle == WaitFailed)
            {
                return WaitForSingleObject(process, 0) == Signaled
                    ? PackageVerificationStatus.ExitedEarly : PackageVerificationStatus.LaunchFailed;
            }

            window.Found = false;
            if (idle == Signaled)
            {
                _ = EnumDesktopWindows(desktop, &FindProbeWindow, (nint)(&window));
            }

            if (!window.Found)
            {
                readySince = null;
                continue;
            }

            readySince ??= Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(readySince.Value) >= StabilityWindow)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return PackageVerificationStatus.Cancelled;
                }

                return WaitForSingleObject(process, 0) switch
                {
                    Signaled => PackageVerificationStatus.ExitedEarly,
                    WaitTimeout => PackageVerificationStatus.Passed,
                    _ => PackageVerificationStatus.LaunchFailed,
                };
            }
        }

        return PackageVerificationStatus.TimedOut;
    }

    [UnmanagedCallersOnly]
    private static int FindProbeWindow(nint handle, nint context)
    {
        var expected = (ProbeWindow*)context;
        if (GetWindowThreadProcessId(handle, out var process) != expected->ThreadId || process != expected->ProcessId ||
            !IsWindowEnabled(handle))
        {
            return 1;
        }

        Span<char> name = stackalloc char[16];
        fixed (char* data = name)
        {
            var length = GetClassName(handle, data, name.Length);
            expected->Found = name[..length].SequenceEqual("Notepad++");
        }

        return expected->Found ? 0 : 1;
    }

    private static bool TerminateAndConfirm(SafeFileHandle job, SafeFileHandle process)
    {
        _ = TerminateJobObject(job, 1);
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < CleanupTimeout)
        {
            var wait = WaitForSingleObject(process, PollMilliseconds);
            if (wait == WaitFailed)
            {
                return false;
            }

            if (wait == Signaled)
            {
                JobBasicAccountingInformation accounting;
                if (!QueryInformationJobObject(job, JobBasicAccounting, &accounting, (uint)sizeof(JobBasicAccountingInformation), 0))
                {
                    return false;
                }

                if (accounting.ActiveProcesses == 0)
                {
                    return true;
                }

                Thread.Sleep((int)PollMilliseconds);
            }
        }

        return false;
    }

    private struct ProbeWindow
    {
        public uint ProcessId;
        public uint ThreadId;
        public bool Found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved;
        public char* Desktop;
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
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public nint AttributeList;
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
        public JobBasicLimitInformation Basic;
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

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        JobExtendedLimitInformation* information, uint size);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(SafeFileHandle job, int informationClass,
        JobBasicAccountingInformation* information, uint size, nint returnedSize);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(SafeFileHandle job, uint exitCode);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint attributes, uint count, uint flags, nuint* size);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint attributes, uint flags, nuint attribute, void* value,
        nuint size, nint previousValue, nint returnedSize);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void DeleteProcThreadAttributeList(nint attributes);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(string applicationName, char* commandLine, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, char* environment,
        string currentDirectory, StartupInfoEx* startupInfo, out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetThreadDesktop(uint threadId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadDesktop(nint desktop);

    [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateDesktop(string name, string? device, nint deviceMode, uint flags,
        uint access, nint security);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint WaitForInputIdle(SafeFileHandle process, uint milliseconds);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDesktopWindows(nint desktop, delegate* unmanaged<nint, nint, int> callback, nint context);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowEnabled(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassName(nint window, char* name, int capacity);
}
