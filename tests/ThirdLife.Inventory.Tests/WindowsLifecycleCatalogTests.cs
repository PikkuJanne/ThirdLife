using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class WindowsLifecycleCatalogTests
{
    private static readonly DateOnly EvaluationDate = new(2026, 9, 30);

    [Theory]
    [InlineData(26100U, 0x65U, "24H2")]
    [InlineData(26200U, 0x30U, "25H2")]
    [InlineData(26300U, 0x30U, "26H2")]
    [InlineData(22631U, 0x04U, "23H2")]
    [InlineData(22631U, 0x79U, "23H2")]
    public void CurrentExactWindows11MappingsRetainAttributableRevision(
        uint build,
        uint edition,
        string release)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(build, edition, "x64", EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.Supported, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.None, result.Limitation);
        Assert.Equal("microsoft.windows-client.release-health", result.SourceId);
        Assert.Equal("thirdlife.windows-lifecycle.2026-09-30.v1", result.Revision);
        Assert.Equal(EvaluationDate, result.AsOf);
        Assert.Equal(new DateOnly(2026, 10, 30), result.FreshThrough);
        Assert.Equal(release, result.Release);
        Assert.Equal("windows_11", result.Family);
    }

    [Theory]
    [InlineData(0x65U, "home")]
    [InlineData(0x62U, "home_n")]
    [InlineData(0x63U, "home_china")]
    [InlineData(0x64U, "home_single_language")]
    [InlineData(0x30U, "pro")]
    [InlineData(0x31U, "pro_n")]
    [InlineData(0xA1U, "pro_workstations")]
    [InlineData(0xA2U, "pro_workstations_n")]
    [InlineData(0xA4U, "pro_education")]
    [InlineData(0x04U, "enterprise")]
    [InlineData(0x1BU, "enterprise_n")]
    [InlineData(0x79U, "education")]
    [InlineData(0x7AU, "education_n")]
    public void ReviewedClientEditionCodesHaveNamesAndLifecycleGroups(uint edition, string name)
    {
        Assert.Equal(name, WindowsLifecycleCatalog.GetEditionName(edition));
        Assert.Equal(
            WindowsLifecycleStatus.Supported,
            WindowsLifecycleCatalog.Current.Evaluate(26200, edition, "x64", EvaluationDate).Status);
    }

    [Theory]
    [InlineData(22000U, 0x30U)]
    [InlineData(22000U, 0x04U)]
    [InlineData(22621U, 0x30U)]
    [InlineData(22621U, 0x04U)]
    [InlineData(22631U, 0x30U)]
    public void EndedWindows11ReleaseAndEditionPairsAreUnsupported(uint build, uint edition)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(build, edition, "x64", EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.Unsupported, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.SupportEnded, result.Limitation);
        Assert.Equal("windows_11", result.Family);
    }

    [Fact]
    public void PublishedEndDateAndFollowingDayAreDistinguishedByControlledDate()
    {
        var catalog = WindowsLifecycleCatalog.Current;

        Assert.Equal(WindowsLifecycleStatus.Supported,
            catalog.Evaluate(26100, 0x30, "x64", new(2026, 10, 13)).Status);
        Assert.Equal(WindowsLifecycleStatus.Unsupported,
            catalog.Evaluate(26100, 0x30, "x64", new(2026, 10, 14)).Status);
        Assert.Equal(WindowsLifecycleStatus.Supported,
            catalog.Evaluate(26100, 0x04, "x64", new(2026, 10, 14)).Status);
    }

    [Theory]
    [InlineData(10240U, "1507")]
    [InlineData(10586U, "1511")]
    [InlineData(14393U, "1607")]
    [InlineData(15063U, "1703")]
    [InlineData(16299U, "1709")]
    [InlineData(17134U, "1803")]
    [InlineData(17763U, "1809")]
    [InlineData(18362U, "1903")]
    [InlineData(18363U, "1909")]
    [InlineData(19041U, "2004")]
    [InlineData(19042U, "20H2")]
    [InlineData(19043U, "21H1")]
    [InlineData(19044U, "21H2")]
    [InlineData(19045U, "22H2")]
    public void KnownWindows10BuildsRemainAuditOnlyWithoutAnEsuOverride(uint build, string release)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(build, 0x30, "x64", EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.AuditOnly, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.Windows10AuditOnly, result.Limitation);
        Assert.Equal("windows_10", result.Family);
        Assert.Equal(release, result.Release);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(19046U)]
    [InlineData(21999U)]
    [InlineData(26101U)]
    [InlineData(26220U)]
    [InlineData(28000U)]
    [InlineData(99999U)]
    [InlineData(uint.MaxValue)]
    public void NoRangeInferenceRecognizesUnknownOrExcludedBuilds(uint build)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(build, 0x30, "x64", EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.UnrecognizedBuild, result.Limitation);
        Assert.Null(result.Release);
        Assert.Null(result.Family);
        Assert.NotNull(result.Revision);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(0x07U)] // Windows Server Standard, including shared build 26100.
    [InlineData(0x48U)] // Enterprise evaluation is not general-availability Enterprise.
    [InlineData(0x7DU)] // Enterprise LTSC is not the general-availability edition.
    [InlineData(0xAFU)] // Enterprise multi-session requires a separate reviewed mapping.
    [InlineData(0xBCU)] // IoT Enterprise.
    [InlineData(0xBFU)] // IoT Enterprise LTSC.
    [InlineData(uint.MaxValue)]
    public void UnknownServerAndSpecializedEditionsCannotInheritClientSupport(uint edition)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(26100, edition, "x64", EvaluationDate);

        Assert.Null(WindowsLifecycleCatalog.GetEditionName(edition));
        Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.UnrecognizedEdition, result.Limitation);
        Assert.Null(result.Family);
        Assert.Null(result.Release);
    }

    [Theory]
    [InlineData("x86")]
    [InlineData("arm64")]
    [InlineData("arm32")]
    [InlineData("ia64")]
    public void NonX64Windows11CannotSatisfyThePreparationTarget(string architecture)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(26200, 0x30, architecture, EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.Unsupported, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.UnsupportedArchitecture, result.Limitation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X64")]
    [InlineData("future_architecture")]
    public void MissingOrUnrecognizedArchitectureCannotPass(string? architecture)
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(26200, 0x30, architecture, EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.UnknownArchitecture, result.Limitation);
    }

    [Fact]
    public void ThirtyDayFreshnessWindowIsBoundedAndStaleWindows10AlsoRemainsUnknown()
    {
        var catalog = WindowsLifecycleCatalog.Current;

        Assert.Equal(WindowsLifecycleStatus.Supported,
            catalog.Evaluate(26200, 0x30, "x64", EvaluationDate.AddDays(30)).Status);
        foreach (var build in new uint[] { 26200, 19045 })
        {
            var stale = catalog.Evaluate(build, 0x30, "x64", EvaluationDate.AddDays(31));
            Assert.Equal(WindowsLifecycleStatus.Unknown, stale.Status);
            Assert.Equal(WindowsLifecycleLimitation.StaleData, stale.Limitation);
            Assert.NotNull(stale.SourceId);
            Assert.NotNull(stale.Revision);
            Assert.Equal(EvaluationDate, stale.AsOf);
        }
    }

    [Fact]
    public void CatalogFromAfterTheEvaluationDateCannotBeUsedAsHistoricalProof()
    {
        var result = WindowsLifecycleCatalog.Current.Evaluate(26200, 0x30, "x64", EvaluationDate.AddDays(-1));

        Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
        Assert.Equal(WindowsLifecycleLimitation.FutureData, result.Limitation);
    }

    [Theory]
    [InlineData(null, "thirdlife.windows-lifecycle.2026-09-30.v1")]
    [InlineData("microsoft.windows-client.release-health", null)]
    [InlineData("unrecognized-source", "thirdlife.windows-lifecycle.2026-09-30.v1")]
    [InlineData("microsoft.windows-client.release-health", "thirdlife.windows-lifecycle.2027-01-01.v2")]
    [InlineData("microsoft.windows-client.release-health", "unrecognized-revision")]
    public void MissingAndUnknownSourceOrRevisionCannotPassOrPublishUnverifiedMetadata(
        string? source,
        string? revision)
    {
        var catalog = new WindowsLifecycleCatalog(source, revision, EvaluationDate, [CurrentEntry()]);
        var result = catalog.Evaluate(26200, 0x30, "x64", EvaluationDate);

        Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
        Assert.Null(result.SourceId);
        Assert.Null(result.Revision);
        Assert.Null(result.AsOf);
        Assert.Null(result.FreshThrough);
    }

    [Fact]
    public void MissingDataOrRevisionDateCannotPass()
    {
        foreach (var catalog in new[]
        {
            new WindowsLifecycleCatalog(WindowsLifecycleData.SourceId, WindowsLifecycleData.Revision, EvaluationDate, null),
            new WindowsLifecycleCatalog(WindowsLifecycleData.SourceId, WindowsLifecycleData.Revision, EvaluationDate, []),
            new WindowsLifecycleCatalog(WindowsLifecycleData.SourceId, WindowsLifecycleData.Revision, null, [CurrentEntry()]),
            new WindowsLifecycleCatalog(WindowsLifecycleData.SourceId, WindowsLifecycleData.Revision, EvaluationDate.AddDays(1), [CurrentEntry()]),
        })
        {
            var result = catalog.Evaluate(26200, 0x30, "x64", EvaluationDate);
            Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
            Assert.Null(result.Revision);
        }
    }

    [Fact]
    public void ContradictoryRowsFailClosedInEitherOrderIncludingUnmatchedRows()
    {
        var entry = CurrentEntry();
        var contrary = entry with { SupportEnds = new DateOnly(2025, 10, 1) };
        foreach (var rows in new[] { new[] { entry, contrary }, new[] { contrary, entry } })
        {
            var catalog = Catalog(rows);
            AssertUnknown(catalog, WindowsLifecycleLimitation.ContradictoryData);
            Assert.Equal(WindowsLifecycleStatus.Unknown,
                catalog.Evaluate(26100, 0x30, "x64", EvaluationDate).Status);
        }
    }

    [Fact]
    public void DuplicateRowsAndCrossEditionIdentityConflictAreRejected()
    {
        var entry = CurrentEntry();
        AssertUnknown(Catalog([entry, entry]), WindowsLifecycleLimitation.ContradictoryData);
        AssertUnknown(Catalog([entry, entry with
        {
            EditionGroup = WindowsLifecycleEditionGroup.EnterpriseAndEducation,
            Release = "26H2",
        }]), WindowsLifecycleLimitation.ContradictoryData);
    }

    [Fact]
    public void MalformedDatesAndOversizedOrNullRowsFailClosed()
    {
        var entry = CurrentEntry();
        foreach (var invalid in new[]
        {
            entry with { SupportEnds = null },
            entry with { Released = null },
            entry with { SupportEnds = new DateOnly(2020, 1, 1) },
            entry with { Family = "unrecognized_family" },
            entry with { Release = "arbitrary raw input" },
            entry with { Build = 0 },
            entry with { EditionGroup = (WindowsLifecycleEditionGroup)99 },
        })
        {
            AssertUnknown(Catalog([invalid]), WindowsLifecycleLimitation.InvalidData);
        }

        AssertUnknown(Catalog([null!]), WindowsLifecycleLimitation.InvalidData);
        AssertUnknown(Catalog(Enumerable.Repeat(entry, WindowsLifecycleCatalog.MaximumEntries + 1).ToArray()),
            WindowsLifecycleLimitation.InvalidData);
    }

    [Fact]
    public void FutureReleaseCannotBeSupportedBeforeItsAvailabilityDate()
    {
        var catalog = Catalog([CurrentEntry() with { Released = EvaluationDate.AddDays(1) }]);

        AssertUnknown(catalog, WindowsLifecycleLimitation.NotYetReleased);
    }

    [Fact]
    public void CallerCannotMutateCatalogAfterItsValidation()
    {
        var rows = new[] { CurrentEntry() };
        var catalog = Catalog(rows);
        rows[0] = rows[0] with { SupportEnds = EvaluationDate.AddDays(-1) };

        Assert.Equal(WindowsLifecycleStatus.Supported,
            catalog.Evaluate(26200, 0x30, "x64", EvaluationDate).Status);
    }

    private static WindowsLifecycleCatalog Catalog(IReadOnlyList<WindowsLifecycleEntry> rows) =>
        new(WindowsLifecycleData.SourceId, WindowsLifecycleData.Revision, EvaluationDate, rows);

    private static WindowsLifecycleEntry CurrentEntry() =>
        new(26200, WindowsLifecycleEditionGroup.HomeAndPro, "windows_11", "25H2",
            new(2025, 9, 30), new(2027, 10, 12));

    private static void AssertUnknown(WindowsLifecycleCatalog catalog, WindowsLifecycleLimitation limitation)
    {
        var result = catalog.Evaluate(26200, 0x30, "x64", EvaluationDate);
        Assert.Equal(WindowsLifecycleStatus.Unknown, result.Status);
        Assert.Equal(limitation, result.Limitation);
    }
}
