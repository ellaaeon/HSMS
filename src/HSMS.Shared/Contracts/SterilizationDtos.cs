namespace HSMS.Shared.Contracts;

public sealed class SterilizationSearchItemDto
{
    public int SterilizationId { get; set; }
    public string CycleNo { get; set; } = string.Empty;

    /// <summary>Cycle program / purpose (e.g. Instruments, Bowie-Dick, Warm Up). From <c>tbl_sterilization.cycle_program</c>.</summary>
    public string? CycleProgram { get; set; }
    public DateTime CycleDateTimeUtc { get; set; }
    public string SterilizerNo { get; set; } = string.Empty;
    public string CycleStatus { get; set; } = string.Empty;

    /// <summary>Account that registered/created this load (tbl_sterilization.created_by). Null for legacy rows.</summary>
    public int? CreatedByAccountId { get; set; }

    /// <summary>Operator name captured at registration time.</summary>
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>Total Pcs across all line items (sum of <c>tbl_str_items.pcs</c>).</summary>
    public int TotalPcs { get; set; }

    /// <summary>Total Qty across all line items (sum of <c>tbl_str_items.qty</c>).</summary>
    public int TotalQty { get; set; }

    /// <summary>Optional legacy/machine field <c>cycle_time_in</c> (UTC).</summary>
    public DateTime? CycleTimeInUtc { get; set; }

    /// <summary>UTC when the load was registered (row <c>created_at</c>). Shown as &quot;Cycle start&quot; in Load Records.</summary>
    public DateTime RegisteredAtUtc { get; set; }

    /// <summary>Cycle end (stored as <c>cycle_time_out</c>, UTC). User-editable from Load Records grid.</summary>
    public DateTime? CycleTimeOutUtc { get; set; }

    /// <summary>Optimistic concurrency token for partial updates (e.g. cycle end).</summary>
    public string RowVersion { get; set; } = string.Empty;
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
    /// <summary>Account that registered/created this load (tbl_sterilization.created_by). Null for legacy rows.</summary>
    public int? CreatedByAccountId { get; set; }
    /// <summary>UTC when <see cref="SterilizationUpsertDto.BiResult"/> was last changed.</summary>
    public DateTime? BiResultUpdatedAtUtc { get; set; }
    public List<ReceiptMetadataDto> Receipts { get; set; } = [];
}

/// <summary>Patch cycle end time only (Load Records grid). Stores <c>cycle_time_out</c> in UTC.</summary>
public sealed class SterilizationCycleEndPatchDto
{
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>null clears cycle end.</summary>
    public DateTime? CycleEndUtc { get; set; }
    public string? ClientMachine { get; set; }
}

/// <summary>Allowed status values editable from Load Records (matches Register load).</summary>
public static class LoadRecordCycleStatuses
{
    public const string Draft = "Draft";
    public const string Completed = "Completed";
    public const string Voided = "Voided";

    public static readonly string[] All = [Draft, Completed, Voided];

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        foreach (var a in All)
        {
            if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }

        return null;
    }
}

/// <summary>Patch <c>cycle_status</c> only (Load Records grid).</summary>
public sealed class SterilizationCycleStatusPatchDto
{
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>One of Draft, Completed, Voided.</summary>
    public string CycleStatus { get; set; } = string.Empty;
    public string? ClientMachine { get; set; }
}

public sealed class ReceiptMetadataDto
{
    public int ReceiptId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}
