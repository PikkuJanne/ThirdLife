using System.Collections.ObjectModel;
using ThirdLife.Core.Evidence;

namespace ThirdLife.Inventory.Providers;

public enum ProviderPrivilegeRequirement
{
    StandardUser = 1,
    AdministratorReadOnly,
}

public enum ProviderNetworkUse
{
    None = 1,
}

public enum ProviderOperatingSystem
{
    Windows10 = 1,
    Windows11,
}

public enum ProviderEvidenceOrigin
{
    ActiveMachine = 1,
    CapturedSample,
    SyntheticFixture,
}

public sealed class InventoryProviderDescriptor
{
    public const int MaximumEvidenceKeyCount = 64;

    public static readonly TimeSpan MaximumExpectedDuration = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(10);

    public InventoryProviderDescriptor(
        ProviderId providerId,
        ProviderPrivilegeRequirement minimumPrivilege,
        TimeSpan expectedDuration,
        TimeSpan timeout,
        ProviderNetworkUse networkUse,
        ProviderEvidenceOrigin evidenceOrigin,
        IEnumerable<ProviderOperatingSystem> supportedOperatingSystems,
        ProviderFailureDefinition failureDefinition,
        IEnumerable<ProviderEvidenceDefinition> evidenceDefinitions)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        MinimumPrivilege = RequireDefined(minimumPrivilege, nameof(minimumPrivilege));
        ExpectedDuration = RequirePositiveBounded(
            expectedDuration,
            MaximumExpectedDuration,
            nameof(expectedDuration));
        Timeout = RequirePositiveBounded(timeout, MaximumTimeout, nameof(timeout));
        NetworkUse = RequireDefined(networkUse, nameof(networkUse));
        EvidenceOrigin = RequireDefined(evidenceOrigin, nameof(evidenceOrigin));
        FailureDefinition = failureDefinition ?? throw new ArgumentNullException(nameof(failureDefinition));

        if (ExpectedDuration > Timeout)
        {
            throw new ArgumentException(
                "The expected provider duration must not exceed its timeout.",
                nameof(expectedDuration));
        }

        SupportedOperatingSystems = CopyOperatingSystems(supportedOperatingSystems);
        EvidenceDefinitions = CopyEvidenceDefinitions(evidenceDefinitions);
        if (EvidenceDefinitions.Any(definition => definition.EvidenceKey == FailureDefinition.EvidenceKey))
        {
            throw new ArgumentException(
                "The provider failure key must be distinct from every value-evidence key.",
                nameof(failureDefinition));
        }
    }

    public ProviderId ProviderId { get; }

    public ProviderPrivilegeRequirement MinimumPrivilege { get; }

    public TimeSpan ExpectedDuration { get; }

    public TimeSpan Timeout { get; }

    public ProviderNetworkUse NetworkUse { get; }

    public ProviderEvidenceOrigin EvidenceOrigin { get; }

    public ProviderFailureDefinition FailureDefinition { get; }

    public ReadOnlyCollection<ProviderOperatingSystem> SupportedOperatingSystems { get; }

    public ReadOnlyCollection<ProviderEvidenceDefinition> EvidenceDefinitions { get; }

    private static ReadOnlyCollection<ProviderOperatingSystem> CopyOperatingSystems(
        IEnumerable<ProviderOperatingSystem> supportedOperatingSystems)
    {
        ArgumentNullException.ThrowIfNull(supportedOperatingSystems);

        const int maximumOperatingSystemCount = 8;
        var values = supportedOperatingSystems.Take(maximumOperatingSystemCount + 1).ToArray();
        if (values.Length is 0 or > maximumOperatingSystemCount)
        {
            throw new ArgumentException(
                $"A provider must declare between 1 and {maximumOperatingSystemCount} supported operating systems.",
                nameof(supportedOperatingSystems));
        }

        foreach (var value in values)
        {
            RequireDefined(value, nameof(supportedOperatingSystems));
        }

        if (values.Distinct().Count() != values.Length)
        {
            throw new ArgumentException(
                "A provider cannot declare a supported operating system more than once.",
                nameof(supportedOperatingSystems));
        }

        return Array.AsReadOnly(values.Order().ToArray());
    }

    private static ReadOnlyCollection<ProviderEvidenceDefinition> CopyEvidenceDefinitions(
        IEnumerable<ProviderEvidenceDefinition> evidenceDefinitions)
    {
        ArgumentNullException.ThrowIfNull(evidenceDefinitions);

        var values = evidenceDefinitions.Take(MaximumEvidenceKeyCount + 1).ToArray();
        if (values.Length is 0 or > MaximumEvidenceKeyCount)
        {
            throw new ArgumentException(
                $"A provider must declare between 1 and {MaximumEvidenceKeyCount} evidence definitions.",
                nameof(evidenceDefinitions));
        }

        if (values.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Provider evidence definitions cannot contain null.",
                nameof(evidenceDefinitions));
        }

        if (values.Select(static value => value.EvidenceKey).Distinct().Count() != values.Length)
        {
            throw new ArgumentException(
                "A provider cannot declare an evidence key more than once.",
                nameof(evidenceDefinitions));
        }

        if (values.Sum(static value => value.MaximumCardinality) + 1 > MaximumEvidenceKeyCount)
        {
            throw new ArgumentException(
                $"A provider can declare at most {MaximumEvidenceKeyCount} total observations.",
                nameof(evidenceDefinitions));
        }

        return Array.AsReadOnly(values
            .OrderBy(static value => value.EvidenceKey.Value, StringComparer.Ordinal)
            .ToArray());
    }

    private static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The enum value is not defined.");
        }

        return value;
    }

    private static TimeSpan RequirePositiveBounded(TimeSpan value, TimeSpan maximum, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The duration must be greater than zero and no greater than {maximum}.");
        }

        return value;
    }
}
