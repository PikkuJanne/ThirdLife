using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Diagnostics.Tests;

public sealed class SanitizedLogStoreTests
{
    private const string CrashRootEnvironmentVariable = "THIRDLIFE_TL0104_CRASH_ROOT";
    private const string CommittedCrashRootEnvironmentVariable = "THIRDLIFE_TL0104_COMMITTED_CRASH_ROOT";
    private const string RetainedCrashRootEnvironmentVariable = "THIRDLIFE_TL0104_RETAINED_CRASH_ROOT";
    private const string WriterRootEnvironmentVariable = "THIRDLIFE_TL0104_WRITER_ROOT";
    private const string WriterGateEnvironmentVariable = "THIRDLIFE_TL0104_WRITER_GATE";
    private const string WriterSuffixEnvironmentVariable = "THIRDLIFE_TL0104_WRITER_SUFFIX";
    private static readonly DateTimeOffset Now = new(2030, 1, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task StoreAppliesConfiguredByteRetentionToWholeRecordsAndReportsEviction()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions((sampleLength * 2L) + 16),
            time);

        await store.AppendAsync(DiagnosticEventFactory.Completed(Now - TimeSpan.FromDays(3), Suffix(1)));
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now - TimeSpan.FromDays(2), Suffix(2)));
        var third = await store.AppendAsync(DiagnosticEventFactory.Completed(Now - TimeSpan.FromDays(1), Suffix(3)));
        var fourth = await store.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(4)));

        var records = await store.ReadOwnedRecordsForTestingAsync();
        Assert.Equal(2, records.Count);
        var combined = string.Join('\n', records.Select(Encoding.UTF8.GetString));
        Assert.DoesNotContain($"event{Suffix(1)}", combined, StringComparison.Ordinal);
        Assert.DoesNotContain($"event{Suffix(2)}", combined, StringComparison.Ordinal);
        Assert.Contains($"event{Suffix(3)}", combined, StringComparison.Ordinal);
        Assert.Contains($"event{Suffix(4)}", combined, StringComparison.Ordinal);
        Assert.Equal(1, third.RemovedRecordCount);
        Assert.Equal(1, fourth.RemovedRecordCount);
        Assert.True(
            Directory.EnumerateFiles(StorePath(temporary), "evt-*.json", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length) <= (sampleLength * 2L) + 16);
    }

    [Fact]
    public async Task AgeRetentionRemovesOnlyRecordsOlderThanTheExactCutoff()
    {
        using var temporary = new TemporaryDirectory();
        var firstTimestamp = Now - TimeSpan.FromDays(14) - TimeSpan.FromTicks(1);
        var cutoffTimestamp = Now - TimeSpan.FromDays(14);
        var time = new FixedTimeProvider(firstTimestamp);
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(256 * 1024),
            time);

        await store.AppendAsync(DiagnosticEventFactory.Completed(firstTimestamp, Suffix(1)));
        time.UtcNow = cutoffTimestamp;
        await store.AppendAsync(DiagnosticEventFactory.Completed(cutoffTimestamp, Suffix(2)));
        time.UtcNow = Now;
        var currentAppend = await store.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(3)));

        Assert.Equal(1, currentAppend.RemovedRecordCount);
        var records = await store.ReadOwnedRecordsForTestingAsync();
        Assert.Equal(2, records.Count);
        var combined = string.Join('\n', records.Select(Encoding.UTF8.GetString));
        Assert.DoesNotContain($"event{Suffix(1)}", combined, StringComparison.Ordinal);
        Assert.Contains($"event{Suffix(2)}", combined, StringComparison.Ordinal);
        Assert.Contains($"event{Suffix(3)}", combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactAgeCutoffIsRetained()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now);
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(64 * 1024),
            time);

        await store.AppendAsync(DiagnosticEventFactory.Completed(Now - TimeSpan.FromDays(14), Suffix(1)));
        var cleanup = await store.CleanupAsync();

        Assert.Equal(0, cleanup.RemovedRecordCount);
        Assert.Single(await store.ReadOwnedRecordsForTestingAsync());
    }

    [Fact]
    public async Task FutureClockRecordFailsClosedAndIsPreserved()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now + TimeSpan.FromDays(1));
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(64 * 1024),
            time);
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now + TimeSpan.FromDays(1), Suffix(2)));
        time.UtcNow -= TimeSpan.FromDays(1);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());

        Assert.Equal("diagnostic_store_clock_invalid", exception.ResultCode);
        Assert.Single(
            Directory.EnumerateFiles(StorePath(temporary), "evt-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task IncomingExpiredAndFutureRecordsFailBeforeCreatingTheStore()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now);
        var expiredRoot = Path.Combine(temporary.Path, "expired-logs");
        await using var expiredStore = SanitizedLogStore.OpenForTesting(
            expiredRoot,
            new SanitizedLogStoreOptions(64 * 1024),
            time);
        var expired = await Assert.ThrowsAsync<DiagnosticContractException>(() => expiredStore.AppendAsync(
            DiagnosticEventFactory.Completed(Now - TimeSpan.FromDays(14) - TimeSpan.FromTicks(1))));

        Assert.Equal("diagnostic_store_record_expired", expired.ResultCode);
        Assert.False(Directory.Exists(expiredRoot));

        var futureRoot = Path.Combine(temporary.Path, "future-logs");
        await using var futureStore = SanitizedLogStore.OpenForTesting(
            futureRoot,
            new SanitizedLogStoreOptions(64 * 1024),
            time);
        var future = await Assert.ThrowsAsync<DiagnosticContractException>(() => futureStore.AppendAsync(
            DiagnosticEventFactory.Completed(Now + TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1))));

        Assert.Equal("diagnostic_store_clock_invalid", future.ResultCode);
        Assert.False(Directory.Exists(futureRoot));
    }

    [Fact]
    public async Task OversizedRecordAndPreCancelledWriteLeaveNoFile()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now);
        var diagnosticEvent = DiagnosticEventFactory.Completed(Now);
        var byteCount = diagnosticEvent.GetUtf8Json().Length;
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(byteCount - 1),
            time);

        var oversized = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.AppendAsync(diagnosticEvent));
        Assert.Equal("diagnostic_store_record_too_large", oversized.ResultCode);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.AppendAsync(diagnosticEvent, cancellation.Token));
        Assert.False(Directory.Exists(StorePath(temporary)));
    }

    [Fact]
    public async Task InterruptionAfterTemporaryWriteLeavesNoPartialRecord()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now);
        var fault = new ThrowingFaultInjector();
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(64 * 1024),
            time,
            fault);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(
            () => store.AppendAsync(DiagnosticEventFactory.Completed(Now)));

        Assert.Equal("diagnostic_store_unavailable", exception.ResultCode);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(StorePath(temporary)),
            static path => Path.GetFileName(path).StartsWith(".tmp-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData((int)LogStoreFaultPoint.BeforeTemporaryCreate)]
    [InlineData((int)LogStoreFaultPoint.AfterTemporaryWriteBeforeFlush)]
    public async Task DeterministicDiskFullFailuresLeaveNoFinalOrPartialAndPermitSafeRetry(
        int faultPointValue)
    {
        var faultPoint = (LogStoreFaultPoint)faultPointValue;
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var options = new SanitizedLogStoreOptions(64 * 1024);
        var time = new FixedTimeProvider(Now);
        await using (var failing = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         time,
                         new DiskFullFaultInjector(faultPoint)))
        {
            var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                failing.AppendAsync(DiagnosticEventFactory.Completed(Now)));
            Assert.Equal("diagnostic_store_unavailable", exception.ResultCode);
            Assert.DoesNotContain("disk", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        }

        await using var recovered = SanitizedLogStore.OpenForTesting(root, options, time);
        var retry = await recovered.AppendAsync(DiagnosticEventFactory.Completed(Now));
        Assert.True(retry.CleanupCompleted);
        Assert.Single(await recovered.ReadOwnedRecordsForTestingAsync());
    }

    [Fact]
    public async Task ConcurrentWritersRemainBoundedAndProduceCompleteRecords()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(Now);
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(256 * 1024),
            time);

        var writes = Enumerable.Range(1, 32)
            .Select(index => store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(index), Suffix(index))))
            .ToArray();
        await Task.WhenAll(writes);

        var records = await store.ReadOwnedRecordsForTestingAsync();
        Assert.Equal(32, records.Count);
        Assert.All(records, static record => Assert.StartsWith("{", Encoding.UTF8.GetString(record), StringComparison.Ordinal));
        Assert.All(records, static record => Assert.EndsWith("}", Encoding.UTF8.GetString(record), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeparateStoreInstancesShareTheCapacityLockAndReportEvictions()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var time = new FixedTimeProvider(Now);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions((sampleLength * 2L) + 16);
        await using var firstStore = SanitizedLogStore.OpenForTesting(root, options, time);
        await using var secondStore = SanitizedLogStore.OpenForTesting(root, options, time);

        await firstStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        var writes = Enumerable.Range(2, 31)
            .Select(index => (index % 2 == 0 ? firstStore : secondStore).AppendAsync(
                DiagnosticEventFactory.Completed(Now.AddTicks(index), Suffix(index))))
            .ToArray();
        var results = await Task.WhenAll(writes);

        var records = await firstStore.ReadOwnedRecordsForTestingAsync();
        Assert.Equal(2, records.Count);
        Assert.True(results.Sum(static result => result.RemovedRecordCount) >= 29);
        Assert.True(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length) <= options.MaximumBytes);
    }

    [Fact]
    public async Task TwoProcessesShareTheRootLockAndLeaveOnlyBoundedCanonicalRecords()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var gate = Path.Combine(temporary.Path, "writer-start.gate");
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        await using (var initialized = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         new FixedTimeProvider(Now)))
        {
            await initialized.CleanupAsync();
        }

        using var first = StartTestChild(
            nameof(WriterChildWaitsThenAppends),
            new Dictionary<string, string>
            {
                [WriterRootEnvironmentVariable] = root,
                [WriterGateEnvironmentVariable] = gate,
                [WriterSuffixEnvironmentVariable] = "1",
            });
        using var second = StartTestChild(
            nameof(WriterChildWaitsThenAppends),
            new Dictionary<string, string>
            {
                [WriterRootEnvironmentVariable] = root,
                [WriterGateEnvironmentVariable] = gate,
                [WriterSuffixEnvironmentVariable] = "2",
            });
        var firstOutput = first.StandardOutput.ReadToEndAsync();
        var firstError = first.StandardError.ReadToEndAsync();
        var secondOutput = second.StandardOutput.ReadToEndAsync();
        var secondError = second.StandardError.ReadToEndAsync();
        File.WriteAllBytes(gate, [0x2A]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Task.WhenAll(
            first.WaitForExitAsync(timeout.Token),
            second.WaitForExitAsync(timeout.Token));
        _ = await firstOutput;
        _ = await firstError;
        _ = await secondOutput;
        _ = await secondError;

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            options,
            new FixedTimeProvider(Now.AddTicks(2)));
        var records = await store.ReadOwnedRecordsForTestingAsync();
        Assert.Single(records);
        foreach (var bytes in records)
        {
            using var document = JsonDocument.Parse(bytes);
            Assert.True(PersistedDiagnosticRecordValidator.IsCanonical(
                bytes,
                document.RootElement.GetProperty("occurred_at_utc").GetDateTimeOffset()));
        }
        Assert.True(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length) <= options.MaximumBytes);
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task WriterChildWaitsThenAppends()
    {
        var root = Environment.GetEnvironmentVariable(WriterRootEnvironmentVariable);
        var gate = Environment.GetEnvironmentVariable(WriterGateEnvironmentVariable);
        var suffixText = Environment.GetEnvironmentVariable(WriterSuffixEnvironmentVariable);
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(gate) || string.IsNullOrEmpty(suffixText))
        {
            return;
        }

        var suffix = int.Parse(suffixText, System.Globalization.CultureInfo.InvariantCulture);
        for (var attempt = 0; attempt < 1_000 && !File.Exists(gate); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        Assert.True(File.Exists(gate));
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(sampleLength + 16L),
            new FixedTimeProvider(Now.AddTicks(suffix)));
        var result = await store.AppendAsync(
            DiagnosticEventFactory.Completed(Now.AddTicks(suffix), Suffix(suffix)));
        Assert.True(result.CleanupCompleted);
    }

    [Fact]
    public async Task RootLockWaitIsBoundedAndCancellationPreservesExistingRecords()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var time = new FixedTimeProvider(Now);
        var options = new SanitizedLogStoreOptions(64 * 1024);
        await using var firstStore = SanitizedLogStore.OpenForTesting(root, options, time);
        await using var secondStore = SanitizedLogStore.OpenForTesting(root, options, time);
        await firstStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));

        var lockPath = Path.Combine(root, ".diagnostic-store.lock");
        await using (var heldLock = new FileStream(
                         lockPath,
                         FileMode.Open,
                         FileAccess.ReadWrite,
                         FileShare.None))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                secondStore.AppendAsync(
                    DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2)),
                    cancellation.Token));
        }

        var records = await firstStore.ReadOwnedRecordsForTestingAsync();
        Assert.Single(records);
        Assert.Contains($"event{Suffix(1)}", Encoding.UTF8.GetString(records[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingOperationQueueRejectsTheSixtyFifthCallerWithoutUnboundedGrowth()
    {
        using var temporary = new TemporaryDirectory();
        using var fault = new BlockingFaultInjector();
        var time = new FixedTimeProvider(Now);
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(2 * 1024 * 1024),
            time,
            fault);

        var first = Task.Run(() => store.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1))));
        Assert.True(fault.WaitUntilBlocked(TimeSpan.FromSeconds(5)));
        var queued = Enumerable.Range(2, SanitizedLogStore.MaximumPendingOperations - 1)
            .Select(index => store.AppendAsync(
                DiagnosticEventFactory.Completed(Now.AddTicks(index), Suffix(index))))
            .ToArray();
        Assert.True(
            SpinWait.SpinUntil(
                () => store.PendingOperationCount == SanitizedLogStore.MaximumPendingOperations,
                TimeSpan.FromSeconds(5)));

        var rejected = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.AppendAsync(
            DiagnosticEventFactory.Completed(Now.AddTicks(100), Suffix(100))));
        Assert.Equal("diagnostic_store_busy", rejected.ResultCode);

        fault.Release();
        await first;
        await Task.WhenAll(queued);
        Assert.Equal(0, store.PendingOperationCount);
        Assert.Equal(
            SanitizedLogStore.MaximumPendingOperations,
            (await store.ReadOwnedRecordsForTestingAsync()).Count);
    }

    [Fact]
    public async Task RestartRecoveryRemovesARealCrashStagedFileAndReportsIt()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var exitCode = await RunCrashChildAsync(
            root,
            CrashRootEnvironmentVariable,
            nameof(CrashChildLeavesProtectedTemporaryRecord));
        Assert.NotEqual(0, exitCode);
        Assert.Single(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));

        await using var recovered = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));
        var cleanup = await recovered.CleanupAsync();

        Assert.Equal(1, cleanup.RemovedTemporaryFileCount);
        Assert.True(cleanup.RemovedTemporaryBytes > 0);
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RepeatedPreCommitCrashesReplaceOrphanResidueInsteadOfGrowingIt()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);

        var firstExit = await RunCrashChildAsync(
            root,
            CrashRootEnvironmentVariable,
            nameof(CrashChildLeavesProtectedTemporaryRecord));
        Assert.NotEqual(0, firstExit);
        Assert.Single(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));

        var secondExit = await RunCrashChildAsync(
            root,
            CrashRootEnvironmentVariable,
            nameof(CrashChildLeavesProtectedTemporaryRecord));

        Assert.NotEqual(0, secondExit);
        Assert.Single(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task IncompletePriorTemporaryCleanupFailsBeforeStagingNewBytes()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var orphan = OwnedTemporaryPath(root, Now);
        WriteRestrictedFile(root, orphan, [0x2A]);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now),
            new ThrowingTemporaryCleanupFaultInjector());

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
            store.AppendAsync(DiagnosticEventFactory.Completed(Now)));

        Assert.Equal("diagnostic_store_cleanup_failed", exception.ResultCode);
        Assert.Equal(orphan, Assert.Single(
            Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly)));
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RestartAfterCommittedTransactionCrashCompletesWithinTheConfiguredCeiling()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         new FixedTimeProvider(Now)))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        var exitCode = await RunCrashChildAsync(
            root,
            CommittedCrashRootEnvironmentVariable,
            nameof(CrashChildLeavesCommittedTransactionBeforeCleanup));

        Assert.NotEqual(0, exitCode);
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.True(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length) <= options.MaximumBytes);

        await using var recovered = SanitizedLogStore.OpenForTesting(
            root,
            options,
            new FixedTimeProvider(Now.AddTicks(1)));
        var cleanup = await recovered.CleanupAsync();

        Assert.True(cleanup.Completed);
        Assert.Equal(1, cleanup.RemovedRecordCount);
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        var surviving = Assert.Single(await recovered.ReadOwnedRecordsForTestingAsync());
        Assert.Contains($"event{Suffix(2)}", Encoding.UTF8.GetString(surviving), StringComparison.Ordinal);
        Assert.True(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length) <= options.MaximumBytes);
    }

    [Fact]
    public async Task RestartAfterEvictionBeforePublishRecoversTheCommittedReplacement()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         new FixedTimeProvider(Now)))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        var exitCode = await RunCrashChildAsync(
            root,
            RetainedCrashRootEnvironmentVariable,
            nameof(CrashChildStopsAfterRetentionBeforePublish));

        Assert.NotEqual(0, exitCode);
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));

        await using var recovered = SanitizedLogStore.OpenForTesting(
            root,
            options,
            new FixedTimeProvider(Now.AddTicks(1)));
        var cleanup = await recovered.CleanupAsync();

        Assert.True(cleanup.Completed);
        Assert.Equal(0, cleanup.RemovedRecordCount);
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        var surviving = Assert.Single(await recovered.ReadOwnedRecordsForTestingAsync());
        Assert.Contains($"event{Suffix(2)}", Encoding.UTF8.GetString(surviving), StringComparison.Ordinal);
        Assert.True(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length) <= options.MaximumBytes);
    }

    [Fact]
    public async Task CancellationRaisedAtTemporaryDeletionBoundaryDoesNotHidePartialCleanup()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var first = OwnedTemporaryPath(root, Now);
        var second = OwnedTemporaryPath(root, Now.AddTicks(1));
        WriteRestrictedFile(root, first, [0x2A]);
        WriteRestrictedFile(root, second, [0x2B]);
        using var cancellation = new CancellationTokenSource();
        var fault = new CancellingTemporaryCleanupFaultInjector(cancellation);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now),
            fault);

        var cleanup = await store.CleanupAsync(cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(cleanup.Completed);
        Assert.Equal(2, cleanup.RemovedTemporaryFileCount);
        Assert.Equal(2, cleanup.RemovedTemporaryBytes);
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task CrashChildLeavesProtectedTemporaryRecord()
    {
        var root = Environment.GetEnvironmentVariable(CrashRootEnvironmentVariable);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now),
            new ProcessTerminatingFaultInjector());
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now));
        throw new InvalidOperationException("The process-termination fault was not invoked.");
    }

    [Fact]
    public async Task CrashChildLeavesCommittedTransactionBeforeCleanup()
    {
        var root = Environment.GetEnvironmentVariable(CommittedCrashRootEnvironmentVariable);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(sampleLength + 16L),
            new FixedTimeProvider(Now.AddTicks(1)),
            new CommittedProcessTerminatingFaultInjector());
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2)));
        throw new InvalidOperationException("The committed-transaction process-termination fault was not invoked.");
    }

    [Fact]
    public async Task CrashChildStopsAfterRetentionBeforePublish()
    {
        var root = Environment.GetEnvironmentVariable(RetainedCrashRootEnvironmentVariable);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(sampleLength + 16L),
            new FixedTimeProvider(Now.AddTicks(1)),
            new PostRetentionProcessTerminatingFaultInjector());
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2)));
        throw new InvalidOperationException("The post-retention process-termination fault was not invoked.");
    }

    [Fact]
    public async Task CorruptUnknownAndDuplicateJsonRecordsFailClosedAndRemainForReview()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var valid = Encoding.UTF8.GetString(DiagnosticEventFactory.Completed(Now).GetUtf8Json());
        var candidates = new[]
        {
            Encoding.UTF8.GetBytes(valid[..^1]),
            [0xC3, 0x28],
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                JsonDocument.Parse(valid).RootElement,
                IndentedJson)),
            Encoding.UTF8.GetBytes(valid.Insert(1, "\"message\":\"SYNTHETIC-SECRET\",")),
            Encoding.UTF8.GetBytes(
                valid.Insert(1, "\"schema_version\":\"thirdlife.diagnostics.event.v1\",")),
            Encoding.UTF8.GetBytes(valid.Replace(
                "diagnostic.operation_completed",
                "diagnostic.operation_failed",
                StringComparison.Ordinal)),
        };
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));

        foreach (var candidate in candidates)
        {
            var path = OwnedEventPath(root, Now);
            WriteRestrictedFile(root, path, candidate);

            var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
            Assert.Equal("diagnostic_store_record_invalid", exception.ResultCode);
            Assert.True(File.Exists(path));
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidOwnedNamesAndFilenameTimestampMismatchFailClosedAndRemain()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var valid = DiagnosticEventFactory.Completed(Now).GetUtf8Json();
        var timestamp = Now.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var candidates = new[]
        {
            (Path.Combine(root, $"evt-invalid-{Guid.NewGuid():N}.json"), valid),
            (Path.Combine(root, $"evt-{timestamp}-{new string('A', 32)}.json"), valid),
            (Path.Combine(root, $".tmp-invalid-{Guid.NewGuid():N}.json"), new byte[] { 0x2A }),
            (Path.Combine(root, $".tmp-{timestamp}-{new string('A', 32)}.json"), new byte[] { 0x2A }),
            (Path.Combine(root, $".txn-invalid-{Guid.NewGuid():N}.json"), valid),
            (Path.Combine(root, $".txn-{timestamp}-{new string('A', 32)}.json"), valid),
            (OwnedTransactionPath(root, Now), new byte[] { 0xC3, 0x28 }),
            (OwnedEventPath(root, Now.AddTicks(1)), valid),
        };
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));

        foreach (var candidate in candidates)
        {
            WriteRestrictedFile(root, candidate.Item1, candidate.Item2);
            var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
            Assert.Equal("diagnostic_store_record_invalid", exception.ResultCode);
            Assert.True(File.Exists(candidate.Item1));
            File.Delete(candidate.Item1);
        }
    }

    [Fact]
    public async Task SameLengthStagedSubstitutionFailsBeforeEvictionOrPublish()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        var time = new FixedTimeProvider(Now);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(root, options, time))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        var replacement = DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(3)).GetUtf8Json();
        var fault = new SubstitutingStagedRecordFaultInjector(root, replacement);
        await using var store = SanitizedLogStore.OpenForTesting(root, options, time, fault);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
            store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2))));

        Assert.Equal("diagnostic_store_publish_invalid", exception.ResultCode);
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        var record = Assert.Single(await store.ReadOwnedRecordsForTestingAsync());
        Assert.Contains($"event{Suffix(1)}", Encoding.UTF8.GetString(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameLengthCommittedTransactionSubstitutionFailsBeforeEviction()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        var time = new FixedTimeProvider(Now);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(root, options, time))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        var replacement = DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(3)).GetUtf8Json();
        var fault = new SubstitutingCommittedTransactionFaultInjector(root, replacement);
        await using var store = SanitizedLogStore.OpenForTesting(root, options, time, fault);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
            store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2))));

        Assert.Equal("diagnostic_store_write_recovery_pending", exception.ResultCode);
        Assert.True(exception.DurableStateAmbiguous);
        Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        var finalPath = Assert.Single(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Contains($"event{Suffix(1)}", await File.ReadAllTextAsync(finalPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureAfterFinalPublishReportsAmbiguousDurabilityWithoutDuplicateState()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now),
            new ThrowingAfterPublishFaultInjector());

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
            store.AppendAsync(DiagnosticEventFactory.Completed(Now)));

        Assert.Equal("diagnostic_store_write_recovery_pending", exception.ResultCode);
        Assert.True(exception.DurableStateAmbiguous);
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(await store.ReadOwnedRecordsForTestingAsync());
    }

    [Fact]
    public async Task IncompleteCommittedCleanupIsAmbiguousAndCannotGrowRepeatedly()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        var time = new FixedTimeProvider(Now);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(root, options, time))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        await using (var store = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         time,
                         new ThrowingDeleteFaultInjector()))
        {
            var second = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2))));

            Assert.Equal("diagnostic_store_write_recovery_pending", second.ResultCode);
            Assert.True(second.DurableStateAmbiguous);
            Assert.Single(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
            Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));

            var blocked = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                store.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(2), Suffix(3))));
            Assert.Equal("diagnostic_store_write_recovery_pending", blocked.ResultCode);
            Assert.True(blocked.DurableStateAmbiguous);
            Assert.Single(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
            Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        }

        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));

        await using var recovered = SanitizedLogStore.OpenForTesting(root, options, time);
        var cleanup = await recovered.CleanupAsync();
        Assert.True(cleanup.Completed);
        Assert.Equal(1, cleanup.RemovedRecordCount);
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        var record = Assert.Single(await recovered.ReadOwnedRecordsForTestingAsync());
        Assert.Contains($"event{Suffix(2)}", Encoding.UTF8.GetString(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidTemporaryBlocksPendingTransactionRecoveryBeforeEviction()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        var time = new FixedTimeProvider(Now);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(root, options, time))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        await using (var interrupted = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         time,
                         new ThrowingDeleteFaultInjector()))
        {
            await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                interrupted.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2))));
        }

        var invalidTemporary = Path.Combine(root, $".tmp-invalid-{Guid.NewGuid():N}.json");
        WriteRestrictedFile(root, invalidTemporary, [0x2A]);
        await using var recovered = SanitizedLogStore.OpenForTesting(root, options, time);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => recovered.CleanupAsync());

        Assert.Equal("diagnostic_store_record_invalid", exception.ResultCode);
        Assert.True(File.Exists(invalidTemporary));
        var finalPath = Assert.Single(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        var seedRecord = await File.ReadAllBytesAsync(finalPath);
        Assert.Contains($"event{Suffix(1)}", Encoding.UTF8.GetString(seedRecord), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FutureClockPendingTransactionFailsBeforeEvictionAndRemainsForReview()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        var initialTime = new FixedTimeProvider(Now);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(root, options, initialTime))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        await using (var interrupted = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         initialTime,
                         new ThrowingDeleteFaultInjector()))
        {
            await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                interrupted.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2))));
        }

        var recoveryTime = new FixedTimeProvider(Now.AddMinutes(-10));
        await using var recovered = SanitizedLogStore.OpenForTesting(root, options, recoveryTime);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => recovered.CleanupAsync());

        Assert.Equal("diagnostic_store_clock_invalid", exception.ResultCode);
        var finalPath = Assert.Single(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        var seedRecord = await File.ReadAllBytesAsync(finalPath);
        Assert.Contains($"event{Suffix(1)}", Encoding.UTF8.GetString(seedRecord), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredPendingTransactionIsDeletedInsteadOfPublishedOrBlockingCleanup()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var sampleLength = DiagnosticEventFactory.Completed(Now).GetUtf8Json().Length;
        var options = new SanitizedLogStoreOptions(sampleLength + 16L);
        var initialTime = new FixedTimeProvider(Now);
        await using (var seedStore = SanitizedLogStore.OpenForTesting(root, options, initialTime))
        {
            await seedStore.AppendAsync(DiagnosticEventFactory.Completed(Now, Suffix(1)));
        }

        await using (var interrupted = SanitizedLogStore.OpenForTesting(
                         root,
                         options,
                         initialTime,
                         new ThrowingDeleteFaultInjector()))
        {
            await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                interrupted.AppendAsync(DiagnosticEventFactory.Completed(Now.AddTicks(1), Suffix(2))));
        }

        var recoveryTime = new FixedTimeProvider(Now.AddDays(15));
        await using var recovered = SanitizedLogStore.OpenForTesting(root, options, recoveryTime);

        var cleanup = await recovered.CleanupAsync();

        Assert.True(cleanup.Completed);
        Assert.Equal(1, cleanup.RemovedTemporaryFileCount);
        Assert.True(cleanup.RemovedTemporaryBytes > 0);
        Assert.Equal(1, cleanup.RemovedRecordCount);
        Assert.Empty(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SameLengthPathReplacementAfterValidationIsDetectedAndPreserved()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        var originalTime = Now - TimeSpan.FromDays(15);
        var time = new FixedTimeProvider(originalTime);
        var replacement = DiagnosticEventFactory.Completed(originalTime, Suffix(2)).GetUtf8Json();
        var fault = new ReplacingFaultInjector(replacement);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            time,
            fault);
        await store.AppendAsync(DiagnosticEventFactory.Completed(originalTime, Suffix(1)));
        var ownedPath = Assert.Single(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        fault.TargetPath = ownedPath;
        time.UtcNow = Now;

        var cleanup = await store.CleanupAsync();

        Assert.False(cleanup.Completed);
        Assert.Equal(0, cleanup.RemovedRecordCount);
        Assert.True(File.Exists(ownedPath));
        Assert.Equal(replacement, File.ReadAllBytes(ownedPath));
        Assert.True(File.Exists(fault.MovedPath));
    }

    [Fact]
    public async Task HistoricalBuildRecordsRemainReadableAndAgeOutWithoutRewritingProvenance()
    {
        using var temporary = new TemporaryDirectory();
        const string goldenPriorBuildRecord = "{\"schema_version\":\"thirdlife.diagnostics.event.v1\",\"event_id\":\"event00000000000000000000000000000001\",\"event_code\":\"operation_completed\",\"component\":\"diagnostics\",\"phase\":\"persist\",\"severity\":\"information\",\"occurred_at_utc\":\"2030-01-20T12:00:00+00:00\",\"correlation_id\":\"correlation00000000000000000000000000000001\",\"build_version\":\"0.9.0\",\"safe_message_key\":\"diagnostic.operation_completed\",\"operation_type\":\"persist_event\",\"result_code\":\"succeeded\",\"duration_ms\":125,\"bounded_count\":1}";
        var root = StorePath(temporary);
        var goldenBytes = Encoding.UTF8.GetBytes(goldenPriorBuildRecord);
        WriteRestrictedFile(root, OwnedEventPath(root, Now), goldenBytes);
        var time = new FixedTimeProvider(Now);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            time);

        var record = Assert.Single(await store.ReadOwnedRecordsForTestingAsync());
        Assert.Equal(goldenBytes, record);
        using (var document = JsonDocument.Parse(record))
        {
            Assert.Equal("0.9.0", document.RootElement.GetProperty("build_version").GetString());
        }

        time.UtcNow = Now + TimeSpan.FromDays(14) + TimeSpan.FromTicks(1);
        var cleanup = await store.CleanupAsync();
        Assert.Equal(1, cleanup.RemovedRecordCount);
        Assert.Empty(await store.ReadOwnedRecordsForTestingAsync());
    }

    [Fact]
    public async Task CleanupFailsClosedWhenOwnedFileIsHeldOrHardLinked()
    {
        using var temporary = new TemporaryDirectory();
        var expiredTimestamp = Now - TimeSpan.FromDays(15);
        var time = new FixedTimeProvider(expiredTimestamp);
        await using var store = SanitizedLogStore.OpenForTesting(
            StorePath(temporary),
            new SanitizedLogStoreOptions(64 * 1024),
            time);
        await store.AppendAsync(DiagnosticEventFactory.Completed(expiredTimestamp));
        time.UtcNow = Now;
        var ownedPath = Assert.Single(
            Directory.EnumerateFiles(StorePath(temporary), "evt-*.json", SearchOption.TopDirectoryOnly));

        var linkPath = Path.Combine(StorePath(temporary), "hardlink-copy.json");
        Assert.True(
            CreateHardLink(linkPath, ownedPath, IntPtr.Zero),
            Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture));
        var linked = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
        Assert.Equal("diagnostic_store_object_unsafe", linked.ResultCode);
        File.Delete(linkPath);

        await using var held = new FileStream(ownedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var heldFailure = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
        Assert.Equal("diagnostic_store_cleanup_failed", heldFailure.ResultCode);
    }

    [Fact]
    public async Task RootAndRecordsUseProtectedCurrentUserSystemAndAdministratorsAcls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now));
        var recordPath = Assert.Single(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));

        AssertProtectedAcl(
            new DirectoryInfo(root).GetAccessControl(
                AccessControlSections.Access | AccessControlSections.Owner));
        AssertProtectedAcl(
            new FileInfo(recordPath).GetAccessControl(
                AccessControlSections.Access | AccessControlSections.Owner));
        using var record = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(1U, WindowsDiagnosticFileIdentity.GetLinkCount(record.SafeFileHandle));
    }

    [Fact]
    public async Task PreExistingPermissiveRootFailsClosedWithoutPublishingAnEvent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        Directory.CreateDirectory(root);
        var directory = new DirectoryInfo(root);
        var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
            store.AppendAsync(DiagnosticEventFactory.Completed(Now)));

        Assert.Equal("diagnostic_store_object_unsafe", exception.ResultCode);
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task WidenedRecordAclFailsClosedAndPreservesTheRecord()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));
        await store.AppendAsync(DiagnosticEventFactory.Completed(Now));
        var recordPath = Assert.Single(
            Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        var file = new FileInfo(recordPath);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            FileSystemRights.Read,
            AccessControlType.Allow));
        file.SetAccessControl(security);

        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());

        Assert.Equal("diagnostic_store_object_unsafe", exception.ResultCode);
        Assert.True(File.Exists(recordPath));
    }

    [Fact]
    public async Task JunctionRootFailsClosedWithoutTouchingItsTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var target = Path.Combine(temporary.Path, "junction-target");
        var junction = StorePath(temporary);
        Directory.CreateDirectory(target);
        await CreateJunctionAsync(junction, target);
        try
        {
            await using var store = SanitizedLogStore.OpenForTesting(
                junction,
                new SanitizedLogStoreOptions(64 * 1024),
                new FixedTimeProvider(Now));
            var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
                store.AppendAsync(DiagnosticEventFactory.Completed(Now)));

            Assert.Equal("diagnostic_store_object_unsafe", exception.ResultCode);
            Assert.Empty(Directory.EnumerateFiles(target, "*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task FileEnumerationIsBoundedAndNeverDeletesUnknownFiles()
    {
        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));
        await store.CleanupAsync();

        for (var index = 0; index < SanitizedLogStore.MaximumOwnedFiles - 1; index++)
        {
            File.WriteAllBytes(Path.Combine(root, $"unknown-{index:D4}.bin"), [0x2A]);
        }

        var unknownDirectory = Path.Combine(root, "unknown-directory");
        Directory.CreateDirectory(unknownDirectory);

        var bounded = await store.CleanupAsync();
        Assert.Equal(0, bounded.RemovedRecordCount);
        Assert.Equal(
            SanitizedLogStore.MaximumOwnedFiles - 1,
            Directory.EnumerateFiles(root, "unknown-*.bin", SearchOption.TopDirectoryOnly).Count());
        Assert.True(Directory.Exists(unknownDirectory));

        var append = await Assert.ThrowsAsync<DiagnosticContractException>(() =>
            store.AppendAsync(DiagnosticEventFactory.Completed(Now)));
        Assert.Equal("diagnostic_store_file_count_exceeded", append.ResultCode);
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));

        File.WriteAllBytes(Path.Combine(root, "unknown-over-bound.bin"), [0x2A]);
        var exception = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
        Assert.Equal("diagnostic_store_file_count_exceeded", exception.ResultCode);
        Assert.Equal(
            SanitizedLogStore.MaximumOwnedFiles,
            Directory.EnumerateFiles(root, "unknown-*.bin", SearchOption.TopDirectoryOnly).Count());
    }

    [Fact]
    public async Task NestedReparseAndRecordShapedDirectoryObjectsFailClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var root = StorePath(temporary);
        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(Now));
        await store.CleanupAsync();
        var recordDirectory = OwnedEventPath(root, Now);
        Directory.CreateDirectory(recordDirectory);

        var recordObject = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
        Assert.Equal("diagnostic_store_object_unsafe", recordObject.ResultCode);
        Assert.True(Directory.Exists(recordDirectory));
        Directory.Delete(recordDirectory);

        var target = Path.Combine(temporary.Path, "nested-junction-target");
        var junction = Path.Combine(root, "unknown-junction");
        Directory.CreateDirectory(target);
        await CreateJunctionAsync(junction, target);
        try
        {
            var reparse = await Assert.ThrowsAsync<DiagnosticContractException>(() => store.CleanupAsync());
            Assert.Equal("diagnostic_store_object_unsafe", reparse.ResultCode);
            Assert.True(Directory.Exists(target));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void RetentionCannotExceedApprovedDefaultAndStoreSizeIsBounded()
    {
        Assert.Equal(TimeSpan.FromDays(14), SanitizedLogStoreOptions.DefaultRetentionAge);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SanitizedLogStoreOptions(1024, TimeSpan.FromDays(14) + TimeSpan.FromTicks(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SanitizedLogStoreOptions(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SanitizedLogStoreOptions(SanitizedLogStoreOptions.MaximumConfigurableBytes + 1));
    }

    private static string Suffix(int value) => value.ToString("D32", System.Globalization.CultureInfo.InvariantCulture);

    private static string StorePath(TemporaryDirectory temporary) => Path.Combine(temporary.Path, "logs");

    private static string OwnedEventPath(string root, DateTimeOffset timestamp) => Path.Combine(
        root,
        $"evt-{timestamp:yyyyMMdd'T'HHmmssfffffff'Z'}-{Guid.NewGuid():N}.json");

    private static string OwnedTemporaryPath(string root, DateTimeOffset timestamp) => Path.Combine(
        root,
        $".tmp-{timestamp:yyyyMMdd'T'HHmmssfffffff'Z'}-{Guid.NewGuid():N}.json");

    private static string OwnedTransactionPath(string root, DateTimeOffset timestamp) => Path.Combine(
        root,
        $".txn-{timestamp:yyyyMMdd'T'HHmmssfffffff'Z'}-{Guid.NewGuid():N}.json");

    private static void WriteRestrictedFile(string root, string path, byte[] bytes)
    {
        using var security = new DiagnosticStoreSecurity(root);
        security.EnsureSafeRoot();
        using var stream = security.CreateRestrictedFile(
            path,
            FileShare.None,
            FileOptions.WriteThrough,
            bufferSize: 4096);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertProtectedAcl(FileSystemSecurity security)
    {
        Assert.True(security.AreAccessRulesProtected);
        var currentUser = WindowsIdentity.GetCurrent().User ??
            throw new InvalidOperationException("The current Windows test identity is unavailable.");
        var allowedSids = new HashSet<SecurityIdentifier>
        {
            currentUser,
            new(WellKnownSidType.LocalSystemSid, domainSid: null),
            new(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
        };
        Assert.Contains(
            Assert.IsType<SecurityIdentifier>(security.GetOwner(typeof(SecurityIdentifier))),
            allowedSids);

        var fullControlSids = new HashSet<SecurityIdentifier>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: true,
                     typeof(SecurityIdentifier)))
        {
            Assert.False(rule.IsInherited);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            var identity = Assert.IsType<SecurityIdentifier>(rule.IdentityReference);
            Assert.Contains(identity, allowedSids);
            if ((rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            {
                fullControlSids.Add(identity);
            }
        }

        Assert.True(allowedSids.SetEquals(fullControlSids));
    }

    private static async Task<int> RunCrashChildAsync(
        string root,
        string environmentVariable,
        string testMethod)
    {
        using var process = StartTestChild(
            testMethod,
            new Dictionary<string, string> { [environmentVariable] = root });
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        _ = await standardOutput;
        _ = await standardError;
        return process.ExitCode;
    }

    private static Process StartTestChild(
        string testMethod,
        IReadOnlyDictionary<string, string> environment)
    {
        var assemblyPath = typeof(SanitizedLogStoreTests).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add($"--TestAdapterPath:{Path.GetDirectoryName(assemblyPath)}");
        startInfo.ArgumentList.Add($"--Tests:{typeof(SanitizedLogStoreTests).FullName}.{testMethod}");
        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The bounded diagnostic-store child process did not start.");
    }

    private static async Task CreateJunctionAsync(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The bounded junction test helper did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await standardOutput;
        _ = await standardError;
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class BlockingFaultInjector : ILogStoreFaultInjector, IDisposable
    {
        private readonly ManualResetEventSlim _blocked = new(initialState: false);
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private int _invocations;

        public bool WaitUntilBlocked(TimeSpan timeout) => _blocked.Wait(timeout);

        public void Release() => _release.Set();

        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point != LogStoreFaultPoint.AfterTemporaryWriteBeforePublish ||
                Interlocked.Increment(ref _invocations) != 1)
            {
                return;
            }

            _blocked.Set();
            Assert.True(_release.Wait(TimeSpan.FromSeconds(15)));
        }

        public void Dispose()
        {
            _blocked.Dispose();
            _release.Dispose();
        }
    }

    private sealed class ProcessTerminatingFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.AfterTemporaryWriteBeforePublish)
            {
                Environment.FailFast("Synthetic TL-0104 crash-stage interruption.");
            }
        }
    }

    private sealed class CommittedProcessTerminatingFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.AfterTransactionCommitBeforeCleanup)
            {
                Environment.FailFast("Synthetic TL-0104 committed-transaction interruption.");
            }
        }
    }

    private sealed class PostRetentionProcessTerminatingFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.AfterRetentionBeforePublish)
            {
                Environment.FailFast("Synthetic TL-0104 post-retention interruption.");
            }
        }
    }

    private sealed class CancellingTemporaryCleanupFaultInjector(CancellationTokenSource cancellation)
        : ILogStoreFaultInjector
    {
        private int _invocations;

        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.BeforeOwnedTemporaryDelete &&
                Interlocked.Increment(ref _invocations) == 1)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class ThrowingTemporaryCleanupFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.BeforeOwnedTemporaryDelete)
            {
                throw new IOException("Synthetic bounded temporary cleanup failure.");
            }
        }
    }

    private sealed class SubstitutingStagedRecordFaultInjector(string root, byte[] replacement)
        : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point != LogStoreFaultPoint.AfterTemporaryWriteBeforePublish)
            {
                return;
            }

            var temporaryPath = Assert.Single(
                Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
            Assert.Equal(new FileInfo(temporaryPath).Length, replacement.LongLength);
            File.WriteAllBytes(temporaryPath, replacement);
        }
    }

    private sealed class SubstitutingCommittedTransactionFaultInjector(string root, byte[] replacement)
        : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point != LogStoreFaultPoint.AfterTransactionCommitBeforeCleanup)
            {
                return;
            }

            var transactionPath = Assert.Single(
                Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
            Assert.Equal(new FileInfo(transactionPath).Length, replacement.LongLength);
            File.WriteAllBytes(transactionPath, replacement);
        }
    }

    private sealed class ReplacingFaultInjector(byte[] replacement) : ILogStoreFaultInjector
    {
        public string? TargetPath { get; set; }

        public string MovedPath { get; private set; } = string.Empty;

        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point != LogStoreFaultPoint.AfterOwnedRecordValidatedBeforeDelete)
            {
                return;
            }

            var target = TargetPath ?? throw new InvalidOperationException("The replacement target is unavailable.");
            Assert.Equal(new FileInfo(target).Length, replacement.LongLength);
            MovedPath = string.Concat(target, ".moved");
            File.Move(target, MovedPath, overwrite: false);
            File.WriteAllBytes(target, replacement);
        }
    }

    private sealed class ThrowingFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.AfterTemporaryWriteBeforePublish)
            {
                throw new IOException("SYNTHETIC-SENSITIVE-PATH-CONTENT");
            }
        }
    }

    private sealed class DiskFullFaultInjector(LogStoreFaultPoint selectedPoint) : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == selectedPoint)
            {
                throw new IOException("Synthetic storage capacity exhaustion.");
            }
        }
    }

    private sealed class ThrowingDeleteFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.BeforeOwnedRecordDelete)
            {
                throw new IOException("Synthetic bounded cleanup failure.");
            }
        }
    }

    private sealed class ThrowingAfterPublishFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.AfterPublishBeforeReturn)
            {
                throw new IOException("Synthetic post-publication failure.");
            }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);
}
