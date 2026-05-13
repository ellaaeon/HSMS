namespace HSMS.Shared.Contracts;

public sealed class QaTestListItemDto
{
    public int QaTestId { get; set; }
    public int SterilizationId { get; set; }
    public string CycleNo { get; set; } = string.Empty;
    public string SterilizerNo { get; set; } = string.Empty;
    public string TestType { get; set; } = string.Empty;
    public DateTime TestDateTimeUtc { get; set; }
    public string Result { get; set; } = string.Empty;
    public decimal? MeasuredValue { get; set; }
    public string? Unit { get; set; }
    public string? PerformedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public string? ApprovedByUsername { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovedRemarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class QaTestQueryDto
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? TestType { get; set; }
    public string? Result { get; set; }
    public bool? PendingApprovalOnly { get; set; }
    public int? SterilizationId { get; set; }
    public int Take { get; set; } = 200;
}

public sealed class QaTestApproveDto
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string? ClientMachine { get; set; }
}
