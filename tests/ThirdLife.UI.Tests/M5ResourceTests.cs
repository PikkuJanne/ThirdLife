using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Reports.WorkshopReport;
using ThirdLife.UI.Shell;
using Xunit.Abstractions;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class M5ResourceTests(ITestOutputHelper output)
{
    private const string Workload = "m5-inventory-recovery-v1";
    private const string Database = "thirdlife-jobs.sqlite3";
    private const int ProviderCount = 16;
    private const int FactsPerProvider = 16;
    private const int SampleMilliseconds = 10;
    private static readonly TimeSpan DestinationDelay = TimeSpan.FromMilliseconds(350);

    [Fact]
    [Trait("Category", "Extended")]
    public Task SoftwareRenderingAndSingleWorkerRetainInventoryOnCancellation() =>
        CheckInventoryAsync(delayDestination: false);

    [Fact]
    [Trait("Category", "Extended")]
    public Task BelowNormalWithDelayedDestinationDrainsTheCommitAndReopens() =>
        CheckInventoryAsync(delayDestination: true);

    private async Task CheckInventoryAsync(bool delayDestination)
    {
        using var process = Process.GetCurrentProcess();
        var priorPriority = process.PriorityClass;
        var cpuStart = process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        var disk = new AuditTestStore();
        var reports = Path.Combine(disk.Root, "reports");
        var database = Path.Combine(disk.Root, "store", Database);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = delayDestination ? new DelayedDestination(entered, release) : null;
        var providers = Enumerable.Range(0, ProviderCount)
            .Select(index => new WorkloadProvider(index, index == 1 && !delayDestination ? entered : null))
            .ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            Workload + "\n" + string.Join('\n', providers.SelectMany(provider => provider.WorkloadRows)))));
        using var sampleStop = new CancellationTokenSource();
        var storage = SampleTemporaryStorageAsync(disk.Root, sampleStop.Token);
        object? metrics = null;
        try
        {
            if (delayDestination)
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                Assert.Equal(ProcessPriorityClass.BelowNormal, process.PriorityClass);
            }

            await using var workspace = new AuditWorkspace(async token =>
            {
                var store = await disk.OpenSqliteAsync(token);
                return delay is null ? store : new DelayedStore(store, delay);
            }, providers, TimeProvider.System, new WorkshopReportStore(reports));

            await workspace.CreateJobAsync();
            var job = Assert.IsType<StoredJob>(workspace.Job).Job.JobId;
            await workspace.RecordSanitizationAsync(VerifiedSample());
            Assert.True(workspace.CanAssess, workspace.GateStatus);
            var databaseBefore = new FileInfo(database).Length;
            ShellTestHost.Run(() =>
            {
                var startup = Stopwatch.StartNew();
                var window = new MainWindow(workspace) { Width = 640, Height = 420 };
                try
                {
                    Assert.Equal(RenderMode.SoftwareOnly, RenderOptions.ProcessRenderMode);
                    ShellTestHost.Show(window);
                    startup.Stop();
                    var view = Named<FrameworkElement>(window, "AuditView");
                    window.ShowStage(ShellStage.Inspect);
                    var start = Named<Button>(view, "StartAssessmentButton");
                    var cancel = Named<Button>(view, "CancelAssessmentButton");
                    Assert.True(start.Focus());
                    var decision = Stopwatch.StartNew();
                    Invoke(window, start);
                    PumpUntil(window, () => entered.Task.IsCompletedSuccessfully && workspace.CanCancel);
                    var inFlightMs = decision.Elapsed.TotalMilliseconds;
                    Assert.Equal(delayDestination ? 1 : 2, providers.Sum(provider => provider.Starts));
                    Assert.True(workspace.IsBusy);

                    var responsive = Stopwatch.StartNew();
                    window.ShowStage(ShellStage.Handover);
                    ShellTestHost.Drain(window);
                    Assert.False(cancel.IsVisible);
                    window.ShowStage(ShellStage.Inspect);
                    ShellTestHost.Drain(window);
                    Assert.True(cancel.IsVisible && cancel.Focus());
                    responsive.Stop();
                    Assert.Equal("Cancel assessment", ShellTestHost.ButtonPeer(cancel).GetName());
                    var status = new TextBlockAutomationPeer(Named<TextBlock>(view, "AssessmentMessage"));
                    Assert.Equal(AutomationLiveSetting.Polite, status.GetLiveSetting());

                    var cancelTime = Stopwatch.StartNew();
                    Invoke(window, cancel);
                    release.TrySetResult();
                    PumpUntil(window, () => workspace.Assessment?.Outcome == AssessmentOutcome.Cancelled && !workspace.IsBusy,
                        () => workspace.OperationError + workspace.AssessmentStatus);
                    cancelTime.Stop();
                    Assert.Contains("cancelled", status.GetName(), StringComparison.OrdinalIgnoreCase);
                    var partial = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
                    Assert.Equal(ProviderRunOutcome.Completed, partial.Providers[0].Outcome);
                    Assert.Equal(FactsPerProvider, partial.Providers[0].Observations.Count);
                    Assert.All(partial.Providers.Skip(delayDestination ? 1 : 2), provider => Assert.Null(provider.Outcome));
                    Assert.False(cancel.IsEnabled);

                    Invoke(window, Named<Button>(view, "CloseJobButton"));
                    Assert.Null(workspace.Job);
                    Named<TextBox>(view, "JobIdInput").Text = job.Value;
                    var resume = Stopwatch.StartNew();
                    Invoke(window, Named<Button>(view, "OpenJobButton"));
                    PumpUntil(window, () => workspace.Job is not null && !workspace.IsBusy, () => workspace.OperationError);
                    resume.Stop();
                    Assert.Equal(partial.AssessmentId, workspace.Assessment?.AssessmentId);
                    Assert.Equal(AssessmentOutcome.Cancelled, workspace.Assessment?.Outcome);
                    Assert.Equal(job, workspace.Job?.Job.JobId);

                    window.ShowStage(ShellStage.Inspect);
                    ShellTestHost.Drain(window);
                    var inventory = Stopwatch.StartNew();
                    Invoke(window, start);
                    PumpUntil(window, () => workspace.Assessment?.Outcome == AssessmentOutcome.Completed && !workspace.IsBusy,
                        () => workspace.OperationError + workspace.AssessmentStatus);
                    inventory.Stop();
                    var completed = Assert.IsType<AssessmentSnapshot>(workspace.Assessment);
                    Assert.NotEqual(partial.AssessmentId, completed.AssessmentId);
                    Assert.Equal(ProviderCount * FactsPerProvider, completed.Observations.Count);
                    Assert.All(completed.Providers, provider => Assert.Equal(ProviderRunOutcome.Completed, provider.Outcome));
                    Assert.All(providers.Skip(2), provider => Assert.Equal(1, provider.Starts));

                    window.ShowStage(ShellStage.Handover);
                    ShellTestHost.Drain(window);
                    Invoke(window, Named<Button>(view, "GenerateReportButton"));
                    PumpUntil(window, () => workspace.CanOpenReport, () => workspace.ReportStatus);
                    Invoke(window, Named<Button>(view, "OpenReportButton"));
                    PumpUntil(window, () => workspace.ReportPreview.Length > 0 && !workspace.IsBusy, () => workspace.ReportStatus);
                    var report = Assert.IsType<WorkshopReportArtifact>(workspace.ReportArtifact);
                    Assert.Equal(job, report.JobId);
                    var reportBytes = new FileInfo(Path.Combine(reports, report.FileName)).Length;
                    Assert.Equal(Encoding.UTF8.GetByteCount(workspace.ReportPreview), reportBytes);
                    Assert.InRange(reportBytes, 1, WorkshopReportStore.MaximumArtifactBytes);
                    Assert.Empty(Directory.EnumerateFiles(reports, "*.tmp"));
                    var databaseAfter = new FileInfo(database).Length;
                    Assert.True(databaseAfter >= databaseBefore);
                    metrics = new
                    {
                        workload = Workload,
                        workload_sha256 = digest,
                        fixture_providers = ProviderCount,
                        facts_per_provider = FactsPerProvider,
                        workers = 1,
                        network = "fixture_has_no_network_provider",
                        render = RenderOptions.ProcessRenderMode.ToString(),
                        priority = process.PriorityClass.ToString(),
                        destination_delay_ms = delayDestination ? DestinationDelay.TotalMilliseconds : 0,
                        window_width_dip = window.ActualWidth,
                        window_height_dip = window.ActualHeight,
                        loaded_window_ms = startup.Elapsed.TotalMilliseconds,
                        first_in_flight_ms = inFlightMs,
                        navigation_while_busy_ms = responsive.Elapsed.TotalMilliseconds,
                        cancel_to_durable_result_ms = cancelTime.Elapsed.TotalMilliseconds,
                        resume_ms = resume.Elapsed.TotalMilliseconds,
                        complete_inventory_ms = inventory.Elapsed.TotalMilliseconds,
                        database_before_bytes = databaseBefore,
                        database_after_bytes = databaseAfter,
                        database_growth_bytes = databaseAfter - databaseBefore,
                        output_bytes = reportBytes,
                    };
                }
                finally
                {
                    // Release the test-owned gate even when a UI assertion fails.
                    release.TrySetResult();
                    CloseWindow(window);
                }
            });
        }
        finally
        {
            process.PriorityClass = priorPriority;
            await sampleStop.CancelAsync();
            try { await storage; }
            finally { disk.Dispose(); }
        }

        Assert.False(Directory.Exists(disk.Root));
        process.Refresh();
        output.WriteLine("TL0510_RESOURCE " + JsonSerializer.Serialize(new
        {
            measurements = Assert.IsAssignableFrom<object>(metrics),
            process_scope = "isolated_vstest_host_plus_fixture",
            peak_working_set_bytes = process.PeakWorkingSet64,
            cpu_ms = (process.TotalProcessorTime - cpuStart).TotalMilliseconds,
            elapsed_ms = elapsed.Elapsed.TotalMilliseconds,
            workspace_temporary_bytes_sampled_peak = storage.Result,
            workspace_temporary_sample_interval_ms = SampleMilliseconds,
            cache = "package_and_update_caches_outside_this_inventory_workload",
            cleanup = "owned_workspace_removed_and_priority_restored",
        }));
    }

    private static async Task<long> SampleTemporaryStorageAsync(string root, CancellationToken token)
    {
        long peak = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(SampleMilliseconds));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".tmp", StringComparison.Ordinal) || file.EndsWith("-journal", StringComparison.Ordinal))
                    {
                        try { total += new FileInfo(file).Length; }
                        catch (FileNotFoundException) { /* The atomic publication completed between enumeration and stat. */ }
                    }
                }

                peak = Math.Max(peak, total);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }

        return peak;
    }

    private sealed class WorkloadProvider : IInventoryProvider
    {
        private readonly NormalizedEvidence[] _evidence;
        private readonly TaskCompletionSource? _waitOnce;
        private int _starts;

        public WorkloadProvider(int index, TaskCompletionSource? waitOnce)
        {
            _waitOnce = waitOnce;
            var name = index.ToString("D2", CultureInfo.InvariantCulture);
            var source = "m5-resource." + name;
            var keys = Enumerable.Range(0, FactsPerProvider)
                .Select(fact => new EvidenceKey(source + ".fact" + fact.ToString("D2", CultureInfo.InvariantCulture)))
                .ToArray();
            _evidence = keys.Select(key => NormalizedEvidence.Observed(key, EvidenceValue.FromBoolean(true))).ToArray();
            WorkloadRows = keys.Select(key => key.Value + "=true").ToArray();
            Descriptor = new(new ProviderId("m5-resource-" + name), ProviderPrivilegeRequirement.StandardUser,
                TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(10), ProviderNetworkUse.None,
                ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
                new(new EvidenceKey(source + ".failure"), source),
                keys.Select(key => new ProviderEvidenceDefinition(key, EvidenceValueKind.Boolean, null, source)));
        }

        public InventoryProviderDescriptor Descriptor { get; }
        public IReadOnlyList<string> WorkloadRows { get; }
        public int Starts => Volatile.Read(ref _starts);

        public async ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _starts) == 1 && _waitOnce is not null)
            {
                _waitOnce.TrySetResult();
                return await WaitForCancellationAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ProviderReadResult.Collected(_evidence);
        }
    }

    private sealed class DelayedDestination(TaskCompletionSource entered, TaskCompletionSource release)
    {
        public async Task BeforeAppendAsync(JobEvidenceBatch batch, CancellationToken token)
        {
            if (batch.Observations.Any(item => item.EvidenceKey.Value == "m5-resource.00.fact00") && entered.TrySetResult())
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                await Task.Delay(DestinationDelay, token);
            }
        }
    }

    private sealed class DelayedStore(IJobStore inner, DelayedDestination delay) : IJobStore
    {
        public Task CreateJobAsync(Job job, CancellationToken cancellationToken = default) =>
            inner.CreateJobAsync(job, cancellationToken);

        public async Task AppendEvidenceAsync(JobEvidenceBatch batch, CancellationToken cancellationToken = default)
        {
            await delay.BeforeAppendAsync(batch, cancellationToken);
            await inner.AppendEvidenceAsync(batch, cancellationToken);
        }

        public Task SetArchiveStateAsync(JobId jobId, bool isArchived, DateTimeOffset changedAtUtc,
            CancellationToken cancellationToken = default) =>
            inner.SetArchiveStateAsync(jobId, isArchived, changedAtUtc, cancellationToken);

        public Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(SanitizationGateDecision candidate,
            CancellationToken cancellationToken = default) =>
            inner.RecordSanitizationGateDecisionAsync(candidate, cancellationToken);

        public Task<StoredJob?> LoadJobAsync(JobId jobId, CancellationToken cancellationToken = default) =>
            inner.LoadJobAsync(jobId, cancellationToken);

        public Task RequireSanitizationGateAsync(JobId jobId, SanitizationGateDecisionId expectedDecisionId,
            CancellationToken cancellationToken = default) =>
            inner.RequireSanitizationGateAsync(jobId, expectedDecisionId, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
