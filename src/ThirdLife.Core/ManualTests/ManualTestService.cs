using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.ManualTests;

public sealed record ManualTestProgressItem(ManualTestCase Case, IReadOnlyList<HumanTestEvidence> History)
{
    public HumanTestEvidence? Latest => History.Count == 0 ? null : History[^1];

    public bool HasBlockerCandidate =>
        Case.IsEssential && History.Any(attempt => attempt.TestResult == HumanTestResult.Fail);
}

public sealed record ManualTestProgress(IReadOnlyList<ManualTestProgressItem> Items)
{
    public ManualTestCase? NextCase => Items
        .FirstOrDefault(item => item.Latest is null or { TestResult: HumanTestResult.NotRun })?.Case;

    public bool HasEssentialFailure => Items.Any(item => item.HasBlockerCandidate);
}

public sealed class ManualTestService
{
    public const int MaximumNotesLength = 256;

    private static readonly ProviderId Provider = new("manual-test-capture");
    private readonly IJobStore _store;
    private readonly TimeProvider _timeProvider;

    public ManualTestService(IJobStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<HumanTestEvidence> RecordAsync(
        JobId jobId,
        string caseId,
        HumanTestResult result,
        OperatorId operatorId,
        string notes,
        ManualTestUnavailableReason? reason = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(jobId, caseId, result, operatorId, notes, reason, assessmentId: null, cancellationToken);

    public async Task<HumanTestEvidence> RecordAsync(
        JobId jobId,
        string caseId,
        HumanTestResult result,
        OperatorId operatorId,
        string notes,
        ManualTestUnavailableReason? reason,
        string? assessmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(operatorId);
        if (!ManualTestCatalog.TryGetCase(caseId, out var testCase))
        {
            throw new ArgumentException("Select a supported manual check.", nameof(caseId));
        }

        var limitation = GetLimitationCode(testCase, result, reason);
        var stored = await _store.LoadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.Job.JobId != jobId)
        {
            throw new JobLifecycleException("job_not_found", "The requested job does not exist.");
        }

        if (stored.IsArchived)
        {
            throw new JobLifecycleException("job_archived", "An archived job cannot record manual checks.");
        }

        var securityReview = ManualTestCatalog.IsSecurityReview(caseId);
        if (securityReview && !SanitizationGate.Inspect(stored).AllowsAssessment)
        {
            throw new JobLifecycleException("sanitization_gate_changed", "Security review requires an assessment under the current intake.");
        }

        var fixtureJob = IsFixtureJob(stored);
        var hasObservation = result is HumanTestResult.Pass or HumanTestResult.Fail;
        if (fixtureJob && hasObservation)
        {
            throw new JobLifecycleException(
                "fixture_manual_result",
                "A fixture job can record unavailable or unperformed checks. Observed results require workshop evidence.");
        }

        // The loaded evidence is in durable append order. Each retest has its own
        // identity and references the previous attempt visible to this operator.
        var previous = stored.HumanTests.LastOrDefault(attempt =>
            IsGuidedRecord(stored, attempt, fixtureJob) && attempt.Details!.CaseId == caseId &&
            attempt.Details.AssessmentId == assessmentId);
        var revision = securityReview ? ManualTestCatalog.SecurityReviewRevision : ManualTestCatalog.ProcedureRevision;
        var details = new ManualTestDetails(caseId, revision, notes, previous?.HumanTestId, assessmentId);
        var origin = fixtureJob ? ProvenanceKind.SyntheticFixture :
            hasObservation ? ProvenanceKind.HumanConfirmation : ProvenanceKind.SystemGenerated;
        var metadata = new EvidenceMetadata(
            EvidenceId.New(),
            PrivacyClassification.WorkshopRestricted,
            hasObservation ? EvidenceClassification.HumanConfirmed : EvidenceClassification.NotAvailable,
            Provider,
            _timeProvider.GetUtcNow(),
            new EvidenceProvenance(origin, revision),
            hasObservation ? ValueAvailability.Available : ValueAvailability.Unknown);
        var record = new HumanTestEvidence(
            HumanTestId.New(), jobId, stored.Job.DeviceId, metadata, result, operatorId, limitation, details);
        await _store.AppendEvidenceAsync(new JobEvidenceBatch(jobId, humanTests: [record], requireUnarchivedJob: true), cancellationToken)
            .ConfigureAwait(false);
        return record;
    }

    public static ManualTestProgress GetProgress(StoredJob job, string? assessmentId = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        var fixtureJob = IsFixtureJob(job);
        var recorded = job.HumanTests.Where(attempt => IsGuidedRecord(job, attempt, fixtureJob) &&
            (attempt.Details!.AssessmentId is null || attempt.Details.AssessmentId == assessmentId))
            .ToLookup(attempt => attempt.Details!.CaseId, StringComparer.Ordinal);
        var items = ManualTestCatalog.Cases.Select(testCase =>
            new ManualTestProgressItem(testCase, Array.AsReadOnly(recorded[testCase.Id].ToArray())));
        return new ManualTestProgress(Array.AsReadOnly(items.ToArray()));
    }

    private static bool IsFixtureJob(StoredJob job) =>
        job.SanitizationEvidence.Any(item => item.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture);

    private static bool IsGuidedRecord(StoredJob job, HumanTestEvidence attempt, bool fixtureJob) =>
        attempt.JobId == job.Job.JobId &&
        attempt.DeviceId == job.Job.DeviceId &&
        attempt.Details is not null &&
        attempt.Metadata.PrivacyClassification == PrivacyClassification.WorkshopRestricted &&
        attempt.Metadata.ProviderId == Provider &&
        attempt.Metadata.Provenance.SourceReference == attempt.Details.ProcedureRevision &&
        (fixtureJob
            ? attempt.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture &&
                attempt.TestResult is HumanTestResult.NotRun or HumanTestResult.NotAvailable
            : attempt.Metadata.Provenance.Kind is ProvenanceKind.HumanConfirmation or ProvenanceKind.SystemGenerated);

    private static string? GetLimitationCode(
        ManualTestCase testCase,
        HumanTestResult result,
        ManualTestUnavailableReason? reason)
    {
        DomainValue.RequireDefined(result, nameof(result));
        if (result != HumanTestResult.NotAvailable)
        {
            if (reason is not null)
            {
                throw new ArgumentException("An unavailable reason is only used with Not available.", nameof(reason));
            }

            return result == HumanTestResult.NotRun ? "not_run" : null;
        }

        if (reason is null || !testCase.UnavailableReasons.Contains(reason.Value))
        {
            throw new ArgumentException("Select an unavailable reason supported by this manual check.", nameof(reason));
        }

        return reason.Value switch
        {
            ManualTestUnavailableReason.CapabilityAbsent => "capability_absent",
            ManualTestUnavailableReason.EquipmentMissing => "equipment_missing",
            ManualTestUnavailableReason.ProviderUnavailable => "provider_unavailable",
            ManualTestUnavailableReason.UnsafeToRun => "unsafe_to_run",
            ManualTestUnavailableReason.PermissionDenied => "permission_denied",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
    }
}
