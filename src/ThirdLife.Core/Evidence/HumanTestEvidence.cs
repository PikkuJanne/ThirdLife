using System.Text.Json.Serialization;
using ThirdLife.Core.Jobs;

namespace ThirdLife.Core.Evidence;

[JsonConverter(typeof(StableStringEnumConverter<HumanTestResult>))]
public enum HumanTestResult
{
    [JsonStringEnumMemberName("pass")]
    Pass = 1,

    [JsonStringEnumMemberName("fail")]
    Fail,

    [JsonStringEnumMemberName("not_tested")]
    NotTested,

    [JsonStringEnumMemberName("not_available")]
    NotAvailable,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanTestEvidence
{
    [JsonConstructor]
    public HumanTestEvidence(
        HumanTestId humanTestId,
        JobId jobId,
        DeviceId deviceId,
        EvidenceMetadata metadata,
        HumanTestResult testResult,
        OperatorId? operatorId,
        string? limitationCode)
    {
        HumanTestId = humanTestId ?? throw new ArgumentNullException(nameof(humanTestId));
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        TestResult = DomainValue.RequireDefined(testResult, nameof(testResult));
        OperatorId = operatorId ?? throw new ArgumentNullException(
            nameof(operatorId),
            "Every human-test result requires an attributable operator.");
        LimitationCode = DomainValue.OptionalCode(limitationCode, nameof(limitationCode));

        ValidateResultAndEvidence(TestResult, Metadata, OperatorId, LimitationCode);
    }

    [JsonPropertyName("human_test_id")]
    public HumanTestId HumanTestId { get; }

    [JsonPropertyName("job_id")]
    public JobId JobId { get; }

    [JsonPropertyName("device_id")]
    public DeviceId DeviceId { get; }

    [JsonPropertyName("metadata")]
    public EvidenceMetadata Metadata { get; }

    [JsonPropertyName("test_result")]
    public HumanTestResult TestResult { get; }

    [JsonPropertyName("operator_id")]
    public OperatorId OperatorId { get; }

    [JsonPropertyName("limitation_code")]
    public string? LimitationCode { get; }

    private static void ValidateResultAndEvidence(
        HumanTestResult testResult,
        EvidenceMetadata metadata,
        OperatorId? operatorId,
        string? limitationCode)
    {
        switch (testResult)
        {
            case HumanTestResult.Pass:
                if (metadata.ValueAvailability != ValueAvailability.Available ||
                    metadata.EvidenceClassification != EvidenceClassification.HumanConfirmed)
                {
                    throw new ArgumentException(
                        "A human-test pass requires available, attributable human-confirmed evidence.");
                }

                break;
            case HumanTestResult.Fail:
                if (metadata.ValueAvailability != ValueAvailability.Available ||
                    metadata.EvidenceClassification is EvidenceClassification.Inferred or EvidenceClassification.NotAvailable)
                {
                    throw new ArgumentException("A human-test failure requires available observed or human-confirmed evidence.");
                }

                break;
            case HumanTestResult.NotTested:
                if (metadata.ValueAvailability != ValueAvailability.Unknown ||
                    metadata.EvidenceClassification != EvidenceClassification.NotAvailable)
                {
                    throw new ArgumentException("Not-tested results require unknown, not-available evidence.");
                }

                RequireLimitation(limitationCode);
                break;
            case HumanTestResult.NotAvailable:
                var unavailableIsUnknown =
                    metadata.ValueAvailability == ValueAvailability.Unknown &&
                    metadata.EvidenceClassification == EvidenceClassification.NotAvailable;
                var unavailableIsNotApplicable =
                    metadata.ValueAvailability == ValueAvailability.NotApplicable &&
                    metadata.EvidenceClassification is EvidenceClassification.Observed or
                        EvidenceClassification.HumanConfirmed;
                if (!unavailableIsUnknown && !unavailableIsNotApplicable)
                {
                    throw new ArgumentException(
                        "Not-available results require unknown unavailable evidence or attributable not-applicable evidence.");
                }

                RequireLimitation(limitationCode);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(testResult), testResult, "The human-test result is not defined.");
        }
    }

    private static void RequireLimitation(string? limitationCode)
    {
        if (limitationCode is null)
        {
            throw new ArgumentException("Unavailable and untested results require a limitation code.", nameof(limitationCode));
        }
    }
}
