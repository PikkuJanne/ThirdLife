using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Recovery;

namespace ThirdLife.Persistence;

public sealed partial class SqliteJobStore : IPackageRecoveryStore
{
    public Task<PackageRebootCandidate?> LoadPackageRebootCheckpointAsync(
        JournalEntry restartEntry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restartEntry);
        ThrowIfDisposed();
        if (!RebootCheckpoint.IsRestartResult(restartEntry, JournalActor.PackageBackend))
        {
            return Task.FromResult<PackageRebootCandidate?>(null);
        }

        return ReadRebootCheckpointsAsync<PackageRebootCandidate?>(async (connection, transaction, token) =>
        {
            var jobId = restartEntry.Plan.JobId;
            var stored = await ReadRebootCheckpointAsync(
                connection, transaction, jobId, restartEntry.EntryId, byJournalEntry: true, token).ConfigureAwait(false);
            if (stored is not { IsDecided: false, Candidate: var candidate } ||
                !candidate.Checkpoint.MatchesRestart(restartEntry))
            {
                return null;
            }

            _layout.ValidateJobDirectory(jobId);
            var latest = await ReadLatestRebootEntryAsync(connection, transaction, restartEntry.Plan,
                restartEntry.ActionId, token).ConfigureAwait(false);
            if (latest != restartEntry || !IsAuthenticRebootCandidate(candidate) ||
                await ReadPackageRecoveryAuthorizationAsync(connection, transaction, restartEntry, token).ConfigureAwait(false) is null)
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            return new PackageRebootCandidate(
                new RebootResumeCandidate(candidate.Checkpoint, candidate.Token, isCurrentPolicyPlan: true),
                candidate.Checkpoint.IsCurrent(now) && HasObservedPackageRestart(candidate.Checkpoint),
                now >= candidate.Checkpoint.ExpiresAtUtc);
        }, cancellationToken);
    }

    public async Task<bool> RecordPackageRebootVerificationAsync(
        JournalEntry verification, RebootResumeToken token, Guid checkpointId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentOutOfRangeException.ThrowIfEqual(checkpointId, Guid.Empty);
        ThrowIfDisposed();
        if (verification.Checkpoint != JournalCheckpoint.PackageRebootVerification)
        {
            return false;
        }

        var recorded = false;
        await ExecuteWriteAsync(async (connection, transaction, ct) =>
        {
            var jobId = verification.Plan.JobId;
            var stored = await ReadRebootCheckpointAsync(
                connection, transaction, jobId, checkpointId, byJournalEntry: false, ct).ConfigureAwait(false);
            if (stored is not { IsDecided: false, Candidate: var candidate } ||
                candidate.Checkpoint.PackageBootUptime is null ||
                !CryptographicOperations.FixedTimeEquals(candidate.Token.AuthenticityBlob.AsSpan(), token.AuthenticityBlob.AsSpan()))
            {
                return;
            }

            _layout.ValidateJobDirectory(jobId);
            var now = _timeProvider.GetUtcNow();
            var checkpoint = candidate.Checkpoint;
            var latest = await ReadLatestActionEntryAsync(connection, transaction, verification, ct).ConfigureAwait(false);
            if (latest is null || !checkpoint.MatchesRestart(latest) ||
                verification.OccurredAtUtc < checkpoint.IssuedAtUtc || verification.OccurredAtUtc > now ||
                !checkpoint.IsCurrent(now) || !IsAuthenticRebootCandidate(candidate) || !HasObservedPackageRestart(checkpoint) ||
                !await TryInsertPackageRecoveryVerificationAsync(connection, transaction, latest, verification, ct).ConfigureAwait(false))
            {
                return;
            }

            await InsertRebootDecisionAsync(connection, transaction, checkpoint, RebootDisposition.Resume, now, ct).ConfigureAwait(false);
            recorded = true;
        }, cancellationToken).ConfigureAwait(false);
        return recorded;
    }

    public async Task<bool> RecordPackageRecoveryVerificationAsync(
        JournalEntry prior, JournalEntry verification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(verification);
        ThrowIfDisposed();
        if (verification.Checkpoint != JournalCheckpoint.PackageRecoveryVerification)
        {
            return false;
        }

        var recorded = false;
        await ExecuteWriteAsync(async (connection, transaction, token) =>
        {
            _layout.ValidateJobDirectory(verification.Plan.JobId);
            var latest = await ReadLatestActionEntryAsync(connection, transaction, verification, token).ConfigureAwait(false);
            if (latest == prior)
            {
                recorded = await TryInsertPackageRecoveryVerificationAsync(
                    connection, transaction, prior, verification, token).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        return recorded;
    }

    private async Task IssuePackageRebootCheckpointAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalEntry restartEntry, CancellationToken token)
    {
        var now = _timeProvider.GetUtcNow();
        if (now < restartEntry.OccurredAtUtc || now > DateTimeOffset.MaxValue - RebootCheckpoint.MaximumLifetime ||
            _readBootUptime() is not { } uptime ||
            await ReadPackageRecoveryAuthorizationAsync(connection, transaction, restartEntry, token).ConfigureAwait(false) is not { } authority)
        {
            return;
        }

        var checkpoint = new RebootCheckpoint(Guid.NewGuid(), restartEntry.Plan, restartEntry.ActionId, restartEntry.EntryId,
            authority.Approval.ApprovalId, restartEntry.CorrelationId, now, now + RebootCheckpoint.MaximumLifetime, uptime,
            _supportsMaintenance ? _readBootSession() : null);
        if (WindowsRebootAuthenticator.TrySeal(checkpoint, _databaseGuard) is { } protectedToken)
        {
            await InsertRebootCheckpointAsync(connection, transaction, checkpoint, protectedToken, token).ConfigureAwait(false);
        }
    }

    private bool HasObservedPackageRestart(RebootCheckpoint checkpoint) =>
        checkpoint.PackageBootSession is { } session
            ? _readBootSession() > session
            : checkpoint.PackageBootUptime is { } saved && _readBootUptime() is { } current && current < saved;

    private async Task<bool> TryInsertPackageRecoveryVerificationAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalEntry prior, JournalEntry verification, CancellationToken token)
    {
        var authority = await ReadPackageRecoveryAuthorizationAsync(connection, transaction, prior, token).ConfigureAwait(false);
        if (authority is not { } current || !JournalStateMachine.CanAppend(current.Plan, current.Approval, prior, verification) ||
            await ReadJournalEntryByIdAsync(connection, transaction, verification, token).ConfigureAwait(false) is not null)
        {
            return false;
        }

        await RequireJournalCapacityAsync(connection, transaction, verification.Plan,
            "SELECT count(*) FROM action_journal_entries WHERE job_id = $job_id;",
            JournalHistory.MaximumEntriesPerJob, token).ConfigureAwait(false);
        await InsertJournalEntryAsync(connection, transaction, verification, token).ConfigureAwait(false);
        return true;
    }

    private async Task<(JournalPlanSnapshot Plan, JournalApproval Approval)?> ReadPackageRecoveryAuthorizationAsync(
        SqliteConnection connection, SqliteTransaction transaction, JournalEntry entry, CancellationToken token)
    {
        var plan = await RequireJournalPlanAsync(connection, transaction, entry.Plan, token).ConfigureAwait(false);
        if (!plan.IsCurrentPolicyPlan || FindPackageJournalAction(plan, entry.ActionId) is null ||
            (plan.Schedule is not null &&
                !await HasMatchingPackageDispatchAsync(connection, transaction, entry, entry, token).ConfigureAwait(false)))
        {
            return null;
        }

        var approval = await ReadApprovalAsync(connection, transaction, entry.Plan, null, token).ConfigureAwait(false);
        if (approval is null || approval.Plan != entry.Plan || approval.ApprovalId != entry.ApprovalId)
        {
            return null;
        }

        try
        {
            await RequireCurrentJournalPlanAsync(connection, transaction, plan, token).ConfigureAwait(false);
            return (plan, approval);
        }
        catch (JobStoreConflictException)
        {
            return null;
        }
    }
}
