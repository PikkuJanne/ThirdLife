using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IJobDataLifecycleStore
{
    private const int MaximumLocalFileReservations = 256;
    private const int MaximumDeletionFiles = MaximumLocalFileReservations + MaximumHandoverPublications + MaximumFileHistorySampleEvents;
    private const string DeletionSavepoint = "job_data_deletion";

    public Task<ArchivedJobDeletionReview?> ReviewArchivedJobDeletionAsync(JobId jobId, CheckpointId checkpointId,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(checkpointId);
        return ReadJournalTransactionAsync((transaction, ct) => ReadDeletionReviewAsync(transaction, jobId, checkpointId, ct), token);
    }

    public async Task<ArchivedJobDeletionReview> ApproveArchivedJobDeletionAsync(ArchivedJobDeletionReview review,
        OperatorId actor, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(actor);
        ThrowIfDisposed();
        if (review.ApprovedBy is not null || review.ApprovedAtUtc is not null || review.Files.Count > MaximumDeletionFiles)
        {
            throw new JobStoreConflictException();
        }

        var approvedFiles = review.Files.ToArray();
        ArchivedJobDeletionReview? approved = null;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            var current = await ReadDeletionReviewAsync(transaction, review.JobId, review.ArchiveCheckpointId, ct)
                .ConfigureAwait(false);
            if (current is null || current.ApprovedBy is not null || !current.Files.SequenceEqual(approvedFiles))
            {
                throw new JobStoreConflictException();
            }

            using var directory = _layout.OpenEmptyJobDirectoryDeleteGuard(review.JobId);
            var approvedAt = _timeProvider.GetUtcNow();
            await using var command = DeletionCommand(transaction, review.JobId, """
                INSERT INTO job_deletion_approvals (job_id, checkpoint_id, manifest_sha256, operator_id, approved_at_utc)
                VALUES ($job, $checkpoint, $manifest, $operator, $approved_at);
                """);
            command.Parameters.AddWithValue("$checkpoint", current.ArchiveCheckpointId.Value);
            command.Parameters.AddWithValue("$manifest", DeletionManifestDigest(current.Files));
            command.Parameters.AddWithValue("$operator", actor.Value);
            command.Parameters.AddWithValue("$approved_at", FormatTimestamp(approvedAt));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            approved = current with { ApprovedBy = actor, ApprovedAtUtc = approvedAt };
        }, token).ConfigureAwait(false);
        return approved ?? throw new JobStoreConflictException();
    }

    public async Task<ArchivedJobDeletionResult> ExecuteArchivedJobDeletionAsync(JobId jobId, CheckpointId checkpointId,
        Func<JobLocalFile, CancellationToken, JobLocalFileRemoval> removeFile, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(checkpointId);
        ArgumentNullException.ThrowIfNull(removeFile);
        ThrowIfDisposed();
        SafeFileHandle? directory = null;
        var retained = new List<JobLocalFile>();
        try
        {
            await ExecuteWriteAsync(async (_, transaction, ct) =>
            {
                var current = await ReadDeletionReviewAsync(transaction, jobId, checkpointId, ct).ConfigureAwait(false);
                if (current?.ApprovedBy is null)
                {
                    throw new JobStoreConflictException();
                }

                directory = _layout.OpenEmptyJobDirectoryDeleteGuard(jobId);
                await transaction.SaveAsync(DeletionSavepoint, ct).ConfigureAwait(false);
                await DeleteArchivedJobRowsAsync(transaction, jobId, ct).ConfigureAwait(false);
                foreach (var file in current.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (removeFile(file, ct) is not (JobLocalFileRemoval.Removed or JobLocalFileRemoval.Missing))
                    {
                        retained.Add(file);
                    }
                }

                if (retained.Count == 0)
                {
                    _layout.ValidateEmptyJobDirectory(jobId, directory);
                }
                else
                {
                    // Files and SQLite have separate durability. Preserve the exact
                    // approval and all job records if any attributed file was refused.
                    await transaction.RollbackAsync(DeletionSavepoint, ct).ConfigureAwait(false);
                }

                await transaction.ReleaseAsync(DeletionSavepoint, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            var completed = retained.Count == 0;
            var directoryRetained = completed && !_layout.TryDeleteEmptyJobDirectory(jobId,
                directory ?? throw new JobStoreConflictException());
            return new(completed, retained.AsReadOnly(), directoryRetained);
        }
        finally
        {
            directory?.Dispose();
        }
    }

    public async Task PublishJobLocalFileAsync(JobId jobId, JobLocalFile file, Action<CancellationToken> publish,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(publish);
        ThrowIfDisposed();
        if (!_supportsJobDataLifecycle || file.Kind is not (JobLocalFileKind.WorkshopReport or JobLocalFileKind.RecipientGuide))
        {
            throw new JobStoreConflictException();
        }

        // Commit ownership before publication. A failed or interrupted publication is a
        // bounded reservation whose exact destination can later be checked as missing.
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            await RequirePublicationJobAsync(transaction, jobId, ct).ConfigureAwait(false);
            await using var command = DeletionCommand(transaction, jobId, """
                INSERT INTO job_local_files (job_id, kind, file_name, byte_length, sha256)
                SELECT $job, $kind, $name, $length, $sha256
                WHERE (SELECT COUNT(*) FROM job_local_files WHERE job_id = $job) < $limit
                ON CONFLICT DO NOTHING;
                """);
            command.Parameters.AddWithValue("$kind", (int)file.Kind);
            command.Parameters.AddWithValue("$name", file.FileName);
            command.Parameters.AddWithValue("$length", file.ByteLength);
            command.Parameters.AddWithValue("$sha256", file.Sha256);
            command.Parameters.AddWithValue("$limit", MaximumLocalFileReservations);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                throw new JobStoreConflictException();
            }
        }, token).ConfigureAwait(false);

        // Hold the SQLite writer reservation during the bounded file operation so a
        // concurrent deletion cannot retire the job between revalidation and publication.
        ExceptionDispatchInfo? publicationFailure = null;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            await RequirePublicationJobAsync(transaction, jobId, ct).ConfigureAwait(false);
            await using var command = DeletionCommand(transaction, jobId, """
                SELECT EXISTS (SELECT 1 FROM job_local_files WHERE job_id = $job AND kind = $kind AND file_name = $name
                    AND byte_length = $length AND sha256 = $sha256);
                """);
            command.Parameters.AddWithValue("$kind", (int)file.Kind);
            command.Parameters.AddWithValue("$name", file.FileName);
            command.Parameters.AddWithValue("$length", file.ByteLength);
            command.Parameters.AddWithValue("$sha256", file.Sha256);
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not 1L)
            {
                throw new JobStoreConflictException();
            }

            ct.ThrowIfCancellationRequested();
            try
            {
                publish(ct);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                publicationFailure = ExceptionDispatchInfo.Capture(exception);
            }
        }, token).ConfigureAwait(false);
        publicationFailure?.Throw();
    }

    public async Task<T> PublishFileHistoryOriginalAsync<T>(JobId jobId, FileHistorySampleAudit intent,
        Func<CancellationToken, T> publish, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(publish);
        ThrowIfDisposed();
        if (intent.State != FileHistorySampleAuditState.Preparing)
        {
            throw new JobStoreConflictException();
        }

        T result = default!;
        ExceptionDispatchInfo? publicationFailure = null;
        await ExecuteWriteAsync(async (_, transaction, ct) =>
        {
            await RequireCurrentFileHistoryHandoverAsync(transaction, jobId, intent.HandoverSequence, ct).ConfigureAwait(false);
            var history = await ReadFileHistorySampleHistoryAsync(transaction, jobId, ct).ConfigureAwait(false);
            if (history.Count == 0 || history[^1].Audit != intent)
            {
                throw new JobStoreConflictException();
            }

            ct.ThrowIfCancellationRequested();
            try
            {
                result = publish(ct);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                publicationFailure = ExceptionDispatchInfo.Capture(exception);
            }
        }, token).ConfigureAwait(false);
        publicationFailure?.Throw();
        return result;
    }

    private async Task RequirePublicationJobAsync(SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        await EnsureJobExistsAsync(_connection, transaction, jobId, token).ConfigureAwait(false);
        if (await ReadJobDeletionApprovalAsync(transaction, jobId, token).ConfigureAwait(false) is not null)
        {
            throw new JobStoreConflictException();
        }

        _layout.ValidateJobDirectory(jobId);
    }

    private sealed record JobDeletionApproval(CheckpointId CheckpointId, string ManifestSha256, OperatorId Actor,
        DateTimeOffset ApprovedAt);

    private async Task<JobDeletionApproval?> ReadJobDeletionApprovalAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        await using var command = DeletionCommand(transaction, jobId, """
            SELECT checkpoint_id, manifest_sha256, operator_id, approved_at_utc FROM job_deletion_approvals WHERE job_id = $job;
            """);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? new(new CheckpointId(reader.GetString(0)), reader.GetString(1), new OperatorId(reader.GetString(2)),
                ParseTimestamp(reader.GetString(3)))
            : null;
    }

    private async Task<ArchivedJobDeletionReview?> ReadDeletionReviewAsync(SqliteTransaction transaction, JobId jobId,
        CheckpointId checkpointId, CancellationToken token)
    {
        if (!_supportsJobDataLifecycle ||
            !await IsExactArchivedCheckpointAsync(transaction, jobId, checkpointId, token).ConfigureAwait(false))
        {
            return null;
        }

        _layout.ValidateJobDirectory(jobId);
        if (await HasOutstandingDeletionBlockersAsync(transaction, jobId, token).ConfigureAwait(false))
        {
            return null;
        }

        var files = await ReadJobDeletionFilesAsync(transaction, jobId, token).ConfigureAwait(false);
        var approval = await ReadJobDeletionApprovalAsync(transaction, jobId, token).ConfigureAwait(false);
        if (approval is not null && (approval.CheckpointId != checkpointId ||
            approval.ManifestSha256 != DeletionManifestDigest(files)))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        return new(jobId, checkpointId, files, approval?.Actor, approval?.ApprovedAt);
    }

    private async Task<bool> IsExactArchivedCheckpointAsync(SqliteTransaction transaction, JobId jobId,
        CheckpointId checkpointId, CancellationToken token)
    {
        await using var command = DeletionCommand(transaction, jobId, """
            SELECT checkpoints.checkpoint_id, checkpoints.checkpoint_kind, checkpoints.recorded_at_utc, jobs.archived_at_utc
            FROM jobs JOIN store_checkpoints AS checkpoints ON checkpoints.job_id = jobs.job_id
            WHERE jobs.job_id = $job AND jobs.is_archived = 1 AND checkpoints.checkpoint_kind = 'archived'
            ORDER BY checkpoints.checkpoint_sequence DESC LIMIT 1;
            """);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) &&
            reader.GetString(0).Equals(checkpointId.Value, StringComparison.Ordinal) &&
            reader.GetString(1).Equals("archived", StringComparison.Ordinal) && !reader.IsDBNull(3) &&
            ParseTimestamp(reader.GetString(2)) == ParseTimestamp(reader.GetString(3));
    }

    private async Task<bool> HasOutstandingDeletionBlockersAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        var plans = await ReadJobJournalHistoriesAsync(transaction, jobId, token).ConfigureAwait(false);
        if (plans.Any(plan => plan.Plan.Actions.Any(action => plan.StateOf(action.ActionId) is
                JournalState.Started or JournalState.Applied or JournalState.RequiresReview)))
        {
            return true;
        }

        var updates = await ReadWindowsUpdatesAsync(transaction, jobId, token).ConfigureAwait(false);
        if (WindowsUpdateHistory.RecoveryBeforePackages(updates) != UpdateRecovery.None)
        {
            return true;
        }

        var settings = await ReadToggleKeysJournalAsync(transaction, jobId, token).ConfigureAwait(false);
        if (settings.Count > 0 && settings[^1].Audit.Outcome == ToggleKeysAuditOutcome.ApprovedPending)
        {
            return true;
        }

        await using var command = DeletionCommand(transaction, jobId, """
            SELECT EXISTS (SELECT 1 FROM reboot_checkpoints AS checkpoint
                WHERE checkpoint.job_id = $job AND NOT EXISTS (SELECT 1 FROM reboot_checkpoint_dispositions AS disposition
                    WHERE disposition.job_id = $job AND disposition.checkpoint_id = checkpoint.checkpoint_id));
            """);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not 0L;
    }

    private async Task<IReadOnlyList<JobLocalFile>> ReadJobDeletionFilesAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        var files = new Dictionary<(JobLocalFileKind, string), JobLocalFile>();
        await using (var command = DeletionCommand(transaction, jobId, """
            SELECT kind, file_name, byte_length, sha256 FROM job_local_files WHERE job_id = $job LIMIT $limit;
            """))
        {
            command.Parameters.AddWithValue("$limit", MaximumLocalFileReservations + 1);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (files.Count == MaximumLocalFileReservations)
                {
                    throw new JobStoreCorruptionException("store_record_limit_exceeded");
                }

                AddDeletionFile(files, new((JobLocalFileKind)reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
            }
        }

        var publications = await ReadPublicationsAsync(transaction, jobId, token).ConfigureAwait(false);
        foreach (var receipt in publications.Select(item => item.File).OfType<HandoverFileReceipt>())
        {
            AddDeletionFile(files, new(receipt.Output.Kind == HandoverOutputKind.WorkshopRecord
                ? JobLocalFileKind.WorkshopReport : JobLocalFileKind.RecipientGuide,
                receipt.FileName, receipt.ByteLength, receipt.Output.Sha256));
        }

        var samples = await ReadFileHistorySampleHistoryAsync(transaction, jobId, token).ConfigureAwait(false);
        foreach (var sample in samples.Select(item => item.Audit).Where(audit => audit.State == FileHistorySampleAuditState.Preparing))
        {
            AddDeletionFile(files, new(JobLocalFileKind.FileHistoryOriginal, sample.FileName, sample.ByteLength, sample.Sha256));
        }

        var sorted = Array.AsReadOnly(files.Values.OrderBy(file => file.Kind).ThenBy(file => file.FileName, StringComparer.Ordinal).ToArray());
        await RequireExclusiveDeletionFilesAsync(transaction, jobId, sorted, token).ConfigureAwait(false);
        return sorted;
    }

    private static void AddDeletionFile(Dictionary<(JobLocalFileKind, string), JobLocalFile> files, JobLocalFile file)
    {
        var key = (file.Kind, file.FileName);
        if (!files.TryAdd(key, file) && files[key] != file)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private async Task RequireExclusiveDeletionFilesAsync(SqliteTransaction transaction, JobId jobId,
        ReadOnlyCollection<JobLocalFile> files, CancellationToken token)
    {
        if (files.Count == 0)
        {
            return;
        }

        // The old receipt table predates the unique reservation key. Refuse even a
        // valid copy if another job's recorded ownership refers to the same exact name.
        await using var command = DeletionCommand(transaction, jobId, """
            SELECT EXISTS (SELECT 1 FROM handover_publications WHERE job_id <> $job
                AND json_extract(payload_json, '$.File.FileName') IN (SELECT value FROM json_each($files)))
                OR EXISTS (SELECT 1 FROM job_local_files WHERE job_id <> $job
                    AND file_name IN (SELECT value FROM json_each($files)))
                OR EXISTS (SELECT 1 FROM file_history_sample_audit WHERE job_id <> $job
                    AND ('thirdlife-restore-' || lower(replace(json_extract(payload_json, '$.Audit.SampleId'), '-', '')) || '.txt')
                        IN (SELECT value FROM json_each($files)));
            """);
        command.Parameters.AddWithValue("$files", JsonSerializer.Serialize(files.Select(file => file.FileName), _jsonOptions));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not 0L)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private string DeletionManifestDigest(IReadOnlyList<JobLocalFile> files) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files, _jsonOptions)));

    private SqliteCommand DeletionCommand(SqliteTransaction transaction, JobId jobId, string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$job", jobId.Value);
        return command;
    }

    private async Task DeleteArchivedJobRowsAsync(SqliteTransaction transaction, JobId jobId, CancellationToken token)
    {
        await using var command = DeletionCommand(transaction, jobId, """
            PRAGMA defer_foreign_keys = ON;
            DELETE FROM policy_snapshots AS snapshot WHERE EXISTS (SELECT 1 FROM policy_decisions AS owned
                WHERE owned.job_id = $job AND owned.policy_id = snapshot.policy_id AND owned.policy_version = snapshot.policy_version
                    AND owned.policy_sha256 = snapshot.policy_sha256)
                AND NOT EXISTS (SELECT 1 FROM policy_decisions AS other
                    WHERE other.job_id <> $job AND other.policy_id = snapshot.policy_id AND other.policy_version = snapshot.policy_version
                        AND other.policy_sha256 = snapshot.policy_sha256);
            DELETE FROM job_deletion_approvals WHERE job_id = $job;
            DELETE FROM job_local_files WHERE job_id = $job;
            DELETE FROM file_history_sample_audit WHERE job_id = $job;
            DELETE FROM recipient_toggle_keys_audit WHERE job_id = $job;
            DELETE FROM handover_signoffs WHERE job_id = $job;
            DELETE FROM handover_publications WHERE job_id = $job;
            DELETE FROM support_export_audit WHERE job_id = $job;
            DELETE FROM finalization_artifact_scans WHERE job_id = $job;
            DELETE FROM recipient_handover_events WHERE job_id = $job;
            DELETE FROM finalization_confirmations WHERE job_id = $job;
            DELETE FROM cold_boot_checkpoints WHERE job_id = $job;
            DELETE FROM profile_verifications WHERE job_id = $job;
            DELETE FROM maintenance_checkpoints WHERE job_id = $job;
            DELETE FROM windows_update_journal WHERE job_id = $job;
            DELETE FROM reboot_checkpoint_dispositions WHERE job_id = $job;
            DELETE FROM reboot_checkpoints WHERE job_id = $job;
            DELETE FROM action_journal_entries WHERE job_id = $job;
            DELETE FROM action_journal_approvals WHERE job_id = $job;
            DELETE FROM action_journal_plans WHERE job_id = $job;
            DELETE FROM policy_decisions WHERE job_id = $job;
            DELETE FROM sanitization_gate_decisions WHERE job_id = $job;
            DELETE FROM store_checkpoints WHERE job_id = $job;
            DELETE FROM evidence_records WHERE job_id = $job;
            """);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        await using var deleteJob = DeletionCommand(transaction, jobId, "DELETE FROM jobs WHERE job_id = $job AND is_archived = 1;");
        if (await deleteJob.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
        {
            throw new JobStoreConflictException();
        }

        await using var check = _connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "PRAGMA foreign_key_check;";
        await using var foreignKeys = await check.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await foreignKeys.ReadAsync(token).ConfigureAwait(false))
        {
            throw new JobStoreCorruptionException("store_foreign_key_mismatch");
        }
    }
}
