namespace HSMS.Shared.Contracts;

public sealed class SterilizationQaTimelineDto
{
    public long RecordId { get; set; }
    public bool IsLegacy { get; set; }
    public List<SterilizationQaTimelineEventDto> Events { get; set; } = [];
}

public sealed class SterilizationQaAttachmentListItemDto
{
    public long AttachmentId { get; set; }
    public long RecordId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string FilePath { get; set; } = string.Empty; // local-secure storage path on this workstation
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SterilizationQaAttachmentAddDto
{
    public string SourceFilePath { get; set; } = string.Empty; // path user selected
    public string? ClientMachine { get; set; }
}

