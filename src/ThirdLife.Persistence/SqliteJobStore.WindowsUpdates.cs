using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Planning;
using ThirdLife.Actions.WindowsUpdates;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Core.WindowsUpdates;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IWindowsUpdateJournal
{
    private const int WindowsUpdateRecordBytes = 32_768;

    public Task<ImmutableArray<WindowsUpdateEntry>> LoadWindowsUpdatesAsync(JobId jobId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return ReadJournalTransactionAsync((transaction, ct) => ReadWindowsUpdatesAsync(transaction, jobId, ct), token);
    }

    public async Task<string?> FindWindowsUpdatePolicyAsync(JobId jobId, SanitizationGateDecisionId gateId,
        Guid scanId, WindowsUpdateCandidate exact, string? servicingPolicySha256, CancellationToken token = default)
    {
        try
        {
            return await ReadJournalTransactionAsync(async (transaction, ct) =>
            {
                _layout.ValidateJobDirectory(jobId);
                await RequireExpectedSanitizationGateAsync(_connection, transaction, jobId, gateId, ct).ConfigureAwait(false);
                var history = await ReadWindowsUpdatesAsync(transaction, jobId, ct).ConfigureAwait(false);
                if (history.LastOrDefault() is not { Stage: WindowsUpdateStage.ScanCompleted, Scan: { } scan } latest ||
                    latest.GateId != gateId || latest.Id != scanId || !WindowsUpdatePolicy.Matches(exact, scan) ||
                    WindowsUpdateHistory.Disposition(history) != WindowsUpdateDisposition.ApprovalRequired ||
                    history.Length > WindowsUpdateHistory.MaximumEntries - 12) { return null; }

                return await RequireWindowsUpdatePolicyAsync(_connection, transaction, jobId, exact, servicingPolicySha256,
                    null, _timeProvider.GetUtcNow(), ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        catch (JobStoreConflictException) { return null; }
    }

    public async Task<WindowsUpdateEntry?> TryStartWindowsUpdateDispatchAsync(WindowsUpdateEntry intent, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Stage is not (WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted))
        {
            return null;
        }
        try { return await AppendWindowsUpdateAsync(intent, token).ConfigureAwait(false); }
        catch (JobStoreConflictException) { return null; }
    }

    public async Task<WindowsUpdateEntry> AppendWindowsUpdateAsync(WindowsUpdateEntry entry, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ThrowIfDisposed();
        WindowsUpdateEntry? recorded = null;
        await ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            _layout.ValidateJobDirectory(entry.JobId);
            var history = await ReadWindowsUpdatesAsync(transaction, entry.JobId, ct).ConfigureAwait(false);
            if (entry.BootUptime is not null || entry.BootSession is not null || entry.OccurredAtUtc > _timeProvider.GetUtcNow())
            {
                throw new JobStoreConflictException();
            }

            // A fresh scan may already have closed the unsent preparation while its owner was
            // waiting. Return that durable outcome without appending over a newer scan or approval.
            if (entry is
                {
                    Stage: WindowsUpdateStage.DispatchNotSent, Scan: null, Verification: null,
                    Operation: { ProgressPercent: null, Status: not WindowsUpdateStatus.Succeeded }
                } &&
                history.LastOrDefault(row => row.Stage == WindowsUpdateStage.DispatchNotSent &&
                    row.PreviousId == entry.PreviousId && row.GateId == entry.GateId && row.Approval == entry.Approval) is { } settled)
            {
                recorded = settled;
                return;
            }

            // Preserve the result of an authorized, already-journaled mutation even if intake
            // changes during the provider call. Every new intent requires the current allowing gate.
            if (!WindowsUpdateHistory.IsPhaseTerminal(entry.Stage))
            {
                await RequireExpectedSanitizationGateAsync(connection, transaction, entry.JobId, entry.GateId, ct)
                    .ConfigureAwait(false);
            }

            if (entry.Stage == WindowsUpdateStage.ScanStarted && history.LastOrDefault() is
                { Stage: WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.InstallPreparing } preparing && entry.PreviousId == preparing.Id)
            {
                var unsent = new WindowsUpdateEntry(Guid.NewGuid(), preparing.Id, entry.JobId, preparing.GateId, entry.OccurredAtUtc,
                    WindowsUpdateStage.DispatchNotSent, null, preparing.Approval, new(WindowsUpdateStatus.ApprovalChanged, null));
                if (!WindowsUpdateHistory.CanAppend(history, unsent)) { throw new JobStoreConflictException(); }
                await InsertWindowsUpdateAsync(connection, transaction, history.Length + 1, unsent, ct).ConfigureAwait(false);
                history = history.Add(unsent);
                entry = entry with { PreviousId = unsent.Id };
            }
            if (!WindowsUpdateHistory.CanAppend(history, entry)) { throw new JobStoreConflictException(); }

            if (entry.Stage is WindowsUpdateStage.DownloadChecked or WindowsUpdateStage.InstallChecked or
                WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.InstallPreparing or
                WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted)
            {
                var packages = await ReadJobJournalHistoriesAsync(transaction, entry.JobId, ct).ConfigureAwait(false);
                if (MaintenancePhases.RequiresPackageRecovery(packages)) { throw new JobStoreConflictException(); }
                var maintenance = await ReadMaintenanceAsync(transaction, entry.JobId, ct).ConfigureAwait(false);
                if (maintenance is { Phase: not MaintenancePhase.FinalVerification })
                {
                    var exactPlan = packages.SingleOrDefault(package => package.Plan.Key == maintenance.Plan);
                    var boot = _readBootSession();
                    if (maintenance.Phase != MaintenancePhase.WindowsUpdates || exactPlan?.Plan.GateDecisionId != entry.GateId ||
                        exactPlan?.ActiveApproval?.ApprovalId != maintenance.ApprovalId || !MaintenancePhases.WithinRestartLimit(maintenance, boot))
                    {
                        throw new JobStoreConflictException();
                    }
                    await RequireMaintenanceEvidenceAsync(transaction, exactPlan!.Plan, ct).ConfigureAwait(false);
                    if (entry.Stage is not (WindowsUpdateStage.DownloadChecked or WindowsUpdateStage.InstallChecked))
                    {
                        var checkedStage = entry.Stage is WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.DownloadStarted
                            ? WindowsUpdateStage.DownloadChecked : WindowsUpdateStage.InstallChecked;
                        var checkedEntry = history.LastOrDefault(row => row.Stage == checkedStage && row.Approval == entry.Approval);
                        if (checkedEntry?.BootSession != boot) { throw new JobStoreConflictException(); }
                    }
                }
            }

            if (entry.Stage is WindowsUpdateStage.Approved or WindowsUpdateStage.DownloadPreparing or WindowsUpdateStage.InstallPreparing
                or WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted)
            {
                if (entry.Approval?.Actions?.MatchesCurrentActions(entry.Approval.Exact) != true ||
                    (entry.Stage == WindowsUpdateStage.Approved && history.Length > WindowsUpdateHistory.MaximumEntries - 12))
                {
                    throw new JobStoreConflictException();
                }
                await RequireWindowsUpdatePolicyAsync(connection, transaction, entry.JobId, entry.Approval.Exact,
                    entry.Approval.ServicingPolicySha256, entry.Approval.PolicyDecisionId, entry.OccurredAtUtc, ct).ConfigureAwait(false);
                if (entry.Stage is WindowsUpdateStage.DownloadStarted or WindowsUpdateStage.InstallStarted)
                {
                    var prepared = history[^1];
                    var expected = entry.Stage == WindowsUpdateStage.DownloadStarted ? WindowsUpdateStage.DownloadPreparing : WindowsUpdateStage.InstallPreparing;
                    var now = _timeProvider.GetUtcNow();
                    if (prepared.Stage != expected || now < prepared.OccurredAtUtc || now - prepared.OccurredAtUtc >= WindowsUpdateHistory.DispatchWindow)
                    {
                        throw new JobStoreConflictException();
                    }
                }
            }

            recorded = entry.Stage == WindowsUpdateStage.ScanStarted || entry.Scan is not null || entry.Operation?.RebootRequired == true
                ? entry with { BootUptime = _readBootUptime(), BootSession = _supportsMaintenance ? _readBootSession() : null } : entry;
            await InsertWindowsUpdateAsync(connection, transaction, history.Length + 1, recorded, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return recorded!;
    }

    private async Task InsertWindowsUpdateAsync(SqliteConnection connection, SqliteTransaction transaction, int sequence,
        WindowsUpdateEntry entry, CancellationToken token)
    {
        var payload = SerializePayload(entry, WindowsUpdateRecordBytes);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO windows_update_journal(job_id, sequence, entry_id, gate_id, payload_json, payload_sha256)
            VALUES ($job_id, $sequence, $id, $gate, $json, $hash);
            """;
        command.Parameters.AddWithValue("$job_id", entry.JobId.Value);
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue("$gate", entry.GateId.Value);
        command.Parameters.AddWithValue("$json", payload.Json);
        command.Parameters.AddWithValue("$hash", payload.Sha256);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task<string> RequireWindowsUpdatePolicyAsync(SqliteConnection connection, SqliteTransaction transaction, JobId jobId,
        WindowsUpdateCandidate exact, string? servicingPolicySha256, string? expectedDecisionId, DateTimeOffset at, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = PolicyDecisionProjection + """

            WHERE d.job_id = $job_id ORDER BY d.decision_sequence DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        var saved = await ReadPolicyDecisionRowAsync(command, token).ConfigureAwait(false);
        var intake = await ReadLatestSanitizationEvidenceAsync(connection, transaction, jobId, token).ConfigureAwait(false);
        if (saved?.Decision.Disposition != Disposition.ReadyToPrepare || intake is null ||
            !saved.Decision.EvidenceIds.Contains(intake.Metadata.EvidenceId) ||
            (expectedDecisionId is not null && saved.Decision.DecisionId != expectedDecisionId) || saved.Decision.EvaluatedAtUtc > at)
        {
            throw new JobStoreConflictException();
        }

        var document = saved.Snapshot.Document;
        if (servicingPolicySha256 is { } digest)
        {
            var now = _timeProvider.GetUtcNow();
            if (saved.Decision.JobEvidenceCount is null || intake.Metadata.Provenance.Kind != ProvenanceKind.HumanConfirmation ||
                document is not
                {
                    IsCurrentSchema: true, SyntheticData: false, Status: PolicyStatus.Effective,
                    ClaimScope: WindowsUpdateServicingPolicy.OrganizationClaimScope
                } ||
                document.Effective.FromUtc is not { } from || from > now || document.Effective.UntilUtc <= now ||
                saved.Decision.ActiveCapabilityIds is not { } selectedCapabilities ||
                !selectedCapabilities.SequenceEqual(document.Rules.SelectMany(rule => rule.ActiveForCapabilityIds)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !WindowsUpdateServicingPolicy.MatchesQualificationOrganizationPolicy(exact, digest, document.ContentSha256, now))
            {
                throw new JobStoreConflictException();
            }
            var job = await ReadStoredJobAsync(connection, transaction, jobId, token).ConfigureAwait(false);
            if (job is not { IsArchived: false } ||
                job.SanitizationEvidence.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ||
                job.Observations.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ||
                job.HumanTests.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture))
            {
                throw new JobStoreConflictException();
            }
        }
        else if (intake.Metadata.Provenance.Kind != ProvenanceKind.SyntheticFixture ||
            document is not { IsCurrentSchema: true, SyntheticData: true, Status: PolicyStatus.Candidate, ClaimScope: "windows_update_fixture" })
        {
            throw new JobStoreConflictException();
        }

        // A subsequent assessment or newly appended fact invalidates preparation against the
        // old decision, including evidence whose source timestamp predates the decision.
        if (saved.Decision.JobEvidenceCount is { } count)
        {
            if (count != await ReadEvidenceCountAsync(connection, transaction, jobId, token).ConfigureAwait(false))
            {
                throw new JobStoreConflictException();
            }
        }
        else
        {
            // Earlier controlled decisions used the last cited evidence ID.
            command.Parameters.Clear();
            command.CommandText = """
                SELECT evidence_id FROM evidence_records WHERE job_id = $job_id ORDER BY evidence_sequence DESC LIMIT 1;
                """;
            command.Parameters.AddWithValue("$job_id", jobId.Value);
            var newestEvidence = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            if (!saved.Decision.EvidenceIds.Any(id => id.Value == newestEvidence))
            {
                throw new JobStoreConflictException();
            }
        }

        await RequirePolicyDecisionEvidenceAsync(connection, transaction, saved.Decision, token).ConfigureAwait(false);
        return saved.Decision.DecisionId;
    }

    private async Task<ImmutableArray<WindowsUpdateEntry>> ReadWindowsUpdatesAsync(SqliteTransaction transaction,
        JobId jobId, CancellationToken token)
    {
        var history = ImmutableArray<WindowsUpdateEntry>.Empty;
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sequence, entry_id, gate_id, length(CAST(payload_json AS BLOB)), payload_json, payload_sha256
            FROM windows_update_journal WHERE job_id = $job_id ORDER BY sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$job_id", jobId.Value);
        command.Parameters.AddWithValue("$limit", WindowsUpdateHistory.MaximumEntries + 1);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (reader.GetInt64(0) != history.Length + 1 || reader.GetInt64(3) is < 2 or > WindowsUpdateRecordBytes)
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            var entry = DeserializePayload<WindowsUpdateEntry>(reader.GetString(4), reader.GetString(5), WindowsUpdateRecordBytes);
            if (entry.JobId != jobId || entry.Id.ToString("D") != reader.GetString(1) || entry.GateId?.Value != reader.GetString(2) ||
                !WindowsUpdateHistory.CanAppend(history, entry))
            {
                throw new JobStoreCorruptionException("store_payload_mismatch");
            }

            history = history.Add(entry);
        }

        return history;
    }
}
