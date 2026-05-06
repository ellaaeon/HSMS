namespace HSMS.Shared.Contracts;

public sealed class SterilizationSearchItemDto
{
    public int SterilizationId { get; set; }
    public string CycleNo { get; set; } = string.Empty;
    public DateTime CycleDateTimeUtc { get; set; }
    public string SterilizerNo { get; set; } = string.Empty;
    public string CycleStatus { get; set; } = string.Empty;
}

public sealed class SterilizationItemDto
{
    public int? SterilizationItemId { get; set; }
    public int? DeptItemId { get; set; }
    public string? DepartmentName { get; set; }
    public string? DoctorOrRoom { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Pcs { get; set; } = 1;
    public int Qty { get; set; }
    public string? RowVersion { get; set; }
}

public class SterilizationUpsertDto
{
    public string? RowVersion { get; set; }
    public string CycleNo { get; set; } = string.Empty;
    public int SterilizerId { get; set; }
    public string SterilizationType { get; set; } = string.Empty;
    /// <summary>Load cycle purpose: Instruments, Bowie Dick, leak test, warm up, etc.</summary>
    public string? CycleProgram { get; set; }
    public DateTime CycleDateTimeUtc { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public decimal? TemperatureC { get; set; }
    public decimal? Pressure { get; set; }
    /// <summary>Exposure duration in minutes (steam sterilizer cycle).</summary>
    public int? ExposureTimeMinutes { get; set; }
    public string? BiLotNo { get; set; }
    public string? BiResult { get; set; }
    public string CycleStatus { get; set; } = "Draft";
    public int? DoctorRoomId { get; set; }
    public bool Implants { get; set; }
    public string? Notes { get; set; }
    public string? ClientMachine { get; set; }
    public List<SterilizationItemDto> Items { get; set; } = [];
}

public sealed class SterilizationDetailsDto : SterilizationUpsertDto
{
    public int SterilizationId { get; set; }
    /// <summary>UTC when <see cref="SterilizationUpsertDto.BiResult"/> was last changed.</summary>
    public DateTime? BiResultUpdatedAtUtc { get; set; }
    public List<ReceiptMetadataDto> Receipts { get; set; } = [];
}

public sealed class ReceiptMetadataDto
{
    public int ReceiptId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}
