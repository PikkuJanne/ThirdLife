using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.UI.Features.Execution;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PlanReviewTests
{
    private const string InterruptionRootVariable = "THIRDLIFE_TL0313_INTERRUPTION_ROOT";
    private const string InterruptionRootPrefix = "ThirdLife-TL0313-";
    private const string InterruptionMarker = "intent-ready.json";
    private const string ConfirmedIntentMarker = "intent-confirmed.marker";

    [Fact]
    [Trait("Category", "Extended")]
    public async Task KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch()
    {
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = Directory.CreateTempSubdirectory(InterruptionRootPrefix);
        try
        {
            var (job, pending) = await KillOwnedApplicationAfterIntentAsync(root.FullName);
            var clock = new FixtureClock();
            var backend = new FixtureBrokerBackend(static (_, _, _, _, _) =>
                throw new InvalidOperationException("Reopening the interrupted job must not dispatch."));

            ShellTestHost.Run(() =>
            {
                var workspace = OpenInterruptionWorkspace(root.FullName, clock, backend);
                workspace.OpenJobAsync(job.Value).GetAwaiter().GetResult();
                Assert.Equal(job.Value, workspace.JobId);
                InQualificationWindow(workspace, (window, _) =>
                {
                    var execution = Named<BrokerExecutionView>(Named<FrameworkElement>(window, "AuditView"), "BrokerExecutionPanel");
                    var history = Named<TextBox>(execution, "SavedBrokerHistory");
                    var recover = Named<Button>(execution, "CheckRecoveryButton");
                    Assert.True(history.IsVisible && history.IsReadOnly);
                    Assert.Contains(pending.Plan.Key.PlanSha256, history.Text, StringComparison.Ordinal);
                    Assert.Contains("Waiting for a final result", history.Text, StringComparison.Ordinal);
                    Assert.True(recover.IsEnabled);
                    Assert.False(workspace.CanRunBrokerExecution);

                    clock.Current += ExecutionCoordinator.MaximumDispatchLifetime;
                    Invoke(window, recover);
                    PumpUntil(window, () => !workspace.IsBusy && !recover.IsEnabled);
                    Assert.Contains("needs review", history.Text, StringComparison.Ordinal);
                    Assert.False(workspace.CanRunBrokerExecution);
                });
            });

            var recovered = await LoadInterruptionJournalAsync(root.FullName, job);
            Assert.Equal(pending.Approvals, recovered.Approvals.AsEnumerable());
            Assert.Equal(pending.Entries, recovered.Entries.Take(pending.Entries.Length));
            var starts = pending.Entries.Where(entry => entry.State == JournalState.Started).ToArray();
            var terminal = recovered.Entries.Skip(pending.Entries.Length).ToArray();
            Assert.Equal(starts.Length, terminal.Length);
            foreach (var start in starts)
            {
                var result = Assert.Single(terminal, entry => entry.ActionId == start.ActionId);
                Assert.Equal(start.ApprovalId, result.ApprovalId);
                Assert.Equal(start.CorrelationId, result.CorrelationId);
                Assert.Equal(JournalState.Started, result.PriorState);
                Assert.Equal(JournalState.RequiresReview, result.State);
                Assert.Equal(JournalCheckpoint.Reconcile, result.Checkpoint);
                Assert.Equal(JournalResult.Interrupted, result.Result);
                Assert.Equal(clock.Current, result.OccurredAtUtc);
                Assert.Null(result.Verification);
            }

            await using (var reopened = OpenInterruptionWorkspace(root.FullName, clock, backend))
            {
                await reopened.OpenJobAsync(job.Value);
                Assert.Contains("needs review", reopened.BrokerHistoryText, StringComparison.Ordinal);
                Assert.False(reopened.CanCheckBrokerRecovery);
                Assert.False(reopened.CanRunBrokerExecution);
            }
            var afterSecondReopen = await LoadInterruptionJournalAsync(root.FullName, job);
            Assert.Equal(recovered.Entries, afterSecondReopen.Entries.AsEnumerable());
            Assert.Equal(0, backend.Calls);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    [Trait("Category", "Extended")]
    public void OwnedNoOpApplicationStopsAtTheDurableIntent()
    {
        var runRoot = Environment.GetEnvironmentVariable(InterruptionRootVariable);
        if (string.IsNullOrEmpty(runRoot)) { return; }
        Assert.False(WindowsBrokerPeer.CurrentIdentity().IsElevated);
        var root = new DirectoryInfo(runRoot);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), root.Parent!.FullName, ignoreCase: true);
        Assert.StartsWith(InterruptionRootPrefix, root.Name, StringComparison.Ordinal);
        Assert.False(root.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Empty(root.EnumerateFileSystemInfos());

        var backend = new FixtureBrokerBackend(async (_, batch, _, _, token) =>
        {
            // The coordinator has committed Started for each action. This fixture has no
            // protected adapter or send path, and leaves authorization untouched.
            var marker = Path.Combine(root.FullName, InterruptionMarker);
            var staged = marker + ".pending";
            await File.WriteAllTextAsync(staged,
                JsonSerializer.Serialize(new InterruptionNotice(batch.Plan.JobId.Value, Environment.ProcessId)), token).ConfigureAwait(false);
            File.Move(staged, marker);
            var confirmed = Path.Combine(root.FullName, ConfirmedIntentMarker);
            while (!File.Exists(confirmed))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), token).ConfigureAwait(false);
            }

            // Terminate the exact WPF test process that reached the held intent.
            using var self = Process.GetCurrentProcess();
            self.Kill();
            throw new InvalidOperationException("The owned WPF process did not terminate.");
        });

        ShellTestHost.Run(() =>
        {
            var workspace = OpenInterruptionWorkspace(root.FullName, new FixtureClock(), backend);
            PrepareAssessmentAsync(workspace).GetAwaiter().GetResult();
            InQualificationWindow(workspace, (window, panel) =>
            {
                SelectQualificationProfile(workspace);
                Build(window, panel, workspace);
                Named<TextBox>(panel, "BrokerOperator").Text = "fixture-interruption-operator";
                ShellTestHost.Drain(window);
                Approve(window, panel, workspace);
                var execution = Named<BrokerExecutionView>(Named<FrameworkElement>(window, "AuditView"), "BrokerExecutionPanel");
                Invoke(window, Named<Button>(execution, "RunBrokerButton"));
                WaitForQualificationOperator(window, static () => false, TimeSpan.FromSeconds(60));
            });
        }, TimeSpan.FromSeconds(75));
    }

    private static async Task<(JobId Job, JournalHistory Journal)> KillOwnedApplicationAfterIntentAsync(string root)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } dotnet ? dotnet : "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(PlanReviewTests).Assembly.Location);
        startInfo.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(PlanReviewTests).FullName}.{nameof(OwnedNoOpApplicationStopsAtTheDurableIntent)}");
        startInfo.ArgumentList.Add("--logger:console;verbosity=quiet");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("RunConfiguration.TreatNoTestsAsError=true");
        startInfo.Environment[InterruptionRootVariable] = root;
        using var process = Assert.IsType<Process>(Process.Start(startInfo));
        try
        {
            var marker = new FileInfo(Path.Combine(root, InterruptionMarker));
            var wait = Stopwatch.StartNew();
            while (!marker.Exists && !process.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                marker.Refresh();
            }

            Assert.True(marker.Exists && !process.HasExited, "The owned WPF test process did not reach its durable intent.");
            Assert.InRange(marker.Length, 1, 512);
            var notice = Assert.IsType<InterruptionNotice>(JsonSerializer.Deserialize<InterruptionNotice>(await File.ReadAllTextAsync(marker.FullName)));
            Assert.Matches(@"\Ajob-[0-9a-f]{32}\z", notice.JobId);
            var job = new JobId(notice.JobId);
            using var client = Process.GetProcessById(notice.ProcessId);
            Assert.False(client.HasExited);
            Assert.Equal(WindowsBrokerPeer.CurrentIdentity().SessionId, client.SessionId);
            var pending = await LoadInterruptionJournalAsync(root, job);
            var approval = Assert.Single(pending.Approvals);
            Assert.Equal(pending.Plan.Actions.Length * 2, pending.Entries.Length);
            Assert.All(pending.Plan.Actions, action =>
            {
                var entries = pending.Entries.Where(entry => entry.ActionId == action.ActionId).ToArray();
                Assert.Equal(new[] { JournalState.Approved, JournalState.Started }, entries.Select(entry => entry.State));
                Assert.Equal(approval.ApprovalId, entries[1].ApprovalId);
                Assert.Equal(JournalCheckpoint.BrokerDispatchIntent, entries[1].Checkpoint);
            });
            Assert.False(client.HasExited);
            using (new FileStream(Path.Combine(root, ConfirmedIntentMarker), FileMode.CreateNew)) { }
            await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(0, process.ExitCode);
            return (job, pending);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private static AuditWorkspace OpenInterruptionWorkspace(string root, FixtureClock clock, INoOpBrokerBackend backend) =>
        new(async token => await SqliteJobStore.OpenAsync(Path.Combine(root, "store"), token).ConfigureAwait(false),
            [new PlanFactsProvider()], clock, fixturePlanPolicy: static _ => ReadyPolicy,
            fixtureBrokerCoordinator: journal => new(journal, backend, clock));

    private static async Task<JournalHistory> LoadInterruptionJournalAsync(string root, JobId job)
    {
        await using var store = await SqliteJobStore.OpenAsync(Path.Combine(root, "store"));
        return Assert.Single(await store.LoadJobJournalsAsync(job));
    }

    private sealed record InterruptionNotice(string JobId, int ProcessId);
}
