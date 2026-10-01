using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Serialization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Jobs;

public interface IJobStore : IAsyncDisposable
{
    Task CreateJobAsync(Job job, CancellationToken cancellationToken = default);

    Task AppendEvidenceAsync(
        JobEvidenceBatch batch,
        CancellationToken cancellationToken = default);

    Task SetArchiveStateAsync(
        JobId jobId,
        bool isArchived,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    Task<SanitizationGateDecision> RecordSanitizationGateDecisionAsync(
        SanitizationGateDecision candidate,
        CancellationToken cancellationToken = default);

    Task<StoredJob?> LoadJobAsync(
        JobId jobId,
        CancellationToken cancellationToken = default);

    // Simple adapters may use the bounded job projection; the SQLite implementation reads
    // only the current gate records, avoiding repeated loading of observation history.
    async Task RequireSanitizationGateAsync(
        JobId jobId,
        SanitizationGateDecisionId expectedDecisionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(expectedDecisionId);
        var job = await LoadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        var access = job is null ? null : SanitizationGate.Inspect(job);
        if (job?.Job.JobId != jobId || access is not { AllowsAssessment: true } ||
            access.Decision!.DecisionId != expectedDecisionId)
        {
            throw new JobLifecycleException("sanitization_gate_changed",
                "The current sanitization gate does not authorize this assessment.");
        }
    }
}

[JsonConverter(typeof(StableStringEnumConverter<JobCheckpointKind>))]
public enum JobCheckpointKind
{
    [JsonStringEnumMemberName("job_created")]
    JobCreated = 1,

    [JsonStringEnumMemberName("evidence_committed")]
    EvidenceCommitted,

    [JsonStringEnumMemberName("archived")]
    Archived,

    [JsonStringEnumMemberName("restored")]
    Restored,
}

public sealed record CheckpointId
{
    public CheckpointId(string value)
    {
        Value = DomainValue.RequireIdentifier(value, nameof(value));
    }

    public string Value { get; }

    public static CheckpointId New() =>
        new(string.Concat("checkpoint-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

public sealed record JobCheckpoint
{
    public JobCheckpoint(
        CheckpointId checkpointId,
        JobId jobId,
        JobCheckpointKind kind,
        DateTimeOffset recordedAtUtc)
    {
        CheckpointId = checkpointId ?? throw new ArgumentNullException(nameof(checkpointId));
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        Kind = DomainValue.RequireDefined(kind, nameof(kind));
        RecordedAtUtc = DomainValue.RequireTimestamp(recordedAtUtc, nameof(recordedAtUtc));
    }

    public CheckpointId CheckpointId { get; }

    public JobId JobId { get; }

    public JobCheckpointKind Kind { get; }

    public DateTimeOffset RecordedAtUtc { get; }
}

public sealed class JobEvidenceBatch
{
    public const int MaximumRecordsPerBatch = 256;

    public JobEvidenceBatch(
        JobId jobId,
        IEnumerable<Observation>? observations = null,
        IEnumerable<SanitizationEvidence>? sanitizationEvidence = null,
        IEnumerable<HumanTestEvidence>? humanTests = null,
        SanitizationGateDecisionId? expectedSanitizationGateDecisionId = null)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        Observations = Copy(observations, nameof(observations));
        SanitizationEvidence = Copy(sanitizationEvidence, nameof(sanitizationEvidence));
        HumanTests = Copy(humanTests, nameof(humanTests));
        ExpectedSanitizationGateDecisionId = expectedSanitizationGateDecisionId;

        if (ExpectedSanitizationGateDecisionId is not null &&
            (SanitizationEvidence.Count != 0 || HumanTests.Count != 0))
        {
            throw new ArgumentException(
                "A sanitization-gated batch may contain only assessment observations.",
                nameof(expectedSanitizationGateDecisionId));
        }

        var total = Observations.Count + SanitizationEvidence.Count + HumanTests.Count;
        if (total == 0)
        {
            throw new ArgumentException("An evidence batch must contain at least one typed record.");
        }

        if (total > MaximumRecordsPerBatch)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observations),
                total,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"An evidence batch may contain at most {MaximumRecordsPerBatch} records."));
        }

        var evidenceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidenceId in EnumerateEvidenceIds())
        {
            if (!evidenceIds.Add(evidenceId))
            {
                throw new ArgumentException("An evidence batch contains a duplicate evidence identifier.");
            }
        }
    }

    public JobId JobId { get; }

    public IReadOnlyList<Observation> Observations { get; }

    public IReadOnlyList<SanitizationEvidence> SanitizationEvidence { get; }

    public IReadOnlyList<HumanTestEvidence> HumanTests { get; }

    // When supplied, the store must revalidate this exact persisted allowing decision
    // and current job state in the same transaction that appends the observations.
    public SanitizationGateDecisionId? ExpectedSanitizationGateDecisionId { get; }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values, string parameterName)
        where T : class
    {
        if (values is null)
        {
            return Array.Empty<T>();
        }

        var array = values.ToArray();
        if (array.Any(value => value is null))
        {
            throw new ArgumentException(
                "Typed persistence batches cannot contain null records.",
                parameterName);
        }

        return new ReadOnlyCollection<T>(array);
    }

    private IEnumerable<string> EnumerateEvidenceIds()
    {
        foreach (var observation in Observations)
        {
            yield return observation.Metadata.EvidenceId.Value;
        }

        foreach (var evidence in SanitizationEvidence)
        {
            yield return evidence.Metadata.EvidenceId.Value;
        }

        foreach (var test in HumanTests)
        {
            yield return test.Metadata.EvidenceId.Value;
        }
    }
}

public sealed class StoredJob
{
    public StoredJob(
        Job job,
        bool isArchived,
        DateTimeOffset? archivedAtUtc,
        IEnumerable<Observation> observations,
        IEnumerable<SanitizationEvidence> sanitizationEvidence,
        IEnumerable<HumanTestEvidence> humanTests,
        IEnumerable<SanitizationGateDecision> sanitizationGateDecisions,
        IEnumerable<JobCheckpoint> checkpoints)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        IsArchived = isArchived;
        ArchivedAtUtc = archivedAtUtc is null
            ? null
            : DomainValue.RequireTimestamp(archivedAtUtc.Value, nameof(archivedAtUtc));

        if (isArchived != (ArchivedAtUtc is not null))
        {
            throw new ArgumentException(
                "Archive state and archive timestamp must agree.",
                nameof(archivedAtUtc));
        }

        Observations = Array.AsReadOnly(
            observations?.ToArray() ?? throw new ArgumentNullException(nameof(observations)));
        SanitizationEvidence = Array.AsReadOnly(
            sanitizationEvidence?.ToArray() ??
            throw new ArgumentNullException(nameof(sanitizationEvidence)));
        HumanTests = Array.AsReadOnly(
            humanTests?.ToArray() ?? throw new ArgumentNullException(nameof(humanTests)));
        SanitizationGateDecisions = Array.AsReadOnly(
            sanitizationGateDecisions?.ToArray() ??
            throw new ArgumentNullException(nameof(sanitizationGateDecisions)));
        Checkpoints = Array.AsReadOnly(
            checkpoints?.ToArray() ?? throw new ArgumentNullException(nameof(checkpoints)));
    }

    public Job Job { get; }

    public bool IsArchived { get; }

    public DateTimeOffset? ArchivedAtUtc { get; }

    public IReadOnlyList<Observation> Observations { get; }

    public IReadOnlyList<SanitizationEvidence> SanitizationEvidence { get; }

    public IReadOnlyList<HumanTestEvidence> HumanTests { get; }

    public IReadOnlyList<SanitizationGateDecision> SanitizationGateDecisions { get; }

    public IReadOnlyList<JobCheckpoint> Checkpoints { get; }
}
