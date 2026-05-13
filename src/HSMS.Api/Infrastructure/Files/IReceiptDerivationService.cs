namespace HSMS.Api.Infrastructure.Files;

/// <summary>
/// Generates derived assets for a receipt:
/// - PDF receipts → first-page PNG preview (max-width 1200px) + 256px thumbnail.
/// - Image receipts → 256px thumbnail only (preview reuses original bytes).
/// </summary>
public interface IReceiptDerivationService
{
    Task<ReceiptDerivationResult> DeriveAsync(int receiptId, CancellationToken cancellationToken);
}

public sealed class ReceiptDerivationResult
{
    public bool Success { get; set; }
    public string? PreviewPath { get; set; }
    public int? PreviewWidth { get; set; }
    public int? PreviewHeight { get; set; }
    public long PreviewSizeBytes { get; set; }
    public string? ThumbnailPath { get; set; }
    public int? ThumbnailWidth { get; set; }
    public int? ThumbnailHeight { get; set; }
    public long ThumbnailSizeBytes { get; set; }
    public string? Error { get; set; }
    public bool NotApplicable { get; set; }
}
