using System.Diagnostics;
using System.Security.Principal;
using ThirdLife.Inventory.Windows;
using Xunit.Abstractions;

namespace ThirdLife.Inventory.Tests;

public sealed class WindowsPreparationResourcesTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 128, 255, WindowsPreparationPower.NoBattery)]
    [InlineData(255, 128, 255, WindowsPreparationPower.NoBattery)]
    [InlineData(1, 8, 50, WindowsPreparationPower.ExternalPower)]
    [InlineData(1, 10, 10, WindowsPreparationPower.ExternalPower)]
    [InlineData(0, 0, 50, WindowsPreparationPower.OnBattery)]
    [InlineData(0, 0, 255, WindowsPreparationPower.OnBattery)]
    [InlineData(0, 2, 9, WindowsPreparationPower.CriticallyLowBattery)]
    [InlineData(1, 8, 9, WindowsPreparationPower.CriticallyLowBattery)]
    [InlineData(1, 12, 4, WindowsPreparationPower.CriticallyLowBattery)]
    [InlineData(1, 4, 255, WindowsPreparationPower.CriticallyLowBattery)]
    [InlineData(255, 4, 4, WindowsPreparationPower.CriticallyLowBattery)]
    [InlineData(255, 0, 50, WindowsPreparationPower.Unknown)]
    [InlineData(1, 255, 255, WindowsPreparationPower.Unknown)]
    [InlineData(1, 0, 255, WindowsPreparationPower.Unknown)]
    [InlineData(1, 16, 50, WindowsPreparationPower.Unknown)]
    [InlineData(1, 136, 255, WindowsPreparationPower.Unknown)]
    [InlineData(1, 5, 50, WindowsPreparationPower.Unknown)]
    [InlineData(1, 0, 101, WindowsPreparationPower.Unknown)]
    [InlineData(1, 128, 50, WindowsPreparationPower.Unknown)]
    [InlineData(0, 8, 50, WindowsPreparationPower.Unknown)]
    [InlineData(0, 128, 255, WindowsPreparationPower.Unknown)]
    [InlineData(2, 128, 255, WindowsPreparationPower.Unknown)]
    public void PowerClassifiesCurrentEvidence(byte externalPower, byte batteryFlag, byte percent, WindowsPreparationPower expected) =>
        Assert.Equal(expected, WindowsPreparationResources.ClassifyPower(externalPower, batteryFlag, percent));

    [Fact]
    public void InstallCapacityRequiresBothRoleReadsOnTheSameKnownVolume()
    {
        Assert.Equal(100UL, WindowsPreparationResources.GetSystemInstallCapacity(200, 100, true));
        Assert.Equal(0UL, WindowsPreparationResources.GetSystemInstallCapacity(0, 100, true));
        Assert.Null(WindowsPreparationResources.GetSystemInstallCapacity(200, 100, false));
        Assert.Null(WindowsPreparationResources.GetSystemInstallCapacity(200, 100, null));
        Assert.Null(WindowsPreparationResources.GetSystemInstallCapacity(null, 100, true));
        Assert.Null(WindowsPreparationResources.GetSystemInstallCapacity(200, null, true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:profile")]
    [InlineData(@"\\server\share\profile")]
    [InlineData(@"\\?\C:\profile")]
    public void PreparationDoesNotProbeNonLocalOrUnqualifiedLocations(string path)
    {
        var (availableBytes, mountPath) = WindowsStorageVolumeSource.ReadPreparationVolume(path);
        Assert.Null(availableBytes);
        Assert.Null(mountPath);
    }

    [Fact]
    public void VolumeComparisonPreservesUnavailableIdentity()
    {
        Assert.True(WindowsStorageVolumeSource.ShareVolume(@"C:\", @"c:\"));
        Assert.Null(WindowsStorageVolumeSource.ShareVolume(@"C:\", null));
        Assert.Null(WindowsStorageVolumeSource.ShareVolume(null, null));
    }

    [Fact]
    [Trait("Category", "ActiveMachine")]
    public void ActiveMachinePreparationProbeUsesUnelevatedBoundedNativeReads()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        var timer = Stopwatch.StartNew();
        var observed = WindowsPreparationResources.Read();
        timer.Stop();

        Assert.Equal(WindowsPlatformEligibility.Supported, observed.Platform);
        Assert.NotNull(observed.SystemAvailableBytes);
        Assert.NotNull(observed.StagingAvailableBytes);
        Assert.NotNull(observed.SharedVolume);
        Assert.True(Enum.IsDefined(observed.Power));
        Assert.InRange(timer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        output.WriteLine($"Resource observation took {timer.Elapsed.TotalMilliseconds:F1} ms. " +
            $"Power state {observed.Power}. System and staging share a volume {observed.SharedVolume}.");
    }
}
