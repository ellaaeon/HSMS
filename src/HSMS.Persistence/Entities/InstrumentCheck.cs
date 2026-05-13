namespace HSMS.Persistence.Entities;

public sealed class InstrumentCheck
{
    public int InstrumentCheckId { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? SerialReference { get; set; }
    public string CheckedByName { get; set; } = string.Empty;
    public string? WitnessByName { get; set; }
    public string? Remarks { get; set; }

    public DateTime? WitnessApprovedAt { get; set; }
    public int? WitnessApprovedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
}

public sealed class InstrumentCheckAttachment
{
    public int AttachmentId { get; set; }
    public int InstrumentCheckId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTime CapturedAt { get; set; }
    public int? CapturedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

