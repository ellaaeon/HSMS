namespace HSMS.Application.Reporting.Datasets;

/// <summary>
/// Strongly-typed dataset for the LoadRecord (single-cycle) report. Maps to the legacy LoadRecord.rdlc layout.
/// Page 2 carries optional receipt images.
/// </summary>
public sealed class LoadRecordReportData
{
    public LoadRecordHeader Header { get; set; } = new();
    public List<LoadRecordItemRow> Items { get; set; } = [];
    public List<LoadRecordReceiptImage> ReceiptImages { get; set; } = [];
}

public sealed class LoadRecordHeader
{
    public string CycleNo { get; set; } = string.Empty;
    public string SterilizerNo { get; set; } = string.Empty;
    public string SterilizerModel { get; set; } = string.Empty;
    public string SterilizationType { get; set; } = string.Empty;
    public string CycleProgram { get; set; } = string.Empty;
    public DateTime CycleDateTimeUtc { get; set; }
    public DateTime? CycleTimeInUtc { get; set; }
    public DateTime? CycleTimeOutUtc { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public decimal? TemperatureC { get; set; }
    public decimal? Pressure { get; set; }
    public int? ExposureTimeMinutes { get; set; }
    public string? BiLotNo { get; set; }
    public string? BiResult { get; set; }
    public string CycleStatus { get; set; } = string.Empty;
    public string? DoctorOrRoom { get; set; }
    public bool Implants { get; set; }
    public string? Notes { get; set; }
    public int TotalPcs { get; set; }
    public int TotalQty { get; set; }
}

public sealed class LoadRecordItemRow
{
    public int LineNo { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DoctorOrRoom { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Pcs { get; set; }
    public int Qty { get; set; }
}

public sealed class LoadRecordReceiptImage
{
    public int ReceiptId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; }

    /// <summary>PNG/JPG bytes ready to render. For PDF receipts this is the derived preview PNG.</summary>
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
}
