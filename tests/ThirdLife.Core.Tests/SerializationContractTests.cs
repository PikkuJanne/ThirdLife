using System.Text.Json;
using System.Text.Json.Nodes;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Jobs;
using ThirdLife.Core.Sanitization;

namespace ThirdLife.Core.Tests;

public sealed class SerializationContractTests
{
    [Fact]
    public void StrictDomainJsonRequiresEveryNonOptionalConstructorField()
    {
        var options = DomainJson.CreateStrictOptions();
        var metadata = CreateMetadata();
        var humanMetadata = CreateHumanMetadata();
        var provenance = metadata.Provenance;
        var job = new Job(
            new JobId("job-001"),
            new DeviceId("device-001"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var device = new Device(job.DeviceId);
        var observation = new Observation(
            metadata,
            new EvidenceKey("memory.installed_bytes"),
            EvidenceValue.FromInteger(8_589_934_592),
            "bytes",
            limitationCode: null);
        var humanTest = new HumanTestEvidence(
            new HumanTestId("human-test-001"),
            job.JobId,
            job.DeviceId,
            humanMetadata,
            HumanTestResult.Pass,
            new OperatorId("operator-001"),
            limitationCode: null);
        var requirement = new RequirementReference(
            new RequirementId("POL-MEM-001"),
            observation.EvidenceKey,
            RequirementType.Repairable,
            RequirementSeverity.Required);
        var sanitization = CreateVerifiedSanitization(metadata);

        AssertPropertiesRequired(provenance, options, "kind", "source_reference");
        AssertPropertiesRequired(
            metadata,
            options,
            "evidence_id",
            "classification",
            "evidence_classification",
            "provider_id",
            "collected_at_utc",
            "provenance",
            "value_availability");
        AssertPropertiesRequired(job, options, "job_id", "device_id", "created_at_utc");
        AssertPropertiesRequired(device, options, "device_id");
        AssertPropertiesRequired(
            observation,
            options,
            "metadata",
            "evidence_key",
            "value",
            "unit",
            "limitation_code");
        AssertPropertiesRequired(
            humanTest,
            options,
            "human_test_id",
            "job_id",
            "device_id",
            "metadata",
            "test_result",
            "operator_id",
            "limitation_code");
        AssertPropertiesRequired(
            requirement,
            options,
            "requirement_id",
            "evidence_key",
            "requirement_type",
            "severity");
        AssertPropertiesRequired(
            sanitization,
            options,
            "metadata",
            "sanitization_state",
            "method_code",
            "operator_id",
            "occurred_at_utc",
            "media_identifier",
            "verification_state",
            "policy_version");
    }

    [Fact]
    public void GovernedEnumsCannotDefaultWhenUsingSerializerDefaults()
    {
        var metadata = CreateMetadata();
        var humanMetadata = CreateHumanMetadata();
        var humanTest = new HumanTestEvidence(
            new HumanTestId("human-test-001"),
            new JobId("job-001"),
            new DeviceId("device-001"),
            humanMetadata,
            HumanTestResult.Pass,
            new OperatorId("operator-001"),
            limitationCode: null);
        var sanitization = CreateVerifiedSanitization(metadata);
        var requirement = new RequirementReference(
            new RequirementId("POL-MEM-001"),
            new EvidenceKey("memory.installed_bytes"),
            RequirementType.Repairable,
            RequirementSeverity.Required);

        AssertRejectedAfterRemoving(metadata, "classification");
        AssertRejectedAfterRemoving(metadata, "evidence_classification");
        AssertRejectedAfterRemoving(metadata, "value_availability");
        AssertRejectedAfterRemoving(metadata.Provenance, "kind");
        AssertRejectedAfterRemoving(humanTest, "test_result");
        AssertRejectedAfterRemoving(sanitization, "sanitization_state");
        AssertRejectedAfterRemoving(sanitization, "verification_state");
        AssertRejectedAfterRemoving(requirement, "requirement_type");
        AssertRejectedAfterRemoving(requirement, "severity");
    }

    [Fact]
    public void StrictDomainJsonRejectsUnknownAndDuplicateProperties()
    {
        var options = DomainJson.CreateStrictOptions();
        var job = new Job(
            new JobId("job-001"),
            new DeviceId("device-001"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var json = JsonSerializer.Serialize(job, options);
        var unknown = json.Replace("{", "{\"unexpected\":true,", StringComparison.Ordinal);
        var duplicate = json.Replace(
            "\"job_id\":\"job-001\"",
            "\"job_id\":\"job-other\",\"job_id\":\"job-001\"",
            StringComparison.Ordinal);

        Assert.False(options.AllowDuplicateProperties);
        Assert.True(options.RespectRequiredConstructorParameters);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Job>(unknown, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Job>(duplicate, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Job>(unknown));
    }

    [Fact]
    public void ExplicitNullOptionalFieldsHaveStableStrictRoundTrips()
    {
        var options = DomainJson.CreateStrictOptions();
        var metadata = new EvidenceMetadata(
            new EvidenceId("evidence-sanitization-unknown"),
            PrivacyClassification.WorkshopRestricted,
            EvidenceClassification.NotAvailable,
            new ProviderId("sanitization-provider"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new EvidenceProvenance(ProvenanceKind.ImportedRecord, "record-unknown"),
            ValueAvailability.Unknown);
        var expected = new SanitizationEvidence(
            metadata,
            SanitizationState.Unknown,
            "not_available",
            operatorId: null,
            occurredAtUtc: null,
            mediaIdentifier: null,
            SanitizationVerificationState.NotAvailable,
            "community-policy@1.0.0");

        var json = JsonSerializer.Serialize(expected, options);
        var actual = JsonSerializer.Deserialize<SanitizationEvidence>(json, options);

        Assert.Contains("\"operator_id\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"occurred_at_utc\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"media_identifier\":null", json, StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    private static EvidenceMetadata CreateMetadata() =>
        new(
            new EvidenceId("evidence-001"),
            PrivacyClassification.WorkshopRestricted,
            EvidenceClassification.Observed,
            new ProviderId("provider-001"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new EvidenceProvenance(ProvenanceKind.ProviderObservation, "provider-run-001"),
            ValueAvailability.Available);

    private static EvidenceMetadata CreateHumanMetadata() =>
        new(
            new EvidenceId("evidence-human-001"),
            PrivacyClassification.WorkshopRestricted,
            EvidenceClassification.HumanConfirmed,
            new ProviderId("human-test-provider"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new EvidenceProvenance(ProvenanceKind.HumanConfirmation, "human-test-run-001"),
            ValueAvailability.Available);

    private static SanitizationEvidence CreateVerifiedSanitization(EvidenceMetadata metadata) =>
        new(
            metadata,
            SanitizationState.Verified,
            "external_sanitization",
            new OperatorId("operator-001"),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new MediaIdentifier("SYNTHETIC-MEDIA-001"),
            SanitizationVerificationState.Verified,
            "community-policy@1.0.0");

    private static void AssertPropertiesRequired<TValue>(
        TValue value,
        JsonSerializerOptions options,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var json = RemoveProperty(value, propertyName, options);

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TValue>(json, options));
        }
    }

    private static void AssertRejectedAfterRemoving<TValue>(TValue value, string propertyName)
    {
        var json = RemoveProperty(value, propertyName, options: null);
        var exception = Record.Exception(() =>
        {
            _ = JsonSerializer.Deserialize<TValue>(json);
        });

        Assert.NotNull(exception);
        Assert.True(
            exception is JsonException or ArgumentException,
            $"Expected a JSON or domain validation exception, but received {exception.GetType().Name}.");
    }

    private static string RemoveProperty<TValue>(
        TValue value,
        string propertyName,
        JsonSerializerOptions? options)
    {
        var json = JsonSerializer.Serialize(value, options);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("The serialized test value must be a JSON object.");

        Assert.True(root.Remove(propertyName), $"The serialized value did not contain '{propertyName}'.");
        return root.ToJsonString();
    }
}
