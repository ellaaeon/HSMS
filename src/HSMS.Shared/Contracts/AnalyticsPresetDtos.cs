using System.Text.Json.Serialization;

namespace HSMS.Shared.Contracts;

public class AnalyticsPresetListItemDto
{
    public int PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AnalyticsPresetDto : AnalyticsPresetListItemDto
{
    public AnalyticsDashboardQueryDto Query { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnalyticsChartPreferencesDto? ChartPreferences { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnalyticsBreakdownsSelectionDto? Breakdowns { get; set; }
}

public sealed class AnalyticsPresetUpsertDto
{
    public string Name { get; set; } = string.Empty;
    public bool SetAsDefault { get; set; }
    public AnalyticsDashboardQueryDto Query { get; set; } = new();
    public AnalyticsChartPreferencesDto? ChartPreferences { get; set; }
    public AnalyticsBreakdownsSelectionDto? Breakdowns { get; set; }
}

public sealed class AnalyticsChartPreferencesDto
{
    public bool ShowCompareOverlay { get; set; }
    public int? MovingAverageWindowDays { get; set; }
    public bool ShowWeeklyTrend { get; set; }
    public bool ShowMonthlyTrend { get; set; }
    public bool DetectSpikes { get; set; }
}

/// <summary>
/// User-selected breakdown panels to show simultaneously. The UI can map these to available breakdown datasets.
/// </summary>
public sealed class AnalyticsBreakdownsSelectionDto
{
    public List<string> Panels { get; set; } = [];
}

