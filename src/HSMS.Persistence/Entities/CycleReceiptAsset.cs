namespace HSMS.Persistence.Entities;

public sealed class CycleReceiptAsset
{
    public int AssetId { get; set; }
    public int ReceiptId { get; set; }

    /// <summary>One of "preview_png" or "thumbnail_png".</summary>
    public string AssetKind { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

public static class CycleReceiptAssetKinds
{
    public const string PreviewPng = "preview_png";
    public const string ThumbnailPng = "thumbnail_png";
}

public sealed class CycleReceiptDerivationState
{
    public int ReceiptId { get; set; }

    /// <summary>Pending, Running, Completed, Failed, NotApplicable (already an image).</summary>
    public string State { get; set; } = "Pending";

    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public static class CycleReceiptDerivationStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string NotApplicable = "NotApplicable";
}
