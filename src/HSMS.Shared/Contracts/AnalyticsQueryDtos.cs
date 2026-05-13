using System.Text.Json.Serialization;

namespace HSMS.Shared.Contracts;

/// <summary>
/// Strongly-typed analytics filter used for dashboard, drill-down, exports, and presets.
/// All fields are optional; they combine conjunctively (AND).
/// </summary>
public sealed class AnalyticsFilterDto
{
    /// <summary>UTC range filter applied to Sterilization.CreatedAt (per current system behavior).</summary>
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    public int? SterilizerId { get; set; }
    public string? SterilizationType { get; set; }

    /// <summary>Cycle status (e.g. Draft/Completed/Voided) using <see cref="LoadRecordCycleStatuses.Normalize"/> semantics.</summary>
    public string? LoadStatus { get; set; }

    public bool? Implants { get; set; }

    public string? BiLotNo { get; set; }
    public string? BiResult { get; set; }

    /// <summary>Department name on item lines (tbl_str_items.department_name).</summary>
    public string? Department { get; set; }

    /// <summary>Doctor/room master key on sterilization header.</summary>
    public int? DoctorRoomId { get; set; }

    /// <summary>Cycle program code/name stored on header (tbl_sterilization.cycle_program).</summary>
    public string? CycleProgram { get; set; }

    /// <summary>Exact operator name match on header.</summary>
    public string? OperatorName { get; set; }

    /// <summary>
    /// Optional structured QA filter. When set, analytics/drilldown should only include sterilizations
    /// with QA tests matching the requested state.
    /// </summary>
    public AnalyticsQaStatusFilterDto? QaStatus { get; set; }

    /// <summary>
    /// Global free-text search applied across common fields (cycle no, operator, notes, BI lot, item name, dept).
    /// This is supplemental to structured filters.
    /// </summary>
    public string? GlobalSearch { get; set; }
}

public sealed class AnalyticsQaStatusFilterDto
{
    /// <summary>When true, require at least one QA test row for the sterilization.</summary>
    public bool RequireAnyQaTest { get; set; }

    /// <summary>When true, only include sterilizations with at least one pending-approval QA test (ApprovedAt null).</summary>
    public bool PendingApprovalOnly { get; set; }

    /// <summary>Optional test type filter (e.g. Leak, BowieDick).</summary>
    public string? TestType { get; set; }

    /// <summary>Optional result filter (e.g. Pass, Fail).</summary>
    public string? Result { get; set; }
}

/// <summary>Dashboard query wrapper for analytics services.</summary>
public sealed class AnalyticsDashboardQueryDto
{
    public AnalyticsFilterDto Filter { get; set; } = new();

    /// <summary>
    /// Optional compare window. When set, the service should populate compare-to-prior-period fields.
    /// </summary>
    public DateTime? CompareFromUtc { get; set; }
    public DateTime? CompareToUtc { get; set; }

    /// <summary>
    /// Performance safeguard: maximum groups per breakdown before applying TopN + Others server-side.
    /// </summary>
    public int? TopN { get; set; }
}

/// <summary>Structured drill-down request used when clicking charts/breakdowns.</summary>
public sealed class AnalyticsDrilldownRequestDto
{
    public AnalyticsFilterDto Filter { get; set; } = new();

    /// <summary>Optional hint for UI breadcrumb / context.</summary>
    public AnalyticsDrilldownContextDto? Context { get; set; }
}

public sealed class AnalyticsDrilldownContextDto
{
    public string Title { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BreakdownKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BreakdownValue { get; set; }
}

