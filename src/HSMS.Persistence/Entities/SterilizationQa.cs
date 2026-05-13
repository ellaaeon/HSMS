namespace HSMS.Persistence.Entities;

public sealed class SterilizationQaRecord
{
    public long RecordId { get; set; }
    public string Category { get; set; } = "BowieDick";
    public int? SterilizationId { get; set; }
    public int? SterilizerId { get; set; }
    public DateTime TestDateTimeUtc { get; set; }
    public string? Department { get; set; }
    public string? Technician { get; set; }
    public string Status { get; set; } = "Draft";
    public string? ResultLabel { get; set; }
    public string? Summary { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }

    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
    public ICollection<SterilizationQaStatusEvent> StatusEvents { get; set; } = new List<SterilizationQaStatusEvent>();
    public ICollection<SterilizationQaAttachment> Attachments { get; set; } = new List<SterilizationQaAttachment>();
}

public sealed class SterilizationQaStatusEvent
{
    public long EventId { get; set; }
    public long RecordId { get; set; }
    public DateTime EventAtUtc { get; set; }
    public int? ActorAccountId { get; set; }
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public string? Comment { get; set; }
}

public sealed class SterilizationQaAttachment
{
    public long AttachmentId { get; set; }
    public long RecordId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public int? CapturedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

