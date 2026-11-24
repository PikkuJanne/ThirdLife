using ThirdLife.Core.Evidence;
using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Tests;

internal static class PolicyEvaluationFixture
{
    private static readonly DateTimeOffset CollectedAtUtc = new(2030, 1, 1, 8, 4, 0, TimeSpan.Zero);

    public static byte[] CandidatePolicyJson() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "community-laptop-policy.json"));

    public static PolicyDocument CandidatePolicy() => PolicySchema.Parse(CandidatePolicyJson());

    // Values mirror the independently authored assessment-ready fixture, not the policy's expectations.
    public static Observation[] ReadyObservations() =>
    [
        Fact("SYNTHETIC-SANITIZATION-READY-001", "sanitization.state", EvidenceValue.FromEnum("verified")),
        EnumFact("001", "operating_system.release", "windows_11"),
        EnumFact("002", "operating_system.support_state", "supported", EvidenceClassification.Inferred),
        EnumFact("003", "system.architecture", "x64"),
        EnumFact("004", "processor.windows_11_eligibility", "eligible", EvidenceClassification.Inferred),
        Fact("SYNTHETIC-EVIDENCE-READY-005", "memory.installed_bytes", EvidenceValue.FromInteger(17_179_869_184), unit: "bytes"),
        Fact("SYNTHETIC-EVIDENCE-READY-006", "storage.system.capacity_bytes", EvidenceValue.FromInteger(274_877_906_944), unit: "bytes"),
        Fact("SYNTHETIC-EVIDENCE-READY-007", "storage.system.free_bytes", EvidenceValue.FromInteger(137_438_953_472), unit: "bytes"),
        EnumFact("008", "storage.system.health_state", "healthy"),
        Fact("SYNTHETIC-EVIDENCE-READY-009", "battery.present", EvidenceValue.FromBoolean(true)),
        EnumFact("010", "battery.swelling_inspection", "no_swelling_observed", EvidenceClassification.HumanConfirmed),
        Fact("SYNTHETIC-EVIDENCE-READY-011", "battery.full_charge_ratio_percent", EvidenceValue.FromInteger(82), EvidenceClassification.Inferred, "percent"),
        EnumFact("012", "firmware.mode", "uefi"),
        Fact("SYNTHETIC-EVIDENCE-READY-013", "secure_boot.capable", EvidenceValue.FromBoolean(true)),
        Fact("SYNTHETIC-EVIDENCE-READY-014", "secure_boot.enabled", EvidenceValue.FromBoolean(true)),
        Fact("SYNTHETIC-EVIDENCE-READY-015", "tpm.present", EvidenceValue.FromBoolean(true)),
        Fact("SYNTHETIC-EVIDENCE-READY-016", "tpm.version", EvidenceValue.FromString("2.0")),
        Fact("SYNTHETIC-EVIDENCE-READY-017", "tpm.ready", EvidenceValue.FromBoolean(true)),
        EnumFact("018", "ownership.lock_state", "clear"),
        EnumFact("019", "activation.state", "activated"),
        EnumFact("020", "function.display", "passed", EvidenceClassification.HumanConfirmed),
        EnumFact("021", "function.keyboard", "passed", EvidenceClassification.HumanConfirmed),
        EnumFact("022", "function.pointing", "passed", EvidenceClassification.HumanConfirmed),
        EnumFact("023", "function.network", "passed", EvidenceClassification.HumanConfirmed),
        EnumFact("024", "function.audio_output", "passed", EvidenceClassification.HumanConfirmed),
        EnumFact("025", "function.camera", "passed", EvidenceClassification.HumanConfirmed),
        EnumFact("026", "function.microphone", "passed", EvidenceClassification.HumanConfirmed),
    ];

    public static string[] ActiveCapabilities() => ["web_browsing", "document_editing", "pdf_reading", "video_calling"];

    public static Observation Fact(
        string id,
        string key,
        EvidenceValue? value,
        EvidenceClassification classification = EvidenceClassification.Observed,
        string? unit = null,
        ValueAvailability availability = ValueAvailability.Available,
        string? limitation = null)
    {
        var human = classification == EvidenceClassification.HumanConfirmed;
        return new Observation(
            new EvidenceMetadata(
                new EvidenceId(id),
                PrivacyClassification.WorkshopRestricted,
                classification,
                new ProviderId("synthetic-policy-fixture"),
                CollectedAtUtc,
                new EvidenceProvenance(
                    human ? ProvenanceKind.HumanConfirmation : ProvenanceKind.SyntheticFixture,
                    "synthetic-policy-fixture"),
                availability),
            new EvidenceKey(key),
            value,
            unit,
            limitation,
            human ? new OperatorId("synthetic-operator") : null);
    }

    private static Observation EnumFact(
        string idSuffix, string key, string value, EvidenceClassification classification = EvidenceClassification.Observed) =>
        Fact("SYNTHETIC-EVIDENCE-READY-" + idSuffix, key, EvidenceValue.FromEnum(value), classification);
}
