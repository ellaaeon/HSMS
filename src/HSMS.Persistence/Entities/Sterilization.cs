namespace HSMS.Persistence.Entities;

public sealed class Sterilization
{
    public int SterilizationId { get; set; }
    public string CycleNo { get; set; } = string.Empty;
    public int SterilizerId { get; set; }
    public string SterilizationType { get; set; } = string.Empty;
    public string? CycleProgram { get; set; }
    public DateTime CycleDateTime { get; set; }
    public DateTime? CycleTimeIn { get; set; }
    public DateTime? CycleTimeOut { get; set; }

    /// <summary>UTC when this row was first inserted (load registration).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Account that created/registered this load (FK to tbl_account_login.account_id).</summary>
    public int? CreatedBy { get; set; }

    /// <summary>UTC when this row was last updated.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Account that last updated this load (FK to tbl_account_login.account_id).</summary>
    public int? UpdatedBy { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public decimal? TemperatureC { get; set; }
    public decimal? TemperatureInC { get; set; }
    public decimal? TemperatureOutC { get; set; }
    public decimal? Pressure { get; set; }
    public int? ExposureTimeMinutes { get; set; }
    public string? BiResult { get; set; }
    /// <summary>UTC when <see cref="BiResult"/> was last changed.</summary>
    public DateTime? BiResultUpdatedAt { get; set; }
    public string? BiLotNo { get; set; }
    public string? BiStripNo { get; set; }
    public DateTime? BiTimeIn { get; set; }
    public DateTime? BiTimeOut { get; set; }
    public DateTime? BiTimeCut { get; set; }
    /// <summary>Periodic QA: routine daily monitoring (paper form checkbox).</summary>
    public bool? BiDaily { get; set; }
    /// <summary>Incubator reading (e.g. 60°C +12).</summary>
    public string? BiIncubatorTemp { get; set; }
    /// <summary>Checkbox to confirm incubator reading was taken.</summary>
    public bool? BiIncubatorChecked { get; set; }
    public string? BiTimeInInitials { get; set; }
    public string? BiTimeOutInitials { get; set; }
    /// <summary>BI processed sample: '+' or '-' at 24 minutes.</summary>
    public string? BiProcessedResult24m { get; set; }
    /// <summary>Optional numeric reading for BI processed sample at 24 minutes.</summary>
    public int? BiProcessedValue24m { get; set; }
    /// <summary>BI processed sample: '+' or '-' at 24 hours.</summary>
    public string? BiProcessedResult24h { get; set; }
    /// <summary>Optional numeric reading for BI processed sample at 24 hours.</summary>
    public int? BiProcessedValue24h { get; set; }
    /// <summary>BI control: '+' or '-' at 24 minutes.</summary>
    public string? BiControlResult24m { get; set; }
    /// <summary>Optional numeric reading for BI control at 24 minutes.</summary>
    public int? BiControlValue24m { get; set; }
    /// <summary>BI control: '+' or '-' at 24 hours.</summary>
    public string? BiControlResult24h { get; set; }
    /// <summary>Optional numeric reading for BI control at 24 hours.</summary>
    public int? BiControlValue24h { get; set; }
    public int? LoadQty { get; set; }
    public string CycleStatus { get; set; } = "Draft";
    public int? DoctorRoomId { get; set; }
    public bool Implants { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<SterilizationItem> Items { get; set; } = new List<SterilizationItem>();
    public ICollection<CycleReceipt> Receipts { get; set; } = new List<CycleReceipt>();
    public ICollection<QaTest> QaTests { get; set; } = new List<QaTest>();
}

public sealed class SterilizationItem
{
    public int SterilizationItemId { get; set; }
    public int SterilizationId { get; set; }
    public int? DeptItemId { get; set; }
    public string? DepartmentName { get; set; }
    public string? DoctorOrRoom { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Pcs { get; set; } = 1;
    public int Qty { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class CycleReceipt
{
    public int ReceiptId { get; set; }
    public int SterilizationId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTime CapturedAt { get; set; }
}

public sealed class QaTest
{
    public int QaTestId { get; set; }
    public int SterilizationId { get; set; }
    public string TestType { get; set; } = string.Empty;
    public DateTime TestDateTime { get; set; }
    public string Result { get; set; } = string.Empty;
    public decimal? MeasuredValue { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedRemarks { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
