using System.Globalization;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.HumanConfirmation;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;
using ThirdLife.Inventory;

namespace ThirdLife.UI.Features.Disposition;

internal static class AuditPolicyEvidence
{
    private const string SecurityProvider = "windows-security-baseline";
    private static readonly EvidenceKey TimeSyncRecency = new("security.time_sync_recency");
    private const string OsProvider = "windows-operating-system";
    private const string IdentitySource = "os.windows.identity";
    private const string LifecycleSource = "os.lifecycle.catalog";
    private const string SystemProvider = "windows-system-inventory";
    private const string FirmwareProvider = "windows-firmware-security";
    private const string BatteryProvider = "windows-battery-report";
    private const string Missing = "provider_value_missing";
    private const string Malformed = "provider_value_malformed";
    private const string Conflict = "provider_values_conflict";
    private const string Stale = "provider_value_stale";

    public static IReadOnlyList<Observation> Create(
        StoredJob job, AssessmentSnapshot assessment, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(assessment);
        var gate = SanitizationGate.Inspect(job);
        if (job.Job.JobId != assessment.JobId || !gate.AllowsAssessment ||
            gate.Decision!.DecisionId != assessment.GateDecisionId ||
            assessment.Outcome is AssessmentOutcome.Incomplete or AssessmentOutcome.Cancelled ||
            evaluatedAtUtc == default || assessment.FinishedAtUtc > evaluatedAtUtc ||
            gate.Decision.EvaluatedAtUtc > assessment.StartedAtUtc)
        {
            throw new InvalidOperationException("A current assessment under the saved sanitization gate is required.");
        }

        var intake = job.SanitizationEvidence.Single(evidence => evidence.Metadata.EvidenceId == gate.EvidenceId);
        var observations = assessment.Observations.ToLookup(fact => fact.EvidenceKey.Value, StringComparer.Ordinal);
        var familyIssue = GetFamilyIssue(observations);
        var lifecycleIssue = StrongestIssue(familyIssue, GetLifecycleIssue(observations, evaluatedAtUtc));
        var architectureIssue = GetArchitectureIssue(observations);
        var projected = new List<Observation> { ProjectSanitization(intake) };
        foreach (var fact in assessment.Observations)
        {
            if (Project(fact, familyIssue, lifecycleIssue, architectureIssue, evaluatedAtUtc) is { } selected)
            {
                if ((fact.EvidenceKey.Value == "operating_system.release" && observations.Contains("os.family")) ||
                    (fact.EvidenceKey.Value == "operating_system.support_state" && observations.Contains("os.support_target")))
                {
                    selected = WithLimit(fact, fact.EvidenceKey, Conflict);
                }

                if (selected.Metadata.CollectedAtUtc > evaluatedAtUtc ||
                    observations[fact.EvidenceKey.Value].Skip(1).Any())
                {
                    selected = WithLimit(selected, selected.EvidenceKey, Conflict);
                }

                projected.Add(selected);
            }
        }

        projected.AddRange(HumanConfirmationProjection.FromJob(job, assessment.AssessmentId.Value)
            .Select(fact => fact.Metadata.CollectedAtUtc > evaluatedAtUtc ? WithLimit(fact, fact.EvidenceKey, Conflict) : fact));
        var repeated = projected.GroupBy(fact => fact.EvidenceKey)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet();

        // Cite both sources of an overlap and let neither become a passing fact.
        return Array.AsReadOnly(projected.Select(fact => repeated.Contains(fact.EvidenceKey)
            ? WithLimit(fact, fact.EvidenceKey, Conflict)
            : fact).ToArray());
    }

    private static Observation ProjectSanitization(SanitizationEvidence intake)
    {
        var token = intake.SanitizationState switch
        {
            SanitizationState.Verified => "verified",
            SanitizationState.ReplacementStorage => "replacement_storage",
            SanitizationState.NoDonorStorage => "no_donor_storage",
            _ => throw new InvalidOperationException("The assessment has no verified sanitization evidence."),
        };
        return new Observation(intake.Metadata, new EvidenceKey("sanitization.state"), EvidenceValue.FromEnum(token), unit: null,
            limitationCode: null,
            intake.Metadata.EvidenceClassification == EvidenceClassification.HumanConfirmed ? intake.OperatorId : null);
    }

    private static Observation? Project(Observation fact, string? familyIssue, string? lifecycleIssue,
        string? architectureIssue, DateTimeOffset evaluatedAtUtc) =>
        fact.EvidenceKey.Value switch
        {
            "os.family" when IsOsFamily(fact) =>
                WithLimit(fact, new EvidenceKey("operating_system.release"), familyIssue),
            "os.support_target" when IsLifecycle(fact) =>
                WithLimit(fact, new EvidenceKey("operating_system.support_state"),
                    StrongestIssue(lifecycleIssue, fact.Value?.StringValue == "supported" ? architectureIssue : null)),
            "os.lifecycle.source" or "os.lifecycle.revision" or "os.lifecycle.as_of" or
                "os.lifecycle.fresh_through" or "os.lifecycle.evaluated_on" or "os.lifecycle.release" or "os.lifecycle.reason"
                when IsLifecycle(fact) => fact,
            "os.architecture" when IsOwned(fact, OsProvider, "os.dotnet.os_architecture") => fact,
            "system.architecture" when IsSystemArchitecture(fact) => WithLimit(fact, fact.EvidenceKey, architectureIssue),
            "security.last_successful_sync_utc" when IsOwned(fact, SecurityProvider, "security.w32time.last_successful_sync") =>
                ProjectTimeSync(fact, evaluatedAtUtc),
            "security.firewall_health" when IsOwned(fact, SecurityProvider, "security.wsc.firewall") =>
                ProjectSecurityEnum(fact, EvidenceClassification.Observed),
            "security.antivirus_health" when IsOwned(fact, SecurityProvider, "security.wsc.antivirus") =>
                ProjectSecurityEnum(fact, EvidenceClassification.Observed),
            "security.defender_health" when IsOwned(fact, SecurityProvider, "security.defender.computer_status") =>
                ProjectSecurityEnum(fact, EvidenceClassification.Observed),
            "security.uac_state" when IsOwned(fact, SecurityProvider, "security.uac_configuration_and_health") =>
                ProjectSecurityEnum(fact, EvidenceClassification.Inferred),
            "ownership.lock_state" when IsOwned(fact, SecurityProvider, "security.management_indicators") &&
                (fact.Value is null or { Kind: EvidenceValueKind.Enum, StringValue: "management_indicated" }) => fact,
            "reboot.pending_status" when IsOwned(fact, "windows-pending-reboot", "pending_reboot.partial_indicator_union") &&
                (fact.Value is null or { Kind: EvidenceValueKind.Enum, StringValue: "pending_indicated" }) => fact,
            _ when IsTrustedDirectFact(fact) => fact,
            _ => null,
        };

    private static Observation ProjectSecurityEnum(Observation fact, EvidenceClassification expectedClass) =>
        fact.Value is null || (fact.Value.Kind == EvidenceValueKind.Enum && fact.Metadata.EvidenceClassification == expectedClass)
            ? fact : WithLimit(fact, fact.EvidenceKey, Malformed);

    private static Observation ProjectTimeSync(Observation fact, DateTimeOffset evaluatedAtUtc)
    {
        var metadata = fact.Metadata;
        if (fact.LimitationCode is not null || fact.Value is null)
        {
            return WithLimit(fact, TimeSyncRecency, fact.LimitationCode ?? Missing);
        }

        if (fact.Unit != "unix_seconds" || metadata.EvidenceClassification != EvidenceClassification.Observed ||
            fact.Value is not { Kind: EvidenceValueKind.WholeNumber, IntegerValue: > 0 and var seconds } ||
            seconds > metadata.CollectedAtUtc.ToUnixTimeSeconds() || metadata.CollectedAtUtc > evaluatedAtUtc)
        {
            return WithLimit(fact, TimeSyncRecency, Malformed);
        }

        var age = evaluatedAtUtc - DateTimeOffset.FromUnixTimeSeconds(seconds);
        var inferred = new EvidenceMetadata(metadata.EvidenceId, metadata.PrivacyClassification, EvidenceClassification.Inferred,
            metadata.ProviderId, metadata.CollectedAtUtc, metadata.Provenance, metadata.ValueAvailability);
        return new Observation(inferred, TimeSyncRecency,
            EvidenceValue.FromEnum(age <= TimeSpan.FromDays(7) ? "within_seven_days" : "older_than_seven_days"),
            unit: null, limitationCode: null);
    }

    private static bool IsTrustedDirectFact(Observation fact)
    {
        if (fact.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture)
        {
            return fact.EvidenceKey.Value is
                "operating_system.release" or "operating_system.support_state" or "processor.windows_11_eligibility" or
                "memory.installed_bytes" or "storage.system.capacity_bytes" or "storage.system.free_bytes" or
                "storage.system.health_state" or "battery.present" or "battery.swelling_inspection" or
                "battery.full_charge_ratio_percent" or "firmware.mode" or "secure_boot.capable" or "secure_boot.enabled" or
                "tpm.present" or "tpm.version" or "tpm.ready" or "activation.state" or
                "function.display" or "function.keyboard" or "function.pointing" or "function.network" or
                "function.audio_output" or "function.camera" or "function.microphone";
        }

        return fact.EvidenceKey.Value switch
        {
            "memory.installed_bytes" => IsOwned(fact, SystemProvider, "system_inventory.kernel32.installed_memory"),
            "battery.present" => IsOwned(fact, BatteryProvider, "battery.powercfg.report") ||
                IsOwned(fact, BatteryProvider, "battery.kernel32.system_power_status"),
            "firmware.mode" => IsOwned(fact, FirmwareProvider, "firmware.get_firmware_type"),
            "secure_boot.capable" => IsOwned(fact, FirmwareProvider, "secure_boot.device_guard.available_properties"),
            "secure_boot.enabled" => IsOwned(fact, FirmwareProvider, "secure_boot.registry.enabled"),
            "tpm.present" or "tpm.version" => IsOwned(fact, FirmwareProvider, "tpm.tbs.device_info"),
            "tpm.ready" => IsOwned(fact, FirmwareProvider, "tpm.wmi.is_ready_information"),
            "activation.state" => IsOwned(fact, "windows-activation", "activation.slc.windows_application_status"),
            _ => false,
        };
    }

    private static string? GetArchitectureIssue(ILookup<string, Observation> observations)
    {
        var (system, systemIssue) = ReadScoped(observations["system.architecture"], IsSystemArchitecture);
        if (system?.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture &&
            !observations.Contains("os.family") && !observations.Contains("os.support_target") && !observations.Contains("os.architecture"))
        {
            return systemIssue;
        }

        var (os, osIssue) = ReadScoped(observations["os.architecture"],
            static fact => IsOwned(fact, OsProvider, "os.dotnet.os_architecture"));
        var issue = StrongestIssue(systemIssue, osIssue);
        if (issue is not null)
        {
            return issue;
        }

        if (system!.Value is not { Kind: EvidenceValueKind.Enum, StringValue: { } systemToken } ||
            os!.Value is not { Kind: EvidenceValueKind.Enum, StringValue: { } osToken })
        {
            return Malformed;
        }

        if (systemToken != osToken)
        {
            return Conflict;
        }

        return osToken != "x64" && observations["os.support_target"].Any(fact =>
            IsLifecycle(fact) && fact.LimitationCode is null && fact.Value?.StringValue == "supported")
            ? Conflict : null;
    }

    private static string? GetLifecycleIssue(ILookup<string, Observation> observations, DateTimeOffset evaluatedAtUtc)
    {
        var (family, familyIssue) = ReadScoped(observations["os.family"], IsOsFamily);
        var (target, targetIssue) = ReadScoped(observations["os.support_target"], IsLifecycle);
        var (source, sourceIssue) = ReadScoped(observations["os.lifecycle.source"], IsLifecycle);
        var (revision, revisionIssue) = ReadScoped(observations["os.lifecycle.revision"], IsLifecycle);
        var (asOf, asOfIssue) = ReadScoped(observations["os.lifecycle.as_of"], IsLifecycle);
        var (freshThrough, freshIssue) = ReadScoped(observations["os.lifecycle.fresh_through"], IsLifecycle);
        var (collectedOn, collectedIssue) = ReadScoped(observations["os.lifecycle.evaluated_on"], IsLifecycle);
        var issue = StrongestIssue(familyIssue, targetIssue, sourceIssue, revisionIssue, asOfIssue, freshIssue, collectedIssue);
        if (issue is not null)
        {
            return issue;
        }

        if (family!.Value is not { Kind: EvidenceValueKind.Enum } || target!.Value is not { Kind: EvidenceValueKind.Enum } ||
            source!.Value is not { Kind: EvidenceValueKind.Text, StringValue: "microsoft.windows-client.release-health" } ||
            revision!.Value is not { Kind: EvidenceValueKind.Text, StringValue: { } revisionValue } ||
            !ReadDate(asOf!, out var first) || !ReadDate(freshThrough!, out var last) || !ReadDate(collectedOn!, out var collected) ||
            !HasScopedRevision(revisionValue, first))
        {
            return Malformed;
        }

        var evaluationDate = DateOnly.FromDateTime(evaluatedAtUtc.UtcDateTime);
        if (collected < first || last < first || evaluationDate < collected || family.Metadata.CollectedAtUtc > evaluatedAtUtc)
        {
            return Malformed;
        }

        if (target.Value.StringValue == "audit_only")
        {
            return "legacy_windows_10_audit";
        }

        if (target.Value.StringValue is not ("supported" or "unsupported"))
        {
            return Malformed;
        }

        if (!new[] { source, revision, asOf!, freshThrough!, collectedOn! }.All(fact =>
                fact.Metadata.Provenance == target.Metadata.Provenance && SameCollection(fact, target)))
        {
            return Conflict;
        }

        return evaluationDate > last ? Stale : null;
    }

    private static string? GetFamilyIssue(ILookup<string, Observation> observations)
    {
        var (family, issue) = ReadScoped(observations["os.family"], IsOsFamily);
        if (issue is not null)
        {
            return issue;
        }

        if (family!.Value is not { Kind: EvidenceValueKind.Enum, StringValue: { } token } ||
            token is not ("windows_11" or "windows_10" or "windows_server" or "windows_other"))
        {
            return Malformed;
        }

        var (target, targetIssue) = ReadScoped(observations["os.support_target"], IsLifecycle);
        if (targetIssue == Conflict ||
            (target is not null && targetIssue is null && (!SameCollection(target, family) ||
                (token != "windows_11" && target.Value?.StringValue == "supported") ||
                (token != "windows_10" && target.Value?.StringValue == "audit_only"))))
        {
            return Conflict;
        }

        return null;
    }

    private static (Observation? Fact, string? Issue) ReadScoped(
        IEnumerable<Observation> facts, Func<Observation, bool> isOwned)
    {
        var values = facts.Take(2).ToArray();
        if (values.Length == 0)
        {
            return (null, Missing);
        }

        var fact = values[0];
        return values.Length > 1 || !isOwned(fact)
            ? (fact, Conflict)
            : (fact, fact.LimitationCode ?? (fact.Value is null ? Missing : null));
    }

    private static bool ReadDate(Observation fact, out DateOnly date) =>
        DateOnly.TryParseExact(fact.Value is { Kind: EvidenceValueKind.Text } value ? value.StringValue : null,
            "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool HasScopedRevision(string revision, DateOnly asOf)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"thirdlife.windows-lifecycle.{asOf:yyyy-MM-dd}.v");
        return revision.StartsWith(prefix, StringComparison.Ordinal) && revision.Length > prefix.Length &&
            revision[prefix.Length] != '0' &&
            int.TryParse(revision.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var version) &&
            version > 0;
    }

    private static bool IsLifecycle(Observation fact) => IsOwned(fact, OsProvider, LifecycleSource);

    private static bool IsOsFamily(Observation fact) => IsOwned(fact, OsProvider, IdentitySource) || IsLifecycle(fact);

    private static bool SameCollection(Observation left, Observation right) =>
        left.Metadata.ProviderId == right.Metadata.ProviderId &&
        left.Metadata.Provenance.Kind == right.Metadata.Provenance.Kind &&
        left.Metadata.CollectedAtUtc == right.Metadata.CollectedAtUtc;

    private static bool IsSystemArchitecture(Observation fact) =>
        fact.Metadata.Provenance.Kind == ProvenanceKind.SyntheticFixture ||
        IsOwned(fact, SystemProvider, "system_inventory.dotnet.os_architecture");

    private static bool IsOwned(Observation fact, string provider, string source) =>
        fact.Metadata.ProviderId.Value == provider && fact.Metadata.Provenance.SourceReference == source &&
        fact.Metadata.Provenance.Kind is ProvenanceKind.ProviderObservation or ProvenanceKind.SyntheticFixture;

    private static string? StrongestIssue(params string?[] issues) =>
        issues.Contains(Conflict, StringComparer.Ordinal) ? Conflict :
        issues.Contains(Stale, StringComparer.Ordinal) ? Stale :
        issues.Contains(Malformed, StringComparer.Ordinal) ? Malformed :
        issues.FirstOrDefault(issue => issue is not null && issue != Missing) ??
        (issues.Contains(Missing, StringComparer.Ordinal) ? Missing : null);

    private static Observation WithLimit(Observation fact, EvidenceKey key, string? issue) =>
        key == fact.EvidenceKey && issue is null
            ? fact
            : new Observation(fact.Metadata, key, fact.Value, fact.Unit,
                issue == Missing ? fact.LimitationCode ?? issue : issue ?? fact.LimitationCode, fact.OperatorId);
}
