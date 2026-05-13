namespace HSMS.Shared.Contracts.Reporting;

/// <summary>
/// Idempotent print-log create payload. The API uses <see cref="CorrelationId"/> to enforce uniqueness, so
/// retries of the same physical job (network blip, app restart) never duplicate audit history.
/// </summary>
public sealed class PrintLogCreateDto
{
    public string ReportType { get; set; } = string.Empty;
    public int? SterilizationId { get; set; }
    public int? QaTestId { get; set; }
    public string? PrinterName { get; set; }
    public int Copies { get; set; } = 1;

    /// <summary>Free-form parameters dictionary serialized as JSON (e.g. date range, filters).</summary>
    public object? Parameters { get; set; }

    public Guid CorrelationId { get; set; }
    public string? ClientMachine { get; set; }
}

public sealed class PrintLogRowDto
{
    public long PrintLogId { get; set; }
    public DateTime PrintedAtUtc { get; set; }
    public int? PrintedByAccountId { get; set; }
    public string? PrintedByUsername { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public int? SterilizationId { get; set; }
    public string? CycleNo { get; set; }
    public int? QaTestId { get; set; }
    public string? PrinterName { get; set; }
    public int Copies { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed class PrintLogQueryDto
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? ReportType { get; set; }
    public int? PrintedByAccountId { get; set; }
    public string? UserSearch { get; set; }
    public int? SterilizationId { get; set; }
    public int? QaTestId { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 100;
}
