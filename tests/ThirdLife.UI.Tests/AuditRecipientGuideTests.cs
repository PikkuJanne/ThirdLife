using System.IO;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using ThirdLife.Core.Evidence;
using ThirdLife.Core.Handover;
using ThirdLife.Core.Jobs;
using ThirdLife.Persistence;
using ThirdLife.Reports.RecipientGuide;
using ThirdLife.UI.Shell;
using static ThirdLife.UI.Tests.AuditAccessibilityTests;
using static ThirdLife.UI.Tests.AuditFinalizationTests;
using static ThirdLife.UI.Tests.AuditWorkflowTests;

namespace ThirdLife.UI.Tests;

[Collection(ShellWindowChecks.Name)]
public sealed class AuditRecipientGuideTests
{
    [Fact]
    public async Task StalePreviewCannotBeSavedAndFreshPrintableGuideContainsOnlyRecipientFacts()
    {
        using var disk = new AuditTestStore();
        var output = Path.Combine(disk.Root, "recipient-guides");
        var clock = TimeProvider.System;
        Task<SqliteJobStore> OpenStore(CancellationToken token = default) => SqliteJobStore.OpenForTestingAsync(
            Path.Combine(disk.Root, "store"), SqliteJobStore.CurrentSchemaVersion, clock, faultInjector: null,
            () => 180_000, () => 10, token);
        await using var workspace = new AuditWorkspace(async token => await OpenStore(token).ConfigureAwait(false),
            [AuditTestProvider.Available("recipient-guide")], clock, recipientGuideStore: new RecipientGuideStore(output));
        await workspace.CreateJobAsync();
        await workspace.RecordSanitizationAsync(VerifiedSample());
        var job = workspace.Job!.Job;
        var actor = new OperatorId("operator-guide-fixture");
        await using (var store = await OpenStore())
        {
            var handover = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
            await store.SaveHandoverModeAsync(job.JobId, handover, HandoverMode.Assisted, actor);
        }

        InHandover(workspace, (window, view) =>
        {
            PumpUntil(window, () => workspace.CanPreviewRecipientGuide);
            var preview = Named<Button>(view, "PreviewRecipientGuideButton");
            var printable = Named<Button>(view, "SaveRecipientGuideButton");
            var text = Named<TextBox>(view, "RecipientGuidePreviewBox");
            Assert.False(printable.IsVisible);
            Assert.Equal("Preview recipient guide", ShellTestHost.ButtonPeer(preview).GetName());
            Assert.True(preview.Focus());
            Invoke(window, preview);
            PumpUntil(window, () => workspace.CanSaveRecipientGuide && !workspace.IsBusy);
            Assert.Same(text, Keyboard.FocusedElement);
            Assert.Equal("Recipient guide preview", new TextBoxAutomationPeer(text).GetName());
            Assert.True(text.IsReadOnly);
            Assert.Contains("Backups. For you to choose later.", text.Text, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));

            Task.Run(async () =>
            {
                await using var store = await OpenStore();
                var handover = Assert.IsType<SavedRecipientHandover>(await store.LoadRecipientHandoverAsync(job.JobId));
                await store.SaveRecipientChoiceAsync(job.JobId, handover, RecipientSetupArea.Backup,
                    RecipientSetupChoice.GuidanceRequested, true, actor);
            }).GetAwaiter().GetResult();
            Invoke(window, printable);
            PumpUntil(window, () => !workspace.IsBusy && !workspace.CanSaveRecipientGuide);
            Assert.Contains("Preview the guide again", workspace.RecipientGuideStatus, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));

            Invoke(window, preview);
            PumpUntil(window, () => workspace.CanSaveRecipientGuide && !workspace.IsBusy);
            Assert.Contains("Backups. You asked for guidance.", text.Text, StringComparison.Ordinal);
            Invoke(window, printable);
            PumpUntil(window, () => !workspace.IsBusy && workspace.RecipientGuideStatus.Contains("Printable guide saved", StringComparison.Ordinal));
        });

        var file = Assert.Single(Directory.EnumerateFiles(output, "recipient-guide-*.html"));
        var html = await File.ReadAllTextAsync(file);
        var mode = RecipientHandover.Start(HandoverMode.Assisted)
            .RecordChoice(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested, recipientPresent: true);
        Assert.Equal(RecipientGuideRenderer.Render(new(mode, null, [], SyntheticExample: true)).PrintableHtml, html);
        Assert.Contains("<h1>Your Windows 11 setup guide</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Backups. You asked for guidance.", html, StringComparison.Ordinal);
        Assert.Contains("Example guide.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(job.JobId.Value, html, StringComparison.Ordinal);
        Assert.DoesNotContain(job.DeviceId.Value, html, StringComparison.Ordinal);
        Assert.DoesNotContain(actor.Value, html, StringComparison.Ordinal);
        Assert.DoesNotContain("WORKSHOP_RESTRICTED", html, StringComparison.Ordinal);
    }
}
