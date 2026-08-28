[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$TaskId = "TL-0102"
$ResultSchemaVersion = 2
$SandboxMemoryMb = 4096
$CommandRawOutputLimitBytes = 2097152
$CommandTailReadBytes = 32768
$CommandTimeoutSeconds = 300
$TailLimitBytes = 8192
$ResultLimitBytes = 32768
$MaxSourceFiles = 10000
$MaxSourceBytes = 536870912
$MaxHistoryBundleBytes = 268435456
$MaxHistoryFiles = 50000
$MaxHistoryBytes = 536870912
$MaxNuGetPackages = 64
$MaxNuGetBytes = 536870912
$Limitation = "Same-machine disposable Windows Sandbox evidence only; no direct-host policy compatibility or cross-hardware certification claim. Raw command output is discarded and only a bounded sanitized tail is retained."

$sourceInput = "C:\TL0102\Input\Source"
$harnessDirectory = "C:\TL0102\Input\Harness"
$launcherInput = Join-Path $harnessDirectory "run-tl0102-sandbox.ps1"
$runnerInput = Join-Path $harnessDirectory "run-tl0102-sandbox-guest.ps1"
$requestDirectory = "C:\TL0102\Input\Request"
$requestInput = Join-Path $requestDirectory "run-request.json"
$configInput = Join-Path $requestDirectory "TL0102.wsb"
$dotnetRoot = "C:\TL0102\Input\DotNet"
$pythonRoot = "C:\TL0102\Input\Python"
$gitRoot = "C:\TL0102\Input\Git"
$nugetInput = "C:\TL0102\Input\NuGet"
$pythonPackagesInput = "C:\TL0102\Input\PythonPackages"
$historyInput = "C:\TL0102\Input\History"
$resultDirectory = "C:\TL0102\Output"
$workDirectory = "C:\TL0102\Work"
$stateDirectory = "C:\TL0102\State"
$resultPath = Join-Path $resultDirectory "tl0102-result.json"
$completionMarkerPath = Join-Path $resultDirectory "complete.marker"
$startedAt = [DateTimeOffset]::UtcNow
$exitCode = -1
$resultState = "failed"
$failureCode = "preflight_failed"
$sourceDigest = "0000000000000000000000000000000000000000000000000000000000000000"
$sourceUnchangedAfter = $false
$dotnetVersion = "unavailable"
$pythonVersion = "not_required"
$gitVersion = "unavailable"
$anyOutputTruncated = $false
$diagnosticTail = New-Object System.Collections.Generic.List[string]
$commandSequence = 0
$commandTreeTerminationUnverified = $false
$request = $null
$phase = "Unavailable"

$commandRunnerSource = @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ThirdLife.Sandbox
{
    public sealed class BoundedCommandResult
    {
        public int ExitCode { get; internal set; }
        public bool ExitCodeAvailable { get; internal set; }
        public bool TimedOut { get; internal set; }
        public bool OutputExceeded { get; internal set; }
        public bool ProcessTreeTerminated { get; internal set; }
        public bool DrainCompleted { get; internal set; }
        public bool TailTruncated { get; internal set; }
        public long RawBytesAccepted { get; internal set; }
        public byte[] TailBytes { get; internal set; }
        public string DrainError { get; internal set; }
    }

    public static class BoundedCommandRunner
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateNoWindow = 0x08000000;
        private const uint StartfUseStdHandles = 0x00000100;
        private const uint HandleFlagInherit = 0x00000001;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const int JobObjectBasicAccountingInformationClass = 1;
        private const int JobObjectExtendedLimitInformationClass = 9;
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;
        private const uint StillActive = 259;
        private const int ErrorBrokenPipe = 109;
        private const int ErrorHandleEof = 38;
        private const int TerminationVerificationMilliseconds = 10000;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string Reserved;
            public string Desktop;
            public string Title;
            public uint X;
            public uint Y;
            public uint XSize;
            public uint YSize;
            public uint XCountChars;
            public uint YCountChars;
            public uint FillAttribute;
            public uint Flags;
            public ushort ShowWindow;
            public ushort Reserved2Length;
            public IntPtr Reserved2;
            public IntPtr StdInput;
            public IntPtr StdOutput;
            public IntPtr StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicAccountingInformation
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        private sealed class DrainState
        {
            private readonly object sync = new object();
            private readonly byte[] tail;
            private readonly long byteLimit;
            private readonly IntPtr job;
            private int tailCount;
            private int tailNext;
            private long rawBytesAccepted;
            private bool outputExceeded;
            private bool completed;
            private string error;

            internal DrainState(int tailCapacity, long maximumBytes, IntPtr jobHandle)
            {
                tail = new byte[tailCapacity];
                byteLimit = maximumBytes;
                job = jobHandle;
            }

            internal bool OutputExceeded
            {
                get { lock (sync) { return outputExceeded; } }
            }

            internal bool Completed
            {
                get { lock (sync) { return completed; } }
            }

            internal string Error
            {
                get { lock (sync) { return error; } }
            }

            internal long RawBytesAccepted
            {
                get { lock (sync) { return rawBytesAccepted; } }
            }

            internal bool TailTruncated
            {
                get { lock (sync) { return rawBytesAccepted > tail.Length || outputExceeded; } }
            }

            internal byte[] GetTail()
            {
                lock (sync)
                {
                    byte[] result = new byte[tailCount];
                    if (tailCount == 0) return result;
                    int start = tailCount == tail.Length ? tailNext : 0;
                    int first = Math.Min(tailCount, tail.Length - start);
                    Buffer.BlockCopy(tail, start, result, 0, first);
                    if (first < tailCount) Buffer.BlockCopy(tail, 0, result, first, tailCount - first);
                    return result;
                }
            }

            private void AppendTail(byte[] source, int count)
            {
                if (count <= 0) return;
                if (count >= tail.Length)
                {
                    Buffer.BlockCopy(source, count - tail.Length, tail, 0, tail.Length);
                    tailCount = tail.Length;
                    tailNext = 0;
                    return;
                }
                int first = Math.Min(count, tail.Length - tailNext);
                Buffer.BlockCopy(source, 0, tail, tailNext, first);
                if (first < count) Buffer.BlockCopy(source, first, tail, 0, count - first);
                tailNext = (tailNext + count) % tail.Length;
                tailCount = Math.Min(tail.Length, tailCount + count);
            }

            internal void Drain(IntPtr pipe)
            {
                byte[] buffer = new byte[4096];
                try
                {
                    while (true)
                    {
                        int requested;
                        lock (sync)
                        {
                            long remaining = byteLimit - rawBytesAccepted;
                            requested = (int)Math.Min((long)buffer.Length, remaining + 1L);
                        }
                        int read;
                        if (!ReadFile(pipe, buffer, requested, out read, IntPtr.Zero))
                        {
                            int code = Marshal.GetLastWin32Error();
                            if (code != ErrorBrokenPipe && code != ErrorHandleEof)
                            {
                                lock (sync) { error = "Redirected output drain failed with Win32 error " + code + "."; }
                            }
                            break;
                        }
                        if (read <= 0) break;

                        bool exceededNow = false;
                        lock (sync)
                        {
                            long remaining = byteLimit - rawBytesAccepted;
                            int accepted = (int)Math.Min((long)read, remaining);
                            AppendTail(buffer, accepted);
                            rawBytesAccepted += accepted;
                            if (read > accepted)
                            {
                                outputExceeded = true;
                                exceededNow = true;
                            }
                        }
                        if (exceededNow)
                        {
                            TerminateJobObject(job, 125);
                            break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    lock (sync) { error = "Redirected output drain stopped unexpectedly: " + exception.GetType().Name + "."; }
                }
                finally
                {
                    lock (sync) { completed = true; }
                }
            }
        }

        public static BoundedCommandResult Run(
            string filePath,
            string arguments,
            string currentDirectory,
            int timeoutMilliseconds,
            long maximumOutputBytes,
            int tailCapacity)
        {
            if (String.IsNullOrWhiteSpace(filePath) || filePath.IndexOf('"') >= 0 || !System.IO.File.Exists(filePath))
                throw new ArgumentException("The wrapper executable path is invalid.", "filePath");
            if (arguments == null) throw new ArgumentNullException("arguments");
            if (String.IsNullOrWhiteSpace(currentDirectory) || !System.IO.Directory.Exists(currentDirectory))
                throw new ArgumentException("The command working directory is invalid.", "currentDirectory");
            if (timeoutMilliseconds <= 0 || maximumOutputBytes <= 0 || tailCapacity <= 0)
                throw new ArgumentOutOfRangeException("Command bounds must be positive.");

            StringBuilder commandLine = new StringBuilder("\"" + filePath + "\" " + arguments);
            if (commandLine.Length > 32767) throw new ArgumentException("The wrapper command line is too long.");

            IntPtr job = IntPtr.Zero;
            IntPtr pipeRead = IntPtr.Zero;
            IntPtr pipeWrite = IntPtr.Zero;
            IntPtr nullInput = IntPtr.Zero;
            ProcessInformation process = new ProcessInformation();
            Thread drainThread = null;
            DrainState drain = null;
            bool processCreated = false;

            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "A command job object could not be created.");

                JobObjectExtendedLimitInformation limits = new JobObjectExtendedLimitInformation();
                limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, ref limits, Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation))))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The command job object could not be hardened.");

                SecurityAttributes inheritable = new SecurityAttributes();
                inheritable.Length = Marshal.SizeOf(typeof(SecurityAttributes));
                inheritable.InheritHandle = true;
                if (!CreatePipe(out pipeRead, out pipeWrite, ref inheritable, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The redirected output pipe could not be created.");
                if (!SetHandleInformation(pipeRead, HandleFlagInherit, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The redirected output pipe could not be restricted.");

                nullInput = CreateFile("NUL", GenericRead, FileShareRead | FileShareWrite, ref inheritable, OpenExisting, FileAttributeNormal, IntPtr.Zero);
                if (nullInput == InvalidHandleValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The command input handle could not be created.");

                StartupInfo startup = new StartupInfo();
                startup.Size = Marshal.SizeOf(typeof(StartupInfo));
                startup.Flags = StartfUseStdHandles;
                startup.StdInput = nullInput;
                startup.StdOutput = pipeWrite;
                startup.StdError = pipeWrite;
                if (!CreateProcess(filePath, commandLine, IntPtr.Zero, IntPtr.Zero, true, CreateSuspended | CreateNoWindow,
                    IntPtr.Zero, currentDirectory, ref startup, out process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The bounded command wrapper could not start.");
                processCreated = true;

                CloseHandle(pipeWrite);
                pipeWrite = IntPtr.Zero;
                CloseHandle(nullInput);
                nullInput = IntPtr.Zero;

                if (!AssignProcessToJobObject(job, process.Process))
                {
                    TerminateProcess(process.Process, 126);
                    WaitForSingleObject(process.Process, TerminationVerificationMilliseconds);
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The bounded command wrapper could not enter its job object.");
                }

                drain = new DrainState(tailCapacity, maximumOutputBytes, job);
                drainThread = new Thread(delegate() { drain.Drain(pipeRead); });
                drainThread.IsBackground = true;
                drainThread.Name = "TL0102 bounded output drain";
                drainThread.Start();

                if (ResumeThread(process.Thread) == UInt32.MaxValue)
                {
                    TerminateJobObject(job, 126);
                    WaitForJobEmpty(job, TerminationVerificationMilliseconds);
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The bounded command wrapper could not resume.");
                }

                Stopwatch elapsed = Stopwatch.StartNew();
                bool timedOut = false;
                bool terminationRequested = false;
                bool processTreeTerminated = false;
                uint observedExitCode = StillActive;
                bool observedExitCodeAvailable = false;
                while (true)
                {
                    if (drain.OutputExceeded)
                    {
                        terminationRequested = true;
                        TerminateJobObject(job, 125);
                        break;
                    }

                    uint waitResult = WaitForSingleObject(process.Process, 10);
                    if (waitResult == WaitObject0)
                    {
                        terminationRequested = true;
                        observedExitCodeAvailable = GetExitCodeProcess(process.Process, out observedExitCode) &&
                            observedExitCode != StillActive;
                        TerminateJobObject(job, observedExitCodeAvailable ? observedExitCode : 126);
                        break;
                    }
                    if (waitResult != WaitTimeout)
                    {
                        terminationRequested = true;
                        TerminateJobObject(job, 126);
                        break;
                    }
                    if (elapsed.ElapsedMilliseconds >= timeoutMilliseconds)
                    {
                        timedOut = true;
                        terminationRequested = true;
                        TerminateJobObject(job, 124);
                        break;
                    }
                    Thread.Sleep(10);
                }

                if (terminationRequested)
                    processTreeTerminated = WaitForJobEmpty(job, TerminationVerificationMilliseconds);

                bool drainCompleted = drainThread.Join(TerminationVerificationMilliseconds) && drain.Completed;
                bool outputExceeded = drain.OutputExceeded;
                uint nativeExitCode = StillActive;
                bool exitCodeAvailable = processTreeTerminated &&
                    (observedExitCodeAvailable ||
                     (GetExitCodeProcess(process.Process, out nativeExitCode) && nativeExitCode != StillActive));
                if (observedExitCodeAvailable) nativeExitCode = observedExitCode;

                BoundedCommandResult result = new BoundedCommandResult();
                result.ExitCode = exitCodeAvailable ? unchecked((int)nativeExitCode) : -1;
                result.ExitCodeAvailable = exitCodeAvailable;
                result.TimedOut = timedOut;
                result.OutputExceeded = outputExceeded;
                result.ProcessTreeTerminated = processTreeTerminated;
                result.DrainCompleted = drainCompleted;
                result.TailTruncated = drain.TailTruncated;
                result.RawBytesAccepted = drain.RawBytesAccepted;
                result.TailBytes = drain.GetTail();
                result.DrainError = drain.Error;
                return result;
            }
            finally
            {
                if (job != IntPtr.Zero) CloseHandle(job);
                if (drainThread != null && drainThread.IsAlive) drainThread.Join(TerminationVerificationMilliseconds);
                if (pipeRead != IntPtr.Zero) CloseHandle(pipeRead);
                if (pipeWrite != IntPtr.Zero) CloseHandle(pipeWrite);
                if (nullInput != IntPtr.Zero && nullInput != InvalidHandleValue) CloseHandle(nullInput);
                if (processCreated)
                {
                    if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                    if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
                }
            }
        }

        private static bool TryGetActiveProcessCount(IntPtr job, out uint activeProcesses)
        {
            JobObjectBasicAccountingInformation accounting;
            if (!QueryInformationJobObject(job, JobObjectBasicAccountingInformationClass, out accounting,
                Marshal.SizeOf(typeof(JobObjectBasicAccountingInformation)), IntPtr.Zero))
            {
                activeProcesses = UInt32.MaxValue;
                return false;
            }
            activeProcesses = accounting.ActiveProcesses;
            return true;
        }

        private static bool WaitForJobEmpty(IntPtr job, int timeoutMilliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < timeoutMilliseconds)
            {
                uint activeProcesses;
                if (!TryGetActiveProcessCount(job, out activeProcesses)) return false;
                if (activeProcesses == 0) return true;
                Thread.Sleep(10);
            }
            uint remaining;
            return TryGetActiveProcessCount(job, out remaining) && remaining == 0;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass,
            ref JobObjectExtendedLimitInformation information, int informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObject(IntPtr job, int informationClass,
            out JobObjectBasicAccountingInformation information, int informationLength, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe,
            ref SecurityAttributes pipeAttributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
            ref SecurityAttributes securityAttributes, uint creationDisposition, uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(IntPtr file, byte[] buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, int milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
'@
Add-Type -TypeDefinition $commandRunnerSource -Language CSharp

$sandboxIdentityVerified = $env:USERNAME -eq "WDAGUtilityAccount"
$sandboxMappedInvocation = [StringComparer]::OrdinalIgnoreCase.Equals(
    [System.IO.Path]::GetFullPath($PSCommandPath),
    [System.IO.Path]::GetFullPath($runnerInput)
)
if (-not $sandboxIdentityVerified) { throw "The internal TL-0102 guest runner may execute only inside Windows Sandbox." }
if (-not $sandboxMappedInvocation) { throw "The internal TL-0102 guest runner must execute from its read-only mapped path." }

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text)))).Replace("-", "").ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-FileSha512Base64 {
    param([Parameter(Mandatory = $true)][string] $Path)
    $stream = [System.IO.File]::OpenRead($Path)
    $sha = [System.Security.Cryptography.SHA512]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash($stream)) }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Get-SourceDigest {
    param([Parameter(Mandatory = $true)][string] $Root)
    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd("\")
    $records = foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force | Sort-Object FullName) {
        $relative = $file.FullName.Substring($rootPath.Length).TrimStart("\").Replace("\", "/")
        $segments = @($relative.Split('/'))
        if (@($segments | Where-Object { $_.ToLowerInvariant() -in @(".git", ".venv", "__pycache__", ".pytest_cache", "bin", "obj", "testresults") }).Count -ne 0) { continue }
        "$relative`:$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    }
    return Get-Sha256Text -Text ($records -join "`n")
}

function Assert-BoundedTree {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][int] $MaximumFiles,
        [Parameter(Mandatory = $true)][long] $MaximumBytes
    )
    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction Stop)
    if ($files.Count -gt $MaximumFiles) { throw "A guest input tree exceeds its file-count bound." }
    [long]$total = 0
    foreach ($file in $files) {
        $total += $file.Length
        if ($total -gt $MaximumBytes) { throw "A guest input tree exceeds its aggregate byte bound." }
    }
}

function Write-Utf8CreateNew {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Content
    )
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Content)
    $stream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None
    )
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function Invoke-Robocopy {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination
    )
    New-Item -ItemType Directory -Path $Destination | Out-Null
    $command = Invoke-BoundedCommand -Label "source-copy" -FilePath "$env:WINDIR\System32\robocopy.exe" -Arguments @(
        $Source, $Destination, "/MIR", "/COPY:DAT", "/DCOPY:DAT", "/R:2", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP"
    )
    if ($command.timed_out) { $script:failureCode = "command_timeout"; throw "The bounded Sandbox source copy timed out." }
    if ($command.output_exceeded) { $script:failureCode = "output_limit_exceeded"; throw "The bounded Sandbox source copy exceeded its output cap." }
    if ($command.exit_code -gt 7) { $script:failureCode = "source_binding_failed"; throw "The bounded Sandbox source copy failed." }
}

function ConvertTo-SanitizedLine {
    param([Parameter(Mandatory = $true)][string] $Line)
    $value = $Line -replace "[\x00-\x08\x0B\x0C\x0E-\x1F]", ""
    if ($value -match "(?i)(?:password|token|authorization|credential)\s*[:=]|recovery\s+key") { return "[redacted sensitive line]" }
    foreach ($replacement in @(
        @($workDirectory, "<work>"), @($stateDirectory, "<state>"), @($sourceInput, "<source>"),
        @($nugetInput, "<nuget-source>"), @($pythonPackagesInput, "<python-source>"),
        @($resultDirectory, "<output>"), @($harnessDirectory, "<harness>"), @($requestDirectory, "<request>")
    )) {
        $value = [regex]::Replace($value, [regex]::Escape($replacement[0]), $replacement[1], [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    $value = [regex]::Replace($value, "(?i)C:\\Users\\[^\\\s]+", "<user-path>")
    $value = [regex]::Replace($value, "(?i)https?://\S+", "<url>")
    $value = [regex]::Replace($value, "(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)", "<ip>")
    $value = $value.Replace("WDAGUtilityAccount", "<sandbox-user>").Trim()
    if ($value.Length -gt 512) { $value = $value.Substring(0, 509) + "..." }
    return $value
}

function Add-DiagnosticTail {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Text,
        [Parameter(Mandatory = $true)][bool] $Truncated
    )
    $lines = @($Text -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 30)
    $script:diagnosticTail.Add("[$Label]")
    foreach ($line in $lines) { $script:diagnosticTail.Add((ConvertTo-SanitizedLine -Line $line)) }
    if ($Truncated) { $script:diagnosticTail.Add("[earlier command output discarded]"); $script:anyOutputTruncated = $true }
    while ($script:diagnosticTail.Count -gt 40 -or
           [System.Text.Encoding]::UTF8.GetByteCount(($script:diagnosticTail -join "`n")) -gt $TailLimitBytes) {
        $script:diagnosticTail.RemoveAt(0)
        $script:anyOutputTruncated = $true
    }
}

function Invoke-BoundedCommand {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $false)][string] $SuccessMarker = ""
    )
    $script:commandSequence += 1
    Add-DiagnosticTail -Label ("start-" + $Label) -Text "started" -Truncated $false
    $commandRequestPath = Join-Path $stateDirectory "command-$($script:commandSequence).json"
    $commandRequest = [ordered]@{ file_path = $FilePath; arguments = @($Arguments) } | ConvertTo-Json -Depth 3 -Compress
    Write-Utf8CreateNew -Path $commandRequestPath -Content $commandRequest

$childScript = @'
$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$request = Get-Content -Raw -LiteralPath $env:TL0102_COMMAND_REQUEST -Encoding UTF8 | ConvertFrom-Json
& $request.file_path @($request.arguments) *>&1 | ForEach-Object { [Console]::Out.WriteLine([string]$_) }
$code = $LASTEXITCODE
if ($null -eq $code) { $code = if ($?) { 0 } else { 1 } }
[Console]::Out.Flush()
exit [int]$code
'@
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($childScript))
    $previousRequestPath = [Environment]::GetEnvironmentVariable("TL0102_COMMAND_REQUEST", [EnvironmentVariableTarget]::Process)
    try {
        [Environment]::SetEnvironmentVariable("TL0102_COMMAND_REQUEST", $commandRequestPath, [EnvironmentVariableTarget]::Process)
        try {
            $nativeResult = [ThirdLife.Sandbox.BoundedCommandRunner]::Run(
                (Join-Path $PSHOME "powershell.exe"),
                "-NoLogo -NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand $encoded",
                (Get-Location).ProviderPath,
                ($CommandTimeoutSeconds * 1000),
                $CommandRawOutputLimitBytes,
                $CommandTailReadBytes
            )
        }
        catch {
            $exceptionType = $_.Exception.GetType().FullName -replace "[^A-Za-z0-9.]", "_"
            $nativeCode = if ($_.Exception -is [System.ComponentModel.Win32Exception]) { $_.Exception.NativeErrorCode } else { -1 }
            Add-DiagnosticTail -Label $Label -Text "runner_exception_${exceptionType}_code_${nativeCode}" -Truncated $false
            throw
        }
        if (-not $nativeResult.ProcessTreeTerminated) {
            $script:commandTreeTerminationUnverified = $true
            throw "A bounded command tree could not be confirmed terminated; result publication is prohibited."
        }
        if (-not $nativeResult.DrainCompleted -or -not [string]::IsNullOrEmpty($nativeResult.DrainError)) {
            Add-DiagnosticTail -Label $Label -Text "runner_output_drain_incomplete" -Truncated $false
            throw "A bounded command output stream did not drain completely."
        }
        if (-not $nativeResult.ExitCodeAvailable -and -not $nativeResult.TimedOut -and -not $nativeResult.OutputExceeded) {
            Add-DiagnosticTail -Label $Label -Text "runner_exit_code_unavailable" -Truncated $false
            throw "A bounded command exit code was unavailable."
        }
        $tailText = ""
        if ($nativeResult.RawBytesAccepted -gt 0) {
            [byte[]] $tailBytes = @($nativeResult.TailBytes)
            $tailText = [System.Text.UTF8Encoding]::new($false, $false).GetString($tailBytes)
        }
        if ($nativeResult.TailTruncated) {
            $firstLineEnd = $tailText.IndexOf("`n")
            $tailText = if ($firstLineEnd -ge 0) { $tailText.Substring($firstLineEnd + 1) } else { "" }
        }
        Add-DiagnosticTail -Label $Label -Text $tailText -Truncated $nativeResult.TailTruncated
        $markerFound = [string]::IsNullOrEmpty($SuccessMarker) -or $tailText.Contains($SuccessMarker)
        $code = if ($nativeResult.TimedOut) { 124 } elseif ($nativeResult.OutputExceeded) { 125 } else { $nativeResult.ExitCode }
        return [pscustomobject]@{
            exit_code = [int]$code
            timed_out = $nativeResult.TimedOut
            output_exceeded = $nativeResult.OutputExceeded
            marker_found = $markerFound
            tail_text = $tailText
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable("TL0102_COMMAND_REQUEST", $previousRequestPath, [EnvironmentVariableTarget]::Process)
        Remove-Item -LiteralPath $commandRequestPath -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $commandRequestPath) { throw "Bounded command metadata cleanup did not complete." }
    }
}

function Invoke-RequiredCommand {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][ValidateSet("source_binding_failed", "python_bootstrap_failed", "restore_failed", "test_failed", "verification_failed")][string] $Failure,
        [Parameter(Mandatory = $false)][string] $SuccessMarker = ""
    )
    $previousFailureCode = $script:failureCode
    $script:failureCode = $Failure
    try {
        $command = Invoke-BoundedCommand -Label $Label -FilePath $FilePath -Arguments $Arguments -SuccessMarker $SuccessMarker
    }
    catch {
        $exceptionType = $_.Exception.GetType().FullName -replace "[^A-Za-z0-9.]", "_"
        $nativeCode = if ($_.Exception -is [System.ComponentModel.Win32Exception]) { $_.Exception.NativeErrorCode } else { -1 }
        Add-DiagnosticTail -Label $Label -Text "command_exception_${exceptionType}_code_${nativeCode}" -Truncated $false
        throw
    }
    $script:exitCode = $command.exit_code
    if ($command.timed_out) { $script:failureCode = "command_timeout"; throw "A governed command exceeded its timeout." }
    if ($command.output_exceeded) { $script:failureCode = "output_limit_exceeded"; throw "A governed command exceeded its raw output limit." }
    if ($command.exit_code -ne 0) { $script:failureCode = $Failure; throw "A governed command returned a failure." }
    if (-not $command.marker_found) { $script:exitCode = 2; $script:failureCode = "success_marker_missing"; throw "A governed command omitted its required success marker." }
    $script:failureCode = $previousFailureCode
}

function Assert-ExactObjectKeys {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string[]] $Keys,
        [Parameter(Mandatory = $true)][string] $Label
    )
    $actual = @($Object.PSObject.Properties.Name) | Sort-Object
    $expected = @($Keys) | Sort-Object
    if (@(Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -ne 0) { throw "$Label does not match its exact schema." }
}

function Assert-RunRequest {
    param([Parameter(Mandatory = $true)] $RunRequest)
    Assert-ExactObjectKeys -Object $RunRequest -Label "The run request" -Keys @(
        "expected_head_commit", "expected_source_digest", "expected_tracked_clean", "history_bundle_sha256", "launcher_sha256",
        "network_reason", "networking_enabled", "nuget_audit_enabled", "nuget_closure_sha256",
        "nuget_package_count", "phase", "python_packages_sha256", "repository_status_sha256", "run_id",
        "runner_sha256", "sandbox_config_sha256", "sandbox_executable_version", "schema_version",
        "source_history_included", "task_id"
    )
    if ($RunRequest.schema_version -ne $ResultSchemaVersion -or $RunRequest.task_id -ne $TaskId -or
        $RunRequest.phase -notin @("RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Targeted", "Quick", "Full")) { throw "The run request targets an invalid task, schema, or phase." }
    if ($RunRequest.run_id -isnot [string] -or $RunRequest.run_id -notmatch "^[0-9a-f]{32}$" -or
        $RunRequest.expected_head_commit -isnot [string] -or $RunRequest.expected_head_commit -notmatch "^[0-9a-f]{40}$") { throw "The run request contains an invalid source identity." }
    foreach ($digestName in @("expected_source_digest", "history_bundle_sha256", "repository_status_sha256", "launcher_sha256", "runner_sha256", "sandbox_config_sha256", "nuget_closure_sha256", "python_packages_sha256")) {
        if ($RunRequest.$digestName -isnot [string] -or $RunRequest.$digestName -notmatch "^[0-9a-f]{64}$") { throw "The run request contains an invalid digest." }
    }
    foreach ($booleanName in @("expected_tracked_clean", "source_history_included", "networking_enabled", "nuget_audit_enabled")) {
        if ($RunRequest.$booleanName -isnot [bool]) { throw "The run request contains an invalid Boolean." }
    }
    $historyRequired = $RunRequest.phase -in @("Quick", "Full")
    if ($RunRequest.source_history_included -ne $historyRequired -or ($RunRequest.nuget_package_count -isnot [int] -and $RunRequest.nuget_package_count -isnot [long]) -or
        $RunRequest.nuget_package_count -lt 0 -or $RunRequest.nuget_package_count -gt $MaxNuGetPackages) { throw "The run request contains an unsafe history or package-count value." }
    $full = $RunRequest.phase -eq "Full"
    if ($RunRequest.networking_enabled -ne $full -or $RunRequest.nuget_audit_enabled -ne $full -or
        $RunRequest.network_reason -ne $(if ($full) { "governed_nuget_audit" } else { "disabled_offline_phase" })) { throw "The run request violates the phase network contract." }
}

function Assert-SandboxConfiguration {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $RunRequest
    )
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $RunRequest.sandbox_config_sha256) { throw "The mapped Sandbox configuration digest does not match the request." }
    [xml]$document = Get-Content -Raw -LiteralPath $Path -Encoding UTF8
    $configuration = $document.Configuration
    $expectedNetworking = if ($RunRequest.networking_enabled) { "Enable" } else { "Disable" }
    if ($configuration.Networking -ne $expectedNetworking -or $configuration.VGpu -ne "Disable" -or
        $configuration.AudioInput -ne "Disable" -or $configuration.VideoInput -ne "Disable" -or
        $configuration.ProtectedClient -ne "Enable" -or $configuration.PrinterRedirection -ne "Disable" -or
        $configuration.ClipboardRedirection -ne "Disable" -or [int]$configuration.MemoryInMB -ne $SandboxMemoryMb) { throw "The mapped Sandbox configuration violates the hardened device or network contract." }
    $folders = @($configuration.MappedFolders.MappedFolder)
    $expectedFolders = [ordered]@{
        "C:\TL0102\Input\Source" = "true"; "C:\TL0102\Input\Harness" = "true"
        "C:\TL0102\Input\Request" = "true"; "C:\TL0102\Input\DotNet" = "true"
        "C:\TL0102\Input\Python" = "true"; "C:\TL0102\Input\Git" = "true"
        "C:\TL0102\Input\NuGet" = "true"; "C:\TL0102\Input\PythonPackages" = "true"
        "C:\TL0102\Input\History" = "true"; "C:\TL0102\Output" = "false"
    }
    if ($folders.Count -ne $expectedFolders.Count -or @($folders | Where-Object { $_.ReadOnly -eq "false" }).Count -ne 1 -or
        @($folders | Where-Object { $_.ReadOnly -eq "false" -and $_.SandboxFolder -eq "C:\TL0102\Output" }).Count -ne 1) { throw "The mapped Sandbox configuration violates the writable-folder allowlist." }
    foreach ($folder in $folders) {
        $sandboxFolder = [string]$folder.SandboxFolder
        if (-not $expectedFolders.Contains($sandboxFolder) -or [string]$folder.ReadOnly -ne $expectedFolders[$sandboxFolder]) { throw "The mapped Sandbox configuration contains an unexpected folder or access mode." }
    }
}

function Assert-HistoryBundle {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $RunRequest
    )
    if ((Get-SourceDigest -Root $Path) -ne $RunRequest.history_bundle_sha256) { throw "The mapped Git history bundle digest does not match the request." }
    $items = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    if (-not $RunRequest.source_history_included) {
        if ($items.Count -ne 0) { throw "A focused phase received unnecessary Git history." }
        return
    }
    if ($items.Count -ne 1 -or $items[0].PSIsContainer -or $items[0].Name -ne "repository-history.bundle" -or
        $items[0].Length -le 0 -or $items[0].Length -gt $MaxHistoryBundleBytes) { throw "The governed Git history input is not the exact bounded bundle." }
}

function Assert-NuGetClosure {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $RunRequest
    )
    if ((Get-SourceDigest -Root $Path) -ne $RunRequest.nuget_closure_sha256) { throw "The mapped NuGet closure digest does not match the request." }
    $manifestPath = Join-Path $Path "nuget-closure.json"
    $manifestItem = Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
    if ($manifestItem.Length -le 0 -or $manifestItem.Length -gt 131072) { throw "The NuGet closure manifest is outside its bound." }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json
    Assert-ExactObjectKeys -Object $manifest -Keys @("packages", "schema_version") -Label "The NuGet closure manifest"
    if ($manifest.schema_version -ne 1) { throw "The NuGet closure manifest uses an unsupported schema." }
    $packages = @($manifest.packages)
    if ($packages.Count -ne $RunRequest.nuget_package_count) { throw "The NuGet closure count does not match the request." }
    $expectedNames = @("nuget-closure.json")
    $seen = @{}
    [long]$totalBytes = 0
    foreach ($package in $packages) {
        Assert-ExactObjectKeys -Object $package -Keys @("archive_sha512", "bytes", "content_hash", "file", "id", "sha256", "version") -Label "A NuGet closure entry"
        if ($package.file -isnot [string] -or $package.file -notmatch "^[a-z0-9][a-z0-9._+-]{0,255}\.nupkg$" -or
            $package.sha256 -isnot [string] -or $package.sha256 -notmatch "^[0-9a-f]{64}$" -or
            $package.content_hash -isnot [string] -or $package.content_hash -notmatch "^[A-Za-z0-9+/]{86}==$" -or
            $package.archive_sha512 -isnot [string] -or $package.archive_sha512 -notmatch "^[A-Za-z0-9+/]{86}==$" -or
            $seen.ContainsKey($package.file)) { throw "A NuGet closure entry is invalid or duplicated." }
        $seen[$package.file] = $true
        $packagePath = Join-Path $Path $package.file
        $item = Get-Item -LiteralPath $packagePath -Force -ErrorAction Stop
        $totalBytes += $item.Length
        if ($totalBytes -gt $MaxNuGetBytes) { throw "The guest-visible NuGet closure exceeds its aggregate byte bound." }
        if ($item.PSIsContainer -or $item.Length -ne [long]$package.bytes -or $item.Length -le 0 -or $item.Length -gt 268435456 -or
            (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256 -or
            (Get-FileSha512Base64 -Path $packagePath) -ne $package.archive_sha512) { throw "A staged NuGet archive does not match its exact closure entry." }
        $expectedNames += $package.file
    }
    $actualNames = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop | ForEach-Object { $_.Name })
    if (@(Compare-Object -ReferenceObject ($expectedNames | Sort-Object) -DifferenceObject ($actualNames | Sort-Object)).Count -ne 0) { throw "The guest-visible NuGet source contains an unrelated object." }
    Assert-BoundedTree -Path $Path -MaximumFiles ($MaxNuGetPackages + 1) -MaximumBytes ($MaxNuGetBytes + 131072)
}

function Assert-PythonPackages {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $RunRequest
    )
    if ((Get-SourceDigest -Root $Path) -ne $RunRequest.python_packages_sha256) { throw "The mapped Python package digest does not match the request." }
    $items = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
    if ($RunRequest.phase -notin @("Quick", "Full")) {
        if ($items.Count -ne 0) { throw "A focused phase received an unnecessary Python package." }
        return
    }
    if ($items.Count -ne 1 -or $items[0].PSIsContainer -or $items[0].Name -notmatch "(?i)^pyyaml-6\.0\.3-.+\.whl$") { throw "The offline Python source does not contain exactly the admitted PyYAML wheel." }
    $requirements = Get-Content -Raw -LiteralPath (Join-Path $workDirectory "tools\requirements.txt") -Encoding UTF8
    $admitted = @([regex]::Matches($requirements, "(?im)--hash=sha256:([0-9a-f]{64})") | ForEach-Object { $_.Groups[1].Value })
    $actual = (Get-FileHash -LiteralPath $items[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -notin $admitted) { throw "The offline Python wheel is not admitted by the exact requirements file." }
}

function Write-SandboxNuGetConfig {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][bool] $AuditEnabled
    )
    $auditSources = if ($AuditEnabled) {
@"
  <auditSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </auditSources>
"@
    }
    else {
@"
  <auditSources>
    <clear />
  </auditSources>
"@
    }
    $content = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="tl0102-local" value="$nugetInput" />
  </packageSources>
$auditSources
  <packageSourceMapping>
    <packageSource key="tl0102-local">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
  <disabledPackageSources><clear /></disabledPackageSources>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
"@
    Write-Utf8CreateNew -Path $Path -Content $content
}

$result = [ordered]@{
    schema_version = $ResultSchemaVersion
    task_id = $TaskId
    run_id = "00000000000000000000000000000000"
    phase = $phase
    result = "failed"
    exit_code = -1
    started_utc = $startedAt.UtcDateTime.ToString("o")
    finished_utc = $startedAt.UtcDateTime.ToString("o")
    duration_seconds = 0.0
    environment = "Windows Sandbox"
    sandbox_memory_mb = $SandboxMemoryMb
    same_physical_machine = $true
    networking_enabled = $false
    network_reason = "disabled_offline_phase"
    nuget_audit_enabled = $false
    source_history_included = $false
    history_bundle_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    expected_head_commit = "0000000000000000000000000000000000000000"
    expected_tracked_clean = $false
    repository_status_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    source_digest = $sourceDigest
    source_unchanged_after = $false
    launcher_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    runner_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    sandbox_config_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    nuget_closure_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    nuget_package_count = 0
    python_packages_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
    git_version = "unavailable"
    dotnet_sdk = "unavailable"
    python_version = "not_required"
    failure_code = "preflight_failed"
    output_truncated = $false
    raw_output_retained = $false
    output_tail = @()
    limitation = $Limitation
}

try {
    foreach ($requiredPath in @(
        $sourceInput, $harnessDirectory, $requestDirectory, $dotnetRoot, $pythonRoot, $gitRoot,
        $nugetInput, $pythonPackagesInput, $historyInput, $resultDirectory, $launcherInput, $runnerInput, $requestInput, $configInput
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath)) { throw "A required read-only input or result mapping is unavailable." }
    }
    if (@(Get-ChildItem -LiteralPath $resultDirectory -Force -ErrorAction Stop).Count -ne 0) { throw "The writable result mapping was not empty at guest start." }
    $requestItem = Get-Item -LiteralPath $requestInput -Force
    if ($requestItem.Length -le 0 -or $requestItem.Length -gt 8192) { throw "The run request is outside its bounded size." }
    $request = Get-Content -Raw -LiteralPath $requestInput -Encoding UTF8 | ConvertFrom-Json
    Assert-RunRequest -RunRequest $request
    $phase = $request.phase

    $result.run_id = $request.run_id
    $result.phase = $request.phase
    $result.networking_enabled = $request.networking_enabled
    $result.network_reason = $request.network_reason
    $result.nuget_audit_enabled = $request.nuget_audit_enabled
    $result.source_history_included = $request.source_history_included
    $result.history_bundle_sha256 = $request.history_bundle_sha256
    $result.expected_head_commit = $request.expected_head_commit
    $result.expected_tracked_clean = $request.expected_tracked_clean
    $result.repository_status_sha256 = $request.repository_status_sha256
    $result.launcher_sha256 = $request.launcher_sha256
    $result.runner_sha256 = $request.runner_sha256
    $result.sandbox_config_sha256 = $request.sandbox_config_sha256
    $result.nuget_closure_sha256 = $request.nuget_closure_sha256
    $result.nuget_package_count = $request.nuget_package_count
    $result.python_packages_sha256 = $request.python_packages_sha256

    if ((Get-FileHash -LiteralPath $launcherInput -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.launcher_sha256 -or
        (Get-FileHash -LiteralPath $runnerInput -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.runner_sha256) { throw "The mapped harness digest does not match the request." }
    Assert-SandboxConfiguration -Path $configInput -RunRequest $request
    if (Test-Path -LiteralPath (Join-Path $sourceInput ".git")) { throw "Git history is prohibited from the guest source mapping." }
    Assert-BoundedTree -Path $sourceInput -MaximumFiles $MaxSourceFiles -MaximumBytes $MaxSourceBytes

    New-Item -ItemType Directory -Path $stateDirectory | Out-Null
    Invoke-Robocopy -Source $sourceInput -Destination $workDirectory
    Assert-BoundedTree -Path $workDirectory -MaximumFiles $MaxSourceFiles -MaximumBytes $MaxSourceBytes
    $sourceDigest = Get-SourceDigest -Root $workDirectory
    $result.source_digest = $sourceDigest
    if ($sourceDigest -ne $request.expected_source_digest) { $failureCode = "source_binding_failed"; throw "The guest-local source digest does not match the host snapshot." }
    Assert-NuGetClosure -Path $nugetInput -RunRequest $request
    Assert-PythonPackages -Path $pythonPackagesInput -RunRequest $request
    Assert-HistoryBundle -Path $historyInput -RunRequest $request

    $dotnetExecutable = Join-Path $dotnetRoot "dotnet.exe"
    $pythonExecutable = Join-Path $pythonRoot "python.exe"
    $gitExecutable = Join-Path $gitRoot "cmd\git.exe"
    $env:PATH = "$dotnetRoot;$pythonRoot;$gitRoot\cmd;$gitRoot\mingw64\bin;$env:PATH"
    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_CLI_HOME = Join-Path $stateDirectory "dotnet-home"
    $env:NUGET_PACKAGES = Join-Path $stateDirectory "nuget-packages"
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $stateDirectory "nuget-http"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
    $env:NUGET_XMLDOC_MODE = "skip"
    $env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
    $env:PIP_NO_INPUT = "1"
    $env:GIT_CONFIG_NOSYSTEM = "1"
    $env:GIT_CONFIG_GLOBAL = "NUL"
    $env:GIT_TERMINAL_PROMPT = "0"

    $dotnetVersionCommand = Invoke-BoundedCommand -Label "dotnet-version" -FilePath $dotnetExecutable -Arguments @("--version")
    $dotnetVersionLines = @($dotnetVersionCommand.tail_text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match "^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$" })
    if ($dotnetVersionCommand.exit_code -ne 0 -or $dotnetVersionCommand.timed_out -or $dotnetVersionCommand.output_exceeded -or $dotnetVersionLines.Count -ne 1) { throw "The mapped .NET SDK could not be resolved safely." }
    $dotnetVersion = $dotnetVersionLines[0]
    $gitVersionCommand = Invoke-BoundedCommand -Label "git-version" -FilePath $gitExecutable -Arguments @("--version")
    $gitVersionLines = @($gitVersionCommand.tail_text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match "^git version \d+\.\d+\.\d+(?:\.windows\.\d+)?$" })
    if ($gitVersionCommand.exit_code -ne 0 -or $gitVersionCommand.timed_out -or $gitVersionCommand.output_exceeded -or $gitVersionLines.Count -ne 1) { throw "The mapped Git runtime could not be resolved safely." }
    $gitText = $gitVersionLines[0]
    $gitText -match "^git version (\d+\.\d+\.\d+(?:\.windows\.\d+)?)$" | Out-Null
    $gitVersion = $Matches[1]

    if ($request.source_history_included) {
        $bundlePath = Join-Path $historyInput "repository-history.bundle"
        Invoke-RequiredCommand -Label "git-init-sanitized-history" -FilePath $gitExecutable -Arguments @(
            "-c", "init.templateDir=", "-C", $workDirectory, "init", "--quiet"
        ) -Failure "source_binding_failed"
        Invoke-RequiredCommand -Label "git-fetch-exact-history" -FilePath $gitExecutable -Arguments @(
            "-C", $workDirectory, "fetch", "--quiet", "--no-tags", $bundlePath, "HEAD"
        ) -Failure "source_binding_failed"
        Invoke-RequiredCommand -Label "git-bind-history-head" -FilePath $gitExecutable -Arguments @(
            "-C", $workDirectory, "update-ref", "refs/heads/tl0102-snapshot", "FETCH_HEAD"
        ) -Failure "source_binding_failed"
        Invoke-RequiredCommand -Label "git-select-history-head" -FilePath $gitExecutable -Arguments @(
            "-C", $workDirectory, "symbolic-ref", "HEAD", "refs/heads/tl0102-snapshot"
        ) -Failure "source_binding_failed"
        Invoke-RequiredCommand -Label "git-index-exact-history" -FilePath $gitExecutable -Arguments @(
            "-C", $workDirectory, "reset", "--mixed", "--quiet", $request.expected_head_commit
        ) -Failure "source_binding_failed"
        Invoke-RequiredCommand -Label "git-verify-history-head" -FilePath $gitExecutable -Arguments @(
            "-C", $workDirectory, "cat-file", "-e", "$($request.expected_head_commit)^{commit}"
        ) -Failure "source_binding_failed"
        $gitDirectory = Join-Path $workDirectory ".git"
        Assert-BoundedTree -Path $gitDirectory -MaximumFiles $MaxHistoryFiles -MaximumBytes $MaxHistoryBytes
        $gitConfig = Join-Path $gitDirectory "config"
        $gitConfigItem = Get-Item -LiteralPath $gitConfig -Force -ErrorAction Stop
        $gitConfigText = Get-Content -Raw -LiteralPath $gitConfig -Encoding UTF8
        if ($gitConfigItem.Length -gt 4096 -or $gitConfigText -match "(?im)^\s*\[(?:remote|credential|include|url)\b|^\s*(?:url|remote|hooksPath|include|credential)\s*=|https?://|[A-Za-z]:\\Users\\") { throw "The guest-created Git configuration is not sanitized." }
        if (Test-Path -LiteralPath (Join-Path $gitDirectory "hooks")) {
            $hookItems = @(Get-ChildItem -LiteralPath (Join-Path $gitDirectory "hooks") -Force -ErrorAction Stop)
            if ($hookItems.Count -ne 0) { throw "The guest-created Git repository unexpectedly contains hooks." }
        }
    }

    $nugetConfig = Join-Path $stateDirectory "NuGet.Config"
    Write-SandboxNuGetConfig -Path $nugetConfig -AuditEnabled:$request.nuget_audit_enabled

    Push-Location $workDirectory
    try {
        if ($phase -in @("Quick", "Full")) {
            $pythonVersionCommand = Invoke-BoundedCommand -Label "python-version" -FilePath $pythonExecutable -Arguments @("-c", "import platform; print(platform.python_version())") -SuccessMarker "3.14.7"
            if ($pythonVersionCommand.exit_code -ne 0 -or -not $pythonVersionCommand.marker_found) { $failureCode = "python_bootstrap_failed"; throw "The mapped Python runtime is not the governed version." }
            $pythonVersion = "3.14.7"
            $venvDirectory = Join-Path $workDirectory ".venv"
            Invoke-RequiredCommand -Label "python-venv" -FilePath $pythonExecutable -Arguments @("-m", "venv", $venvDirectory) -Failure "python_bootstrap_failed"
            $venvPython = Join-Path $venvDirectory "Scripts\python.exe"
            Invoke-RequiredCommand -Label "python-offline-install" -FilePath $venvPython -Arguments @(
                "-m", "pip", "install", "--no-index", "--find-links", $pythonPackagesInput,
                "--require-hashes", "--requirement", "tools\requirements.txt"
            ) -Failure "python_bootstrap_failed"
            Invoke-RequiredCommand -Label "python-package-version" -FilePath $venvPython -Arguments @("-c", "import yaml; print(yaml.__version__)") -Failure "python_bootstrap_failed" -SuccessMarker "6.0.3"
        }

        if ($phase -in @("RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Targeted")) {
            $persistenceProject = "tests\ThirdLife.Persistence.Tests\ThirdLife.Persistence.Tests.csproj"
            Invoke-RequiredCommand -Label "locked-offline-restore" -FilePath $dotnetExecutable -Arguments @(
                "restore", $persistenceProject, "--locked-mode", "--configfile", $nugetConfig, "--nologo", "-p:NuGetAudit=false"
            ) -Failure "restore_failed"
            if ($phase -eq "Targeted") {
                $coreProject = "tests\ThirdLife.Core.Tests\ThirdLife.Core.Tests.csproj"
                Invoke-RequiredCommand -Label "locked-offline-core-restore" -FilePath $dotnetExecutable -Arguments @(
                    "restore", $coreProject, "--locked-mode", "--configfile", $nugetConfig, "--nologo", "-p:NuGetAudit=false"
                ) -Failure "restore_failed"
                Invoke-RequiredCommand -Label "core-tests" -FilePath $dotnetExecutable -Arguments @(
                    "test", $coreProject, "--configuration", "Release", "--no-restore", "--nologo"
                ) -Failure "test_failed" -SuccessMarker "Passed!"
            }
            $filter = switch ($phase) {
                "RoundTrip" { "FullyQualifiedName~SqliteJobStoreIntegrationTests.CreateCloseReopenArchiveAndRestorePreserveCommittedState" }
                "Integration" { "FullyQualifiedName~SqliteJobStoreIntegrationTests" }
                "Migration" { "FullyQualifiedName~MigrationAndCorruptionTests" }
                "PathSecurity" { "FullyQualifiedName~PathSecurityTests" }
                "Interruption" { "FullyQualifiedName~TransactionInterruptionTests" }
                default { $null }
            }
            $testArguments = @("test", $persistenceProject, "--configuration", "Release", "--no-restore", "--nologo")
            if ($null -ne $filter) { $testArguments += @("--filter", $filter) }
            Invoke-RequiredCommand -Label "persistence-tests" -FilePath $dotnetExecutable -Arguments $testArguments -Failure "test_failed" -SuccessMarker "Passed!"
        }
        elseif ($phase -eq "Quick") {
            Invoke-RequiredCommand -Label "governed-quick" -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "eng\verify.ps1", "-Tier", "Quick"
            ) -Failure "verification_failed" -SuccessMarker "OK: ThirdLife quick documentation/schema/static verification passed."
        }
        else {
            Invoke-RequiredCommand -Label "governed-quick-before-full" -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "eng\verify.ps1", "-Tier", "Quick"
            ) -Failure "verification_failed" -SuccessMarker "OK: ThirdLife quick documentation/schema/static verification passed."
            Invoke-RequiredCommand -Label "locked-full-restore-and-audit" -FilePath $dotnetExecutable -Arguments @(
                "restore", "ThirdLife.sln", "--locked-mode", "--configfile", $nugetConfig, "--nologo",
                "-p:NuGetAudit=true", "-p:NuGetAuditMode=all"
            ) -Failure "restore_failed"
            Invoke-RequiredCommand -Label "full-format" -FilePath $dotnetExecutable -Arguments @(
                "format", "ThirdLife.sln", "--verify-no-changes", "--no-restore"
            ) -Failure "verification_failed"
            Invoke-RequiredCommand -Label "full-build" -FilePath $dotnetExecutable -Arguments @(
                "build", "ThirdLife.sln", "--configuration", "Release", "--no-restore", "--warnaserror", "--nologo"
            ) -Failure "verification_failed" -SuccessMarker "Build succeeded."
            Invoke-RequiredCommand -Label "full-tests" -FilePath $dotnetExecutable -Arguments @(
                "test", "ThirdLife.sln", "--configuration", "Release", "--no-build", "--no-restore", "--nologo"
            ) -Failure "test_failed" -SuccessMarker "Passed!"
        }
    }
    finally { Pop-Location }

    $exitCode = 0
    $failureCode = "none"
    $resultState = "passed"
}
catch {
    $resultState = "failed"
    if ($failureCode -eq "none") { $failureCode = "preflight_failed" }
    Write-Host "FAILED - the TL-0102 guest phase stopped safely with bounded diagnostics."
}
finally {
    try {
        if ($null -ne $request -and (Test-Path -LiteralPath $workDirectory -PathType Container)) {
            try {
                $sourceUnchangedAfter = (Get-SourceDigest -Root $workDirectory) -eq $request.expected_source_digest
            }
            catch { $sourceUnchangedAfter = $false }
        }
        $rawFiles = @(Get-ChildItem -LiteralPath $stateDirectory -File -Filter "command-*" -ErrorAction SilentlyContinue)
        if ($rawFiles.Count -ne 0) {
            $rawFiles | Remove-Item -Force -ErrorAction SilentlyContinue
            $rawFiles = @(Get-ChildItem -LiteralPath $stateDirectory -File -Filter "command-*" -ErrorAction SilentlyContinue)
        }
        $rawOutputRetained = $rawFiles.Count -ne 0
        if ($resultState -eq "passed" -and (-not $sourceUnchangedAfter -or $rawOutputRetained)) {
            $resultState = "failed"
            $exitCode = 3
            $failureCode = "postflight_failed"
        }

        if (-not $commandTreeTerminationUnverified) {
            $finishedAt = [DateTimeOffset]::UtcNow
            $result.result = $resultState
            $result.exit_code = [int]$exitCode
            $result.finished_utc = $finishedAt.UtcDateTime.ToString("o")
            $result.duration_seconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
            $result.source_digest = $sourceDigest
            $result.source_unchanged_after = $sourceUnchangedAfter
            $result.dotnet_sdk = $dotnetVersion
            $result.python_version = $pythonVersion
            $result.git_version = $gitVersion
            $result.failure_code = $failureCode
            $result.output_truncated = $anyOutputTruncated
            $result.raw_output_retained = $rawOutputRetained
            $result.output_tail = @($diagnosticTail)
            $json = $result | ConvertTo-Json -Depth 5 -Compress
            while ([System.Text.Encoding]::UTF8.GetByteCount($json) -gt $ResultLimitBytes -and $diagnosticTail.Count -gt 0) {
                $diagnosticTail.RemoveAt(0)
                $result.output_tail = @($diagnosticTail)
                $result.output_truncated = $true
                $json = $result | ConvertTo-Json -Depth 5 -Compress
            }
            if ([System.Text.Encoding]::UTF8.GetByteCount($json) -gt $ResultLimitBytes) { throw "The structured Sandbox result exceeds its bounded schema." }
            Write-Utf8CreateNew -Path $resultPath -Content $json
            Write-Utf8CreateNew -Path $completionMarkerPath -Content "complete`n"
        }
    }
    finally {
        if ($sandboxMappedInvocation -and $sandboxIdentityVerified) {
            Start-Process -FilePath "$env:WINDIR\System32\shutdown.exe" -ArgumentList @("/s", "/t", "2", "/f") -WindowStyle Hidden | Out-Null
        }
    }
}
