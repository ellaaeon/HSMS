namespace HSMS.Shared.Contracts;

public sealed class SterilizationQaDashboardQueryDto
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? SterilizerId { get; set; }
    public string? Department { get; set; }
}

public sealed class SterilizationQaDashboardDto
{
    public int Total { get; set; }
    public int Approved { get; set; }
    public int Failed { get; set; }
    public int PendingReview { get; set; }
    public int Archived { get; set; }

    public int LegacyQaTotal { get; set; }
    public int LegacyPendingApproval { get; set; }

    public int OverduePpm { get; set; }
    public int UpcomingMaintenance { get; set; }
    public int BiFailures { get; set; }

    public int? LastFailedSterilizerId { get; set; }
    public string? LastFailedSterilizerNo { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }

    public List<SterilizationQaTrendPointDto> ByDay { get; set; } = [];
    public List<SterilizationQaBreakdownPointDto> ByCategory { get; set; } = [];
    public List<SterilizationQaBreakdownPointDto> BySterilizer { get; set; } = [];

    public List<SterilizationQaAlertDto> Alerts { get; set; } = [];
}

public sealed class SterilizationQaTrendPointDto
{
    public DateTime DayUtc { get; set; }
    public int Total { get; set; }
    public int Approved { get; set; }
    public int Failed { get; set; }
    public int PendingReview { get; set; }
}

public sealed class SterilizationQaBreakdownPointDto
{
    public string Key { get; set; } = string.Empty;
    public int Value { get; set; }
}

public enum SterilizationQaAlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed class SterilizationQaAlertDto
{
    public SterilizationQaAlertSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty; // stable code for filtering
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int? SterilizerId { get; set; }
    public string? SterilizerNo { get; set; }
    public DateTime? EventAtUtc { get; set; }
}

