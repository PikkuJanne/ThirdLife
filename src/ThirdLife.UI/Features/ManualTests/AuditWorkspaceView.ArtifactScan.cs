using System.Windows;

namespace ThirdLife.UI.Features.ManualTests;

public partial class AuditWorkspaceView
{
    private async void ScanWorkshopArtifacts(object sender, RoutedEventArgs e)
    {
        if (!Workspace.CanScanArtifacts) { return; }

        if (WorkshopAccountNames.Text.Length >= WorkshopAccountNames.MaxLength ||
            WorkshopShareNames.Text.Length >= WorkshopShareNames.MaxLength)
        {
            Workspace.ReportOversizedArtifactTargets();
            FocusAndReveal(ArtifactScanMessage);
            return;
        }

        var accounts = ReadArtifactTargets(WorkshopAccountNames.Text);
        var shares = ReadArtifactTargets(WorkshopShareNames.Text);
        ClearArtifactTargets();
        FocusAndReveal(ArtifactScanMessage);
        await Workspace.ScanArtifactsAsync(accounts, shares);
    }

    private void CancelWorkshopArtifactScan(object sender, RoutedEventArgs e)
    {
        FocusAndReveal(ArtifactScanMessage);
        Workspace.CancelArtifactScan();
    }

    private void ClearArtifactTargets()
    {
        WorkshopAccountNames.Clear();
        WorkshopShareNames.Clear();
    }

    private static string[] ReadArtifactTargets(string input) => input.Split(['\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
