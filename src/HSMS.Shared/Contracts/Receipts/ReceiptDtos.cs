namespace HSMS.Shared.Contracts.Receipts;

public sealed class ReceiptListItemDto
{
    public int ReceiptId { get; set; }
    public int SterilizationId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CapturedAtUtc { get; set; }

    public string DerivationState { get; set; } = string.Empty;
    public bool HasPreview { get; set; }
    public bool HasThumbnail { get; set; }
}

public sealed class ReceiptDerivationKinds
{
    public const string PreviewPng = "preview_png";
    public const string ThumbnailPng = "thumbnail_png";
}
