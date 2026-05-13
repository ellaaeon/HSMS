namespace HSMS.Shared.Contracts;

public sealed class InstrumentCheckListItemDto
{
    public int InstrumentCheckId { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? SerialReference { get; set; }
    public string CheckedByName { get; set; } = string.Empty;
    public string? WitnessByName { get; set; }
    public string? Remarks { get; set; }
    public DateTime? WitnessApprovedAtUtc { get; set; }
    public int? WitnessApprovedBy { get; set; }
    public string? WitnessApprovedByUsername { get; set; }
    public int AttachmentCount { get; set; }
}

public sealed class InstrumentCheckCreateDto
{
    public string ItemName { get; set; } = string.Empty;
    public string? SerialReference { get; set; }
    public string CheckedByName { get; set; } = string.Empty;
    public string? WitnessByName { get; set; }
    public string? Remarks { get; set; }
}

public sealed class InstrumentCheckWitnessApproveDto
{
    public string? Remarks { get; set; }
    public string? ClientMachine { get; set; }
}

public sealed class InstrumentCheckAttachmentListItemDto
{
    public int AttachmentId { get; set; }
    public int InstrumentCheckId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}

