using System.Collections.Immutable;
using ThirdLife.Actions.Contracts;
using ThirdLife.Actions.Journal;
using ThirdLife.Actions.Registry;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Policy.Decisions;
using ThirdLife.Policy.Evaluation;
using ThirdLife.Policy.Exceptions;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Actions.Planning;

public static class PlanResolver
{
    /// <summary>Checks the current policy evidence against the evidence bound to a saved plan.</summary>
    public static bool MatchesEvidence(JournalPlanSnapshot plan, IEnumerable<Observation> observations, DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentOutOfRangeException.ThrowIfEqual(asOfUtc, default);
        var facts = SnapshotEvidence(observations, plan.SyntheticData, asOfUtc);
        return !facts.IsDefault && PlanIdentity.EvidenceHash(facts) == plan.EvidenceSha256;
    }

    /// <summary>Proposes preparation for a current job and its normalized policy evidence.</summary>
    /// <remarks>Callers supply evidence projected from the current assessment under the saved sanitization gate.</remarks>
    public static ProposedPlan Resolve(
        StoredJob job,
        PolicyDocument policy,
        ProfileDocument profile,
        CatalogDocument catalog,
        IEnumerable<Observation> observations,
        IEnumerable<string> selectedOptionalCapabilityIds,
        DateTimeOffset asOfUtc,
        PolicyExceptionHistory? exceptions = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(selectedOptionalCapabilityIds);
        ArgumentOutOfRangeException.ThrowIfEqual(asOfUtc, default);

        var gate = SanitizationGate.Inspect(job);
        var subject = new PlanSubject(job.Job.JobId, gate.Decision?.DecisionId,
            profile.ContentSha256, catalog.ContentSha256, policy.ContentSha256, null, profile.SyntheticData, [],
            job.Observations.Count + job.HumanTests.Count + job.SanitizationEvidence.Count);
        var choices = profile.RecipientChoices.OrderBy(choice => choice.ChoiceId, StringComparer.Ordinal).ToImmutableArray();
        ProposedPlan Block(PlanBlockReason reason, DispositionDecision? decision = null) =>
            new(subject, decision, reason, [], choices);

        if (profile.Catalog.Id != catalog.CatalogId || profile.Catalog.Version != catalog.Version ||
            profile.CatalogContentSha256 != catalog.ContentSha256 || profile.SyntheticData != catalog.SyntheticData)
        {
            return Block(PlanBlockReason.ProfileCatalogMismatch);
        }

        if (profile.Policy.Id != policy.PolicyId || profile.Policy.Version != policy.Version)
        {
            return Block(PlanBlockReason.ProfilePolicyMismatch);
        }

        if (!PolicyApplies(policy, profile.SyntheticData, asOfUtc))
        {
            return Block(PlanBlockReason.PolicyNotApplicable);
        }

        if (!gate.AllowsAssessment || gate.Decision is not { } currentGate ||
            currentGate.EvaluatedAtUtc > asOfUtc || job.Job.CreatedAtUtc > asOfUtc ||
            !IsKnownIntakeVersion(currentGate.PolicyVersion, policy))
        {
            return Block(PlanBlockReason.SanitizationRequired);
        }

        var activeCapabilities = SelectCapabilities(profile.WorkshopCapabilities, selectedOptionalCapabilityIds);
        if (activeCapabilities.IsDefault)
        {
            return Block(PlanBlockReason.CapabilitySelectionInvalid);
        }

        var activeIds = activeCapabilities.Select(capability => capability.CapabilityId)
            .Order(StringComparer.Ordinal).ToImmutableArray();
        subject = subject with { ActiveCapabilityIds = activeIds };
        var facts = SnapshotEvidence(observations, profile.SyntheticData, asOfUtc);
        if (facts.IsDefault || (exceptions is not null && exceptions.JobId != job.Job.JobId))
        {
            return Block(PlanBlockReason.EvidenceInvalid);
        }

        subject = subject with { EvidenceSha256 = PlanIdentity.EvidenceHash(facts) };
        var intake = job.SanitizationEvidence[^1];
        if ((!profile.SyntheticData && intake.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture) ||
            !MatchesSanitization(facts, intake))
        {
            return Block(PlanBlockReason.SanitizationRequired);
        }

        var result = exceptions is null
            ? DispositionEngine.Evaluate(policy, facts, activeIds)
            : DispositionEngine.Evaluate(policy, facts, activeIds, exceptions, asOfUtc);
        if (result.Disposition != Disposition.ReadyToPrepare)
        {
            return Block(PlanBlockReason.PolicyBlocksPreparation, result);
        }

        var actions = ImmutableArray.CreateBuilder<PlannedAction>(activeCapabilities.Length);
        foreach (var capability in activeCapabilities)
        {
            var entry = catalog.Applications.SingleOrDefault(item => item.CapabilityId == capability.CapabilityId);
            if (entry is null || !PackageApplies(entry, policy.Target, profile.SyntheticData, asOfUtc))
            {
                return Block(PlanBlockReason.PackageNotApplicable, result);
            }

            var id = PlanIdentity.ActionId(subject, result, capability.CapabilityId);
            ActionRequest request = profile.SyntheticData
                ? ActionRegistry.CreateNoOp(id)
                : ActionRegistry.CreateInstallPackage(id, entry.Package.SourceId, entry.Package.PackageId,
                    entry.Package.ExactVersion, entry.Review.Publisher, ActionScope.Machine, ActionArchitecture.X64);
            actions.Add(new PlannedAction(capability.CapabilityId, capability.ReasonCode, capability.Intent,
                request, ActionRegistry.Describe(request), entry));
        }

        return new ProposedPlan(subject, result, null, actions.MoveToImmutable(), choices);
    }

    private static bool IsKnownIntakeVersion(string version, PolicyDocument policy) =>
        version == SanitizationGate.WorkshopIntakePolicyVersion ||
        version == string.Concat(policy.PolicyId, "@", policy.Version);

    internal static bool PolicyApplies(PolicyDocument policy, bool synthetic, DateTimeOffset asOfUtc)
    {
        if (!policy.IsCurrentSchema)
        {
            return false;
        }

        if (synthetic)
        {
            return policy.SyntheticData && policy.Status == PolicyStatus.Candidate;
        }

        return !policy.SyntheticData && policy.Status == PolicyStatus.Effective &&
            policy.Effective.FromUtc is { } from && from <= asOfUtc &&
            (policy.Effective.UntilUtc is not { } until || asOfUtc < until);
    }

    private static ImmutableArray<ProfileCapability> SelectCapabilities(
        ImmutableArray<ProfileCapability> workshopCapabilities, IEnumerable<string> selectedOptional)
    {
        var availableOptions = workshopCapabilities.Where(capability => capability.Intent == CapabilityIntent.Optional)
            .Select(capability => capability.CapabilityId).ToHashSet(StringComparer.Ordinal);
        var selection = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var id in selectedOptional)
        {
            if (++count > PolicyEvaluator.MaximumCapabilityIds || id is null || !availableOptions.Contains(id))
            {
                return default;
            }

            selection.Add(id);
        }

        return workshopCapabilities.Where(capability => capability.Intent == CapabilityIntent.Essential || selection.Contains(capability.CapabilityId))
            .OrderBy(capability => capability.Intent)
            .ThenBy(capability => capability.CapabilityId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<Observation> SnapshotEvidence(
        IEnumerable<Observation> observations, bool synthetic, DateTimeOffset asOfUtc)
    {
        var byId = new Dictionary<EvidenceId, Observation>();
        var count = 0;
        foreach (var fact in observations)
        {
            if (++count > PolicyEvaluator.MaximumObservations || fact is null || fact.Metadata.CollectedAtUtc > asOfUtc ||
                (!synthetic && fact.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture))
            {
                return default;
            }

            var id = fact.Metadata.EvidenceId;
            if (!byId.TryAdd(id, fact) && byId[id] != fact)
            {
                return default;
            }
        }

        return byId.Values.OrderBy(fact => fact.Metadata.EvidenceId.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    private static bool MatchesSanitization(ImmutableArray<Observation> facts, SanitizationEvidence intake)
    {
        var expected = intake.SanitizationState switch
        {
            SanitizationState.Verified => "verified",
            SanitizationState.ReplacementStorage => "replacement_storage",
            SanitizationState.NoDonorStorage => "no_donor_storage",
            _ => null,
        };
        var matches = facts.Where(fact => fact.EvidenceKey.Value == "sanitization.state").Take(2).ToArray();
        return expected is not null && matches is [var current] &&
            current.Metadata == intake.Metadata && current.Unit is null && current.LimitationCode is null &&
            current.OperatorId == (intake.Metadata.EvidenceClassification == EvidenceClassification.HumanConfirmed ? intake.OperatorId : null) &&
            current.Value is { Kind: EvidenceValueKind.Enum } value && value.StringValue == expected;
    }

    private static bool PackageApplies(CatalogApplication entry, PolicyTarget target, bool synthetic, DateTimeOffset asOfUtc)
    {
        if (entry.Package.Scope != CatalogScope.Machine || entry.Package.MinimumOs != target.OperatingSystem ||
            target.Architecture != "x64" || !entry.Package.Architectures.Contains(CatalogArchitecture.X64))
        {
            return false;
        }

        return synthetic || (entry.ProductionEligible && entry.Package.ExternalArtifact &&
            entry.Review.InstallationAllowed && entry.Verification.Reviewed &&
            entry.Review.LicenseReviewedAtUtc <= asOfUtc && entry.Review.PrivacyReviewedAtUtc <= asOfUtc &&
            entry.Review.ValidUntilUtc > asOfUtc);
    }
}
