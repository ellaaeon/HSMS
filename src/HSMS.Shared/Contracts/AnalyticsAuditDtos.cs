namespace HSMS.Shared.Contracts;

public sealed class AnalyticsAuditEventDto
{
    public string Action { get; set; } = string.Empty;
    public string? Format { get; set; }
    public AnalyticsFilterDto? Filter { get; set; }
    public string? ReportType { get; set; }
    public string? ClientMachine { get; set; }
    public string? Notes { get; set; }
}

