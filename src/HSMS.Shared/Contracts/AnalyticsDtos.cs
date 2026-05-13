namespace HSMS.Shared.Contracts;

public sealed class SterilizationAnalyticsDto
{
    public int TotalLoads { get; set; }
    public int DraftLoads { get; set; }
    public int CompletedLoads { get; set; }
    public int VoidedLoads { get; set; }

    public int TotalPcs { get; set; }
    public int TotalQty { get; set; }

    /// <summary>Top operators (loads/pcs/qty).</summary>
    public List<AnalyticsOperatorSummaryRowDto> ByOperator { get; set; } = [];

    /// <summary>Top sterilizers by number of loads.</summary>
    public List<AnalyticsSterilizerSummaryRowDto> BySterilizer { get; set; } = [];

    /// <summary>Daily trend (UTC calendar day).</summary>
    public List<AnalyticsDaySummaryRowDto> ByDay { get; set; } = [];

    /// <summary>Same-length window immediately before the main range (sterilization loads).</summary>
    public AnalyticsPeriodCompareDto? ComparePriorPeriod { get; set; }

    public List<AnalyticsBreakdownRowDto> BySterilizationType { get; set; } = [];

    public List<AnalyticsBreakdownRowDto> ByBiResult { get; set; } = [];

    /// <summary>Item lines grouped by department on loads in range.</summary>
    public List<AnalyticsBreakdownRowDto> ByDepartment { get; set; } = [];

    public List<AnalyticsBreakdownRowDto> TopItemsByQty { get; set; } = [];

    /// <summary>Loads aggregated by sterilization header doctor/room (master lookup).</summary>
    public List<AnalyticsBreakdownRowDto> ByDoctorRoom { get; set; } = [];

    /// <summary>Periodic QA BI log sheet (paper form) completeness on sterilization rows (same CreatedAt filter).</summary>
    public AnalyticsBiLogPaperSummaryDto? BiLogPaper { get; set; }

    public AnalyticsQaSummaryDto? QaTests { get; set; }

    public AnalyticsInstrumentSummaryDto? InstrumentChecks { get; set; }
}

/// <summary>BI log sheet paper-form fields summarized for loads matching analytics filters.</summary>
public sealed class AnalyticsBiLogPaperSummaryDto
{
    public int LoadsInScope { get; set; }

    public int LotNoCaptured { get; set; }
    public int RoutineDailyMarked { get; set; }
    public int BiTimeInCaptured { get; set; }
    public int BiTimeOutCaptured { get; set; }
    public int BiTimesBothCaptured { get; set; }
    public int IncubatorReadingChecked { get; set; }

    public List<AnalyticsBreakdownRowDto> ProcessedSample24mSign { get; set; } = [];

    public List<AnalyticsBreakdownRowDto> ProcessedSample24hSign { get; set; } = [];

    public List<AnalyticsBreakdownRowDto> ControlSample24mSign { get; set; } = [];

    public List<AnalyticsBreakdownRowDto> ControlSample24hSign { get; set; } = [];
}

public sealed class AnalyticsPeriodCompareDto
{
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public int TotalLoads { get; set; }
    public int CompletedLoads { get; set; }
    public int DraftLoads { get; set; }
    public int VoidedLoads { get; set; }
    public int TotalPcs { get; set; }
    public int TotalQty { get; set; }
}

public sealed class AnalyticsBreakdownRowDto
{
    /// <summary>
    /// Optional structured identifier for drill-down (e.g., DoctorRoomId).
    /// When null, drill-down should use <see cref="Key"/> as the dimension value.
    /// </summary>
    public int? Id { get; set; }

    public string Key { get; set; } = string.Empty;

    /// <summary>Sterilization cycles for load-level breakdowns; item line count for department/items.</summary>
    public int Loads { get; set; }

    public int Pcs { get; set; }
    public int Qty { get; set; }
}

public sealed class AnalyticsQaSummaryDto
{
    public int LeakPass { get; set; }
    public int LeakFail { get; set; }
    public int BowiePass { get; set; }
    public int BowieFail { get; set; }
    public int PendingApproval { get; set; }
}

public sealed class AnalyticsInstrumentSummaryDto
{
    public int TotalChecks { get; set; }
    public int WitnessPending { get; set; }
    public int WitnessApproved { get; set; }
}

public sealed class AnalyticsCountRowDto
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class AnalyticsOperatorSummaryRowDto
{
    public string OperatorName { get; set; } = string.Empty;
    public int Loads { get; set; }
    public int Pcs { get; set; }
    public int Qty { get; set; }
}

public sealed class AnalyticsDaySummaryRowDto
{
    public DateTime DayUtc { get; set; }
    public int Loads { get; set; }
    public int Pcs { get; set; }
    public int Qty { get; set; }
}

public sealed class AnalyticsSterilizerSummaryRowDto
{
    public int SterilizerId { get; set; }
    public string SterilizerNo { get; set; } = string.Empty;
    public int Loads { get; set; }
    public int Pcs { get; set; }
    public int Qty { get; set; }
}

