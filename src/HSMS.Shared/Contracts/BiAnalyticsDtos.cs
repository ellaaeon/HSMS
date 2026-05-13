namespace HSMS.Shared.Contracts;

public sealed class BiAnalyticsDto
{
    public int TotalBiCycles { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Pending { get; set; }
    public int MissingEntries { get; set; }

    /// <summary>Daily BI trend (UTC day based on Sterilization.CreatedAt.Date for consistency with analytics).</summary>
    public List<BiAnalyticsDayTrendRowDto> ByDay { get; set; } = [];

    public List<BiAnalyticsBreakdownRowDto> BySterilizer { get; set; } = [];
    public List<BiAnalyticsBreakdownRowDto> ByLotNumber { get; set; } = [];
    public List<BiAnalyticsBreakdownRowDto> ByResult { get; set; } = [];

    /// <summary>Completeness metric (how many cycles have key BI fields captured).</summary>
    public BiAnalyticsCompletenessDto Completeness { get; set; } = new();
}

public sealed class BiAnalyticsCompletenessDto
{
    public int CyclesInScope { get; set; }
    public int LotNoCaptured { get; set; }
    public int ResultCaptured { get; set; }
    public int TimeInCaptured { get; set; }
    public int TimeOutCaptured { get; set; }
}

public sealed class BiAnalyticsDayTrendRowDto
{
    public DateTime DayUtc { get; set; }
    public int Total { get; set; }
    public int Pass { get; set; }
    public int Fail { get; set; }
    public int Pending { get; set; }
    public int Missing { get; set; }
}

public sealed class BiAnalyticsBreakdownRowDto
{
    public int? Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Pass { get; set; }
    public int Fail { get; set; }
    public int Pending { get; set; }
    public int Missing { get; set; }
}

