namespace HSMS.Application.Reporting.Datasets;

/// <summary>
/// Shared dataset for Leak and Bowie-Dick reports. The render engine flips header text + applicable result fields
/// based on <see cref="TestType"/>.
/// </summary>
public sealed class QaTestReportData
{
    public string TestType { get; set; } = string.Empty;
    public int QaTestId { get; set; }
    public string CycleNo { get; set; } = string.Empty;
    public string SterilizerNo { get; set; } = string.Empty;
    public DateTime TestDateTimeUtc { get; set; }
    public string Result { get; set; } = string.Empty;
    public decimal? MeasuredValue { get; set; }
    public string? Unit { get; set; }
    public string? PerformedBy { get; set; }
    public string? Notes { get; set; }
    public string? SupervisorRemarks { get; set; }
    public string? ApprovedByUsername { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string ApprovalStatus { get; set; } = "Pending";
}
