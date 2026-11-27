using Microsoft.Data.Sqlite;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IRecipientHandoverStore
{
    private const int RecipientHandoverPayloadLimit = 1024;

    public Task<SavedRecipientHandover?> LoadRecipientHandoverAsync(JobId jobId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync((transaction, ct) => _supportsRecipientHandover
            ? ReadRecipientHandoverAsync(transaction, jobId, ct)
            : Task.FromResult<SavedRecipientHandover?>(null), token);
    }

    public Task SaveHandoverModeAsync(JobId jobId, SavedRecipientHandover expected, HandoverMode mode,
        OperatorId operatorId, CancellationToken token = default) =>
        AppendRecipientHandoverAsync(jobId, expected, operatorId, current => mode == current.Handover?.Mode
            ? null
            : new(current.Sequence + 1, mode, null, null, false, operatorId, _timeProvider.GetUtcNow()), token);

    public Task SaveRecipientChoiceAsync(JobId jobId, SavedRecipientHandover expected, RecipientSetupArea area,
        RecipientSetupChoice choice, bool recipientPresent, OperatorId operatorId, CancellationToken token = default)
    {
        if (!recipientPresent)
        {
            throw new ArgumentException("Record a recipient choice only with the recipient present.", nameof(recipientPresent));
        }

        return AppendRecipientHandoverAsync(jobId, expected, operatorId, current =>
        {
            if (current.Handover?.Mode != HandoverMode.Assisted)
            {
                throw new JobStoreConflictException();
            }
            return new(current.Sequence + 1, HandoverMode.Assisted, area, choice, recipientPresent, operatorId,
                _timeProvider.GetUtcNow());
        }, token);
    }

    private sealed record StoredRecipientHandoverEvent(JobId JobId, RecipientHandoverEvent Event);

    private Task AppendRecipientHandoverAsync(JobId jobId, SavedRecipientHandover expected, OperatorId operatorId,
        Func<SavedRecipientHandover, RecipientHandoverEvent?> create, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(operatorId);
        ThrowIfDisposed();
        return ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            if (!_supportsRecipientHandover || expected.JobId != jobId || !expected.CanRecord ||
                await ReadRecipientHandoverAsync(transaction, jobId, ct).ConfigureAwait(false) is not { CanRecord: true } current ||
                current.Sequence != expected.Sequence)
            {
                throw new JobStoreConflictException();
            }

            if (create(current) is not { } entry)
            {
                return;
            }

            var payload = SerializePayload(new StoredRecipientHandoverEvent(jobId, entry), RecipientHandoverPayloadLimit);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO recipient_handover_events (job_id, sequence, is_mode_selection, payload_json, payload_sha256)
                VALUES ($job, $sequence, $selection, $payload, $sha);
                """;
            command.Parameters.AddWithValue("$job", jobId.Value);
            command.Parameters.AddWithValue("$sequence", entry.Sequence);
            command.Parameters.AddWithValue("$selection", entry.Area.HasValue ? 0 : 1);
            command.Parameters.AddWithValue("$payload", payload.Json);
            command.Parameters.AddWithValue("$sha", payload.Sha256);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, token);
    }

    private async Task<SavedRecipientHandover?> ReadRecipientHandoverAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT j.is_archived, e.sequence, e.is_mode_selection, length(CAST(e.payload_json AS BLOB)),
                e.payload_json, e.payload_sha256
            FROM jobs AS j
            LEFT JOIN recipient_handover_events AS e ON e.job_id = j.job_id
            WHERE j.job_id = $job ORDER BY e.sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        command.Parameters.AddWithValue("$limit", SavedRecipientHandover.MaximumHistoryPerJob + 1);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        _layout.ValidateJobDirectory(jobId);
        var archived = reader.GetInt64(0);
        if (archived is not 0 and not 1)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        var history = new List<RecipientHandoverEvent>();
        if (!reader.IsDBNull(1))
        {
            do
            {
                var sequence = reader.GetInt64(1);
                if (history.Count >= SavedRecipientHandover.MaximumHistoryPerJob || sequence != history.Count + 1 ||
                    reader.GetInt64(3) is < 2 or > RecipientHandoverPayloadLimit)
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }

                var stored = DeserializePayload<StoredRecipientHandoverEvent>(reader.GetString(4), reader.GetString(5),
                    RecipientHandoverPayloadLimit);
                if (stored.JobId != jobId || stored.Event?.Sequence != sequence ||
                    reader.GetInt64(2) != (stored.Event.Area.HasValue ? 0 : 1))
                {
                    throw new JobStoreCorruptionException("store_payload_mismatch");
                }
                history.Add(stored.Event);
            }
            while (await reader.ReadAsync(token).ConfigureAwait(false));
        }

        try
        {
            return SavedRecipientHandover.Evaluate(jobId, archived == 1, history);
        }
        catch (ArgumentException)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private async Task<long> ReadRecipientHandoverModeSequenceAsync(SqliteTransaction transaction, JobId jobId,
        CancellationToken token)
    {
        if (!_supportsRecipientHandover)
        {
            return 0;
        }

        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT coalesce(max(sequence), 0) FROM recipient_handover_events
            WHERE job_id = $job AND is_mode_selection = 1;
            """;
        command.Parameters.AddWithValue("$job", jobId.Value);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long sequence &&
            sequence is >= 0 and <= SavedRecipientHandover.MaximumHistoryPerJob
            ? sequence
            : throw new JobStoreCorruptionException("store_payload_mismatch");
    }
}
