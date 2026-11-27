using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using ThirdLife.Core.Handover;

namespace ThirdLife.Reports.RecipientGuide;

public enum RecipientGuideCapability
{
    WebBrowsing = 1,
    DocumentEditing,
    PdfReading,
    VideoCalling,
}

public enum RecipientGuideSoftwareState
{
    AwaitingVerification = 1,
    VerifiedInstalled,
}

public enum RecipientGuideProgram
{
    NotepadPlusPlus = 1,
}

public sealed record RecipientGuideSoftware(RecipientGuideCapability Capability, RecipientGuideSoftwareState State,
    RecipientGuideProgram? Program = null, string? VerifiedVersion = null);

public sealed record RecipientGuideContent(RecipientHandover Handover, DateOnly? PreparedOn,
    IReadOnlyList<RecipientGuideSoftware> Software, bool BackupRestoreVerified = false, bool SyntheticExample = false,
    string? PublicOrganization = null, string? PublicSupportContact = null);

public sealed class RenderedRecipientGuide
{
    internal RenderedRecipientGuide(string previewText, string printableHtml)
    {
        PreviewText = previewText;
        PrintableHtml = printableHtml;
    }

    public string PreviewText { get; }
    public string PrintableHtml { get; }
}

public static class RecipientGuideRenderer
{
    private const string Title = "Your Windows 11 setup guide";
    private const int MaximumSoftwareItems = 4;
    private const int MaximumPublicText = 128;
    private const string DocumentStart = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
        <title>Your Windows 11 setup guide</title>
        <style>
        :root { color-scheme: light dark; }
        body { margin: 0 auto; max-width: 64ch; padding: 1rem; font: 1rem/1.5 system-ui, sans-serif; overflow-wrap: anywhere; }
        h1, h2 { line-height: 1.2; }
        h2 { margin-block-start: 1.5em; }
        @media print { body { max-width: none; padding: 0; } h2 { break-after: avoid; } }
        </style>
        </head>
        <body>
        <main>
        <h1>Your Windows 11 setup guide</h1>
        """;

    public static RenderedRecipientGuide Render(RecipientGuideContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Handover);
        ArgumentNullException.ThrowIfNull(content.Software);
        if (content.Software.Count > MaximumSoftwareItems ||
            content.Software.Any(item => item is null || !IsValidSoftware(item)) ||
            content.Software.Select(item => item.Capability).Distinct().Count() != content.Software.Count)
        {
            throw new ArgumentException("The guide requires bounded, recognized software results.", nameof(content));
        }

        var organization = PublicText(content.PublicOrganization);
        var contact = PublicText(content.PublicSupportContact);
        var sections = GetSections(content, organization, contact);
        var text = new StringBuilder().Append(Title).Append('\n');
        var html = new StringBuilder().Append(DocumentStart.ReplaceLineEndings("\n")).Append('\n');
        foreach (var section in sections)
        {
            text.Append('\n').Append(section.Heading).Append('\n');
            html.Append("<section>\n<h2>").Append(section.Heading).Append("</h2>\n");
            if (section.IsList)
            {
                html.Append("<ul>\n");
            }

            foreach (var line in section.Lines)
            {
                if (section.IsList)
                {
                    text.Append("- ");
                }

                text.Append(line).Append('\n');
                html.Append(section.IsList ? "<li>" : "<p>").Append(HtmlEncoder.Default.Encode(line))
                    .Append(section.IsList ? "</li>\n" : "</p>\n");
            }

            if (section.IsList)
            {
                html.Append("</ul>\n");
            }

            html.Append("</section>\n");
        }

        html.Append("</main>\n</body>\n</html>\n");
        return new(text.ToString(), html.ToString());
    }

    private static IReadOnlyList<GuideSection> GetSections(RecipientGuideContent content, string? organization, string? contact)
    {
        var preparation = content.PreparedOn is { } date
            ? $"Preparation date {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
            : "A preparation date is unavailable.";
        var mode = content.Handover.Mode == HandoverMode.Assisted
            ? "In assisted handover, you can ask the workshop to go through these choices with you."
            : "In sealed handover, you make the personal choices below after you receive the computer.";
        var software = content.Software.Count == 0
            ? ["A verified software list is unavailable. Ask the workshop which programs were checked."]
            : content.Software.OrderBy(item => item.Capability).Select(item => DescribeSoftware(item, content.SyntheticExample)).ToArray();
        var support = contact is null ? "Ask the workshop that prepared your computer for its support contact."
            : $"Support contact {contact}.";
        var details = organization is null ? new[] { support } : [organization, support];

        return
        [
            new("Getting started", content.SyntheticExample
                ? [preparation, mode, "Example guide. The example does not describe this computer."]
                : [preparation, mode]),
            new("Programs checked by the workshop", software, IsList: content.Software.Count > 0),
            new("Windows and app updates",
                ["Open Settings, then Windows Update, to check for Windows updates. Save your work before restarting.",
                 "Follow each app's built-in update instructions or get updates from the app's publisher."]),
            new("Where to find your files",
                ["Open File Explorer. Use Documents for your work, Pictures for images, and Downloads for files you download."]),
            new("Back up and restore your files", content.BackupRestoreVerified && !content.SyntheticExample
                ? ["The workshop confirmed a backup by restoring a sample file. Keep your backup destination and recovery information under your control."]
                : ["Backup setup is still unconfirmed. Choose a backup for your files and restore a sample file to check it works."]),
            new("Accessibility settings",
                ["Open Settings, then Accessibility. You can change text size, contrast, keyboard, pointer, sound and captions there."]),
            new("Your setup choices", content.Handover.Decisions.Select(DescribeChoice).ToArray(), IsList: true),
            new("Help and support", details),
            new("What this guide covers",
                ["The guide covers the checks listed here. Try the features you rely on and ask the workshop about anything still unconfirmed."]),
        ];
    }

    private static string DescribeChoice(RecipientSetupDecision decision)
    {
        var area = decision.Area switch
        {
            RecipientSetupArea.PersonalAccount => "Personal accounts",
            RecipientSetupArea.Backup => "Backups",
            RecipientSetupArea.RecoveryMaterial => "Recovery information",
            RecipientSetupArea.Browser => "Browser preferences",
            RecipientSetupArea.Accessibility => "Accessibility preferences",
            RecipientSetupArea.OtherPreferences => "Other personal preferences",
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };
        var choice = decision.Choice switch
        {
            RecipientSetupChoice.Deferred => "For you to choose later.",
            RecipientSetupChoice.GuidanceRequested => "You asked for guidance. Setup may still be needed.",
            RecipientSetupChoice.Declined => "You chose to skip the workshop's guidance.",
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };
        return $"{area}. {choice}";
    }

    private static string DescribeSoftware(RecipientGuideSoftware item, bool synthetic)
    {
        var name = item.Capability switch
        {
            RecipientGuideCapability.WebBrowsing => "Web browsing",
            RecipientGuideCapability.DocumentEditing => "Editing text",
            RecipientGuideCapability.PdfReading => "Reading PDF files",
            RecipientGuideCapability.VideoCalling => "Video calls",
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        if (item.State == RecipientGuideSoftwareState.AwaitingVerification)
        {
            return $"{name}. The workshop still needs to verify this.";
        }

        var installed = item.Program switch
        {
            RecipientGuideProgram.NotepadPlusPlus => $"Notepad++ version {item.VerifiedVersion}.",
            _ => "The workshop checked installation.",
        };
        return synthetic ? $"{name}. Example result. {installed}" : $"{name}. {installed}";
    }

    private static bool IsValidSoftware(RecipientGuideSoftware item) =>
        Enum.IsDefined(item.Capability) && Enum.IsDefined(item.State) &&
        (item.Program is null || item is { Program: RecipientGuideProgram.NotepadPlusPlus, Capability: RecipientGuideCapability.DocumentEditing }) &&
        (item.State == RecipientGuideSoftwareState.AwaitingVerification
            ? item.Program is null && item.VerifiedVersion is null
            : item.Program is null && item.VerifiedVersion is null ||
                item.VerifiedVersion is { Length: > 0 and <= 24 } value && Version.TryParse(value, out var parsed) &&
                parsed.ToString() == value);

    private static string? PublicText(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) { return null; }
        if (trimmed.Length > MaximumPublicText || trimmed.Any(character => char.IsControl(character) || char.IsSurrogate(character)))
        {
            throw new ArgumentException("Use a short, single-line public support value.", nameof(value));
        }

        return trimmed;
    }

    private sealed record GuideSection(string Heading, IReadOnlyList<string> Lines, bool IsList = false);
}
