using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ThirdLife.Actions.Execution;
using ThirdLife.Actions.Journal;
using ThirdLife.Broker.Protocol.Transport;
using ThirdLife.Persistence;
using ThirdLife.UI.BrokerClient;
using ThirdLife.UI.Features.Execution;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

public sealed partial class PlanReviewTests
{
    private static readonly string[] QualificationCapabilities = ["document_editing", "pdf_reading", "web_browsing"];
    private static readonly JsonSerializerOptions QualificationTraceOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void NoOpApplicationPreviewSavesTheExactPlanWithoutApproval()
    {
        using var disk = new AuditTestStore();
        RunApplicationQualification("ApplicationPreview", disk.Root, TextWriter.Null);
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ProtectedNoOpApplicationPreviewsWithoutApproval() => RunProtectedApplicationQualification("ApplicationPreview");

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ProtectedNoOpApplicationAcceptsWithSavedApproval() => RunProtectedApplicationQualification("ApplicationAccept");

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ProtectedNoOpApplicationDeclinesWithSavedApproval() => RunProtectedApplicationQualification("ApplicationDecline");

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ProtectedNoOpApplicationCancelsBeforeDispatch() => RunProtectedApplicationQualification("ApplicationCancel");

    private static void RunProtectedApplicationQualification(string scenario)
    {
        var run = RequireProtectedApplicationRun(scenario);
        ProtectedBrokerRehearsalTests.VerifyInstalledCandidate();
        var tracePath = Path.Combine(run, "trace.jsonl");
        using var stream = new FileStream(tracePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var trace = TextWriter.Synchronized(new StreamWriter(stream) { AutoFlush = true });
        RunApplicationQualification(scenario, run, trace);
    }

    private static void RunApplicationQualification(string scenario, string run, TextWriter trace)
    {
        var preview = scenario == "ApplicationPreview";
        var cancel = scenario == "ApplicationCancel";
        var scenarioCode = scenario.ToLowerInvariant();
        var root = Path.Combine(run, "application-store");
        Assert.False(Directory.Exists(root));
        JournalPlanSnapshot? expected = null;
        var attempts = 0;
        var sendChecks = 0;
        var awaitingCancellation = 0;
        var receivedProgress = new List<NoOpExecutionProgress>();
        using var testDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(285));
        var protectedBackend = new ProtectedNoOpBrokerBackend(new BrokerBatchClient());
        var backend = new FixtureBrokerBackend(async (_, batch, authorize, progress, token) =>
        {
            var plan = expected;
            if (plan is null || Interlocked.Increment(ref attempts) != 1 || plan.Key != batch.Plan ||
                !batch.Actions.Select(action => action.ActionId).SequenceEqual(plan.Actions.Select(action => action.ActionId)))
            {
                return new(NoOpBrokerBatchStatus.Unavailable, []);
            }

            await using var store = await OpenStoreAsync(token).ConfigureAwait(false);
            var saved = await store.LoadJournalAsync(plan.Key, token).ConfigureAwait(false);
            if (saved?.ActiveApproval is not { } approval || approval.ApprovalId != batch.ApprovalId ||
                !plan.Actions.All(action => saved.Entries.Any(entry => entry.ActionId == action.ActionId &&
                    entry.State == JournalState.Started && entry.Checkpoint == JournalCheckpoint.BrokerDispatchIntent &&
                    entry.CorrelationId == batch.CorrelationId && entry.ApprovalId == approval.ApprovalId)))
            {
                return new(NoOpBrokerBatchStatus.Unavailable, []);
            }

            Record(new("ApprovalSaved", scenarioCode, PlanSha256: plan.Key.PlanSha256, ImpactSha256: plan.Key.ImpactSha256));
            Record(new("RequestingConsent", scenarioCode));
            return await protectedBackend.RunAsync(batch, CheckBeforeSendAsync,
                new QualificationProgress(progress, receivedProgress), token).ConfigureAwait(false);

            async Task<bool> CheckBeforeSendAsync(CancellationToken dispatchToken)
            {
                if (cancel)
                {
                    // Hold the authenticated broker before send so the WPF Cancel control
                    // can be exercised without racing the no-op.
                    Record(new("AwaitingCancellation", scenarioCode));
                    Volatile.Write(ref awaitingCancellation, 1);
                    await Task.Delay(Timeout.InfiniteTimeSpan, dispatchToken).ConfigureAwait(false);
                }

                Interlocked.Increment(ref sendChecks);
                return await authorize(dispatchToken).ConfigureAwait(false);
            }
        });

        ShellTestHost.Run(() =>
        {
            var workspace = OpenWorkspace(preview ? null : backend);
            PrepareAssessmentAsync(workspace).GetAwaiter().GetResult();
            SelectQualificationProfile(workspace);
            workspace.BuildPlanReviewAsync().GetAwaiter().GetResult();

            var shown = Assert.IsType<PlanReviewSnapshot>(workspace.PlanReview);
            Assert.Equal(QualificationCapabilities, shown.Plan.Actions.Select(action => action.CapabilityId));
            expected = JournalPlanSnapshot.Create(shown.Plan, shown.Impact);
            Assert.True(ExecutionCoordinator.IsBrokerPlan(expected));
            var jobId = workspace.JobId;
            RunWithStore(store => Assert.Equal(JournalWriteResult.Recorded,
                store.SaveJournalPlanAsync(expected).GetAwaiter().GetResult()));

            InQualificationWindow(workspace, (window, panel) =>
            {
                var details = Named<Expander>(panel, "PlanDetailsExpander");
                details.IsExpanded = true;
                details.BringIntoView();
                ShellTestHost.Drain(window);
                Assert.Contains(expected.Key.PlanSha256, Named<TextBox>(panel, "PlanDetailsText").Text, StringComparison.Ordinal);
                Assert.False(Named<Button>(panel, "ApprovePlanReviewButton").IsEnabled);
                var execution = Named<BrokerExecutionView>(panel, "BrokerExecutionPanel");
                Assert.False(Named<Button>(execution, "RunBrokerButton").IsEnabled);
                Record(new("CandidateReady", scenarioCode, PlanSha256: expected.Key.PlanSha256,
                    ImpactSha256: expected.Key.ImpactSha256, ActionCount: expected.Actions.Length,
                    ClientUnelevated: !WindowsBrokerPeer.CurrentIdentity().IsElevated,
                    Capabilities: QualificationCapabilities, ActionIds: expected.Actions.Select(action => action.ActionId).ToArray()));
                if (preview) { return; }

                // Normal WPF operator entry, approval and Run are the only route to dispatch.
                var status = QualificationOutcome(scenario).HistoryText;
                WaitForQualificationOperator(window, () => Volatile.Read(ref attempts) != 0, TimeSpan.FromSeconds(145));
                if (cancel)
                {
                    WaitForQualificationOperator(window, () => Volatile.Read(ref awaitingCancellation) != 0, TimeSpan.FromSeconds(95));
                    var cancelButton = Named<Button>(execution, "CancelBrokerButton");
                    Assert.True(cancelButton.IsEnabled);
                    Invoke(window, cancelButton);
                }

                WaitForQualificationOperator(window, () => !workspace.IsBusy &&
                    workspace.BrokerHistoryText.Contains(status, StringComparison.Ordinal), cancel
                        ? TimeSpan.FromSeconds(15) : ExecutionCoordinator.MaximumDispatchLifetime);
                Assert.False(workspace.CanRunBrokerExecution);
                Assert.Equal(scenario != "ApplicationAccept", workspace.CanApprovePlanReview);
            });

            var reopened = OpenWorkspace();
            reopened.OpenJobAsync(jobId).GetAwaiter().GetResult();
            Assert.Equal(jobId, reopened.JobId);
            if (preview)
            {
                SelectQualificationProfile(reopened);
                reopened.BuildPlanReviewAsync().GetAwaiter().GetResult();
            }

            InQualificationWindow(reopened, (_, panel) =>
            {
                Assert.False(reopened.CanRunBrokerExecution);
                Assert.False(reopened.CanCheckBrokerRecovery);
                if (preview)
                {
                    Assert.False(reopened.HasBrokerHistory);
                    var displayed = Assert.IsType<PlanReviewSnapshot>(reopened.PlanReview);
                    Assert.Equal(expected.Key, JournalPlanSnapshot.Create(displayed.Plan, displayed.Impact).Key);
                }
                else
                {
                    var execution = Named<BrokerExecutionView>(panel, "BrokerExecutionPanel");
                    var history = Named<TextBox>(execution, "SavedBrokerHistory");
                    Assert.True(history.IsVisible && history.IsReadOnly);
                    Assert.Contains(expected.Key.PlanSha256, history.Text, StringComparison.Ordinal);
                    Assert.Contains(QualificationOutcome(scenario).HistoryText, history.Text, StringComparison.Ordinal);
                }
            });

            var terminals = 0;
            RunWithStore(store =>
            {
                var history = Assert.Single(store.LoadJobJournalsAsync(expected.Key.JobId).GetAwaiter().GetResult());
                Assert.Equal(expected.Key, history.Plan.Key);
                Assert.Equal(expected.Actions.Select(action => action.ActionId), history.Plan.Actions.Select(action => action.ActionId));
                if (preview)
                {
                    Assert.Empty(history.Approvals);
                    Assert.Empty(history.Entries);
                }
                else
                {
                    terminals = AssertQualificationJournal(history, scenario);
                    if (scenario == "ApplicationAccept")
                    {
                        Assert.Equal(expected.Actions.Select(action => action.ActionId), receivedProgress.Select(update => update.ActionId));
                        var correlation = history.Entries.First(entry => entry.State == JournalState.Started).CorrelationId;
                        Assert.All(receivedProgress, update =>
                        {
                            Assert.Equal(correlation, update.CorrelationId);
                            Assert.Equal(NoOpProgressStage.Accepted, update.Stage);
                        });
                    }
                }
            });
            Assert.Equal(preview ? 0 : 1, attempts);
            Assert.Equal(scenario == "ApplicationAccept" ? 1 : 0, sendChecks);
            Assert.Equal(scenario == "ApplicationAccept" ? expected.Actions.Length : 0, receivedProgress.Count);
            Record(new("Result", scenarioCode, Status: preview ? "Previewed" : QualificationOutcome(scenario).Status,
                TerminalCount: terminals, ClientUnelevated: !WindowsBrokerPeer.CurrentIdentity().IsElevated, Reopened: true));
        }, TimeSpan.FromSeconds(295));

        Task<SqliteJobStore> OpenStoreAsync(CancellationToken token) => SqliteJobStore.OpenAsync(root, token);

        AuditWorkspace OpenWorkspace(INoOpBrokerBackend? approvedBackend = null) =>
            new(async token => await OpenStoreAsync(token).ConfigureAwait(false), [new PlanFactsProvider()], TimeProvider.System,
                fixturePlanPolicy: static _ => ReadyPolicy, fixtureBrokerCoordinator: journal => new(journal, approvedBackend));

        void RunWithStore(Action<SqliteJobStore> action)
        {
            var store = OpenStoreAsync(testDeadline.Token).GetAwaiter().GetResult();
            try { action(store); }
            finally { store.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }

        void Record(QualificationTrace value) => trace.WriteLine(JsonSerializer.Serialize(value, QualificationTraceOptions));
    }

    private static void SelectQualificationProfile(AuditWorkspace workspace)
    {
        workspace.SelectedPlanProfile = Assert.Single(workspace.PlanProfiles, option => option.Profile.ProfileId == "basic");
        foreach (var capability in workspace.PlanCapabilities.Where(capability => !capability.IsEssential))
        {
            capability.IsIncluded = false;
        }
    }

    private static void InQualificationWindow(AuditWorkspace workspace, Action<MainWindow, PlanReviewView> check)
    {
        var window = new MainWindow(new ShellViewModel(), workspace)
        {
            Title = "ThirdLife TL-0313 no-op qualification",
            WindowState = WindowState.Maximized,
        };
        try
        {
            ShellTestHost.Show(window);
            ShellTestHost.Invoke(window, "PrepareButton");
            var panel = Named<PlanReviewView>(Named<FrameworkElement>(window, "AuditView"), "PlanReviewPanel");
            Assert.True(panel.IsVisible);
            check(window, panel);
        }
        finally { if (window.IsVisible) { CloseWindow(window); } }
    }

    private static void WaitForQualificationOperator(Window window, Func<bool> condition, TimeSpan timeout)
    {
        if (condition()) { return; }
        var frame = new DispatcherFrame();
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        timer.Tick += (_, _) => frame.Continue = window.IsVisible && !condition() && clock.Elapsed < timeout;
        try
        {
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        finally { timer.Stop(); }
        Assert.True(window.IsVisible && condition(), "The exact application qualification did not finish within its operator or dispatch window.");
    }

    private static (string Status, JournalState State, JournalResult Result, string HistoryText) QualificationOutcome(string scenario) => scenario switch
    {
        "ApplicationAccept" => ("Completed", JournalState.Applied, JournalResult.BackendApplied, "Independent verification is pending"),
        "ApplicationDecline" => ("Declined", JournalState.Failed, JournalResult.AuthorizationDeclined, "Windows consent declined. No action was sent."),
        "ApplicationCancel" => ("Cancelled", JournalState.Failed, JournalResult.Cancelled, "Cancelled before dispatch. No action was sent."),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static int AssertQualificationJournal(JournalHistory history, string scenario)
    {
        var expected = QualificationOutcome(scenario);
        var approved = Assert.Single(history.Approvals);
        Assert.False(string.IsNullOrWhiteSpace(approved.ActorId.Value));
        Assert.Equal(history.Plan.Actions.Length * 3, history.Entries.Length);
        Assert.Equal(history.Entries.Length, history.Entries.Select(entry => entry.EntryId).Distinct().Count());
        Assert.Single(history.Entries.Where(entry => entry.State == JournalState.Started).Select(entry => entry.CorrelationId).Distinct());
        foreach (var action in history.Plan.Actions)
        {
            var entries = history.Entries.Where(entry => entry.ActionId == action.ActionId).ToArray();
            Assert.Equal(new[] { JournalState.Approved, JournalState.Started, expected.State }, entries.Select(entry => entry.State));
            Assert.All(entries, entry => Assert.Equal(approved.ApprovalId, entry.ApprovalId));
            Assert.Equal(JournalCheckpoint.BrokerApproval, entries[0].Checkpoint);
            Assert.Equal(approved.ActorId, entries[0].ActorId);
            Assert.Equal(JournalCheckpoint.BrokerDispatchIntent, entries[1].Checkpoint);
            Assert.Equal(entries[1].CorrelationId, entries[2].CorrelationId);
            Assert.Equal(expected.Result, entries[2].Result);
            Assert.Equal(expected.State == JournalState.Applied ? JournalCheckpoint.None :
                JournalCheckpoint.BrokerPreDispatchFailure, entries[2].Checkpoint);
            Assert.Null(entries[2].Verification);
        }

        return history.Plan.Actions.Length;
    }

    private static string RequireProtectedApplicationRun(string scenario)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var client = WindowsBrokerPeer.CurrentIdentity();
        Assert.True(Environment.UserInteractive && !client.IsElevated && client.SessionId > 0 &&
            Environment.MachineName.Equals("ThirdLife-W11", StringComparison.OrdinalIgnoreCase) &&
            identity.Name.Equals("ThirdLife-W11\\ThirdLifeAdmin", StringComparison.OrdinalIgnoreCase),
            "An unelevated console token in the dedicated ThirdLife Hyper-V guest is required.");
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        Assert.True(bios?.GetValue("SystemManufacturer") is "Microsoft Corporation" &&
            bios.GetValue("SystemProductName") is "Virtual Machine", "Hyper-V guest firmware is required.");

        var runId = Environment.GetEnvironmentVariable("THIRDLIFE_TL0313_HYPERV_RUN_ID");
        Assert.NotNull(runId);
        Assert.Matches(@"\A[0-9a-f]{32}\z", runId);
        Assert.Equal(scenario.ToLowerInvariant(), Environment.GetEnvironmentVariable("THIRDLIFE_TL0311_CONSENT_REHEARSAL"));
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var staging = new DirectoryInfo(Path.Combine(programFiles, "ThirdLifeVerification"));
        var run = new DirectoryInfo(Path.Combine(staging.FullName, runId));
        var request = new FileInfo(Path.Combine(run.FullName, "run-request.json"));
        const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Access;
        Assert.False(staging.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.False(run.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.False(request.Attributes.HasFlag(FileAttributes.ReparsePoint));
        RequireQualificationInputProtection(staging.GetAccessControl(sections));
        RequireQualificationInputProtection(run.GetAccessControl(sections));
        RequireQualificationInputProtection(request.GetAccessControl(sections));
        Assert.True(request.IsReadOnly);
        using var file = new FileStream(request.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.InRange(file.Length, 1, 8192);
        using var json = JsonDocument.Parse(file, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 4 });
        var root = json.RootElement;
        Assert.True(root.EnumerateObject().Count() == 3 && root.GetProperty("task_id").ValueEquals("TL-0313") &&
            root.GetProperty("scenario").ValueEquals(scenario) && root.GetProperty("run_id").ValueEquals(runId));

        var results = new DirectoryInfo(Path.Combine(run.FullName, "result"));
        Assert.False(results.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(Path.Combine(results.FullName, "trace.jsonl"), Environment.GetEnvironmentVariable("THIRDLIFE_TL0317_TRACE_PATH"),
            ignoreCase: true);
        return results.FullName;
    }

    private static void RequireQualificationInputProtection(FileSystemSecurity security)
    {
        static bool IsTrusted(SecurityIdentifier sid) => sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
            sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);

        Assert.True(security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && IsTrusted(owner));
        Assert.True(security.AreAccessRulesCanonical);
        const FileSystemRights readOnly = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        Assert.NotEmpty(rules);
        Assert.All(rules.Cast<FileSystemAccessRule>(), rule => Assert.True(rule.AccessControlType != AccessControlType.Allow ||
            IsTrusted((SecurityIdentifier)rule.IdentityReference) || (rule.FileSystemRights & ~readOnly) == 0));
    }

    private sealed class QualificationProgress(IProgress<NoOpExecutionProgress>? target, List<NoOpExecutionProgress> received)
        : IProgress<NoOpExecutionProgress>
    {
        public void Report(NoOpExecutionProgress update)
        {
            received.Add(update);
            target?.Report(update);
        }
    }

    private sealed record QualificationTrace(string Stage, string Scenario, string? Status = null, int? TerminalCount = null,
        bool? ClientUnelevated = null, bool? Reopened = null, string? PlanSha256 = null, string? ImpactSha256 = null,
        int? ActionCount = null, string[]? Capabilities = null, Guid[]? ActionIds = null)
    {
        public DateTimeOffset TimestampUtc { get; } = TimeProvider.System.GetUtcNow();
    }
}
