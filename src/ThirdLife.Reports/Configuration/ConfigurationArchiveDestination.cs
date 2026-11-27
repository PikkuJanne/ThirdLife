namespace ThirdLife.Reports.Configuration;

/// <summary>The exact local parent folder selected for the protected configuration directory.</summary>
public sealed class ConfigurationArchiveDestination
{
    public const string ProtectedFolderName = "ThirdLifeConfiguration";

    private ConfigurationArchiveDestination(string folder)
    {
        SelectedFolder = folder;
    }

    internal string SelectedFolder { get; }

    public static ConfigurationArchiveDestination? FromSelectedFolder(string? folder)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(folder) || folder.Length < 3 ||
            !char.IsAsciiLetter(folder[0]) || folder[1] != ':' || folder[2] != '\\' ||
            folder.AsSpan(2).Contains(':') || folder.Any(char.IsControl) || !Path.IsPathFullyQualified(folder))
        {
            return null;
        }

        try
        {
            var requested = Path.TrimEndingDirectorySeparator(folder);
            var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            return string.Equals(canonical, requested, StringComparison.OrdinalIgnoreCase) ? new(canonical) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
