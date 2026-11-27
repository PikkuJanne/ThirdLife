using ThirdLife.Core.Handover;
using ThirdLife.Reports.RecipientGuide;

namespace ThirdLife.Reports.Tests;

public sealed class RecipientGuideRendererTests
{
    private static readonly DateOnly PreparedOn = new(2026, 11, 27);

    [Theory]
    [InlineData(HandoverMode.Assisted, "In assisted handover")]
    [InlineData(HandoverMode.Sealed, "In sealed handover")]
    public void PrintableGuideAndPreviewUseTheSameReadingOrder(HandoverMode mode, string modeText)
    {
        var input = new RecipientGuideContent(RecipientHandover.Start(mode), PreparedOn, []);
        var guide = RecipientGuideRenderer.Render(input);
        var repeated = RecipientGuideRenderer.Render(input);
        Assert.Equal(guide.PreviewText, repeated.PreviewText);
        Assert.Equal(guide.PrintableHtml, repeated.PrintableHtml);
        Assert.Contains(modeText, guide.PreviewText, StringComparison.Ordinal);
        Assert.Contains(modeText, guide.PrintableHtml, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", guide.PrintableHtml, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">", guide.PrintableHtml,
            StringComparison.Ordinal);
        Assert.Contains("<h1>Your Windows 11 setup guide</h1>", guide.PrintableHtml, StringComparison.Ordinal);
        string[] sections =
        [
            "Getting started", "Programs checked by the workshop", "Windows and app updates", "Where to find your files",
            "Back up and restore your files", "Accessibility settings", "Your setup choices", "Help and support",
            "What this guide covers",
        ];
        var position = -1;
        var previewPosition = -1;
        foreach (var section in sections)
        {
            var next = guide.PrintableHtml.IndexOf($"<h2>{section}</h2>", StringComparison.Ordinal);
            var nextPreview = guide.PreviewText.IndexOf($"\n{section}\n", StringComparison.Ordinal);
            Assert.True(next > position);
            Assert.True(nextPreview > previewPosition);
            position = next;
            previewPosition = nextPreview;
        }

        Assert.Contains("Preparation date 2026-11-27.", guide.PreviewText, StringComparison.Ordinal);
        Assert.Contains("Ask the workshop that prepared your computer for its support contact.", guide.PreviewText,
            StringComparison.Ordinal);
        Assert.Contains("Backup setup is still unconfirmed.", guide.PreviewText, StringComparison.Ordinal);
        Assert.Contains("Personal accounts. For you to choose later.", guide.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", guide.PrintableHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", guide.PrintableHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuideDistinguishesVerifiedSoftwareAndRestoreFromGuidance()
    {
        var handover = RecipientHandover.Start(HandoverMode.Assisted)
            .RecordChoice(RecipientSetupArea.Backup, RecipientSetupChoice.GuidanceRequested, recipientPresent: true)
            .RecordChoice(RecipientSetupArea.Browser, RecipientSetupChoice.Declined, recipientPresent: true);
        var content = new RecipientGuideContent(handover, PreparedOn,
            [
                new(RecipientGuideCapability.WebBrowsing, RecipientGuideSoftwareState.AwaitingVerification),
                new(RecipientGuideCapability.DocumentEditing, RecipientGuideSoftwareState.VerifiedInstalled,
                    RecipientGuideProgram.NotepadPlusPlus, "8.9.9.1"),
            ], BackupRestoreVerified: true);
        var guide = RecipientGuideRenderer.Render(content).PreviewText;

        Assert.Contains("Web browsing. The workshop still needs to verify this.", guide, StringComparison.Ordinal);
        Assert.Contains("Editing text. Notepad++ version 8.9.9.1.", guide, StringComparison.Ordinal);
        Assert.Contains("The workshop confirmed a backup by restoring a sample file.", guide, StringComparison.Ordinal);
        Assert.Contains("Backups. You asked for guidance. Setup may still be needed.", guide, StringComparison.Ordinal);
        Assert.Contains("Browser preferences. You chose to skip the workshop's guidance.", guide, StringComparison.Ordinal);
        var example = RecipientGuideRenderer.Render(content with { SyntheticExample = true }).PreviewText;
        Assert.Contains("Example guide.", example, StringComparison.Ordinal);
        Assert.Contains("Editing text. Example result. Notepad++", example, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmed a backup", example, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideKeepsOnlyReviewedFieldsAndTreatsPublicContactAsText()
    {
        var content = new RecipientGuideContent(RecipientHandover.Start(HandoverMode.Sealed), null, [],
            PublicOrganization: "<script>brand</script>", PublicSupportContact: "<a href='javascript:void(0)'>support</a>");
        var guide = RecipientGuideRenderer.Render(content);
        Assert.Contains("&lt;script&gt;brand&lt;/script&gt;", guide.PrintableHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;a href=", guide.PrintableHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", guide.PrintableHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href", guide.PrintableHtml, StringComparison.Ordinal);
        Assert.Contains("A preparation date is unavailable.", guide.PreviewText, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => RecipientGuideRenderer.Render(content with { PublicSupportContact = new string('a', 129) }));
        Assert.Throws<ArgumentException>(() => RecipientGuideRenderer.Render(content with { PublicSupportContact = "extra\nline" }));
        Assert.Throws<ArgumentException>(() => RecipientGuideRenderer.Render(content with
        {
            Software =
            [
                new(RecipientGuideCapability.DocumentEditing, RecipientGuideSoftwareState.VerifiedInstalled,
                    RecipientGuideProgram.NotepadPlusPlus, @"C:\private\installer.exe"),
            ],
        }));
    }
}
