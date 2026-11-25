namespace ThirdLife.Catalog.Schema;

internal static class CatalogVersionOrder
{
    // Only called with versions that have passed the catalog SemVer grammar.
    public static int Compare(string left, string right)
    {
        var leftParts = left.Split('-', 2);
        var rightParts = right.Split('-', 2);
        var leftCore = leftParts[0].Split('.');
        var rightCore = rightParts[0].Split('.');
        for (var index = 0; index < leftCore.Length; index++)
        {
            var order = CompareNumeric(leftCore[index], rightCore[index]);
            if (order != 0)
            {
                return order;
            }
        }

        if (leftParts.Length != 2 || rightParts.Length != 2)
        {
            // A release sorts above a prerelease with the same numeric core.
            return rightParts.Length.CompareTo(leftParts.Length);
        }

        var leftPrerelease = leftParts[1].Split('.');
        var rightPrerelease = rightParts[1].Split('.');
        for (var index = 0; index < Math.Min(leftPrerelease.Length, rightPrerelease.Length); index++)
        {
            var order = ComparePrerelease(leftPrerelease[index], rightPrerelease[index]);
            if (order != 0)
            {
                return order;
            }
        }

        return leftPrerelease.Length.CompareTo(rightPrerelease.Length);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return leftNumeric ? CompareNumeric(left, right) : string.CompareOrdinal(left, right);
    }

    private static int CompareNumeric(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length == 0 ? string.CompareOrdinal(left, right) : length;
    }
}
