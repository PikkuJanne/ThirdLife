using System.Buffers;
using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory.Normalization;

namespace ThirdLife.Inventory;

public sealed record AssessmentId
{
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create("0123456789abcdef");

    public AssessmentId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 43 || !value.StartsWith("assessment-", StringComparison.Ordinal) ||
            value.AsSpan(11).ContainsAnyExcept(HexCharacters))
        {
            throw new ArgumentException("An assessment identifier must use the generated opaque format.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static AssessmentId New() =>
        new(string.Concat("assessment-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

    public override string ToString() => Value;
}

public enum AssessmentOutcome
{
    Incomplete = 1,
    Completed,
    Cancelled,
    RequiresReview,
}

public sealed class AssessmentException : Exception
{
    private static readonly SearchValues<char> ResultCodeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz_");

    public AssessmentException(string resultCode, AssessmentId? assessmentId = null)
        : base("The assessment could not be completed or read safely. Review its recorded state before retrying.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultCode);
        if (!resultCode.StartsWith("assessment_", StringComparison.Ordinal) || resultCode.Length > 64 ||
            resultCode.AsSpan().ContainsAnyExcept(ResultCodeCharacters))
        {
            throw new ArgumentException("An assessment result code must use the bounded product-code format.", nameof(resultCode));
        }

        ResultCode = resultCode;
        AssessmentId = assessmentId;
    }

    public string ResultCode { get; }

    public AssessmentId? AssessmentId { get; }
}

public sealed class AssessmentProviderSnapshot
{
    internal AssessmentProviderSnapshot(
        ProviderId providerId,
        ProviderRunOutcome? outcome,
        IEnumerable<Observation> observations)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(observations);
        var copied = observations.Take(65).ToArray();
        if ((outcome is not null && !Enum.IsDefined(outcome.Value)) || copied.Length > 64 ||
            (outcome is null) != (copied.Length == 0) ||
            copied.Any(observation => observation is null || observation.Metadata.ProviderId != providerId))
        {
            throw new ArgumentException("The provider snapshot requires a bounded, attributed observation set.", nameof(observations));
        }

        ProviderId = providerId;
        Outcome = outcome;
        Observations = Array.AsReadOnly(copied);
    }

    public ProviderId ProviderId { get; }

    public ProviderRunOutcome? Outcome { get; }

    public IReadOnlyList<Observation> Observations { get; }
}

public sealed class AssessmentSnapshot
{
    internal AssessmentSnapshot(
        AssessmentId assessmentId,
        JobId jobId,
        SanitizationGateDecisionId gateDecisionId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? finishedAtUtc,
        AssessmentOutcome outcome,
        IEnumerable<AssessmentProviderSnapshot> providers)
    {
        ArgumentNullException.ThrowIfNull(assessmentId);
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(gateDecisionId);
        ArgumentNullException.ThrowIfNull(providers);
        var copied = providers.Take(AssessmentJournal.MaximumProviders + 1).ToArray();
        if (!Enum.IsDefined(outcome) || startedAtUtc == default || finishedAtUtc == default(DateTimeOffset) ||
            (outcome == AssessmentOutcome.Incomplete) != (finishedAtUtc is null) ||
            copied.Length is < 1 or > AssessmentJournal.MaximumProviders ||
            copied.Any(static provider => provider is null) ||
            copied.Select(static provider => provider.ProviderId).Distinct().Count() != copied.Length)
        {
            throw new ArgumentException("The assessment snapshot is incomplete or inconsistent.", nameof(providers));
        }

        AssessmentId = assessmentId;
        JobId = jobId;
        GateDecisionId = gateDecisionId;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        FinishedAtUtc = finishedAtUtc?.ToUniversalTime();
        Outcome = outcome;
        Providers = Array.AsReadOnly(copied);
        Observations = Array.AsReadOnly(Providers.SelectMany(static provider => provider.Observations).ToArray());
        LifecycleEvidence = Array.AsReadOnly(Observations.Where(static observation =>
            observation.EvidenceKey.Value.StartsWith("os.lifecycle.", StringComparison.Ordinal) ||
            observation.EvidenceKey.Value == "os.support_target").ToArray());
    }

    public AssessmentId AssessmentId { get; }

    public JobId JobId { get; }

    public SanitizationGateDecisionId GateDecisionId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? FinishedAtUtc { get; }

    public AssessmentOutcome Outcome { get; }

    public IReadOnlyList<AssessmentProviderSnapshot> Providers { get; }

    public IReadOnlyList<Observation> Observations { get; }

    public IReadOnlyList<Observation> LifecycleEvidence { get; }
}
