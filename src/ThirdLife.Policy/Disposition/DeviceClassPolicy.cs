using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Decisions;

public static class DeviceClassPolicy
{
    private const int MaximumPolicies = 16;

    public static PolicyDocument? Select(PolicyDeviceClass selected, IEnumerable<PolicyDocument> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (!Enum.IsDefined(selected))
        {
            throw new ArgumentOutOfRangeException(nameof(selected));
        }

        var candidates = policies.Take(MaximumPolicies + 1).ToArray();
        if (candidates.Length > MaximumPolicies || candidates.Any(policy => policy is null))
        {
            throw new ArgumentException("The available device-class policies are invalid.", nameof(policies));
        }

        return candidates.SingleOrDefault(policy => policy.Target.DeviceClass == selected);
    }
}
