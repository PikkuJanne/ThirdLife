using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ThirdLife.Catalog.Profiles;
using ThirdLife.Catalog.Schema;
using ThirdLife.Policy.Versioning;
using ThirdLife.Reports.Configuration;
using ThirdLife.UI.Features.PlanReview;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class WorkshopConfigurationExportTests
{
    private static readonly DateTimeOffset CandidateReviewTime = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("basic", true)]
    [InlineData("job-seeker", true)]
    [InlineData("plain-text-editor", false)]
    public void BundledConfigurationExportPreservesValidatedExactBytes(string profileId, bool synthetic)
    {
        var option = string.Equals(profileId, "plain-text-editor", StringComparison.Ordinal)
            ? Assert.IsType<PlanProfileOption>(WorkshopPackageProfile.Load(CandidateReviewTime))
            : Assert.Single(WorkshopPlanCandidate.Options, candidate => candidate.Profile.ProfileId == profileId);
        var export = Assert.IsType<ConfigurationArchivePreview>(WorkshopConfigurationExport.ForOption(option));
        Assert.Equal(profileId, export.ProfileId);
        Assert.Equal(option.Profile.Version, export.ProfileVersion);
        Assert.Equal(synthetic, export.SyntheticData);
        Assert.Equal(["profile.json", "catalog.json", "policy.json"], export.Files.Select(file => file.FileName));
        Assert.All(export.Files, file =>
            Assert.Equal(file.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(file.Utf8Json.AsSpan()))));

        var catalogueFile = Assert.Single(export.Files, file => file.FileName == "catalog.json");
        var profileFile = Assert.Single(export.Files, file => file.FileName == "profile.json");
        var policyFile = Assert.Single(export.Files, file => file.FileName == "policy.json");
        Assert.Equal(option.Profile.Utf8Json.AsEnumerable(), profileFile.Utf8Json);
        Assert.Equal(option.Catalog.Utf8Json.AsEnumerable(), catalogueFile.Utf8Json);
        var catalogue = CatalogSchema.Parse(catalogueFile.Utf8Json.AsMemory(), CandidateReviewTime);
        var profile = ProfileSchema.Parse(profileFile.Utf8Json.AsMemory(), catalogue);
        var policy = PolicySnapshot.Restore(policyFile.Utf8Json.AsMemory(), policyFile.ContentSha256);
        Assert.Equal(option.Profile.ContentSha256, profile.ContentSha256);
        Assert.Equal(option.Profile.CatalogContentSha256, catalogue.ContentSha256);
        Assert.Equal(profile.Policy.Id, policy.Document.PolicyId);
        Assert.Equal(profile.Policy.Version, policy.Document.Version);
        Assert.Equal(synthetic, policy.Document.SyntheticData);

        var zipBytes = export.CopyArchiveBytes();
        Assert.Equal(export.ArchiveSha256, Convert.ToHexStringLower(SHA256.HashData(zipBytes)));
        Assert.Equal(export.ArchiveByteCount, zipBytes.Length);
        Assert.InRange(zipBytes.Length, 1, ConfigurationArchivePreview.MaximumArchiveBytes);
        Assert.Equal(zipBytes, WorkshopConfigurationExport.ForOption(option)!.CopyArchiveBytes());
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        Assert.Equal(export.Files.Select(file => file.FileName), zip.Entries.Select(entry => entry.FullName));
        foreach (var file in export.Files)
        {
            using var entry = zip.GetEntry(file.FileName)!.Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            Assert.Equal(file.Utf8Json.AsEnumerable(), bytes.ToArray());
        }
    }

    [Fact]
    public void RejectsADifferentCatalogDigestOrPolicyEvenWhenInputsAreWellFormed()
    {
        var option = Assert.Single(WorkshopPlanCandidate.Options, candidate => candidate.Profile.ProfileId == "basic");
        var changedCatalog = CatalogSchema.Parse(option.Catalog.Utf8Json.Add((byte)'\n').AsMemory(), CandidateReviewTime);
        Assert.Equal(option.Catalog.CatalogId, changedCatalog.CatalogId);
        Assert.Equal(option.Catalog.Version, changedCatalog.Version);
        Assert.NotEqual(option.Catalog.ContentSha256, changedCatalog.ContentSha256);
        Assert.Null(WorkshopConfigurationExport.ForOption(option with { Catalog = changedCatalog }));

        var other = Assert.IsType<PlanProfileOption>(WorkshopPackageProfile.Load(CandidateReviewTime));
        Assert.Null(WorkshopConfigurationExport.ForOption(option with { PackagePolicy = other.PackagePolicy }));
    }

    [Fact]
    public void PreparePreviewsAndSavesTheExactConfigurationWithoutOpeningAJobOrChangingThePlan()
    {
        using var disk = new AuditTestStore();
        ShellTestHost.Run(() =>
        {
            var workspace = AuditWorkspace.CreateLive(_ => throw new InvalidOperationException("Configuration export must not open a job store."));
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Prepare);
                var review = Named<PlanReviewView>(Named<FrameworkElement>(window, "AuditView"), "PlanReviewPanel");
                var picker = Named<ComboBox>(review, "ConfigurationExportSelector");
                var previewButton = Named<Button>(review, "PreviewConfigurationExportButton");
                var saveButton = Named<Button>(review, "SaveConfigurationExportButton");
                var region = Named<StackPanel>(review, "ConfigurationPreviewRegion");
                Assert.Equal("Configuration to export", new ComboBoxAutomationPeer(picker).GetName());
                Assert.False(previewButton.IsEnabled);
                Assert.False(region.IsVisible);
                Assert.False(saveButton.IsVisible);

                picker.SelectedItem = Assert.Single(workspace.PlanProfiles, option => option.Profile.ProfileId == "basic");
                Assert.Contains("synthetic candidate", workspace.ConfigurationExportOption!.ConfigurationExportLabel);
                Invoke(window, previewButton);
                var preview = Assert.IsType<ConfigurationArchivePreview>(workspace.ConfigurationPreview);
                Assert.Equal("basic", preview.ProfileId);
                Assert.True(region.IsVisible);
                Assert.Equal(3, Named<ItemsControl>(review, "ConfigurationPreviewFiles").Items.Count);
                Assert.Equal(AutomationLiveSetting.Polite,
                    AutomationProperties.GetLiveSetting(Named<TextBlock>(review, "ConfigurationExportMessage")));
                var protectedRoot = Path.Combine(disk.Root, ConfigurationArchiveDestination.ProtectedFolderName);
                review.SelectConfigurationFolderForTesting = () => null;
                Invoke(window, saveButton);
                Assert.False(Directory.Exists(protectedRoot));
                Assert.Same(preview, workspace.ConfigurationPreview);

                review.SelectConfigurationFolderForTesting = () => disk.Root;
                Invoke(window, saveButton);
                PumpUntil(window, () => !workspace.IsBusy && workspace.ConfigurationExportStatus.StartsWith("Saved ", StringComparison.Ordinal),
                    () => workspace.ConfigurationExportStatus);
                var exported = Path.Combine(protectedRoot, preview.FileName);
                Assert.Equal(preview.CopyArchiveBytes(), File.ReadAllBytes(exported));
                Assert.DoesNotContain(disk.Root, workspace.ConfigurationExportStatus, StringComparison.OrdinalIgnoreCase);
                Assert.Single(Directory.EnumerateFileSystemEntries(protectedRoot));

                File.WriteAllText(exported, "retained collision fixture");
                Invoke(window, saveButton);
                PumpUntil(window, () => !workspace.IsBusy && workspace.ConfigurationExportStatus.Contains("already has this archive name",
                    StringComparison.Ordinal), () => workspace.ConfigurationExportStatus);
                Assert.Equal("retained collision fixture", File.ReadAllText(exported));
                Assert.Single(Directory.EnumerateFileSystemEntries(protectedRoot));
                Assert.Null(workspace.Job);
                Assert.Null(workspace.SelectedPlanProfile);
                Assert.Null(workspace.PlanReview);

                var otherFolder = Directory.CreateDirectory(Path.Combine(disk.Root, "changed-selection")).FullName;
                review.SelectConfigurationFolderForTesting = () =>
                {
                    picker.SelectedItem = Assert.Single(workspace.PlanProfiles, option => option.Profile.ProfileId == "job-seeker");
                    return otherFolder;
                };
                Invoke(window, saveButton);
                Assert.Null(workspace.ConfigurationPreview);
                Assert.False(region.IsVisible);
                Assert.Empty(Directory.EnumerateFileSystemEntries(otherFolder));
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }

    [Fact]
    public void CancelledAndObstructedDestinationsLeaveTheChosenParentIntact()
    {
        using var disk = new AuditTestStore();
        var destination = Assert.IsType<ConfigurationArchiveDestination>(ConfigurationArchiveDestination.FromSelectedFolder(disk.Root));
        var option = Assert.Single(WorkshopPlanCandidate.Options, candidate => candidate.Profile.ProfileId == "basic");
        var preview = Assert.IsType<ConfigurationArchivePreview>(WorkshopConfigurationExport.ForOption(option));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ConfigurationArchiveStore.Save(preview, destination, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(disk.Root));

        var obstruction = Path.Combine(disk.Root, ConfigurationArchiveDestination.ProtectedFolderName);
        File.WriteAllText(obstruction, "unrelated fixture");
        Assert.Equal(ConfigurationArchiveSaveResult.DestinationUnavailable, ConfigurationArchiveStore.Save(preview, destination));
        Assert.Equal("unrelated fixture", File.ReadAllText(obstruction));
        Assert.Single(Directory.EnumerateFileSystemEntries(disk.Root));
    }

    [Fact]
    public async Task SelectedDirectoryJunctionCannotRedirectTheProtectedArchive()
    {
        using var disk = new AuditTestStore();
        var outside = Directory.CreateDirectory(Path.Combine(disk.Root, "unrelated")).FullName;
        var sentinel = Path.Combine(outside, "retained.txt");
        File.WriteAllText(sentinel, "unrelated fixture");
        var selected = Path.Combine(disk.Root, "selected");
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", selected, outside })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create the test junction.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(deadline.Token);
        Assert.Equal(0, process.ExitCode);
        try
        {
            var destination = Assert.IsType<ConfigurationArchiveDestination>(ConfigurationArchiveDestination.FromSelectedFolder(selected));
            var option = Assert.Single(WorkshopPlanCandidate.Options, candidate => candidate.Profile.ProfileId == "basic");
            var preview = Assert.IsType<ConfigurationArchivePreview>(WorkshopConfigurationExport.ForOption(option));
            Assert.Equal(ConfigurationArchiveSaveResult.DestinationUnavailable, ConfigurationArchiveStore.Save(preview, destination));
            Assert.Equal("unrelated fixture", File.ReadAllText(sentinel));
            Assert.Single(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(selected);
        }
    }
}
