namespace HSMS.Application.Reporting.Datasets;

/// <summary>
/// Date-range BI log sheet (matches legacy BILogSheet.rdlc).
/// </summary>
public sealed class BiLogSheetReportData
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public string? SterilizationTypeFilter { get; set; }
    public List<BiLogSheetReportRow> Rows { get; set; } = [];
}

public sealed class BiLogSheetReportRow
{
    public DateTime CycleDateTimeUtc { get; set; }
    public string CycleNo { get; set; } = string.Empty;
    public string SterilizerNo { get; set; } = string.Empty;
    public string SterilizationType { get; set; } = string.Empty;
    public string? BiLotNo { get; set; }
    public DateTime? BiTimeInUtc { get; set; }
    public DateTime? BiTimeOutUtc { get; set; }
    public string? BiTimeInInitials { get; set; }
    public string? BiTimeOutInitials { get; set; }
    public string? BiResult { get; set; }
    public string? BiIncubatorTemp { get; set; }
    public string? BiProcessedResult24m { get; set; }
    public string? BiProcessedResult24h { get; set; }
    public string? BiControlResult24m { get; set; }
    public string? BiControlResult24h { get; set; }
    public string? OperatorName { get; set; }
    public string? Notes { get; set; }
}
