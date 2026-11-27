using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Core.Support;
using ThirdLife.Diagnostics.Redaction;
using ThirdLife.Inventory;
using ThirdLife.Inventory.Normalization;
using ThirdLife.Inventory.Providers;
using ThirdLife.Persistence;
using ThirdLife.UI.Features.ManualTests;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditSupportArchiveTests
{
    private const string PrivateDecoy = "device-owner@example.invalid";

    [Fact]
    public async Task PreviewShowsBothExactFilesAndRejectsExportWithoutApproval()
    {
        using var disk = new AuditTestStore();
        await using var workspace = await CreateWorkspaceAsync(disk);
        InHandover(workspace, (window, view) =>
        {
            var preview = Named<Button>(view, "PreviewSupportButton");
            Assert.Equal("Preview support archive", ShellTestHost.ButtonPeer(preview).GetName());
            preview.BringIntoView();
            ShellTestHost.Drain(window);
            Assert.True(preview.Focus());
            Invoke(window, preview);
            PumpUntil(window, () => !workspace.IsBusy && workspace.SupportPreview is not null, () => workspace.SupportExportStatus);
            var archive = Assert.IsType<SupportArchivePreview>(workspace.SupportPreview);
            Assert.Contains("not_available", archive.Files[1].Contents, StringComparison.Ordinal);
            Assert.All(archive.Files, file => Assert.DoesNotContain(PrivateDecoy, file.Contents, StringComparison.Ordinal));
            var tabs = Named<TabControl>(view, "SupportFiles");
            Assert.Equal(2, tabs.Items.Count);
            for (var index = 0; index < archive.Files.Count; index++)
            {
                tabs.SelectedIndex = index;
                ShellTestHost.Drain(window);
                var content = Assert.IsType<TextBox>(FindPreviewText(tabs));
                Assert.Equal(archive.Files[index].Name, new TextBoxAutomationPeer(content).GetName());
                Assert.Equal(archive.Files[index].Contents, content.Text);
                Assert.True(content.IsReadOnly);
            }

            Assert.False(Named<Button>(view, "ExportSupportButton").IsEnabled);
            Assert.False(Named<CheckBox>(view, "SupportExportApproved").IsChecked);
        });
    }

    [Fact]
    public async Task ChangedSavedCheckRequiresNewApprovalAndPublishedArchiveMatchesTheReviewedBytes()
    {
        using var disk = new AuditTestStore();
        var destination = Path.Combine(disk.Root, "private-support-export");
        Directory.CreateDirectory(destination);
        await using var workspace = await CreateWorkspaceAsync(disk);
        var jobId = workspace.Job!.Job.JobId;
        const string actorCode = "operator-support-test";
        SupportArchivePreview? approved = null;
        InHandover(workspace, (window, view) =>
        {
            view.SelectSupportFolderForTesting = () => destination;
            var preview = Named<Button>(view, "PreviewSupportButton");
            var approve = Named<CheckBox>(view, "SupportExportApproved");
            var actor = Named<TextBox>(view, "SupportOperatorCode");
            var export = Named<Button>(view, "ExportSupportButton");

            Invoke(window, preview);
            PumpUntil(window, () => !workspace.IsBusy && workspace.SupportPreview is not null);
            Assert.False(export.IsEnabled);
            approve.IsChecked = true;
            actor.Text = actorCode;
            Assert.True(export.IsEnabled);

            Task.Run(async () =>
            {
                await using var other = await OpenStoreAsync(disk);
                var checklist = Assert.IsType<FinalizationChecklist>(await other.LoadFinalizationAsync(jobId));
                await other.SaveFinalizationConfirmationAsync(jobId, checklist, FinalizationRuleId.WorkshopAccounts,
                    FinalizationOutcome.Failed, null, new OperatorId(actorCode));
            }).GetAwaiter().GetResult();

            Invoke(window, export);
            PumpUntil(window, () => !workspace.IsBusy && workspace.SupportPreview is null);
            Assert.Contains("saved facts changed", workspace.SupportExportStatus, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(destination, "ThirdLifeSupport")));

            Invoke(window, preview);
            PumpUntil(window, () => !workspace.IsBusy && workspace.SupportPreview is not null);
            approved = workspace.SupportPreview;
            Assert.False(export.IsEnabled);
            approve.IsChecked = true;
            actor.Text = actorCode;
            Invoke(window, export);
            PumpUntil(window, () => !workspace.IsBusy && workspace.SupportExportStatus.Contains("recorded the support export", StringComparison.Ordinal));
        });

        var expected = Assert.IsType<SupportArchivePreview>(approved);
        var path = Assert.Single(Directory.EnumerateFiles(Path.Combine(destination, "ThirdLifeSupport"), "*.zip"));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(expected.ArchiveByteCount, bytes.Length);
        Assert.Equal(expected.ArchiveSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using (var archive = ZipFile.OpenRead(path))
        {
            Assert.Equal(expected.Files.Select(file => file.Name), archive.Entries.Select(entry => entry.FullName));
            foreach (var file in expected.Files)
            {
                using var reader = new StreamReader(archive.GetEntry(file.Name)!.Open());
                Assert.Equal(file.Contents, await reader.ReadToEndAsync());
                Assert.DoesNotContain(actorCode, file.Contents, StringComparison.Ordinal);
                Assert.DoesNotContain(jobId.Value, file.Contents, StringComparison.Ordinal);
                Assert.DoesNotContain(PrivateDecoy, file.Contents, StringComparison.Ordinal);
            }
        }

        await using var reopened = await OpenStoreAsync(disk);
        var store = Assert.IsAssignableFrom<ISupportExportAuditStore>(reopened);
        var receipt = Assert.Single(await store.LoadSupportExportsAsync(jobId));
        Assert.Equal(SupportExportState.Published, receipt.State);
        Assert.Equal(expected.InternalSupportId, receipt.InternalSupportId);
        Assert.Equal(expected.ArchiveSha256, receipt.ContentSha256);
        Assert.Equal(actorCode, receipt.OperatorId.Value);
    }

    private static async Task<AuditWorkspace> CreateWorkspaceAsync(AuditTestStore disk)
    {
        var workspace = new AuditWorkspace(async token => await OpenStoreAsync(disk, token).ConfigureAwait(false),
            [new SupportFixtureProvider()], TimeProvider.System);
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        await workspace.StartAssessmentAsync();
        Assert.Contains(workspace.Job!.Observations, item => item.Value?.StringValue == PrivateDecoy);
        return workspace;
    }

    private static Task<SqliteJobStore> OpenStoreAsync(AuditTestStore disk, CancellationToken token = default) =>
        SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion,
            TimeProvider.System, faultInjector: null, () => 180_000, () => 10, token);

    private static void InHandover(AuditWorkspace workspace, Action<Window, AuditWorkspaceView> check)
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Handover);
                PumpUntil(window, () => workspace.Finalization is not null && workspace.CanPreviewSupport);
                check(window, Named<AuditWorkspaceView>(window, "AuditView"));
            }
            finally { CloseWindow(window); }
        });
    }

    private static TextBox? FindPreviewText(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBox box && box.IsVisible) { return box; }
            if (FindPreviewText(child) is { } result) { return result; }
        }

        return null;
    }

    private sealed class SupportFixtureProvider : IInventoryProvider
    {
        private static readonly EvidenceKey Decoy = new("device.serial_number");
        private const string Source = "support.synthetic.privacy";

        public InventoryProviderDescriptor Descriptor { get; } = new(new("fixture-support"),
            ProviderPrivilegeRequirement.StandardUser, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2),
            ProviderNetworkUse.None, ProviderEvidenceOrigin.SyntheticFixture, [ProviderOperatingSystem.Windows11],
            new(new("support.fixture_status"), Source),
            [new(Decoy, EvidenceValueKind.Text, null, Source)]);

        public ValueTask<ProviderReadResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderReadResult.Collected(
                [NormalizedEvidence.Observed(Decoy, EvidenceValue.FromString(PrivateDecoy), sourceReference: Source)]));
    }
}
