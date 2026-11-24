using ThirdLife.Policy.Schema;

namespace ThirdLife.Policy.Versioning;

public static class PolicyVersionOrder
{
    public static int Compare(string left, string right)
    {
        var leftVersion = SplitVersion(left, nameof(left));
        var rightVersion = SplitVersion(right, nameof(right));
        var leftCore = leftVersion[0].Split('.');
        var rightCore = rightVersion[0].Split('.');
        for (var index = 0; index < leftCore.Length; index++)
        {
            var comparison = CompareNumeric(leftCore[index], rightCore[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (leftVersion.Length != 2 || rightVersion.Length != 2)
        {
            // A release sorts above any prerelease with the same version core.
            return rightVersion.Length.CompareTo(leftVersion.Length);
        }

        var leftPrerelease = leftVersion[1].Split('.');
        var rightPrerelease = rightVersion[1].Split('.');
        for (var index = 0; index < Math.Min(leftPrerelease.Length, rightPrerelease.Length); index++)
        {
            var comparison = CompareIdentifiers(leftPrerelease[index], rightPrerelease[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftPrerelease.Length.CompareTo(rightPrerelease.Length);
    }

    private static string[] SplitVersion(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (!PolicySchema.IsPolicyVersion(value))
        {
            throw new ArgumentException("The policy version must have a supported semantic version.", parameter);
        }

        return value.Split('-', 2);
    }

    private static int CompareIdentifiers(string left, string right)
    {
        var leftIsNumeric = left.All(char.IsAsciiDigit);
        var rightIsNumeric = right.All(char.IsAsciiDigit);
        if (leftIsNumeric != rightIsNumeric)
        {
            return leftIsNumeric ? -1 : 1;
        }

        return leftIsNumeric ? CompareNumeric(left, right) : string.CompareOrdinal(left, right);
    }

    private static int CompareNumeric(string left, string right)
    {
        var lengthOrder = left.Length.CompareTo(right.Length);
        return lengthOrder != 0 ? lengthOrder : string.CompareOrdinal(left, right);
    }
}
