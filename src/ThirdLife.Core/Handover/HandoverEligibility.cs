using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Handover;

public enum HandoverRequirement
{
    OpenJob = 1,
    SupportedPlatform,
    Sanitization,
    Ownership,
    PolicyDisposition,
    AuthorizedExceptions,
    EssentialActionVerification,
    FinalProfileVerification,
    ColdBootVerification,
    PendingRestart,
    Finalization,
    RecipientHandoverMode,
    WorkshopRecord,
    RecipientGuide,
    SupportBundleDecision,
}

public enum HandoverEvidenceState
{
    Unavailable = 1,
    Verified,
    Failed,
    Stale,
    Unauthorized,
    OmittedByRecordedChoice,
}

public enum HandoverBlockReason
{
    JobArchived = 1,
    EvidenceMissing,
    EvidenceConflict,
    ScopeMismatch,
    EvidenceUnavailable,
    CheckFailed,
    EvidenceStale,
    AuthorizationMissing,
    PolicyRequiresReview,
    FinalizationIncomplete,
    OutputUnbound,
}

public enum HandoverEvidenceOrigin
{
    ActiveDevice = 1,
    SyntheticFixture,
}

/// <summary>The exact reviewed job state and evidence origin, supplied by the protected store.</summary>
public sealed record HandoverReviewScope
{
    public HandoverReviewScope(JobId jobId, string version, HandoverEvidenceOrigin origin)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        Version = DomainValue.RequireVersion(version, nameof(version));
        Origin = DomainValue.RequireDefined(origin, nameof(origin));
    }

    public JobId JobId { get; }
    public string Version { get; }
    public HandoverEvidenceOrigin Origin { get; }
}

/// <summary>One typed result derived from the requirement's persisted evidence.</summary>
public sealed record HandoverRequirementEvidence
{
    public HandoverRequirementEvidence(HandoverRequirement requirement, HandoverEvidenceState state,
        HandoverReviewScope scope, string? evidenceVersion = null, HandoverOutputReference? output = null,
        HandoverSupportBundleOmission? supportBundleOmission = null)
    {
        Requirement = DomainValue.RequireDefined(requirement, nameof(requirement));
        State = DomainValue.RequireDefined(state, nameof(state));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (requirement == HandoverRequirement.OpenJob ||
            (state == HandoverEvidenceState.OmittedByRecordedChoice && requirement != HandoverRequirement.SupportBundleDecision))
        {
            throw new ArgumentException("This evidence state is unavailable for the requirement.", nameof(requirement));
        }

        EvidenceVersion = state is HandoverEvidenceState.Verified or HandoverEvidenceState.OmittedByRecordedChoice
            ? DomainValue.RequireVersion(evidenceVersion, nameof(evidenceVersion))
            : evidenceVersion is null ? null : DomainValue.RequireVersion(evidenceVersion, nameof(evidenceVersion));
        Output = output;
        SupportBundleOmission = supportBundleOmission;
    }

    public HandoverRequirement Requirement { get; }
    public HandoverEvidenceState State { get; }
    public HandoverReviewScope Scope { get; }
    public string? EvidenceVersion { get; }
    public HandoverOutputReference? Output { get; }
    public HandoverSupportBundleOmission? SupportBundleOmission { get; }
}

public sealed record HandoverBlocker(HandoverRequirement Requirement, HandoverBlockReason Reason);

public sealed class HandoverEligibility
{
    private HandoverEligibility(HandoverReviewScope scope, IReadOnlyList<HandoverBlocker> blockers,
        IReadOnlyList<HandoverRequirementEvidence> evidence)
    {
        Scope = scope;
        Blockers = blockers;
        Evidence = evidence;
    }

    public HandoverReviewScope Scope { get; }
    public IReadOnlyList<HandoverBlocker> Blockers { get; }
    public IReadOnlyList<HandoverRequirementEvidence> Evidence { get; }
    public bool CanRequestSignOff => Blockers.Count == 0;

    public bool MatchesReview(HandoverEligibility reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        return CanRequestSignOff && reviewed.CanRequestSignOff && Scope == reviewed.Scope && Evidence.SequenceEqual(reviewed.Evidence);
    }

    private static readonly HandoverRequirement[] EvidenceRequirements = Enum.GetValues<HandoverRequirement>()
        .Where(requirement => requirement != HandoverRequirement.OpenJob).ToArray();

    /// <summary>Evaluates the reviewed state for a separate attributable sign-off action.</summary>
    public static HandoverEligibility Evaluate(HandoverReviewScope currentScope, bool isArchived,
        Disposition? policyDisposition, FinalizationChecklist? finalization,
        IReadOnlyList<HandoverRequirementEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(currentScope);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count > EvidenceRequirements.Length * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence));
        }

        var boundedEvidence = evidence.Take(EvidenceRequirements.Length * 2 + 1).ToArray();
        if (boundedEvidence.Length > EvidenceRequirements.Length * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence));
        }

        if (boundedEvidence.Any(item => item is null))
        {
            throw new ArgumentException("Handover evidence requires typed results.", nameof(evidence));
        }

        var byRequirement = boundedEvidence.ToLookup(item => item.Requirement);
        var blockers = new List<HandoverBlocker>();
        if (isArchived)
        {
            blockers.Add(new(HandoverRequirement.OpenJob, HandoverBlockReason.JobArchived));
        }

        foreach (var requirement in EvidenceRequirements)
        {
            var matching = byRequirement[requirement].Take(2).ToArray();
            var reason = matching.Length switch
            {
                0 => HandoverBlockReason.EvidenceMissing,
                1 => CheckEvidence(matching[0], currentScope, policyDisposition, finalization),
                _ => HandoverBlockReason.EvidenceConflict,
            };
            if (reason is { } blocker)
            {
                blockers.Add(new(requirement, blocker));
            }
        }

        return new(currentScope, blockers.AsReadOnly(), Array.AsReadOnly(boundedEvidence.OrderBy(item => item.Requirement).ToArray()));
    }

    private static HandoverBlockReason? CheckEvidence(HandoverRequirementEvidence evidence,
        HandoverReviewScope currentScope, Disposition? policyDisposition, FinalizationChecklist? finalization)
    {
        if (evidence.Scope != currentScope)
        {
            return HandoverBlockReason.ScopeMismatch;
        }

        return evidence.State switch
        {
            HandoverEvidenceState.Unavailable => HandoverBlockReason.EvidenceUnavailable,
            HandoverEvidenceState.Failed => HandoverBlockReason.CheckFailed,
            HandoverEvidenceState.Stale => HandoverBlockReason.EvidenceStale,
            HandoverEvidenceState.Unauthorized => HandoverBlockReason.AuthorizationMissing,
            HandoverEvidenceState.Verified when evidence.Requirement == HandoverRequirement.PolicyDisposition &&
                policyDisposition != Disposition.ReadyToPrepare => HandoverBlockReason.PolicyRequiresReview,
            HandoverEvidenceState.Verified when evidence.Requirement == HandoverRequirement.Finalization &&
                (finalization?.EvidenceVersion != evidence.EvidenceVersion || finalization is not { HasBlockingChecks: false }) =>
                HandoverBlockReason.FinalizationIncomplete,
            HandoverEvidenceState.Verified when OutputKind(evidence.Requirement) is { } kind &&
                (evidence.Output is not { } output || output.Kind != kind || output.Scope != currentScope ||
                 output.Version != evidence.EvidenceVersion || evidence.SupportBundleOmission is not null) => HandoverBlockReason.OutputUnbound,
            HandoverEvidenceState.OmittedByRecordedChoice when evidence.SupportBundleOmission is not { } omission ||
                omission.Scope != currentScope || omission.Version != evidence.EvidenceVersion || evidence.Output is not null =>
                HandoverBlockReason.OutputUnbound,
            HandoverEvidenceState.Verified when OutputKind(evidence.Requirement) is null &&
                (evidence.Output is not null || evidence.SupportBundleOmission is not null) => HandoverBlockReason.OutputUnbound,
            HandoverEvidenceState.Verified or HandoverEvidenceState.OmittedByRecordedChoice => null,
            _ => HandoverBlockReason.EvidenceUnavailable,
        };
    }

    private static HandoverOutputKind? OutputKind(HandoverRequirement requirement) => requirement switch
    {
        HandoverRequirement.WorkshopRecord => HandoverOutputKind.WorkshopRecord,
        HandoverRequirement.RecipientGuide => HandoverOutputKind.RecipientGuide,
        HandoverRequirement.SupportBundleDecision => HandoverOutputKind.SupportBundle,
        _ => null,
    };
}
