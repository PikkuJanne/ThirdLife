using System.Text;
using System.Text.Json;
using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class BatteryReportSourceTests
{
    private const string ValidXml = "<BatteryReport xmlns=\"http://schemas.microsoft.com/battery/2012\"><ReportInformation><ReportVersion>1.1</ReportVersion></ReportInformation><Batteries><Battery><RelativeCapacity>0</RelativeCapacity><DesignCapacity>50000</DesignCapacity><FullChargeCapacity>40000</FullChargeCapacity><CycleCount>123</CycleCount></Battery></Batteries></BatteryReport>";

    [Fact]
    public void SuccessfulCollectionParsesBeforeCleanupAndReturnsOnlyTypedValues()
    {
        var fixture = new Fixture();
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.Collected, result.Status);
        Assert.Equal(50000U, Assert.Single(result.Report!.Batteries).DesignCapacityMwh.GetRequiredValue());
        Assert.Equal(0U, result.ExitCode);
        Assert.True(result.CleanupComplete);
        Assert.Equal(["workspace", "power", "start", "wait", "open", "process_dispose", "cleanup", "workspace_dispose"], fixture.Calls);
        Assert.DoesNotContain("fixture-sensitive", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitNoBatteryStillRecoversOwnedWorkspaceWithoutRunningCommand()
    {
        var fixture = new Fixture { NoBattery = true };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.NoBattery, result.Status);
        Assert.Null(result.Report);
        Assert.Null(result.ExitCode);
        Assert.Equal(["workspace", "power", "cleanup", "workspace_dispose"], fixture.Calls);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(8, false)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    [InlineData(255, false)]
    public void UnknownPowerStatusCannotBecomeNoBattery(byte flag, bool absent) =>
        Assert.Equal(absent, WindowsBatteryReportEnvironment.IsExplicitNoBattery(flag));

    [Fact]
    public void NonzeroProcessExitIsFailureEvenIfReportLooksValid()
    {
        var fixture = new Fixture { ExitCode = uint.MaxValue };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.Failed, result.Status);
        Assert.Equal(uint.MaxValue, result.ExitCode);
        Assert.Null(result.Report);
        Assert.DoesNotContain("open", fixture.Calls);
        Assert.Contains("cleanup", fixture.Calls);
    }

    [Theory]
    [InlineData("<malformed")]
    [InlineData("")]
    public void InvalidReportIsCleanedWithoutRawFallback(string xml)
    {
        var fixture = new Fixture { Xml = xml };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.InvalidData, result.Status);
        Assert.True(result.CleanupComplete);
        Assert.Contains("cleanup", fixture.Calls);
    }

    [Fact]
    public void TimeoutTerminatesOwnedJobBeforeArtifactCleanup()
    {
        var fixture = new Fixture();
        fixture.OnWait = () => { fixture.Clock.Advance(TimeSpan.FromSeconds(21)); return false; };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.TimedOut, result.Status);
        Assert.Null(result.Report);
        Assert.True(result.CleanupComplete);
        Assert.True(fixture.Calls.IndexOf("terminate") < fixture.Calls.IndexOf("cleanup"));
    }

    [Fact]
    public void CancellationTerminatesOwnedJobAndCleansWithoutUsingCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.OnWait = () => { cancellation.Cancel(); return false; };
        var result = fixture.Source.Read(cancellation.Token);
        Assert.Equal(BatteryReportSourceStatus.Cancelled, result.Status);
        Assert.True(result.CleanupComplete);
        Assert.Contains("terminate", fixture.Calls);
        Assert.Contains("cleanup", fixture.Calls);
    }

    [Fact]
    public void PreCancelledReadCreatesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fixture = new Fixture();
        Assert.Throws<OperationCanceledException>(() => fixture.Source.Read(cancellation.Token));
        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public void OversizedOutputTerminatesWriterAndWithholdsAllReportFacts()
    {
        var fixture = new Fixture { ReportLength = BatteryReportParser.MaximumReportBytes + 1L };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.InvalidData, result.Status);
        Assert.Contains("terminate", fixture.Calls);
        Assert.DoesNotContain("open", fixture.Calls);
        Assert.True(result.CleanupComplete);
    }

    [Theory]
    [InlineData(5U, (int)BatteryReportSourceStatus.AccessDenied)]
    [InlineData(0x80070005U, (int)BatteryReportSourceStatus.AccessDenied)]
    [InlineData(1260U, (int)BatteryReportSourceStatus.Failed)]
    public void StartFailureKeepsNumericReasonAndCleansOwnedFiles(uint code, int expected)
    {
        var fixture = new Fixture { StartFailure = new BatteryReportNativeException(code) };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal((BatteryReportSourceStatus)expected, result.Status);
        Assert.Equal(code, result.NativeErrorCode);
        Assert.Contains("cleanup", fixture.Calls);
    }

    [Fact]
    public void DeniedWorkspaceIsAccessDeniedAndDoesNotAttemptExecution()
    {
        var fixture = new Fixture { WorkspaceFailure = new BatteryReportWorkspaceException(false, 5) };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.AccessDenied, result.Status);
        Assert.Equal(5U, result.NativeErrorCode);
        Assert.Equal(["workspace"], fixture.Calls);
    }

    [Fact]
    public void StartupCleanupUncertaintyPreservesArtifactsAndBlocksFutureInstances()
    {
        var gate = new BatteryReportExecutionGate();
        var fixture = new Fixture(gate) { StartFailure = new BatteryReportWorkspaceException(true, 5) };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.CleanupIncomplete, result.Status);
        Assert.False(result.CleanupComplete);
        Assert.DoesNotContain("cleanup", fixture.Calls);
        var next = new Fixture(gate);
        Assert.Equal(BatteryReportSourceStatus.CleanupIncomplete, next.Source.Read(CancellationToken.None).Status);
        Assert.Empty(next.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminationFailureNeverDeletesArtifactsOrAllowsRetry(bool throws)
    {
        var fixture = new Fixture { TerminationFails = true, TerminationThrows = throws };
        fixture.OnWait = () => { fixture.Clock.Advance(TimeSpan.FromSeconds(21)); return false; };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.CleanupIncomplete, result.Status);
        Assert.False(result.CleanupComplete);
        Assert.Null(result.Report);
        Assert.DoesNotContain("cleanup", fixture.Calls);
        Assert.Contains("process_dispose", fixture.Calls);
        Assert.Contains("workspace_dispose", fixture.Calls);
        var previousCount = fixture.Calls.Count;
        Assert.Equal(BatteryReportSourceStatus.CleanupIncomplete, fixture.Source.Read(CancellationToken.None).Status);
        Assert.Equal(previousCount, fixture.Calls.Count);
    }

    [Fact]
    public void ReportCleanupFailureRemovesPreviouslyParsedFactsAndBlocksRetry()
    {
        var fixture = new Fixture { CleanupFails = true };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.CleanupIncomplete, result.Status);
        Assert.Null(result.Report);
        Assert.Equal(32U, result.NativeErrorCode);
        Assert.False(result.CleanupComplete);
    }

    [Fact]
    public void SensitiveExceptionMessagesCannotEscapeSourceResults()
    {
        var fixture = new Fixture { OpenFailure = new IOException("fixture-sensitive person serial path") };
        var result = fixture.Source.Read(CancellationToken.None);
        Assert.Equal(BatteryReportSourceStatus.Failed, result.Status);
        Assert.DoesNotContain("fixture-sensitive", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Contains("cleanup", fixture.Calls);
    }

    [Fact]
    public async Task SharedGateKeepsAnotherSourceOutUntilCleanupCompletes()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var gate = new BatteryReportExecutionGate();
        var first = new Fixture(gate);
        first.OnWait = () => { entered.Set(); return release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None); };
        var work = Task.Run(() => first.Source.Read(CancellationToken.None), CancellationToken.None);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
            var second = new Fixture(gate);
            Assert.Equal(BatteryReportSourceStatus.Unavailable, second.Source.Read(CancellationToken.None).Status);
            Assert.Empty(second.Calls);
            release.Set();
            Assert.Equal(BatteryReportSourceStatus.Collected, (await work).Status);
            Assert.Equal(BatteryReportSourceStatus.Collected, second.Source.Read(CancellationToken.None).Status);
        }
        finally
        {
            release.Set();
            await work;
        }
    }

    [Theory]
    [InlineData("../report.xml")]
    [InlineData("C:\\report.xml")]
    [InlineData("report-00000000000000000000000000000000.xml /energy")]
    [InlineData("report-0000000000000000000000000000000g.xml")]
    public void FixedCommandRejectsUnregisteredOutputNames(string name)
    {
        Assert.False(BatteryReportWorkspace.IsReportFileName(name));
        Assert.Throws<BatteryReportWorkspaceException>(() => BatteryReportProcess.BuildCommandLine(name));
    }

    [Fact]
    public void FixedCommandContainsOnlyReviewedOptionsAndGeneratedRelativeName()
    {
        const string name = "report-0123456789abcdef0123456789abcdef.xml";
        Assert.Equal("powercfg.exe /batteryreport /xml /output " + name + " /duration 1\0",
            BatteryReportProcess.BuildCommandLine(name));
    }

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan amount) => _timestamp += amount.Ticks;
    }

    private sealed class Fixture : IBatteryReportEnvironment, IBatteryReportWorkspace, IBatteryReportProcess
    {
        internal Fixture(BatteryReportExecutionGate? gate = null)
        {
            Source = new(this, Clock, gate);
        }

        internal Clock Clock { get; } = new();
        internal WindowsBatteryReportSource Source { get; }
        internal List<string> Calls { get; } = [];
        internal bool NoBattery { get; init; }
        internal string Xml { get; init; } = ValidXml;
        internal Func<bool>? OnWait { get; set; }
        internal Exception? StartFailure { get; init; }
        internal Exception? WorkspaceFailure { get; init; }
        internal Exception? OpenFailure { get; init; }
        internal bool TerminationFails { get; init; }
        internal bool TerminationThrows { get; init; }
        internal bool CleanupFails { get; init; }
        public long ReportLength { get; init; } = 100;
        public uint ExitCode { get; init; }
        public string DirectoryPath => "fixture-sensitive-directory";
        public string ReportFileName => "report-0123456789abcdef0123456789abcdef.xml";

        public bool HasNoSystemBattery() { Calls.Add("power"); return NoBattery; }
        public IBatteryReportWorkspace CreateWorkspace()
        {
            Calls.Add("workspace");
            if (WorkspaceFailure is not null) { throw WorkspaceFailure; }
            return this;
        }
        public IBatteryReportProcess StartProcess(IBatteryReportWorkspace workspace)
        {
            Calls.Add("start");
            if (StartFailure is not null) { throw StartFailure; }
            return this;
        }

        public bool WaitForExit(int milliseconds) { Assert.Equal(100, milliseconds); Calls.Add("wait"); return OnWait?.Invoke() ?? true; }
        public Stream OpenReport()
        {
            Calls.Add("open");
            if (OpenFailure is not null) { throw OpenFailure; }
            return new MemoryStream(Encoding.UTF8.GetBytes(Xml));
        }

        public BatteryReportCleanup Cleanup() { Calls.Add("cleanup"); return new(!CleanupFails, CleanupFails ? 32U : null); }
        public BatteryReportCleanup TerminateAndWait(TimeSpan timeout)
        {
            Assert.Equal(TimeSpan.FromSeconds(5), timeout);
            Calls.Add("terminate");
            if (TerminationThrows) { throw new BatteryReportNativeException(5); }
            return new(!TerminationFails);
        }

        void IDisposable.Dispose() => Calls.Add(Calls.Contains("process_dispose", StringComparer.Ordinal) || !Calls.Contains("start", StringComparer.Ordinal) || StartFailure is not null
            ? "workspace_dispose" : "process_dispose");
    }
}
