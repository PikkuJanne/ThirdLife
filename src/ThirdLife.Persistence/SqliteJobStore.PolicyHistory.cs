using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThirdLife.Core.Jobs;
using ThirdLife.Policy.Schema;
using ThirdLife.Policy.Versioning;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IPolicyHistoryStore
{
    public const int MaximumPolicyDecisionsPerJob = 256;
    private const int MaximumPolicySnapshots = 1_024;

    private const string PolicyDecisionProjection = """
        SELECT d.decision_id, d.job_id, d.assessment_id, d.policy_id, d.policy_version, d.policy_sha256,
               d.evaluated_at_utc, d.previous_decision_id, d.payload_json, d.payload_sha256,
               length(CAST(d.payload_json AS BLOB)), s.policy_json, length(s.policy_json)
        FROM policy_decisions AS d
        LEFT JOIN policy_snapshots AS s ON s.policy_id = d.policy_id AND s.policy_version = d.policy_version
            AND s.policy_sha256 = d.policy_sha256
        """;

    public async Task AppendPolicyDecisionAsync(
        PolicySnapshot snapshot, PolicyDecisionRecord decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(decision);
        ThrowIfDisposed();
        if (decision.PolicyId != snapshot.Document.PolicyId || decision.PolicyVersion != snapshot.Document.Version ||
            decision.PolicySha256 != snapshot.Document.ContentSha256)
        {
            throw new ArgumentException("The decision does not belong to the supplied policy snapshot.", nameof(decision));
        }

        var payload = SerializePayload(decision, PolicyDecisionRecord.MaximumSerializedBytes);
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            var existing = await ReadPolicyDecisionByIdAsync(connection, transaction, null, decision.DecisionId, token)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                var existingPayload = SerializePayload(existing.Decision, PolicyDecisionRecord.MaximumSerializedBytes);
                if (existingPayload != payload || !existing.Snapshot.Utf8Json.AsSpan().SequenceEqual(snapshot.Utf8Json.AsSpan()))
                {
                    throw new JobStoreConflictException();
                }

                _layout.ValidateJobDirectory(decision.JobId);
                return;
            }

            if (decision.JobEvidenceCount is { } expectedCount &&
                expectedCount != await ReadEvidenceCountAsync(connection, transaction, decision.JobId, token).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }
            if (!decision.HasEvaluationProvenance)
            {
                throw new ArgumentException("A new policy decision requires an evaluation result.", nameof(decision));
            }

            if (await ReadArchiveStateAsync(connection, transaction, decision.JobId, token).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }

            _layout.ValidateJobDirectory(decision.JobId);
            var latest = await ReadLatestPolicyDecisionCoreAsync(connection, transaction, decision.JobId, decision.AssessmentId, token)
                .ConfigureAwait(false);
            if (decision.PreviousDecisionId != latest?.Decision.DecisionId ||
                (latest is not null && (decision.EvaluatedAtUtc < latest.Decision.EvaluatedAtUtc ||
                    !decision.EvidenceIds.AsSpan().SequenceEqual(latest.Decision.EvidenceIds.AsSpan()))))
            {
                throw new JobStoreConflictException();
            }

            await RequirePolicyDecisionEvidenceAsync(connection, transaction, decision, token).ConfigureAwait(false);
            await RequirePolicyDecisionCapacityAsync(connection, transaction, decision.JobId, token).ConfigureAwait(false);
            await RegisterPolicySnapshotAsync(connection, transaction, snapshot, token).ConfigureAwait(false);
            await InsertPolicyDecisionAsync(connection, transaction, decision, payload, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<StoredPolicyDecision?> LoadPolicyDecisionAsync(
        JobId jobId, string decisionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        RequirePolicyLookupId(decisionId, nameof(decisionId));
        return ReadPolicyHistoryAsync(jobId,
            (connection, transaction, token) => ReadPolicyDecisionByIdAsync(connection, transaction, jobId, decisionId, token),
            cancellationToken);
    }

    public Task<StoredPolicyDecision?> LoadLatestPolicyDecisionAsync(
        JobId jobId, string assessmentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        RequirePolicyLookupId(assessmentId, nameof(assessmentId));
        return ReadPolicyHistoryAsync(jobId,
            (connection, transaction, token) => ReadLatestPolicyDecisionCoreAsync(connection, transaction, jobId, assessmentId, token),
            cancellationToken);
    }

    private async Task<StoredPolicyDecision?> ReadPolicyHistoryAsync(
        JobId jobId,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<StoredPolicyDecision?>> read,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TranslateReadErrorsAsync(async () =>
            {
                _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                await using var transaction = _connection.BeginTransaction(deferred: true);
                var stored = await read(_connection, transaction, cancellationToken).ConfigureAwait(false);
                if (stored is not null)
                {
                    _layout.ValidateJobDirectory(jobId);
                    await RequirePolicyDecisionEvidenceAsync(_connection, transaction, stored.Decision, cancellationToken,
                        reading: true).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _layout.ValidateStoreGuards(_databaseGuard, _journalGuard);
                ValidateStoreFileBounds(_databaseGuard, _journalGuard);
                return stored;
            }).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static void RequirePolicyLookupId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Supply a bounded policy history identifier.", parameterName);
        }
    }

    private async Task<StoredPolicyDecision?> ReadPolicyDecisionByIdAsync(
        SqliteConnection connection, SqliteTransaction transaction, JobId? jobId, string decisionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = PolicyDecisionProjection + "\nWHERE d.decision_id = $decision_id AND ($job_id IS NULL OR d.job_id = $job_id);";
        command.Parameters.AddWithValue("$decision_id", decisionId);
        command.Parameters.AddWithValue("$job_id", jobId is null ? DBNull.Value : jobId.Value);
        return await ReadPolicyDecisionRowAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoredPolicyDecision?> ReadLatestPolicyDecisionCoreAsync(
        SqliteConnection connection, SqliteTransaction transaction, JobId jobId, string assessmentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = PolicyDecisionProjection + """

            WHERE d.job_id = $job_id AND d.assessment_id = $assessment_id
            ORDER BY d.decision_sequence DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        command.Parameters.AddWithValue("$assessment_id", assessmentId);
        return await ReadPolicyDecisionRowAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoredPolicyDecision?> ReadPolicyDecisionRowAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            if (reader.GetInt64(10) is < 2 or > PolicyDecisionRecord.MaximumSerializedBytes)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var decision = DeserializePayload<PolicyDecisionRecord>(reader.GetString(8), reader.GetString(9),
                PolicyDecisionRecord.MaximumSerializedBytes);
            var policyId = reader.GetString(3);
            var policyVersion = reader.GetString(4);
            var policySha256 = reader.GetString(5);
            var snapshot = ReadPolicySnapshot(reader, 11, policyId, policyVersion, policySha256);
            var previousDecisionId = reader.IsDBNull(7) ? null : reader.GetString(7);
            if (decision.DecisionId != reader.GetString(0) || decision.JobId.Value != reader.GetString(1) ||
                decision.AssessmentId != reader.GetString(2) || decision.PolicyId != policyId ||
                decision.PolicyVersion != policyVersion || decision.PolicySha256 != policySha256 ||
                decision.EvaluatedAtUtc != ParseTimestamp(reader.GetString(6)) || decision.PreviousDecisionId != previousDecisionId)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            return new StoredPolicyDecision(snapshot, decision);
        }
        catch (Exception exception) when (IsPersistedValueFailure(exception))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static PolicySnapshot ReadPolicySnapshot(
        SqliteDataReader reader, int blobColumn, string policyId, string version, string sha256)
    {
        try
        {
            if (reader.IsDBNull(blobColumn) || reader.GetInt64(blobColumn + 1) is < 1 or > PolicySchema.MaximumDocumentBytes)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var snapshot = PolicySnapshot.Restore(reader.GetFieldValue<byte[]>(blobColumn), sha256);
            if (snapshot.Document.PolicyId != policyId || snapshot.Document.Version != version)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException || IsPersistedValueFailure(exception))
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }
    }

    private static async Task RequirePolicyDecisionEvidenceAsync(
        SqliteConnection connection, SqliteTransaction transaction, PolicyDecisionRecord decision,
        CancellationToken cancellationToken, bool reading = false)
    {
        if (decision.EvidenceIds.IsEmpty)
        {
            return;
        }

        var remaining = decision.EvidenceIds.Select(id => id.Value).ToHashSet(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT evidence_id FROM evidence_records WHERE job_id = $job_id
            ORDER BY evidence_sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job_id", decision.JobId.Value);
        command.Parameters.AddWithValue("$limit", MaximumRecordsPerJob);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (remaining.Remove(reader.GetString(0)) && remaining.Count == 0)
            {
                return;
            }
        }

        if (reading)
        {
            throw new JobStoreCorruptionException("store_payload_mismatch");
        }

        throw new JobStoreConflictException();
    }

    private static async Task RequirePolicyDecisionCapacityAsync(
        SqliteConnection connection, SqliteTransaction transaction, JobId jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM policy_decisions WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (count >= MaximumPolicyDecisionsPerJob)
        {
            throw new JobStoreConflictException();
        }
    }

    private static async Task RegisterPolicySnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, PolicySnapshot snapshot, CancellationToken cancellationToken)
    {
        var policy = snapshot.Document;
        await using (var latest = connection.CreateCommand())
        {
            latest.Transaction = transaction;
            latest.CommandText = """
                SELECT policy_version, policy_sha256, policy_json, length(policy_json)
                FROM policy_snapshots WHERE policy_id = $policy_id ORDER BY snapshot_sequence DESC LIMIT 1;
                """;
            latest.Parameters.AddWithValue("$policy_id", policy.PolicyId);
            await using var reader = await latest.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var active = ReadPolicySnapshot(reader, 2, policy.PolicyId, reader.GetString(0), reader.GetString(1));
                var order = PolicyVersionOrder.Compare(policy.Version, active.Document.Version);
                if (order < 0 || (order == 0 && !snapshot.Utf8Json.AsSpan().SequenceEqual(active.Utf8Json.AsSpan())))
                {
                    throw new JobStoreConflictException();
                }

                if (order == 0)
                {
                    return;
                }
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM policy_snapshots;";
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (count >= MaximumPolicySnapshots)
        {
            throw new JobStoreConflictException();
        }

        command.CommandText = """
            INSERT INTO policy_snapshots (policy_id, policy_version, policy_sha256, policy_json)
            VALUES ($policy_id, $policy_version, $policy_sha256, $policy_json);
            """;
        command.Parameters.AddWithValue("$policy_id", policy.PolicyId);
        command.Parameters.AddWithValue("$policy_version", policy.Version);
        command.Parameters.AddWithValue("$policy_sha256", policy.ContentSha256);
        command.Parameters.Add("$policy_json", SqliteType.Blob).Value = snapshot.Utf8Json.ToArray();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertPolicyDecisionAsync(
        SqliteConnection connection, SqliteTransaction transaction, PolicyDecisionRecord decision, Payload payload,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO policy_decisions (decision_id, job_id, assessment_id, policy_id, policy_version, policy_sha256,
                evaluated_at_utc, previous_decision_id, payload_json, payload_sha256)
            VALUES ($decision_id, $job_id, $assessment_id, $policy_id, $policy_version, $policy_sha256,
                $evaluated_at_utc, $previous_decision_id, $payload_json, $payload_sha256);
            """;
        command.Parameters.AddWithValue("$decision_id", decision.DecisionId);
        command.Parameters.AddWithValue("$job_id", decision.JobId.Value);
        command.Parameters.AddWithValue("$assessment_id", decision.AssessmentId);
        command.Parameters.AddWithValue("$policy_id", decision.PolicyId);
        command.Parameters.AddWithValue("$policy_version", decision.PolicyVersion);
        command.Parameters.AddWithValue("$policy_sha256", decision.PolicySha256);
        command.Parameters.AddWithValue("$evaluated_at_utc", FormatTimestamp(decision.EvaluatedAtUtc));
        command.Parameters.AddWithValue("$previous_decision_id", decision.PreviousDecisionId is { } previous ? previous : DBNull.Value);
        command.Parameters.AddWithValue("$payload_json", payload.Json);
        command.Parameters.AddWithValue("$payload_sha256", payload.Sha256);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
