using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Finalization;
using ThirdLife.Persistence;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditArtifactScanTests
{
    [Fact]
    public async Task HandoverScansExactOneTimeTargetsAndReopensOnlyTypedEvidence()
    {
        using var disk = new AuditTestStore();
        var clock = TimeProvider.System;
        const string account = "WorkshopAccountOne";
        const string share = "WorkshopLocalShare";
        string jobId;
        await using (var workspace = Open())
        {
            await workspace.CreateJobAsync();
            await workspace.RefreshFinalizationAsync();
            Assert.False(workspace.CanScanArtifacts);
            Assert.Contains("Complete the sanitization record", workspace.ArtifactScanStatus, StringComparison.Ordinal);
            await workspace.RecordSanitizationAsync(VerifiedSample());
            jobId = workspace.JobId;
            InHandover(workspace, (window, view) =>
            {
                Named<Expander>(view, "WorkshopArtifactScanRegion").IsExpanded = true;
                var names = Named<TextBox>(view, "WorkshopAccountNames");
                var shares = Named<TextBox>(view, "WorkshopShareNames");
                Assert.Equal("Workshop local account names", AutomationProperties.GetName(names));
                Assert.False(names.IsUndoEnabled);
                names.Text = account;
                shares.Text = share;
                var scan = Named<Button>(view, "ScanWorkshopArtifactsButton");
                Assert.Equal("Scan workshop artifacts", ShellTestHost.ButtonPeer(scan).GetName());
                Invoke(window, scan);
                PumpUntil(window, () => workspace.Finalization?.ArtifactScans.Count == 1 && !workspace.IsBusy,
                    () => workspace.ArtifactScanStatus);

                Assert.Empty(names.Text);
                Assert.Empty(shares.Text);
                var result = Named<TextBlock>(view, "ArtifactScanResult");
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(result));
                Assert.Contains("Package staging folders", result.Text, StringComparison.Ordinal);
                Assert.Contains("No indication in the checked scope", result.Text, StringComparison.Ordinal);
                Assert.Contains("source was unavailable", result.Text, StringComparison.Ordinal);
                Assert.DoesNotContain(account, result.Text, StringComparison.Ordinal);
                Assert.DoesNotContain(share, result.Text, StringComparison.Ordinal);
                Named<ComboBox>(view, "FinalizationCheck").SelectedValue = FinalizationRuleId.Downloads;
                Assert.Contains("saved artifact indication still requires resolution",
                    Named<TextBlock>(view, "FinalizationFinding").Text, StringComparison.Ordinal);
            });
        }

        await using var reopened = Open();
        await reopened.OpenJobAsync(jobId);
        InHandover(reopened, (_, view) =>
        {
            Named<Expander>(view, "WorkshopArtifactScanRegion").IsExpanded = true;
            var scan = Assert.Single(reopened.Finalization!.ArtifactScans);
            Assert.Equal(ProvenanceKind.SyntheticFixture, scan.Origin);
            Assert.Equal(WorkshopArtifactScan.CurrentDetectorRevision, scan.DetectorRevision);
            Assert.Equal(WorkshopArtifactStatus.Present,
                Assert.Single(scan.Findings, item => item.Kind == WorkshopArtifactKind.PackageStagingResidue).Status);
            var text = Named<TextBlock>(view, "ArtifactScanResult").Text;
            Assert.DoesNotContain(account, text, StringComparison.Ordinal);
            Assert.DoesNotContain(share, text, StringComparison.Ordinal);
        });

        AuditWorkspace Open() => new(async token => await SqliteJobStore.OpenForTestingAsync(Path.Combine(disk.Root, "store"),
                SqliteJobStore.CurrentSchemaVersion, clock, null, () => 180_000, () => 19, token).ConfigureAwait(false),
            [AuditTestProvider.Available("artifact-scan-ui")], clock)
        {
            FixtureArtifactScan = (accountNames, shareNames, token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.Equal([account], accountNames);
                Assert.Equal([share], shareNames);
                IReadOnlyList<WorkshopArtifactFinding> findings = Enum.GetValues<WorkshopArtifactKind>()
                    .Select(kind => new WorkshopArtifactFinding(kind, kind switch
                    {
                        WorkshopArtifactKind.PackageStagingResidue => WorkshopArtifactStatus.Present,
                        WorkshopArtifactKind.ConfiguredLocalAccounts or WorkshopArtifactKind.ConfiguredSmbShares =>
                            WorkshopArtifactStatus.AbsentInCheckedScope,
                        _ => WorkshopArtifactStatus.Unavailable,
                    })).ToArray();
                return Task.FromResult(findings);
            },
        };
    }

    private static void InHandover(AuditWorkspace workspace, Action<Window, FrameworkElement> check)
    {
        ShellTestHost.Run(() =>
        {
            var window = new MainWindow(workspace) { Width = 640, Height = 420 };
            try
            {
                ShellTestHost.Show(window);
                window.ShowStage(ShellStage.Handover);
                PumpUntil(window, () => workspace.CanScanArtifacts, () => workspace.ArtifactScanStatus);
                check(window, Named<FrameworkElement>(window, "AuditView"));
            }
            finally
            {
                CloseWindow(window);
            }
        });
    }
}
