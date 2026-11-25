namespace ThirdLife.Inventory.Windows;

internal enum WindowsLifecycleStatus
{
    Unknown = 0,
    Supported,
    Unsupported,
}

internal enum WindowsLifecycleLimitation
{
    None = 0,
    MissingData,
    UnrecognizedData,
    FutureData,
    StaleData,
    ContradictoryData,
    InvalidData,
    UnrecognizedBuild,
    UnrecognizedEdition,
    UnsupportedArchitecture,
    UnknownArchitecture,
    NotYetReleased,
    SupportEnded,
    UnsupportedOperatingSystem,
    UnverifiedIdentity,
    UnreviewedReleaseCoverage,
}

internal sealed record WindowsLifecycleResult(
    WindowsLifecycleStatus Status,
    WindowsLifecycleLimitation Limitation,
    string? SourceId,
    string? Revision,
    DateOnly? AsOf,
    DateOnly? FreshThrough,
    string? Release,
    string? Family);

internal sealed class WindowsLifecycleCatalog
{
    internal const int MaximumAgeDays = 30;
    internal const int MaximumEntries = 64;
    private readonly WindowsLifecycleEntry[] _entries;
    private readonly WindowsLifecycleLimitation _dataLimitation;
    private readonly bool _hasRecognizedMetadata;

    internal static WindowsLifecycleCatalog Current { get; } = new(
        WindowsLifecycleData.SourceId,
        WindowsLifecycleData.Revision,
        WindowsLifecycleData.AsOf,
        WindowsLifecycleData.CreateEntries());

    // Internal injection is for deterministic failure tests; there is no runtime
    // file, network, user preference, or policy input that can replace this data.
    internal WindowsLifecycleCatalog(
        string? sourceId,
        string? revision,
        DateOnly? asOf,
        IReadOnlyList<WindowsLifecycleEntry>? entries)
    {
        _hasRecognizedMetadata =
            string.Equals(sourceId, WindowsLifecycleData.SourceId, StringComparison.Ordinal) &&
            string.Equals(revision, WindowsLifecycleData.Revision, StringComparison.Ordinal) &&
            asOf == WindowsLifecycleData.AsOf;

        _dataLimitation = string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(revision) || asOf is null
            ? WindowsLifecycleLimitation.MissingData
            : !_hasRecognizedMetadata
                ? WindowsLifecycleLimitation.UnrecognizedData
                : WindowsLifecycleLimitation.None;

        if (entries is null || entries.Count == 0)
        {
            _entries = [];
            _dataLimitation = WindowsLifecycleLimitation.MissingData;
            _hasRecognizedMetadata = false;
        }
        else if (entries.Count > MaximumEntries)
        {
            _entries = [];
            _dataLimitation = WindowsLifecycleLimitation.InvalidData;
        }
        else
        {
            _entries = entries.ToArray();
            if (_dataLimitation == WindowsLifecycleLimitation.None)
            {
                _dataLimitation = ValidateEntries(_entries);
            }
        }
    }

    internal WindowsLifecycleResult Evaluate(
        uint build,
        uint edition,
        string? architecture,
        DateOnly evaluationDate)
    {
        if (_dataLimitation != WindowsLifecycleLimitation.None)
        {
            return Result(WindowsLifecycleStatus.Unknown, _dataLimitation);
        }

        if (evaluationDate < WindowsLifecycleData.AsOf)
        {
            return Result(WindowsLifecycleStatus.Unknown, WindowsLifecycleLimitation.FutureData);
        }

        if (evaluationDate.DayNumber - WindowsLifecycleData.AsOf.DayNumber > MaximumAgeDays)
        {
            return Result(WindowsLifecycleStatus.Unknown, WindowsLifecycleLimitation.StaleData);
        }

        var buildEntries = _entries.Where(entry => entry.Build == build).ToArray();
        if (buildEntries.Length == 0)
        {
            return Result(WindowsLifecycleStatus.Unknown, build == WindowsLifecycleData.DeviceSpecificWindows11Build
                ? WindowsLifecycleLimitation.UnreviewedReleaseCoverage
                : WindowsLifecycleLimitation.UnrecognizedBuild);
        }

        var group = GetEditionGroup(edition);
        var entry = buildEntries.SingleOrDefault(candidate => candidate.EditionGroup == group);
        if (entry is null)
        {
            // Server and other products can share a client base build. Without a
            // recognized client edition, even the family/release must stay unknown.
            return Result(WindowsLifecycleStatus.Unknown, WindowsLifecycleLimitation.UnrecognizedEdition);
        }

        if (architecture is not ("x64" or "x86" or "arm64" or "arm32" or "ia64"))
        {
            return Result(WindowsLifecycleStatus.Unknown, WindowsLifecycleLimitation.UnknownArchitecture, entry);
        }

        if (architecture != "x64")
        {
            return Result(WindowsLifecycleStatus.Unsupported, WindowsLifecycleLimitation.UnsupportedArchitecture, entry);
        }

        if (evaluationDate < entry.Released)
        {
            return Result(WindowsLifecycleStatus.Unknown, WindowsLifecycleLimitation.NotYetReleased, entry);
        }

        return evaluationDate > entry.SupportEnds
            ? Result(WindowsLifecycleStatus.Unsupported, WindowsLifecycleLimitation.SupportEnded, entry)
            : Result(WindowsLifecycleStatus.Supported, WindowsLifecycleLimitation.None, entry);
    }

    // Base-build identity does not expire with the edition-specific servicing snapshot.
    // 26H1 is independently identified by Microsoft's release information; its
    // device-specific servicing coverage remains unreviewed for ThirdLife.
    internal bool RecognizesWindows11Build(uint build) =>
        _dataLimitation == WindowsLifecycleLimitation.None &&
        (build == WindowsLifecycleData.DeviceSpecificWindows11Build || _entries.Any(entry => entry.Build == build));

    internal static string? GetEditionName(uint edition) => edition switch
    {
        0x65 => "home",
        0x62 => "home_n",
        0x63 => "home_china",
        0x64 => "home_single_language",
        0x30 => "pro",
        0x31 => "pro_n",
        0xA1 => "pro_workstations",
        0xA2 => "pro_workstations_n",
        0xA4 => "pro_education",
        0x04 => "enterprise",
        0x1B => "enterprise_n",
        0x79 => "education",
        0x7A => "education_n",
        _ => null,
    };

    private static WindowsLifecycleEditionGroup? GetEditionGroup(uint edition) => edition switch
    {
        0x65 or 0x62 or 0x63 or 0x64 or 0x30 or 0x31 or 0xA1 or 0xA2 or 0xA4 =>
            WindowsLifecycleEditionGroup.HomeAndPro,
        0x04 or 0x1B or 0x79 or 0x7A => WindowsLifecycleEditionGroup.EnterpriseAndEducation,
        _ => null,
    };

    private WindowsLifecycleResult Result(
        WindowsLifecycleStatus status,
        WindowsLifecycleLimitation limitation,
        WindowsLifecycleEntry? entry = null) =>
        new(
            status,
            limitation,
            _hasRecognizedMetadata ? WindowsLifecycleData.SourceId : null,
            _hasRecognizedMetadata ? WindowsLifecycleData.Revision : null,
            _hasRecognizedMetadata ? WindowsLifecycleData.AsOf : null,
            _hasRecognizedMetadata ? WindowsLifecycleData.AsOf.AddDays(MaximumAgeDays) : null,
            entry?.Release,
            entry?.Family);

    private static WindowsLifecycleLimitation ValidateEntries(WindowsLifecycleEntry[] entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null || entry.Build == 0 || !Enum.IsDefined(entry.EditionGroup) ||
                entry.Family != "windows_11" ||
                entry.Release is not { Length: >= 1 and <= 8 } ||
                !entry.Release.All(char.IsAsciiLetterOrDigit) ||
                entry.Released is null || entry.SupportEnds is null || entry.SupportEnds < entry.Released)
            {
                return WindowsLifecycleLimitation.InvalidData;
            }
        }

        var identities = new HashSet<(uint Build, WindowsLifecycleEditionGroup EditionGroup)>();
        var families = new Dictionary<uint, (string Family, string Release, DateOnly? Released)>();
        foreach (var entry in entries)
        {
            if (!identities.Add((entry.Build, entry.EditionGroup)))
            {
                return WindowsLifecycleLimitation.ContradictoryData;
            }

            var identity = (entry.Family, entry.Release, entry.Released);
            if (families.TryGetValue(entry.Build, out var existing) && existing != identity)
            {
                return WindowsLifecycleLimitation.ContradictoryData;
            }

            families.TryAdd(entry.Build, identity);
        }

        return WindowsLifecycleLimitation.None;
    }
}
