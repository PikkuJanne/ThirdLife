using ThirdLife.Core.Jobs;
using ThirdLife.Reports.WorkshopReport;

namespace ThirdLife.Reports.RecipientGuide;

/// <summary>Saves an explicitly previewed printable recipient copy in its protected, separate local folder.</summary>
public sealed class RecipientGuideStore
{
    private readonly WorkshopReportStore _files;

    public RecipientGuideStore(string registeredDirectory) =>
        _files = new(registeredDirectory, Guid.NewGuid, printableRecipientGuide: true);

    public string Save(JobId jobId, RenderedRecipientGuide preview, CancellationToken cancellationToken = default) =>
        SaveArtifact(jobId, preview, cancellationToken).FileName;

    public WorkshopReportArtifact SaveArtifact(JobId jobId, RenderedRecipientGuide preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return _files.Write(jobId, preview.PrintableHtml, cancellationToken);
    }

    public void VerifyReceipt(WorkshopReportReceipt receipt, CancellationToken cancellationToken = default) =>
        _files.VerifyReceipt(receipt, cancellationToken);
}
