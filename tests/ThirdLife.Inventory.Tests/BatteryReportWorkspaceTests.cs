using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class BatteryReportWorkspaceTests(ITestOutputHelper output)
{
    [Fact]
    public void RegisteredRootUsesTheResolvedKnownFolderTargetAndOnlyFixedChildren()
    {
        const string target = @"C:\Synthetic\Packages\Example\LocalCache\Local";
        Assert.Equal(@"C:\Synthetic\Packages\Example\LocalCache\Local\ThirdLife\SetupCore\BatteryReports",
            BatteryReportWorkspace.FromKnownFolderBase(target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData(@"\\synthetic-server\share")]
    public void MissingOrNonlocalKnownFolderNeverFallsBackToTheWorkingDirectory(string? path)
    {
        Assert.Throws<BatteryReportWorkspaceException>(() => BatteryReportWorkspace.FromKnownFolderBase(path));
    }

    [Fact]
    public void KnownFolderBindingRequiresOneIdentityAtBothFixedSuffixLocations()
    {
        var identity = new BatteryReportFileIdentity(1, 42);
        BatteryReportWorkspace.ValidateKnownFolderBinding(
            @"C:\Synthetic\Local\ThirdLife\SetupCore\BatteryReports",
            @"C:\Synthetic\Private\ThirdLife\SetupCore\BatteryReports", identity, identity, identity);
    }

    [Theory]
    [InlineData(@"C:\Synthetic\ThirdLife\SetupCore\Other")]
    [InlineData(@"C:\Synthetic\Other\SetupCore\BatteryReports")]
    [InlineData(@"\\synthetic-server\share\ThirdLife\SetupCore\BatteryReports")]
    [InlineData(@"C:\Synthetic\..\ThirdLife\SetupCore\BatteryReports")]
    [InlineData(@"C:\Synthetic/../ThirdLife\SetupCore\BatteryReports")]
    public void KnownFolderBindingRejectsUnsafeRequestedOrCanonicalLocations(string invalid)
    {
        const string valid = @"C:\Synthetic\ThirdLife\SetupCore\BatteryReports";
        var identity = new BatteryReportFileIdentity(1, 42);
        Assert.Throws<BatteryReportWorkspaceException>(() =>
            BatteryReportWorkspace.ValidateKnownFolderBinding(valid, invalid, identity, identity, identity));
        Assert.Throws<BatteryReportWorkspaceException>(() =>
            BatteryReportWorkspace.ValidateKnownFolderBinding(invalid, valid, identity, identity, identity));
    }

    [Theory]
    [InlineData(1U, 42UL, 1U, 43UL, 1U, 42UL)]
    [InlineData(1U, 42UL, 1U, 42UL, 2U, 42UL)]
    [InlineData(1U, 0UL, 1U, 0UL, 1U, 0UL)]
    public void KnownFolderBindingRejectsChangedOrMissingPinnedIdentity(uint volume, ulong index,
        uint aliasVolume, ulong aliasIndex, uint canonicalVolume, ulong canonicalIndex)
    {
        const string requested = @"C:\Synthetic\Local\ThirdLife\SetupCore\BatteryReports";
        const string canonical = @"C:\Synthetic\Private\ThirdLife\SetupCore\BatteryReports";
        Assert.Throws<BatteryReportWorkspaceException>(() => BatteryReportWorkspace.ValidateKnownFolderBinding(
            requested, canonical, new(volume, index), new(aliasVolume, aliasIndex), new(canonicalVolume, canonicalIndex)));
    }

    [Fact]
    public void OwnedWriterCanTruncateTheGuardedReportAndCleanupRemovesOnlyItsArtifacts()
    {
        using var root = new FixtureRoot();
        using var workspace = new BatteryReportWorkspace(root.Path);
        var report = System.IO.Path.Combine(workspace.DirectoryPath, workspace.ReportFileName);
        Assert.True(BatteryReportWorkspace.IsReportFileName(workspace.ReportFileName));
        using (var writer = new FileStream(report, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            writer.Write("<synthetic/>"u8);
        }

        Assert.Equal(12, workspace.ReportLength);
        using (var read = workspace.OpenReport()) { Assert.Equal(12, read.Length); }
        Assert.True(workspace.Cleanup().Complete);
        Assert.True(workspace.Cleanup().Complete);
        Assert.Equal([BatteryReportWorkspace.LockName], Directory.EnumerateFileSystemEntries(root.Path).Select(System.IO.Path.GetFileName));
    }

    [Fact]
    public void RootAndReportAreProtectedForOnlyTheCurrentUserSystemAndAdministrators()
    {
        using var root = new FixtureRoot();
        using var workspace = new BatteryReportWorkspace(root.Path);
        using var identity = WindowsIdentity.GetCurrent();
        var expected = new HashSet<SecurityIdentifier>
        {
            identity.User!,
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        var directorySecurity = new DirectoryInfo(root.Path).GetAccessControl();
        var fileSecurity = new FileInfo(System.IO.Path.Combine(root.Path, workspace.ReportFileName)).GetAccessControl();
        foreach (FileSystemSecurity security in new FileSystemSecurity[] { directorySecurity, fileSecurity })
        {
            Assert.True(security.AreAccessRulesProtected);
            Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
            {
                Assert.Contains((SecurityIdentifier)rule.IdentityReference, expected);
                Assert.False(rule.IsInherited);
                Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            });
        }

        Assert.True(workspace.Cleanup().Complete);
    }

    [Fact]
    public void ExclusiveLockPreventsAnotherWorkspaceFromRecoveringAnActiveOperation()
    {
        using var root = new FixtureRoot();
        using var first = new BatteryReportWorkspace(root.Path);
        var error = Assert.Throws<BatteryReportWorkspaceException>(() => new BatteryReportWorkspace(root.Path));
        Assert.False(error.CleanupIncomplete);
        Assert.Equal(32U, error.NativeErrorCode);
        Assert.True(first.Cleanup().Complete);
    }

    [Fact]
    public void NextOperationRecoversOnlyTheDurablyRecordedPreviousReport()
    {
        using var root = new FixtureRoot();
        string prior;
        using (var interrupted = new BatteryReportWorkspace(root.Path))
        {
            prior = System.IO.Path.Combine(root.Path, interrupted.ReportFileName);
            File.WriteAllText(prior, "synthetic private battery bytes", Encoding.UTF8);
        }

        using var next = new BatteryReportWorkspace(root.Path);
        Assert.False(File.Exists(prior));
        Assert.True(next.Cleanup().Complete);
        Assert.Single(Directory.EnumerateFileSystemEntries(root.Path));
    }

    [Theory]
    [InlineData("corrupt_record")]
    [InlineData("unregistered_file")]
    [InlineData("replaced_report")]
    [InlineData("extra_file")]
    public void UncertainOwnedStateIsPreservedAndBlocksAnotherCapture(string defect)
    {
        using var root = new FixtureRoot();
        string report;
        using (var interrupted = new BatteryReportWorkspace(root.Path))
        {
            report = System.IO.Path.Combine(root.Path, interrupted.ReportFileName);
        }

        var ownership = System.IO.Path.Combine(root.Path, BatteryReportWorkspace.OwnershipName);
        switch (defect)
        {
            case "corrupt_record": File.WriteAllText(ownership, "bad", Encoding.UTF8); break;
            case "unregistered_file": File.Delete(ownership); break;
            case "replaced_report": File.Delete(report); File.WriteAllText(report, "replacement sentinel", Encoding.UTF8); break;
            case "extra_file": File.WriteAllText(System.IO.Path.Combine(root.Path, "unknown.bin"), "sentinel", Encoding.UTF8); break;
        }

        var error = Assert.Throws<BatteryReportWorkspaceException>(() => new BatteryReportWorkspace(root.Path));
        Assert.True(error.CleanupIncomplete);
        Assert.True(File.Exists(report));
    }

    [Fact]
    public void HardLinkedReportIsNeverDeletedThroughRecovery()
    {
        using var root = new FixtureRoot();
        string report;
        using (var interrupted = new BatteryReportWorkspace(root.Path))
        {
            report = System.IO.Path.Combine(root.Path, interrupted.ReportFileName);
        }

        var alias = root.Path + ".alias";
        Assert.True(CreateHardLinkW(alias, report, 0));
        var error = Assert.Throws<BatteryReportWorkspaceException>(() => new BatteryReportWorkspace(root.Path));
        Assert.True(error.CleanupIncomplete);
        Assert.True(File.Exists(report));
        Assert.True(File.Exists(alias));
    }

    [Fact]
    public void BroadenedDirectoryAclBlocksCaptureWithoutChangingTheAcl()
    {
        using var root = new FixtureRoot();
        using (var initial = new BatteryReportWorkspace(root.Path)) { Assert.True(initial.Cleanup().Complete); }
        var directory = new DirectoryInfo(root.Path);
        var security = directory.GetAccessControl();
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
        var error = Assert.Throws<BatteryReportWorkspaceException>(() => new BatteryReportWorkspace(root.Path));
        Assert.True(error.CleanupIncomplete);
        Assert.Contains(directory.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.WorldSid, null));
    }

    [Fact]
    public void CleanupRefusesAConcurrentWriterAndThenSucceedsAfterWriterCloses()
    {
        using var root = new FixtureRoot();
        using var workspace = new BatteryReportWorkspace(root.Path);
        using (var writer = new FileStream(System.IO.Path.Combine(root.Path, workspace.ReportFileName), FileMode.Open,
            FileAccess.Write, FileShare.ReadWrite))
        {
            var cleanup = workspace.Cleanup();
            Assert.False(cleanup.Complete);
            Assert.Equal(32U, cleanup.NativeErrorCode);
        }

        Assert.True(workspace.Cleanup().Complete);
    }

    [Fact]
    public void FutureOwnershipTimestampCannotAuthorizeCleanupAfterClockRollback()
    {
        using var root = new FixtureRoot();
        string report;
        using (var interrupted = new BatteryReportWorkspace(root.Path))
        {
            report = System.IO.Path.Combine(root.Path, interrupted.ReportFileName);
        }

        using (var ownership = new FileStream(System.IO.Path.Combine(root.Path, BatteryReportWorkspace.OwnershipName),
            FileMode.Open, FileAccess.Write, FileShare.None))
        {
            ownership.Position = 36;
            using var writer = new BinaryWriter(ownership, Encoding.UTF8, leaveOpen: true);
            writer.Write(DateTimeOffset.UtcNow.AddDays(1).UtcTicks);
        }

        var error = Assert.Throws<BatteryReportWorkspaceException>(() => new BatteryReportWorkspace(root.Path));
        Assert.True(error.CleanupIncomplete);
        Assert.True(File.Exists(report));
    }

    [Fact]
    public void AncestorJunctionIsRejectedBeforeCreatingAnyDescendant()
    {
        using var target = new FixtureRoot();
        using (var initialize = new BatteryReportWorkspace(target.Path)) { Assert.True(initialize.Cleanup().Complete); }
        var junction = target.Path + ".junction";
        Directory.CreateDirectory(junction);
        try
        {
            CreateJunction(junction, target.Path);
            var nested = System.IO.Path.Combine(junction, "uncreated-child");
            var error = Assert.Throws<BatteryReportWorkspaceException>(() => new BatteryReportWorkspace(nested));
            Assert.True(error.CleanupIncomplete);
            Assert.False(Directory.Exists(System.IO.Path.Combine(target.Path, "uncreated-child")));
            Assert.Single(Directory.EnumerateFileSystemEntries(target.Path));
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
        }
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ActiveMachineProductionSourceHasBoundedSanitizedMetricsAndVerifiedCleanup()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var beforeMemory = process.PrivateMemorySize64;
        var beforeHandles = process.HandleCount;
        var beforeCpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var result = new WindowsBatteryReportSource().Read(CancellationToken.None);
        watch.Stop();
        process.Refresh();
        output.WriteLine(FormattableString.Invariant(
            $"Production source status: {result.Status}; exit code: {result.ExitCode}; native error: {result.NativeErrorCode}; elapsed milliseconds: {watch.Elapsed.TotalMilliseconds:F3}; CPU milliseconds: {(process.TotalProcessorTime - beforeCpu).TotalMilliseconds:F3}; private growth bytes: {Math.Max(0, process.PrivateMemorySize64 - beforeMemory)}; handle growth: {Math.Max(0, process.HandleCount - beforeHandles)}; cleanup complete: {result.CleanupComplete}."));
        Assert.True(result.CleanupComplete);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ActiveMachineFixedPowercfgReportHasVerifiedCleanupAndNoRawOutput()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var isElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        output.WriteLine($"Fixed operation elevated: {isElevated}.");
        Assert.False(isElevated);
        using var root = new FixtureRoot();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var beforeMemory = process.PrivateMemorySize64;
        var beforeHandles = process.HandleCount;
        var beforeCpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var environment = new ActiveEnvironment(root.Path);
        var source = new WindowsBatteryReportSource(environment, TimeProvider.System);
        var result = source.Read(CancellationToken.None);
        watch.Stop();
        process.Refresh();
        root.Preserve = !result.CleanupComplete;
        output.WriteLine(FormattableString.Invariant($"Fixed operation status: {result.Status}; exit code: {result.ExitCode}; native error: {result.NativeErrorCode}; elapsed milliseconds: {watch.Elapsed.TotalMilliseconds:F3}; CPU milliseconds: {(process.TotalProcessorTime - beforeCpu).TotalMilliseconds:F3}; private growth bytes: {Math.Max(0, process.PrivateMemorySize64 - beforeMemory)}; handle growth: {Math.Max(0, process.HandleCount - beforeHandles)}; cleanup complete: {result.CleanupComplete}; parsed battery count: {result.Report?.Batteries.Count}; observed completed report bytes: {environment.CompletedReportBytes}."));
        if (result.Report is not null)
        {
            foreach (var battery in result.Report.Batteries)
            {
                output.WriteLine(FormattableString.Invariant($"Battery ordinal: {battery.Ordinal}; design capacity: {battery.DesignCapacityMwh.Status}; full charge capacity: {battery.FullChargeCapacityMwh.Status}; cycle count: {battery.CycleCount.Status}."));
            }
        }
        Assert.True(result.CleanupComplete);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ActiveMachineCancellationTerminatesTheOwnedJobAndVerifiesCleanup()
    {
        using var root = new FixtureRoot();
        using var cancellation = new CancellationTokenSource();
        var source = new WindowsBatteryReportSource(new ActiveEnvironment(root.Path, cancellation), TimeProvider.System);
        var watch = Stopwatch.StartNew();
        var result = source.Read(cancellation.Token);
        watch.Stop();
        root.Preserve = !result.CleanupComplete;
        output.WriteLine(FormattableString.Invariant($"Fixed operation cancelled immediately after native start: status {result.Status}; cleanup complete {result.CleanupComplete}; native error {result.NativeErrorCode}; elapsed milliseconds {watch.Elapsed.TotalMilliseconds:F3}."));
        Assert.True(result.CleanupComplete);
        Assert.Equal(BatteryReportSourceStatus.Cancelled, result.Status);
        Assert.Null(result.Report);
        Assert.Equal([BatteryReportWorkspace.LockName], Directory.EnumerateFileSystemEntries(root.Path).Select(System.IO.Path.GetFileName));
    }

    private sealed class ActiveEnvironment(string root, CancellationTokenSource? cancelAfterStart = null) : IBatteryReportEnvironment
    {
        internal long? CompletedReportBytes { get; private set; }
        public bool HasNoSystemBattery() => false;
        public IBatteryReportWorkspace CreateWorkspace() => new ObservedWorkspace(new BatteryReportWorkspace(root), this);
        public IBatteryReportProcess StartProcess(IBatteryReportWorkspace workspace)
        {
            var process = BatteryReportProcess.LaunchFixedReport(workspace.DirectoryPath, workspace.ReportFileName);
            cancelAfterStart?.Cancel();
            return process;
        }

        private sealed class ObservedWorkspace(BatteryReportWorkspace workspace, ActiveEnvironment environment) : IBatteryReportWorkspace
        {
            public string DirectoryPath => workspace.DirectoryPath;
            public string ReportFileName => workspace.ReportFileName;
            public long ReportLength => workspace.ReportLength;
            public Stream OpenReport()
            {
                var stream = workspace.OpenReport();
                environment.CompletedReportBytes = stream.Length;
                return stream;
            }
            public BatteryReportCleanup Cleanup() => workspace.Cleanup();
            public void Dispose() => workspace.Dispose();
        }
    }

    internal static void CreateJunction(string link, string target)
    {
        var substitute = Encoding.Unicode.GetBytes("\\??\\" + target);
        var printName = Encoding.Unicode.GetBytes(target);
        var data = new byte[16 + substitute.Length + printName.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xA0000003);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), checked((ushort)(data.Length - 8)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), checked((ushort)substitute.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), checked((ushort)(substitute.Length + 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), checked((ushort)printName.Length));
        substitute.CopyTo(data, 16);
        printName.CopyTo(data, 18 + substitute.Length);
        using var handle = CreateFileW(link, 0x40000000, 0, 0, 3, 0x02200000, 0);
        Assert.False(handle.IsInvalid);
        Assert.True(DeviceIoControl(handle, 0x000900A4, data, (uint)data.Length, 0, 0, out _, 0));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint controlCode, byte[] input, uint inputSize,
        nint outputBuffer, uint outputSize, out uint returned, nint overlapped);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, nint security);

    private sealed class FixtureRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ThirdLife-Battery-" + Guid.NewGuid().ToString("N"));
        internal bool Preserve { get; set; }
        public void Dispose()
        {
            if (Preserve) { return; }
            if (File.Exists(Path + ".alias")) { File.Delete(Path + ".alias"); }
            if (!Directory.Exists(Path)) { return; }
            foreach (var file in Directory.EnumerateFiles(Path)) { File.Delete(file); }
            Directory.Delete(Path, recursive: false);
        }
    }
}
