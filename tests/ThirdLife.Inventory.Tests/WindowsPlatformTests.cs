using ThirdLife.Inventory.Windows;

namespace ThirdLife.Inventory.Tests;

public sealed class WindowsPlatformTests
{
    [Theory]
    [InlineData(22000)]
    [InlineData(22621)]
    [InlineData(22631)]
    [InlineData(26100)]
    [InlineData(26200)]
    [InlineData(26300)]
    [InlineData(28000)]
    public void ReviewedWindows11WorkstationBuildsAllowNativeX64Execution(int build)
    {
        var platform = WindowsPlatform.Evaluate(Snapshot(build));

        Assert.Equal(WindowsPlatformEligibility.Supported, platform.Eligibility);
        Assert.Equal("windows_11", platform.Family);
    }

    [Theory]
    [InlineData(19045, 1, 48, 9, WindowsPlatformEligibility.UnsupportedOperatingSystem, "windows_10")]
    [InlineData(26100, 3, 7, 9, WindowsPlatformEligibility.UnsupportedOperatingSystem, "windows_server")]
    [InlineData(99999, 2, 7, 9, WindowsPlatformEligibility.UnsupportedOperatingSystem, "windows_server")]
    [InlineData(26300, 1, 48, 12, WindowsPlatformEligibility.UnsupportedArchitecture, "windows_11")]
    [InlineData(26300, 1, 48, 999, WindowsPlatformEligibility.UnverifiedIdentity, "windows_11")]
    [InlineData(26100, 3, 48, 9, WindowsPlatformEligibility.UnverifiedIdentity, null)]
    [InlineData(26100, 1, 7, 9, WindowsPlatformEligibility.UnverifiedIdentity, null)]
    [InlineData(99999, 1, 48, 9, WindowsPlatformEligibility.UnverifiedIdentity, null)]
    [InlineData(26300, 0, 48, 9, WindowsPlatformEligibility.UnverifiedIdentity, null)]
    public void OSRoleBuildAndNativeArchitectureDetermineScope(
        int build, int productType, int edition, int osArchitecture, WindowsPlatformEligibility eligibility, string? family)
    {
        // Architecture 12 models an Arm64 OS even when this deterministic test runs in an x64 process.
        var platform = WindowsPlatform.Evaluate(Snapshot(build, (WindowsProductType)productType, (uint)edition, (ushort)osArchitecture));
        Assert.Equal(eligibility, platform.Eligibility);
        Assert.Equal(family, platform.Family);
    }

    [Fact]
    public void EarlierWindowsAndContradictoryVersionBuildsAreDistinct()
    {
        var earlier = Snapshot(7601) with { Version = SystemInventorySourceValue<OperatingSystemVersion>.Available(new(6, 1, 7601)) };
        var contradictory = earlier with { Version = SystemInventorySourceValue<OperatingSystemVersion>.Available(new(6, 1, 26300)) };

        var unsupported = WindowsPlatform.Evaluate(earlier);
        Assert.Equal(WindowsPlatformEligibility.UnsupportedOperatingSystem, unsupported.Eligibility);
        Assert.Equal("windows_other", unsupported.Family);
        Assert.Equal(WindowsPlatformEligibility.UnverifiedIdentity, WindowsPlatform.Evaluate(contradictory).Eligibility);
    }

    [Fact]
    public void MissingIdentityInputsCannotPass()
    {
        var snapshot = Snapshot(26300);
        foreach (var missing in new[]
        {
            snapshot with { Version = SystemInventorySourceValue<OperatingSystemVersion>.Unavailable() },
            snapshot with { ProductType = SystemInventorySourceValue<WindowsProductType>.AccessDenied() },
            snapshot with { Architecture = SystemInventorySourceValue<ushort>.InvalidData() },
        })
        {
            Assert.Equal(WindowsPlatformEligibility.UnverifiedIdentity, WindowsPlatform.Evaluate(missing).Eligibility);
        }
    }

    [Fact]
    public void StartupIdentityDoesNotConferServicingEligibility()
    {
        var snapshot = Snapshot(26300);
        var staleDate = WindowsLifecycleData.AsOf.AddDays(WindowsLifecycleCatalog.MaximumAgeDays + 1);
        var stale = WindowsLifecycleCatalog.Current.Evaluate(26300, 48, "x64", staleDate);
        var specialEdition = WindowsLifecycleCatalog.Current.Evaluate(26300, 0x7D, "x64", WindowsLifecycleData.AsOf);
        var deviceSpecificRelease = WindowsLifecycleCatalog.Current.Evaluate(28000, 48, "x64", WindowsLifecycleData.AsOf);

        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.Evaluate(snapshot).Eligibility);
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.Evaluate(Snapshot(26300, edition: 0x7D)).Eligibility);
        Assert.Equal(WindowsPlatformEligibility.Supported, WindowsPlatform.Evaluate(Snapshot(28000)).Eligibility);
        Assert.Equal(WindowsLifecycleLimitation.StaleData, stale.Limitation);
        Assert.Equal(WindowsLifecycleLimitation.UnrecognizedEdition, specialEdition.Limitation);
        Assert.Equal(WindowsLifecycleLimitation.UnreviewedReleaseCoverage, deviceSpecificRelease.Limitation);
    }

    private static OperatingSystemSnapshot Snapshot(
        int build, WindowsProductType productType = WindowsProductType.Workstation, uint edition = 48, ushort architecture = 9) =>
        new(
            SystemInventorySourceValue<OperatingSystemVersion>.Available(new(10, 0, build)),
            SystemInventorySourceValue<uint>.Available(edition),
            SystemInventorySourceValue<ushort>.Available(architecture),
            SystemInventorySourceValue<WindowsProductType>.Available(productType));
}
