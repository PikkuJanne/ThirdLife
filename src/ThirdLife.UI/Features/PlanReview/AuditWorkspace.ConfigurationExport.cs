using ThirdLife.Reports.Configuration;
using ThirdLife.UI.Features.PlanReview;

namespace ThirdLife.UI.Shell;

internal sealed partial class AuditWorkspace
{
    private PlanProfileOption? _configurationExportOption;

    public PlanProfileOption? ConfigurationExportOption
    {
        get => _configurationExportOption;
        set
        {
            if (ReferenceEquals(value, _configurationExportOption) || IsBusy || _closing ||
                (value is not null && !PlanProfiles.Contains(value)))
            {
                return;
            }

            _configurationExportOption = value;
            ConfigurationPreview = null;
            ConfigurationExportStatus = string.Empty;
            Publish();
        }
    }

    public ConfigurationArchivePreview? ConfigurationPreview { get; private set; }
    public bool HasConfigurationPreview => ConfigurationPreview is not null;
    public bool HasConfigurationExportOptions => PlanProfiles.Count != 0;
    public bool CanChooseConfigurationExport => !_closing && !IsBusy && PlanProfiles.Count != 0;
    public bool CanPreviewConfigurationExport => CanChooseConfigurationExport && ConfigurationExportOption is not null;
    public bool CanSaveConfigurationExport => CanPreviewConfigurationExport && HasConfigurationPreview;
    public string ConfigurationExportStatus { get; private set; } = string.Empty;
    public bool HasConfigurationExportStatus => ConfigurationExportStatus.Length != 0;

    public void PreviewConfigurationExport()
    {
        if (!CanPreviewConfigurationExport || ConfigurationExportOption is not { } option)
        {
            return;
        }

        ConfigurationPreview = WorkshopConfigurationExport.ForOption(option);
        ConfigurationExportStatus = ConfigurationPreview is null ? "The configuration files could not be verified." : "Configuration preview is ready.";
        Publish();
    }

    public Task SaveConfigurationExportAsync(ConfigurationArchivePreview expected,
        ConfigurationArchiveDestination destination)
    {
        if (!CanSaveConfigurationExport || !ReferenceEquals(ConfigurationPreview, expected))
        {
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            ConfigurationExportStatus = "Saving the reviewed configuration ZIP.";
            Publish();
            var result = await Task.Run(() => ConfigurationArchiveStore.Save(expected, destination));
            ConfigurationExportStatus = result switch
            {
                ConfigurationArchiveSaveResult.Saved =>
                    $"Saved {expected.FileName} in {ConfigurationArchiveDestination.ProtectedFolderName}.",
                ConfigurationArchiveSaveResult.AlreadyExists =>
                    "A file or folder already has this archive name. Choose a different folder.",
                ConfigurationArchiveSaveResult.DestinationUnavailable =>
                    "The protected configuration folder could not be verified. Choose another local folder.",
                ConfigurationArchiveSaveResult.InsufficientSpace => "The selected drive has insufficient free space.",
                _ => "The ZIP publication could not be confirmed. Check the chosen folder before trying again.",
            };
        }, refreshFinalization: false);
    }

    public void ShowInvalidConfigurationDestination()
    {
        ConfigurationExportStatus = "Choose a local Windows folder for the configuration export.";
        Publish();
    }
}
