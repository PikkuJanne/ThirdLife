using ThirdLife.Reports.Configuration;

namespace ThirdLife.Reports.Tests;

public sealed class ConfigurationArchiveDestinationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(@"C:relative")]
    [InlineData(@"\root\folder")]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"\\?\C:\folder")]
    [InlineData(@"C:\exports\..\different")]
    [InlineData(@"C:\exports:stream")]
    public void RejectsAmbiguousRelativeAndNonlocalSelections(string? selected)
    {
        Assert.Null(ConfigurationArchiveDestination.FromSelectedFolder(selected));
    }

    [Fact]
    public void PreservesTheExactChosenParentWithoutCreatingFiles()
    {
        var selected = Path.Combine(Path.GetTempPath(), $"thirdlife-config-destination-{Guid.NewGuid():N}");
        var destination = Assert.IsType<ConfigurationArchiveDestination>(ConfigurationArchiveDestination.FromSelectedFolder(selected));
        Assert.Equal(selected, destination.SelectedFolder);
        Assert.False(Directory.Exists(selected));
    }
}
