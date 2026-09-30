using System.Text;
using System.Text.Json;
using ThirdLife.Diagnostics.Logging;

namespace ThirdLife.Diagnostics.Tests;

public sealed class ExceptionSanitizerTests
{
    private const string SensitiveSeed = "TOKEN-SYNTHETIC C:\\Users\\SyntheticPerson 192.0.2.44";

    public static TheoryData<Exception, DiagnosticResultCode, DiagnosticErrorCategory, bool> KnownFailures => new()
    {
        { new OperationCanceledException(SensitiveSeed), DiagnosticResultCode.OperationCancelled, DiagnosticErrorCategory.Cancellation, false },
        { new TimeoutException(SensitiveSeed), DiagnosticResultCode.OperationTimedOut, DiagnosticErrorCategory.Timeout, true },
        { new UnauthorizedAccessException(SensitiveSeed), DiagnosticResultCode.AccessDenied, DiagnosticErrorCategory.AccessDenied, false },
        { new IOException(SensitiveSeed), DiagnosticResultCode.IoFailure, DiagnosticErrorCategory.IoFailure, true },
        { new ArgumentException(SensitiveSeed), DiagnosticResultCode.InvalidInput, DiagnosticErrorCategory.InvalidInput, false },
        { new InvalidOperationException(SensitiveSeed), DiagnosticResultCode.InvalidState, DiagnosticErrorCategory.InvalidState, false },
        { new SyntheticUnknownException(SensitiveSeed), DiagnosticResultCode.UnexpectedFailure, DiagnosticErrorCategory.UnexpectedFailure, false },
    };

    [Theory]
    [MemberData(nameof(KnownFailures))]
    public void KnownAndUnknownExceptionsMapOnlyToStableSafeValues(
        Exception exception,
        DiagnosticResultCode result,
        DiagnosticErrorCategory category,
        bool retryable)
    {
        exception.Data["raw"] = SensitiveSeed;
        exception.Source = SensitiveSeed;

        var sanitized = ExceptionSanitizer.Sanitize(exception);

        Assert.Equal(result, sanitized.ResultCode);
        Assert.Equal(category, sanitized.ErrorCategory);
        Assert.Equal(retryable, sanitized.Retryable);
        Assert.True(sanitized.DurableStateAmbiguous);
        Assert.DoesNotContain(SensitiveSeed, sanitized.DisplayMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizerNeverReadsHostileExceptionTextAccessors()
    {
        var hostile = new HostileException();

        var sanitized = ExceptionSanitizer.Sanitize(hostile);

        Assert.Equal(DiagnosticResultCode.UnexpectedFailure, sanitized.ResultCode);
        Assert.False(hostile.MessageRead);
        Assert.False(hostile.ToStringRead);
        Assert.DoesNotContain(SensitiveSeed, sanitized.DisplayMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledExceptionIsSanitizedBeforeFirstWriteAndDisplay()
    {
        using var temporary = new TemporaryDirectory();
        var time = new FixedTimeProvider(TimeProvider.System.GetUtcNow());
        await using var store = SanitizedLogStore.OpenForTesting(
            Path.Combine(temporary.Path, "logs"),
            new SanitizedLogStoreOptions(64 * 1024),
            time);
        var logger = new StructuredDiagnosticLogger(store);
        var exception = new SyntheticUnknownException(SensitiveSeed, new InvalidOperationException(SensitiveSeed));
        exception.Data["raw"] = SensitiveSeed;
        exception.Source = SensitiveSeed;

        var result = await logger.RecordUnhandledExceptionForTestingAsync(
            exception,
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"));

        var records = await store.ReadOwnedRecordsForTestingAsync();
        var record = Assert.Single(records);
        var persisted = Encoding.UTF8.GetString(record);
        Assert.DoesNotContain(SensitiveSeed, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inner", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticWriteStatus.Written, result.WriteStatus);
        Assert.DoesNotContain(SensitiveSeed, result.Failure.DisplayMessage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(KnownFailures))]
    public async Task LoggerAcceptsEveryRegisteredSanitizedFailureTuple(
        Exception exception,
        DiagnosticResultCode expectedResult,
        DiagnosticErrorCategory expectedCategory,
        bool expectedRetryable)
    {
        using var temporary = new TemporaryDirectory();
        await using var store = SanitizedLogStore.OpenForTesting(
            Path.Combine(temporary.Path, "logs"),
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(TimeProvider.System.GetUtcNow()));
        var result = await new StructuredDiagnosticLogger(store).RecordUnhandledExceptionForTestingAsync(
            exception,
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"));

        Assert.Equal(DiagnosticWriteStatus.Written, result.WriteStatus);
        Assert.Equal(expectedResult, result.Failure.ResultCode);
        Assert.Equal(expectedCategory, result.Failure.ErrorCategory);
        Assert.Equal(expectedRetryable, result.Failure.Retryable);
    }

    [Fact]
    public async Task DiagnosticWriteFailureNeverHidesTheSanitizedCrashResult()
    {
        using var temporary = new TemporaryDirectory();
        await using var store = SanitizedLogStore.OpenForTesting(
            Path.Combine(temporary.Path, "logs"),
            new SanitizedLogStoreOptions(1),
            new FixedTimeProvider(TimeProvider.System.GetUtcNow()));
        var logger = new StructuredDiagnosticLogger(store);
        var exception = new SyntheticUnknownException(SensitiveSeed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await logger.RecordUnhandledExceptionForTestingAsync(
            exception,
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"),
            cancellation.Token);
        var unavailable = await logger.RecordUnhandledExceptionForTestingAsync(
            exception,
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000002"));

        Assert.Equal(DiagnosticWriteStatus.Cancelled, cancelled.WriteStatus);
        Assert.Equal(DiagnosticWriteStatus.Unavailable, unavailable.WriteStatus);
        Assert.All(
            new[] { cancelled, unavailable },
            result =>
            {
                Assert.Equal(DiagnosticResultCode.UnexpectedFailure, result.Failure.ResultCode);
                Assert.DoesNotContain(SensitiveSeed, result.Failure.DisplayMessage, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task UnhandledExceptionReportsRecoveryPendingWhenCommittedRetentionCannotComplete()
    {
        using var temporary = new TemporaryDirectory();
        var now = TimeProvider.System.GetUtcNow();
        var representativeUnhandled = StructuredDiagnosticEvent.CreateForTesting(
            DiagnosticEventCode.UnhandledException,
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticSeverity.Error,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"),
            [
                DiagnosticEventField.ResultCode(DiagnosticResultCode.UnexpectedFailure),
                DiagnosticEventField.SanitizedErrorCategory(DiagnosticErrorCategory.UnexpectedFailure),
                DiagnosticEventField.Retryable(false),
                DiagnosticEventField.LimitationCode(DiagnosticLimitationCode.DurableStateAmbiguous),
            ],
            "event00000000000000000000000000000001",
            now);
        var maximumBytes = Math.Max(
            representativeUnhandled.GetUtf8Json().Length,
            DiagnosticEventFactory.Completed(now).GetUtf8Json().Length) + 16L;
        var root = Path.Combine(temporary.Path, "logs");
        var options = new SanitizedLogStoreOptions(maximumBytes);
        var time = new FixedTimeProvider(now);
        await using (var seed = SanitizedLogStore.OpenForTesting(root, options, time))
        {
            await seed.AppendAsync(DiagnosticEventFactory.Completed(now));
        }

        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            options,
            time,
            new ThrowingCleanupFaultInjector());
        var result = await new StructuredDiagnosticLogger(store).RecordUnhandledExceptionForTestingAsync(
            new SyntheticUnknownException(SensitiveSeed),
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000002"));

        Assert.Equal(DiagnosticWriteStatus.WriteStateAmbiguous, result.WriteStatus);
        Assert.Single(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, ".txn-*.json", SearchOption.TopDirectoryOnly));
        Assert.DoesNotContain(SensitiveSeed, result.Failure.DisplayMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledExceptionReportsUnavailableWhenPriorTemporaryCleanupCannotComplete()
    {
        using var temporary = new TemporaryDirectory();
        var now = TimeProvider.System.GetUtcNow();
        var root = Path.Combine(temporary.Path, "logs");
        using (var security = new DiagnosticStoreSecurity(root))
        {
            security.EnsureSafeRoot();
            var temporaryPath = Path.Combine(
                root,
                $".tmp-{now:yyyyMMdd'T'HHmmssfffffff'Z'}-{Guid.NewGuid():N}.json");
            using var stream = security.CreateRestrictedFile(
                temporaryPath,
                FileShare.None,
                FileOptions.WriteThrough,
                bufferSize: 4096);
            stream.WriteByte(0x2A);
            stream.Flush(flushToDisk: true);
        }

        await using var store = SanitizedLogStore.OpenForTesting(
            root,
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(now),
            new ThrowingTemporaryCleanupFaultInjector());
        var result = await new StructuredDiagnosticLogger(store).RecordUnhandledExceptionForTestingAsync(
            new SyntheticUnknownException(SensitiveSeed),
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect,
            DiagnosticCorrelationId.CreateForTesting("correlation00000000000000000000000000000001"));

        Assert.Equal(DiagnosticWriteStatus.Unavailable, result.WriteStatus);
        Assert.Empty(Directory.EnumerateFiles(root, "evt-*.json", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(root, ".tmp-*.json", SearchOption.TopDirectoryOnly));
        Assert.DoesNotContain(SensitiveSeed, result.Failure.DisplayMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicCrashRecorderCreatesAFreshCorrelationForEveryRecord()
    {
        using var temporary = new TemporaryDirectory();
        await using var store = SanitizedLogStore.OpenForTesting(
            Path.Combine(temporary.Path, "logs"),
            new SanitizedLogStoreOptions(64 * 1024),
            new FixedTimeProvider(TimeProvider.System.GetUtcNow()));
        var logger = new StructuredDiagnosticLogger(store);

        var first = await logger.RecordUnhandledExceptionAsync(
            new SyntheticUnknownException(SensitiveSeed),
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect);
        var second = await logger.RecordUnhandledExceptionAsync(
            new SyntheticUnknownException(SensitiveSeed),
            DiagnosticComponent.Inventory,
            DiagnosticPhase.Collect);

        var records = await store.ReadOwnedRecordsForTestingAsync();
        var correlations = new List<string?>();
        foreach (var bytes in records)
        {
            using var document = JsonDocument.Parse(bytes);
            correlations.Add(document.RootElement.GetProperty("correlation_id").GetString());
        }
        Assert.Equal(DiagnosticWriteStatus.Written, first.WriteStatus);
        Assert.Equal(DiagnosticWriteStatus.Written, second.WriteStatus);
        Assert.Equal(2, correlations.Count);
        Assert.All(correlations, static correlation => Assert.Matches("^[0-9a-f]{32}$", correlation));
        Assert.Equal(2, correlations.Distinct(StringComparer.Ordinal).Count());
    }

    private sealed class ThrowingCleanupFaultInjector : ILogStoreFaultInjector
    {
        public void ThrowIfRequested(LogStoreFaultPoint point)
        {
            if (point == LogStoreFaultPoint.BeforeOwnedRecordDelete)
            {
                throw new IOException("Synthetic bounded cleanup failure.");
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

    private sealed class HostileException : Exception
    {
        public bool MessageRead { get; private set; }

        public bool ToStringRead { get; private set; }

        public override string Message
        {
            get
            {
                MessageRead = true;
                throw new InvalidOperationException(SensitiveSeed);
            }
        }

        public override string ToString()
        {
            ToStringRead = true;
            throw new InvalidOperationException(SensitiveSeed);
        }
    }

    private sealed class SyntheticUnknownException : Exception
    {
        public SyntheticUnknownException(string message)
            : base(message)
        {
        }

        public SyntheticUnknownException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
