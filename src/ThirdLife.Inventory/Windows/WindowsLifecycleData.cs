namespace ThirdLife.Inventory.Windows;

internal enum WindowsLifecycleEditionGroup
{
    HomeAndPro = 1,
    EnterpriseAndEducation,
}

internal sealed record WindowsLifecycleEntry(
    uint Build,
    WindowsLifecycleEditionGroup EditionGroup,
    string Family,
    string Release,
    DateOnly? Released,
    DateOnly? SupportEnds);

// Reviewed offline data. Source URLs, edition mappings, date semantics, and refresh
// requirements are recorded in docs/inventory/windows-lifecycle-data.md.
internal static class WindowsLifecycleData
{
    internal const string SourceId = "microsoft.windows-client.release-health";
    internal const string Revision = "thirdlife.windows-lifecycle.2026-11-25.v1";
    internal const uint DeviceSpecificWindows11Build = 28000;
    internal static readonly DateOnly AsOf = new(2026, 11, 25);

    internal static IReadOnlyList<WindowsLifecycleEntry> CreateEntries()
    {
        var entries = new List<WindowsLifecycleEntry>();
        AddWindows11(entries, 22000, "21H2", new(2021, 10, 4), new(2023, 10, 10), new(2024, 10, 8));
        AddWindows11(entries, 22621, "22H2", new(2022, 9, 20), new(2024, 10, 8), new(2025, 10, 14));
        AddWindows11(entries, 22631, "23H2", new(2023, 10, 31), new(2025, 11, 11), new(2026, 11, 10));
        AddWindows11(entries, 26100, "24H2", new(2024, 10, 1), new(2026, 10, 13), new(2027, 10, 12));
        AddWindows11(entries, 26200, "25H2", new(2025, 9, 30), new(2027, 10, 12), new(2028, 10, 10));
        AddWindows11(entries, 26300, "26H2", new(2026, 9, 29), new(2028, 10, 10), new(2029, 10, 9));

        return entries.AsReadOnly();
    }

    private static void AddWindows11(
        List<WindowsLifecycleEntry> entries,
        uint build,
        string release,
        DateOnly released,
        DateOnly homeAndProEnd,
        DateOnly enterpriseAndEducationEnd)
    {
        entries.Add(new(build, WindowsLifecycleEditionGroup.HomeAndPro, "windows_11", release, released, homeAndProEnd));
        entries.Add(new(build, WindowsLifecycleEditionGroup.EnterpriseAndEducation, "windows_11", release, released, enterpriseAndEducationEnd));
    }
}
