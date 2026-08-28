using System.Globalization;
using System.Security.Cryptography;

namespace ThirdLife.Diagnostics.Logging;

public sealed class SanitizedLogStoreOptions
{
    public static readonly TimeSpan DefaultRetentionAge = TimeSpan.FromDays(14);
    public const long MaximumConfigurableBytes = 256L * 1024 * 1024;

    public SanitizedLogStoreOptions(long maximumBytes, TimeSpan? retentionAge = null)
    {
        if (maximumBytes <= 0 || maximumBytes > MaximumConfigurableBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes),
                maximumBytes,
                $"The configured sanitized-log ceiling must be between 1 and {MaximumConfigurableBytes} bytes.");
        }

        var selectedAge = retentionAge ?? DefaultRetentionAge;
        if (selectedAge <= TimeSpan.Zero || selectedAge > DefaultRetentionAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionAge),
                selectedAge,
                "Sanitized-log retention must be positive and must not exceed the approved 14-day default.");
        }

        MaximumBytes = maximumBytes;
        RetentionAge = selectedAge;
    }

    public long MaximumBytes { get; }

    public TimeSpan RetentionAge { get; }
}

public sealed class SanitizedLogCleanupResult
{
    internal SanitizedLogCleanupResult(
        int removedRecordCount,
        long removedBytes,
        int removedTemporaryFileCount = 0,
        long removedTemporaryBytes = 0,
        bool completed = true)
    {
        RemovedRecordCount = removedRecordCount;
        RemovedBytes = removedBytes;
        RemovedTemporaryFileCount = removedTemporaryFileCount;
        RemovedTemporaryBytes = removedTemporaryBytes;
        Completed = completed;
    }

    public int RemovedRecordCount { get; }

    public long RemovedBytes { get; }

    public int RemovedTemporaryFileCount { get; }

    public long RemovedTemporaryBytes { get; }

    public bool Completed { get; }
}

public sealed class SanitizedLogAppendResult
{
    internal SanitizedLogAppendResult(SanitizedLogCleanupResult cleanup)
    {
        RemovedRecordCount = cleanup.RemovedRecordCount;
        RemovedBytes = cleanup.RemovedBytes;
        RemovedTemporaryFileCount = cleanup.RemovedTemporaryFileCount;
        RemovedTemporaryBytes = cleanup.RemovedTemporaryBytes;
        CleanupCompleted = cleanup.Completed;
    }

    public int RemovedRecordCount { get; }

    public long RemovedBytes { get; }

    public int RemovedTemporaryFileCount { get; }

    public long RemovedTemporaryBytes { get; }

    public bool CleanupCompleted { get; }
}

public sealed class SanitizedLogStore : IAsyncDisposable
{
    internal const int MaximumOwnedFiles = 4096;
    internal const int MaximumPendingOperations = 64;
    private const int MaximumRootLockAttempts = 500;
    private const string EventPrefix = "evt-";
    private const string EventSuffix = ".json";
    private const string TemporaryPrefix = ".tmp-";
    private const string TransactionPrefix = ".txn-";
    private const string RootLockFileName = ".diagnostic-store.lock";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";
    private const int TimestampTextLength = 23;
    private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);

    private readonly string _rootPath;
    private readonly SanitizedLogStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogStoreFaultInjector? _faultInjector;
    private readonly DiagnosticStoreSecurity _security;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pendingOperations;
    private bool _disposed;

    private SanitizedLogStore(
        string rootPath,
        SanitizedLogStoreOptions options,
        TimeProvider timeProvider,
        ILogStoreFaultInjector? faultInjector)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _options = options;
        _timeProvider = timeProvider;
        _faultInjector = faultInjector;
        _security = new DiagnosticStoreSecurity(_rootPath);
    }

    public static SanitizedLogStore OpenDefault(SanitizedLogStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_unavailable",
                "The registered local diagnostic-store root is unavailable.");
        }

        var root = Path.Combine(localApplicationData, "ThirdLife", "SetupCore", "Logs");
        return new SanitizedLogStore(root, options, TimeProvider.System, faultInjector: null);
    }

    internal static SanitizedLogStore OpenForTesting(
        string rootPath,
        SanitizedLogStoreOptions options,
        TimeProvider timeProvider,
        ILogStoreFaultInjector? faultInjector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new SanitizedLogStore(rootPath, options, timeProvider, faultInjector);
    }

    internal string RootPath => _rootPath;

    internal int PendingOperationCount => Volatile.Read(ref _pendingOperations);

    public async Task<SanitizedLogAppendResult> AppendAsync(
        StructuredDiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = diagnosticEvent.GetUtf8Json();
        if (bytes.LongLength > _options.MaximumBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_too_large",
                "The sanitized diagnostic record exceeds the configured store ceiling.");
        }

        ValidateIncomingTimestamp(diagnosticEvent.OccurredAtUtc);

        await EnterOperationGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _security.EnsureSafeRoot();
            using var rootLock = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var recovered = RecoverPendingTransaction();
            var temporaryCleanup = PlanTemporaryCleanup(cancellationToken);
            var removedTemporaries = ExecuteTemporaryCleanup(temporaryCleanup);
            if (!removedTemporaries.Completed)
            {
                throw new DiagnosticContractException(
                    "diagnostic_store_cleanup_failed",
                    "Verified prior diagnostic residue must be cleaned before another record is staged.");
            }

            var retention = PlanRetention(bytes.LongLength, cancellationToken);
            StagedRecord? staged = null;
            try
            {
                staged = await StageRecordAsync(diagnosticEvent, bytes, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateStagedRecord(staged);
                CommitStagedRecord(staged);
                _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterTransactionCommitBeforeCleanup);

                // The committed transaction is recoverable, so no caller cancellation is observed
                // while the bounded eviction and final publication complete.
                ValidateStagedPath(staged, staged.TransactionPath);
                var removedRecords = ExecuteRetention(retention);
                if (!removedRecords.Completed)
                {
                    throw RecoveryPending();
                }

                _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterRetentionBeforePublish);
                PublishStagedRecord(staged);
                _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterPublishBeforeReturn);
                var cleanup = CombineCleanup(
                    CombineCleanup(recovered, removedRecords),
                    removedTemporaries);
                return new SanitizedLogAppendResult(cleanup);
            }
            catch (Exception)
            {
                if (staged is { Committed: false, Published: false } &&
                    !DeleteExactSafeTemporary(staged.TemporaryPath))
                {
                    throw new DiagnosticContractException(
                        "diagnostic_store_cleanup_failed",
                        "A verified owned temporary diagnostic file could not be removed.");
                }

                if (staged is { Committed: true } or { Published: true })
                {
                    throw RecoveryPending();
                }

                throw;
            }
        }
        catch (DiagnosticContractException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SystemException)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_unavailable",
                "The sanitized diagnostic store could not complete the operation.");
        }
        finally
        {
            ExitOperationGate();
        }
    }

    public async Task<SanitizedLogCleanupResult> CleanupAsync(
        CancellationToken cancellationToken = default)
    {
        await EnterOperationGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _security.EnsureSafeRoot();
            using var rootLock = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var recovered = RecoverPendingTransaction();
            var temporaryCleanup = PlanTemporaryCleanup(cancellationToken);
            var retention = PlanRetention(incomingBytes: 0, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var removedTemporaries = ExecuteTemporaryCleanup(temporaryCleanup);
            return CombineCleanup(
                CombineCleanup(recovered, ExecuteRetention(retention)),
                removedTemporaries);
        }
        catch (DiagnosticContractException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SystemException)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_cleanup_failed",
                "The sanitized diagnostic store could not complete bounded cleanup.");
        }
        finally
        {
            ExitOperationGate();
        }
    }

    internal async Task<IReadOnlyList<byte[]>> ReadOwnedRecordsForTestingAsync(
        CancellationToken cancellationToken = default)
    {
        await EnterOperationGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _security.EnsureSafeRoot();
            using var rootLock = await AcquireRootLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _ = RecoverPendingTransaction();
            var records = EnumerateStoreFiles(cancellationToken).Records
                .OrderBy(static record => record.TimestampUtc)
                .ThenBy(static record => record.Path, StringComparer.Ordinal)
                .ToArray();
            var result = new List<byte[]>(records.Length);
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(ReadAndValidateOwnedRecord(record));
            }

            return result.AsReadOnly();
        }
        finally
        {
            ExitOperationGate();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            _security.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private SanitizedLogCleanupResult RecoverPendingTransaction()
    {
        // Validate every temporary-shaped object and reject every reparse object before a
        // committed transaction can evict a final record. Cleanup is planned here only;
        // the ordinary operation performs the verified deletions after recovery.
        _ = PlanTemporaryCleanup(CancellationToken.None);
        var pending = FindPendingTransaction();
        if (pending is null)
        {
            return new SanitizedLogCleanupResult(0, 0);
        }

        var now = _timeProvider.GetUtcNow();
        if (pending.TimestampUtc > now + MaximumFutureClockSkew)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_clock_invalid",
                "A committed diagnostic transaction timestamp is unexpectedly in the future.",
                durableStateAmbiguous: true);
        }

        if (pending.TimestampUtc < now - _options.RetentionAge)
        {
            ValidatePendingTransaction(pending);
            try
            {
                DeletePendingTransaction(pending);
            }
            catch (Exception exception) when (exception is DiagnosticContractException or
                IOException or
                UnauthorizedAccessException)
            {
                throw RecoveryPending();
            }

            return new SanitizedLogCleanupResult(
                removedRecordCount: 0,
                removedBytes: 0,
                removedTemporaryFileCount: 1,
                removedTemporaryBytes: pending.Length);
        }

        var retention = PlanRetention(
            pending.Length,
            CancellationToken.None,
            incomingAlreadyPresent: true);
        ValidatePendingTransaction(pending);
        var cleanup = ExecuteRetention(retention);
        if (!cleanup.Completed)
        {
            throw RecoveryPending();
        }

        File.Move(pending.Path, pending.FinalPath, overwrite: false);
        if (!TryParseOwnedRecord(pending.FinalPath, out var published) ||
            published.TimestampUtc != pending.TimestampUtc ||
            published.Length != pending.Length ||
            !string.Equals(
                published.ContentDigestSha256,
                pending.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw RecoveryPending();
        }

        return cleanup;
    }

    private void ValidatePendingTransaction(PendingTransaction expected)
    {
        if (!TryParseTransaction(expected.Path, out var current) ||
            current.TimestampUtc != expected.TimestampUtc ||
            current.Length != expected.Length ||
            !string.Equals(current.FinalPath, expected.FinalPath, StringComparison.Ordinal) ||
            !string.Equals(
                current.ContentDigestSha256,
                expected.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A committed diagnostic transaction changed before retention cleanup.",
                durableStateAmbiguous: true);
        }
    }

    private void DeletePendingTransaction(PendingTransaction pending)
    {
        _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.BeforeOwnedTemporaryDelete);
        using (var stream = _security.OpenValidatedRecordForDeletion(pending.Path))
        {
            ValidateOpenedPendingTransaction(stream, pending);
            _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterOwnedTemporaryValidatedBeforeDelete);
            ValidateOpenedPendingTransaction(stream, pending);
            _security.DeleteOpenedRecord(pending.Path, stream);
        }

        if (File.Exists(pending.Path))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A committed diagnostic transaction path changed during expired-state cleanup.",
                durableStateAmbiguous: true);
        }
    }

    private static void ValidateOpenedPendingTransaction(FileStream stream, PendingTransaction pending)
    {
        if (stream.Length != pending.Length ||
            stream.Length is <= 0 or > StructuredDiagnosticEvent.MaximumSerializedBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A committed diagnostic transaction changed before expired-state cleanup.",
                durableStateAmbiguous: true);
        }

        stream.Position = 0;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (!PersistedDiagnosticRecordValidator.IsCanonical(bytes, pending.TimestampUtc) ||
            !string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                pending.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A committed diagnostic transaction changed before expired-state cleanup.",
                durableStateAmbiguous: true);
        }
    }

    private PendingTransaction? FindPendingTransaction()
    {
        PendingTransaction? pending = null;
        var enumeratedCount = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
        };

        foreach (var path in Directory.EnumerateFileSystemEntries(_rootPath, "*", options))
        {
            if (IsRootLockPath(path))
            {
                continue;
            }

            enumeratedCount++;
            if (enumeratedCount > MaximumOwnedFiles)
            {
                throw new DiagnosticContractException(
                    "diagnostic_store_file_count_exceeded",
                    "The diagnostic store contains too many objects to inspect safely.");
            }

            if (!LooksLikeOwnedTransaction(path))
            {
                continue;
            }

            if (!IsRegularNonReparseFile(path) || !TryParseTransaction(path, out var candidate))
            {
                throw InvalidOwnedRecord();
            }

            if (pending is not null)
            {
                throw new DiagnosticContractException(
                    "diagnostic_store_transaction_conflict",
                    "The diagnostic store contains more than one pending write transaction.",
                    durableStateAmbiguous: true);
            }

            pending = candidate;
        }

        return pending;
    }

    private bool TryParseTransaction(string path, out PendingTransaction transaction)
    {
        transaction = default!;
        var fileName = Path.GetFileName(path);
        if (fileName.Length != TransactionPrefix.Length + TimestampTextLength + 1 + 32 + EventSuffix.Length)
        {
            return false;
        }

        var timestampText = fileName.Substring(TransactionPrefix.Length, TimestampTextLength);
        var separatorIndex = TransactionPrefix.Length + TimestampTextLength;
        var identifier = fileName.Substring(separatorIndex + 1, 32);
        if (fileName[separatorIndex] != '-' ||
            identifier.Any(static character => !IsLowerHexDigit(character)) ||
            !DateTimeOffset.TryParseExact(
                timestampText,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return false;
        }

        using var stream = _security.OpenValidatedRecord(path, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length is <= 0 or > StructuredDiagnosticEvent.MaximumSerializedBytes)
        {
            return false;
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (!PersistedDiagnosticRecordValidator.IsCanonical(bytes, timestamp))
        {
            return false;
        }

        var finalPath = Path.Combine(_rootPath, $"{EventPrefix}{timestampText}-{identifier}{EventSuffix}");
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_transaction_conflict",
                "A pending diagnostic transaction conflicts with its final record.",
                durableStateAmbiguous: true);
        }

        transaction = new PendingTransaction(
            path,
            finalPath,
            timestamp,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return true;
    }

    private RetentionPlan PlanRetention(
        long incomingBytes,
        CancellationToken cancellationToken,
        bool incomingAlreadyPresent = false)
    {
        var enumeration = EnumerateStoreFiles(cancellationToken);
        if (incomingBytes > 0 && !incomingAlreadyPresent &&
            enumeration.TotalFileCount >= MaximumOwnedFiles)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_file_count_exceeded",
                "The diagnostic store has reached its bounded file count.");
        }

        var records = enumeration.Records
            .OrderBy(static record => record.TimestampUtc)
            .ThenBy(static record => record.Path, StringComparer.Ordinal)
            .ToList();
        var removals = new List<OwnedRecord>();
        var existingBytes = records.Sum(static record => record.Length);
        if (incomingBytes > 0 && !incomingAlreadyPresent &&
            existingBytes > _options.MaximumBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_capacity_exceeded",
                "The sanitized diagnostic store requires successful cleanup before another append.");
        }

        var now = _timeProvider.GetUtcNow();
        if (records.Any(record => record.TimestampUtc > now + MaximumFutureClockSkew))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_clock_invalid",
                "Sanitized-log cleanup stopped because a record timestamp is unexpectedly in the future.");
        }

        var cutoff = now - _options.RetentionAge;

        foreach (var record in records.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.TimestampUtc >= cutoff)
            {
                continue;
            }

            records.Remove(record);
            removals.Add(record);
        }

        var currentBytes = records.Sum(static record => record.Length);
        foreach (var record in records.ToArray())
        {
            if (currentBytes + incomingBytes <= _options.MaximumBytes)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            records.Remove(record);
            currentBytes -= record.Length;
            removals.Add(record);
        }

        if (currentBytes + incomingBytes > _options.MaximumBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_capacity_exceeded",
                "The configured sanitized diagnostic store has insufficient capacity.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new RetentionPlan(removals.AsReadOnly());
    }

    private void ValidateIncomingTimestamp(DateTimeOffset timestampUtc)
    {
        var now = _timeProvider.GetUtcNow();
        if (timestampUtc > now + MaximumFutureClockSkew)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_clock_invalid",
                "The sanitized diagnostic record timestamp is unexpectedly in the future.");
        }

        if (timestampUtc < now - _options.RetentionAge)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_expired",
                "The sanitized diagnostic record is already outside the configured retention window.");
        }
    }

    private SanitizedLogCleanupResult ExecuteRetention(RetentionPlan plan)
    {
        var removedCount = 0;
        long removedBytes = 0;
        foreach (var record in plan.Records)
        {
            try
            {
                DeleteOwnedRecord(record);
                removedCount++;
                removedBytes += record.Length;
            }
            catch (Exception exception) when (exception is DiagnosticContractException or
                IOException or
                UnauthorizedAccessException)
            {
                return new SanitizedLogCleanupResult(
                    removedCount,
                    removedBytes,
                    completed: false);
            }
        }

        return new SanitizedLogCleanupResult(removedCount, removedBytes);
    }

    private static SanitizedLogCleanupResult CombineCleanup(
        SanitizedLogCleanupResult first,
        SanitizedLogCleanupResult second) =>
        new(
            first.RemovedRecordCount + second.RemovedRecordCount,
            first.RemovedBytes + second.RemovedBytes,
            first.RemovedTemporaryFileCount + second.RemovedTemporaryFileCount,
            first.RemovedTemporaryBytes + second.RemovedTemporaryBytes,
            first.Completed && second.Completed);

    private static SanitizedLogCleanupResult CombineCleanup(
        SanitizedLogCleanupResult retention,
        TemporaryCleanupResult temporaryCleanup) =>
        new(
            retention.RemovedRecordCount,
            retention.RemovedBytes,
            temporaryCleanup.RemovedFileCount,
            temporaryCleanup.RemovedBytes,
            retention.Completed && temporaryCleanup.Completed);

    private StoreEnumeration EnumerateStoreFiles(CancellationToken cancellationToken)
    {
        var records = new List<OwnedRecord>();
        var enumeratedCount = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
        };

        foreach (var path in Directory.EnumerateFileSystemEntries(_rootPath, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRootLockPath(path))
            {
                continue;
            }

            enumeratedCount++;
            if (enumeratedCount > MaximumOwnedFiles)
            {
                throw new DiagnosticContractException(
                    "diagnostic_store_file_count_exceeded",
                    "The diagnostic store contains too many files to inspect safely.");
            }

            if (!IsRegularNonReparseFile(path))
            {
                if (LooksLikeOwnedRecord(path))
                {
                    throw UnsafeStoreObject();
                }

                continue;
            }

            if (!TryParseOwnedRecord(path, out var record))
            {
                if (LooksLikeOwnedRecord(path))
                {
                    throw InvalidOwnedRecord();
                }

                continue;
            }

            records.Add(record);
        }

        return new StoreEnumeration(records.AsReadOnly(), enumeratedCount);
    }

    private bool TryParseOwnedRecord(string path, out OwnedRecord record)
    {
        record = default;
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith(EventPrefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(EventSuffix, StringComparison.Ordinal) ||
            fileName.Length != EventPrefix.Length + TimestampTextLength + 1 + 32 + EventSuffix.Length)
        {
            return false;
        }

        var timestampText = fileName.Substring(EventPrefix.Length, TimestampTextLength);
        var separatorIndex = EventPrefix.Length + TimestampTextLength;
        var randomText = fileName.Substring(separatorIndex + 1, 32);
        if (fileName[separatorIndex] != '-' ||
            randomText.Any(static character => !IsLowerHexDigit(character)) ||
            !DateTimeOffset.TryParseExact(
                timestampText,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return false;
        }

        var bytes = ReadValidatedRecordBytes(path, timestamp);
        record = new OwnedRecord(
            path,
            timestamp,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return true;
    }

    private async Task<StagedRecord> StageRecordAsync(
        StructuredDiagnosticEvent diagnosticEvent,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var timestamp = diagnosticEvent.OccurredAtUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var identifier = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var finalName = $"{EventPrefix}{timestamp}-{identifier}{EventSuffix}";
        var finalPath = Path.Combine(_rootPath, finalName);
        var temporaryPath = Path.Combine(_rootPath, $"{TemporaryPrefix}{timestamp}-{identifier}{EventSuffix}");
        var transactionPath = Path.Combine(_rootPath, $"{TransactionPrefix}{timestamp}-{identifier}{EventSuffix}");
        var temporaryCreated = false;

        try
        {
            _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.BeforeTemporaryCreate);
            await using (var stream = _security.CreateRestrictedFile(
                temporaryPath,
                FileShare.None,
                FileOptions.Asynchronous | FileOptions.WriteThrough,
                bufferSize: 4096))
            {
                temporaryCreated = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterTemporaryWriteBeforeFlush);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterTemporaryWriteBeforePublish);
            cancellationToken.ThrowIfCancellationRequested();
            return new StagedRecord(
                temporaryPath,
                transactionPath,
                finalPath,
                diagnosticEvent.OccurredAtUtc,
                bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        catch
        {
            if (temporaryCreated && !DeleteExactSafeTemporary(temporaryPath))
            {
                throw new DiagnosticContractException(
                    "diagnostic_store_cleanup_failed",
                    "A verified owned temporary diagnostic file could not be removed.");
            }

            throw;
        }
    }

    private void ValidateStagedRecord(StagedRecord staged)
    {
        ValidateStagedPath(staged, staged.TemporaryPath);
    }

    private void ValidateStagedPath(StagedRecord staged, string path)
    {
        using var stream = _security.OpenValidatedRecord(
            path,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length != staged.Length)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_publish_invalid",
                "The staged diagnostic record failed validation.");
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (!PersistedDiagnosticRecordValidator.IsCanonical(bytes, staged.TimestampUtc) ||
            !string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                staged.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_publish_invalid",
                "The staged diagnostic record failed validation.");
        }
    }

    private void CommitStagedRecord(StagedRecord staged)
    {
        File.Move(staged.TemporaryPath, staged.TransactionPath, overwrite: false);
        staged.Committed = true;
        ValidateStagedPath(staged, staged.TransactionPath);
    }

    private void PublishStagedRecord(StagedRecord staged)
    {
        File.Move(staged.TransactionPath, staged.FinalPath, overwrite: false);
        staged.Published = true;

        if (!TryParseOwnedRecord(staged.FinalPath, out var record) ||
            record.Length != staged.Length ||
            !string.Equals(
                record.ContentDigestSha256,
                staged.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_publish_invalid",
                "The published diagnostic record failed validation.");
        }
    }

    private void DeleteOwnedRecord(OwnedRecord record)
    {
        _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.BeforeOwnedRecordDelete);
        using (var stream = _security.OpenValidatedRecordForDeletion(record.Path))
        {
            ValidateOpenedOwnedRecord(stream, record);
            _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterOwnedRecordValidatedBeforeDelete);
            ValidateOpenedOwnedRecord(stream, record);
            _security.DeleteOpenedRecord(record.Path, stream);
        }

        if (File.Exists(record.Path))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A diagnostic-store record path changed during cleanup.");
        }
    }

    private static void ValidateOpenedOwnedRecord(FileStream stream, OwnedRecord record)
    {
        if (stream.Length != record.Length || stream.Length is <= 0 or > StructuredDiagnosticEvent.MaximumSerializedBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A diagnostic-store record changed before cleanup.");
        }

        stream.Position = 0;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (!PersistedDiagnosticRecordValidator.IsCanonical(bytes, record.TimestampUtc) ||
            !string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                record.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A diagnostic-store record changed before cleanup.");
        }
    }

    private bool DeleteExactSafeTemporary(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            if (TryParseOwnedTemporary(path, out var temporary))
            {
                DeleteOwnedTemporary(temporary);
                return true;
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async ValueTask<FileStream> AcquireRootLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(_rootPath, RootLockFileName);
        for (var attempt = 0; attempt < MaximumRootLockAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquired = _security.TryAcquireExclusiveLock(lockPath);
            if (acquired is not null)
            {
                return acquired;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }

        throw new DiagnosticContractException(
            "diagnostic_store_busy",
            "The sanitized diagnostic store did not become available within its bounded wait.");
    }

    private async ValueTask EnterOperationGateAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _pendingOperations) > MaximumPendingOperations)
        {
            Interlocked.Decrement(ref _pendingOperations);
            throw new DiagnosticContractException(
                "diagnostic_store_busy",
                "The sanitized diagnostic store has reached its bounded operation queue.");
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingOperations);
            throw;
        }
    }

    private void ExitOperationGate()
    {
        _gate.Release();
        Interlocked.Decrement(ref _pendingOperations);
    }

    private TemporaryCleanupPlan PlanTemporaryCleanup(CancellationToken cancellationToken)
    {
        var temporaries = new List<OwnedTemporary>();
        var enumeratedCount = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
        };

        foreach (var path in Directory.EnumerateFileSystemEntries(_rootPath, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRootLockPath(path))
            {
                continue;
            }

            enumeratedCount++;
            if (enumeratedCount > MaximumOwnedFiles)
            {
                throw new DiagnosticContractException(
                    "diagnostic_store_file_count_exceeded",
                    "The diagnostic store contains too many files to inspect safely.");
            }

            if (!IsRegularNonReparseFile(path))
            {
                if (LooksLikeOwnedTemporary(path))
                {
                    throw UnsafeStoreObject();
                }

                continue;
            }

            if (TryParseOwnedTemporary(path, out var temporary))
            {
                temporaries.Add(temporary);
            }
            else if (LooksLikeOwnedTemporary(path))
            {
                throw InvalidOwnedRecord();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new TemporaryCleanupPlan(temporaries.AsReadOnly());
    }

    private TemporaryCleanupResult ExecuteTemporaryCleanup(TemporaryCleanupPlan plan)
    {
        var removedCount = 0;
        long removedBytes = 0;
        foreach (var temporary in plan.Files)
        {
            try
            {
                _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.BeforeOwnedTemporaryDelete);
                DeleteOwnedTemporary(temporary);
                removedCount++;
                removedBytes += temporary.Length;
            }
            catch (Exception exception) when (exception is DiagnosticContractException or
                IOException or
                UnauthorizedAccessException)
            {
                return new TemporaryCleanupResult(removedCount, removedBytes, Completed: false);
            }
        }

        return new TemporaryCleanupResult(removedCount, removedBytes, Completed: true);
    }

    private bool TryParseOwnedTemporary(string path, out OwnedTemporary temporary)
    {
        temporary = default;
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith(TemporaryPrefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(EventSuffix, StringComparison.Ordinal) ||
            fileName.Length != TemporaryPrefix.Length + TimestampTextLength + 1 + 32 + EventSuffix.Length)
        {
            return false;
        }

        var timestampText = fileName.Substring(TemporaryPrefix.Length, TimestampTextLength);
        var separatorIndex = TemporaryPrefix.Length + TimestampTextLength;
        var randomText = fileName.Substring(separatorIndex + 1, 32);
        if (fileName[separatorIndex] != '-' ||
            randomText.Any(static character => !IsLowerHexDigit(character)) ||
            !DateTimeOffset.TryParseExact(
                timestampText,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _))
        {
            return false;
        }

        using var stream = _security.OpenValidatedRecord(path, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length is < 0 or > StructuredDiagnosticEvent.MaximumSerializedBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_invalid",
                "An owned temporary diagnostic record is outside its byte bound.");
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        temporary = new OwnedTemporary(
            path,
            stream.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return true;
    }

    private void DeleteOwnedTemporary(OwnedTemporary temporary)
    {
        using (var stream = _security.OpenValidatedRecordForDeletion(temporary.Path))
        {
            ValidateOpenedOwnedTemporary(stream, temporary);
            _faultInjector?.ThrowIfRequested(LogStoreFaultPoint.AfterOwnedTemporaryValidatedBeforeDelete);
            ValidateOpenedOwnedTemporary(stream, temporary);
            _security.DeleteOpenedRecord(temporary.Path, stream);
        }

        if (File.Exists(temporary.Path))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "An owned temporary diagnostic path changed during cleanup.");
        }
    }

    private static void ValidateOpenedOwnedTemporary(FileStream stream, OwnedTemporary temporary)
    {
        if (stream.Length != temporary.Length || stream.Length is < 0 or > StructuredDiagnosticEvent.MaximumSerializedBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "An owned temporary diagnostic record changed before cleanup.");
        }

        stream.Position = 0;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (!string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                temporary.ContentDigestSha256,
                StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "An owned temporary diagnostic record changed before cleanup.");
        }
    }

    private byte[] ReadAndValidateOwnedRecord(OwnedRecord record)
    {
        var bytes = ReadValidatedRecordBytes(record.Path, record.TimestampUtc);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (bytes.LongLength != record.Length ||
            !string.Equals(digest, record.ContentDigestSha256, StringComparison.Ordinal))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_changed",
                "A diagnostic-store record changed before it was read.");
        }

        return bytes;
    }

    private byte[] ReadValidatedRecordBytes(string path, DateTimeOffset expectedTimestamp)
    {
        using var stream = _security.OpenValidatedRecord(path, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length is <= 0 or > StructuredDiagnosticEvent.MaximumSerializedBytes)
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_invalid",
                "A diagnostic-store record is outside its byte bound.");
        }

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (!PersistedDiagnosticRecordValidator.IsCanonical(bytes, expectedTimestamp))
        {
            throw new DiagnosticContractException(
                "diagnostic_store_record_invalid",
                "A diagnostic-store record does not match the registered canonical schema.");
        }

        return bytes;
    }

    private static bool IsRootLockPath(string path) =>
        string.Equals(Path.GetFileName(path), RootLockFileName, StringComparison.Ordinal);

    private static bool IsRegularNonReparseFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw UnsafeStoreObject();
        }

        return (attributes & FileAttributes.Directory) == 0;
    }

    private static bool LooksLikeOwnedRecord(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith(EventPrefix, StringComparison.Ordinal) &&
            fileName.EndsWith(EventSuffix, StringComparison.Ordinal);
    }

    private static bool LooksLikeOwnedTemporary(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith(TemporaryPrefix, StringComparison.Ordinal) &&
            fileName.EndsWith(EventSuffix, StringComparison.Ordinal);
    }

    private static bool LooksLikeOwnedTransaction(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith(TransactionPrefix, StringComparison.Ordinal) &&
            fileName.EndsWith(EventSuffix, StringComparison.Ordinal);
    }

    private static bool IsLowerHexDigit(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static DiagnosticContractException InvalidOwnedRecord() =>
        new(
            "diagnostic_store_record_invalid",
            "A diagnostic-store object uses an invalid owned-record name or schema.");

    private static DiagnosticContractException RecoveryPending() =>
        new(
            "diagnostic_store_write_recovery_pending",
            "A diagnostic write reached a durable state that requires bounded recovery.",
            durableStateAmbiguous: true);

    private static DiagnosticContractException UnsafeStoreObject() =>
        new(
            "diagnostic_store_object_unsafe",
            "The sanitized diagnostic store contains an unsafe object or access policy.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct OwnedRecord(
        string Path,
        DateTimeOffset TimestampUtc,
        long Length,
        string ContentDigestSha256);

    private readonly record struct OwnedTemporary(string Path, long Length, string ContentDigestSha256);

    private readonly record struct TemporaryCleanupResult(
        int RemovedFileCount,
        long RemovedBytes,
        bool Completed);

    private sealed class TemporaryCleanupPlan(IReadOnlyList<OwnedTemporary> files)
    {
        public IReadOnlyList<OwnedTemporary> Files { get; } = files;
    }

    private sealed class StoreEnumeration(IReadOnlyList<OwnedRecord> records, int totalFileCount)
    {
        public IReadOnlyList<OwnedRecord> Records { get; } = records;

        public int TotalFileCount { get; } = totalFileCount;
    }

    private sealed record PendingTransaction(
        string Path,
        string FinalPath,
        DateTimeOffset TimestampUtc,
        long Length,
        string ContentDigestSha256);

    private sealed class StagedRecord(
        string temporaryPath,
        string transactionPath,
        string finalPath,
        DateTimeOffset timestampUtc,
        long length,
        string contentDigestSha256)
    {
        public string TemporaryPath { get; } = temporaryPath;

        public string TransactionPath { get; } = transactionPath;

        public string FinalPath { get; } = finalPath;

        public DateTimeOffset TimestampUtc { get; } = timestampUtc;

        public long Length { get; } = length;

        public string ContentDigestSha256 { get; } = contentDigestSha256;

        public bool Committed { get; set; }

        public bool Published { get; set; }
    }

    private sealed class RetentionPlan(IReadOnlyList<OwnedRecord> records)
    {
        public IReadOnlyList<OwnedRecord> Records { get; } = records;
    }
}

internal enum LogStoreFaultPoint
{
    BeforeTemporaryCreate,
    AfterTemporaryWriteBeforeFlush,
    AfterTemporaryWriteBeforePublish,
    AfterTransactionCommitBeforeCleanup,
    AfterRetentionBeforePublish,
    AfterPublishBeforeReturn,
    BeforeOwnedRecordDelete,
    AfterOwnedRecordValidatedBeforeDelete,
    BeforeOwnedTemporaryDelete,
    AfterOwnedTemporaryValidatedBeforeDelete,
}

internal interface ILogStoreFaultInjector
{
    void ThrowIfRequested(LogStoreFaultPoint point);
}
