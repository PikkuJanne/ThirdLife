using System.Collections.ObjectModel;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Inventory.Normalization;

public enum ProviderRunOutcome
{
    Completed = 1,
    Unavailable,
    AccessDenied,
    Cancelled,
    TimedOut,
    InvalidData,
    ContractInvalid,
    CleanupIncomplete,
    Failed,
}

public enum ProviderRecoveryAction
{
    RetryCollection = 1,
    ReviewAccess,
    ReviewProviderData,
    ReviewProviderContract,
    ReviewCleanup,
    ReviewUnexpectedFailure,
}

public sealed class SanitizedProviderError
{
    internal SanitizedProviderError(ProviderRunOutcome outcome)
    {
        if (!Enum.IsDefined(outcome) || outcome == ProviderRunOutcome.Completed)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A failure outcome is required.");
        }

        Outcome = outcome;
    }

    public ProviderRunOutcome Outcome { get; }

    public string ErrorCode => Outcome switch
    {
        ProviderRunOutcome.Unavailable => "provider_unavailable",
        ProviderRunOutcome.AccessDenied => "provider_access_denied",
        ProviderRunOutcome.Cancelled => "provider_collection_cancelled",
        ProviderRunOutcome.TimedOut => "provider_collection_timed_out",
        ProviderRunOutcome.InvalidData => "provider_data_invalid",
        ProviderRunOutcome.ContractInvalid => "provider_contract_invalid",
        ProviderRunOutcome.CleanupIncomplete => "provider_cleanup_incomplete",
        ProviderRunOutcome.Failed => "provider_failed",
        _ => throw new InvalidOperationException("A completed provider run has no error code."),
    };

    public ProviderRecoveryAction RecoveryAction => Outcome switch
    {
        ProviderRunOutcome.Unavailable or
        ProviderRunOutcome.Cancelled or
        ProviderRunOutcome.TimedOut => ProviderRecoveryAction.RetryCollection,
        ProviderRunOutcome.AccessDenied => ProviderRecoveryAction.ReviewAccess,
        ProviderRunOutcome.InvalidData => ProviderRecoveryAction.ReviewProviderData,
        ProviderRunOutcome.ContractInvalid => ProviderRecoveryAction.ReviewProviderContract,
        ProviderRunOutcome.CleanupIncomplete => ProviderRecoveryAction.ReviewCleanup,
        ProviderRunOutcome.Failed => ProviderRecoveryAction.ReviewUnexpectedFailure,
        _ => throw new InvalidOperationException("A completed provider run has no recovery action."),
    };

    public ProviderLimitation Limitation => Outcome switch
    {
        ProviderRunOutcome.Unavailable => ProviderLimitation.ProviderUnavailable,
        ProviderRunOutcome.AccessDenied => ProviderLimitation.AccessDenied,
        ProviderRunOutcome.Cancelled => ProviderLimitation.CollectionCancelled,
        ProviderRunOutcome.TimedOut => ProviderLimitation.CollectionTimedOut,
        ProviderRunOutcome.InvalidData => ProviderLimitation.InvalidProviderData,
        ProviderRunOutcome.ContractInvalid => ProviderLimitation.ProviderContractInvalid,
        ProviderRunOutcome.CleanupIncomplete => ProviderLimitation.CleanupIncomplete,
        ProviderRunOutcome.Failed => ProviderLimitation.UnexpectedProviderFailure,
        _ => throw new InvalidOperationException("A completed provider run has no limitation."),
    };
}

public sealed class ProviderRunResult
{
    private ProviderRunResult(
        ProviderId providerId,
        ProviderRunOutcome outcome,
        IEnumerable<Observation> observations,
        SanitizedProviderError? error)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Outcome = outcome;
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The provider outcome is not defined.");
        }

        Error = error;

        var copiedObservations = observations?.ToArray()
            ?? throw new ArgumentNullException(nameof(observations));
        if (copiedObservations.Length == 0 || copiedObservations.Any(static observation => observation is null))
        {
            throw new ArgumentException("A provider run must contain non-null observations.", nameof(observations));
        }

        if ((Outcome == ProviderRunOutcome.Completed) != (Error is null))
        {
            throw new ArgumentException(
                "Completed runs cannot contain an error and unsuccessful runs require one.",
                nameof(error));
        }

        if (Error is not null && Error.Outcome != Outcome)
        {
            throw new ArgumentException("The sanitized error must match the run outcome.", nameof(error));
        }

        if (copiedObservations.Any(observation => observation.Metadata.ProviderId != ProviderId))
        {
            throw new ArgumentException("Every observation must be attributed to the run provider.", nameof(observations));
        }

        if (Error is not null && !copiedObservations.Any(observation =>
                observation.Metadata.EvidenceClassification == EvidenceClassification.NotAvailable &&
                observation.Metadata.ValueAvailability == ValueAvailability.Unknown &&
                observation.Value is null &&
                observation.LimitationCode == Error.Limitation.ToCode()))
        {
            throw new ArgumentException(
                "An unsuccessful run must identify at least one affected not-available observation.",
                nameof(observations));
        }

        Observations = Array.AsReadOnly(copiedObservations);
    }

    public ProviderId ProviderId { get; }

    public ProviderRunOutcome Outcome { get; }

    public ReadOnlyCollection<Observation> Observations { get; }

    public SanitizedProviderError? Error { get; }

    internal static ProviderRunResult Completed(ProviderId providerId, IEnumerable<Observation> observations) =>
        new(providerId, ProviderRunOutcome.Completed, observations, error: null);

    internal static ProviderRunResult Failed(
        ProviderId providerId,
        ProviderRunOutcome outcome,
        IEnumerable<Observation> observations,
        SanitizedProviderError error) =>
        new(providerId, outcome, observations, error);
}
